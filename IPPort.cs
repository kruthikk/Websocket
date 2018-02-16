using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using GainFrameWork.Communication.Interfaces;

namespace GainFrameWork.Communication
{
    public class IPPort:IChannel,IDisposable
    {
        public event DataReceivedEventHandler Received;
        public event ConnectionHandler Connected;
        public event ConnectionHandler Disconnected;
        public event MessageReceivedHandler MessageReceived;
        public event ErrorEventHandler OnError;

        private IChannelParameters _channelParameters;
        private Socket _tcpSocket;
        
        
        private bool _isConnected;
        private object connectSync = new object();
        private readonly IPEndPoint _serverEndPoint;
        private IMessageBuilder _messageBuilder;
        private string _eol;
        private byte[] _eolb;

        // Signals the sendready/receiveready operation.
        private const Int32 ReceiveOperation = 1, SendOperation = 0;

        private static AutoResetEvent[] autoSendReceiveEvents = new AutoResetEvent[]
        {
            new AutoResetEvent(false),
            new AutoResetEvent(false)
        };
        private SocketAsyncEventArgs receiveArgs = new SocketAsyncEventArgs();
        
        #region Ctor
        public IPPort(IChannelParameters channelparamaters)
        {
            _channelParameters = channelparamaters;
            _serverEndPoint = new IPEndPoint(IPAddress.Parse(_channelParameters.IP), _channelParameters.Port);
            RemoteHost = _channelParameters.IP;
            RemotePort = _channelParameters.Port;
            LocalHost = "127.0.0.1";
            LocalPort = -1;
            EOL = "\r";
            EOLB = System.Text.Encoding.ASCII.GetBytes("\r");
            
        }
        #endregion

        #region IChannel members
        public bool IsConnected
        {
            get
            {
                lock (connectSync)
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
                // Prepare arguments for send/receive operation.
                SocketAsyncEventArgs completeArgs = new SocketAsyncEventArgs();
                completeArgs.SetBuffer(data, 0, data.Length);
                completeArgs.UserToken = _tcpSocket;
                completeArgs.RemoteEndPoint = _serverEndPoint;
                completeArgs.Completed += new EventHandler<SocketAsyncEventArgs>(OnSend);

                autoSendReceiveEvents[SendOperation].WaitOne();
                // Start sending asynchcronously.
                _tcpSocket.SendAsync(completeArgs);
            }
            else
            {
                OnError(this, new ErrorEventArgs((Int32)SocketError.NotConnected, string.Format("Connection not available for LocalAddress: {0}-{1} RemoteAddress: {2}-{3} ", LocalHost, LocalPort, RemoteHost, RemotePort)));
            }
        }
        
