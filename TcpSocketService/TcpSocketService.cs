#region Licence
/*
 * TcpSocketService
 *
 * Copyright (C) Marcin Badurowicz 2013
 *
 *
 * Permission is hereby granted, free of charge, to any person obtaining
 * a copy of this software and associated documentation files
 * (the "Software"), to deal in the Software without restriction,
 * including without limitation the rights to use, copy, modify, merge,
 * publish, distribute, sublicense, and/or sell copies of the Software,
 * and to permit persons to whom the Software is furnished to do so,
 * subject to the following conditions:
 * 
 * The above copyright notice and this permission notice shall be
 * included in all copies or substantial portions of the Software.
 * 
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND,
 * EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES
 * OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
 * NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS
 * BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN
 * ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN
 * CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
 * SOFTWARE. 
 */
#endregion

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Windows.Networking.Sockets;
using Windows.Storage.Streams;
using Windows.System.Threading;

namespace Ktos.SocketService
{
    /// <summary>
    /// A list of possible modes for service - client or server
    /// </summary>
    public enum SocketServiceMode
    {
        /// <summary>
        /// Designates mode as server
        /// </summary>
        SERVER = 0,

        /// <summary>
        /// Designates client mode
        /// </summary>
        CLIENT = 1
    }

    /// <summary>
    /// A helper class which represents client
    /// </summary>
    public class ServiceClient : IDisposable
    {
        /// <summary>
        /// Client GUID by which is identified by the server
        /// </summary>
        public string Id { get; private set; }

        /// <summary>
        /// A reference to client socket
        /// </summary>
        public StreamSocket Socket { get; private set; }

        /// <summary>
        /// Client's own DataWriter
        /// </summary>
        public DataWriter Writer { get; set; }

        /// <summary>
        /// Creates a new TcpCleint
        /// </summary>
        /// <param name="id">An id, should be GUID</param>
        /// <param name="socket">A client socket got by accepted connection</param>
        public ServiceClient(string id, StreamSocket socket)
        {
            this.Id = id;
            this.Socket = socket;
        }

        /// <summary>
        /// Generates random GUID
        /// </summary>
        /// <returns>A generated GUID</returns>
        public static string GetRandomId()
        {
            return Guid.NewGuid().ToString();
        }

        /// <summary>
        /// Disposes the object and it's all subelements
        /// </summary>
        public void Dispose()
        {
            this.Socket.Dispose();
            this.Socket = null;

            this.Writer.Dispose();
            this.Writer = null;

            this.Id = null;
        }
    }

    /// <summary>
    /// TcpSocketService is a base class for Windows 8 sockets-based applications. 
    /// Based on Windows.Networking.Sockets and Windows.Storage.Streams is common
    /// for Windows 8 and Windows Phone 8 and provides a TCP client and server
    /// implementation for exchanging messages.
    /// 
    /// Every message is a byte array preceded by 4 bytes (uint32) of message's
    /// length.
    /// 
    /// Based on Sockets, it could be easily modified to use e.g. Bluetooth 
    /// instead of TCP.
    /// 
    /// Server is single-client oriented at the moment.
    /// 
    /// Supports creating derived classes for specific implementation, like
    /// text chat.
    /// </summary>
    public class TcpSocketService
    {
        /// <summary>
        /// Fake client GUID
        /// </summary>
        protected const string CLIENTGUID = "00000000-0000-0000-0000-000000000000";        

        /// <summary>
        /// Server socket
        /// </summary>
        protected StreamSocketListener socketListener;

        /// <summary>
        /// List of connected clients
        /// </summary>
        protected List<ServiceClient> clients;

        /// <summary>
        /// Selected operating mode (client or server)
        /// </summary>
        protected SocketServiceMode operationMode;

        /// <summary>
        /// Creating a service
        /// </summary>
        /// <param name="mode">Operation mode - client or server</param>
        public TcpSocketService(SocketServiceMode mode)
        {
            this.operationMode = mode;
            clients = new List<ServiceClient>();
        }

