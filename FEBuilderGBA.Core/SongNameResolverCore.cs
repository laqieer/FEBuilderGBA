using System;
using System.Collections.Generic;
using System.IO;

namespace FEBuilderGBA
{
    /// <summary>
    /// Cross-platform port of the WinForms song-name resolution chain
    /// (<c>SoundRoomForm.GetSongNameWhereSongID</c> + the SE-list fallback in
    /// <c>SongTableForm.GetSongNameFast</c>). Resolves a song id to its
    /// Sound Room name (read from the sound-room table) and, when the song is
    /// not present in the Sound Room (i.e. it is a sound effect), falls back to
    /// the SE list shipped in <c>config/data/sound_*.txt</c>.
    ///
    /// This is the real-name engine behind <see cref="NameResolver.GetSongName"/>
    /// so every Avalonia song display (Boss BGM, Song Table, Sound Room,
    /// World Map BGM) surfaces the same names WinForms shows instead of the old
    /// <c>"Song 0x{id:X}"</c> placeholder.
    /// </summary>
    public static class SongNameResolverCore
    {
        // SE list cache, invalidated when the ROM instance changes (so a ROM
        // reload / version switch re-reads the matching sound_{title}.txt file).
        static readonly object _seLock = new object();
        static ROM _seRom;
        static Dictionary<uint, string> _seList;

        /// <summary>Drop the cached SE list (call on ROM reload / undo / language change).</summary>
        public static void ClearCache()
        {
            lock (_seLock)
            {
                _seRom = null;
                _seList = null;
            }
        }

        /// <summary>
        /// The SE (sound-effect) name list, loaded lazily from
        /// <c>config/data/sound_{title}.txt</c> via <see cref="U.ConfigDataFilename"/>
        /// using the same loader (<see cref="U.LoadDicResource"/>) WinForms uses in
        /// <c>SongTableForm.PreLoadResource</c>. Cached per ROM instance.
        /// </summary>
        public static Dictionary<uint, string> GetSoundEffectList(ROM rom)
        {
            if (rom == null) return new Dictionary<uint, string>();
            lock (_seLock)
            {
                if (_seList != null && ReferenceEquals(_seRom, rom))
                {
                    return _seList;
                }
                Dictionary<uint, string> dic;
                try
                {
                    string fullfilename = U.ConfigDataFilename("sound_", rom);
                    dic = U.LoadDicResource(fullfilename);
                }
                catch (Exception ex)
                {
                    // Do NOT cache the failure — leave _seRom/_seList untouched so
                    // a later call (e.g. after CoreState.BaseDirectory is set)
                    // retries the load instead of being permanently poisoned with
                    // an empty SE list until ClearCache().
                    Log.Error("SongNameResolverCore.GetSoundEffectList failed: " + ex.Message);
                    return new Dictionary<uint, string>();
                }
                // Only cache a successfully-loaded list.
                _seList = dic;
                _seRom = rom;
                return dic;
            }
        }

        /// <summary>
        /// Resolve a song id to a human-readable name. Mirrors the name-only path
        /// of WinForms <c>SongTableForm.GetSongNameFast</c>:
        /// <list type="number">
        ///   <item>look the id up in the Sound Room table
        ///     (<see cref="GetSongNameWhereSongID"/>); if found, return that room
        ///     name (trimmed);</item>
        ///   <item>otherwise fall back to the SE list
        ///     (<c>config/data/sound_*.txt</c>);</item>
        ///   <item>otherwise return <c>""</c>.</item>
        /// </list>
        /// The empty-track suffix and per-row comment that WinForms appends are
        /// list-rendering concerns and are intentionally NOT included here so the
        /// resolver stays a pure name lookup usable from any editor.
        /// </summary>
        public static string GetSongName(ROM rom, uint songId)
        {
            if (rom?.RomInfo == null) return "";
            try
            {
                string name = GetSongNameWhereSongID(rom, songId);
                if (!string.IsNullOrEmpty(name))
                {
                    return name.Trim();
                }
                // Songs not present in the Sound Room are sound effects — look
                // them up in the SE list (same fallback as WinForms).
                return U.at(GetSoundEffectList(rom), songId);
            }
            catch (Exception ex)
            {
                Log.Error("SongNameResolverCore.GetSongName(" + songId + ") failed: " + ex.Message);
                return "";
            }
        }

        /// <summary>
        /// Walk the Sound Room table looking for an entry whose song id (u32 at
        /// offset 0) equals <paramref name="songId"/>; return that entry's room
        /// name. Port of WinForms <c>SoundRoomForm.GetSongNameWhereSongID</c>.
        /// Returns <c>""</c> when no Sound Room entry references the id.
        /// </summary>
        public static string GetSongNameWhereSongID(ROM rom, uint songId)
        {
            if (rom?.RomInfo == null) return "";

            uint pointer = rom.RomInfo.sound_room_pointer;
            uint dataSize = rom.RomInfo.sound_room_datasize;
            if (pointer == 0 || pointer == U.NOT_FOUND || dataSize == 0) return "";

            uint baseAddr = rom.p32(U.toOffset(pointer));
            if (!U.isSafetyOffset(baseAddr, rom)) return "";

            uint count = GetSoundRoomCount(rom, baseAddr, dataSize);

            uint addr = baseAddr;
            for (uint i = 0; i < count; i++, addr += dataSize)
            {
                uint a = rom.u32(addr);
                if (songId == a)
                {
                    return GetSongNameLow(rom, addr);
                }
            }
            return "";
        }

        /// <summary>
        /// Decode the room-name text stored in a Sound Room entry. Mirrors
        /// WinForms <c>SoundRoomForm.GetSongNameLow</c>: FE6 stores the name
        /// text id as a u32 at offset +4; FE7/FE8 store it at offset +12.
        /// Control codes are stripped via the shared <see cref="NameResolver"/>
        /// text decoder so the result matches the names used elsewhere.
        /// </summary>
        public static string GetSongNameLow(ROM rom, uint addr)
        {
            if (rom?.RomInfo == null) return "";
            uint textIdOffset = (rom.RomInfo.version == 6) ? addr + 4 : addr + 12;
            if (!U.isSafetyOffset(textIdOffset + 3, rom)) return "";
            uint textId = rom.u32(textIdOffset);
            if (textId == 0) return "";
            return NameResolver.GetTextById(textId);
        }

        /// <summary>
        /// Count the Sound Room table entries the same way WinForms
        /// <c>SoundRoomForm.Init</c>'s read-max callback does: stop at a
        /// <c>0xFFFFFFFF</c> terminator, or after index 10 once 10 consecutive
        /// blocks are empty.
        /// </summary>
        static uint GetSoundRoomCount(ROM rom, uint baseAddr, uint dataSize)
        {
            return rom.getBlockDataCount(baseAddr, dataSize, (int i, uint addr) =>
            {
                if (rom.u32(addr) == 0xFFFFFFFF)
                {
                    return false;
                }
                if (i > 10 && rom.IsEmpty(addr, dataSize * 10))
                {
                    return false;
                }
                return true;
            });
        }
    }
}
