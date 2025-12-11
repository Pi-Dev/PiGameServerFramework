using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading;

namespace PiGSF.Utils
{
    public static class Utils
    {
        public static readonly Random RandomShared = new Random(); // Create a single instance

        //tries to preserve negative spaces e.g. 0..3/4 = 0,  4..7/4 = 1,  but -1...-4 = -1, -5...-8 = -2 and so on
        // this corrects that. 
        public static int nfdiv(float a, float b)
        {
            return (int)(a > 0 ? a / b : (a - b + 1) / b);
        }
        public static float nfmod(float a, float b)
        {
            return a - b * (float)Math.Floor(a / b);
        }

        // AB based ranges
        public static float RemapRanges(this float v, float oldMin, float oldMax, float newMin, float newMax)
        {
            return (((v - oldMin) * (newMax - newMin)) / (oldMax - oldMin)) + newMin;
        }
        public static double RemapRanges(this double v, double oldMin, double oldMax, double newMin, double newMax)
        {
            return (((v - oldMin) * (newMax - newMin)) / (oldMax - oldMin)) + newMin;
        }
        public static decimal RemapRanges(this decimal v, decimal oldMin, decimal oldMax, decimal newMin, decimal newMax)
        {
            return (((v - oldMin) * (newMax - newMin)) / (oldMax - oldMin)) + newMin;
        }
        public static long RemapRanges(this long v, long oldMin, long oldMax, long newMin, long newMax)
        {
            return (((v - oldMin) * (newMax - newMin)) / (oldMax - oldMin)) + newMin;
        }

        public static float Clamp(this float value, float min, float max) => Math.Clamp(value, min, max);
        public static double Clamp(this double value, double min, double max) => Math.Clamp(value, min, max);
        public static int Clamp(this int value, int min, int max) => Math.Clamp(value, min, max);


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

        public static T Choose<T>(params T[] x) => x[RandomShared.Next(0, x.Length)];
        public static T Choose<T>(IList<T> x) => x[RandomShared.Next(0, x.Count)];
        public static int Choose(params int[] x) => x[RandomShared.Next(0, x.Length)];
        public static T GetRandomElement<T>(this IList<T> collection)
            => collection[RandomShared.Next(0, collection.Count)];

        public static T GetRandomElementByWeight<T>(this IEnumerable<T> sequence, Func<T, float> weightSelector)
            => GetRandomElementByWeight(sequence, weightSelector, RandomShared);
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

        // ForEach basically
        public static void Each<T>(this IEnumerable<T> source, Action<T> action)
        {
            foreach (T item in source)
                action(item);
        }

        // Weighted Random
        public class Weighted<T>
        {
            public T item;
            public float weight;
        }
        public static T GetByWeight<T>(this ICollection<Weighted<T>> collection)
        {
            return collection.GetRandomElementByWeight(x => x.weight).item;
        }

        // Common safety things
        public static T GetOrDefault<T>(this System.Array arr, int index, T def)
        {
            if (index < 0) return def;
            if (index >= arr.Length) return def;
            return (T)arr.GetValue(index);
        }

        public static T GetOrDefault<T>(this List<T> arr, int index, T def)
        {
            if (index < 0) return def;
            if (index >= arr.Count) return def;
            return (T)arr.ToArray().GetValue(index);
        }

        public static V GetOrDefault<K, V>(this Dictionary<K, V> dict, K key, V def)
        {
            if (dict == null) return def;
            V val;
            if (dict.TryGetValue(key, out val)) return val;
            return def;
        }
    }
}
