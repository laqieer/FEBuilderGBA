namespace FEBuilderGBA
{
    /// <summary>
    /// Lightweight address+name pair used throughout the codebase.
    /// Extracted from U.AddrResult to be a top-level public type
    /// so it can be shared across Core and UI assemblies without
    /// cross-assembly type conflicts (CS0029/CS0436).
    /// </summary>
    public class AddrResult
    {
        public uint addr;
        public string name;
        public uint tag;
        public bool isNULL() { return addr == 0 || name == null; }
        public AddrResult(uint addr, string name) { this.addr = addr; this.name = name; }
        public AddrResult(uint addr, string name, uint tag) { this.addr = addr; this.name = name; this.tag = tag; }
        public AddrResult() { }
    }
}
