using System;
using System.IO;

namespace GainFrameWork.Communication
{
    public delegate void DataReceivedEventHandler(object sender, DataEventArgs e);

    public class DataEventArgs : EventArgs
    {
        private readonly MemoryStream stream;
        private string _messageText;
        private int _connectionId;

        #region Ctor
       
        public DataEventArgs(byte[] data)
        {
            stream = new MemoryStream(data);
            _messageText = System.Text.Encoding.ASCII.GetString(data);
        }
       
        public DataEventArgs(byte[] data, int connectionId) : this(data)
        {
            _connectionId = connectionId;
        }

        public DataEventArgs(string text,  int connectionId)
        {
            _connectionId = connectionId;
            _messageText = text;
            stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(text));
        }

        #endregion
       
        public byte[] Data
        {
            get { return stream.ToArray(); }
        }
        
        public Stream Stream
        {
            get { return stream; }
        }
        
        
        public int ConnectionId
        {
            get { return _connectionId; }
        }

        public string Text
        {
            get { return _messageText; }
        }

    }
}
