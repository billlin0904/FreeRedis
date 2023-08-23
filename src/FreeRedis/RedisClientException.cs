using System;

namespace FreeRedis
{
    public class RedisClientException : Exception
    {
        public RedisClientException(string message) : base(message) { }
    }
}
