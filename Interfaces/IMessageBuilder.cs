namespace GainFrameWork.Communication.Interfaces
{
    public interface IMessageBuilder
    {
        void Store(byte[] bytes, int nBytes);
        event MessageReceivedHandler MessageReceived;
        event MultiMessageReceivedHandler MultiMessageReceived;
        MessageStructure MessageStructure { get; }
        int ConnectionId { get; }
    }

    public enum MessageType
    {
        DelimiterBased = 0,
        FixedLengthBased = 1,
        HeaderBased = 2
    }
}
