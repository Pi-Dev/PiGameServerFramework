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
        public static T Choose<T>(params T[] x) => x[Random.Shared.Next(0, x.Length)];
        public static T Choose<T>(IList<T> x) => x[Random.Shared.Next(0, x.Count)];
        public static int Choose(params int[] x) => x[Random.Shared.Next(0, x.Length)];
        public static float RandomRange(this (float x, float y) range)
            => (float)(Random.Shared.NextDouble() * (range.y - range.x) + range.x);

        public static T GetRandomElement<T>(this IList<T> collection)
            => collection[Random.Shared.Next(0, collection.Count)];

        public static T GetRandomElementByWeight<T>(this IEnumerable<T> sequence, Func<T, float> weightSelector)
        {
            float totalWeight = sequence.Sum(weightSelector);
            float itemWeightIndex = (float)(Random.Shared.NextDouble() * totalWeight);
            float currentWeightIndex = 0;
            foreach (var item in sequence.Select(weightedItem => new { Value = weightedItem, Weight = weightSelector(weightedItem) }))
            {
                currentWeightIndex += item.Weight;
                if (currentWeightIndex >= itemWeightIndex)
                    return item.Value;
            }
            return default;
        }

    }
}
