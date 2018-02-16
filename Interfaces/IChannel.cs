using System;

namespace GainFrameWork.Communication.Interfaces
{
    public interface IChannel
    {
        
        event DataReceivedEventHandler Received;
        
        bool IsConnected { get; }
        
        void Send(byte[] data);

        event ConnectionHandler Connected;
        
        event ConnectionHandler Disconnected;

        event ErrorEventHandler OnError;
        
        void Connect();
        
        void Disconnect();
        
        bool Linger { get; }

        bool KeepAlive { get; }

        //string RemoteHost { get; }

        //int RemotePort { get; }

        string LocalHost { get; }

        int LocalPort { get; }

        //string EOL { get; set; }

        //byte[] EOLB { get; set; }

    }
}
