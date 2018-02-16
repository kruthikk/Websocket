using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.IO;
using log4net;

namespace GainFrameWork.Communication.WebSockets.Http
{
    public class BadRequestService :IService 
    {
        private readonly Stream _stream;
        private readonly string _header;
        private readonly ILog _logger;

        public BadRequestService(Stream stream, string header, ILog logger)
        {
            _stream = stream;
            _header = header;
            _logger = logger;
        }

        public void Respond()
        {

            try
            {
                HttpHelper.WriteHttpHeader("HTTP/1.1 400 Bad Request", _stream);

                // limit what we log. Headers can be up to 16K in size
                string header = _header.Length > 255 ? _header.Substring(0, 255) + "..." : _header;
                _logger.WarnFormat("Bad request: '{0}'", header);
            }
            catch (IOException ioex)
            {
                _logger.Error("Error in Respond", ioex);
            }

            catch (ObjectDisposedException)
            {
                // do nothing. This will be thrown if the Listener has been stopped
            }
            catch (Exception ex)
            {
                _logger.Error("Error in Respond", ex);
                throw;
            }
        }

        public void Dispose()
        {
            // do nothing
        }
    }
}
