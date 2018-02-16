using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.IO;
using System.Reflection;
using System.Configuration;
using System.Xml;
using GainFrameWork.Communication.WebSockets.Common;
using log4net;

namespace GainFrameWork.Communication.WebSockets.Http
{
    public class HttpService : IService
    {
        private readonly Stream _stream;
        private readonly string _path;
        private readonly string _webRoot;
        private readonly ILog _logger;
        private readonly MimeTypes _mimeTypes;

        public HttpService(Stream stream, string path, string webRoot, ILog logger)
        {
            _stream = stream;
            _path = path;
            _webRoot = webRoot;
            _logger = logger;
            _mimeTypes = MimeTypesFactory.GetMimeTypes(webRoot);
        }

        private static bool IsDirectory(string file)
        {
            if (Directory.Exists(file))
            {
                //detect whether its a directory or file
                FileAttributes attr = File.GetAttributes(file);
                return ((attr & FileAttributes.Directory) == FileAttributes.Directory);
            }

            return false;
        }

        public void Respond()
        {
            try
            {
                _logger.InfoFormat("Request: {0}", _path);
                string file = GetSafePath(_path);

                // default to index.html is path is supplied
                if (IsDirectory(file))
                {
                    file += "index.html";
                }

                FileInfo fi = new FileInfo(file);

                if (fi.Exists)
                {
                    string ext = fi.Extension.ToLower();

                    string contentType;
                    if (_mimeTypes.TryGetValue(ext, out contentType))
                    {
                        Byte[] bytes = File.ReadAllBytes(fi.FullName);
                        RespondSuccess(contentType, bytes.Length);
                        _stream.Write(bytes, 0, bytes.Length);
                        _logger.InfoFormat("Served file: {0}", file);

                        // delete zip files once served
                        if (contentType == "application/zip")
                        {
                            //  File.Delete(fi.FullName);
                            // _logger.Information(this.GetType(), "Deleted file: {0}", file);
                        }
                    }
                    else
                    {
                        RespondMimeTypeFailure(file);
                    }
                }
                else
                {
                    RespondNotFoundFailure(file);
                }
            }
            catch (IOException ioex)
            {
                _logger.Error("Error in Respond", ioex);
            }

            catch (ObjectDisposedException)
            {
                // do nothing. This will be thrown if the Listener has been stopped
            }
            catch(Exception ex)
            {
                _logger.Error("Error in Respond", ex);
                throw;
            }
        }

        /// <summary>
        /// I am not convinced that this function is indeed safe from hacking file path tricks
        /// </summary>
        /// <param name="path">The relative path</param>
        /// <returns>The file system path</returns>
        private string GetSafePath(string path)
        {
            path = path.Trim().Replace("/", "\\");
            if (path.Contains("..") || !path.StartsWith("\\") || path.Contains(":"))
            {
                return string.Empty;
            }

            string file = _webRoot + path;
            return file;
        }

        public void RespondMimeTypeFailure(string file)
        {
            try
            {
                HttpHelper.WriteHttpHeader("415 Unsupported Media Type", _stream);
                _logger.WarnFormat("File extension not found MimeTypes.config: {0}", file);
            }
            catch (IOException ioex)
            {
                _logger.Error("Error in RespondMimeTypeFailure", ioex);
            }

            catch (ObjectDisposedException)
            {
                // do nothing. This will be thrown if the Listener has been stopped
            }
            catch (Exception ex)
            {
                _logger.Error("Error in RespondMimeTypeFailure", ex);
                throw;
            }
        }

        public void RespondNotFoundFailure(string file)
        {
            try
            {
                HttpHelper.WriteHttpHeader("HTTP/1.1 404 Not Found", _stream);
                _logger.InfoFormat("File not found: {0}", file);
            }
            catch (IOException ioex)
            {
                _logger.Error("Error in RespondNotFoundFailure", ioex);
            }

            catch (ObjectDisposedException)
            {
                // do nothing. This will be thrown if the Listener has been stopped
            }
            catch (Exception ex)
            {
                _logger.Error("Error in RespondNotFoundFailure", ex);
                throw;
            }
        }

        public void RespondSuccess(string contentType, int contentLength)
        {
            try
            {
                string response = "HTTP/1.1 200 OK" + Environment.NewLine +
                                  "Content-Type: " + contentType + Environment.NewLine +
                                  "Content-Length: " + contentLength + Environment.NewLine +
                                  "Connection: close";
                HttpHelper.WriteHttpHeader(response, _stream);
            }
            catch (IOException ioex)
            {
                _logger.Error("Error in RespondSuccess", ioex);
            }

            catch (ObjectDisposedException)
            {
                // do nothing. This will be thrown if the Listener has been stopped
            }
            catch (Exception ex)
            {
                _logger.Error("Error in RespondSuccess", ex);
                throw;
            }
        }

        public void Dispose()
        {
            // do nothing. The network stream will be closed by the WebServer
        }
    }
}
