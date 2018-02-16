using System;
using GainFrameWork.Communication.WebSockets.Common;
using GainFrameWork.Communication.WebSockets.Exceptions;
using GainFrameWork.Communication.WebSockets.Http;
using System.IO;
using GainFrameWork.Communication.WebSockets.Events;
using log4net;
using System.Net.Sockets;
using System.Text.RegularExpressions;
using System.Net;
using GainFrameWork.Communication.Interfaces;
using System.Text;

namespace GainFrameWork.Communication.WebSockets
{
    
    public class WebSocketHandlerService : WebSocketBase, IService
    {
        private readonly string _header;
        private readonly ILog _logger;
        private readonly TcpClient _tcpClient;
        private bool _isDisposed = false;

        public string RemoteHost { get; set; }
        public int RemotePort { get; set; }
        public string LocalHost { get; set; }
        public int LocalPort { get; set; }
        public int ConnectionId { get; set; }
        private SocketHandler _socketHandler;
        private WebSocketFrameBuilder _frameBuilder = new WebSocketFrameBuilder();

        public IChannelParameters ChannelParameters { get; set; }

        public event EventHandler WebSocketHandShakeSent;
        public event EventHandler<ConnectionCloseEventArgs> BuilderConnectionClose;

        public WebSocketHandlerService(Stream stream, TcpClient tcpClient, string header, bool noDelay, ILog logger)
            : base(logger)
        {
            _stream = stream;
            _header = header;
            _logger = logger;
            _tcpClient = tcpClient;

            // send requests immediately if true (needed for small low latency packets but not a long stream). 
            // Basically, dont wait for the buffer to be full before before sending the packet
            tcpClient.NoDelay = noDelay;

            RemoteHost = ((IPEndPoint)tcpClient.Client.RemoteEndPoint).Address.ToString();
            RemotePort = ((IPEndPoint)tcpClient.Client.RemoteEndPoint).Port;
            LocalHost = ((IPEndPoint)tcpClient.Client.LocalEndPoint).Address.ToString();
            LocalPort = ((IPEndPoint)tcpClient.Client.LocalEndPoint).Port;

            _frameBuilder = new WebSocketFrameBuilder();
            _frameBuilder.OnClose += FrameBuilder_OnClose;
            _frameBuilder.OnError += FrameBuilder_OnError;
            _frameBuilder.OnPing += FrameBuilder_OnPing;
            _frameBuilder.OnPong += FrameBuilder_OnPong;
            _frameBuilder.OnTextFrame += FrameBuilder_OnTextFrame;

        }

        private void FrameBuilder_OnTextFrame(object sender, TextFrameEventArgs e)
        {
            base.OnTextFrame(e.Text);
        }

        private void FrameBuilder_OnPong(object sender, PingEventArgs e)
        {
            base.OnPong(e.Payload);
        }

        private void FrameBuilder_OnPing(object sender, PingEventArgs e)
        {
            base.OnPing(e.Payload);
        }

        private void FrameBuilder_OnError(object sender, ErrorEventArgs e)
        {
            _logger.ErrorFormat("Error in framebuilder {0} - {1}", e.ErrorCode, e.Description);
            _socketHandler.OnDisconnect(new ConnectionEventArgs(ConnectionId, e.ErrorCode, e.Description));
        }

        private void FrameBuilder_OnClose(object sender, ConnectionCloseEventArgs e)
        {
            _socketHandler.OnDisconnect(new ConnectionEventArgs(ConnectionId, (int)e.Code, e.Reason));
            if (BuilderConnectionClose != null)
                BuilderConnectionClose(sender, e);
        }

        public void Respond()
        {
            OpenBlocking(_stream, _tcpClient.Client);
        }

        protected override void OpenBlocking(Stream stream, Socket socket)
        {
            _socket = socket;
            _stream = stream;
            _writer = new WebSocketFrameWriter(stream);
            PerformHandshake(stream);
            _isOpen = true;
            OnConnectionOpened();

            if (_socket.Connected)
            {
                if (ChannelParameters.TimeOut > 0)
                {
                    _socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.SendTimeout, ChannelParameters.TimeOut);
                    _socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReceiveTimeout, ChannelParameters.TimeOut);
                }

