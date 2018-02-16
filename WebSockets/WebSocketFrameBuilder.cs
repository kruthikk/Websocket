using System;
using GainFrameWork.Communication.Interfaces;
using System.IO;
using log4net;
using GainFrameWork.Communication.WebSockets.Common;
using GainFrameWork.Communication;
using GainFrameWork.Communication.WebSockets.Events;
using System.Text;
using System.Collections.Generic;

namespace GainFrameWork.Communication.WebSockets
{
    public class WebSocketFrameBuilder:IMessageBuilder,IDisposable 
    {
        private int _connectionId = 0;
        private object _syncObject = new object();
        private MemoryStream _messageStream = new MemoryStream();
        private FrmeBuilderState _frameBuilderState = FrmeBuilderState.GET_INFO;
        private object _syncFrameBuilderState = new object();
        private ILog _logger;
        bool _isFinBitSet;
        int _opCode;
        uint _payLoadLength;
        bool _fragmented=false;
        bool _compressed;
        bool _isMaskBitSet = false;
        const uint MaxPayLoad = 2147483648; // 2GB
        uint _totalPayloadLength=0;
        byte[] _mask;
        private int _messageLength=0;
        private List<byte[]> _fragmentsStream = new List<byte[]>();

        public WebSocketFrameBuilder()
        {
            _logger = LogManager.GetLogger(typeof(WebSocketFrameBuilder));
            lock (_syncFrameBuilderState)
            {
                _frameBuilderState = FrmeBuilderState.GET_INFO;
            }
            
        }

        public int ConnectionId
        {
            get { return _connectionId; }
            set{_connectionId=value; }
        }


        public void Store(byte[] bytes, int nBytes)
        {
            lock(_syncObject)
            {
                _messageStream.Position = _messageStream.Length;
                _messageStream.Write(bytes, 0, nBytes);
            }
            if (_logger.IsDebugEnabled)
                _logger.DebugFormat("Received data from {0} of length {1}", ConnectionId, nBytes);

            GetFrameMessage();
        }
        private FrmeBuilderState GetFrameBuilderState()
        {
            var frameBuilderState = FrmeBuilderState.GET_INFO;
            lock (_syncFrameBuilderState)
            {
                frameBuilderState = _frameBuilderState;
            }
            return frameBuilderState;
        }

        private void GetFrameMessage()
        {
            
            try
            {
                var frameBuilderState = GetFrameBuilderState();
                if (frameBuilderState == FrmeBuilderState.GET_INFO)
                {
                    GetInfo();
                }

                frameBuilderState = GetFrameBuilderState();
                if (frameBuilderState == FrmeBuilderState.GET_PAYLOAD_LENGTH_16)
                {
                    GetPayloadLength16();
                }
                
                frameBuilderState = GetFrameBuilderState();
                if (frameBuilderState == FrmeBuilderState.GET_PAYLOAD_LENGTH_64)
                {
                    GetPayloadLength64();
                }
                
                frameBuilderState = GetFrameBuilderState();
                if (frameBuilderState == FrmeBuilderState.GET_MASK)
                {
                    GetMask();
                }
                
                frameBuilderState = GetFrameBuilderState();
                if (frameBuilderState == FrmeBuilderState.GET_DATA)
                {
                    GetData();
                }
            }
            catch(Exception ex)
            {
                _logger.Error("Error in reading bytes", ex);
                RaiseError(new ErrorEventArgs(1011, "Error in reading bytes"));
            }
        }

