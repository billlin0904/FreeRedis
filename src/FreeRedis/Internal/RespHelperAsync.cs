#if isasync
using FreeRedis.Internal.Buffered;
using FreeRedis.Internal.Memory;
using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

namespace FreeRedis
{
    partial class RespHelper
    {
        partial class Resp3Reader
        {
            public async Task ReadBlobStringChunkAsync(Stream destination, int bufferSize)
            {
                char c = (char)_stream.ReadByte();
                switch (c)
                {
                    case '$':
                    case '=':
                    case '!': await ReadBlobStringAsync(c, null, destination, bufferSize); break;
                    default: throw new ProtocolViolationException($"Expecting fail MessageType '{c}'");
                }
            }

            private byte[] _blobStringBuffer = new byte[1];

            async Task<object> ReadBlobStringAsync(char msgtype, Encoding encoding, Stream destination, int bufferSize)
            {
                var ns = (NetworkStream)_stream;

                var clob = await ReadClobAsync().ConfigureAwait(false);
                if (encoding == null) return clob;
                if (clob == null) return null;
                return encoding.GetString(clob);

                async Task<byte[]> ReadClobAsync()
                {
                    MemoryStream ms = null;                    
                    try
                    {
                        if (destination == null) destination = ms = RecyclableMemory.GetStream();
                        var lenstr = await ReadLineAsync(null);
                        if (int.TryParse(lenstr, out var len))
                        {
                            if (len < 0) return null;
                            if (len > 0) await ReadAsync(destination, len, bufferSize).ConfigureAwait(false);
                            await ReadLineAsync(null).ConfigureAwait(false);
                            if (len == 0) return new byte[0];
                            return ms?.ToArray();
                        }
                        if (lenstr == "?")
                        {
                            while (true)
                            {
                                _blobStringBuffer[0] = (byte) ';';
                                var readed = await ns.ReadAsync(_blobStringBuffer, 0, 1).ConfigureAwait(false);
                                var c = (char)_blobStringBuffer[0];
                                if (c != ';') throw new ProtocolViolationException($"Expecting fail Streamed strings ';', got '{c}'");
                                var clenstr = await ReadLineAsync(null).ConfigureAwait(false);
                                if (int.TryParse(clenstr, out var clen))
                                {
                                    if (clen == 0) break;
                                    if (clen > 0)
                                    {
                                        await ReadAsync(destination, clen, bufferSize).ConfigureAwait(false);
                                        await ReadLineAsync(null).ConfigureAwait(false);
                                        continue;
                                    }
                                }
                                throw new ProtocolViolationException($"Expecting fail Streamed strings ';0', got ';{clenstr}'");
                            }
                            return ms?.ToArray();
                        }
                        throw new ProtocolViolationException($"Expecting fail Blob string '{msgtype}0', got '{msgtype}{lenstr}'");
                    }
                    finally
                    {
                        ms?.Close();
                        ms?.Dispose();
                    }
                }
            }

            async Task<object[]> ReadArrayAsync(char msgtype, Encoding encoding)
            {
                var lenstr = await ReadLineAsync(null);
                if (int.TryParse(lenstr, out var len))
                {
                    if (len < 0) return null;
                    var arr = new object[len];
                    for (var a = 0; a < len; a++)
                        arr[a] = (await ReadObjectAsync(encoding).ConfigureAwait(false)).Value;
                    if (len == 1 && arr[0] == null) return new object[0];
                    return arr;
                }
                if (lenstr == "?")
                {
                    var arr = new List<object>();
                    while (true)
                    {
                        var ro = await ReadObjectAsync(encoding).ConfigureAwait(false);
                        if (ro.IsEnd) break;
                        arr.Add(ro.Value);
                    }
                    return arr.ToArray();
                }
                throw new ProtocolViolationException($"Expecting fail Array '{msgtype}3', got '{msgtype}{lenstr}'");
            }
            async Task<object[]> ReadMapAsync(char msgtype, Encoding encoding)
            {
                var lenstr = await ReadLineAsync(null);
                if (int.TryParse(lenstr, out var len))
                {
                    if (len < 0) return null;
                    var arr = new object[len * 2];
                    for (var a = 0; a < len; a++)
                    {
                        arr[a * 2] = (await ReadObjectAsync(encoding).ConfigureAwait(false)).Value;
                        arr[a * 2 + 1] = (await ReadObjectAsync(encoding).ConfigureAwait(false)).Value;
                    }
                    return arr;
                }
                if (lenstr == "?")
                {
                    var arr = new List<object>();
                    while (true)
                    {
                        var rokey = await ReadObjectAsync(encoding).ConfigureAwait(false);
                        if (rokey.IsEnd) break;
                        var roval = await ReadObjectAsync(encoding).ConfigureAwait(false);
                        arr.Add(rokey.Value);
                        arr.Add(roval.Value);
                    }
                    return arr.ToArray();
                }
                throw new ProtocolViolationException($"Expecting fail Map '{msgtype}3', got '{msgtype}{lenstr}'");
            }

