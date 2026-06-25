using System.Collections.Generic;
using FEBuilderGBA.Avalonia.Services;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    /// <summary>
    /// C-String editor ViewModel. Real port of WinForms <c>CStringForm</c> — a
    /// pointer-bound field helper that reads + writes a NUL-terminated,
    /// system-encoded C-string at an arbitrary ROM address. Backed by
    /// <see cref="CStringCore"/> for all ROM I/O. Manual-address sub-editor (no
    /// enumerable list), mirroring <see cref="AOERANGEViewModel"/>.
    /// </summary>
    public class CStringViewModel : ViewModelBase, IDataVerifiable
    {
        uint _currentAddr;
        uint _parentPointerSlot;
        bool _isLoaded;
        string _text = string.Empty;
        string _status = string.Empty;

        /// <summary>OFFSET of the C-string currently loaded (0 = none).</summary>
        public uint CurrentAddr { get => _currentAddr; set => SetField(ref _currentAddr, value); }

        /// <summary>
        /// ROM offset of the parent pointer slot that references this string, when
        /// the editor was reached from a parent (the WinForms <c>Numeric</c>
        /// NumericUpDown). 0 in the standalone manual-address path.
        /// </summary>
        public uint ParentPointerSlot { get => _parentPointerSlot; set => SetField(ref _parentPointerSlot, value); }

        public bool IsLoaded { get => _isLoaded; set => SetField(ref _isLoaded, value); }

        /// <summary>The editable C-string text (escape tokens kept verbatim).</summary>
        public string Text { get => _text; set => SetField(ref _text, value); }

        /// <summary>Last write/status message (success or refusal reason).</summary>
        public string Status { get => _status; set => SetField(ref _status, value); }

        /// <summary>
        /// Decode the C-string at <paramref name="addr"/> into <see cref="Text"/>.
        /// On an unsafe/oob address the text is cleared and <see cref="IsLoaded"/>
        /// stays false (no throw).
        /// </summary>
        public void LoadEntry(uint addr)
        {
            ROM rom = CoreState.ROM;
            if (rom == null) return;

            uint offset = U.toOffset(addr);
            if (!U.isSafetyPointer(addr, rom) && !U.isSafetyOffset(offset, rom))
            {
                CurrentAddr = offset;
                Text = string.Empty;
                IsLoaded = false;
                Status = $"No valid C-string at 0x{offset:X08}.";
                return;
            }

            CurrentAddr = offset;
            Text = CStringCore.ReadCString(rom, addr);
            IsLoaded = true;
            Status = $"Loaded 0x{CurrentAddr:X08}.";
        }

        /// <summary>
        /// Persist <see cref="Text"/> via <see cref="CStringCore.WriteCString"/>.
        /// On a move the returned (new) offset is adopted into
        /// <see cref="CurrentAddr"/>. <see cref="Status"/> is set in every case.
        /// </summary>
        /// <returns><c>true</c> when a mutation was performed (in-place or move).</returns>
        public bool Write()
        {
            ROM rom = CoreState.ROM;
            if (rom == null)
            {
                Status = "ROM not loaded.";
                return false;
            }
            // Refuse unless we have a valid target. A successful load (IsLoaded)
            // gives a real CurrentAddr to write to. A parent pointer slot (e.g. a
            // NULL CSTRING pointer reached from a field jump) authorises a FRESH
            // APPEND even with no string loaded. Without EITHER, refuse: writing
            // against a failed-load CurrentAddr could edit ROM at a bad address.
            if (!IsLoaded && ParentPointerSlot == 0)
            {
                Status = "No valid C-string loaded. Enter a valid address and Reload first.";
                return false;
            }

            CStringCore.WriteResult r = CStringCore.WriteCString(
                rom, ParentPointerSlot, CurrentAddr, Text ?? string.Empty);

            switch (r.Status)
            {
                case CStringCore.WriteStatus.InPlace:
                    Status = $"Wrote in place at 0x{r.Address:X08}.";
                    return true;
                case CStringCore.WriteStatus.Moved:
                    CurrentAddr = r.Address;
                    IsLoaded = true;
                    Status = $"Moved to 0x{r.Address:X08}; repointed {r.RepointedSlots} reference(s).";
                    return true;
                default:
                    Status = r.Message;
                    return false;
            }
        }

        public int GetListCount() => IsLoaded && CurrentAddr != 0 ? 1 : 0;

        public Dictionary<string, string> GetDataReport()
        {
            return new Dictionary<string, string>
            {
                ["addr"] = $"0x{CurrentAddr:X08}",
                ["Text"] = Text ?? string.Empty,
                ["Length"] = $"0x{(Text?.Length ?? 0):X04}",
            };
        }

        public Dictionary<string, string> GetFieldOffsetMap()
        {
            return new Dictionary<string, string>
            {
                ["Text"] = "cstring@0x00_NulTerminated",
            };
        }

        public Dictionary<string, string> GetRawRomReport()
        {
            ROM rom = CoreState.ROM;
            if (rom == null || CurrentAddr == 0) return new Dictionary<string, string>();
            if (!U.isSafetyOffset(CurrentAddr, rom)) return new Dictionary<string, string>();

            int length;
            rom.getString(CurrentAddr, out length);
            return new Dictionary<string, string>
            {
                ["addr"] = $"0x{CurrentAddr:X08}",
                ["byteLength"] = $"0x{(uint)(length + 1):X04}",
            };
        }
    }
}