        private void GetInfo () 
        {
            byte[] msgbuf = new byte[2];
            lock (_syncObject)
            {
                if (_messageStream==null || _messageStream.Length < 2)
                {
                    return;
                }
                _messageStream.Position = 0;
                _messageStream.Read(msgbuf, 0, 2);
            }

            
            if ((msgbuf[0] & 0x30) != 0x00) 
            {
                RaiseError(new ErrorEventArgs(1002, "RSV2 and RSV3 must be clear"));
                return;
            }

            var compressed = (msgbuf[0] & 0x40) == 0x40;

            if (compressed) 
            {
                RaiseError(new ErrorEventArgs(1002, "RSV1 must be clear and compressed data not supported"));
                return;
            }

            // process first byte
            byte finBitFlag = 0x80;
            byte opCodeFlag = 0x0F;
            _isFinBitSet = (msgbuf[0] & finBitFlag) == finBitFlag;
            _opCode =  msgbuf[0] & opCodeFlag;
                    

            //  process second byte
            byte byte2 = msgbuf[1];
            byte maskFlag = 0x80;
            _isMaskBitSet = (byte2 & maskFlag) == maskFlag;

            byte payloadLenFlag = 0x7F;
            _payLoadLength   = (uint)(byte2 & payloadLenFlag);


            if (_opCode == 0x00) 
            {
                if (compressed) 
                {
                    RaiseError(new ErrorEventArgs(1002, "RSV1 must be clear and compressed data not supported"));
                    return;
                }
                if (!_fragmented) 
                {
                    RaiseError(new ErrorEventArgs(1002, string .Format("invalid opcode {0}", _opCode)));
                    return;
                }
            } 
            else if (_opCode == 0x01 || _opCode == 0x02) 
            {
                if (_fragmented) 
                {
                    RaiseError(new ErrorEventArgs(1002, string.Format("invalid opcode {0}", _opCode)));
                    return;
                }
                _compressed = compressed;
            } 
            else if (_opCode > 0x07 && _opCode < 0x0b) 
            {
                if (!_isFinBitSet ) 
                {
                    RaiseError(new ErrorEventArgs(1002, "FIN must be set"));
                    return;
                }

                if (compressed) 
                {
                    RaiseError(new ErrorEventArgs(1002, "RSV1 must be clear"));
                    return;
                }

                if (_payLoadLength > 0x7d) 
                {
                    RaiseError(new ErrorEventArgs(1002, "invalid payload length"));
                    return;
                }
            } 
            else 
            {
                RaiseError(new ErrorEventArgs(1002,  string .Format("invalid opcode {0}", _opCode)));
                return;
            }

            if (!_isFinBitSet && !_fragmented) _fragmented = true;


            if (_payLoadLength == 126)
            {
                lock (_syncFrameBuilderState)
                {
                    _frameBuilderState = FrmeBuilderState.GET_PAYLOAD_LENGTH_16;
                }
            }
            else if (_payLoadLength == 127)
            {
                lock (_syncFrameBuilderState)
                {
                    _frameBuilderState = FrmeBuilderState.GET_PAYLOAD_LENGTH_64;
                }
            }
            else HaveLength();

            

        }

        

        private void GetPayloadLength16()
        {
            byte[] msgbuf = new byte[2];
            lock (_syncObject)
            {
                if (_messageStream.Length < 4)
                {
                    return;
                }
                _messageStream.Position = 2;
                _messageStream.Read(msgbuf, 0, 2);
            }
            
            Array.Reverse(msgbuf); // big endian
            
            _payLoadLength = BitConverter.ToUInt16(msgbuf, 0);
            HaveLength();
        }
        private void GetPayloadLength64()
        {
            byte[] msgbuf = new byte[8];
            lock (_syncObject)
            {
                if (_messageStream.Length < 10)
                {
                    return;
                }
                _messageStream.Position = 2;
                _messageStream.Read(msgbuf, 0, 8);
            }

            Array.Reverse(msgbuf); // big endian
            
            _payLoadLength= (uint)BitConverter.ToUInt64(msgbuf, 0);
            
            HaveLength();
 
        }

        private void HaveLength()
        {
            if (_opCode < 0x08 && MaxPayloadExceeded(_payLoadLength))
            {
                return;
            }

            lock (_syncFrameBuilderState)
            {
                if (_isMaskBitSet)
                    _frameBuilderState = FrmeBuilderState.GET_MASK;
                else
                    _frameBuilderState = FrmeBuilderState.GET_DATA;
            }
        }

