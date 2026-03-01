using System;
using System.Collections.Generic;
using System.Text;

namespace FEBuilderGBA
{
    public class ToolTextCharRecreate
    {
        // CharCounter is now defined in FETextEncode (Core).
        // Keep a type alias for backward compatibility.
        public class CharCounter : FETextEncode.CharCounter { }

        Dictionary<uint, FETextEncode.CharCounter> CharCounterMap = new Dictionary<uint, FETextEncode.CharCounter>();

	    public void Add(string str)
	    {
            Program.FETextEncoder.StringCount(str, CharCounterMap);
	    }
        public void AddEN(string str)
        {
            Program.FETextEncoder.StringCountEN(str, CharCounterMap);
        }

        public List<FETextEncode.CharCounter> GetSortedList()
	    {
            List<FETextEncode.CharCounter> list = new List<FETextEncode.CharCounter>(CharCounterMap.Values);
            list.Sort((a, b) => { return b.count != a.count ? (b.count) - (a.count) : (b.length) - (a.length); });
            return list;
	    }
    }
}
