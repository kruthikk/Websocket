using System;

namespace GainFrameWork.Communication.WebSockets
{
    public interface IService : IDisposable
    {
        /// <summary>
        /// Sends data back to the client. This is built using the IConnectionFactory
        /// </summary>
        void Respond();
    }
}