        private bool  MaxPayloadExceeded (uint length) 
        {
            if (length == 0 || MaxPayLoad < 1) return false;

            uint fullLength = _totalPayloadLength + length;

            if (fullLength <= MaxPayLoad) 
            {
                _totalPayloadLength = fullLength;
                return false;
            }
            RaiseError(new ErrorEventArgs(1009,  "Max payload size exceeded"));
            return true;
        }

        private void GetMask()
        {
            byte[] msgbuf = new byte[4];
            int maskKeyPosition = GetMaskKeyPosition();
            lock (_syncObject)
            {
                if (_messageStream==null || _messageStream.Length < maskKeyPosition + 4)
                {
                    return;
                }
                _messageStream.Position = maskKeyPosition;
                _messageStream.Read(msgbuf, 0, 4);
            }

            _mask = msgbuf;
            lock (_syncFrameBuilderState)
            {
                _frameBuilderState = FrmeBuilderState.GET_DATA;
            }
        }

        private int GetMaskKeyPosition()
        {
            // 0 based index
            if (_payLoadLength < 126) return 2;
            if (_payLoadLength < 65536) return 4;
            return 10;
        }

        private int GetPayLoadDataPosition()
        {
            int maskKeyPosition = GetMaskKeyPosition();
            return _isMaskBitSet ? maskKeyPosition + 4 : maskKeyPosition ;
        }

