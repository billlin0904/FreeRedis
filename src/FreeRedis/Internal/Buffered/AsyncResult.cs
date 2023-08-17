using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace FreeRedis.Internal.Buffered
{
    internal class AsyncResult : AsyncResult<object>
    {
        public AsyncResult(AsyncCallback userCallback, object userStateObject)
            : base(userCallback, userStateObject)
        {
        }
        public AsyncResult() : base()
        {
        }    
    }

    internal class AsyncResult<T> : IAsyncResult, IDisposable
    {
        private AsyncCallback _userCallback;
        private object _userStateObject;
        private ManualResetEvent _waitHandle;
        private T _result = default;
        private Exception _exception = null;
        private bool _isComplete;
        private bool _completedSynchronously;

        public T Result => _result;
        public Exception Exception => _exception;

        public AsyncResult(AsyncCallback userCallback, object userStateObject)
        {
            _userCallback = userCallback;
            _userStateObject = userStateObject;
        }
        public AsyncResult()
        {
        }

        public object AsyncState
        {
            get { return _userStateObject; }
        }

        public bool IsCompleted
        {
            get { return _isComplete; }
        }
        ~AsyncResult()
        {
            Dispose(false);
        }
        protected virtual void Dispose(bool disposing)
        {
            if (disposing)
            {
                _waitHandle?.Close();
            }
            _waitHandle = null;
            _userStateObject = null;
            _userCallback = null;
        }
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }
        public WaitHandle AsyncWaitHandle
        {
            get
            {
                if (_waitHandle == null)
                {
                    ManualResetEvent mre = new ManualResetEvent(false);

                    if (Interlocked.CompareExchange(ref _waitHandle, mre, null) == null)
                    {
                        if (_isComplete)
                            _waitHandle.Set();
                    }
                    else
                    {
                        mre.Close();
                    }
                }
                return _waitHandle;
            }
        }

        // Returns true iff the user callback was called by the thread that 
        // called BeginRead or BeginWrite.  If we use an async delegate or
        // threadpool thread internally, this will be false.  This is used
        // by code to determine whether a successive call to BeginRead needs 
        // to be done on their main thread or in their callback to avoid a
        // stack overflow on many reads or writes.
        public bool CompletedSynchronously
        {
            get { return _completedSynchronously; }
        }

        protected virtual void CallUserCallbackWorker()
        {
            Complete();
            _userCallback(this);
            _userCallback = null;
        }


        protected internal void Complete()
        {
            _isComplete = true;

            // ensure _isComplete is set before reading _waitHandle
            Thread.MemoryBarrier();
            if (_waitHandle != null)
                _waitHandle.Set();
        }

        public virtual void CallUserCallback()
        {
            if (_userCallback != null)
            {
                // Call user's callback on a threadpool thread.  
                // Set completedSynchronously to false, since it's on another 
                // thread, not the main thread.
                _completedSynchronously = false;
                ThreadPool.UnsafeQueueUserWorkItem(state => (state as AsyncResult<T>).CallUserCallbackWorker(), this);
                return;
            }
            Complete();
        }

        public void SetFailed(Exception ex)
        {
            _exception = ex;
            CallUserCallback();
        }

        public void SetResult(T result)
        {
            _result = result;
            CallUserCallback();
        }
    }
}
