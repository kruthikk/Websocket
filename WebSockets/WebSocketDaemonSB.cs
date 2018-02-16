using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using GainFrameWork.Communication.Interfaces;
using System.IO;
using System.Text.RegularExpressions;
using GainFrameWork.Communication.WebSockets.Http;
using GainFrameWork.Communication.WebSockets.Exceptions;
using System.Security.Cryptography.X509Certificates;
using System.Security.Cryptography;
using System.Net.Security;
using System.Security.Authentication;
using System.Text;
using System.Reflection;
using GainFrameWork.Communication.WebSockets.Common;
using log4net;

namespace GainFrameWork.Communication.WebSockets
{
    public class WebSocketDaemonSB:IDaemon,IDisposable  
    {
        private Dictionary<int, WebSocketHandlerService> m_workerSocketList = new Dictionary<int, WebSocketHandlerService>();
        private readonly IServiceFactory _serviceFactory;
        private ILog _logger;
        private TcpListener _mainSocket;
        private string _service = "/gain";
        private X509Certificate2 _sslCertificate;
        private bool isConnected = false;
        private bool _isDisposed = false;
        private bool enabled;
        private List<int> m_DisconnectedConnections = new List<int>();
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
        
        
        #region Ctor
        public WebSocketDaemonSB(IChannelParameters channelparameters,  X509Certificate2 sslCertificate, string serviceName="/gain"):this(channelparameters, serviceName)
        {
            _sslCertificate = sslCertificate;
           
        }

        public WebSocketDaemonSB(IChannelParameters channelparameters, string serviceName = "/gain")
        {
            _channelParameters = channelparameters;
            _service = serviceName;
            _logger = LogManager.GetLogger(typeof(WebSocketDaemonSB));
            string webRoot = "";
            if (string.IsNullOrEmpty(webRoot) || !Directory.Exists(webRoot))
            {
                string baseFolder = AppDomain.CurrentDomain.BaseDirectory;
                _logger.WarnFormat( "Webroot folder {0} not found. Using application base directory: {1}", webRoot, baseFolder);
                webRoot = baseFolder;
            }
            _serviceFactory = new ServiceFactoryDefault(webRoot, _logger);
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
                foreach (WebSocketHandlerService sh in m_workerSocketList.Values)
                {
                    if (sh != null)
                    {
                        try
                        {
                            sh.Send(data);
                        }
                        catch(Exception ex)
                        {
                            _logger.Error("Erorr in send byte data", ex);
                        }
                        
                    }
                }
            }

        }

        public void Send(string textData)
        {
            lock (m_workerSocketList)
            {
                foreach (WebSocketHandlerService sh in m_workerSocketList.Values)
                {
                    if (sh != null)
                    {
                        try
                        {
                            sh.Send(textData);
                        }
                        catch (Exception ex)
                        {
                            _logger.Error("Erorr in send byte data", ex);
                        }

                    }
                }
            }

        }
        
        public void Send(int connectionId, string textdata)
        {
            WebSocketHandlerService sh = null;

            lock (m_workerSocketList)
            {
                if (m_workerSocketList.ContainsKey(connectionId))
                {
                    sh = m_workerSocketList[connectionId];
                }
            }

            if (sh != null)
            {
                sh.Send(textdata);
            }
            else
            {
                // remove from socket list --this should not happen so log it 
                DeleteClientConnection(connectionId);
                OnError(this, new ErrorEventArgs(-1, string.Format("Connection not available for Id: {0}", connectionId)));
            }
        }

