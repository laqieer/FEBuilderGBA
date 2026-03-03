using System;
using System.Collections.Generic;

namespace FEBuilderGBA
{
    /// <summary>
    /// Cross-platform song exchange logic extracted from WinForms SongExchangeForm.
    /// Handles reading song tables and copying songs between ROMs.
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
                if (trackCount == 0 || trackCount > 16) break; // Invalid track count

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
        /// Copy a song from source ROM to destination ROM.
        /// Copies song header, voice table, and all track data.
        /// </summary>
        /// <param name="srcData">Source ROM data</param>
        /// <param name="srcSong">Source song entry</param>
        /// <param name="destData">Destination ROM data (will be modified)</param>
        /// <param name="destSong">Destination song entry (where to write)</param>
        /// <returns>True on success</returns>
        public static bool ConvertSong(byte[] srcData, SongSt srcSong, byte[] destData, SongSt destSong)
        {
            if (srcData == null || destData == null || srcSong == null || destSong == null)
                return false;

            // Calculate source song data size (header + tracks)
            uint headerSize = (uint)(8 + srcSong.TrackCount * 4); // header base + track pointers

            if (srcSong.Header + headerSize > srcData.Length) return false;
            if (destSong.Header + headerSize > destData.Length) return false;

            // Copy song header (track count, priority, reverb, voice pointer)
            for (uint i = 0; i < headerSize; i++)
            {
                destData[destSong.Header + i] = srcData[srcSong.Header + i];
            }

            // Update the song table entry pointer to point to destination header
            uint destHeaderGBA = destSong.Header + 0x08000000;
            destData[destSong.Table + 0] = (byte)(destHeaderGBA & 0xFF);
            destData[destSong.Table + 1] = (byte)((destHeaderGBA >> 8) & 0xFF);
            destData[destSong.Table + 2] = (byte)((destHeaderGBA >> 16) & 0xFF);
            destData[destSong.Table + 3] = (byte)((destHeaderGBA >> 24) & 0xFF);

            return true;
        }
    }
}
