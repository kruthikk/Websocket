using System;
using System.Net;
using GainFrameWork.Communication.Interfaces;

namespace GainFrameWork.Communication
{
    public class ChannelParameters : IChannelParameters
    {
        private int _port;
        private string _ipaddress;
        private int _bufferSize = 4096;
        private bool _keepAlive = false;
        private bool _linger = true;
        private int _timeout = 0;

        public ChannelParameters(int port, string ipaddress)
        {
            _port = port;
            IPAddress tmpaddress;

            if(IPAddress.TryParse(ipaddress, out tmpaddress))
            {
                _ipaddress = ipaddress;
            }
            else
            {
                //check if it is hostname
                try
                {
                    IPAddress[] ipAddresses = Dns.GetHostAddresses(ipaddress);
                    if(ipAddresses.Length >0)
                    {
                    //take the last address
                    _ipaddress = ipAddresses[ipAddresses.Length - 1].ToString();
                    }
                }
                catch (Exception ex)
                {
                   
                    throw ex;
                }
            }
            

        }
        
        public int TimeOut {get { return _timeout; } set { _timeout = value; }  }

        public bool KeepAlive {get { return _keepAlive; } set { _keepAlive = value; } }

        public bool Linger { get { return _linger; } set { _linger = value;} }
        
        public int BufferSize
        {
            get { return _bufferSize; } 
            set { _bufferSize = value; }
        }
        

        public string IP
        {
            get { return _ipaddress; }
            set { _ipaddress = value; }
        }

        public int Port
        {
            get { return _port; }
            set { _port = value; }
        }
    }
}
