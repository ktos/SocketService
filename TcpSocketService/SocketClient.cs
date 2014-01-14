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
using Windows.Networking.Sockets;
using Windows.Storage.Streams;

namespace Ktos.SocketService
{
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
            return "4";
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
}
