using System;

namespace FEBuilderGBA
{
    /// <summary>
    /// No-op IAsmMapCache for headless (CLI / Avalonia) use. The real
    /// WinForms <c>FEBuilderGBA.AsmMapFileAsmCache</c> parses ASM map files
    /// on a background thread to populate per-unit / per-class / per-item
    /// "is hardcoded" lookups. That parsing depends on the full WinForms
    /// patch/symbol pipeline which is not (yet) ported to Core. Until it
    /// is, the headless host wires this stub so call sites can null-check
    /// the interface without crashing — `IsHardCodeUnit` always returns
    /// false, which means the unit-editor "[HardCoding]" warning hyperlink
    /// stays hidden in Avalonia (and behaves identically to WinForms on a
    /// fresh ROM where no hardcoded references have been detected).
    /// </summary>
    /// <remarks>
    /// Wired from <c>FEBuilderGBA.Avalonia/App.axaml.cs</c> after
    /// <c>HeadlessEtcCache</c> / <c>HeadlessSystemTextEncoder</c>. See #428
    /// for the originating gap-sweep PR that surfaced the need for this
    /// stub. A future PR can promote the WinForms hardcode-detection logic
    /// into Core and back this stub with a real implementation; the API
    /// surface (`IAsmMapCache.IsHardCodeUnit`) stays unchanged.
    /// </remarks>
    public class HeadlessAsmMapCache : IAsmMapCache
    {
        public void ClearCache() { }

        public bool IsHardCodeUnit(uint unitId) => false;
    }
}
