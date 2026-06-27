using System;
using System.Collections.Generic;
using FEBuilderGBA.Avalonia.Services;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    /// <summary>
    /// Map Terrain Name editor ViewModel (multibyte / JP sub-view of
    /// <c>TerrainNameEditorView</c>).
    ///
    /// <para>On multibyte (Japanese) ROMs the terrain-name table holds 4-byte
    /// POINTERS to NUL-terminated, system-encoded name strings. Previously this VM
    /// only dereferenced the pointer and surfaced a read-only <c>-&gt; 0x........</c>
    /// label, and its <c>Write()</c> wrote the raw <c>u32</c> pointer — so the
    /// terrain name could not be edited at all (#1601). It now decodes the string
    /// into an EDITABLE <see cref="TerrainName"/> field, shows real decoded names in
    /// the list, and writes the edited string back (encode + append + repoint the
    /// <c>+i*4</c> slot), faithfully porting WinForms <c>MapTerrainNameForm</c>.</para>
    ///
    /// <para>The write-back reuses the proven, fault-safe pattern from the sibling
    /// <see cref="TerrainNameEditorViewModel"/>: a shared-pointer guard (#929) so a
    /// co-owned old string region is never recycled, a defensive snapshot +
    /// length-aware byte-identical restore on any fault (#885/#923), and
    /// <see cref="RecycleAddress.WriteAndWritePointerAmbient"/> so every write
    /// composes into the View's ambient undo scope.</para>
    /// </summary>
    public class MapTerrainNameViewModel : ViewModelBase, IDataVerifiable
    {
        uint _currentAddr;
        bool _isLoaded;
        uint _terrainNamePointer;
        string _terrainName = "";

        public uint CurrentAddr { get => _currentAddr; set => SetField(ref _currentAddr, value); }
        public bool IsLoaded { get => _isLoaded; set => SetField(ref _isLoaded, value); }

        /// <summary>Raw 32-bit pointer value stored in the selected slot. READ-ONLY
        /// diagnostics — the editable surface is <see cref="TerrainName"/>; the
        /// pointer is repointed automatically by <see cref="Write"/>.</summary>
        public uint TerrainNamePointer { get => _terrainNamePointer; set => SetField(ref _terrainNamePointer, value); }

        /// <summary>The decoded, EDITABLE terrain name string (multibyte case).</summary>
        public string TerrainName { get => _terrainName; set => SetField(ref _terrainName, value); }

        public List<AddrResult> LoadList()
        {
            ROM rom = CoreState.ROM;
            if (rom?.RomInfo == null) return new List<AddrResult>();

            uint pointer = rom.RomInfo.map_terrain_name_pointer;
            if (pointer == 0) return new List<AddrResult>();

            uint baseAddr = rom.p32(pointer);
            if (!U.isSafetyOffset(baseAddr, rom)) return new List<AddrResult>();

            // Multibyte (Japanese) ROMs: 4-byte pointer entries. The stop rule mirrors
            // WinForms MapTerrainNameForm.Init's read callback U.isPointerOrNULL(u32(addr)).
            const uint blockSize = 4;
            uint maxCount = MaxCount(rom);

            var result = new List<AddrResult>();
            for (uint i = 0; i < maxCount; i++)
            {
                uint addr = baseAddr + i * blockSize;
                if (addr + blockSize > (uint)rom.Data.Length) break;

                uint ptr = rom.u32(addr);
                if (!U.isPointerOrNULL(ptr)) break;

                // Decode the actual name string (WinForms list shows getString(p32(addr))).
                string name = DecodeName(rom, ptr);
                result.Add(new AddrResult(addr, $"0x{i:X2} {name}", i));
            }
            return result;
        }

        public void LoadEntry(uint addr)
        {
            ROM rom = CoreState.ROM;
            if (rom == null) return;
            if (addr + 4 > (uint)rom.Data.Length) return;

            CurrentAddr = addr;
            TerrainNamePointer = rom.u32(addr);
            TerrainName = DecodeName(rom, TerrainNamePointer);
            IsLoaded = true;
        }

        /// <summary>The number of 4-byte slots the terrain-name table is bounded to.
        /// Single source of truth shared by <see cref="LoadList"/> and
        /// <see cref="IsOldStringShared"/> so both enumerate the SAME range (mirrors
        /// WinForms <c>map_terrain_type_count</c> with the 65 fallback).</summary>
        static uint MaxCount(ROM rom)
        {
            uint maxCount = rom.RomInfo.map_terrain_type_count;
            if (maxCount == 0) maxCount = 65;
            return maxCount;
        }

        /// <summary>Decode the NUL-terminated string a slot pointer references.
        /// Returns "" for a NULL/non-pointer or an unsafe target (never throws).</summary>
        static string DecodeName(ROM rom, uint rawPtr)
        {
            if (!U.isPointer(rawPtr)) return "";
            uint strOff = U.toOffset(rawPtr);
            if (!U.isSafetyOffset(strOff, rom)) return "";
            try { return rom.getString(strOff); }
            catch { return ""; }
        }

        /// <summary>
        /// Persist the edited <see cref="TerrainName"/> string back to ROM
        /// (multibyte write-back): encode via the system text encoder, append a
        /// NUL, allocate it in free space and repoint the 4-byte slot at
        /// <see cref="CurrentAddr"/>. Must be called inside an active ambient undo
        /// scope (the View opens one via <c>UndoService.Begin</c>); every write is
        /// ambient-recorded so a single Undo reverts the whole operation.
        /// </summary>
        public void Write()
        {
            ROM rom = CoreState.ROM;
            if (rom == null || CurrentAddr == 0) return;

            // Encode the human string -> raw bytes + NUL terminator (WF :169-170).
            byte[] strbytes = CoreState.SystemTextEncoder.Encode(TerrainName ?? "");
            strbytes = U.ArrayAppend(strbytes, new byte[] { 0x00 });

            uint slot = CurrentAddr;
            uint oldRawPtr = rom.u32(slot);

            // ----- shared-pointer guard (#929) -----------------------------
            // The OLD string region is recyclable ONLY when the slot currently
            // points at a real string AND no OTHER terrain slot points at the SAME
            // offset (a co-owning entry would still reference those bytes).
            var recycle = new List<Address>();
            if (U.isPointer(oldRawPtr))
            {
                uint oldStrOff = U.toOffset(oldRawPtr);
                if (U.isSafetyOffset(oldStrOff, rom) && !IsOldStringShared(rom, slot, oldRawPtr))
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
                        // Old bytes don't decode cleanly (malformed encoding): skip
                        // recycling rather than abort — conservatively leak the old
                        // region; the new string is still allocated + repointed.
                        recycle.Clear();
                    }
                }
            }

            // ----- defensive snapshot (#885) -------------------------------
            byte[] snapshot = (byte[])rom.Data.Clone();
            try
            {
                var ra = new RecycleAddress(recycle);
                uint newOff = ra.WriteAndWritePointerAmbient(slot, strbytes);
                if (newOff == U.NOT_FOUND)
                    throw new InvalidOperationException(
                        "Terrain name write failed: no free space to allocate the string.");

                // Refresh the diagnostics pointer from the (possibly repointed) slot.
                TerrainNamePointer = rom.u32(slot);
            }
            catch (Exception)
            {
                // Restore byte-for-byte. Length-aware: undo any grow first so trailing
                // grown bytes can't survive (#923 H1 parity).
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
            if (!U.isSafetyOffset(baseAddr, rom)) return true;

            const uint blockSize = 4;
            uint maxCount = MaxCount(rom); // same bounded range as LoadList.
            for (uint i = 0; i < maxCount; i++)
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

        public int GetListCount() => LoadList().Count;

        public Dictionary<string, string> GetDataReport()
        {
            return new Dictionary<string, string>
            {
                ["addr"] = $"0x{CurrentAddr:X08}",
                ["TerrainNamePointer"] = $"0x{TerrainNamePointer:X08}",
                ["TerrainName"] = TerrainName ?? "",
            };
        }

        public Dictionary<string, string> GetRawRomReport()
        {
            ROM rom = CoreState.ROM;
            if (rom == null || CurrentAddr == 0) return new Dictionary<string, string>();
            uint a = CurrentAddr;
            return new Dictionary<string, string>
            {
                ["addr"] = $"0x{a:X08}",
                ["TerrainNamePointer@0x00"] = $"0x{rom.u32(a):X08}",
            };
        }

        public Dictionary<string, string> GetFieldOffsetMap() => new()
        {
            ["TerrainNamePointer"] = "TerrainNamePointer@0x00",
        };
    }
}
