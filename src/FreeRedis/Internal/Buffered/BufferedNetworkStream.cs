using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

namespace FreeRedis.Internal.Buffered
{
    internal class BufferedNetworkStream : BasicNetworkStream
    {
        private static readonly int MAX_BUF_SIZE = 64 * 1024;

        public BufferedNetworkStream(Socket baseSocket) 
            : base(baseSocket) 
        { 
        }

        public BufferedNetworkStream(Socket baseSocket, bool ownSocket) 
            : base(baseSocket, ownSocket) 
        { 
        }

        protected override void Dispose(bool disposing)
        {
            _buffer = null;
            base.Dispose(disposing);
        }

        private bool _buffered = true;
        public bool Buffered
        {
            get => _buffered;
            set => _buffered = value;
        }

        private byte[] _buffer = new byte[MAX_BUF_SIZE];
        private int _offset = 0;
        private int _length = 0;

        public override int ReadByte()
        {
            if (_length > 0)
            {
                _length--;
                return _buffer[_offset++];
            }
            return base.ReadByte();
        }

        public override int Read(byte[] buffer, int offset, int size)
        {
            if (_length == 0 && _buffered)
            {
                _offset = 0;
                _length = base.Read(_buffer, 0, _buffer.Length);
                if (_length == 0)
                    return 0;
            }
            if (_length == 0)
            {
                return base.Read(buffer, offset, size);
            }
            return CopyFromBuffer(buffer, offset, size);
        }

        public override IAsyncResult BeginRead(byte[] buffer, int offset, int size, AsyncCallback callback, object state)
        {
            if (_length == 0 && _buffered)
            {
                _offset = 0;
                var asyncResult = new BufferedReadResult(callback, state, buffer, offset, size);
                base.BeginRead(_buffer, _offset, _buffer.Length, AfterRead, asyncResult);
                return asyncResult;
            }

            if (_length > 0)
            {
                int rec = CopyFromBuffer(buffer, offset, size);
                var asyncResult = new BufferedReadResult(callback, state, buffer, offset, size);
                asyncResult.BytesTransfered = rec;
                asyncResult.CallUserCallback();
                return asyncResult;
            }

            return base.BeginRead(buffer, offset, size, callback, state);
        }

        private void AfterRead(IAsyncResult asyncResult)
        {
            var asyncReadResult = asyncResult.AsyncState as BufferedReadResult;
            try
            {
                int rec = base.EndRead(asyncResult);
                if (rec == 0)
                {
                    asyncReadResult.BytesTransfered = 0;
                    asyncReadResult.CallUserCallback();
                    return;
                }
                _length += rec;
                asyncReadResult.BytesTransfered = CopyFromBuffer(asyncReadResult.Buffer, asyncReadResult.Offset, asyncReadResult.Count);
                asyncReadResult.CallUserCallback();
            }
            catch (Exception e)
            {
                asyncReadResult.SetFailed(e);
            }
        }

        public override int EndRead(IAsyncResult asyncResult)
        {
            if (asyncResult is BufferedReadResult asyncReadResult)
            {
                if (asyncReadResult.Exception != null)
                    throw asyncReadResult.Exception;

                return asyncReadResult.BytesTransfered;
            }
            return base.EndRead(asyncResult);
        }

        private int CopyFromBuffer(byte[] buffer, int offset, int size)
        {
            if (size > _length) 
                size = _length;


            if (size > 1)
            {
                Array.Copy(_buffer, _offset, buffer, offset, size);
            }
            else
            {
                buffer[offset] = _buffer[_offset];
            }            

            _offset += size;
            _length -= size;
            return size;
        }
    }
}
