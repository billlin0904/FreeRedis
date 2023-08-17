using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

namespace FreeRedis.Internal.Buffered
{
    public delegate void AsyncReadCallback(int bytesReceived, int errorCode, object state);

    public delegate void AsyncWriteCallback(int errorCode, object state);

    internal class TcpSocketAsyncEventArgs : SocketAsyncEventArgs
    {
        private AsyncReadCallback _asyncReadCallback = null;
        private AsyncWriteCallback _asyncWriteCallback = null;

        private static ConcurrentStack<TcpSocketAsyncEventArgs> _stacks = new ConcurrentStack<TcpSocketAsyncEventArgs>();

        protected override void OnCompleted(SocketAsyncEventArgs e)
        {
            if (e.LastOperation == SocketAsyncOperation.Receive && _asyncReadCallback != null)
            {
                _asyncReadCallback(e.BytesTransferred, (int)e.SocketError, UserToken);
                return;
            }
            if (e.LastOperation == SocketAsyncOperation.Send && _asyncWriteCallback != null)
            {
                _asyncWriteCallback((int)e.SocketError, UserToken);
                return;
            }
            base.OnCompleted(e);
        }

        public void ReadAsync(Socket socket, byte[] buffer, int offset, int size, AsyncReadCallback callback, object state)
        {
            _asyncReadCallback = callback;
            UserToken = state;
            SetBuffer(buffer, offset, size);
            if (!socket.ReceiveAsync(this))
            {
                OnCompleted(this);
            }
        }

        public void WriteAsync(Socket socket, byte[] buffer, int offset, int size, AsyncWriteCallback callback, object state)
        {
            _asyncWriteCallback = callback;
            UserToken = state;
            SetBuffer(buffer, offset, size);
            if (!socket.SendAsync(this))
            {
                OnCompleted(this);
            }
        }

        public static TcpSocketAsyncEventArgs Pop()
        {
            if (_stacks.TryPop(out TcpSocketAsyncEventArgs e))
                return e;

            return new TcpSocketAsyncEventArgs();
        }

        public static void Push(TcpSocketAsyncEventArgs e)
        {
            e._asyncReadCallback = null;
            e._asyncWriteCallback = null;
            e.SetBuffer(null, 0, 0);
            e.UserToken = null;
            _stacks.Push(e);
        }
    }
}
