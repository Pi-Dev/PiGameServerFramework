using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.Metrics;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace PiGSF.Utils
{
    public static class Utils
    {
        // Usage: Another thread should be waiting on this queue 

        public static void EnqueueAndNotify<T>(this ConcurrentQueue<T> self, T value, object? mutex = null)
        {
            lock (mutex ?? self)
            {
                self.Enqueue(value);
                Monitor.Pulse(mutex ?? self);
            }
        }
        public static void EnqueueAndNotify<T>(this Queue<T> self, T value, object? mutex = null)
        {
            lock (mutex ?? self)
            {
                self.Enqueue(value);
                Monitor.Pulse(mutex ?? self);
            }
        }
        public static void AddAndNotify<T>(this Queue<T> self, T value, object? mutex = null)
        {
            lock (mutex ?? self)
            {
                self.Enqueue(value);
                Monitor.Pulse(mutex ?? self);
            }
        }
    }
}
