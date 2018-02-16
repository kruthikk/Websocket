using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using GainFrameWork.Communication.WebSockets.Common;
using System.Diagnostics;
using System.Security.Policy;
using GainFrameWork.Communication.WebSockets.Exceptions;
using GainFrameWork.Communication.WebSockets.Http;
using System.Threading;
using GainFrameWork.Communication.WebSockets.Events;
using System.Threading.Tasks;
using System.Security.Cryptography.X509Certificates;
using System.Security.Authentication;
using log4net;
using GainFrameWork.Communication.Interfaces;

namespace GainFrameWork.Communication.WebSockets
{
    public class WebSocketClient : WebSocketBase, IDisposable
    {
        private readonly bool _noDelay;
        private readonly ILog _logger;
        private TcpClient _tcpClient;
        private Stream _stream;
        private Uri _uri;
        private ManualResetEvent _conectionCloseWait;
        private SocketHandler _socketHandler;
        private WebSocketFrameBuilder _frameBuilder = new WebSocketFrameBuilder();
        private IChannelParameters _channelParameters;

        private static ILog _globalLogger;
        private const int SECURE_PORT_443 = 443;
        private int _connectionId;

        private object connectSync = new object();

        public int ConnectionId
        {
            get { return _connectionId; }
            set
            {
                _connectionId = value;
                _socketHandler.ConnectionId = value;
            }
        }

        public event EventHandler<ConnectionCloseEventArgs> BuilderConnectionClose;
        public event ErrorEventHandler OnError;

        public WebSocketClient(bool noDelay, ILog logger)
            : base(logger)
        {
            _noDelay = noDelay;
            if (logger == null) logger = LogManager.GetLogger(typeof(WebSocketClient));
            _logger = logger;
            _globalLogger = logger; // not such a big deal updating this because reference assignments are thread safe

            _conectionCloseWait = new ManualResetEvent(false);

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
        }

        private void FrameBuilder_OnClose(object sender, ConnectionCloseEventArgs e)
        {
            _socketHandler.OnDisconnect(new ConnectionEventArgs(ConnectionId, 0, "Disconnected by request"));
            if (BuilderConnectionClose != null)
                BuilderConnectionClose(sender, e);
        }

        public WebSocketClient(IChannelParameters channelParameters, bool noDelay, ILog logger): this(noDelay,logger)
        {
            _channelParameters = channelParameters;
        }

        // The following method is invoked by the RemoteCertificateValidationDelegate.
        // if you want to ignore certificate errors (for debugging) then return true;
        public static bool ValidateServerCertificate(object sender, X509Certificate certificate, X509Chain chain, SslPolicyErrors sslPolicyErrors)
        {
            if (sslPolicyErrors == SslPolicyErrors.None)
            {
                return true;
            }

            _globalLogger.Error(string.Format("Certificate error: {0}", sslPolicyErrors));

            // Do not allow this client to communicate with unauthenticated servers.
            return false;
        }

        private Stream GetStream(TcpClient tcpClient, bool isSecure, string host)
        {
            if (isSecure)
            {
                SslStream sslStream = new SslStream(tcpClient.GetStream(), false, new RemoteCertificateValidationCallback(ValidateServerCertificate), null);
                _logger.InfoFormat( "Attempting to secure connection...");

                // This will throw an AuthenticationException if the sertificate is not valid
                sslStream.AuthenticateAsClient(host);

                _logger.Info("Connection successfully secured.");
                return sslStream;
            }
            else
            {
                _logger.Info("Connection not secure");
                return tcpClient.GetStream();
            }
        }

        public virtual void OpenBlocking(Uri uri)
        {
            try
            {
                var isOpen = false;
                lock (connectSync)
                {
                    isOpen = _isOpen;
                }
                if (isOpen) return;

                
                string host = uri.Host;
                int port = uri.Port;
                _tcpClient = new TcpClient();
                _tcpClient.NoDelay = _noDelay;
                bool useSsl = uri.Scheme.ToLower() == "wss";

                IPAddress ipAddress;
                if (IPAddress.TryParse(host, out ipAddress))
                {
                    _tcpClient.Connect(ipAddress, port);
                }
                else
                {
                    _tcpClient.Connect(host, port);
                }

                if (_channelParameters == null)
                {
                    _channelParameters = new ChannelParameters(port, host);
                }

                _stream = GetStream(_tcpClient, useSsl, host);
                _uri = uri;

                //

                _socket = _tcpClient.Client;
                _writer = new WebSocketFrameWriter(_stream);

                PerformHandshake(_stream);
                lock (connectSync)
                {
                    _isOpen = true;

                }

                if (_socket.Connected)
                {
                    if (_channelParameters.TimeOut > 0)
                    {
                        _socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.SendTimeout, _channelParameters.TimeOut);
                        _socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReceiveTimeout, _channelParameters.TimeOut);
                    }

                    _socketHandler = new SocketHandler(_socket, _channelParameters.BufferSize);
                    _socketHandler.Received += sh_received;
                    _socketHandler.Disconnected += sh_disconnected;
                    _socketHandler.ConnectionId = ConnectionId;
                    _frameBuilder.ConnectionId = ConnectionId;
                    _socketHandler.Start();

                    OnConnectionOpened();
                }
                else
                {
                    lock (connectSync)
                    {
                        _isOpen  = false;
                    }
                    if (OnError != null)
                    {
                        OnError(this, new ErrorEventArgs(-1, "Cannot connect to remote host."));
                    }
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
            }
        
        }

