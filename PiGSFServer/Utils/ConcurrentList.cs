using System;
using System.Collections;
using System.Collections.Generic;

namespace PiGSF.Utils
{
    public class ConcurrentList<T> : IList<T>
    {
        private readonly List<T> _internalList = new List<T>();
        private readonly object _lock = new object();

        public void ForEach(Action<T> operation)
        {
            lock (_lock)
            {
                foreach (T item in _internalList) operation(item);
            }
        }

        public void Filter(Func<T, bool> predicate)
        {
            lock (_lock)
            {
                for (int i = 0; i < _internalList.Count; i++)
                {
                    if (!predicate(_internalList[i]))
                    {
                        _internalList.RemoveAt(i);
                        i--;
                    }
                }
            }
        }

        public List<T> Copy()
        {
            lock (_lock)
            {
                return new List<T>(_internalList); // Creates a new copy
            }
        }

        public T this[int index]
        {
            get
            {
                lock (_lock)
                {
                    return _internalList[index];
                }
            }
            set
            {
                lock (_lock)
                {
                    _internalList[index] = value;
                }
            }
        }

        public int Count
        {
            get
            {
                lock (_lock)
                {
                    return _internalList.Count;
                }
            }
        }

        public bool IsReadOnly => false;

        public void Add(T item)
        {
            lock (_lock)
            {
                _internalList.Add(item);
            }
        }

        public bool AddIfNotExists(T item)
        {
            lock (_lock)
            {
                if (_internalList.Contains(item)) return false;
                _internalList.Add(item); return true;
            }
        }

        public void Clear()
        {
            lock (_lock)
            {
                _internalList.Clear();
            }
        }

        public bool Contains(T item)
        {
            lock (_lock)
            {
                return _internalList.Contains(item);
            }
        }

        public void CopyTo(T[] array, int arrayIndex)
        {
            lock (_lock)
            {
                _internalList.CopyTo(array, arrayIndex);
            }
        }

        public IEnumerator<T> GetEnumerator()
        {
            lock (_lock)
            {
                return new List<T>(_internalList).GetEnumerator();
            }
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }

        public int IndexOf(T item)
        {
            lock (_lock)
            {
                return _internalList.IndexOf(item);
            }
        }

        public void Insert(int index, T item)
        {
            lock (_lock)
            {
                _internalList.Insert(index, item);
            }
        }

        public bool Remove(T item)
        {
            lock (_lock)
            {
                return _internalList.Remove(item);
            }
        }

        public void RemoveAt(int index)
        {
            lock (_lock)
            {
                _internalList.RemoveAt(index);
            }
        }
    }
}
