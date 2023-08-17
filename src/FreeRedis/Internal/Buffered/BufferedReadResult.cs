using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FreeRedis.Internal.Buffered
{
    internal class BufferedReadResult : AsyncResult
    {
        public int BytesTransfered { get; internal set; } = 0;
        public byte[] Buffer { get; internal set; } = null;
        public int Offset { get; internal set; } = 0;
        public int Count { get; internal set; } = 0;

        public BufferedReadResult(AsyncCallback callback, object state)
            : this(callback, state, null, 0, 0)
        {
        }

        public BufferedReadResult(AsyncCallback callback, object state, byte[] buffer, int offset, int count)
            : base(callback, state)
        {
            Buffer = buffer;
            Offset = offset;
            Count = count;
        }

        protected override void Dispose(bool disposing)
        {
            Buffer = null;
            base.Dispose(disposing);
        }
    }
}