        /// <summary>
        /// Server initialization - starting listening on a specified port. IP address to bind to will be found automatically 
        /// without including APIPA addresses, but using IPv6 if possible.
        /// </summary>
        /// <param name="servicePort">Port number to bind to</param>
        public void InitializeServer(string servicePort)
        {
            this.InitializeServer(servicePort, false, true);
        }


        /// <summary>
        /// Server initialization - starting listening on a specified port. IP address to bind to will be found automatically, 
        /// (excluding APIPA and IPv6, if specified)
        /// </summary>
        /// <param name="servicePort">Port number to bind to</param>
        /// <param name="useApipa">Allows using APIPA addresses (169.254.0.0/16)</param>
        /// <param name="useIpv6">Allows using IPv6-family addresses</param>
        public void InitializeServer(string servicePort, bool useApipa, bool useIpv6)
        {
            this.InitializeServer(servicePort, findAddress(useApipa, useIpv6));
        }

        /// <summary>
        /// Server initialization - starting listening on a specified port and address.
        /// </summary>
        /// <param name="servicePort">Port number to bind to</param>
        /// <param name="serviceAddress">Address to bind to</param>
        public async void InitializeServer(string servicePort, string serviceAddress)
        {
            if (operationMode != SocketServiceMode.SERVER)
                throw new SocketServiceException("Mode not set properly.");

            try
            {
                string listen = serviceAddress;

                if (listen != null)
                {

                    // start listening
                    socketListener = new StreamSocketListener();
                    socketListener.ConnectionReceived += OnConnectionReceived;

                    await socketListener.BindServiceNameAsync(servicePort);

                    // say you are listening
                    if (Listening != null)
                        Listening.Invoke(this, new ListeningEventArgs(listen));
                }
                else
                {
                    throw new SocketServiceException("Cannot bind to address");
                }
            }
            catch (Exception e)
            {
                throw new SocketServiceException("Cannot start server.", e);
            }
        }

        /// <summary>
        /// Automatic finding to what adddress bind to
        /// </summary>
        /// <param name="useApipa">Allows using APIPA addresses (169.254.0.0/16)</param>
        /// <param name="useIpv6">Allows using IPv6-family addresses</param>
        /// <returns>IP address possible to bind to, or null</returns>
        protected static string findAddress(bool useApipa, bool useIpv6)
        {
            string listen = null;

            // finding an IP address to bind to
            foreach (var item in Windows.Networking.Connectivity.NetworkInformation.GetHostNames())
            {
                if (item.IPInformation != null)
                {
                    if ((item.Type == Windows.Networking.HostNameType.Ipv6) && !useIpv6)
                        continue;

                    if (item.DisplayName.StartsWith("169.254") && !useApipa)
                        continue;

                    listen = item.DisplayName;
                    break;
                }
            }

            return listen;
        }

        /// <summary>
        /// An event performed when server started listening
        /// </summary>
        public virtual event ListeningEventHandler Listening;

        /// <summary>
        /// Event handler when server starts listening
        /// </summary>
        /// <param name="sender">Sender class reference</param>
        /// <param name="e">Event arguments - address bound to</param>
        public delegate void ListeningEventHandler(object sender, ListeningEventArgs e);

        /// <summary>
        /// When server connection is received, start communication
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="args"></param>
        protected virtual void OnConnectionReceived(
            StreamSocketListener sender, StreamSocketListenerConnectionReceivedEventArgs args)
        {
            try
            {
                // add client
                var cid = ServiceClient.GetRandomId();
                var c = new ServiceClient(cid, args.Socket);
                clients.Add(c);

                // send some information about client
                if (ClientConnected != null)
                    ClientConnected.Invoke(this, new ClientConnectedEventArgs(c.Socket.Information, c.Id));

                // start the communication loop
                CommunicationLoop(cid);
            }
            catch (Exception e)
            {
                throw new SocketServiceException("Inner exception caused communication break.", e);
            }
        }