                _socketHandler  = new SocketHandler(_socket, ChannelParameters.BufferSize);
                _socketHandler.Received += sh_received;
                _socketHandler.Disconnected += sh_disconnected;
                _socketHandler.ConnectionId = ConnectionId ;
                _frameBuilder.ConnectionId = ConnectionId;
                _socketHandler.Start();
                

            }
        }

        private void sh_received(object sender, DataEventArgs e)
        {
            var sh = sender as SocketHandler;
            /*if (sh != null)
                _logger.DebugFormat("Received data from connection {0} - {1}", sh.ConnectionId, e.Text );
            else
                _logger.InfoFormat("Received data from nonexisting client - {0}", e.Text);*/

            _frameBuilder.Store(e.Data, e.Data.Length);
        }

        private void sh_disconnected(object sender, ConnectionEventArgs e)
        {
            var sh = sender as SocketHandler;
            if (sh != null)
                _logger.InfoFormat("Connection closed for {0}", sh.ConnectionId);
            else
                _logger.Info("Client Connection closed for nonexisting client");

            base.OnConnectionClose(new byte[0]);
           
        }

        protected override void PerformHandshake(Stream stream)
        {
            string header = _header;

            try
            {
                Regex webSocketKeyRegex = new Regex("Sec-WebSocket-Key: (.*)");
                Regex webSocketVersionRegex = new Regex("Sec-WebSocket-Version: (.*)");

                // check the version. Support version 13 and above
                const int WebSocketVersion = 13;
                int secWebSocketVersion = Convert.ToInt32(webSocketVersionRegex.Match(header).Groups[1].Value.Trim());
                if (secWebSocketVersion < WebSocketVersion)
                {
                    throw new WebSocketVersionNotSupportedException(string.Format("WebSocket Version {0} not suported. Must be {1} or above", secWebSocketVersion, WebSocketVersion));
                }

                string secWebSocketKey = webSocketKeyRegex.Match(header).Groups[1].Value.Trim();
                string setWebSocketAccept = base.ComputeSocketAcceptString(secWebSocketKey);
                string response = ("HTTP/1.1 101 Switching Protocols" + Environment.NewLine
                                   + "Connection: Upgrade" + Environment.NewLine
                                   + "Upgrade: websocket" + Environment.NewLine
                                   + "Sec-WebSocket-Accept: " + setWebSocketAccept);

                HttpHelper.WriteHttpHeader(response, stream);
                if (WebSocketHandShakeSent != null)
                {
                    WebSocketHandShakeSent(this, null);
                }
                _logger.Info("Web Socket handshake sent");
            }
            catch (WebSocketVersionNotSupportedException ex)
            {
                string response = "HTTP/1.1 426 Upgrade Required" + Environment.NewLine + "Sec-WebSocket-Version: 13";
                HttpHelper.WriteHttpHeader(response, stream);
                throw;
            }
            catch (Exception ex)
            {
                HttpHelper.WriteHttpHeader("HTTP/1.1 400 Bad Request", stream);
                throw;
            }
        }

        public virtual void Dispose()
        {
            // send special web socket close message. Don't close the network stream, it will be disposed later
            try
            {
                if (!_isDisposed)
                {
                    _isDisposed = true;
                }
            }
            catch(Exception ex)
            {

            }

            try
            {
                if (_frameBuilder != null)
                {
                    _frameBuilder.Dispose();
                }
            }
            catch(Exception ex)
            {

            }
        }

       

       
        public override void Send(byte[] bytedata)
        {
            Send(WebSocketOpCode.BinaryFrame, bytedata, true);
        }

        protected override void Send(WebSocketOpCode opCode, byte[] toSend, bool isLastFrame)
        {
            byte[] buffer = _frameBuilder.GetFrameBytesFromData(opCode, toSend, isLastFrame);
            if (_socketHandler != null)
            {
                _socketHandler.Send(buffer);
            }
        }

        protected override void Send(WebSocketOpCode opCode, byte[] toSend)
        {
            Send(opCode, toSend, true);
        }


        public override void Send(string text)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(text);
            Send(WebSocketOpCode.TextFrame, bytes, true);
        }

        public void Disconnect()
        {
            if (_socketHandler !=null)
            {
                _socketHandler.OnDisconnect(new ConnectionEventArgs(ConnectionId, 0, "Disconnected by request"));
            }
        }
    }
}
