using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using XSockets.Client40.Common.Event.Arguments;
using XSockets.Client40.Common.Interfaces;
using XSockets.Client40.Model;

namespace XSockets.Client40
{
    public partial class Controller : IController
    {
        private Task<T> _Target<T>(IMessage m, int timeoutMilliseconds = 30000)
        {
            var t = new Task<T>(() =>
            {
                var data = default(T);
                var working = true;
                var listener = new Listener(m.Topic,
                    message =>
                    {                        
                        data = this.XSocketClient.Serializer.DeserializeFromString<T>(message.Data);
                        working = false;
                    },
                    SubscriptionType.One);

                this.Listeners.AddOrUpdate(m.Topic, listener);

                this.Invoke(m);
                var r = SpinWait.SpinUntil(() => working == false, timeoutMilliseconds);

                if (r == false)
                {
                    this.DisposeListener(listener);
                    throw new TimeoutException("The server did not respond in the given time frame");
                }


                return data;
            });
            t.Start();
            return t;
        }

        private Task<T> _Target<T>(string s, int timeoutMilliseconds = 30000)
        {
            s = s.ToLower();
            var t = new Task<T>(() =>
            {
                var data = default(T);
                var working = true;
                var listener = new Listener(s,
                    message =>
                    {
                        data = this.XSocketClient.Serializer.DeserializeFromString<T>(message.Data); working = false;
                    },
                    SubscriptionType.One);

                this.Listeners.AddOrUpdate(s, listener);

                this.Invoke(this.AsMessage(s, null));

                var r = SpinWait.SpinUntil(() => working == false, timeoutMilliseconds);

                if (r == false)
                {
                    this.DisposeListener(listener);
                    throw new TimeoutException("The server did not respond in the given time frame");
                }

                return data;
            });
            t.Start();
            return t;
        }        

        public virtual void Invoke(IMessage payload)
        {
            if (!this.XSocketClient.IsConnected)
                throw new Exception("You cant send messages when not connected to the server");

            payload.Controller = this.ClientInfo.Controller;
            var frame = GetDataFrame(payload).ToBytes();  
            //If controller not yet open... Queue message
            if (!this.XSocketClient.IsHandshakeDone)
            {
                this.queuedFrames.AddRange(frame);
                return;
            }

            this.XSocketClient.Socket.Send(frame, () => { }, err => FireClosed());            
        }

        public virtual void Invoke(string target)
        {
            this.Invoke(this.AsMessage(target, null));
        }

        public virtual void Invoke(string target, object data)
        {
            this.Invoke(this.AsMessage(target, data));
        }

        public virtual void Invoke(string target, IList<byte> blob)
        {
            this.Invoke(target,blob,string.Empty);
        }

        public virtual void Invoke(string target, byte[] data)
        {
            this.Invoke(target, data, string.Empty);
        }

        public virtual void Invoke(string target, IList<byte> data, object metadata)
        {
            this.Invoke(new Message(data, metadata, target, this.ClientInfo.Controller));
        }

        public virtual Task<T> Invoke<T>(string target, int timeoutMilliseconds = 30000)
        {
            return _Target<T>(target, timeoutMilliseconds);        
        }

        public virtual Task<T> Invoke<T>(IMessage message, int timeoutMilliseconds = 30000)
        {
            return _Target<T>(message, timeoutMilliseconds);           
        }
        public virtual Task<T> Invoke<T>(string target, object data, int timeoutMilliseconds = 30000)
        {
            return _Target<T>(this.AsMessage(target, data), timeoutMilliseconds);            
        }

        public virtual Task<T> Invoke<T>(string target, IList<byte> data, int timeoutMilliseconds = 30000)
        {
            return this.Invoke<T>(new Message(data, null, target, this.ClientInfo.Controller));
        }

        public virtual Task<T> Invoke<T>(string target, byte[] data, int timeoutMilliseconds = 2000)
        {
            return this.Invoke<T>(new Message(data, target, this.ClientInfo.Controller));
        }

        public virtual Task<T> Invoke<T>(string target, IList<byte> data, object metadata, int timeoutMilliseconds = 30000)
        {
            return this.Invoke<T>(new Message(data, metadata, target, this.ClientInfo.Controller));
        }

        public virtual Task<T> Invoke<T>(string target, byte[] data, object metadata, int timeoutMilliseconds = 30000)
        {
            return this.Invoke<T>(new Message(data, metadata, target, this.ClientInfo.Controller));
        }

        public virtual IListener On<T>(string target, Action<T> action)
        {
            if (typeof(T) == typeof(IMessage))
            {
                var listener = new Listener(target, message => action((T)message))
                {
                    Controller = this
                };
                return this.Listeners.AddOrUpdate(listener.Topic, listener);
            }
            else
            {
                var listener = new Listener(target, message => action(this.XSocketClient.Serializer.DeserializeFromString<T>(message.Data)))
                {
                    Controller = this
                };
                return this.Listeners.AddOrUpdate(listener.Topic, listener);
            }

        }

        public virtual IListener On(string target, Action action)
        {
            var listener = new Listener(target, message => action())
            {
                Controller = this
            };
            return this.Listeners.AddOrUpdate(listener.Topic, listener);
        }

        public virtual void DisposeListener(IListener listener)
        {
            this.Listeners.Remove(listener.Topic.ToLower());
        }        
    }
}