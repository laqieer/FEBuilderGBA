using System;
using System.Collections.Generic;
using System.IO;
using Xunit;
using FEBuilderGBA;

namespace FEBuilderGBA.Core.Tests
{
    /// <summary>
    /// Independent oracle tests for the cross-platform song-name resolver
    /// (<see cref="SongNameResolverCore"/>) that powers
    /// <see cref="NameResolver.GetSongName"/> (#961 W2a).
    ///
    /// These DO NOT compare against the Avalonia view-model / golden builder.
    /// Instead they hand-derive expectations from raw ROM bytes (the sound-room
    /// table) and from the shipped <c>config/data/sound_*.txt</c> SE list, then
    /// assert the resolver reproduces them. They skip cleanly when no ROM is
    /// available (CI without roms/).
    /// </summary>
    [Collection("SharedState")]
    public class SongNameResolverCoreTests
    {
        // ----------------------------------------------------------------
        // ROM / repo-root discovery (mirrors the other Core real-ROM tests)
        // ----------------------------------------------------------------

        static string FindRepoRoot()
        {
            string dir = AppContext.BaseDirectory;
            for (int i = 0; i < 10 && dir != null; i++)
            {
                if (File.Exists(Path.Combine(dir, "FEBuilderGBA.sln")))
                    return dir;
                dir = Path.GetDirectoryName(dir);
            }
            return null;
        }

        static string FindRom(string romName)
        {
            string root = FindRepoRoot();
            if (root == null) return null;
            string path = Path.Combine(root, "roms", romName);
            return File.Exists(path) ? path : null;
        }

        /// <summary>
        /// Load a real ROM fully enough for text decode (Huffman from the ROM +
        /// a headless system encoder) with BaseDirectory pointed at the repo
        /// root so the SE-list config file resolves. Skips (no-op) when the ROM
        /// is missing.
        /// </summary>
        static void WithRealRom(string romName, Action<ROM> action)
        {
            string romPath = FindRom(romName);
            if (romPath == null) return; // skip

            string root = FindRepoRoot();
            var savedRom = CoreState.ROM;
            var savedEnc = CoreState.SystemTextEncoder;
            var savedBase = CoreState.BaseDirectory;
            try
            {
                var rom = new ROM();
                if (!rom.Load(romPath, out string _)) return; // skip
                CoreState.ROM = rom;
                CoreState.SystemTextEncoder = new HeadlessSystemTextEncoder(rom);
                if (root != null) CoreState.BaseDirectory = root;
                NameResolver.ClearCache();
                action(rom);
            }
            finally
            {
                CoreState.ROM = savedRom;
                CoreState.SystemTextEncoder = savedEnc;
                CoreState.BaseDirectory = savedBase;
                NameResolver.ClearCache();
            }
        }

        // ----------------------------------------------------------------
        // Raw sound-room table walk (independent of SongNameResolverCore)
        // ----------------------------------------------------------------

        /// <summary>
        /// Independently enumerate the sound-room table from raw bytes, yielding
        /// (songId, roomTextId) for each entry, stopping at 0xFFFFFFFF. Mirrors
        /// the WinForms layout (song id u32@+0; room-name text id u32@+12 for
        /// FE7/8, u32@+4 for FE6) WITHOUT calling the resolver under test.
        /// </summary>
        static List<(uint songId, uint textId)> RawSoundRoomEntries(ROM rom)
        {
            var list = new List<(uint, uint)>();
            uint pointer = rom.RomInfo.sound_room_pointer;
            uint dataSize = rom.RomInfo.sound_room_datasize;
            if (pointer == 0 || dataSize == 0) return list;

            uint baseAddr = rom.p32(U.toOffset(pointer));
            if (!U.isSafetyOffset(baseAddr, rom)) return list;

            uint textOff = (rom.RomInfo.version == 6) ? 4u : 12u;
            uint addr = baseAddr;
            for (int i = 0; i < 0x400; i++, addr += dataSize)
            {
                if (addr + dataSize > (uint)rom.Data.Length) break;
                if (rom.u32(addr) == 0xFFFFFFFF) break;
                if (i > 10 && rom.IsEmpty(addr, dataSize * 10)) break;
                uint songId = rom.u32(addr);
                uint textId = rom.u32(addr + textOff);
                list.Add((songId, textId));
            }
            return list;
        }

        // ================================================================
        // Sound Room name resolution
        // ================================================================

