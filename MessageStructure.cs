using GainFrameWork.Communication.Interfaces;

namespace GainFrameWork.Communication
{
    public class MessageStructure
    {
        public MessageType MessageType { get; set; }
        public string Delimiter { get; set; }
        public byte[] DelimiterBytes { get; set; }
        public string FixedLength { get; set; }
        public object Header { get; set; }

        public MessageStructure(MessageType messagetype, string delimiter, byte[] delimiterbytes)
        {
            MessageType = messagetype;
            Delimiter = delimiter;
            DelimiterBytes = delimiterbytes;
        }
    }
}
