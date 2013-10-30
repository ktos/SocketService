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
using System.Linq;
using Windows.Networking.Sockets;
using Windows.Storage.Streams;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.Rfcomm;

namespace Ktos.SocketService
{
    /// <summary>
    /// RfcomppSocketService is a base class for Windows 8 sockets-based applications.
    /// Windows.Devices.Bluetooth is not supported in Windows Phone 8, so RfcommSocketService
    /// is for Windows 8 only.
    /// 
    /// Based on Windows.Networking.Sockets and Windows.Storage.Streams is very similar
    /// to TCP sockets implementation and provides a Bluetooth RFCOMM client and server
    /// implementation for exchanging messages.        
    /// 
    /// Supports creating derived classes for specific protocol implementations.
    /// </summary>
    public class RfcommSocketService
    {
        /// <summary>
        /// Server socket
        /// </summary>
        protected StreamSocketListener socketListener;

        /// <summary>
        /// Provider of RFCOMM
        /// </summary>
        private RfcommServiceProvider rfcommProvider;

        /// <summary>
        /// The Id of the Service Name SDP attribute
        /// </summary>
        private const UInt16 SdpServiceNameAttributeId = 0x100;

        /// <summary>
        /// The SDP Type of the Service Name SDP attribute.
        /// The first byte in the SDP Attribute encodes the SDP Attribute Type as follows :
        ///    -  the Attribute Type size in the least significant 3 bits,
        ///    -  the SDP Attribute Type value in the most significant 5 bits.
        /// </summary>
        private const byte SdpServiceNameAttributeType = (4 << 3) | 5;

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
        public RfcommSocketService(SocketServiceMode mode)
        {
            this.operationMode = mode;
            clients = new List<ServiceClient>();
        }

        /// <summary>
        /// Initialize the Rfcomm service's SDP attributes.
        /// </summary>
        /// <param name="rfcommProvider">The Rfcomm service provider to initialize.</param>
        /// <param name="serviceName">The name of a service (visible to RFCOMM clients)</param>
        private void InitializeServiceSdpAttributes(RfcommServiceProvider rfcommProvider, string serviceName)
        {
            var sdpWriter = new DataWriter();

            // Write the Service Name Attribute.

            sdpWriter.WriteByte(SdpServiceNameAttributeType);

            // The length of the UTF-8 encoded Service Name SDP Attribute.
            sdpWriter.WriteByte((byte)serviceName.Length);

            // The UTF-8 encoded Service Name value.
            sdpWriter.UnicodeEncoding = Windows.Storage.Streams.UnicodeEncoding.Utf8;
            sdpWriter.WriteString(serviceName);

            // Set the SDP Attribute on the RFCOMM Service Provider.
            rfcommProvider.SdpRawAttributes.Add(SdpServiceNameAttributeId, sdpWriter.DetachBuffer());
        }

        /// <summary>
        /// Server initialization
        /// </summary>
        /// <param name="serviceName">Port number to bind to</param>
        /// <param name="serviceGuid">Address to bind to</param>
        public async void InitializeServer(string serviceName, Guid serviceGuid)
        {
            if (operationMode != SocketServiceMode.SERVER)
                throw new SocketServiceException("Mode not set properly.");

            try
            {
                rfcommProvider = await RfcommServiceProvider.CreateAsync(RfcommServiceId.FromUuid(serviceGuid));

                if (serviceName != null && serviceGuid != null && serviceGuid != Guid.Empty)
                {
                    // start listening
                    socketListener = new StreamSocketListener();
                    socketListener.ConnectionReceived += OnConnectionReceived;

                    // binding to service name
                    await socketListener.BindServiceNameAsync(rfcommProvider.ServiceId.AsString(),
                    SocketProtectionLevel.BluetoothEncryptionAllowNullAuthentication);

                    // setting SDP attributes and starting advertising our service
                    InitializeServiceSdpAttributes(rfcommProvider, serviceName);
                    rfcommProvider.StartAdvertising(socketListener);

                    // say you are listening
                    if (Listening != null)
                        Listening.Invoke(this, new ListeningEventArgs(null));
                }
                else
                {
                    throw new SocketServiceException("Service name and GUID must not be null");
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

        /// <summary>
        /// A Delegate for event when client connects to the server
        /// </summary>
        /// <param name="sender">Instance of the server class</param>
        /// <param name="e">Client ID and client socket information</param>
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
            var cid = Guid.Empty.ToString();
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

        /// <summary>
        /// A delegate handling event when client connects tot he server
        /// </summary>
        /// <param name="sender">Instance of client class</param>
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

        /// <summary>
        /// A delegate handling receiving data from server or client
        /// </summary>
        /// <param name="sender">Instance of client/server class</param>
        /// <param name="e">Data received</param>
        public delegate void DataReceivedEventHandler(object sender, DataReceivedEventArgs e);

        /// <summary>
        /// Executed when client (or server) disconnects
        /// </summary>
        public virtual event DisconnectedEventHandler Disconnected;

        /// <summary>
        /// A delegate handling disconnection event
        /// </summary>
        /// <param name="sender">Instance of client/server class</param>
        /// <param name="e">Client ID of a disconnected client and exception (if any)</param>
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

            this.Disconnect(Guid.Empty.ToString());
        }
    }

}