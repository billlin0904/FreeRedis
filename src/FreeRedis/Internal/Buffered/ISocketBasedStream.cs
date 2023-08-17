using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

namespace FreeRedis.Internal.Buffered
{
    internal interface ISocketBasedStream
    {
        Socket BaseSocket { get; }
    }
}
