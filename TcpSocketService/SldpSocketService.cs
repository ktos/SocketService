﻿#region Licence
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
using System.Threading.Tasks;
using Windows.Storage.Streams;

namespace Ktos.SocketService.SldpSocketService
{
    /// <summary>
    /// A TcpSocketService-based class, implementing the protocol previously implemented in generic TcpSocketService.
    /// 
    /// "Protocol" is based on sending first a length of a message as a uint, and then the whole message. Receiving also handles
    /// first length, then data, thus the protocol is dubbed "Simple Length-Data Protocol" - SLDP. However, it's not defined
    /// in any formal way.
    /// 
    /// Allows building derived class implementing specific tasks, like text chat
    /// </summary>
    public class SldpSocketService : TcpSocketService
    {
        /// <summary>
        /// Creates a new SldpSocketService object with a specified operation mode - as a client or a server
        /// </summary>
        /// <param name="operationMode">A server or client mode</param>
        public SldpSocketService(SocketServiceMode operationMode)
            : base(operationMode)
        {
            
        }

        /// <summary>
        /// Communication loop with the server (or client - it was the same thing). Overrides generic TcpSocketService 
        /// CommunicationLoop() with a specific logic of data reading
        /// </summary>
        protected override async void CommunicationLoop(string clientId)
        {
            try
            {
                // get a client to start communication loop
                var c = GetClient(clientId);

                if (c == null)
                    throw new IndexOutOfRangeException("Client not found");

                var reader = new DataReader(c.Socket.InputStream);

                reader.InputStreamOptions = InputStreamOptions.None;
                c.Writer = new DataWriter(c.Socket.OutputStream);

                // while client is not disconnected
                bool remoteDisconnection = false;
                while (!remoteDisconnection)
                {
                    // logic for reading data - first size, then rest of data
                    uint readLength = await reader.LoadAsync(sizeof(uint));
                    if (readLength < sizeof(uint))
                    {
                        remoteDisconnection = true;
                        break;
                    }

                    uint currentLength = reader.ReadUInt32();

                    //if (currentLength > 0)
                    //{
                        readLength = await reader.LoadAsync(currentLength);
                        if (readLength < currentLength)
                        {
                            remoteDisconnection = true;
                            break;
                        }

                        readMessage(reader, currentLength);
                    //}
                }

                // when disconnected - detach, send event and remove client
                reader.DetachStream();
                Disconnect(clientId);
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
        /// Sends the message to the other side
        /// </summary>
        /// <param name="message">Message, will be automatically added length</param>
        /// <param name="clientId">GUID of a client to send message to</param>
        public override async Task SendAsync(byte[] message, string clientId)
        {
            try
            {
                var c = GetClient(clientId);
                if (c != null)
                {
                    c.Writer.WriteUInt32((uint)message.Length);
                    c.Writer.WriteBytes(message);

                    await c.Writer.StoreAsync();
                    return;
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
        /// Executed when new message arrives - should be used instead of regular DataReceived,
        /// as this show properly formatted messages
        /// </summary>
        public event MessageReceivedEventHandler MessageReceived;

        /// <summary>
        /// A delegate for handling new messages
        /// </summary>
        /// <param name="sender">Instance of a server class</param>
        /// <param name="e">A byte array of message</param>
        public delegate void MessageReceivedEventHandler(object sender, MessageReceivedEventArgs e);
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

        /// <summary>
        /// Creates a new MessageReceivedEventArgs object
        /// </summary>
        /// <param name="message">Bytes of a message (without length)</param>
        public MessageReceivedEventArgs(byte[] message)
            : base()
        {
            this.message = message;
        }
    }
}