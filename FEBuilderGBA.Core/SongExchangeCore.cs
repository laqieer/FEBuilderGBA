// SPDX-License-Identifier: GPL-3.0-or-later
// Cross-platform Song Exchange — cross-ROM song transplant (#1002 Slice 3).
//
// Faithful (byte-for-byte) port of WinForms SongExchangeForm.cs's transplant
// pipeline: InstrumentMap (Rip/Burn-time voice + sample collection), Rip (the
// per-track byte transform), and Burn (free-space alloc + buffer assembly +
// sample recycling + pointer fixups). The WinForms form showed MessageBoxes on
// warnings/errors; this Core seam never opens dialogs — it returns a
// ConvertResult (Success / ErrorMessage / HadStructureWarning) so the host
// (Avalonia / CLI) decides what to surface.
//
// API: ConvertSong(ROM destRom, SongSt destSong, byte[] srcData, SongSt srcSong,
//      Undo.UndoData undo). The caller owns the undo scope (an Avalonia
//      UndoService.Begin / a CLI ROM.BeginUndoScope). All mutating writes go
//      through the undo-taking rom.write_* overloads, matching the proven
//      ItemUsagePointerCore.Switch2Expands pattern. The buffer + recycle +
//      pointer fixups are ALL resolved BEFORE the first ROM write, so a fault
//      (no free space / bad source pointer) leaves destRom byte-identical.
//
// No-growth: unlike WinForms (AllocBinaryData → SearchFreeSpaceOne may extend
// the ROM), the Core path uses rom.FindFreeSpace only (upper-half-first,
// lower-half fallback — the established headless allocator convention). If no
// free region fits, ConvertSong fails cleanly with NO mutation.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using FEBuilderGBA.Core;

namespace FEBuilderGBA
{
    /// <summary>
    /// Cross-platform song exchange logic extracted from WinForms SongExchangeForm.
    /// Handles reading song tables and transplanting songs between ROMs.
    /// </summary>
    public static class SongExchangeCore
    {
        /// <summary>Song table entry.</summary>
        public class SongSt
        {
            public uint Number { get; set; }  // song index
            public uint Table { get; set; }   // pointer to song table entry
            public uint Header { get; set; }  // pointer to song header
            public uint Voices { get; set; }  // instruments pointer
            public uint Tracks { get; set; }  // track data pointer
            public int TrackCount { get; set; }
        }

        /// <summary>
        /// Result of a <see cref="ConvertSong(ROM,SongSt,byte[],SongSt,Undo.UndoData)"/>
        /// transplant. <see cref="Success"/> is true on a completed write;
        /// <see cref="HadStructureWarning"/> flags a partially-corrupt source song
        /// (WinForms popped a "force?" dialog — Core proceeds but records the flag).
        /// </summary>
        public class ConvertResult
        {
            public bool Success;
            public string ErrorMessage = "";
            public bool HadStructureWarning;
        }

        // ---- WinForms SongInstrumentForm.IsDirectSound / IsWaveMemory parity ----
        // Inlined (pure constant checks) so Core takes no WinForms dependency.
        // Mirrors FEBuilderGBA/SongInstrumentForm.cs L909-932 exactly.
        static bool IsDirectSound(uint instrumentCode)
        {
            return instrumentCode == 0x00
                || instrumentCode == 0x08
                || instrumentCode == 0x10
                || instrumentCode == 0x18;
        }
        static bool IsWaveMemory(uint instrumentCode)
        {
            return instrumentCode == 0x03
                || instrumentCode == 0x0B;
        }