        private void GetData()
        {
            byte[] msgbuf = new byte[0];
            int payLoadLength = (int)_payLoadLength;
            if (payLoadLength > 0)
            {
                
                msgbuf = new byte[payLoadLength];
                var dataPosition = GetPayLoadDataPosition();
                lock (_syncObject)
                {

                    if (_messageStream==null || _messageStream.Length < payLoadLength + dataPosition)
                    {
                        return;
                    }
                    _messageStream.Position = dataPosition;
                    _messageStream.Read(msgbuf, 0, payLoadLength);

                    // now remove data read from memory stream
                     MemoryStream leftovermessagestream = new MemoryStream();
                    if(_messageStream.Length > payLoadLength + dataPosition)
                    {
                        int leftoverBytesLength = (int)_messageStream.Length-(payLoadLength + dataPosition);
                        byte[] leftoverbytes = new byte[leftoverBytesLength];
                        _messageStream.Read(leftoverbytes, 0, leftoverBytesLength);

                        leftovermessagestream.Write(leftoverbytes, 0,leftoverBytesLength );
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

                if (_isMaskBitSet)
                {
                    const int maskKeyLen = 4;
                    // apply the mask key
                    for (int i = 0; i < msgbuf.Length; i++)
                    {
                        msgbuf[i] = (Byte)(msgbuf[i] ^ _mask[i % maskKeyLen]);
                    }
                }
            }

            if (_opCode > 0x07)
            {
                ControlMessage(msgbuf);
            }
            else if (_compressed)
            {
                lock (_syncFrameBuilderState)
                {
                    _frameBuilderState = FrmeBuilderState.INFLATING;
                }
                Decompress(msgbuf);
            }
            else if (PushFragment(msgbuf))
            {
                DataMessage();
            }
        }

        private void DataMessage()
        {
            if (_isFinBitSet) 
            {
                int messageLength = _messageLength;
                List<byte[]> fragments = new List<byte[]> (_fragmentsStream);
                               
                _totalPayloadLength = 0;
                _messageLength = 0;
                _fragmented = false;

                _fragmentsStream = new List<byte[]>();
      
                byte[] data = GetDataFromFragments(fragments, messageLength);
                if (_opCode == 2) 
                {
                    if(_isFinBitSet)
                    {
                        if(_logger.IsDebugEnabled)
                        {
                            _logger.DebugFormat("Received binary frame from connection {0} of length {1}", ConnectionId, data.Length);
                        }
                        if(OnBinaryFrame !=null)  OnBinaryFrame(this, new BinaryFrameEventArgs(data));
                    }
                    else
                    {
                        if (_logger.IsDebugEnabled)
                        {
                            _logger.DebugFormat("Received multi binary frame from connection {0} of length {1} isfinal: {2} ", ConnectionId, data.Length, _isFinBitSet);
                        }
                        if(OnBinaryMultiFrame !=null) OnBinaryMultiFrame(this, new BinaryMultiFrameEventArgs(data, _isFinBitSet));
                    }
                } 
                else 
                {
                    String strdata = Encoding.UTF8.GetString(data, 0, data.Length);
                    if(_isFinBitSet)
                    {
                        if (_logger.IsDebugEnabled)
                        {
                            _logger.DebugFormat("Received text frame from connection {0} of length {1}  ", ConnectionId, strdata.Length);
                        }
                        if(OnTextFrame !=null) OnTextFrame(this, new TextFrameEventArgs(strdata));
                    }
                    else
                    {
                        if (_logger.IsDebugEnabled)
                        {
                            _logger.DebugFormat("Received multi text frame from connection {0} of length {1} isfinal: {2} ", ConnectionId, strdata.Length, _isFinBitSet);
                        }
                        if( OnTextMultiFrame !=null) OnTextMultiFrame(this, new TextMultiFrameEventArgs(strdata, _isFinBitSet));
                    }
                    
                }
            }

            lock (_syncFrameBuilderState)
            {
                _frameBuilderState = FrmeBuilderState.GET_INFO;
            }
        }

        private byte[] GetDataFromFragments(List<byte[]> fragments, int messageLength)
        {
            if (fragments.Count == 1) return fragments[0];
            if (fragments.Count > 1)
            {
                return ConcatFragments(fragments, messageLength);
            }
            return new byte[0];
        }

        private byte[] ConcatFragments(List<byte[]> fragments, int messageLength)
        {
            byte[] target = new byte[messageLength];
            var offset = 0;

            for (var i = 0; i < fragments.Count; i++) {
                byte[] buf = fragments[i];
                buf.CopyTo(target, offset);
                offset += buf.Length;
            }
            return target;
        }

        private void FragmentStore(byte[] bytes)
        {
            _fragmentsStream.Add(bytes);
        }

        private bool PushFragment(byte[] fragment)
        {
            if (fragment.Length == 0) return true;
            int  totalLength = _messageLength + fragment.Length;
            if (MaxPayLoad  < 1 || totalLength <= MaxPayLoad) 
            {
                _messageLength = totalLength;
                FragmentStore(fragment);
                return true;  
            }       
            RaiseError(new ErrorEventArgs(1009, "max payload size exceeded"));
            return false;
        }

        private void Decompress(byte[] msgbuf)
        {
           //do nothing for now...compression not allowed
            _logger.Warn("Frame received as compressed data");
            lock (_syncFrameBuilderState)
            {
                _frameBuilderState = FrmeBuilderState.GET_INFO;
            }
        }

        private bool  IsValidErrorCode(ushort code) 
        {
            return (code >= 1000 && code <= 1013 && code != 1004 && code != 1005 && code != 1006) ||
                (code >= 3000 && code <= 4999);
            /*
              1000: 'normal',
              1001: 'going away',
              1002: 'protocol error',
              1003: 'unsupported data',
              1004: 'reserved',
              1005: 'reserved for extensions',
              1006: 'reserved for extensions',
              1007: 'inconsistent or invalid data',
              1008: 'policy violation',
              1009: 'message too big',
              1010: 'extension handshake missing',
              1011: 'an unexpected condition prevented the request from being fulfilled',
              1012: 'service restart',
              1013: 'try again later'
             */
        }
        private void ControlMessage(byte[] data)
        {
            if (_opCode == 0x08) 
            {
                ConnectionCloseEventArgs closeEventArgs;
                
                if (data.Length == 0) 
                {
                    closeEventArgs = new ConnectionCloseEventArgs(WebSocketCloseCode.Normal, "Close frame received - zero bytes received");
                   
                } 
                else if (data.Length == 1) 
                {
                    closeEventArgs = new ConnectionCloseEventArgs(WebSocketCloseCode.ProtocolError, "Invalid payload load");
                } 
                else 
                {
                    var code = BitConverter.ToUInt16(data, 0);
                    if (!IsValidErrorCode(code)) 
                    {
                        closeEventArgs =  new ConnectionCloseEventArgs(WebSocketCloseCode.ProtocolError, string.Format("invalid status code {0}", code));
                        
                    }
                    else
                    {
                        closeEventArgs = new ConnectionCloseEventArgs((WebSocketCloseCode)code, "Close frame received");
                    }
                }
                _logger.ErrorFormat("Received close code from connection {0} - {1}", ConnectionId, closeEventArgs.Reason);
                if (OnClose != null) OnClose(this, closeEventArgs);
                return;
            }   

            if (_opCode == 0x09) 
                if(OnPing !=null) OnPing(this, new PingEventArgs(data));
            else 
                if(OnPong !=null) OnPong(this, new PingEventArgs(data));


            lock (_syncFrameBuilderState)
            {
                _frameBuilderState = FrmeBuilderState.GET_INFO;
            }
        }

        private void RaiseError(ErrorEventArgs args)
        {
            if (OnError != null)
            {
                OnError(this, args);
            }
        }

       
        public event MessageReceivedHandler MessageReceived;  // not used
        public event MultiMessageReceivedHandler MultiMessageReceived; //not used

        public event ErrorEventHandler OnError;

        public event EventHandler<ConnectionCloseEventArgs> OnClose;
        public event EventHandler<PingEventArgs> OnPing;
        public event EventHandler<PingEventArgs> OnPong;

        public event EventHandler<TextFrameEventArgs> OnTextFrame;
        public event EventHandler<TextMultiFrameEventArgs> OnTextMultiFrame;
        public event EventHandler<BinaryFrameEventArgs> OnBinaryFrame;
        public event EventHandler<BinaryMultiFrameEventArgs> OnBinaryMultiFrame;

        

        public MessageStructure MessageStructure
        {
            get { return null; }
        }

        public void Dispose()
        {
            lock (_syncObject)
            {
                if (_messageStream != null)
                {
                    _messageStream.Close();
                    _messageStream = null;
                }
            }
        }

        public byte[] GetFrameBytesFromData(WebSocketOpCode opCode, byte[] payload, bool isLastFrame)
        {

            using (MemoryStream memoryStream = new MemoryStream())
            {
                byte finBitSetAsByte = isLastFrame ? (byte)0x80 : (byte)0x00;
                byte byte1 = (byte)(finBitSetAsByte | (byte)opCode);
                memoryStream.WriteByte(byte1);

                // NB, dont set the mask flag. No need to mask data from server to client
                // depending on the size of the length we want to write it as a byte, ushort or ulong
                if (payload.Length < 126)
                {
                    byte byte2 = (byte)payload.Length;
                    memoryStream.WriteByte(byte2);
                }
                else if (payload.Length <= ushort.MaxValue)
                {
                    byte byte2 = 126;
                    memoryStream.WriteByte(byte2);
                    BinaryReaderWriter.WriteUShort((ushort)payload.Length, memoryStream, false);
                }
                else
                {
                    byte byte2 = 127;
                    memoryStream.WriteByte(byte2);
                    BinaryReaderWriter.WriteULong((ulong)payload.Length, memoryStream, false);
                }

                memoryStream.Write(payload, 0, payload.Length);
                byte[] buffer = memoryStream.ToArray();
                return buffer;
            }
        }
    }

    internal enum FrmeBuilderState
    {
        GET_INFO = 0,
        GET_PAYLOAD_LENGTH_16 = 1,
        GET_PAYLOAD_LENGTH_64 = 2,
        GET_MASK = 3,
        GET_DATA = 4,
        INFLATING = 5
    }

}
