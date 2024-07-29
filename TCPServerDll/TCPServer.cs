using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;

namespace TCPServerDll
{
    /// <summary>
    /// About this file
    /// </summary>
    public static class TCPServerInfo
    {
        public const string Description = "Create and running a TCP Server.";
        public const string Developer = "Morteza";
        public const string EMail = "mortezadvlp@gmail.com";
    }

    /// <summary>
    /// Create and running a TCP Server
    /// </summary>
    public class TCPServer
    {
        protected static Dictionary<EndPoint, Socket> acceptedSockets = new Dictionary<EndPoint, Socket>();
        protected TcpListener server { get; set; }
        private int port { get; set; }
        /// <summary>
        /// Create a TCP Server than listens to the specific port.
        /// </summary>
        /// <param name="port"> The port number that server wants to listen it</param>
        public TCPServer(int port)
        {
            this.port = port;
            try
            {
                server = new TcpListener(IPAddress.Any, port);
            }
            catch (Exception e)
            {
                server = null;
            }
        }

        TCPSocketListener socketListener;
        private Thread serverThread { get; set; }
        private bool stopServer { get; set; }

        /// <summary>
        /// Start TCP Server
        /// </summary>
        /// <exception cref="TCPServerException"></exception>
        public void StartServer()
        {
            if (server != null)
            {
                acceptedSockets = new Dictionary<EndPoint, Socket>();
                server.Server.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                server.Start();
                stopServer = false;
                serverThread = new Thread(new ThreadStart(ServerThreadStart));
                serverThread.Start();

            }
            else
                throw new TCPServerException();
        }

        private void ServerThreadStart()
        {
            Socket clientSocket = null;
            while (!stopServer)
            {
                try
                {
                    if (!server.Pending())
                    {
                        Thread.Sleep(500);
                        continue; // skip to next iteration of loop
                    }
                    clientSocket = server.AcceptSocket();
                    socketListener = new TCPSocketListener(clientSocket);
                    if (!acceptedSockets.ContainsKey(clientSocket.RemoteEndPoint))
                        acceptedSockets.Add(clientSocket.RemoteEndPoint, clientSocket);
                    else
                    {
                        acceptedSockets[clientSocket.RemoteEndPoint].Shutdown(SocketShutdown.Both);
                        acceptedSockets[clientSocket.RemoteEndPoint].Close();

                        acceptedSockets[clientSocket.RemoteEndPoint] = clientSocket;
                    }

                    socketListener.OnPacketReceive += GetReceivedData;
                    socketListener.StartSocketListener();
                }
                catch (SocketException se)
                {
                    stopServer = true;
                }
            }

        }

        public delegate void DataEventHandler(object sender, DataEventArgs e);

        public event DataEventHandler OnPacketReceive;
        private void GetReceivedData(object sender, DataEventArgs args)
        {
            PacketReceive(args);
        }
        protected virtual void PacketReceive(DataEventArgs e)
        {
            OnPacketReceive?.Invoke(this, e);
        }

        /// <summary>
        /// Send text data as tcp packet via TCP server.
        /// </summary>
        /// <param name="ip">Destination IP</param>
        /// <param name="port">Destination Port</param>
        /// <param name="text">Data as text</param>
        public void SendData(string ip, int port, string text)
        {
            byte[] data = Encoding.Default.GetBytes(text);
            SendData(ip, port, data);
        }

        /// <summary>
        /// Send byte[] data as tcp packet via TCP server.
        /// </summary>
        /// <param name="ip">Destination IP</param>
        /// <param name="port">Destination Port</param>
        /// <param name="data">Data as byte array</param>
        public void SendData(string ip, int port, byte[] data)
        {
            EndPoint ep = new IPEndPoint(IPAddress.Parse(ip), port);
            try
            {
                (acceptedSockets.First(r => (r.Key as IPEndPoint).Address.ToString() == ip.ToString()).Value).SendTo(data, ep);
            }
            catch { }
        }

        /// <summary>
        /// Stop server and dispose all of accepted sockets.
        /// </summary>
        public void StopServer()
        {
            if (server != null)
            {
                stopServer = true;
                if (socketListener != null)
                    socketListener.StopReceive();
                server.Stop();

                // Wait for one second for the the thread to stop.
                serverThread.Join(1000);

                if (serverThread.IsAlive)
                {
                    serverThread.Abort();
                }
                serverThread = null;
                server = null;
                foreach (var item in acceptedSockets)
                {
                    try
                    {
                        item.Value.Shutdown(SocketShutdown.Both);
                        item.Value.Close();
                    }
                    catch { }
                }
                acceptedSockets.Clear();
            }
        }

        /// <summary>
        /// Return IpEndPoints of accepted sockets by server.
        /// </summary>
        /// <returns>List of IpEndPoints of accepted sockets</returns>
        public List<IPEndPoint> GetAcceptedSocketsEndPoint()
        {
            List<IPEndPoint> list = new List<IPEndPoint>();
            foreach (var item in acceptedSockets)
            {
                list.Add((IPEndPoint)item.Value.RemoteEndPoint);
            }
            return list;
        }


        private class TCPSocketListener
        {
            private Socket socket { get; set; }
            private EndPoint ep { get; set; }
            private Thread thread { get; set; }
            private bool stopReceive { get; set; }

            public TCPSocketListener(Socket socket)
            {
                this.socket = socket;
                this.ep = socket.RemoteEndPoint;
            }
            public TCPSocketListener(EndPoint ep)
            {
                this.ep = ep;
            }

            public void StartSocketListener()
            {
                stopReceive = false;
                thread = new Thread(new ThreadStart(ReceiveThread));
                thread.Start();


            }

            public void ReceiveThread()
            {
                byte[] bytes = null;
                while (!stopReceive)
                {
                    try
                    {
                        bytes = new byte[1024000];
                        int bytesRec = socket.Receive(bytes);
                        string data = Encoding.Default.GetString(bytes).TrimEnd('\0').TrimEnd();
                        if (data.Length == 0)
                        {
                            break;
                        }
                        AddPacket(data, (socket.RemoteEndPoint as IPEndPoint).Address.ToString());
                    }
                    catch (Exception ex)
                    {
                        break;
                    }
                }
            }

            public void StopReceive()
            {
                acceptedSockets.Remove(socket.RemoteEndPoint);
                stopReceive = true;
                thread.Abort();
                socket.Shutdown(SocketShutdown.Receive);
                socket.Close();
            }

            public delegate void DataEventHandler(object sender, DataEventArgs e);
            public event DataEventHandler OnPacketReceive;
            public void AddPacket(string data, string sourceIp)
            {
                DataEventArgs args = new DataEventArgs();
                args.data = data;
                args.sourceIp = sourceIp;
                PacketReceive(args);
            }
            protected virtual void PacketReceive(DataEventArgs e)
            {
                OnPacketReceive?.Invoke(this, e);
            }

        }

    }



    public class DataEventArgs : EventArgs
    {
        public string data { get; set; }
        public string sourceIp { get; set; }
    }

    public class TCPServerException : Exception
    {
        public override string Message
        {
            get
            {
                return "Error in creating listener.";
            }
        }
    }

}
