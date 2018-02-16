namespace GainFrameWork.Communication.Interfaces
{
    public interface IChannelParameters
    {
        int Port { get; set; }
        int BufferSize { get; set; }
        string IP { get; set; }
        int TimeOut { get; set; }
        bool KeepAlive { get; set; }
        bool Linger { get; set; }

    }

    
}