        /// <summary>
        /// Find the song table pointer in ROM data.
        /// The song table pointer is stored at sound_table_pointer in ROMFEINFO.
        /// </summary>
        public static uint FindSongTablePointer(byte[] romData, uint soundTablePointerAddr)
        {
            if (romData == null || soundTablePointerAddr + 3 >= romData.Length) return 0;

            uint ptr = (uint)(romData[soundTablePointerAddr] |
                             (romData[soundTablePointerAddr + 1] << 8) |
                             (romData[soundTablePointerAddr + 2] << 16) |
                             (romData[soundTablePointerAddr + 3] << 24));

            // Convert GBA pointer to offset
            if (ptr >= 0x08000000 && ptr < 0x0A000000)
                return ptr - 0x08000000;

            return 0;
        }

        /// <summary>
        /// Read song table entries from ROM data.
        /// Each entry is 8 bytes: 4-byte pointer to song header + 4-byte metadata.
        /// PUBLIC SIGNATURE IS FROZEN (CLI + tests + Avalonia depend on it).
        /// </summary>
        public static List<SongSt> SongTableToSongList(byte[] romData, uint tableAddr)
        {
            var list = new List<SongSt>();
            if (romData == null || tableAddr == 0) return list;

            for (uint i = 0; ; i++)
            {
                uint entryAddr = tableAddr + i * 8;
                if (entryAddr + 7 >= romData.Length) break;

                uint headerPtr = (uint)(romData[entryAddr] |
                                       (romData[entryAddr + 1] << 8) |
                                       (romData[entryAddr + 2] << 16) |
                                       (romData[entryAddr + 3] << 24));

                // Stop at invalid pointer
                if (headerPtr < 0x08000000 || headerPtr >= 0x0A000000) break;

                uint headerAddr = headerPtr - 0x08000000;
                if (headerAddr + 7 >= romData.Length) break;

                // Read song header: track count (u8), padding (u8), priority (u8), reverb (u8), voice pointer (u32)
                int trackCount = romData[headerAddr];
                // Entry 0 is often a dummy with trackCount=0; skip but don't terminate.
                // Only terminate on trackCount > 16 which indicates corrupt/end-of-table data.
                if (trackCount > 16) break;
                if (trackCount == 0)
                {
                    // Add placeholder entry so indices align with WinForms behavior
                    list.Add(new SongSt { Number = i, Table = entryAddr, Header = headerAddr });
                    continue;
                }

                uint voicePtr = (uint)(romData[headerAddr + 4] |
                                      (romData[headerAddr + 5] << 8) |
                                      (romData[headerAddr + 6] << 16) |
                                      (romData[headerAddr + 7] << 24));

                uint voiceAddr = 0;
                if (voicePtr >= 0x08000000 && voicePtr < 0x0A000000)
                    voiceAddr = voicePtr - 0x08000000;

                var song = new SongSt
                {
                    Number = i,
                    Table = entryAddr,
                    Header = headerAddr,
                    Voices = voiceAddr,
                    TrackCount = trackCount,
                };

                // Read first track pointer
                if (headerAddr + 8 + 3 < romData.Length)
                {
                    uint trackPtr = (uint)(romData[headerAddr + 8] |
                                          (romData[headerAddr + 9] << 8) |
                                          (romData[headerAddr + 10] << 16) |
                                          (romData[headerAddr + 11] << 24));
                    if (trackPtr >= 0x08000000 && trackPtr < 0x0A000000)
                        song.Tracks = trackPtr - 0x08000000;
                }

                list.Add(song);
            }

            return list;
        }

