using System;
using System.Collections.Generic;

namespace Tyrant.Dumper.Serialization
{
    public sealed class DumpObject
    {
        public DumpObject(Type clrType, EngineObjectInfo info, string json)
        {
            ClrType = clrType;
            Type = info.Type;
            Name = info.Name;
            Id = info.Id;
            Json = json;
        }

        public Type ClrType { get; }
        public string Type { get; }
        public string Name { get; }
        public long Id { get; }
        public string Json { get; }
    }

    public sealed class DumpResult
    {
        public List<DumpObject> Objects { get; } = new List<DumpObject>();
        public List<string> Errors { get; } = new List<string>();
    }

    /// <summary>Dumps the roots and, breadth-first, every data asset they reference — each exactly once.</summary>
    public sealed class DumpCollector
    {
        private readonly IDumpAdapter _adapter;

        public DumpCollector(IDumpAdapter adapter) => _adapter = adapter;

        public DumpResult Collect(IEnumerable<object> roots, int maxObjects = 20000)
        {
            var result = new DumpResult();
            var queue = new Queue<object>();
            var seen = new HashSet<object>(RefEq.Instance);

            void Enqueue(object o)
            {
                if (seen.Add(o)) queue.Enqueue(o);
            }

            foreach (var root in roots)
                if (root != null && _adapter.IsEngineObject(root) && _adapter.IsDumpable(root)) Enqueue(root);

            var serializer = new DumpSerializer(_adapter, Enqueue);
            while (queue.Count > 0)
            {
                if (result.Objects.Count >= maxObjects)
                {
                    result.Errors.Add($"Stopped after {maxObjects} objects; {queue.Count} more were not dumped.");
                    break;
                }
                var obj = queue.Dequeue();
                var info = _adapter.Describe(obj);
                try
                {
                    result.Objects.Add(new DumpObject(obj.GetType(), info, serializer.SerializeRoot(obj)));
                }
                catch (Exception ex)
                {
                    result.Errors.Add($"{info.Type} '{info.Name}': {ex.GetType().Name}: {ex.Message}");
                }
            }
            return result;
        }
    }
}
