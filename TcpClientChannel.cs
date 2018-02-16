using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using GainFrameWork.Communication.Interfaces;

namespace GainFrameWork.Communication
{
    public class TcpClientChannel : IChannel
    {
        #region Private Members

        private Socket _tcpSocket;
        private readonly IChannelParameters _channelParameters;
        private readonly IPEndPoint _serverEndPoint;
        private bool _isConnected;
        private object connectSync = new object();
        private SocketHandler _sh;
        private string _eol;
        private byte[] _eolb;
        private int _connectionId;

        public string RemoteHost { get; set; }
        public int RemotePort { get; set; }
        public string LocalHost { get; set; }
        public int LocalPort { get; set; }

        public string UserTag { get; set; }

        public int ConnectionId 
        { 
            get { return _connectionId; } 
            set 
            { 
                _connectionId = value;
                _sh.ConnectionId = value;
            } 
        }
        
        public event ConnectionHandler Connected;
        public event ConnectionHandler Disconnected;
        public event DataReceivedEventHandler Received;
        public event MessageReceivedHandler MessageReceived;
        public event ErrorEventHandler OnError;

        private IMessageBuilder _messageBuilder;

        

        #endregion

        #region Ctor
        public TcpClientChannel(IChannelParameters channelparamaters)
        {
            _channelParameters = channelparamaters;
            _serverEndPoint = new IPEndPoint(IPAddress.Parse(_channelParameters.IP), _channelParameters.Port);
            RemoteHost = _channelParameters.IP;
            RemotePort = _channelParameters.Port;
            LocalHost = "";
            EOL = "\r";
            EOLB = System.Text.Encoding.ASCII.GetBytes("\r");
        }
               
        #endregion
        
        #region IChannel Members

        
        public bool IsConnected
        {
            get 
            {
                lock(connectSync)
                {
                    return _isConnected;
                } 
            }
        }

        public void Send(string textdata)
        {
            Send(System.Text.Encoding.ASCII.GetBytes(textdata));
        }

        public void Send(byte[] data)
        {
            if (IsConnected)
            {
                if (_sh != null)
                {
                    _sh.Send(data);
                }
            }
            else
            {
                OnError(this, new ErrorEventArgs((Int32)SocketError.NotConnected, string.Format("Connection not available for LocalAddress: {0}-{1} RemoteAddress: {2}-{3} ", LocalHost,LocalPort, RemoteHost, RemotePort)));
            }
        }
        
        public void Connect()
        {
            lock (connectSync)
            {
                if (_isConnected) return;
            }
            try
            {
                _tcpSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);

                //add send timeout and receive timeout
                if (_channelParameters.TimeOut > 0)
                {
                    _tcpSocket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.SendTimeout, _channelParameters.TimeOut);
                    _tcpSocket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReceiveTimeout, _channelParameters.TimeOut);
                }

