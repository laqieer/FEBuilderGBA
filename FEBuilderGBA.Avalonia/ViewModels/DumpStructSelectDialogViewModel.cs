// SPDX-License-Identifier: GPL-3.0-or-later
// Avalonia view model for the Struct Dump Selector dispatcher dialog.
// Mirrors WinForms `DumpStructSelectDialogForm` — a non-data-editing dialog
// that dispatches between binary view / clipboard formats / export formats /
// import. The 12 Func enum values match the WinForms enum 1:1 so paired tests
// stay in sync.
//
// This dispatcher exposes 4 cross-editor jumps via INavigationTargetSource
// (see the .NavigationTargets.cs partial): Hex Editor, PointerToolCopyTo,
// the text-display dialog, and the dispatcher itself.
using System.Text;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    public partial class DumpStructSelectDialogViewModel : ViewModelBase
    {
        /// <summary>
        /// Dispatcher action enum. 12 values mirror the WinForms
        /// <c>DumpStructSelectDialogForm.Func</c> enum exactly.
        /// </summary>
        public enum Func
        {
            Func_Cancel,
            Func_Binary,
            Func_CSV,
            Func_TSV,
            Func_STRUCT,
            Func_EA,
            Func_NMM,
            Func_Clipbord_Pointer,
            Func_Clipbord_Copy,
            Func_Clipbord_LittleEndian,
            Func_Clipbord_NoDollBreakPoint,
            Func_Import,
        }

        uint _currentAddr;
        bool _isLoaded;
        Func _selectedFunc = Func.Func_Cancel;

        /// <summary>Address being dumped — set by <see cref="LoadAddress(uint)"/>.</summary>
        public uint CurrentAddr { get => _currentAddr; set => SetField(ref _currentAddr, value); }
        /// <summary>True after <see cref="LoadAddress(uint)"/> has been called at least once.</summary>
        public bool IsLoaded { get => _isLoaded; set => SetField(ref _isLoaded, value); }
        /// <summary>Most recently selected action (which button was last clicked).</summary>
        public Func SelectedFunc { get => _selectedFunc; set => SetField(ref _selectedFunc, value); }

        /// <summary>
        /// Initialize the dispatcher for a given address. Mirrors the WinForms
        /// <c>Init(uint addr)</c> method on the dialog. Does not perform any
        /// ROM operations — the dispatch happens when a button is clicked.
        /// </summary>
        public void LoadAddress(uint addr)
        {
            CurrentAddr = addr;
            IsLoaded = true;
        }

        // ===================================================================
        // Clipboard text builders. Each method mirrors the WinForms Click
        // handlers' clipboard payload exactly (verified against
        // DumpStructSelectDialogForm.cs lines 1188-1213).
        // ===================================================================

        /// <summary>
        /// Clipboard text for "Copy as Pointer". WinForms behavior at line 1190
        /// is <c>U.ToHexString(U.toPointer(addr))</c>: convert offset-form
        /// addresses to ROM-pointer form before hex-formatting.
        /// </summary>
        public string MakeCopyPointerText(uint addr) => U.ToHexString(U.toPointer(addr));

        /// <summary>
        /// Clipboard text for "Copy to Clipboard". WinForms behavior at line
        /// 1195 is <c>U.ToHexString(addr)</c>: plain hex format with NO
        /// pointer conversion (caller gets exactly the address as displayed).
        /// </summary>
        public string MakeCopyAddressText(uint addr) => U.ToHexString(addr);

        /// <summary>
        /// Clipboard text for "Copy as Little Endian Pointer". WinForms
        /// behavior at lines 1200-1206: byte-swap the pointer form of the
        /// address so it copies as the four bytes a GBA emulator memory dump
        /// would show.
        /// </summary>
        public string MakeCopyLittleEndianText(uint addr)
        {
            uint a = U.toPointer(addr);
            uint r = (((a & 0xFFu) << 24)
                + ((a & 0xFF00u) << 8)
                + ((a & 0xFF0000u) >> 8)
                + ((a & 0xFF000000u) >> 24));
            return U.ToHexString(r);
        }

        /// <summary>
        /// Clipboard text for "Copy as no$gba Read Breakpoint". WinForms
        /// behavior at line 1211: surround the pointer form with brackets +
        /// a "?" suffix, matching the no$gba debugger's breakpoint syntax.
        /// </summary>
        public string MakeCopyNoDollBreakpointText(uint addr)
            => "[" + U.ToHexString(U.toPointer(addr)) + "]?";

        // ===================================================================
        // Export text builders.
        //
        // CSV/TSV/EA/STRUCT/NMM now produce struct-aware output via
        // StructExportCore when the loaded address falls inside a known ROM data
        // table (units, classes, items, ...). The dispatcher resolves the table
        // from the address alone (StructExportCore.ResolveTableAt), so it no
        // longer needs the source editor's InputFormRef widget tree — closing the
        // long-standing Avalonia-vs-WinForms gap tracked from #439 (#770) and
        // extended to STRUCT (.h C-header) + NMM (No$gba memory map) in #1012.
        //
        // CSV/TSV/EA emit per-entry table DATA; STRUCT/NMM emit the struct LAYOUT
        // (field names/offsets/types). Any address that resolves to no known
        // table (or no ROM) still falls back to the honest 128-byte hex dump in
        // MakeStubExportText.
        // ===================================================================

        /// <summary>
        /// Build the export preview text for a given format button.
        /// For CSV/TSV/EA/STRUCT/NMM, if the loaded address falls inside a known
        /// ROM struct table, returns the raw struct-aware formatter output —
        /// byte-identical to the CLI <c>--export-data</c> writers. CSV/TSV/EA emit
        /// the table data; STRUCT emits the C-header layout and NMM the No$gba
        /// memory map. Otherwise (no table matched / no ROM loaded) falls back to
        /// the honest hex dump from <see cref="MakeStubExportText"/>.
        /// </summary>
        public string MakeExportText(string format)
        {
            if (format is "CSV" or "TSV" or "EA" or "STRUCT" or "NMM")
            {
                ROM? rom = CoreState.ROM;
                if (rom != null)
                {
                    var table = StructExportCore.ResolveTableAt(rom, CurrentAddr);
                    var sd = table != null ? StructExportCore.LoadStructDef(rom, table) : null;
                    if (table != null && sd != null)
                    {
                        if (format == "STRUCT") return StructExportCore.FormatSTRUCT(sd);
                        if (format == "NMM") return StructExportCore.FormatNMM(rom, table, sd);
                        var entries = StructExportCore.ExportTable(rom, table, sd);
                        return format switch
                        {
                            "CSV" => StructExportCore.FormatCSV(entries, sd),
                            "TSV" => StructExportCore.FormatTSV(entries, sd),
                            "EA" => StructExportCore.FormatEA(entries, sd),
                            _ => MakeStubExportText(format),
                        };
                    }
                }
            }

            // Unresolved address / no ROM → honest hex fallback.
            return MakeStubExportText(format);
        }

        /// <summary>
        /// Name of the struct table that contains <see cref="CurrentAddr"/>, or
        /// null if no ROM is loaded or no known table matches. Used by the View
        /// to label the export preview filename.
        /// </summary>
        public string? ResolvedTableName()
            => CoreState.ROM is { } r ? StructExportCore.ResolveTableAt(r, CurrentAddr)?.Name : null;

        // ===================================================================
        // Honest-stub export text builder. Returns a placeholder hex dump of
        // 128 bytes starting at the loaded address. Used as the fallback for any
        // address that does not resolve to a known struct table (or when no ROM
        // is loaded) across ALL export formats — including STRUCT/NMM, which
        // produce struct-aware output for RESOLVED tables (#1012) but fall back
        // here otherwise. KEPT (per #770) as the honest fallback path.
        // ===================================================================

        /// <summary>
        /// Honest-stub export text. Returns a banner explaining the limitation
        /// followed by a 128-byte hex dump of the loaded address.
        /// </summary>
        public string MakeStubExportText(string formatName)
        {
            var sb = new StringBuilder();
            sb.AppendLine("# " + formatName + " export — Avalonia stub.");
            sb.AppendLine("# This Avalonia editor shows a hex-byte dump only.");
            sb.AppendLine("# The WinForms editor produces struct-aware "
                + formatName + " output (with field names and types).");
            sb.AppendLine("# Tracking issue: follow-up to #439.");
            sb.AppendLine();
            sb.AppendLine("Address: " + U.To0xHexString(CurrentAddr));
            sb.AppendLine();
            ROM? rom = CoreState.ROM;
            if (rom == null || rom.Data == null)
            {
                sb.AppendLine("# No ROM loaded.");
                return sb.ToString();
            }
            uint addr = CurrentAddr;
            const int DumpLen = 128;
            for (int i = 0; i < DumpLen; i += 16)
            {
                if (addr + i + 16 > (uint)rom.Data.Length) break;
                sb.Append(U.ToHexString8(addr + (uint)i));
                sb.Append(": ");
                for (int j = 0; j < 16; j++)
                {
                    sb.Append(U.ToHexString2(rom.u8(addr + (uint)(i + j))));
                    sb.Append(' ');
                }
                sb.AppendLine();
            }
            return sb.ToString();
        }
    }
}