        /// <summary>
        /// Transplant a song from source ROM bytes into the destination ROM.
        /// Faithful port of WinForms SongExchangeForm.ConvertSong: builds an
        /// <see cref="InstrumentMap"/> from the source voices, Rips every track,
        /// then Burns the result (alloc + buffer + recycle + fixups) into the
        /// destination ROM under the caller's undo scope.
        /// </summary>
        /// <param name="destRom">Destination ROM (mutated; must be CoreState.ROM so undo snapshots resolve).</param>
        /// <param name="destSong">Destination song-table slot to overwrite.</param>
        /// <param name="srcData">Raw bytes of the SOURCE ROM (the donor).</param>
        /// <param name="srcSong">Source song-table entry to transplant.</param>
        /// <param name="undo">Active undo group owned by the caller.</param>
        public static ConvertResult ConvertSong(ROM destRom, SongSt destSong, byte[] srcData, SongSt srcSong, Undo.UndoData undo)
        {
            var result = new ConvertResult();
            if (destRom == null || destRom.Data == null || destSong == null || srcData == null || srcSong == null)
            {
                result.ErrorMessage = R._("Invalid arguments for song exchange.");
                return result;
            }

            // ---- Build the instrument map from the source voices. ----
            // WinForms surfaced a "force?" dialog when ErrorMessage != ""; Core
            // does NOT abort — it proceeds and flags HadStructureWarning so the
            // host can warn (matches "import only the tracks we recognized").
            InstrumentMap instrument_map = new InstrumentMap(srcData, srcSong.Voices);
            if (instrument_map.ErrorMessage != "")
            {
                result.HadStructureWarning = true;
            }

            // ---- Rip every track (NO mutation). ----
            var trackdata = new List<List<byte>>();
            string ripError = "";
            bool success = Rip(srcData, srcSong, instrument_map, trackdata, ref ripError);
            if (!success)
            {
                result.ErrorMessage = R._("This song's data is corrupt and could not be Ripped.") + ripError;
                return result;
            }

            // ---- Burn into the destination ROM. ----
            return Burn(destRom, destSong, instrument_map, trackdata, undo, result);
        }