        [Theory]
        [InlineData("FE8J.gba")]
        [InlineData("FE8U.gba")]
        [InlineData("FE7U.gba")]
        [InlineData("FE6.gba")]
        public void SoundRoomSongIds_ResolveToTheirRealRoomNames(string romName)
        {
            WithRealRom(romName, rom =>
            {
                var entries = RawSoundRoomEntries(rom);
                if (entries.Count == 0) return; // skip — no sound room

                int checkedRows = 0;
                foreach (var (songId, textId) in entries)
                {
                    if (textId == 0) continue; // no room name on this row

                    // Independent expected name: decode the room text id directly
                    // and strip control codes the same way the resolver does.
                    string expected =
                        NameResolver.StripControlCodes(FETextDecode.Direct(textId));
                    if (string.IsNullOrEmpty(expected)) continue;

                    string actual = SongNameResolverCore.GetSongName(rom, songId);

                    // The resolver must surface the REAL room name, not the old
                    // "Song 0x..." placeholder.
                    Assert.Equal(expected, actual);
                    Assert.DoesNotContain("Song 0x", actual);

                    // And NameResolver.GetSongName (the routed public API) must
                    // agree for the same id.
                    Assert.Equal(actual, NameResolver.GetSongName(songId));

                    if (++checkedRows >= 5) break; // a handful is plenty
                }

                // We expect at least ONE resolvable room name on a real ROM.
                Assert.True(checkedRows > 0,
                    "expected at least one sound-room entry with a decodable name");
            });
        }

        // ================================================================
        // SE-list fallback
        // ================================================================

        [Theory]
        [InlineData("FE8J.gba")]
        [InlineData("FE8U.gba")]
        public void SeListId_NotInSoundRoom_ResolvesViaSeList(string romName)
        {
            WithRealRom(romName, rom =>
            {
                // Load the SE list independently (same loader the resolver uses).
                string seFile = U.ConfigDataFilename("sound_", rom);
                var seList = U.LoadDicResource(seFile);
                Assert.NotEmpty(seList);

                // Build the set of song ids that ARE in the sound room so we can
                // pick an SE id that is NOT (forcing the SE-list fallback path).
                var roomIds = new HashSet<uint>();
                foreach (var (songId, _) in RawSoundRoomEntries(rom))
                    roomIds.Add(songId);

                int checkedIds = 0;
                foreach (var kv in seList)
                {
                    uint id = kv.Key;
                    string seName = kv.Value;
                    if (string.IsNullOrEmpty(seName)) continue;
                    if (roomIds.Contains(id)) continue; // would resolve via room, not SE

                    string actual = SongNameResolverCore.GetSongName(rom, id);
                    Assert.Equal(seName, actual);
                    Assert.Equal(actual, NameResolver.GetSongName(id));

                    if (++checkedIds >= 5) break;
                }

                Assert.True(checkedIds > 0,
                    "expected at least one SE-list id outside the sound room");
            });
        }

        // ================================================================
        // Unknown-id fallback
        // ================================================================

        [Theory]
        [InlineData("FE8J.gba")]
        [InlineData("FE8U.gba")]
        public void UnknownSongId_FallsBack(string romName)
        {
            WithRealRom(romName, rom =>
            {
                // Pick an id that is neither in the sound room nor the SE list.
                var used = new HashSet<uint>();
                foreach (var (songId, _) in RawSoundRoomEntries(rom))
                    used.Add(songId);
                foreach (var kv in U.LoadDicResource(U.ConfigDataFilename("sound_", rom)))
                    used.Add(kv.Key);

                uint unknown = 0;
                for (uint candidate = 0xF000; candidate < 0xFFFF; candidate++)
                {
                    if (!used.Contains(candidate)) { unknown = candidate; break; }
                }
                Assert.NotEqual(0u, unknown);

                // The Core resolver returns "" for an unresolved id.
                Assert.Equal("", SongNameResolverCore.GetSongName(rom, unknown));

                // The routed public API keeps a safe, identifiable placeholder.
                Assert.Equal($"Song 0x{unknown:X}", NameResolver.GetSongName(unknown));
            });
        }

        // ================================================================
        // Null / guard behaviour (no ROM required)
        // ================================================================

        [Fact]
        public void GetSongName_NullRom_ReturnsEmpty()
        {
            Assert.Equal("", SongNameResolverCore.GetSongName(null, 0x1B));
        }

        [Fact]
        public void GetSoundEffectList_NullRom_ReturnsEmptyDictionary()
        {
            var dic = SongNameResolverCore.GetSoundEffectList(null);
            Assert.NotNull(dic);
            Assert.Empty(dic);
        }

        [Fact]
        public void GetSongNameWhereSongID_NullRom_ReturnsEmpty()
        {
            Assert.Equal("", SongNameResolverCore.GetSongNameWhereSongID(null, 0x1B));
        }
    }
}
