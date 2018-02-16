using System;
using System.Net;
using System.Net.Sockets;

namespace GainFrameWork.Communication
{
    internal class SocketHandler
    {
        private Socket socket;
        private byte[] _data;
        internal event DataReceivedEventHandler Received;
        public event ConnectionHandler Disconnected;
        public int ConnectionId { get; set; }
        public string RemoteHost { get; set; }
        public int RemotePort { get; set; }
        public string LocalHost { get; set; }
        public int LocalPort { get; set; }

        public SocketHandler(Socket socket, int bufferSize)
        {
            this.socket = socket;
            _data = new byte[bufferSize];
            RemoteHost = ((IPEndPoint)socket.RemoteEndPoint).Address.ToString();
            RemotePort = ((IPEndPoint) socket.RemoteEndPoint).Port;
            LocalHost = ((IPEndPoint)socket.LocalEndPoint).Address.ToString();
            LocalPort = ((IPEndPoint)socket.LocalEndPoint).Port;

        }

        
        public void Send(byte[] data)
        {
            try
            {
                socket.Send(data);
            }
            catch(SocketException se)
            {
                if (!(se.SocketErrorCode == SocketError.WouldBlock || se.SocketErrorCode == SocketError.IOPending || se.SocketErrorCode == SocketError.NoBufferSpaceAvailable))
                {
                    OnDisconnect(new ConnectionEventArgs(ConnectionId, se.ErrorCode, se.Message));
                }
                throw se;
            }
            catch (ObjectDisposedException e)
            {
                //Do nothing -- socket disposed......
            }
            catch (Exception ex)
            {
                OnDisconnect(new ConnectionEventArgs(ConnectionId, -1, ex.Message));
                throw ex;
            }
        }

        

        internal void OnDisconnect(ConnectionEventArgs e)
        {
            DisconnectSocket();
            if (Disconnected != null)
                Disconnected(this, e);
        
        }
        
        internal void DisconnectSocket()
        {
            try
            {
                //ensures that all data is sent and received on the connected socket before it is closed.
                socket.Shutdown(SocketShutdown.Both);
            }
            catch (ObjectDisposedException e)
            {
                //Do nothing -- socket disposed......
            }
            catch (Exception ex)
            {
                //TODO logging
                //Config.AppRefs.CommunicationLibrary.AppEvents.Communication.SocketHandlerError.Log("",ExtraMessageType.Append, ex.Message);
            }
            try
            {
                socket.Close(0);
            }
            catch (Exception ex)
            {
            }
            
        }
        
        public void Start()
        {
            try
            {
                //if (socket.Poll(100, SelectMode.SelectWrite) == false)
                //    throw new SocketException();

                
                if (socket.Available > _data.Length)
                {
                    _data = new byte[socket.Available];
                }

                socket.BeginReceive(_data, 0, _data.Length, SocketFlags.None, new AsyncCallback(StartReceiving), null);
            }
            catch (SocketException se)
            {
                if (!(se.ErrorCode == (int)SocketError.Disconnecting || se.ErrorCode == (int)SocketError.Shutdown))
                {
                    OnDisconnect(new ConnectionEventArgs(ConnectionId, se.ErrorCode, se.Message));
                }
            }
            catch (ObjectDisposedException e)
            {
                //Do nothing -- socket disposed......
            }
            catch (Exception ex)
            {
                //TODO logging
                //Config.AppRefs.CommunicationLibrary.AppEvents.Communication.SocketHandlerError.Log("",ExtraMessageType.Append,ex.Message);

                OnDisconnect(new ConnectionEventArgs(ConnectionId, -1, ex.Message));
            }
        }
        
        private void StartReceiving(IAsyncResult ar)
        {
            try
            {
                int total = socket.EndReceive(ar);
                
                if (total > 0)
                {
                    byte[] data = new byte[total];
                    Array.Copy(_data, data, total);
                    if (Received != null)
                        Received(this, new DataEventArgs(data, ConnectionId));

                    Start();
                }
                else
                {
                    //TODO logging
                    //Config.AppRefs.CommunicationLibrary.AppEvents.Communication.SocketHandlerError.Log("", ExtraMessageType.Append, string.Format("Zeror bytes received from Remote address: {0}:{1} Local address {2}:{3}", RemoteHost, RemotePort, LocalHost, LocalPort));
                    OnDisconnect(new ConnectionEventArgs(ConnectionId, 0, "Zero bytes received"));
                }
            }
            catch(ObjectDisposedException e)
            {
                //Config.AppRefs.CommunicationLibrary.AppEvents.Communication.SocketHandlerError.LogException(e);
                //Do nothing -- socket disposed......
            }
            catch(SocketException se)
            {
                if (!(se.SocketErrorCode == SocketError.WouldBlock || se.SocketErrorCode == SocketError.IOPending || se.SocketErrorCode == SocketError.NoBufferSpaceAvailable))
                {
                    OnDisconnect(new ConnectionEventArgs(ConnectionId, se.ErrorCode, se.Message));
                }
            }
            catch (Exception ex)
            {
                //TODO logging
                //Config.AppRefs.CommunicationLibrary.AppEvents.Communication.SocketHandlerError.Log("", ExtraMessageType.Append, ex.Message);
                OnDisconnect(new ConnectionEventArgs(ConnectionId, -1, ex.Message));
            }
        }
    }
}
