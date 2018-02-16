using System;
using System.Collections.Generic;

namespace GainFrameWork.Communication
{
    public delegate void MessageReceivedHandler(object sender, MessageReceivedEventArgs e);
    public delegate void MultiMessageReceivedHandler(object sender, List<MessageReceivedEventArgs> e);
    public delegate void ClientMessageReceivedHandler(object sender, ClientMessageReceivedEventArgs e);
    public delegate void ClientMultiMessageReceivedHandler(object sender, List<ClientMessageReceivedEventArgs> e);


    public class MessageReceivedEventArgs : EventArgs
    {
        private byte[] _messagebytes;
        private string _messageText;
        
        public MessageReceivedEventArgs(byte[] data)
        {
            _messagebytes = data;
            _messageText = System.Text.Encoding.ASCII.GetString(data);
        }

        public MessageReceivedEventArgs(byte[] data, string textdata)
        {
            _messagebytes = data;
            _messageText = textdata;
        }

        public byte[] MessageBytes
        {
            get
            {
                return _messagebytes;
            }
        }
        public string MessageText
        {
            get { return _messageText; }
        }

        
    }

    public class ClientMessageReceivedEventArgs : EventArgs
    {
        private byte[] _messagebytes;
        private string _messageText;
        private int _connectionId;

        public ClientMessageReceivedEventArgs(byte[] data, int connectionId)
        {
            _messagebytes = data;
            _messageText = System.Text.Encoding.ASCII.GetString(data);
            _connectionId = connectionId;
        }

        public byte[] MessageBytes
        {
            get
            {
                return _messagebytes;
            }
        }
        public string MessageText
        {
            get { return _messageText; }
        }

        public int ConnectionId
        {
            get { return _connectionId; }
        }

    }
}
