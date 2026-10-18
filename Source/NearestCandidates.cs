using System;
using System.Collections.Generic;

namespace RimFront
{
    // Bounded max heap: the worst retained candidate is always available for
    // replacement, regardless of the order in which cached chunks are visited.
    internal sealed class NearestCandidates<T>
    {
        internal struct Entry
        {
            internal T Value;
            internal float Distance;
            internal int Id;
        }
        readonly int capacity;
        readonly List<Entry> heap = new List<Entry>(), ordered = new List<Entry>();
        internal NearestCandidates(int capacity)
        { if (capacity <= 0) throw new ArgumentOutOfRangeException(nameof(capacity)); this.capacity = capacity; }
        internal int Count => heap.Count;
        static int Compare(Entry a, Entry b)
        { int distance = a.Distance.CompareTo(b.Distance); return distance != 0 ? distance : a.Id.CompareTo(b.Id); }
        internal bool Offer(T value, float distance, int id)
        {
            if (float.IsNaN(distance) || float.IsInfinity(distance) || distance < 0) return false;
            var entry = new Entry { Value = value, Distance = distance, Id = id };
            if (heap.Count == capacity)
            {
                if (Compare(entry, heap[0]) >= 0) return false;
                heap[0] = entry;
                int parent = 0;
                while (parent * 2 + 1 < heap.Count)
                {
                    int child = parent * 2 + 1;
                    if (child + 1 < heap.Count && Compare(heap[child + 1], heap[child]) > 0) child++;
                    if (Compare(heap[parent], heap[child]) >= 0) break;
                    Entry swap = heap[parent]; heap[parent] = heap[child]; heap[child] = swap; parent = child;
                }
            }
            else
            {
                heap.Add(entry); int child = heap.Count - 1;
                while (child > 0)
                {
                    int parent = (child - 1) / 2;
                    if (Compare(heap[parent], heap[child]) >= 0) break;
                    Entry swap = heap[parent]; heap[parent] = heap[child]; heap[child] = swap; child = parent;
                }
            }
            return true;
        }
        internal List<Entry> Ordered()
        { ordered.Clear(); ordered.AddRange(heap); ordered.Sort(Compare); return ordered; }
        internal void Clear() { heap.Clear(); ordered.Clear(); }
    }
}
