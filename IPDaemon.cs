using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using GainFrameWork.Communication.Interfaces;



namespace GainFrameWork.Communication
{
    public class IPDaemon : IDaemon, IDisposable  
    {
        private Socket _mainSocket;
        private bool enabled;
        private bool isConnected = false;
        private Dictionary<int, SocketHandler> m_workerSocketList = new Dictionary<int, SocketHandler>();
        private List<int> m_DisconnectedConnections = new List<int>();

        private ClientMessageBuilderCollection clientsmsgBuilderCollection = new ClientMessageBuilderCollection();

        private IChannelParameters _channelParameters;

        // The following variable will keep track of the cumulative total number of clients connected at any time. Since multiple threads
        // can access this variable, modifying this variable should be done
        // in a thread safe manner
        private int m_clientCount = 0;
        private int m_maxConnections = 100000;
        private int m_ConnectionsBackLog = 1000;

        public event DataReceivedEventHandler Received;
        public event ClientMessageReceivedHandler ClientMessageReceived;
        public event ConnectionHandler Connected;
        public event ConnectionHandler DaemonConnected;
        public event ConnectionHandler Disconnected;
        public event ConnectionHandler ClientDisconnected;
        public event ErrorEventHandler OnError;
        
        public int ConnectionCount {
            get 
            { 
                lock(m_workerSocketList)
                {
                    return m_workerSocketList.Count;
                }
            } 
        }

        public int MaxConnections
        {
            get { return m_maxConnections; }
            set 
            { 
                m_maxConnections = value; 
            }
        }

        public int ConnectionBackLog
        {
            get { return m_ConnectionsBackLog; }
            set { if (!IsConnected) m_ConnectionsBackLog = value; }
        }

        #region Ctor
        public IPDaemon(IChannelParameters channelparameters)
        {
            _channelParameters = channelparameters;
        }
        
        #endregion

        #region IChannel Members
        public bool IsConnected
        {
            get
            {
                return isConnected;
            }
        }

        public void Send(byte[] data)
        {
            lock (m_workerSocketList)
            {
                foreach (SocketHandler sh in m_workerSocketList.Values)
                {
                    if (sh != null)
                    {
                        sh.Send(data);
                    }
                }
            }

        }
        
        public void Send(int connectionId, string textdata)
        {
            Send(connectionId, System.Text.Encoding.ASCII.GetBytes(textdata));
        }

        public void Send(int connectionId, byte[] data)
        {
            SocketHandler sh = null;

            lock (m_workerSocketList)
            {
                if (m_workerSocketList.ContainsKey(connectionId))
                {
                    sh = m_workerSocketList[connectionId];
                }
            }
            
            if (sh != null)
            {
                sh.Send(data);
            }
            else
            {
                // remove from socket list --this should not happen so log it 
                DeleteClientConnection(connectionId);
                OnError(this, new ErrorEventArgs(-1, string.Format("Connection not available for Id: {0}", connectionId)));
            }
        }

        private void DeleteClientConnection(int connectionId)
        {
            lock (m_workerSocketList)
            {
                if (m_workerSocketList.ContainsKey(connectionId))
                {
                    m_workerSocketList.Remove(connectionId);
                    lock(m_DisconnectedConnections)
                    {
                        if (!m_DisconnectedConnections.Contains(connectionId))
                        {
                            m_DisconnectedConnections.Add(connectionId);
                        }
                    }
                }
            }
            clientsmsgBuilderCollection.Remove(connectionId);
        }

        public void Connect()
        {
            try
            {
                if (enabled)
                    return;

                _mainSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
                IPEndPoint ipLocal = new IPEndPoint(IPAddress.Any, _channelParameters.Port);
                _mainSocket.Blocking = false;
                // Bind to local IP Address...
                _mainSocket.Bind(ipLocal);
                // Start listening...set backlog to 10
                _mainSocket.Listen(m_ConnectionsBackLog);
                // Create the call back for any client connections...
                _mainSocket.BeginAccept(new AsyncCallback(OnClientConnect), null);

                isConnected = true;
                if (DaemonConnected != null)
                    DaemonConnected(this, new ConnectionEventArgs(0, 0, "OK"));

                enabled = true;
                
            }
            catch (Exception e)
            {
                if (OnError != null)
                {
                    OnError(this, new ErrorEventArgs(-1, e.Message));
                }

                throw e;
            }
            
        }

        public void Disconnect()
        {
            if (_mainSocket != null)
            {
                _mainSocket.Close();
            }
            lock (m_workerSocketList)
            {
                foreach (var socketHandler in m_workerSocketList.Values)
                {
                    if (socketHandler != null)
                    {
                        socketHandler.DisconnectSocket();
                    }
                }
            }
            isConnected = false;
            enabled = false;

            if (Disconnected != null)
                Disconnected(this, new ConnectionEventArgs(0, 0, "Stopped Listeneing"));
        }

        public bool Linger
        {
            get { return _channelParameters.Linger; }
            set { _channelParameters.Linger = value; }
        }

