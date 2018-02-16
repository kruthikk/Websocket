using System;

namespace GainFrameWork.Communication
{
    public delegate void ConnectionHandler(object sender, ConnectionEventArgs e);

    public class ConnectionEventArgs : EventArgs
    {
        private int _connectionId;
        private int _status;
        private string _description;

        
        public ConnectionEventArgs(int connectionId, int status, string description)
        {
            _connectionId = connectionId;
            _status = status;
            _description = description;
        }


        
        public int ConnectionId
        {
            get { return _connectionId; }
        }

        public int StatusCode
        {
            get { return _status; }
        }

        public string Description
        {
            get { return _description; }
        }
    }
}
