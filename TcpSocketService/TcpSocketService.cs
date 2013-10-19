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

namespace Ktos.SocketService
{
    /// <summary>
    /// A list of possible modes for service - client or server
    /// </summary>
    public enum SocketServiceMode
    {
        SERVER = 0,
        CLIENT = 1
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
        /// Server socket
        /// </summary>
        protected StreamSocketListener socketListener;

        /// <summary>
        /// Client socket
        /// </summary>
        protected Windows.Networking.Sockets.StreamSocket client;

        /// <summary>
        /// Writer used when sending messages
        /// </summary>
        protected DataWriter writer;

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
        }

        /// <summary>
        /// Server initialization - starting listening on a specified port
        /// </summary>
        /// <param name="servicePort">Port number to bind to</param>
        public async void InitializeServer(string servicePort)
        {
            if (operationMode != SocketServiceMode.SERVER)
                throw new SocketServiceException("Mode not set properly.");            

            string listen = "";
            try
            {                
                // finding an IP address to bind to
                foreach (var item in Windows.Networking.Connectivity.NetworkInformation.GetHostNames())
                {
                    if (item.IPInformation != null)
                    {                        
                        listen = item.DisplayName;
                        break;
                    }
                }

                if (listen != "")
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
        /// An event performed when server started listening
        /// </summary>
        public event ListeningEventHandler Listening;
        public delegate void ListeningEventHandler(object sender, ListeningEventArgs e);

        /// <summary>
        /// When server connection is received, start communication
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="args"></param>
        protected void OnConnectionReceived(
            StreamSocketListener sender, StreamSocketListenerConnectionReceivedEventArgs args)
        {
            try
            {
                // remove listener, we're done
                // TODO: change to multi-client
                socketListener.Dispose();
                socketListener = null;

                client = args.Socket;
                
                // send some information about client
                if (ClientConnected != null)
                    ClientConnected.Invoke(this, new ClientConnectedEventArgs(client.Information));

                CommunicationLoop();
            }
            catch (Exception e)
            {
                //Disconnected.Invoke(this, new DisconnectedEventArgs(e));                
                throw new SocketServiceException("Inner exception caused communication break.", e);
            }
        }

        

        /// <summary>
        /// Reading the message
        /// </summary>
        /// <param name="reader"></param>
        /// <param name="currentLength"></param>
        protected void readMessage(DataReader reader, uint currentLength)
        {
            byte[] message = new byte[currentLength];
            reader.ReadBytes(message);
            if (MessageReceived != null)
                MessageReceived.Invoke(this, new MessageReceivedEventArgs(message));
        }

        /// <summary>
        /// Notification about client connected, sends information about the client
        /// </summary>
        public event ClientConnectedEventHandler ClientConnected;
        public delegate void ClientConnectedEventHandler(object sender, ClientConnectedEventArgs e);

        /// <summary>
        /// Client initialization - to some host and port
        /// </summary>
        /// <param name="host">Host name to connect to</param>
        /// <param name="port">Port number to connect to</param>
        public async void InitializeClient(string host, string port)
        {
            if (operationMode != SocketServiceMode.CLIENT)
                throw new SocketServiceException("Mode not set properly.");

            // try to connect
            client = new StreamSocket();
            try
            {
                // if succeeded, notify about it
                await client.ConnectAsync(new Windows.Networking.HostName(host), port);
                if (Connected != null)
                    Connected.Invoke(this);

                // and start communication loop
                CommunicationLoop();
            }
            catch (Exception e)
            {
                throw new SocketServiceException("Client connection failed.", e);
            }
        }

        /// <summary>
        /// Notifies about connecting to the client
        /// </summary>
        public event ConnectedEventHandler Connected;
        public delegate void ConnectedEventHandler(object sender);        

        /// <summary>
        /// Communication loop with the server (or client - it was the same thing)
        /// </summary>
        protected async void CommunicationLoop()
        {
            try
            {
                var reader = new DataReader(client.InputStream);
                writer = new DataWriter(client.OutputStream);

                // same 
                bool remoteDisconnection = false;
                while (!remoteDisconnection)
                {
                    uint readLength = await reader.LoadAsync(sizeof(uint));
                    if (readLength < sizeof(uint))
                    {
                        remoteDisconnection = true;
                        break;
                    }
                    uint currentLength = reader.ReadUInt32();

                    readLength = await reader.LoadAsync(currentLength);
                    if (readLength < currentLength)
                    {
                        remoteDisconnection = true;
                        break;
                    }

                    readMessage(reader, currentLength);                    
                }

                reader.DetachStream();
                if (remoteDisconnection)
                {
                    if (Disconnected != null)
                        Disconnected.Invoke(this, new DisconnectedEventArgs(null));
                }
            }
            catch (Exception e)
            {
                throw new SocketServiceException("Inner exception caused communication break.", e);
            }
        }

        /// <summary>
        /// Executed when new message arrives
        /// </summary>
        public event MessageReceivedEventHandler MessageReceived;
        public delegate void MessageReceivedEventHandler(object sender, MessageReceivedEventArgs e);

        /// <summary>
        /// Executed when client (or server) disconnects
        /// </summary>
        public event DisconnectedEventHandler Disconnected;
        public delegate void DisconnectedEventHandler(object sender, DisconnectedEventArgs e);        

        /// <summary>
        /// Sends the message to the other side
        /// </summary>
        /// <param name="message">Message, will be automatically added length</param>
        public async void Send(byte[] message)
        {
            try
            {
                if (client != null)
                {                    
                    writer.WriteUInt32((uint)message.Length);
                    writer.WriteBytes(message);

                    await writer.StoreAsync();                    
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
        /// Disconnects from server (or client)
        /// </summary>
        public void Disconnect()
        {

            if (socketListener != null)
            {
                socketListener.Dispose();
                socketListener = null;
            }

            if (writer != null)
            {
                writer.DetachStream();
                writer = null;
            }

            if (client != null)
            {
                client.Dispose();
                client = null;
            }

        }
    }

    /// <summary>
    /// Event arguments when message received
    /// </summary>
    public class MessageReceivedEventArgs : EventArgs
    {
        /// <summary>
        /// Received message (without length)
        /// </summary>
        public byte[] Message { get { return this.message; } }
        private byte[] message;

        public MessageReceivedEventArgs(byte[] message) : base()
        {
            this.message = message;
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

        public ClientConnectedEventArgs(StreamSocketInformation clientInformation)
            : base()
        {
            this.clientInformation = clientInformation;
        }
    }

    /// <summary>
    /// Event args when disconnects is
    /// </summary>
    public class DisconnectedEventArgs : EventArgs
    {
        /// <summary>
        /// Exception, if disconnection is because of exception
        /// </summary>
        public Exception Error { get { return this.error; } }
        private Exception error;

        public DisconnectedEventArgs(Exception ex)
            : base()
        {
            this.error = ex;
        }
    }

    /// <summary>
    /// Generic class for various exceptions from TcpSocketService
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