        public bool KeepAlive
        {
            get { return _channelParameters.KeepAlive; }
        }

        
        public string LocalHost
        {
            get { return _channelParameters.IP; }
        }

        public int LocalPort
        {
            get { return _channelParameters.Port; }
        }

        
        public void EOL(int ConnectionId, string sVal)
        {
            SetDelimiterMessageBuilder(ConnectionId, sVal, System.Text.Encoding.ASCII.GetBytes(sVal));
        }

        public void EOLB(int ConnectionId, byte[] sVal)
        {
            SetDelimiterMessageBuilder(ConnectionId, System.Text.Encoding.ASCII.GetString(sVal), sVal);
        }

       

        #endregion


        private void SetDelimiterMessageBuilder(int connectionId, string eol, byte[] eolb)
        {
            ClientMessageBuilder cmsg = clientsmsgBuilderCollection[connectionId];
            if (cmsg != null)
            {
                cmsg.MessageBuilder.MessageReceived -= _messageBuilder_MessageReceived;
                cmsg.MessageBuilder.MultiMessageReceived += _messageBuilder_MultiMessageReceived;
            }
            IMessageBuilder msgBuilder = new DelimiterMessageBuilder(connectionId, new MessageStructure(MessageType.DelimiterBased, eol, eolb));
            cmsg = new ClientMessageBuilder(eol, eolb, msgBuilder);
            cmsg.MessageBuilder.MessageReceived += _messageBuilder_MessageReceived;
            cmsg.MessageBuilder.MultiMessageReceived += _messageBuilder_MultiMessageReceived;
            clientsmsgBuilderCollection.Add(connectionId, cmsg);

        }

        private void _messageBuilder_MultiMessageReceived(object sender, List<MessageReceivedEventArgs> e)
        {
            if (ClientMessageReceived == null && Received == null) return;
            foreach (var message in e)
            {
                if (Received != null)
                {
                    // 11/22 - raising received event here.....
                    EventsHelper.Fire(Received, this, new DataEventArgs(message.MessageBytes, ((IMessageBuilder)sender).ConnectionId));
                }
                if (ClientMessageReceived != null)
                {
                    EventsHelper.Fire(ClientMessageReceived, this, new ClientMessageReceivedEventArgs(message.MessageBytes, ((IMessageBuilder)sender).ConnectionId));
                }
            }
        }

        private void _messageBuilder_MessageReceived(object sender, MessageReceivedEventArgs e)
        {
            if (Received != null)
            {
                // 11/22 - raising received event here.....
                EventsHelper.Fire(Received, this, new DataEventArgs(e.MessageBytes, ((IMessageBuilder)sender).ConnectionId));
            }
            if (ClientMessageReceived != null)
            {
                EventsHelper.Fire(ClientMessageReceived, this, new ClientMessageReceivedEventArgs(e.MessageBytes, ((IMessageBuilder)sender).ConnectionId));
            }
        
        }

        private void sh_received(object sender, DataEventArgs e)
        {
            // 11/22 Commenting this as we want to raise events only when client specified complete 
            // message is received .....
            //if (Received != null)
            //    Received(this, e);
            
            ClientMessageBuilder clientmsgbuilder = clientsmsgBuilderCollection[e.ConnectionId];
            if (clientmsgbuilder != null && clientmsgbuilder.MessageBuilder != null)
            {
                clientmsgbuilder.MessageBuilder.Store(e.Data, e.Data.Length);
            }
        }

        private void OnClientConnect(IAsyncResult asyn)
        {
            try
            {
                // Here we complete/end the BeginAccept() asynchronous call
                // by calling EndAccept() - which returns the reference to
                // a new Socket object
                Socket workerSocket = _mainSocket.EndAccept(asyn);

                int tmpConnId;
                lock (m_DisconnectedConnections)
                {
                    if (m_DisconnectedConnections.Count > 0)
                    {
                        // Assign old disconnecetd connection id to a new one
                        tmpConnId = m_DisconnectedConnections[0];
                        m_DisconnectedConnections.RemoveAt(0);
                    }
                    else
                    {
                        tmpConnId = Interlocked.Increment(ref m_clientCount);
                    }
                }
                if (workerSocket.Connected)
                {
                    if (_channelParameters.TimeOut > 0)
                    {
                        workerSocket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.SendTimeout, _channelParameters.TimeOut);
                    }
                    if (_channelParameters.TimeOut > 0)
                    {
                        workerSocket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReceiveTimeout, _channelParameters.TimeOut);
                    }
                    SocketHandler sh = new SocketHandler(workerSocket, _channelParameters.BufferSize);
                    sh.Received += sh_received;
                    sh.Disconnected += sh_disconnected;
                    sh.ConnectionId = tmpConnId;
                    // Add the workerSocket handler reference to our ArrayList
                    lock (m_workerSocketList)
                    {
                        m_workerSocketList.Add(sh.ConnectionId, sh);
                    }
                    //12/29 set the delimiter before receiving...
                    //11/22 - By default set message builder eol to carriage return...
                    EOL(sh.ConnectionId, string.Format("\r"));

                    sh.Start();
                    if (Connected != null)
                    {
                        EventsHelper.Fire(Connected, this, new ConnectionEventArgs(sh.ConnectionId, 0, "OK"));
                        //Connected(this, new ConnectionEventArgs(sh.ConnectionId, 0, "OK"));
                    }
                   
                }

