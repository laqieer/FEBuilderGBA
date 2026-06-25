// SPDX-License-Identifier: GPL-3.0-or-later
// #1450 — SoundRoom over-255 expansion patch detector tests.
//
// Proves PatchDetection.SearchSoundRoomExpandsPatch (the cross-platform port of
// WinForms PatchUtil.Search_SoundRoomExpands) matches the FE8J/FE8U signatures
// verbatim and is correctly version-gated:
//   - FE8J @ 0xb449c {0x68,0x34,0x21,0x88}
//   - FE8U @ 0xAF87C {0x68,0x34,0x21,0x88}
// The Avalonia Sound Room editor reads this detector to pick its list-expansion
// cap (255 vanilla / 1000 patched).
using System;
using Xunit;
using FEBuilderGBA;

namespace FEBuilderGBA.Core.Tests
{
    [Collection("SharedState")]
    public class PatchDetectionSoundRoomExpandsTests : IDisposable
    {
        readonly ROM? _savedRom;

        public PatchDetectionSoundRoomExpandsTests()
        {
            _savedRom = CoreState.ROM;
        }

        public void Dispose()
        {
            CoreState.ROM = _savedRom;
            PatchDetection.ClearAllCaches();
        }

        const uint Fe8jAddr = 0xb449c;
        const uint Fe8uAddr = 0xAF87C;
        static readonly byte[] Sig = { 0x68, 0x34, 0x21, 0x88 };

        static ROM MakeFe8jRom(bool plant)
        {
            var rom = new ROM();
            var data = new byte[0x1100000];
            if (plant) Array.Copy(Sig, 0, data, Fe8jAddr, Sig.Length);
            rom.LoadLow("synthetic-fe8j.gba", data, "BE8J01");
            return rom;
        }

        static ROM MakeFe8uRom(bool plant)
        {
            var rom = new ROM();
            var data = new byte[0x1100000];
            if (plant) Array.Copy(Sig, 0, data, Fe8uAddr, Sig.Length);
            rom.LoadLow("synthetic-fe8u.gba", data, "BE8E01");
            return rom;
        }

        static ROM MakeFe6Rom()
        {
            var rom = new ROM();
            var data = new byte[0x1100000];
            rom.LoadLow("synthetic-fe6.gba", data, "AE6J00");
            return rom;
        }

        [Fact]
        public void SearchSoundRoomExpandsPatch_NullRom_ReturnsFalse()
        {
            Assert.False(PatchDetection.SearchSoundRoomExpandsPatch((ROM?)null));
        }

        [Fact]
        public void SearchSoundRoomExpandsPatch_FE8J_PatchAbsent_ReturnsFalse()
        {
            Assert.False(PatchDetection.SearchSoundRoomExpandsPatch(MakeFe8jRom(plant: false)));
        }

        [Fact]
        public void SearchSoundRoomExpandsPatch_FE8J_PatchPresent_ReturnsTrue()
        {
            Assert.True(PatchDetection.SearchSoundRoomExpandsPatch(MakeFe8jRom(plant: true)));
        }

        [Fact]
        public void SearchSoundRoomExpandsPatch_FE8U_PatchAbsent_ReturnsFalse()
        {
            Assert.False(PatchDetection.SearchSoundRoomExpandsPatch(MakeFe8uRom(plant: false)));
        }

        [Fact]
        public void SearchSoundRoomExpandsPatch_FE8U_PatchPresent_ReturnsTrue()
        {
            Assert.True(PatchDetection.SearchSoundRoomExpandsPatch(MakeFe8uRom(plant: true)));
        }

        [Fact]
        public void SearchSoundRoomExpandsPatch_FE8U_SigAtFE8JAddr_RejectedByVersionFilter()
        {
            // Plant the FE8J-address signature on an FE8U ROM — the version filter
            // (table entry is "FE8J") must NOT match it.
            var rom = MakeFe8uRom(plant: false);
            Array.Copy(Sig, 0, rom.Data, Fe8jAddr, Sig.Length);
            Assert.False(PatchDetection.SearchSoundRoomExpandsPatch(rom));
        }

        [Fact]
        public void SearchSoundRoomExpandsPatch_FE6_AlwaysFalse()
        {
            // The table only contains FE8J/FE8U — a non-FE8 ROM never matches even
            // with the bytes planted at both addresses.
            var rom = MakeFe6Rom();
            Array.Copy(Sig, 0, rom.Data, Fe8jAddr, Sig.Length);
            Array.Copy(Sig, 0, rom.Data, Fe8uAddr, Sig.Length);
            Assert.False(PatchDetection.SearchSoundRoomExpandsPatch(rom));
        }

        [Fact]
        public void SearchSoundRoomExpandsPatch_AmbientOverload_ReadsCoreStateRom()
        {
            CoreState.ROM = MakeFe8uRom(plant: true);
            Assert.True(PatchDetection.SearchSoundRoomExpandsPatch());
            CoreState.ROM = MakeFe8uRom(plant: false);
            Assert.False(PatchDetection.SearchSoundRoomExpandsPatch());
        }
    }
}