                if (_channelParameters.KeepAlive)
                {
                    _tcpSocket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.KeepAlive, _channelParameters.KeepAlive);
                }
                if (_channelParameters.Linger)
                {
                    _tcpSocket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.Linger, _channelParameters.Linger);
                }
                _tcpSocket.Blocking = false;
                _tcpSocket.BeginConnect(_serverEndPoint, ConnectCallback, _tcpSocket);

            }
            catch(SocketException se)
            {
                lock (connectSync)
                {
                    _isConnected = false;
                }

                if (OnError != null)
                {
                    OnError(this, new ErrorEventArgs(se.ErrorCode, se.Message));
                }
            }
            catch (Exception ex)
            {
                lock(connectSync)
                {
                    _isConnected = false;
                }

                if (OnError != null)
                {
                    OnError(this, new ErrorEventArgs(-1, ex.Message));
                }
            }
            

        }
        
        public void Disconnect()
        {
            if (_sh != null)
            {
                _sh.OnDisconnect(new ConnectionEventArgs(ConnectionId, 0, "Disconnected by request"));
            }
        }

        private void OnDisconnect()
        {
            lock (connectSync)
            {
                _isConnected = false;
            }
            if (Disconnected != null)
            {
                EventsHelper.Fire(Disconnected, this, new ConnectionEventArgs(0, 0, "Disconnected"));
                //Disconnected(this, new ConnectionEventArgs(0, 0, "Disconnected"));
            }
        }
        
        public string EOL
        {
            get { return _eol; } 
            set
            {
                _eol = value;
                SetDelimiterMessageBuilder(_eol, System.Text.Encoding.ASCII.GetBytes(_eol));
            }
        }

        public byte[] EOLB 
        { get { return _eolb; }
          set
          {
              _eolb = value;
              SetDelimiterMessageBuilder(System.Text.Encoding.ASCII.GetString(_eolb), _eolb);
          }
        }

        public bool KeepAlive
        {
            get { return _channelParameters.KeepAlive; }
        }

        public bool Linger
        {
            get { return _channelParameters.Linger; }
        }

        #endregion
        
        private void ConnectCallback(IAsyncResult ar)
        {
            try
            {
                _tcpSocket.EndConnect(ar);


                if (_tcpSocket.Connected)
                {
                    lock (connectSync)
                    {
                        _isConnected = true;
                    }
                    _sh = new SocketHandler(_tcpSocket, _channelParameters.BufferSize);
                    _sh.Received += sh_received;
                    _sh.Disconnected += sh_disconnected;
                    _sh.Start();
                    LocalHost = ((IPEndPoint)_tcpSocket.LocalEndPoint).Address.ToString();
                    LocalPort = ((IPEndPoint)_tcpSocket.LocalEndPoint).Port;
                    if (Connected != null)
                    {
                        EventsHelper.Fire(Connected, this, new ConnectionEventArgs(0, 0, "OK"));
                        //Connected(this, new ConnectionEventArgs(0, 0, "OK"));
                    }
                }
                else
                {
                    lock (connectSync)
                    {
                        _isConnected = false;
                    }
                    if (OnError != null)
                    {
                        OnError(this, new ErrorEventArgs(-1, "Cannot connect to remote host."));
                    }
                }
            }
            catch(SocketException se)
            {
                if (OnError != null)
                {
                    OnError(this, new ErrorEventArgs(se.ErrorCode, se.Message));
                }
            }
            catch (Exception e)
            {
                if (OnError !=null)
                {
                    OnError(this, new ErrorEventArgs(-1, e.Message));
                }
            }
        }

        private void SetDelimiterMessageBuilder(string eol, byte[] eolb)
        {
            if (_messageBuilder != null)
            {
                _messageBuilder.MessageReceived -= _messageBuilder_MessageReceived;
                _messageBuilder.MultiMessageReceived += _messageBuilder_MultiMessageReceived;
            }
            _messageBuilder = new DelimiterMessageBuilder(new MessageStructure(MessageType.DelimiterBased, eol, eolb));
            _messageBuilder.MessageReceived += _messageBuilder_MessageReceived;
            _messageBuilder.MultiMessageReceived += _messageBuilder_MultiMessageReceived;

        }

        #region events
        
        private void sh_disconnected(object sender, ConnectionEventArgs e)
        {
            OnDisconnect();
            if (OnError !=null)
            {
                OnError(this, new ErrorEventArgs(e.StatusCode, e.Description));    
            }
        }
        
        private void sh_received(object sender, DataEventArgs e)
        {
            _messageBuilder.Store(e.Data, e.Data.Length);

            if (Received != null)
            {
                EventsHelper.Fire(Received, this, e);
                //Received(this, e);
            }
        }
        
        private void _messageBuilder_MultiMessageReceived(object sender, List<MessageReceivedEventArgs> e)
        {
            if (MessageReceived == null) return;
            foreach(var message in e)
            {
                EventsHelper.Fire(MessageReceived, this, message);
                //MessageReceived(this, message);
            }
        }

        private void _messageBuilder_MessageReceived(object sender, MessageReceivedEventArgs e)
        {
            if(MessageReceived !=null)
            {
                EventsHelper.Fire(MessageReceived, this, e);
                //MessageReceived(this, e);
            }
        }
        
        #endregion
    }
}
