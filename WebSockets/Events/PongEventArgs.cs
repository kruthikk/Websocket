using System;

namespace GainFrameWork.Communication.WebSockets.Events
{
    public class PongEventArgs : EventArgs
    {
        public byte[] Payload { get; private set; }

        public PongEventArgs(byte[] payload)
        {
            Payload = payload;
        }
    }
}
