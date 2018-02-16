using GainFrameWork.Communication.Interfaces;

namespace GainFrameWork.Communication
{
    public class TimedChannelParameters : ChannelParameters, ITimeChannelParameters
    {
        private int _sendTimeout = 0;
        private int _receiveTimeout = 0;

        public TimedChannelParameters(int sendTimeout, int receiveTimeout, int port, int buffersize, string ipaddress)
            : base(port, buffersize, ipaddress)
        {
            _sendTimeout = sendTimeout;
            _receiveTimeout = receiveTimeout;
        }

        public int SendTimeOut
        {
            get { return _sendTimeout; }
        }

        public int ReceiveTimeout
        {
            get { return _receiveTimeout; }
        }
    }
}