        // -----------------------------------------------------------------
        // Burn — faithful port of SongExchangeForm.Burn. The buffer + sample
        // recycle + pointer fixups are resolved fully BEFORE the first ROM
        // write, so a no-free-space failure leaves destRom byte-identical.
        // -----------------------------------------------------------------
        static ConvertResult Burn(ROM destRom, SongSt song, InstrumentMap instrument_map,
            List<List<byte>> trackdata, Undo.UndoData undo, ConvertResult result)
        {
            //必要なサイズを計算する. (compute the size we need)
            uint use_size = 8 + (4 * (uint)trackdata.Count); //ヘッダー (header)
            for (int track = 0; track < trackdata.Count; track++)
            {
                use_size += (uint)trackdata[track].Count; //楽譜 (track data)
            }
            use_size = U.Padding4(use_size); //楽譜と楽器の間は 4バイトアライメントが必要. (4-byte align between tracks and instruments)
            use_size += (uint)(instrument_map.Instrument_mapping.Count * 12); //楽器 (instruments)
            use_size += (uint)instrument_map.Sample_data.Count;      //楽器データ (sample data)

            // No-growth free-space allocation (upper-half-first, lower-half fallback)
            // — the established headless allocator convention. NO ROM extension.
            uint write_pointer = FindFreeSpaceNoGrow(destRom, use_size);
            if (write_pointer == U.NOT_FOUND)
            {
                result.ErrorMessage = R._("Could not allocate {0} bytes of free space. The ROM may be full or the music failed to parse.", use_size);
                return result;
            }

            byte[] data = new byte[use_size];
            U.write_u8(data, 0, (uint)trackdata.Count); //トラック数 (track count)
            U.write_u8(data, 1, 0x0);  //常にゼロ. (always zero)
            U.write_u8(data, 2, 0x0A); //Do these values matter?
            U.write_u8(data, 3, 0x80); //This is just copying what the stock ROM does...
            uint offset = 8 + (4 * (uint)trackdata.Count);

            //データ構造 (data structure)
            //ヘッダー
            //[track数] [0] [0x0A] [0x80] [楽器ポンタ] [楽譜1ポインタ] [楽譜2ポインタ].... [楽譜Nポインタ]
            //
            //実データ
            //[楽譜1データ].......
            //[楽譜2データ].......
            //
            //[楽器データ]
            //[楽器サンプルデータ]

            //楽譜 (track data)
            for (int track = 0; track < trackdata.Count; track++)
            {
                //楽譜ポインタの書き込み (write the track pointer)
                U.write_u32(data, 8 + (4 * (uint)track), U.toPointer(write_pointer + offset));

                //楽譜データを書き込む. (write the track data)
                burn_track(data, offset, write_pointer, trackdata[track].ToArray());
                offset += (uint)trackdata[track].Count;
            }
            offset = U.Padding4(offset); //楽譜と楽器の間は 4バイトアライメントが必要.

            //楽器ポインタ (instrument pointer)
            U.write_u32(data, 4, U.toPointer(write_pointer + offset));

            uint instrument_start = offset; //楽器開始 (instrument start)
            uint instrumentdata_start = instrument_start + (12 * (uint)instrument_map.Instrument_mapping.Count); //楽器データ開始 (sample data start)

            U.write_range(data, instrument_start, instrument_map.Instrument_codes.ToArray());
            U.write_range(data, instrumentdata_start, instrument_map.Sample_data.ToArray());

            //楽器 (instruments) — sample recycle + pointer fixups.
            uint resyclesize = 0;
            for (int i = 0; i < instrument_map.Instrument_mapping.Count; i++)
            {
                uint this_instrument = instrument_start + (12 * (uint)i);

                uint instrumentCode = U.u8(data, this_instrument + 0);
                if (IsDirectSound(instrumentCode)
                    || IsWaveMemory(instrumentCode))
                {
                    uint sample_data_start = U.u32(data, this_instrument + 4);
                    sample_data_start += instrumentdata_start;
                    if (sample_data_start < resyclesize)
                    {
                        continue;
                    }
                    sample_data_start -= resyclesize;
                    uint sample_data_len;
                    if (IsWaveMemory(instrumentCode))
                    {
                        sample_data_len = 16;
                    }
                    else
                    {
                        sample_data_len = U.u32(data, sample_data_start + 12);
                        sample_data_len = U.Padding4(sample_data_len);
                    }

                    uint found_address = U.Grep(destRom.Data, U.subrange(data, sample_data_start, sample_data_start + sample_data_len), 100, 0, 4);
                    if (found_address != U.NOT_FOUND)
                    {
                        //existing address in ROM. recycle
                        data = U.del(data, sample_data_start, sample_data_start + sample_data_len);
                        U.write_u32(data, this_instrument + 4, U.toPointer(found_address));

                        resyclesize += sample_data_len;
                    }
                    else
                    {
                        //nothing to recycle, write the data.
                        uint baseoffset = U.u32(data, this_instrument + 4); //相対アドレスが書いてあるので、それを絶対値に変換する (relative -> absolute)
                        U.write_u32(data, this_instrument + 4
                            , U.toPointer((instrumentdata_start + write_pointer + baseoffset) - resyclesize));
                    }
                }
                else if (instrumentCode == 0x80)
                {//ドラム (drum)
                    uint baseoffset = U.u32(data, this_instrument + 4);
                    U.write_u32(data, this_instrument + 4
                        , U.toPointer(instrument_start + write_pointer + baseoffset));
                }
                else if (instrumentCode == 0x40)
                {//MULTI TRACK
                    uint baseoffset = U.u32(data, this_instrument + 4);
                    U.write_u32(data, this_instrument + 4
                        , U.toPointer(instrument_start + write_pointer + baseoffset));

                    baseoffset = U.u32(data, this_instrument + 8);
                    U.write_u32(data, this_instrument + 8
                        , U.toPointer((instrumentdata_start + write_pointer + baseoffset) - resyclesize));
                }
            }

            // ---- The ONLY mutations: write the buffer + repoint the slot + priority. ----
            // All via the undo-taking overloads, under the caller's scope. Note the
            // recycle loop above may have shrunk `data` (U.del), so we re-validate
            // the final region fits the slot we found (the slot was sized for the
            // pre-recycle buffer, so it always fits — but guard anyway).
            if ((long)write_pointer + data.Length > destRom.Data.Length)
            {
                result.ErrorMessage = R._("Could not allocate {0} bytes of free space. The ROM may be full or the music failed to parse.", (uint)data.Length);
                return result;
            }

            destRom.write_p32(song.Table, write_pointer, undo);
            destRom.write_range(write_pointer, data, undo);

            uint priority = GetSongPriority(trackdata.Count);
            destRom.write_u32(song.Table + 4, priority, undo);

            result.Success = true;
            return result;
        }

