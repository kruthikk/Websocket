using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.IO;
using log4net;
using System.Threading;

namespace GainFrameWork.Communication.WebSockets.Http
{
    public class HttpHelper
    {
        public static string ReadHttpHeader(Stream stream, ILog log)
        {
            int length = 1024 * 16; // 16KB buffer more than enough for http header
            byte[] buffer = new byte[length];
            int offset = 0;
            int bytesRead = 0;
            do
            {
                if (offset >= length)
                {
                    throw new Exception("Http header message too large to fit in buffer (16KB)");
                }

                bytesRead = stream.Read(buffer, offset, length - offset);
                offset += bytesRead;
                string header = Encoding.UTF8.GetString(buffer, 0, offset);

                if(log!=null  && bytesRead > 1536)
                {
                    if (log.IsInfoEnabled)
                        log.InfoFormat("Read httpheader of size {0} ", bytesRead);
                }

                // as per http specification, all headers should end this this
                if (header.Contains("\r\n\r\n"))
                {
                    return header;
                }
            } while (bytesRead > 0);

            return string.Empty;
        }

        public static void WriteHttpHeader(string response, Stream stream)
        {
            response = response.Trim() + Environment.NewLine + Environment.NewLine;
            Byte[] bytes = Encoding.UTF8.GetBytes(response);
            stream.Write(bytes, 0, bytes.Length);
        }
    }
}
