using System;
using System.Collections.Generic;
using System.IO;

// Annotation-only nullable context: lets Write(Undo.UndoData? undoData) declare the nullable
// contract (the method is a no-op when null) without enabling whole-file null-flow warnings.
#nullable enable annotations

namespace FEBuilderGBA.Avalonia.ViewModels
{
    /// <summary>
    /// "Other In-ROM Literal Text" editor — port of WinForms <c>OtherTextForm</c>.
    /// Edits miscellaneous hardcoded/literal in-ROM strings (outside the main Huffman text table).
    /// The entry list comes from the version-specific <c>other_text_</c> config (one pointer-slot
    /// address per line); each slot holds a p32 to a null-terminated system-encoded string. Writing
    /// re-encodes the edited text, appends it to free space, and repoints the slot (mirrors WF
    /// <c>InputFormRef.WriteBinaryDataPointer</c>).
    /// </summary>
    public class OtherTextViewModel : ViewModelBase
    {
        uint _currentAddr;
        bool _isLoaded;
        uint _stringAddr;
        string _text = "";
        int _byteLength;

        /// <summary>The config-listed pointer-slot address (holds a p32 to the string).</summary>
        public uint CurrentAddr { get => _currentAddr; set => SetField(ref _currentAddr, value); }
        public bool IsLoaded { get => _isLoaded; set => SetField(ref _isLoaded, value); }
        /// <summary>Dereferenced string address (<c>p32(CurrentAddr)</c>).</summary>
        public uint StringAddr { get => _stringAddr; set => SetField(ref _stringAddr, value); }
        /// <summary>The decoded editable string.</summary>
        public string Text { get => _text; set => SetField(ref _text, value); }
        /// <summary>Byte length of the stored string (excluding the terminator).</summary>
        public int ByteLength { get => _byteLength; set => SetField(ref _byteLength, value); }

        public List<AddrResult> LoadList()
        {
            ROM rom = CoreState.ROM;
            if (rom?.RomInfo == null) return new List<AddrResult>();

            var result = new List<AddrResult>();
            string file = U.ConfigDataFilename("other_text_");
            if (!U.IsRequiredFileExist(file)) return result;

            string[] lines;
            try { lines = File.ReadAllLines(file); }
            catch { return result; }

            foreach (string raw in lines)
            {
                // Per-line guard: one malformed/EOF-adjacent entry must not abort the whole list.
                try
                {
                    if (U.IsComment(raw) || U.OtherLangLine(raw)) continue;
                    string line = U.ClipComment(raw).Trim();
                    if (line == "") continue;

                    uint addr = U.toOffset(U.atoh(line));
                    // p32 reads 4 bytes, so require addr+4 in bounds (isSafetyOffset only checks addr).
                    if (!U.isSafetyOffset(addr) || (ulong)addr + 4 > (ulong)rom.Data.Length) continue;

                    uint pStr = rom.p32(addr);
                    string preview = U.isSafetyOffset(pStr)
                        ? U.ToHexString(pStr) + " " + rom.getString(pStr)
                        : U.ToHexString(pStr);
                    result.Add(new AddrResult(addr, preview, pStr));
                }
                catch
                {
                    // Skip this line; keep parsing the rest.
                }
            }
            return result;
        }

        public void LoadEntry(uint addr)
        {
            ROM rom = CoreState.ROM;
            if (rom == null) return;
            if (!U.isSafetyOffset(addr) || (ulong)addr + 4 > (ulong)rom.Data.Length) return;

            CurrentAddr = addr;
            uint cAddr = rom.p32(addr);
            if (!U.isSafetyOffset(cAddr))
            {
                StringAddr = cAddr;
                Text = "";
                ByteLength = 0;
                IsLoaded = true;
                return;
            }

            string str = rom.getString(cAddr, out int length);
            StringAddr = cAddr;
            Text = str;
            ByteLength = length;
            IsLoaded = true;
        }

        /// <summary>
        /// Re-encode <see cref="Text"/>, append it to free space, and repoint the slot at
        /// <see cref="CurrentAddr"/>. Mirrors WF <c>WriteBinaryDataPointer</c>: the literal is
        /// relocated (never overwritten in place) so a longer string can never clobber adjacent
        /// data. Returns false (no mutation) without a ROM / selection / encoder / undo scope.
        /// </summary>
        public bool Write(Undo.UndoData? undoData)
        {
            ROM rom = CoreState.ROM;
            if (rom == null || CurrentAddr == 0) return false;
            if (!U.isSafetyOffset(CurrentAddr) || (ulong)CurrentAddr + 4 > (ulong)rom.Data.Length) return false;
            if (undoData == null || CoreState.SystemTextEncoder == null) return false;

            byte[] bytes = CoreState.SystemTextEncoder.Encode(Text ?? "");
            bytes = U.ArrayAppend(bytes, new byte[] { 0x00 });

            uint newAddr = AppendBinaryDataHeadless(rom, bytes, undoData);
            if (!U.isSafetyOffset(newAddr)) return false;

            // Repoint the slot to the new string location (same transaction as the append).
            rom.write_p32(CurrentAddr, newAddr, undoData);

            StringAddr = rom.p32(CurrentAddr);
            ByteLength = bytes.Length - 1;
            return true;
        }

        public int GetListCount() => LoadList().Count;

        /// <summary>
        /// Headless equivalent of InputFormRef.AppendBinaryData (mirrors AIScriptViewModel):
        /// route through the wired CoreState.AppendBinaryData free-space allocator when present,
        /// else grow the ROM and append at the old end. Returns the new data OFFSET.
        /// </summary>
        static uint AppendBinaryDataHeadless(ROM rom, byte[] buffer, Undo.UndoData undoData)
        {
            if (buffer == null || buffer.Length == 0) return U.NOT_FOUND;

            var allocator = CoreState.AppendBinaryData;
            if (allocator != null)
                return allocator(buffer, undoData);

            uint appendAt = (uint)rom.Data.Length;
            if (!rom.write_resize_data((uint)(appendAt + buffer.Length)))
                return U.NOT_FOUND;
            rom.write_range(appendAt, buffer, undoData);
            return appendAt;
        }
    }
}
