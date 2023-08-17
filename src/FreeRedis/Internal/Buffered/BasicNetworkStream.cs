using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

namespace FreeRedis.Internal.Buffered
{
    internal class BasicNetworkStream : NetworkStream, ISocketBasedStream
    {
        public Socket BaseSocket => Socket;

        private class ReadWriteArgs
        {
            public TcpSocketAsyncEventArgs TcpSocketAsyncEventArgs;
            public ReadWriteResult AsyncResult;
            public ReadWriteArgs(TcpSocketAsyncEventArgs e, ReadWriteResult asyncResult)
            {
                TcpSocketAsyncEventArgs = e;
                AsyncResult = asyncResult;
            }

            ~ReadWriteArgs()
            {
                TcpSocketAsyncEventArgs = null;
                AsyncResult = null;
            }
        }

        public BasicNetworkStream(Socket baseSocket) 
            : base(baseSocket) 
        { 
        }

        public BasicNetworkStream(Socket baseSocket, bool ownSocket) 
            : base(baseSocket, ownSocket) 
        { 
        }

        public override IAsyncResult BeginRead(byte[] buffer, int offset, int size, AsyncCallback callback, object state)
        {
            var e = TcpSocketAsyncEventArgs.Pop();
            var asyncResult = new ReadWriteResult(callback, state, buffer, offset, size);

            try
            {
                e.ReadAsync(Socket, buffer, offset, size, AfterRead, new ReadWriteArgs(e, asyncResult));
                return asyncResult;
            }
            catch (SocketException ex)
            {
                asyncResult.SetFailed(ex);
                TcpSocketAsyncEventArgs.Push(e);
                return asyncResult;
            }
            catch
            {
                asyncResult.Dispose();
                TcpSocketAsyncEventArgs.Push(e);
                throw;
            }
        }

        public override int EndRead(IAsyncResult asyncResult)
        {
            var result = asyncResult as ReadWriteResult;
            if (result == null) 
                throw new InvalidOperationException("asyncResult");

            if (result.IsCompleted) 
                result.AsyncWaitHandle.WaitOne();

            if (result.Exception != null) 
                throw result.Exception;

            return result.BytesTransfered;
        }

        private void AfterRead(int bytesReceived, int errorCode, object state)
        {
            var args = state as ReadWriteArgs;
            var result = args.AsyncResult;

            if (errorCode != 0)
            {
                result.SetFailed(new SocketException(errorCode));
            }
            else
            {
                result.BytesTransfered = bytesReceived;
                result.CallUserCallback();
            }
            TcpSocketAsyncEventArgs.Push(args.TcpSocketAsyncEventArgs);
        }

        public override IAsyncResult BeginWrite(byte[] buffer, int offset, int size, AsyncCallback callback, object state)
        {
            var e = TcpSocketAsyncEventArgs.Pop();
            var asyncResult = new ReadWriteResult(callback, state, buffer, offset, size);

            try
            {
                e.WriteAsync(Socket, buffer, offset, size, AfterWrite, new ReadWriteArgs(e, asyncResult));
                return asyncResult;
            }
            catch (SocketException ex)
            {
                asyncResult.SetFailed(ex);
                TcpSocketAsyncEventArgs.Push(e);
                return asyncResult;
            }
            catch
            {
                asyncResult.Dispose();
                TcpSocketAsyncEventArgs.Push(e);
                throw;
            }
        }

        private void AfterWrite(int errorCode, object state)
        {
            var args = state as ReadWriteArgs;
            var result = args.AsyncResult;

            if (errorCode != 0)
            {
                result.SetFailed(new SocketException(errorCode));
            }
            else
            {
                result.CallUserCallback();
            }
            TcpSocketAsyncEventArgs.Push(args.TcpSocketAsyncEventArgs);
        }
        public override void EndWrite(IAsyncResult asyncResult)
        {
            var result = asyncResult as ReadWriteResult;
            if (result == null)
                throw new InvalidOperationException("asyncResult");

            if (result.IsCompleted) 
                result.AsyncWaitHandle.WaitOne();

            if (result.Exception != null)
                throw result.Exception;
        }
    }
}
