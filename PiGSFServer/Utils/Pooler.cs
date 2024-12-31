using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PiGSF.Utils
{
    // Written by PeterSvP, originally for his game ColorBlend FX: Desaturation
    // commented out the reinit feature
    // Modified to be thread-safe
    public class ObjectPooler<T>
    {
        public ObjectPooler() { }
        public ObjectPooler(ObjectGenerateFunc f) => funcGenerate = f;

        // Generator function that will generate new object
        public delegate T ObjectGenerateFunc();
        public delegate void ObjectReinitFunc(T obj);
        public delegate void ObjectRecycleFunc(T obj);
        public delegate void ObjectDestroyFunc(T obj);

        public ObjectGenerateFunc? funcGenerate;
        //public ObjectReinitFunc? funcReinit;
        public ObjectRecycleFunc? funcRecycle;
        public ObjectDestroyFunc? funcDestroy;

        // The Stack pool of objects
        public ConcurrentStack<T> objects = new ConcurrentStack<T>();

        // Recycle object, so it's ready for reusing
        public void Recycle(T obj)
        {
            if (funcRecycle != null) funcRecycle(obj);
            objects.Push(obj);
        }

        // Get a new object from the pooler
        public T Buy()
        {
            if (objects.TryPop(out var obj)) return obj;
            else if (funcGenerate != null) return funcGenerate();
            else return default;
        }

        // Fill the pooler with objects
        public void Stock(int amount)
        {
            for (int i = 0; i < amount; ++i)
            {
                T obj;
                if (funcGenerate != null) obj = funcGenerate();
                else obj = default;
                objects.Push(obj);
            }
        }

        // Reset the pooler
        public void Clear(bool destroy = true)
        {
            if (destroy && funcDestroy != null)
            {
                var all = objects.ToArray();
                objects.Clear();
                foreach (var o in all)
                    funcDestroy(o);
            }
            else objects.Clear();
        }
    }

}
