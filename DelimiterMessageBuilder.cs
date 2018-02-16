using System;
using System.Collections.Generic;
using System.IO;
using GainFrameWork.Communication.Interfaces;

namespace GainFrameWork.Communication
{
    public class DelimiterMessageBuilder:IMessageBuilder
    {
        private object _syncObject = new object();
        private MemoryStream _messageStream = new MemoryStream();
        private int _connectionId = 0;

        public event MessageReceivedHandler MessageReceived;
        public event MultiMessageReceivedHandler MultiMessageReceived;

        public DelimiterMessageBuilder(MessageStructure messageStructure)
        {
            MessageStructure = messageStructure;
        }

        public DelimiterMessageBuilder(int connectionId, MessageStructure messageStructure)
        {
            MessageStructure = messageStructure;
            _connectionId = connectionId;
        }

        public int ConnectionId
        {
            get { return _connectionId; }
        }

        public void Store(byte[] bytes, int nBytes)
        {
            lock(_syncObject)
            {
                _messageStream.Position = _messageStream.Length;
                _messageStream.Write(bytes,0,nBytes);
            }

            List<MessageReceivedEventArgs> inmessages = GetMessageBytes();
            if (inmessages != null)
            {
                if (inmessages.Count > 0)
                {
                    if (inmessages.Count == 1)
                    {
                        OnMessageReceived(inmessages[0]);
                    }
                    else
                    {
                        OnMultiMessageReceived(inmessages);
                    }
                }
            }
        }

        private List<MessageReceivedEventArgs> GetMessageBytes()
        {
            List<MessageReceivedEventArgs> inmessages = new List<MessageReceivedEventArgs>();
            if (MessageStructure.Delimiter == null) return null;
            try
            {
                lock(_syncObject)
                {
                    _messageStream.Position = 0;
                    if(_messageStream.Length < MessageStructure.DelimiterBytes.Length)
                    {
                        return null;
                    }
                    byte[] msgbuf = new byte[_messageStream.Length];
                    _messageStream.Read(msgbuf, 0, (int)_messageStream.Length);
                    inmessages = GetSearchBytePattern(MessageStructure.DelimiterBytes, msgbuf);
                }
            }
            catch (Exception e)
            {
              System.Diagnostics.Debug.WriteLine(e.Message);
            }

            return inmessages;
        }

        private void OnMessageReceived(MessageReceivedEventArgs messageReceivedEventArgs)
        {
            try
            {
                if (MessageReceived != null)
                {
                    MessageReceived(this, messageReceivedEventArgs);
                }
            }
            catch (Exception)
            {
               lock(_syncObject)
               {
                   try
                   {
                       _messageStream.Close();
                   }
                   finally 
                   {
                       _messageStream = new MemoryStream();
                   }
                   
               }
                
            }
           
        }

        private void OnMultiMessageReceived(List<MessageReceivedEventArgs> messageReceivedEventArgs)
        {
            try
            {
                if (MultiMessageReceived != null)
                {
                    MultiMessageReceived(this, messageReceivedEventArgs);
                }
            }
            catch (Exception)
            {
                lock (_syncObject)
                {
                    try
                    {
                        _messageStream.Close();
                    }
                    finally
                    {
                        _messageStream = new MemoryStream();
                    }

                }

            }

        }
        public MessageStructure MessageStructure { get; private set; }

        private List<MessageReceivedEventArgs> GetSearchBytePattern(byte[] pattern, byte[] bytes)
        {
            List<MessageReceivedEventArgs> matchBytes = new List<MessageReceivedEventArgs>();
            int matches = 0;
            int lastpaternposition = -1;
            for (int i = 0; i < bytes.Length; i++)
            {
                if (pattern[0] == bytes[i] && bytes.Length - i >= pattern.Length)
                {
                    bool ismatch = true;
                    for (int j = 1; j < pattern.Length && ismatch == true; j++)
                    {
                        if (bytes[i + j] != pattern[j])
                            ismatch = false;
                    }
                    if (ismatch)
                    {
                        MemoryStream tmpms = new MemoryStream();
                        if (lastpaternposition == -1)
                        {
                            tmpms.Write(bytes, 0, i);

                        }
                        else
                        {
                            tmpms.Write(bytes, lastpaternposition + 1, ((i - 1) - lastpaternposition));
                        }
                        byte[] msgbuf = new byte[tmpms.Length];
                        tmpms.Position = 0;
                        tmpms.Read(msgbuf, 0, msgbuf.Length);

                        if (tmpms.Length > 0)
                        {
                            matchBytes.Add(new MessageReceivedEventArgs(msgbuf));
                        }
                        matches++;
                        i += pattern.Length - 1;
                        lastpaternposition = i;

                    }
                }
            }
            if (matches > 0)
            {
                MemoryStream leftovermessagestream = new MemoryStream();
                if (lastpaternposition + 1 < bytes.Length)
                {
                    // There are some left over bytes, and we need to store for later processing....
                    leftovermessagestream.Write(bytes, lastpaternposition + 1, bytes.Length - (lastpaternposition + 1));
                }
                try
                {
                    _messageStream.Close();
                }
                finally
                {
                }
                _messageStream = leftovermessagestream;
            }
            return matchBytes;
        }
        
    }
}