        public void Send(int connectionId, byte[] data)
        {
            WebSocketHandlerService sh = null;

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

        public int ConnectionCount
        {
            get
            {
                lock (m_workerSocketList)
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

        public void Connect()
        {
            try
            {
                if (enabled)
                    return;

                _mainSocket = new TcpListener(IPAddress.Any, _channelParameters.Port);
                _mainSocket.Start();
                _logger.InfoFormat("Web Server started listening on port {0}", _channelParameters.Port);

                StartAccept();

                isConnected = true;
                enabled = true;

                if (DaemonConnected != null)
                    DaemonConnected(this, new ConnectionEventArgs(0, 0, "OK"));

            }
            catch(SocketException se)
            {
                string message = string.Format("Error listening on port {0}. Make sure IIS or another application is not running and consuming your port.", _channelParameters.Port);
                if (OnError != null)
                {
                    OnError(this, new ErrorEventArgs(-1, se.Message));
                }
                throw new ServerListenerSocketException(message, se);
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

        private void StartAccept()
        {
            // this is a non-blocking operation. It will consume a worker thread from the threadpool
            _mainSocket.BeginAcceptTcpClient(new AsyncCallback(HandleAsyncConnection), null);
 
        }

        private void HandleAsyncConnection(IAsyncResult res)
        {
            try
            {
                if (_isDisposed)
                {
                    return;
                }

                // this worker thread stays alive until either of the following happens:
                // Client sends a close conection request OR
                // An unhandled exception is thrown OR
                // The server is disposed
                try
                {
                    //using (
                    TcpClient tcpClient = _mainSocket.EndAcceptTcpClient(res);
                    //)
                    {
                        if (ConnectionCount < MaxConnections)
                        {
                            StartAccept();
                        }
                        _logger.Info("WebSocketDaemonSB opened client connection");

                        // get a secure or insecure stream
                        Stream stream = GetStream(tcpClient);

                        // extract the connection details and use those details to build a connection
                        ConnectionDetails connectionDetails = GetConnectionDetails(stream, tcpClient);
                        //using (
                            IService service = _serviceFactory.CreateInstance(connectionDetails, true);
                          //  )
                        {
                            // respond to the http request.
                            // Take a look at the WebSocketConnection or HttpConnection classes

                            WebSocketHandlerService sh = service as WebSocketHandlerService;
                            if (sh != null)
                            {
                                sh.ChannelParameters = _channelParameters;
                                sh.WebSocketHandShakeSent += sh_WebSocketHandShakeSent;
                            }

                            service.Respond();
                                                        
                            _logger.DebugFormat("Service responded {0} ", connectionDetails.ConnectionType);

                            
                        }

                    }
                }
                catch (Exception ex)
                {
                    _logger.Error("Error in HandleAsyncConnection", ex);
                }
                
                
            }
            catch (ObjectDisposedException)
            {
                // do nothing. This will be thrown if the Listener has been stopped
            }
            catch (Exception ex)
            {
                _logger.Error("Error in HandleAsyncConnection", ex);
            }
            
        }

        private void sh_WebSocketHandShakeSent(object sender, EventArgs e)
        {
            WebSocketHandlerService sh = sender as WebSocketHandlerService;
            if (sh == null) return;
            
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

            sh.ConnectionId = tmpConnId;
            sh.ConnectionOpened += sh_ConnectionOpened;
            sh.TextFrame += sh_TextFrame;
            sh.TextMultiFrame += sh_TextMultiFrame;
            sh.ConnectionClose += sh_ConnectionClose;
            sh.BinaryFrame += sh_BinaryFrame;
            sh.BinaryMultiFrame += sh_BinaryMultiFrame;

            // Add the workerSocket handler reference to our ArrayList
            lock (m_workerSocketList)
            {
                m_workerSocketList.Add(sh.ConnectionId, sh);
            }

            _logger.DebugFormat("Connected for client {0} ", tmpConnId);
            if (Connected != null)
            {
                Connected(this, new ConnectionEventArgs(sh.ConnectionId, 0, "OK"));
            }
            
        }

        private void sh_ConnectionClose(object sender, Events.ConnectionCloseEventArgs e)
        {
            WebSocketHandlerService sh = sender as WebSocketHandlerService;
            if (sh != null)
            {
                DeleteClientConnection(sh.ConnectionId);
            }


            if (ClientDisconnected != null)
                ClientDisconnected(this, new ConnectionEventArgs(sh.ConnectionId, (int)e.Code, e.Reason));
        }

        private void sh_TextFrame(object sender, Events.TextFrameEventArgs e)
        {
            WebSocketHandlerService sh = sender as WebSocketHandlerService;
            if (sh != null)
            {
                EventsHelper.Fire(Received, this, new DataEventArgs(e.Text,sh.ConnectionId) );
            }
        }

        private void sh_ConnectionOpened(object sender, EventArgs e)
        {
            WebSocketHandlerService sh = sender as WebSocketHandlerService;
            if (sh != null)
            {
                EventsHelper.Fire(Connected, this, new ConnectionEventArgs(sh.ConnectionId, 0, "OK"));
            }
        }

        private void DeleteClientConnection(int connectionId)
        {
            lock (m_workerSocketList)
            {
                if (m_workerSocketList.ContainsKey(connectionId))
                {
                    WebSocketHandlerService ws = m_workerSocketList[connectionId];
                    m_workerSocketList.Remove(connectionId);
                    lock (m_DisconnectedConnections)
                    {
                        if (!m_DisconnectedConnections.Contains(connectionId))
                        {
                            m_DisconnectedConnections.Add(connectionId);
                        }
                    }
                    ws.Dispose();
                }
            }

        }

        void sh_TextMultiFrame(object sender, Events.TextMultiFrameEventArgs e)
        {
            WebSocketHandlerService sh = sender as WebSocketHandlerService;
            if (sh != null)
            {
                EventsHelper.Fire(Received, this, new DataEventArgs(e.Text, sh.ConnectionId));
            }
        }

        void sh_BinaryMultiFrame(object sender, Events.BinaryMultiFrameEventArgs e)
        {
            WebSocketHandlerService sh = sender as WebSocketHandlerService;
            if (sh != null)
            {
                EventsHelper.Fire(Received, this, new DataEventArgs(e.Payload, sh.ConnectionId));
            }
        }

        void sh_BinaryFrame(object sender, Events.BinaryFrameEventArgs e)
        {
            WebSocketHandlerService sh = sender as WebSocketHandlerService;
            if (sh != null)
            {
                EventsHelper.Fire(Received, this, new DataEventArgs(e.Payload, sh.ConnectionId));
            }
        }

        public void Disconnect()
        {
            Dispose();
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

        
        

       

        #endregion

        
        private Stream GetStream(TcpClient tcpClient)
        {
            Stream stream = tcpClient.GetStream();

            // we have no ssl certificate
            if (_sslCertificate == null)
            {
                _logger.Info( "Connection not secure");
                return stream;
            }

            try
            {
                SslStream sslStream = new SslStream(stream, false);
                _logger.Info("Attempting to secure connection...");
                sslStream.AuthenticateAsServer(_sslCertificate, false, SslProtocols.Tls, true);
                _logger.Info( "Connection successfully secured");
                return sslStream;
            }
            catch (AuthenticationException e)
            {
                // TODO: send 401 Unauthorized
                throw;
            }
        }

        private  ConnectionDetails GetConnectionDetails(Stream stream, TcpClient tcpClient)
        {
            // read the header and check that it is a GET request
            string header = HttpHelper.ReadHttpHeader(stream);
            Regex getRegex = new Regex(@"^GET(.*)HTTP\/1\.1", RegexOptions.IgnoreCase);

            Match getRegexMatch = getRegex.Match(header);
            if (getRegexMatch.Success)
            {
                // extract the path attribute from the first line of the header
                string path = getRegexMatch.Groups[1].Value.Trim();

                // check if this is a web socket upgrade request
                Regex webSocketUpgradeRegex = new Regex("Upgrade: websocket", RegexOptions.IgnoreCase);
                Match webSocketUpgradeRegexMatch = webSocketUpgradeRegex.Match(header);

                if (webSocketUpgradeRegexMatch.Success)
                {
                    return new ConnectionDetails(stream, tcpClient, path, ConnectionType.WebSocket, header);
                }
                else
                {
                    return new ConnectionDetails(stream, tcpClient, path, ConnectionType.Http, header);
                }
            }
            else
            {
                return new ConnectionDetails(stream, tcpClient, string.Empty, ConnectionType.Unknown, header);
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
                    socketHandler.Dispose();
                }
            }
        }

        public void Dispose()
        {
            
            if (!_isDisposed)
            {
                _isDisposed = true;

                // safely attempt to shut down the listener
                try
                {
                    if (_mainSocket != null)
                    {
                        if (_mainSocket.Server != null)
                        {
                            _mainSocket.Server.Close();
                        }

                        _mainSocket.Stop();
                    }
                }
                catch (Exception ex)
                {
                    _logger.Error("Error in disposing WebSocketDaemonSB", ex);
                }

                CloseAllConnections();
                _logger.InfoFormat("WebSocketDaemonSB {0}:{1} disposed", _channelParameters.IP, _channelParameters.Port );
            }
            
        }

        private void CloseAllConnections()
        {
            List<WebSocketHandlerService> openConnections;

            lock (m_workerSocketList)
            {
                openConnections = new List<WebSocketHandlerService>(m_workerSocketList.Values);
                m_workerSocketList.Clear();
            }

            // safely attempt to close each connection
            foreach (WebSocketHandlerService openConnection in openConnections)
            {
                try
                {
                    openConnection.Dispose();
                }
                catch (Exception ex)
                {
                    _logger.Error("Error disposing client connection", ex);
                }
            }
        }

       

    }

    

    
}