        /// <summary>
        /// No-growth free-space search mirroring CoreState's headless allocator:
        /// upper-half first (Data.Length/2), then a 0x100 lower-half fallback.
        /// Returns <see cref="U.NOT_FOUND"/> when nothing fits (NO ROM extension).
        /// </summary>
        static uint FindFreeSpaceNoGrow(ROM rom, uint needsize)
        {
            uint addr = rom.FindFreeSpace((uint)(rom.Data.Length / 2), needsize);
            if (addr == U.NOT_FOUND)
            {
                addr = rom.FindFreeSpace(0x100u, needsize);
            }
            return addr;
        }

        static uint GetSongPriority(int trackdata)
        {
            if (trackdata <= 1)
            {//SFX?
                return 0x60006;
            }
            else
            {//MAP
                return 0x10001;
            }
        }

        static void burn_track(byte[] data, uint offset, uint write_pointer, byte[] trackdata)
        {
            for (int i = 0; i < trackdata.Length;)
            {
                byte b = trackdata[i];
                data[i + offset] = b;
                i++;
                if (b == 0xB2 || b == 0xB3)
                {
                    uint p = U.u32(trackdata, (uint)i);
                    U.write_u32(data, (uint)(i + offset), U.toPointer(p + offset + write_pointer));
                    i += 4;
                }
            }
        }

        static bool Rip(byte[] data, SongSt song, InstrumentMap instrument_map, List<List<byte>> trackdata, ref string errorMessage)
        {
            //Rip
            for (uint track = 0; track < song.TrackCount; track++)
            {
                uint songtrack_pointer = song.Header + 8 + (4 * track);
                if ((long)songtrack_pointer + 3 >= data.Length)
                {
                    errorMessage += "\r\n" + R._("track:{0} pointer is out of range. addr:{1}", track, songtrack_pointer.ToString("X08"));
                    return false;
                }
                uint songtrackdata_pointer = U.u32(data, songtrack_pointer);
                if (!U.isPointer(songtrackdata_pointer))
                {
                    errorMessage += "\r\n" + R._("track:{0} can not pointer! addr:{1} data:{2}",
                        track, songtrack_pointer.ToString("X08"), songtrackdata_pointer.ToString("X08"));
                    return false;
                }
                songtrackdata_pointer = U.toOffset(songtrackdata_pointer);
                if ((long)songtrackdata_pointer >= data.Length)
                {
                    errorMessage += "\r\n" + R._("track:{0} data pointer is out of range. addr:{1}", track, songtrackdata_pointer.ToString("X08"));
                    return false;
                }

                List<byte> track_data = process_track(data, songtrackdata_pointer, instrument_map);

                trackdata.Add(track_data);
            }
            //ドラムがない曲の場合、それは困るので、ダミーのドラムを追加する. (no-drum song -> add a dummy drum)
            instrument_map.appendDrumIfNoDrum();
            return true;
        }

