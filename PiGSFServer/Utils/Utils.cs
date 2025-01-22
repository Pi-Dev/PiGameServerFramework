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
        public static T Choose<T>(Random r, params T[] x) => x[r.Next(0, x.Length)];
        public static T Choose<T>(Random r, IList<T> x) => x[r.Next(0, x.Count)];
        public static int Choose(Random r, params int[] x) => x[r.Next(0, x.Length)];
        public static T GetRandomElement<T>(this IList<T> collection, Random r)
            => collection[r.Next(0, collection.Count)];

        public static T Choose<T>(params T[] x) => x[Random.Shared.Next(0, x.Length)];
        public static T Choose<T>(IList<T> x) => x[Random.Shared.Next(0, x.Count)];
        public static int Choose(params int[] x) => x[Random.Shared.Next(0, x.Length)];
        public static T GetRandomElement<T>(this IList<T> collection)
            => collection[Random.Shared.Next(0, collection.Count)];

        public static T GetRandomElementByWeight<T>(this IEnumerable<T> sequence, Func<T, float> weightSelector)
            => GetRandomElementByWeight(sequence, weightSelector, Random.Shared);
        public static T GetRandomElementByWeight<T>(this IEnumerable<T> sequence, Func<T, float> weightSelector, Random r)
        {
            float totalWeight = sequence.Sum(weightSelector);
            float itemWeightIndex = (float)(r.NextDouble() * totalWeight);
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