        /// <summary>
        /// Get client from list of client, with specified GUID
        /// </summary>
        /// <param name="id">GUID to look for</param>
        /// <returns>Whole reference to the client</returns>
        public ServiceClient GetClient(string id)
        {
            try
            {
                return clients.First(c => c.Id == id);
            }
            catch (InvalidOperationException)
            {
                return null;
            }
        }

        /// <summary>
        /// Notification about client connected, sends information about the client
        /// </summary>
        public virtual event ClientConnectedEventHandler ClientConnected;
        public delegate void ClientConnectedEventHandler(object sender, ClientConnectedEventArgs e);

        /// <summary>
        /// Client initialization - to some host and port
        /// </summary>
        /// <param name="host">Host name to connect to</param>
        /// <param name="port">Port number to connect to</param>
        public virtual async void InitializeClient(string host, string port)
        {
            if (operationMode != SocketServiceMode.CLIENT)
                throw new SocketServiceException("Mode not set properly.");

            // try to connect
            var clientSocket = new StreamSocket();
            var cid = TcpSocketService.CLIENTGUID;
            clients.Add(new ServiceClient(cid, clientSocket));
            try
            {
                // if succeeded, notify about it
                await clientSocket.ConnectAsync(new Windows.Networking.HostName(host), port);
                if (Connected != null)
                    Connected.Invoke(this);

                // and start communication loop
                CommunicationLoop(cid);
            }
            catch (Exception e)
            {
                throw new SocketServiceException("Client connection failed.", e);
            }
        }

        /// <summary>
        /// Notifies about connecting to the sever
        /// </summary>
        public virtual event ConnectedEventHandler Connected;
        public delegate void ConnectedEventHandler(object sender);

        /// <summary>
        /// Communication loop with the server (or client - it was the same thing)
        /// </summary>
        protected virtual async void CommunicationLoop(string clientId)
        {
            try
            {
                // get a client to start communication loop
                var c = GetClient(clientId);

                if (c == null)
                    throw new IndexOutOfRangeException("Client not found");

                var reader = new DataReader(c.Socket.InputStream);

                // input stream is ready as soon as possible
                reader.InputStreamOptions = InputStreamOptions.Partial;
                c.Writer = new DataWriter(c.Socket.OutputStream);

                // while client is not disconnected
                bool remoteDisconnection = false;
                while (!remoteDisconnection)
                {
                    // try to get about 4 KB of data (may be less)
                    var data = await reader.LoadAsync(4096);

                    // if there is no data - disconnected
                    if (data == 0)
                    {
                        remoteDisconnection = true;
                        break;
                    }

                    // put them to buffer, and send event
                    var b = new byte[data];
                    reader.ReadBytes(b);

                    if (DataReceived != null)
                        DataReceived.Invoke(this, new DataReceivedEventArgs(b));
                }

                // when disconnected - detach, send event and remove client
                reader.DetachStream();

                if (Disconnected != null)
                    Disconnected.Invoke(this, new DisconnectedEventArgs(clientId));

                // removing client
                clients.Remove(c);
                c.Dispose();
                c = null;
            }
            catch (SocketServiceException)
            {
                throw;
            }
            catch (Exception e)
            {
                switch (e.HResult)
                {
                    case -2147014842: // exception with -2147014842 is thrown when client is disconnecting
                        {
                            Disconnect(clientId);
                            break;
                        }

                    case -2147023901: // exception with -2147023901 is thrown when disconnect is done from our side, so we're ignoring it here
                        {
                            break;
                        }

                    default:
                        {
                            throw new SocketServiceException("Inner exception caused communication break.", e);
                        }

                };
            }
        }

        /// <summary>
        /// Executed when new message arrives
        /// </summary>
        public virtual event DataReceivedEventHandler DataReceived;
        public delegate void DataReceivedEventHandler(object sender, DataReceivedEventArgs e);

        /// <summary>
        /// Executed when client (or server) disconnects
        /// </summary>
        public virtual event DisconnectedEventHandler Disconnected;
        public delegate void DisconnectedEventHandler(object sender, DisconnectedEventArgs e);

