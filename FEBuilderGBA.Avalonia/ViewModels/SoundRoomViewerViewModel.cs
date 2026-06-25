using System;
using System.Collections.Generic;
using FEBuilderGBA.Avalonia.Services;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    public class SoundRoomViewerViewModel : ViewModelBase, IDataVerifiable
    {
        // Fields: D0=SongId, D4=Raw4, D8=Raw8, D12=TextId (only if dataSize>=16)
        static readonly List<EditorFormRef.FieldDef> _fields12 =
            EditorFormRef.DetectFields(new[] { "D0", "D4", "D8" });
        static readonly List<EditorFormRef.FieldDef> _fields16 =
            EditorFormRef.DetectFields(new[] { "D0", "D4", "D8", "D12" });

        uint _currentAddr;
        bool _canWrite;
        uint _songId;
        uint _textId;
        uint _raw4, _raw8;

        // List-expansion state (#1450) — mirrors WF InputFormRef.BaseAddress /
        // DataCount and ImageMapActionAnimationViewModel.ReadStartAddress/ReadCount.
        uint _readStartAddress;
        uint _readCount;

        public uint CurrentAddr { get => _currentAddr; set => SetField(ref _currentAddr, value); }
        public bool CanWrite { get => _canWrite; set => SetField(ref _canWrite, value); }
        public uint SongId { get => _songId; set => SetField(ref _songId, value); }
        public uint TextId { get => _textId; set => SetField(ref _textId, value); }
        public uint Raw4 { get => _raw4; set => SetField(ref _raw4, value); }
        public uint Raw8 { get => _raw8; set => SetField(ref _raw8, value); }

        /// <summary>ROM offset of the sound-room table base (cached by
        /// <see cref="LoadSoundRoomList"/>). The WF <c>InputFormRef.BaseAddress</c>.</summary>
        public uint ReadStartAddress { get => _readStartAddress; set => SetField(ref _readStartAddress, value); }

        /// <summary>Number of visible sound-room rows (cached by
        /// <see cref="LoadSoundRoomList"/>). The WF <c>InputFormRef.DataCount</c>.</summary>
        public uint ReadCount { get => _readCount; set => SetField(ref _readCount, value); }

        public List<AddrResult> LoadSoundRoomList()
        {
            ROM rom = CoreState.ROM;
            if (rom?.RomInfo == null) return new List<AddrResult>();

            uint ptr = rom.RomInfo.sound_room_pointer;
            if (ptr == 0) return new List<AddrResult>();

            uint dataSize = rom.RomInfo.sound_room_datasize;
            if (dataSize == 0) return new List<AddrResult>();

            uint baseAddr = rom.p32(ptr);
            if (!U.isSafetyOffset(baseAddr)) return new List<AddrResult>();

            ReadStartAddress = baseAddr;

            // Scan up to the effective cap (vanilla 255, patched 1000) + 1 so a
            // soundroom_over255 ROM that was expanded past the old 0x200/512 ceiling
            // reloads fully (#1450 review fix #2). The 0xFFFFFFFF terminator + the
            // empty-run stop below still bound a vanilla ROM well before the cap, so
            // this is a strict superset of the previous behavior.
            uint scanCap = GetExpandsCap() + 1;

            var result = new List<AddrResult>();
            for (uint i = 0; i < scanCap; i++)
            {
                uint addr = (uint)(baseAddr + i * dataSize);
                if (addr + dataSize > (uint)rom.Data.Length) break;

                if (rom.u32(addr) == 0xFFFFFFFF) break;
                if (i > 10 && rom.IsEmpty(addr, dataSize * 10)) break;

                uint songId = rom.u16(addr);
                string songName = NameResolver.GetSongName(songId);
                string name = $"{(i + 1):D3} {songName} (0x{songId:X04})";
                result.Add(new AddrResult(addr, name, i));
            }
            ReadCount = (uint)result.Count;
            return result;
        }

        /// <summary>
        /// The list-expansion row cap: 255 (vanilla) or 1000 when the
        /// "soundroom_over255" patch is installed. Mirrors WinForms
        /// <c>SoundRoomForm</c>'s <c>AddressListExpandsButton_255</c> →
        /// <c>_1000</c> rename driven by <c>PatchUtil.Search_SoundRoomExpands</c>.
        /// </summary>
        public uint GetExpandsCap()
        {
            ROM rom = CoreState.ROM;
            return PatchDetection.SearchSoundRoomExpandsPatch(rom) ? 1000u : 255u;
        }

        public void LoadSoundRoom(uint addr)
        {
            ROM rom = CoreState.ROM;
            if (rom == null) return;

            uint dataSize = rom.RomInfo.sound_room_datasize;
            if (addr + dataSize > (uint)rom.Data.Length) return;

            CurrentAddr = addr;
            bool wide = dataSize >= 16;
            var fields = wide ? _fields16 : _fields12;
            var values = EditorFormRef.ReadFields(rom, addr, fields);
            SongId = values["D0"];
            Raw4 = values["D4"];
            Raw8 = values["D8"];
            TextId = wide ? values["D12"] : 0;
            CanWrite = true;
        }

        public void WriteSoundRoom()
        {
            ROM rom = CoreState.ROM;
            if (rom == null || CurrentAddr == 0) return;

            uint dataSize = rom.RomInfo.sound_room_datasize;
            if (CurrentAddr + dataSize > (uint)rom.Data.Length) return;

            bool wide = dataSize >= 16;
            var fields = wide ? _fields16 : _fields12;
            var values = new Dictionary<string, uint>
            {
                ["D0"] = SongId, ["D4"] = Raw4, ["D8"] = Raw8,
            };
            if (wide) values["D12"] = TextId;
            EditorFormRef.WriteFields(rom, CurrentAddr, values, fields);
        }

        public int GetListCount() => LoadSoundRoomList().Count;

        // ----------------------------------------------------------------
        // List expansion (#1450) — mirrors WinForms SoundRoomForm's
        // "List Expansion" button (AddressListExpandsButton_255 / _1000) which
        // delegates to MoveToFreeSapceForm + InputFormRef.ExpandsArea.
        // ----------------------------------------------------------------

        /// <summary>
        /// Implausibly-large repoint count guard. The sound-room table base is
        /// normally referenced from one canonical pointer (+ at most a handful of
        /// ASM/LDR sites with the over-255 patch). A flood of hundreds of "raw u32
        /// == base" matches indicates a false-positive coincidence rather than a
        /// real reference set — abort rather than corrupt. Mirrors
        /// <c>MapSettingCore.MaxPlausibleRepointSlots</c>.
        /// </summary>
        const int MaxPlausibleRepointSlots = 64;

        /// <summary>
        /// Grow the sound-room pointer table to <paramref name="newCount"/> rows.
        /// Mirrors WinForms <c>SoundRoomForm</c>'s list-expansion (the
        /// <c>MoveToFreeSapceForm</c> + <c>InputFormRef.ExpandsArea</c> path):
        /// <list type="bullet">
        ///   <item>copies the existing rows verbatim and fills every NEW row with
        ///         row 0 (<see cref="DataExpansionCore.ExpandFill.First"/>) — WF
        ///         <c>NewDataInit</c> seeds new list-expansion rows from an existing
        ///         row, NOT zeros, so the SoundRoom reload scanner's empty-run stop
        ///         does not hide them (#1450 review fix #1);</item>
        ///   <item>writes a single <c>0xFFFFFFFF</c> dword terminator (the SoundRoom
        ///         scan predicate is <c>u32(addr)==0xFFFFFFFF</c>);</item>
        ///   <item>repoints EVERY raw 32-bit + ARM-Thumb LDR literal-pool reference
        ///         to the moved base (<see cref="DataExpansionCore.ExpandRepoint.RawAndLdrAll"/>),
        ///         matching WF <c>MoveToFreeSapceForm.SearchPointer</c>.</item>
        /// </list>
        /// Validate-all-before-mutate with a defensive snapshot: on ANY fault the
        /// ROM is restored byte-identical (no partial commit) — mirrors
        /// <c>MapSettingCore.ExpandMapSettingList</c>. Caller wraps in an
        /// <c>UndoService.Begin/Commit/Rollback</c> scope and passes the active
        /// <see cref="Undo.UndoData"/>.
        /// </summary>
        /// <param name="newCount">Target row count (must be &gt;= current
        /// <see cref="ReadCount"/> and &lt;= <see cref="GetExpandsCap"/>).</param>
        /// <param name="undo">Active ambient undo buffer (may be null).</param>
        /// <returns>Empty on success, error string otherwise.</returns>
        public string ExpandList(uint newCount, Undo.UndoData? undo)
        {
            ROM rom = CoreState.ROM;
            if (rom?.RomInfo == null) return R._("ROM not loaded.");

            uint ptr = rom.RomInfo.sound_room_pointer;
            if (ptr == 0) return R._("This ROM has no sound room table.");

            uint dataSize = rom.RomInfo.sound_room_datasize;
            if (dataSize == 0) return R._("This ROM has no sound room table.");

            // Refresh ReadCount/ReadStartAddress from the live ROM so an explicit
            // VM call (no preceding LoadSoundRoomList) still has a current count.
            if (ReadStartAddress == 0)
            {
                LoadSoundRoomList();
            }

            uint cap = GetExpandsCap();
            if (newCount > cap)
                return R._("New count ({0}) exceeds the maximum ({1}).", newCount, cap);
            if (newCount < ReadCount)
                return R._("New count ({0}) must be greater than or equal to current count ({1}).",
                    newCount, ReadCount);
            if (newCount == ReadCount)
                return ""; // no-op success
            if (ReadCount == 0)
                return R._("Cannot expand: the sound room list is empty (no row 0 to copy).");

            // Defensive snapshot — guarantees a FAILED expand mutates ZERO bytes,
            // even beyond what the ambient undo scope tracks.
            byte[] snap = (byte[])rom.Data.Clone();

            try
            {
                var result = DataExpansionCore.ExpandTableTo(
                    rom, ptr, dataSize, ReadCount, newCount,
                    new DataExpansionCore.ExpandOptions
                    {
                        Fill = DataExpansionCore.ExpandFill.First,
                        Repoint = DataExpansionCore.ExpandRepoint.RawAndLdrAll,
                        FullZeroTerminatorRow = false,
                    });

                if (!result.Success)
                {
                    RestoreSnapshot(rom, snap, undo);
                    return result.Error ?? R._("Sound room table expansion failed.");
                }

                // --- Audit guard (mirrors MapSettingCore) -----------------
                var slots = result.RepointedSlots ?? System.Array.Empty<uint>();

                // (a) zero repointed slots ⇒ the canonical pointer was not even
                // found — abort with ZERO net change.
                if (slots.Count == 0)
                {
                    RestoreSnapshot(rom, snap, undo);
                    return R._("Sound room expand aborted: no references were repointed (expected at least the canonical pointer slot).");
                }

                // (b) the canonical sound_room_pointer slot MUST be among them.
                bool canonicalCovered = false;
                foreach (uint s in slots)
                {
                    if (s == ptr) { canonicalCovered = true; break; }
                }
                if (!canonicalCovered)
                {
                    RestoreSnapshot(rom, snap, undo);
                    return R._("Sound room expand aborted: the canonical pointer slot (0x{0:X}) was not among the repointed references.", ptr);
                }

                // (c) implausibly large ⇒ likely a false-positive flood — abort.
                if (slots.Count > MaxPlausibleRepointSlots)
                {
                    RestoreSnapshot(rom, snap, undo);
                    return R._("Sound room expand aborted: {0} references would be repointed, exceeding the plausible maximum ({1}) — likely a false-positive match.", slots.Count, MaxPlausibleRepointSlots);
                }

                // Refresh the read-config from the new pointer base (NOT a ROM write).
                ReadStartAddress = result.NewBaseAddress;
                ReadCount = result.NewCount;
                return "";
            }
            catch (Exception ex)
            {
                RestoreSnapshot(rom, snap, undo);
                // Log.Error joins args with spaces (no composite formatting),
                // so pass a single interpolated string + the full exception.
                Log.Error("SoundRoomViewerViewModel.ExpandList failed: " + ex.ToString());
                return R._("Sound room table expansion failed: {0}", ex.Message);
            }
        }

        /// <summary>
        /// Length-aware byte-identical restore on fault: a free-space resize-append
        /// can GROW rom.Data, so down-resize back to the snapshot length BEFORE the
        /// in-place copy. After restoring, clears the caller's
        /// <paramref name="undo"/> position list so a later <c>Rollback()</c> cannot
        /// replay stale recorded ranges over the already-restored ROM. Mirrors
        /// <c>MapSettingCore.RestoreSnapshot</c> (#885/#923/#1096).
        /// </summary>
        static void RestoreSnapshot(ROM rom, byte[] snap, Undo.UndoData? undo)
        {
            if (rom.Data.Length != snap.Length)
                rom.write_resize_data((uint)snap.Length);
            Array.Copy(snap, rom.Data, snap.Length);
            undo?.list?.Clear();
        }

        public Dictionary<string, string> GetDataReport()
        {
            return new Dictionary<string, string>
            {
                ["addr"] = $"0x{CurrentAddr:X08}",
                ["SongId"] = $"0x{SongId:X08}",
                ["Raw4"] = $"0x{Raw4:X08}",
                ["Raw8"] = $"0x{Raw8:X08}",
                ["TextId"] = $"0x{TextId:X08}",
            };
        }

        public Dictionary<string, string> GetRawRomReport()
        {
            ROM rom = CoreState.ROM;
            if (rom == null || CurrentAddr == 0) return new Dictionary<string, string>();
            uint a = CurrentAddr;
            uint dataSize = rom.RomInfo.sound_room_datasize;
            var report = new Dictionary<string, string>
            {
                ["addr"] = $"0x{a:X08}",
                ["u32@0x00"] = $"0x{rom.u32(a + 0):X08}",
                ["u32@0x04"] = $"0x{rom.u32(a + 4):X08}",
                ["u32@0x08"] = $"0x{rom.u32(a + 8):X08}",
            };
            if (dataSize >= 16)
            {
                report["u32@0x0C"] = $"0x{rom.u32(a + 12):X08}";
            }
            return report;
        }

        public Dictionary<string, string> GetFieldOffsetMap()
        {
            var map = new Dictionary<string, string>
            {
                ["SongId"] = "u32@0x00",
                ["Raw4"] = "u32@0x04",
                ["Raw8"] = "u32@0x08",
            };
            var rom = CoreState.ROM;
            if (rom?.RomInfo != null && rom.RomInfo.sound_room_datasize >= 16)
                map["TextId"] = "u32@0x0C";
            return map;
        }
    }
}