                // Since the main Socket is now free, it can go back and wait for
                // other clients who are attempting to connect
                if (ConnectionCount < MaxConnections)
                {
                    _mainSocket.BeginAccept(new AsyncCallback(OnClientConnect), null);
                }
            }
            catch (SocketException se)
            {
                if (OnError != null)
                {
                    OnError(this, new ErrorEventArgs(se.ErrorCode, se.Message));
                }
            }
            catch (Exception e)
            {
                if (OnError != null)
                {
                    OnError(this, new ErrorEventArgs(-1, e.Message));
                }
                //throw e;
                
            }
           

        }

        private void sh_disconnected(object sender, ConnectionEventArgs e)
        {
            SocketHandler sh = sender as SocketHandler;
            if (sh != null)
            {
                DeleteClientConnection(sh.ConnectionId);
            }

            //if (OnError != null)
            //{
            //    OnError(this, new ErrorEventArgs(e.StatusCode, e.Description));
            //}
            if (ClientDisconnected != null)
                ClientDisconnected(this, e);
        }

        public bool IsClientConnected(object connectionHandler)
        {
            var sh = connectionHandler as SocketHandler;
            if (sh == null) return false;
            lock (m_workerSocketList)
            {
                return m_workerSocketList.ContainsKey(sh.ConnectionId);
            }
        }

        public bool IsClientConnected(int connectionId)
        {
            lock (m_workerSocketList)
            {
                return m_workerSocketList.ContainsKey(connectionId);
            }
        }

        public string RemoteHost(int connectionId)
        {
            lock (m_workerSocketList)
            {
                if (m_workerSocketList.ContainsKey(connectionId))
                {
                    var socketHandler = m_workerSocketList[connectionId];
                    return socketHandler.RemoteHost;
                }
            }
            return "";
        }

        public void Disconnect(int connectionId)
        {
            lock (m_workerSocketList)
            {
                if (m_workerSocketList.ContainsKey(connectionId))
                {
                    var socketHandler = m_workerSocketList[connectionId];
                    socketHandler.OnDisconnect(new ConnectionEventArgs(connectionId, 0, "Disconnected by request"));
                }
            }
        }

        public void Dispose()
        {
            lock (m_workerSocketList)
            {
                m_workerSocketList.Clear();
            }
            clientsmsgBuilderCollection.Dispose();
        }
    }

    internal class ClientMessageBuilderCollection:IDisposable
    {
        private Dictionary<int, ClientMessageBuilder> m_ClientsMsgBuilder = new Dictionary<int, ClientMessageBuilder>();
        public ClientMessageBuilderCollection()
        {
        }

        public void Add(int key, ClientMessageBuilder msgBuilder)
        {
            lock(m_ClientsMsgBuilder)
            {
                if(!m_ClientsMsgBuilder.ContainsKey(key))
                {
                    m_ClientsMsgBuilder.Add(key, msgBuilder);
                }
                else
                {
                    m_ClientsMsgBuilder[key] = msgBuilder;
                }
            }
        }

        public ClientMessageBuilder this[int index]
        {
            get 
            {
                lock (m_ClientsMsgBuilder)
                {
                    if (m_ClientsMsgBuilder.ContainsKey(index))
                        return m_ClientsMsgBuilder[index];
                }
                return null;
           }
        }

        public void Remove(int key)
        {
            lock (m_ClientsMsgBuilder)
            {
                if (m_ClientsMsgBuilder.ContainsKey(key))
                {
                    ClientMessageBuilder item = m_ClientsMsgBuilder[key];
                    m_ClientsMsgBuilder.Remove(key);
                    item.Dispose();
                }
            }
        }

        public void Dispose()
        {
            lock (m_ClientsMsgBuilder)
            {
                m_ClientsMsgBuilder.Clear();
            }
        }


    }

    internal class ClientMessageBuilder:IDisposable
    {
        private string m_EOL;
        private byte[] m_EOLB;
        private IMessageBuilder m_messageBuilder;

        public ClientMessageBuilder(string eol, byte[] eolb, IMessageBuilder messageBuilder)
        {
            m_EOL = eol;
            m_EOLB = eolb;
            m_messageBuilder = messageBuilder;
        }

        public string EOL { get { return m_EOL; } }
        public byte[] EOLB { get { return m_EOLB; } }
        public IMessageBuilder MessageBuilder { get { return m_messageBuilder; } }


        

        public void Dispose()
        {
            m_messageBuilder = null;
        }

        
    }
}
