using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using GainFrameWork.Communication.WebSockets.Common;
using System.IO;
using System.Reflection;
using GainFrameWork.Communication.WebSockets.Http;
using log4net;

namespace GainFrameWork.Communication.WebSockets
{
    public class ServiceFactoryDefault : IServiceFactory
    {
        private readonly ILog _logger;
        private readonly string _webRoot;

        private string GetWebRoot()
        {
            if (!string.IsNullOrWhiteSpace(_webRoot) && Directory.Exists(_webRoot))
            {
                return _webRoot;
            }

            return Path.GetDirectoryName(AppDomain.CurrentDomain.BaseDirectory).Replace(@"file:\", string.Empty);
        }

        public ServiceFactoryDefault(string webRoot, ILog logger)
        {
            _logger = logger;
            _webRoot = string.IsNullOrWhiteSpace(webRoot) ? GetWebRoot() : webRoot;
            if (!Directory.Exists(_webRoot))
            {
                _logger.WarnFormat("Web root not found: {0}", _webRoot);
            }
            else
            {
                _logger.InfoFormat("Web root: {0}", _webRoot);
            }
        }

        public IService CreateInstance(ConnectionDetails connectionDetails, bool isLowLevel=false)
        {
            switch (connectionDetails.ConnectionType)
            {
                case ConnectionType.WebSocket:
                    _logger.DebugFormat("creating WebSocketService {0}", connectionDetails.Path);
                    if(!isLowLevel)
                        return new WebSocketService(connectionDetails.Stream, connectionDetails.TcpClient, connectionDetails.Header, true, _logger);
                    else
                        return new WebSocketHandlerService(connectionDetails.Stream, connectionDetails.TcpClient, connectionDetails.Header, true, _logger);
                    break;
                case ConnectionType.Http:
                    // this path actually refers to the reletive location of some html file or image
                    return new HttpService(connectionDetails.Stream, connectionDetails.Path, _webRoot, _logger);
            }

            return new BadRequestService(connectionDetails.Stream, connectionDetails.Header, _logger);
        }


    }
}
