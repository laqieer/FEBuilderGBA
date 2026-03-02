using System;
using System.Collections.Generic;

namespace FEBuilderGBA
{
    /// <summary>
    /// No-op IEtcCache for headless (CLI/Avalonia) use.
    /// Stores nothing but satisfies the interface so Core code doesn't NullRef.
    /// </summary>
    public class HeadlessEtcCache : IEtcCache
    {
        readonly Dictionary<uint, string> _store = new Dictionary<uint, string>();

        public void RemoveOverRange(uint range)
        {
            var toRemove = new List<uint>();
            foreach (var k in _store.Keys)
                if (k >= range) toRemove.Add(k);
            foreach (var k in toRemove)
                _store.Remove(k);
        }

        public void RemoveRange(uint start, uint end)
        {
            var toRemove = new List<uint>();
            foreach (var k in _store.Keys)
                if (k >= start && k < end) toRemove.Add(k);
            foreach (var k in toRemove)
                _store.Remove(k);
        }

        public bool CheckFast(uint num) => _store.ContainsKey(num);

        public string At(uint num, string def = "") =>
            _store.TryGetValue(num, out string v) ? v : def;

        public string S_At(uint num) =>
            _store.TryGetValue(num, out string v) ? v : "";

        public bool TryGetValue(uint num, out string out_data) =>
            _store.TryGetValue(num, out out_data);

        public void Update(uint addr, string comment) =>
            _store[addr] = comment;

        public void Remove(uint addr) =>
            _store.Remove(addr);
    }
}