            private byte[] _testByteBuffer = new byte[1];

            public async Task<RedisResult> ReadObjectAsync(Encoding encoding)
            {
                var ns = (BufferedNetworkStream)_stream;

                while (true)
                {
                    _testByteBuffer[0] = 0;
                    var readed = await ns.ReadAsync(_testByteBuffer, 0, 1).ConfigureAwait(false);
                    var c = (char)_testByteBuffer[0];
                    var b = _testByteBuffer[0];
                    //debugger++;
                    //if (debugger > 10000 && debugger % 10 == 0) 
                    //    throw new ProtocolViolationException($"Expecting fail MessageType '{b},{string.Join(",", ReadAll())}'");
                    switch (c)
                    {
                        case '$': return new RedisResult(await ReadBlobStringAsync(c, encoding, null, 1024).ConfigureAwait(false), false, RedisMessageType.BlobString);
                        case '+': return new RedisResult(await ReadSimpleStringAsync().ConfigureAwait(false), false, RedisMessageType.SimpleString);
                        case '=': return new RedisResult(await ReadBlobStringAsync(c, encoding, null, 1024).ConfigureAwait(false), false, RedisMessageType.VerbatimString);
                        case '-':
                            {
                                var simpleError = await ReadSimpleStringAsync().ConfigureAwait(false);
                                if (simpleError == "NOAUTH Authentication required.")
                                    throw new ProtocolViolationException(simpleError);
                                return new RedisResult(simpleError, false, RedisMessageType.SimpleError);
                            }
                        case '!': return new RedisResult(await ReadBlobStringAsync(c, encoding, null, 1024).ConfigureAwait(false), false, RedisMessageType.BlobError);
                        case ':': return new RedisResult(await ReadNumberAsync(c).ConfigureAwait(false), false, RedisMessageType.Number);
                        case '(': return new RedisResult(await ReadBigNumberAsync(c).ConfigureAwait(false), false, RedisMessageType.BigNumber);
                        case '_': await ReadLineAsync(null).ConfigureAwait(false); return new RedisResult(null, false, RedisMessageType.Null);
                        case ',': return new RedisResult(ReadDoubleAsync(c).ConfigureAwait(false), false, RedisMessageType.Double);
                        case '#': return new RedisResult(ReadBooleanAsync(c).ConfigureAwait(false), false, RedisMessageType.Boolean);

                        case '*': return new RedisResult(await ReadArrayAsync(c, encoding).ConfigureAwait(false), false, RedisMessageType.Array);
                        case '~': return new RedisResult(await ReadArrayAsync(c, encoding).ConfigureAwait(false), false, RedisMessageType.Set);
                        case '>': return new RedisResult(await ReadArrayAsync(c, encoding).ConfigureAwait(false), false, RedisMessageType.Push);
                        case '%': return new RedisResult(await ReadMapAsync(c, encoding).ConfigureAwait(false), false, RedisMessageType.Map);
                        case '|': return new RedisResult(await ReadMapAsync(c, encoding).ConfigureAwait(false), false, RedisMessageType.Attribute);
                        case '.': await ReadLineAsync(null).ConfigureAwait(false); return new RedisResult(null, true, RedisMessageType.SimpleString); //无类型
                        case ' ': continue;
                        default:
                            if (b == -1) return new RedisResult(null, true, RedisMessageType.Null);
                            //if (b == -1) return new RedisResult(null, true, RedisMessageType.Null);
                            var allBytes = DebugReadAll();
                            throw new ProtocolViolationException($"Expecting fail MessageType '{b},{string.Join(",", allBytes)}'");
                    }
                }
            }

            async Task ReadAsync(Stream outStream, int len, int bufferSize = 1024)
            {
                if (len <= 0) return;
                var bufferLength = Math.Min(bufferSize, len);
                var buffer = RecyclableMemory.GetBuffer(bufferLength);
                try
                {
                    while (true)
                    {
                        var readed = await _stream.ReadAsync(buffer, 0, bufferLength).ConfigureAwait(false);
                        if (readed <= 0)
                            throw new ProtocolViolationException($"Expecting fail Read surplus length: {len}");
                        if (readed > 0) await outStream.WriteAsync(buffer, 0, readed).ConfigureAwait(false);
                        len = len - readed;
                        if (len <= 0) break;
                        if (len < buffer.Length) bufferLength = len;
                    }
                }
                finally
                {
                    RecyclableMemory.ReturnBuffer(buffer);
                }
            }
        }
    }
}
#endif