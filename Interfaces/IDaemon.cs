

namespace GainFrameWork.Communication.Interfaces
{
    public interface IDaemon:IChannel 
    {
        bool IsClientConnected(int connectionId);
        string RemoteHost(int connectionId);
        void Disconnect(int connectionId);
        void Send(int connectionId, string textdata);
        int ConnectionCount { get; }
        event ConnectionHandler DaemonConnected;
        event ConnectionHandler ClientDisconnected;
    }
}
