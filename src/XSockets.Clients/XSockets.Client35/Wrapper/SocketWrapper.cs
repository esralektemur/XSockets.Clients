﻿using System;
using System.IO;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;
using XSockets.Client35.Common.Interfaces;

namespace XSockets.Client35.Wrapper
{
    public class SocketWrapper : ISocketWrapper, IDisposable
    {
        private readonly CancellationTokenSource _tokenSource;
        private readonly TaskFactory _taskFactory;
        private readonly Uri _uri;
        public string RemoteIpAddress
        {
            get
            {
                var endpoint = Socket.RemoteEndPoint as IPEndPoint;
                return endpoint != null ? endpoint.Address.ToString() : null;
            }
        }

        public SocketWrapper()
        {

        }

        public SocketWrapper(Socket socket)
        {
            _tokenSource = new CancellationTokenSource();
            _taskFactory = new TaskFactory(_tokenSource.Token);
            Socket = socket;
            Socket.NoDelay = false;
            if (Socket.Connected)
                Stream = new NetworkStream(Socket);
        }

        public SocketWrapper(Socket socket, X509Certificate2 certificate2, Uri uri)
        {
            this._uri = uri;
            _tokenSource = new CancellationTokenSource();
            _taskFactory = new TaskFactory(_tokenSource.Token);
            Socket = socket;
            
            if (Socket.Connected)
                Stream = new NetworkStream(Socket);

            if (certificate2 == null)
            {
                this.AuthenticateAsClient().Wait();
            }
            else
            {
                this.AuthenticateAsClient(certificate2).Wait();
            }                
        }

        public Task AuthenticateAsClient()
        {
            var ssl = new SslStream(Stream, false, (sender, x509Certificate, chain, errors) =>
            {
                if (errors == SslPolicyErrors.None)
                    return true;

                return false;
            }, null);

            var tempStream = new SslStreamWrapper(ssl);
            Stream = tempStream;            
            Func<AsyncCallback, object, IAsyncResult> begin =
                (cb, s) => ssl.BeginAuthenticateAsClient(this._uri.Host, cb, s);

            var task = Task.Factory.FromAsync(begin, ssl.EndAuthenticateAsClient, null);

            return task;            
        }

        public Task AuthenticateAsClient(X509Certificate2 certificate)
        {
            var ssl = new SslStream(Stream, false, (sender, x509Certificate, chain, errors) =>
            {
                if (errors == SslPolicyErrors.None)
                    return true;
                
                return false;
            }, null);

            var tempStream = new SslStreamWrapper(ssl);
            Stream = tempStream;            
            Func<AsyncCallback, object, IAsyncResult> begin =
                (cb, s) => ssl.BeginAuthenticateAsClient(this._uri.Host,
                    new X509Certificate2Collection(certificate),SslProtocols.Tls, false, cb, s);

            var task = Task.Factory.FromAsync(begin, ssl.EndAuthenticateAsClient, null);
           
            return task;
        }

        public virtual void Listen(int backlog)
        {
            Socket.Listen(backlog);
        }

        public Socket Socket { get; set; }

        public virtual void Bind(EndPoint endPoint)
        {
            Socket.Bind(endPoint);
        }

        public virtual bool Connected
        {
            get { return Socket.Connected; }
            set { }
        }

        public Stream Stream { get; private set; }

        public virtual Task<int> Receive(byte[] buffer, Action<int> callback, Action<Exception> error, int offset)
        {
            if (_tokenSource.IsCancellationRequested || !this.Connected)
                return null;
            
            Func<AsyncCallback, object, IAsyncResult> begin =
                (cb, s) => Stream.BeginRead(buffer, offset, buffer.Length, cb, s);


            Task<int> task = Task.Factory.FromAsync<int>(begin, Stream.EndRead, null);
            if (callback != null)
            {
                task.ContinueWith(t => callback(t.Result), TaskContinuationOptions.NotOnFaulted)
                    .ContinueWith(t => error(t.Exception), TaskContinuationOptions.OnlyOnFaulted);
            }
            task.ContinueWith(t => error(t.Exception), TaskContinuationOptions.OnlyOnFaulted);
            return task;

        }

        public virtual Task<ISocketWrapper> Accept(Action<ISocketWrapper> callback, Action<Exception> error)
        {
            Func<IAsyncResult, ISocketWrapper> end = r =>
            {
                _tokenSource.Token.ThrowIfCancellationRequested();
                return new SocketWrapper(Socket.EndAccept(r));
            };
            var task = _taskFactory.FromAsync(Socket.BeginAccept, end, null);
            task.ContinueWith(t => callback(t.Result), TaskContinuationOptions.OnlyOnRanToCompletion)
                .ContinueWith(t => error(t.Exception), TaskContinuationOptions.OnlyOnFaulted);
            task.ContinueWith(t => error(t.Exception), TaskContinuationOptions.OnlyOnFaulted);
            return task;
        }

        public virtual void Dispose()
        {
            _tokenSource.Cancel();           
            _tokenSource.Dispose();
            if (Stream != null) Stream.Dispose();            
        }

        public virtual void Close()
        {
            if (Stream != null)
            {
                Stream.Flush();
                Stream.Close();
            }

            if (Socket != null) Socket.Close();
        }

        public int EndSend(IAsyncResult asyncResult)
        {
            Stream.EndWrite(asyncResult);
            return 0;
        }

        public virtual Task Send(byte[] buffer, Action callback, Action<Exception> error)
        {
            try
            {
                if (_tokenSource.IsCancellationRequested || !this.Connected)
                    return null;
                Func<AsyncCallback, object, IAsyncResult> begin =
                    (cb, s) =>
                    {
                        try
                        {
                            return Stream.BeginWrite(buffer, 0, buffer.Length, cb, s);
                        }
                        catch (IOException)
                        {
                            _tokenSource.Cancel();
                            
                            return null;
                        }
                    };


                Task task = Task.Factory.FromAsync(begin, Stream.EndWrite, null);
                task.ContinueWith(t => callback(), TaskContinuationOptions.NotOnFaulted)
                    .ContinueWith(t => error(t.Exception), TaskContinuationOptions.OnlyOnFaulted);
                task.ContinueWith(t => error(t.Exception), TaskContinuationOptions.OnlyOnFaulted);
                return task;
            }
            catch (Exception ex)
            {
                error(ex);
                return null;
            }
        }
    }
}
