using System;
using System.Collections.Generic;
using FEBuilderGBA.Avalonia.Services;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    /// <summary>
    /// Terrain Name editor ViewModel (#5 of #943).
    ///
    /// The terrain-name table is stored in TWO completely different data models
    /// depending on the ROM's text encoding, mirroring WinForms:
    ///
    ///  * <b>Multibyte (JP: FE6/FE7J/FE8J)</b> — <c>MapTerrainNameForm</c>:
    ///    BlockSize = 4, each entry is a 32-bit POINTER to a raw, NUL-terminated
    ///    string. The list-stop rule is <c>U.isPointerOrNULL(u32(addr))</c>.
    ///  * <b>Non-multibyte (US/EU: FE7U/FE8U)</b> — <c>MapTerrainNameEngForm</c>:
    ///    BlockSize = 2, each entry is a 16-bit Huffman text ID. The list-stop
    ///    rule is <c>textId == 0</c>.
    ///
    /// The previous Avalonia VM ALWAYS read 2-byte text IDs, which produced
    /// garbage on multibyte ROMs (e.g. FE8J showed "づっ共聖…"). This VM now
    /// switches on <see cref="ROM.RomInfo"/>.<c>is_multibyte</c> exactly like the
    /// authoritative WinForms forms and like
    /// <see cref="MoveCostEditorViewModel.LoadTerrainNames"/>.
    /// </summary>
    public class TerrainNameEditorViewModel : ViewModelBase, IDataVerifiable
    {
        uint _currentAddr;
        string _terrainName = "";
        uint _textId;
        bool _canWrite;
        bool _isMultibyte;

        /// <summary>The address of the currently selected slot. For multibyte
        /// ROMs this is the 4-byte pointer slot; for non-multibyte it is the
        /// 2-byte text-ID slot.</summary>
        public uint CurrentAddr { get => _currentAddr; set => SetField(ref _currentAddr, value); }

        /// <summary>Decoded terrain name string. Editable + written back for the
        /// multibyte case; a read-only decoded preview for the non-multibyte
        /// case.</summary>
        public string TerrainName { get => _terrainName; set => SetField(ref _terrainName, value); }

        /// <summary>Text ID (non-multibyte case only; W0 / u16 @ 0x00).</summary>
        public uint TextId { get => _textId; set => SetField(ref _textId, value); }

        public bool CanWrite { get => _canWrite; set => SetField(ref _canWrite, value); }

        /// <summary>True when the loaded ROM uses the 4-byte string-pointer data
        /// model (JP). Drives both the read/write branch and the View's field
        /// visibility.</summary>
        public bool IsMultibyte { get => _isMultibyte; set => SetField(ref _isMultibyte, value); }

        public List<AddrResult> LoadTerrainNameList()
        {
            ROM rom = CoreState.ROM;
            if (rom?.RomInfo == null) return new List<AddrResult>();

            IsMultibyte = rom.RomInfo.is_multibyte;

            uint ptr = rom.RomInfo.map_terrain_name_pointer;
            if (ptr == 0) return new List<AddrResult>();

            uint baseAddr = rom.p32(ptr);
            if (!U.isSafetyOffset(baseAddr)) return new List<AddrResult>();

            var result = new List<AddrResult>();

            if (IsMultibyte)
            {
                // JP: 4-byte entries, each a pointer to a raw string.
                // Stop rule mirrors MapTerrainNameForm Init's read-stop callback
                // U.isPointerOrNULL(Program.ROM.u32(addr + 0)).
                const uint blockSize = 4;
                for (uint i = 0; i < 0x100; i++)
                {
                    uint addr = baseAddr + i * blockSize;
                    if (addr + blockSize > (uint)rom.Data.Length) break;

                    uint rawPtr = rom.u32(addr);
                    if (!U.isPointerOrNULL(rawPtr)) break;

                    string decoded = "";
                    if (U.isPointer(rawPtr))
                    {
                        uint strOff = U.toOffset(rawPtr);
                        if (U.isSafetyOffset(strOff))
                        {
                            try { decoded = rom.getString(strOff); }
                            catch { decoded = ""; }
                        }
                    }

                    string name = U.ToHexString(i) + " " + decoded;
                    result.Add(new AddrResult(addr, name, i));
                }
            }
            else
            {
                // US/EU: 2-byte entries, each a Huffman text ID. Mirrors
                // MapTerrainNameEngViewModel.LoadList(): terminate on textId == 0.
                const uint blockSize = 2;
                for (uint i = 0; i < 0x100; i++)
                {
                    uint addr = baseAddr + i * blockSize;
                    if (addr + blockSize > (uint)rom.Data.Length) break;

                    uint textId = rom.u16(addr);
                    if (textId == 0x0000) break;

                    string decoded;
                    try { decoded = NameResolver.GetTextById(textId); }
                    catch { decoded = "???"; }

                    string name = U.ToHexString(i) + " " + decoded;
                    result.Add(new AddrResult(addr, name, i));
                }
            }

            return result;
        }

        public void LoadTerrainName(uint addr)
        {
            ROM rom = CoreState.ROM;
            if (rom?.RomInfo == null) return;

            bool multibyte = rom.RomInfo.is_multibyte;

            // Validate bounds BEFORE committing ANY state (#945 review): an
            // out-of-range addr must leave the VM unchanged (no stale CurrentAddr
            // / CanWrite from a previous selection) so the report helpers
            // (GetRawRomReport, etc.) can't perform out-of-bounds reads.
            uint need = multibyte ? 4u : 2u;
            if (addr + need > (uint)rom.Data.Length) return;

            IsMultibyte = multibyte;
            CurrentAddr = addr;

            if (multibyte)
            {
                uint rawPtr = rom.u32(addr);
                string decoded = "";
                if (U.isPointer(rawPtr))
                {
                    uint strOff = U.toOffset(rawPtr);
                    if (U.isSafetyOffset(strOff))
                    {
                        try { decoded = rom.getString(strOff); }
                        catch { decoded = ""; }
                    }
                }
                TerrainName = decoded;
                TextId = 0;
            }
            else
            {
                TextId = rom.u16(addr);
                try { TerrainName = NameResolver.GetTextById(TextId); }
                catch { TerrainName = "???"; }
            }

            CanWrite = true;
        }

        public void WriteTerrainName()
        {
            ROM rom = CoreState.ROM;
            if (rom == null || CurrentAddr == 0) return;

            if (!IsMultibyte)
            {
                // Non-multibyte: a plain 2-byte text-ID overwrite (unchanged).
                rom.write_u16(CurrentAddr, TextId);
                return;
            }

            WriteTerrainNameMultibyte(rom);
        }

        /// <summary>
        /// Multibyte string write-back. Mirrors WinForms
        /// <c>MapTerrainNameForm.TextWriteButton_Click</c>: encode the string via
        /// the system text encoder, append a NUL, allocate it in free space and
        /// repoint the 4-byte slot. We use <see cref="RecycleAddress"/>'s ambient
        /// overload so every write composes into the ambient undo scope the View
        /// already opened (<see cref="UndoService.Begin"/> calls
        /// <c>ROM.BeginUndoScope</c>, which is NON-reentrant — opening a second
        /// scope here would clobber the View's scope, so we MUST NOT).
        ///
        /// <para><b>Shared-pointer guard (#929):</b> before recycling the old
        /// string region we check every OTHER slot in the table. If any other
        /// slot points to the SAME old string offset, the old region is shared
        /// and MUST NOT be freed (a co-owning entry still references those bytes)
        /// — we repoint WITHOUT seeding the recycle pool.</para>
        ///
        /// <para><b>Defensive snapshot (#885):</b> <c>rom.Data</c> is cloned
        /// BEFORE any mutation. On ANY exception during the write the snapshot is
        /// restored in-place (length-aware: down-resize first if the ROM grew)
        /// so a fault leaves the ROM byte-identical, then the exception is
        /// rethrown for the View's catch.</para>
        /// </summary>
        void WriteTerrainNameMultibyte(ROM rom)
        {
            // Encode the human string -> raw bytes + NUL terminator (WF :169-170).
            byte[] strbytes = CoreState.SystemTextEncoder.Encode(TerrainName ?? "");
            strbytes = U.ArrayAppend(strbytes, new byte[] { 0x00 });

            // The 4-byte slot we are repointing.
            uint slot = CurrentAddr;
            uint oldRawPtr = rom.u32(slot);

            // ----- shared-pointer guard (#929) -----------------------------
            // Determine whether the OLD string region can be safely recycled.
            // It is recyclable ONLY when the slot currently points at a real
            // string AND no OTHER terrain slot points at the SAME offset.
            var recycle = new List<Address>();
            if (U.isPointer(oldRawPtr))
            {
                uint oldStrOff = U.toOffset(oldRawPtr);
                if (U.isSafetyOffset(oldStrOff) && !IsOldStringShared(rom, slot, oldRawPtr))
                {
                    try
                    {
                        int oldLen;
                        rom.getString(oldStrOff, out oldLen); // length excludes the NUL
                        uint freeLen = (uint)oldLen + 1;       // include the NUL terminator
                        Address.AddAddress(recycle, oldStrOff, freeLen, slot,
                            "TerrainName", Address.DataTypeEnum.BIN);
                    }
                    catch
                    {
                        // The old bytes don't decode as a clean string (malformed /
                        // unknown encoding). Rather than abort the whole write-back,
                        // skip recycling this region — conservatively leak it (the
                        // new string is still allocated + the slot repointed). The
                        // recycle pool is left empty so no freed range is seeded.
                        recycle.Clear();
                    }
                }
            }

            // ----- defensive snapshot (#885) -------------------------------
            byte[] snapshot = (byte[])rom.Data.Clone();
            try
            {
                // recycle may be empty (shared / NULL old slot) — then the write
                // simply allocates fresh free space and never frees the old bytes.
                var ra = new RecycleAddress(recycle);
                uint newOff = ra.WriteAndWritePointerAmbient(slot, strbytes);
                if (newOff == U.NOT_FOUND)
                    throw new InvalidOperationException(
                        "Terrain name write failed: no free space to allocate the string.");
            }
            catch (Exception)
            {
                // Restore byte-for-byte. Length-aware: a grow via RecycleAddress
                // (write_resize_data) must be undone before the in-place copy so
                // trailing grown bytes can't survive (#923 H1 parity).
                if (rom.Data.Length != snapshot.Length)
                    rom.write_resize_data((uint)snapshot.Length);
                Array.Copy(snapshot, rom.Data, snapshot.Length);
                throw;
            }
        }

        /// <summary>
        /// True when any terrain slot OTHER than <paramref name="excludeSlot"/>
        /// references the same raw pointer value <paramref name="oldRawPtr"/>
        /// (i.e. the old string region is co-owned and must not be recycled).
        /// Walks the whole 4-byte table using the same stop rule as the list.
        /// </summary>
        static bool IsOldStringShared(ROM rom, uint excludeSlot, uint oldRawPtr)
        {
            if (rom?.RomInfo == null) return true; // be conservative
            uint ptr = rom.RomInfo.map_terrain_name_pointer;
            if (ptr == 0) return true;
            uint baseAddr = rom.p32(ptr);
            if (!U.isSafetyOffset(baseAddr)) return true;

            const uint blockSize = 4;
            for (uint i = 0; i < 0x100; i++)
            {
                uint addr = baseAddr + i * blockSize;
                if (addr + blockSize > (uint)rom.Data.Length) break;

                uint rawPtr = rom.u32(addr);
                if (!U.isPointerOrNULL(rawPtr)) break;

                if (addr == excludeSlot) continue;
                if (rawPtr == oldRawPtr) return true;
            }
            return false;
        }

        public int GetListCount() => LoadTerrainNameList().Count;

        public Dictionary<string, string> GetDataReport()
        {
            if (IsMultibyte)
            {
                return new Dictionary<string, string>
                {
                    ["addr"] = $"0x{CurrentAddr:X08}",
                    ["W0_Name"] = TerrainName ?? "",
                };
            }
            return new Dictionary<string, string>
            {
                ["addr"] = $"0x{CurrentAddr:X08}",
                ["W0_TextId"] = $"0x{TextId:X04}",
            };
        }

        public Dictionary<string, string> GetRawRomReport()
        {
            ROM rom = CoreState.ROM;
            if (rom == null || CurrentAddr == 0) return new Dictionary<string, string>();

            uint a = CurrentAddr;
            if (IsMultibyte)
            {
                return new Dictionary<string, string>
                {
                    ["addr"] = $"0x{a:X08}",
                    ["p32@0x00"] = $"0x{rom.u32(a + 0):X08}",
                };
            }
            return new Dictionary<string, string>
            {
                ["addr"] = $"0x{a:X08}",
                ["u16@0x00"] = $"0x{rom.u16(a + 0):X04}",
            };
        }

        public Dictionary<string, string> GetFieldOffsetMap()
        {
            if (IsMultibyte)
            {
                return new Dictionary<string, string>
                {
                    ["W0_Name"] = "p32@0x00",
                };
            }
            return new Dictionary<string, string>
            {
                ["W0_TextId"] = "u16@0x00",
            };
        }
    }
}