        /// <summary>
        /// Sends the message to the client of specified Id
        /// </summary>
        /// <param name="message">Message, will be automatically added length</param>
        /// <param name="clientId">Client Id to send data to</param>
        public virtual async void Send(byte[] message, string clientId)
        {
            try
            {
                var c = GetClient(clientId);
                if (c != null)
                {
                    //writer.WriteUInt32((uint)message.Length);
                    c.Writer.WriteBytes(message);

                    await c.Writer.StoreAsync();
                }
                else
                {
                    throw new SocketServiceException("Not connected to server or client.");
                }
            }
            catch (Exception ex)
            {
                throw new SocketServiceException("Inner exception caused communication break.", ex);
            }
        }

        /// <summary>
        /// Sends message to every client
        /// </summary>
        /// <param name="message"></param>
        public void Send(byte[] message)
        {
            for (int i = 0; i < clients.Count; i++)
            {
                this.Send(message, clients[i].Id);
            }
        }

        /// <summary>
        /// Disconnects from server (or client)
        /// </summary>
        public virtual void Disconnect(string clientId)
        {
            if (socketListener != null)
            {
                socketListener.Dispose();
                socketListener = null;
            }

            var c = this.GetClient(clientId);
            if (c != null)
            {
                // removing client
                clients.Remove(c);
                c.Dispose();
                c = null;

                if (Disconnected != null)
                    Disconnected.Invoke(this, new DisconnectedEventArgs(clientId));
            }
        }

        /// <summary>
        /// Disconnects from server
        /// </summary>
        public void Disconnect()
        {
            if (operationMode != SocketServiceMode.CLIENT)
                throw new SocketServiceException("Invalid operation mode");

            this.Disconnect(TcpSocketService.CLIENTGUID);
        }
    }

    /// <summary>
    /// Event arguments when data from client (or server) are received
    /// </summary>
    public class DataReceivedEventArgs : EventArgs
    {
        /// <summary>
        /// Received message (without length)
        /// </summary>
        public byte[] Data { get { return this.data; } }
        private byte[] data;

        public DataReceivedEventArgs(byte[] data)
            : base()
        {
            this.data = data;
        }
    }

    /// <summary>
    /// Event arguments when listening started
    /// </summary>
    public class ListeningEventArgs : EventArgs
    {
        /// <summary>
        /// Host we're bind to
        /// </summary>
        public string Host { get { return this.host; } }
        private string host;

        public ListeningEventArgs(string host)
            : base()
        {
            this.host = host;
        }
    }

    /// <summary>
    /// Event args when client connects
    /// </summary>
    public class ClientConnectedEventArgs : EventArgs
    {
        /// <summary>
        /// Information about connected client
        /// </summary>
        public StreamSocketInformation ClientInformation { get { return this.clientInformation; } }
        private StreamSocketInformation clientInformation;

        /// <summary>
        /// GUID of connected client
        /// </summary>
        public string ClientId { get; private set; }

        public ClientConnectedEventArgs(StreamSocketInformation clientInformation, string clientId)
            : base()
        {
            this.clientInformation = clientInformation;
            this.ClientId = clientId;
        }
    }

    /// <summary>
    /// Event args when client is disconnected
    /// </summary>
    public class DisconnectedEventArgs : EventArgs
    {
        /// <summary>
        /// Exception, if disconnection is because of exception
        /// </summary>
        public Exception Error { get; private set; }

        /// <summary>
        /// Client ID, which was disconnected
        /// </summary>
        public string Id { get; private set; }

        public DisconnectedEventArgs(Exception ex)
            : base()
        {
            this.Error = ex;
        }

        public DisconnectedEventArgs(string id)
            : base()
        {
            this.Id = id;
        }

        public DisconnectedEventArgs(Exception ex, string id)
            : base()
        {
            this.Error = ex;
            this.Id = id;
        }
    }

    /// <summary>
    /// Generic class for various exceptions from SocketService
    /// </summary>
    public class SocketServiceException : Exception
    {
        public SocketServiceException(string message)
            : base(message)
        {

        }

        public SocketServiceException(string message, Exception innerException)
            : base(message, innerException)
        {

        }

    }
}