        private void sh_received(object sender, DataEventArgs e)
        {
            var sh = sender as SocketHandler;
            if (sh != null)
                _logger.DebugFormat("Received data for connection {0} - {1}", sh.ConnectionId, e.Text);
            else
                _logger.InfoFormat("Received data from nonexisting client - {0}", e.Text);

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
            if (OnError != null)
            {
                OnError(this, new ErrorEventArgs(e.StatusCode, e.Description));
            }

        }

        protected override void PerformHandshake(Stream stream)
        {
            Uri uri = _uri;
            WebSocketFrameReader reader = new WebSocketFrameReader();
            Random rand = new Random();
            byte[] keyAsBytes = new byte[16];
            rand.NextBytes(keyAsBytes);
            string secWebSocketKey = Convert.ToBase64String(keyAsBytes);

            string handshakeHttpRequestTemplate = @"GET {0} HTTP/1.1{4}" +
                                                  "Host: {1}:{2}{4}" +
                                                  "Upgrade: websocket{4}" +
                                                  "Connection: Upgrade{4}" +
                                                  "Sec-WebSocket-Key: {3}{4}" +
                                                  "Sec-WebSocket-Version: 13{4}{4}";

            string handshakeHttpRequest = string.Format(handshakeHttpRequestTemplate, uri.PathAndQuery, uri.Host, uri.Port, secWebSocketKey, Environment.NewLine);
            byte[] httpRequest = Encoding.UTF8.GetBytes(handshakeHttpRequest);
            stream.Write(httpRequest, 0, httpRequest.Length);
            _logger.Info( "Handshake sent. Waiting for response.");

            // make sure we escape the accept string which could contain special regex characters
            string regexPattern = "Sec-WebSocket-Accept: (.*)";
            Regex regex = new Regex(regexPattern);

            string response = string.Empty;

            try
            {
                response = HttpHelper.ReadHttpHeader(stream, _logger);
            }
            catch (Exception ex)
            {
                throw new WebSocketHandshakeFailedException("Handshake unexpected failure", ex);
            }

            // check the accept string
            string expectedAcceptString = base.ComputeSocketAcceptString(secWebSocketKey);
            string actualAcceptString = regex.Match(response).Groups[1].Value.Trim();
            if (expectedAcceptString != actualAcceptString)
            {
                throw new WebSocketHandshakeFailedException(string.Format("Handshake failed because the accept string from the server '{0}' was not the expected string '{1}'", expectedAcceptString, actualAcceptString));
            }
            else
            {
                _logger.Info("Handshake response received. Connection upgraded to WebSocket protocol.");
            }
        }

        public virtual void Dispose()
        {
            var isOpen = false;
            lock (connectSync)
            {
                isOpen = _isOpen;
            }

            if (isOpen)
            {
                /*using (MemoryStream stream = new MemoryStream())
                {
                    // set the close reason to GoingAway
                    BinaryReaderWriter.WriteUShort((ushort)WebSocketCloseCode.GoingAway, stream, false);

                    // send close message to server to begin the close handshake
                    Send(WebSocketOpCode.ConnectionClose, stream.ToArray());
                    _logger.Info( "Sent websocket close message to server. Reason: GoingAway");
                }*/

                // this needs to run on a worker thread so that the read loop (in the base class) is not blocked
                WaitForServerCloseMessage();
            }
            
        }

        private void WaitForServerCloseMessage()
        {
            try
            {
                // as per the websocket spec, the server must close the connection, not the client. 
                // The client is free to close the connection after a timeout period if the server fails to do so
                //_conectionCloseWait.WaitOne(TimeSpan.FromSeconds(10));

                // this will only happen if the server has failed to reply with a close response
                var isOpen = false;
                lock (connectSync)
                {
                    isOpen = _isOpen;
                }

                if (isOpen)
                {
                    //_logger.Warn("Server failed to respond with a close response. Closing the connection from the client side.");

                    // wait for data to be sent before we close the stream and client

                    try
                    {
                        _tcpClient.Client.Shutdown(SocketShutdown.Both);
                    }
                    catch(Exception ex)
                    {

                    }
                    try
                    {
                        _stream.Close();
                    }
                    catch(Exception ex)
                    {

                    }
                    try
                    {
                        _tcpClient.Close();
                    }
                    catch(Exception ex)
                    {

                    }

                    if (_frameBuilder != null)
                    {
                        _frameBuilder.Dispose();
                    }
                }

                _logger.Info("Client: Connection closed");
            }
            catch
            {

            }
        }

        protected override void OnConnectionClose(byte[] payload)
        {
            // server has either responded to a client close request or closed the connection for its own reasons
            // the server will close the tcp connection so the client will not have to do it
            lock (connectSync)
            {
                _isOpen = false;
            }
            _conectionCloseWait.Set();
            base.OnConnectionClose(payload);
        }

        public override void Send(byte[] bytedata)
        {
            Send(WebSocketOpCode.BinaryFrame, bytedata, true);
        }

        public bool IsConnected
        {
            get
            {
                lock (connectSync)
                {
                    return _isOpen;
                }
            }
        }


        protected override void Send(WebSocketOpCode opCode, byte[] toSend, bool isLastFrame)
        {
            if (!IsConnected)
            {
                OnError(this, new ErrorEventArgs((Int32)SocketError.NotConnected, string.Format("Connection not available for  {0} {1} ", _channelParameters.IP, _channelParameters.Port)));
                return;
            }
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
    }
}