        static List<byte> process_track(byte[] data, uint songtrackdata_pointer, InstrumentMap instrument_map)
        {
            List<byte> ret = new List<byte>();

            //Transform the data.
            uint position = songtrackdata_pointer;
            uint percussion = 0;
            while (true)
            {
                uint b = U.u8(data, position);
                position++;
                ret.Add((byte)b);

                if (b == 0xB1)
                {
                    break;
                }
                else if (b == 0xB2 || b == 0xB3)
                {
                    //repointer
                    U.append_u32(ret, U.p32(data, position) - songtrackdata_pointer);
                    position += 4;
                }
                else if (b == 0xBD)
                {
                    uint next_byte = U.u8(data, position);
                    position++;

                    uint translated = instrument_map.translate(next_byte);
                    if (translated == 0)
                    {
                        percussion = next_byte;
                    }
                    ret.Add((byte)translated);
                }
                else if (b == 0xBB || b == 0xBC || b == 0xBE || b == 0xBF || b == 0xC0 || b == 0xC1 || b == 0xC2 || b == 0xC3 || b == 0xC4 || b == 0xC5 || b == 0xC8)
                {
                    // These commands take a data byte that must not be processed.
                    ret.Add((byte)U.u8(data, position));
                    position++;
                }
                else if (b == 0xb9)
                {//MEMACC 4バイト命令 (4-byte command)
                    //最初の1バイトはコピー済みなので、残りの3バイトコピーする. (first byte already copied; copy remaining 3)
                    ret.Add((byte)U.u8(data, position));
                    position++;
                    ret.Add((byte)U.u8(data, position));
                    position++;
                    ret.Add((byte)U.u8(data, position));
                    position++;
                }
                else if (percussion != 0 && b < 0x80)
                {
                    uint inst = instrument_map.translate_percussion(percussion, b);
                    ret[ret.Count - 1] = ((byte)inst);
                    //There might be a volume marker, and then a 'gate' byte
                    //For now, assuming that any subsequent low-value bytes are extra data
                    //that should be passed as-is - even though previous experimentation suggested
                    //that these bytes could be used to specify a chord...
                    while (U.u8(data, position) < 0x80)
                    {// Volume marker
                        ret.Add((byte)U.u8(data, position));
                        position++;
                    }
                }
            }
            return ret;
        }

        // -----------------------------------------------------------------
        // InstrumentMap — faithful port of SongExchangeForm.InstrumentMap.
        // Maps non-percussion instruments to sequential ids 1..n and extracts
        // the matching samples. Percussion maps all collapse to id 0; their
        // mapped samples are assigned sequential ids in the same mapping.
        // -----------------------------------------------------------------
        public class InstrumentMap
        {
            public List<byte> Instrument_codes;
            public List<byte> Sample_data;

            public Dictionary<string, uint> Instrument_mapping;
            byte[] Data;
            uint Instrument_map_offset;

            public string ErrorMessage;

            public InstrumentMap(byte[] data, uint instrument_map_pointer)
            {
                this.ErrorMessage = "";
                this.Instrument_codes = new List<byte>();
                this.Sample_data = new List<byte>();
                this.Instrument_mapping = new Dictionary<string, uint>();

                this.Instrument_codes.AddRange(new byte[] { 0x80, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 });
                this.Data = data;
                this.Instrument_map_offset = U.toOffset(instrument_map_pointer);
            }

            public uint Count()
            {
                uint bytecount = (uint)this.Instrument_codes.Count;
                Debug.Assert(bytecount % 12 == 0);

                uint result = bytecount / 12;
                Debug.Assert(result < 0x80); //allocated too many instruments?
                return result;
            }

            public void appendDrumIfNoDrum()
            {
                foreach (var pair in this.Instrument_mapping)
                {
                    if (pair.Key.Length <= 0)
                    {
                        continue;
                    }
                    if (pair.Key[0] == 'D')
                    {//ドラム発見 (drum found)
                        return;
                    }
                }
                //ドラムが見つからない! ダミーのドラムを追加する. (no drum -> add a dummy)
                this.Instrument_mapping["Drum_Dummy"] = 0;
            }

            public byte[] get_instrument(uint index, uint baseindex = 0)
            {
                if (baseindex == 0)
                {
                    baseindex = this.Instrument_map_offset;
                }
                baseindex += 12 * index;

                return U.subrange(this.Data, baseindex, baseindex + 12);
            }

