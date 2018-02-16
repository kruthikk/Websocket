using System;

namespace GainFrameWork.Communication.WebSockets.Events
{
    public class PingEventArgs : EventArgs
    {
        public byte[] Payload { get; private set; }

        public PingEventArgs(byte[] payload)
        {
            Payload = payload;
        }
    }
}
