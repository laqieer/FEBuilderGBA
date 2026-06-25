using System;
using System.Globalization;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    public class MoveToFreeSpaceViewViewModel : ViewModelBase
    {
        bool _isLoaded;
        string _currentAddress = string.Empty;
        string _freeSpaceAddress = string.Empty;
        string _dataSize = string.Empty;
        string _newAddress = string.Empty;
        string _statusMessage = "Free Space Manager finds and manages unused ROM space.\nUse this tool to relocate data to free areas when expanding content.";
        string _dialogResult = "";

        public bool IsLoaded { get => _isLoaded; set => SetField(ref _isLoaded, value); }
        /// <summary>Current data address in the ROM.</summary>
        public string CurrentAddress { get => _currentAddress; set => SetField(ref _currentAddress, value); }
        /// <summary>Address of found free space.</summary>
        public string FreeSpaceAddress { get => _freeSpaceAddress; set => SetField(ref _freeSpaceAddress, value); }
        /// <summary>Size of data to be moved (in bytes).</summary>
        public string DataSize { get => _dataSize; set => SetField(ref _dataSize, value); }
        /// <summary>New destination address after move.</summary>
        public string NewAddress { get => _newAddress; set => SetField(ref _newAddress, value); }
        public string StatusMessage { get => _statusMessage; set => SetField(ref _statusMessage, value); }
        public string DialogResult { get => _dialogResult; set => SetField(ref _dialogResult, value); }

        public void Initialize()
        {
            IsLoaded = true;
        }

        /// <summary>
        /// Scan the ROM for a contiguous block of free space (0xFF or 0x00 bytes).
        /// Returns the start address of the block, or null if not found.
        /// </summary>
        public uint? FindFreeSpace(uint requestedSize, byte fillByte = 0xFF)
        {
            var rom = CoreState.ROM;
            if (rom == null || requestedSize == 0) return null;
            uint consecutive = 0;
            for (uint i = 0; i < (uint)rom.Data.Length; i++)
            {
                if (rom.u8(i) == fillByte || rom.u8(i) == 0x00)
                {
                    consecutive++;
                    if (consecutive >= requestedSize)
                        return i - requestedSize + 1;
                }
                else
                {
                    consecutive = 0;
                }
            }
            return null;
        }

        /// <summary>Parse a hex string (with optional 0x prefix) into a uint.</summary>
        static bool TryParseHex(string input, out uint value)
        {
            value = 0;
            if (string.IsNullOrWhiteSpace(input)) return false;
            string text = input.Trim();
            if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                text = text.Substring(2);
            return uint.TryParse(text, NumberStyles.HexNumber, null, out value);
        }

        /// <summary>
        /// Execute the move operation: find free space for the requested size,
        /// update FreeSpaceAddress/NewAddress, and copy data + update pointer.
        /// </summary>
        public void ExecuteMove()
        {
            var rom = CoreState.ROM;
            if (rom == null)
            {
                StatusMessage = "No ROM loaded.";
                return;
            }

            if (!TryParseHex(DataSize, out uint size) || size == 0)
            {
                StatusMessage = "Invalid or zero data size.";
                return;
            }

            if (!TryParseHex(CurrentAddress, out uint srcAddr))
            {
                StatusMessage = "Invalid current address.";
                return;
            }

            uint? freeAddr = FindFreeSpace(size);
            if (freeAddr == null)
            {
                StatusMessage = $"Could not find {size} bytes of free space.";
                FreeSpaceAddress = "";
                NewAddress = "";
                return;
            }

            uint dst = freeAddr.Value;
            FreeSpaceAddress = $"0x{dst:X08}";

            // Copy data from source to destination
            if (srcAddr + size <= (uint)rom.Data.Length && dst + size <= (uint)rom.Data.Length)
            {
                for (uint i = 0; i < size; i++)
                    rom.write_u8(dst + i, rom.u8(srcAddr + i));

                // Repoint every reference to the moved block BEFORE the
                // destructive source clear. Mirrors WinForms
                // MoveToFreeSapceForm.RunButton_Click (repoint, then clear).
                // Pass undo=null so the writes record into the ambient
                // ROM.BeginUndoScope already opened by Move_Click — copy +
                // repoint + clear are one undo group. The helper rescans raw
                // 32-bit pointers AND ARM-Thumb LDR literal-pool loads
                // (mirror of WF GrepPointerAll + GrepPointerAllOnLDR), de-dups,
                // danger-zone-gates each slot, and write_p32's each one.
                // KnownGap vs WF SearchPointer: the event-aware
                // GrepPointerAllOnEvent pass and the IsFixedASM confirmation
                // are intentionally out of Core scope (InputFormRef-dependent).
                int refs = DataExpansionCore.RepointAllReferences(rom, srcAddr, dst, null);

                if (refs <= 0)
                {
                    // No references found. Refuse the destructive clear — wiping
                    // the source now would orphan the moved block (the very
                    // corruption this tool exists to prevent). Mirrors the WF
                    // "no pointers found — dangerous, stop" guard (RunButton_Click
                    // lines 732-742); the headless VM has no confirm dialog, so
                    // refuse-and-warn is the safe default. The block was copied
                    // into free space but the source is preserved, so the ROM
                    // stays valid.
                    NewAddress = $"0x{dst:X08}";
                    StatusMessage =
                        $"Copied {size} bytes from 0x{srcAddr:X08} to 0x{dst:X08}, " +
                        "but found NO references to repoint. The source was NOT cleared " +
                        "to avoid corrupting the ROM. This data may be unreferenced or " +
                        "referenced in a way this tool cannot detect (event scripts / ASM).";
                    DialogResult = "NoReferences";
                    return;
                }

                // References were repointed — now it is safe to clear the source.
                for (uint i = 0; i < size; i++)
                    rom.write_u8(srcAddr + i, 0xFF);

                // Relocate per-row lint/comment cache keys so they follow the
                // moved block. Mirrors WF MoveToFreeSapceForm.RepointEtcData.
                // Forward-only (ROM undo does not reverse this — matches WF).
                CoreState.LintCache?.RepointEtcData(srcAddr, size, dst);
                CoreState.CommentCache?.RepointEtcData(srcAddr, size, dst);

                NewAddress = $"0x{dst:X08}";
                StatusMessage =
                    $"Moved {size} bytes from 0x{srcAddr:X08} to 0x{dst:X08}; " +
                    $"repointed {refs} reference{(refs == 1 ? "" : "s")}.";
                DialogResult = "Moved";
            }
            else
            {
                StatusMessage = "Address range exceeds ROM bounds.";
            }
        }
    }
}
