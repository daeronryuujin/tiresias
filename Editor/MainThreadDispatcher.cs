using UnityEditor;
using System;
using System.Collections.Concurrent;
using System.Threading;

namespace Tiresias
{
    /// <summary>
    /// Dispatches work from background threads (HTTP handlers) onto the Unity main thread.
    /// Required for any operation that mutates scene state (SerializedObject, GameObject creation, etc.).
    /// </summary>
    [InitializeOnLoad]
    public static class MainThreadDispatcher
    {
        private class WorkItem
        {
            public Action<WorkItem> Action;
            public ManualResetEventSlim Signal = new ManualResetEventSlim(false);
            public Exception Exception;
            public object Result;
        }

        private static readonly ConcurrentQueue<WorkItem> _queue = new ConcurrentQueue<WorkItem>();

        static MainThreadDispatcher()
        {
            EditorApplication.update += DrainQueue;
        }

        private static void DrainQueue()
        {
            while (_queue.TryDequeue(out var item))
            {
                try
                {
                    item.Action(item);
                }
                catch (Exception ex)
                {
                    item.Exception = ex;
                }
                finally
                {
                    item.Signal.Set();
                }
            }
        }

        /// <summary>
        /// Execute a function on the Unity main thread and block until it completes.
        /// Throws TimeoutException if the main thread doesn't pick it up within timeoutMs.
        /// Re-throws any exception the action raised.
        /// </summary>
        public static T Execute<T>(Func<T> func, int timeoutMs = 5000)
        {
            var item = new WorkItem
            {
                Action = wi => { wi.Result = func(); }
            };

            _queue.Enqueue(item);

            if (!item.Signal.Wait(timeoutMs))
                throw new TimeoutException("Main thread operation timed out");

            if (item.Exception != null)
                throw item.Exception;

            return (T)item.Result;
        }

        /// <summary>
        /// Execute a void action on the Unity main thread and block until it completes.
        /// </summary>
        public static void Execute(Action action, int timeoutMs = 5000)
        {
            Execute<object>(() => { action(); return null; }, timeoutMs);
        }
    }
}