            public uint translate(uint original_id)
            {
                string original_id_str = original_id.ToString();
                if (this.Instrument_mapping.ContainsKey(original_id_str))
                {
                    return this.Instrument_mapping[original_id_str];
                }
                byte[] instrument_code = this.get_instrument(original_id);
                return this._prepare(instrument_code, original_id.ToString(), false);
            }

            //for drum
            public uint translate_percussion(uint original_id, uint pitch)
            {
                string key = "D" + "_" + original_id.ToString() + "_" + pitch.ToString();
                if (this.Instrument_mapping.ContainsKey(key))
                {
                    return this.Instrument_mapping[key];
                }

                byte[] instrument = this.get_instrument(original_id);
                uint instrument_offset = U.p32(instrument, 4);

                byte[] instrument_code = this.get_instrument(pitch, instrument_offset);
                return this._prepare(instrument_code, key, true);
            }

            //for MultiSample
            public uint translate_multisample(uint original_id, uint multivoices)
            {
                string key = "M" + "_" + original_id.ToString() + "_" + multivoices.ToString();
                if (this.Instrument_mapping.ContainsKey(key))
                {
                    return this.Instrument_mapping[key];
                }

                byte[] instrument_code = this.get_instrument(original_id, multivoices);
                return this._prepare(instrument_code, key, true);
            }

            byte[] bad_inst()
            {
                return new byte[] {
                    0x04,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00};
            }

            bool _prepare_DirectSound(byte[] instrument_code, string key, bool is_deps)
            {
                Debug.Assert(IsDirectSound(instrument_code[0]));
                uint sample_location = U.p32(instrument_code, 4);
                if (sample_location > this.Data.Length)
                {
                    this.ErrorMessage += "\r\n" +
                        R._("There was bad data inside a DirectSound. Ignoring it. sample_location:{0} > {1} ROM Size",
                        U.To0xHexString(sample_location), U.To0xHexString(this.Data.Length));

                    //ダメな楽器として認識する. (treat as bad instrument)
                    return false;
                }
                uint sample_hz1024 = U.u32(this.Data, sample_location + 4) / 1024;
                uint sample_length = SongDirectSoundWavCore.GetDirectSoundWaveDataLength(this.Data, sample_location);

                if (is_deps)
                {
                    if (sample_length > 1024 * 1024 * 1   //1MB
                        || sample_hz1024 > 48 * 1024    //48khz Over
                        )
                    {
                        this.ErrorMessage += "\r\n" +
                            R._("There was bad data inside a Multi or Drum. Ignoring it. OverHZ Sample:{0} bytes ({1} *1024 hz)", sample_length, sample_hz1024);

                        //ダメな楽器として認識する.
                        return false;
                    }
                }

                List<byte> current_sample = U.subrangeToList(this.Data, sample_location, sample_location + 16 + sample_length);
                //4バイトアライメント (4-byte align)
                while ((current_sample.Count % 4) != 0)
                {
                    current_sample.Add(0);
                }

                U.write_u32(instrument_code, 4, (uint)this.Sample_data.Count);
                this.Sample_data.AddRange(current_sample);

                return true;
            }

            bool _prepare_WaveMemory(byte[] instrument_code, string key, bool is_deps)
            {
                Debug.Assert(IsWaveMemory(instrument_code[0]));
                uint sample_location = U.p32(instrument_code, 4);
                if (sample_location > this.Data.Length)
                {
                    this.ErrorMessage += "\r\n" +
                        R._("There was bad data inside a DirectSound. Ignoring it. sample_location:{0} > {1} ROM Size",
                            U.To0xHexString(sample_location), U.To0xHexString(this.Data.Length));

                    //ダメな楽器として認識する.
                    return false;
                }

                List<byte> current_sample = U.subrangeToList(this.Data, sample_location, sample_location + 16);

                //4バイトアライメント
                while ((current_sample.Count % 4) != 0)
                {
                    current_sample.Add(0);
                }

                U.write_u32(instrument_code, 4, (uint)this.Sample_data.Count);
                this.Sample_data.AddRange(current_sample);

                return true;
            }

