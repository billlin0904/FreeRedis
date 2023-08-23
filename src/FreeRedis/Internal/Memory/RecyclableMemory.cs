using System.Buffers;
using System.IO;
using System.Text;
using Microsoft.Extensions.ObjectPool;
using Microsoft.IO;

namespace FreeRedis.Internal.Memory
{
    public static class RecyclableMemory
    {
        private static readonly RecyclableMemoryStreamManager _rms = new RecyclableMemoryStreamManager();
        private static readonly ObjectPool<StringBuilder> _stringBuilderPool
            = new DefaultObjectPoolProvider().CreateStringBuilderPool();

        public static MemoryStream GetStream() => _rms.GetStream();

        public static RecyclableMemoryStream GetRecyclableMemoryStream() => (RecyclableMemoryStream) _rms.GetStream();

        public static byte[] GetBuffer(int minimumLength) => ArrayPool<byte>.Shared.Rent(minimumLength);

        public static void ReturnBuffer(byte[] buffer) => ArrayPool<byte>.Shared.Return(buffer);

        public static StringBuilder GetStringBuilder() => _stringBuilderPool.Get();

        public static void ReturnStringBuilder(StringBuilder builder) => _stringBuilderPool.Return(builder);

        static RecyclableMemory()
        {
        }
    }
}