        public void Connect()
        {
            lock (connectSync)
            {
                if (_isConnected) return;
            }
            _tcpSocket = new Socket(_serverEndPoint.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
            if (_channelParameters.TimeOut > 0)
            {
                _tcpSocket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.SendTimeout, _channelParameters.TimeOut);
            }
            if (_channelParameters.TimeOut > 0)
            {
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

            receiveArgs = new SocketAsyncEventArgs();
            receiveArgs.UserToken = _tcpSocket;
            receiveArgs.RemoteEndPoint = _serverEndPoint;
            receiveArgs.Completed += new EventHandler<SocketAsyncEventArgs>(OnReceive);

            SocketAsyncEventArgs connectArgs = new SocketAsyncEventArgs();
            connectArgs.UserToken = _tcpSocket;
            connectArgs.RemoteEndPoint = _serverEndPoint;
            connectArgs.Completed += new EventHandler<SocketAsyncEventArgs>(OnConnect);
            _tcpSocket.ConnectAsync(connectArgs);

        }

        public void Disconnect()
        {
            try
            {

                if (_tcpSocket.Connected)
                {

                    try
                    {
                        _tcpSocket.Shutdown(SocketShutdown.Both);
                    }
                    catch (Exception)
                    {

                        //if already closed throws exception
                    }
                    finally
                    {
                        if (_tcpSocket.Connected)
                        {
                            _tcpSocket.Close();
                        }
                    }

                }
                lock (connectSync)
                {
                    if (_isConnected)
                    {
                        _isConnected = false;
                        if (Disconnected != null)
                        {
                            EventsHelper.Fire(Disconnected, this, new ConnectionEventArgs(0, 0, "Disconnected"));
                            //Disconnected(this, new ConnectionEventArgs(0, 0, "Disconnected"));
                        }
                    }
                }
            }
            catch(Exception e)
            {
                
            }
        }
        
        public bool Linger
        {
            get { return _channelParameters.Linger; }
        }

        public bool KeepAlive
        {
            get { return _channelParameters.KeepAlive; }
        }

        public string LocalHost { get; set; }
        public int LocalPort { get; set; }

        #endregion

        public string RemoteHost { get; set; }
        public int RemotePort { get; set; }

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
        {
            get { return _eolb; }
            set
            {
                _eolb = value;
                SetDelimiterMessageBuilder(System.Text.Encoding.ASCII.GetString(_eolb), _eolb);
            }
        }

        public int ConnectionId { get; set; }

        #region helper methods

        private void OnConnect(object sender, SocketAsyncEventArgs e)
        {
            // Set the flag for socket connected.
            if (e.SocketError == SocketError.Success)
            {
                lock (connectSync)
                {
                    _isConnected = (e.SocketError == SocketError.Success);
                }
                autoSendReceiveEvents[SendOperation].Set();
                autoSendReceiveEvents[ReceiveOperation].Set();
                
                LocalHost = ((IPEndPoint) _tcpSocket.LocalEndPoint).Address.ToString();
                LocalPort = ((IPEndPoint) _tcpSocket.LocalEndPoint).Port;
                if (Connected != null)
                {
                    EventsHelper.Fire(Connected, this, new ConnectionEventArgs(ConnectionId, (int)SocketError.Success, e.SocketError.ToString()));
                    //Connected(this, new ConnectionEventArgs(ConnectionId, (int) SocketError.Success, e.SocketError.ToString()));
                }
                StartReceiving(receiveArgs);
            }
            else
            {
                ProcessError(e);
            }
        }
        
        private void StartReceiving(SocketAsyncEventArgs e)
        {

            Socket s = e.UserToken as Socket;
            if (s == null)
            {
                //raise error
                throw new NullReferenceException("Invalid socket");
            }
            
            byte[] receiveBuffer = new byte[_channelParameters.BufferSize];
            e.SetBuffer(receiveBuffer, 0, receiveBuffer.Length);

            autoSendReceiveEvents[ReceiveOperation].WaitOne();
            s.ReceiveAsync(e);
        }

        private void OnReceive(object sender, SocketAsyncEventArgs e)
        {
            // Signals the end of receive.
            autoSendReceiveEvents[ReceiveOperation].Set();
            if (e.SocketError != SocketError.Success)
            {
                ProcessError(e);
            }
            else
            {
                if (e.BytesTransferred > 0)
                {

                    byte[] validdata = e.Buffer.Where(b => b != 0x00).ToArray();
                    //byte[] data = new byte[e.Buffer.Length];
                    //Buffer.BlockCopy(e.Buffer, e.Offset, data, 0, e.BytesTransferred);

                    _messageBuilder.Store(validdata, validdata.Length);

                    StartReceiving(receiveArgs);

                    if (Received != null)
                    {
                        EventsHelper.Fire(Received, this, new DataEventArgs(validdata, ConnectionId));
                        //Received(this, new DataEventArgs(data, ConnectionId));
                    }
                }
                else
                {
                    Disconnect();   
                }
            }
        }

        private void OnSend(object sender, SocketAsyncEventArgs e)
        {
            // Signals the end of send.
            autoSendReceiveEvents[SendOperation].Set();
            if (e.SocketError != SocketError.Success)
            {
                ProcessError(e);
            }
            e.Dispose();
            e = null;
        }

        private void ProcessError(SocketAsyncEventArgs e)
        {
            Socket s = e.UserToken as Socket;
            if (s == null) return;

 
            // Throw the SocketException
            SocketException ex = new SocketException((Int32)e.SocketError);

            if (!(ex.SocketErrorCode == SocketError.WouldBlock || ex.SocketErrorCode == SocketError.IOPending || ex.SocketErrorCode == SocketError.NoBufferSpaceAvailable))
            {
                Disconnect();
            }
            
            if (OnError !=null)
            {
                OnError(this, new ErrorEventArgs(ex.ErrorCode, ex.Message));
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

        private void _messageBuilder_MultiMessageReceived(object sender, List<MessageReceivedEventArgs> e)
        {
            if (MessageReceived == null) return;
            foreach (var message in e)
            {
                EventsHelper.Fire(MessageReceived, this, message);
                //MessageReceived(this, message);
            }
        }

        private void _messageBuilder_MessageReceived(object sender, MessageReceivedEventArgs e)
        {
            if (MessageReceived != null)
            {
                EventsHelper.Fire(MessageReceived, this, e);
                //MessageReceived(this, e);
            }
        }
        #endregion

        public void Dispose()
        {
            autoSendReceiveEvents[SendOperation].Close();
            autoSendReceiveEvents[ReceiveOperation].Close();
            
            if (_tcpSocket.Connected)
            {
                _tcpSocket.Close();
            }
        }
    }
}
