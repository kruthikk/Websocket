
namespace GainFrameWork.Communication.WebSockets.Events
{
    public class BinaryMultiFrameEventArgs : BinaryFrameEventArgs
    {
        public bool IsLastFrame { get; private set; }

        public BinaryMultiFrameEventArgs(byte[] payload, bool isLastFrame)
            : base(payload)
        {
            IsLastFrame = isLastFrame;
        }
    }
}