            bool _prepare_MultiSample(byte[] instrument_code, string key, bool is_deps)
            {
                uint multisample_voices = U.p32(instrument_code, 4);
                uint sample_location = U.p32(instrument_code, 8);
                if (multisample_voices > this.Data.Length)
                {
                    this.ErrorMessage += "\r\n" +
                        R._("There was bad data inside a MultiSample. Ignoring it. multisample_voices:{0} sample_location:{1} > {2} ROM Size",
                            U.To0xHexString(multisample_voices), U.To0xHexString(sample_location), U.To0xHexString(this.Data.Length));

                    //ダメな楽器として認識する.
                    return false;
                }
                if (sample_location > this.Data.Length)
                {
                    this.ErrorMessage += "\r\n" +
                        R._("There was bad data inside a MultiSample. Ignoring it. multisample_voices:{0} sample_location:{1} > {2} ROM Size",
                            U.To0xHexString(multisample_voices), U.To0xHexString(sample_location), U.To0xHexString(this.Data.Length));

                    //ダメな楽器として認識する.
                    return false;
                }

                List<byte> current_sample = U.subrangeToList(this.Data, sample_location, sample_location + 128);

                Dictionary<int, uint> dic = new Dictionary<int, uint>();
                for (int i = 0; i < current_sample.Count; i++)
                {
                    int id = current_sample[i];
                    if (id > 0x7F)
                    {
                        continue;
                    }
                    if (!dic.ContainsKey(id))
                    {
                        dic[id] = this.translate_multisample((uint)id, multisample_voices);
                    }
                    current_sample[i] = (byte)dic[id];
                }

                U.write_u32(instrument_code, 4, 0); //follow instrument start posstion
                U.write_u32(instrument_code, 8, (uint)this.Sample_data.Count);
                this.Sample_data.AddRange(current_sample);

                return true;
            }

            uint _prepare(byte[] instrument_code, string key, bool is_deps)
            {
                // Fix instrument pointer to be an offset relative to start of sample data.
                // The pointer for the first instrument - which is the percussion map - is
                // of course relative to the start of instrument data, being zero. The
                // burn procedure is aware of this.
                if (is_deps && (instrument_code[0] == 0x80 || instrument_code[0] == 0x40))
                {
                    this.ErrorMessage += "\r\n" +
                        R._("There was a Multi or Drum inside a Multi or Drum. This is too complex to handle, so it is ignored.\r\n");

                    instrument_code = bad_inst();
                }
                else if (IsDirectSound(instrument_code[0]))
                {
                    bool success = _prepare_DirectSound(instrument_code, key, is_deps);
                    if (success == false)
                    {
                        instrument_code = bad_inst();
                    }
                }
                else if (IsWaveMemory(instrument_code[0]))
                {
                    bool success = _prepare_WaveMemory(instrument_code, key, is_deps);
                    if (success == false)
                    {
                        instrument_code = bad_inst();
                    }
                }
                else if (instrument_code[0] == 0x80)
                {
                    //drum instrument is always id:0
                    this.Instrument_mapping[key] = 0;
                    return 0;
                }
                else if (instrument_code[0] == 0x40)
                {
                    bool success = _prepare_MultiSample(instrument_code, key, is_deps);
                    if (success == false)
                    {
                        instrument_code = bad_inst();
                    }
                }
                else
                {
                    // unknown instrument code — copied as-is (WF "???" branch)
                }
                Debug.Assert(instrument_code.Length >= 0xC);

                uint result = this.Count();
                this.Instrument_mapping[key] = result;
                this.Instrument_codes.AddRange(instrument_code);
                return result;
            }
        }
    }
}
