// SPDX-License-Identifier: GPL-3.0-or-later
// #1978 Slice 3: shared, fully synthetic (non-copyrighted) ROM fixtures for the one-click
// Map Editor integration tests (RandomMapOneClickWorkflowTests / RandomMapOneClickServiceTests /
// OptionsViewModelTilesetMappingTests). Deliberately duplicated from (rather than referencing)
// FEBuilderGBA.Core.Tests's BuiltInRandomMapTestFixture: the two test assemblies are independent
// and this is test-only scaffolding, not production logic.
using System;
using System.IO;
using System.Reflection;
using global::Avalonia.Threading;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;
using Xunit;

namespace FEBuilderGBA.Avalonia.Tests
{
    static class RandomMapOneClickTestSupport
    {
        // ------------------------------------------------------------------
        // Small identity-only ROM (0x8000-byte "NAZO" fallback version): used by tests that only
        // exercise MapEditorViewModel's write-identity/apply/rollback plumbing and never need a
        // resolvable OBJ/PAL/CFG tileset.
        // ------------------------------------------------------------------

        public static ROM CreateRom(int size = 0x8000)
        {
            byte[] data = new byte[size];
            for (int i = 0; i < size; i++) data[i] = 0xAA;
            for (int i = size / 2; i < size / 2 + 0x1000; i++) data[i] = 0x00;

            var rom = new ROM();
            Assert.True(rom.LoadLow("synthetic.gba", data, "NAZO"));
            return rom;
        }

        public static byte[] BuildMap(int width, int height, ushort fill)
        {
            byte[] map = new byte[2 + width * height * 2];
            map[0] = (byte)width;
            map[1] = (byte)height;
            for (int i = 0; i < width * height; i++)
            {
                int off = 2 + i * 2;
                map[off] = (byte)(fill & 0xFF);
                map[off + 1] = (byte)(fill >> 8);
            }
            return map;
        }

        public static void SeedMapData(MapEditorViewModel vm, byte[] mapData)
            => SetPrivateField(vm, "_cachedMapData", (byte[])mapData.Clone());

        public static void ConfigureMapPointerIdentity(
            MapEditorViewModel vm,
            uint pointerEntryAddr,
            byte mapPlist)
        {
            ROM rom = CoreState.ROM!;
            const uint tablePointerAddr = 0x220;
            uint tableBase = pointerEntryAddr - mapPlist * 4u;
            PropertyInfo? property = rom.RomInfo.GetType().BaseType?.GetProperty(
                "map_map_pointer_pointer",
                BindingFlags.Public | BindingFlags.Instance);
            Assert.NotNull(property);
            property!.SetValue(rom.RomInfo, tablePointerAddr);
            rom.write_p32(tablePointerAddr, tableBase);
            rom.write_u8(vm.CurrentAddr + 8, mapPlist);
        }

        public static string FindRepoRoot()
        {
            string? directory = AppContext.BaseDirectory;
            while (directory != null && !File.Exists(Path.Combine(directory, "FEBuilderGBA.sln")))
                directory = Path.GetDirectoryName(directory);
            Assert.NotNull(directory);
            return directory!;
        }

        public static void SetPrivateField(object target, string name, object value)
        {
            FieldInfo? field = target.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.NotNull(field);
            field!.SetValue(target, value);
        }

        // ------------------------------------------------------------------
        // Fully resolvable-tileset ROM ("BE8E01" / ROMFE8U, 16 MiB): the only game code large
        // enough to resolve a real ROMFEINFO subclass with real default OBJ/PAL/CFG/MAP table
        // pointers (see Rom.cs LoadLow). Every table/blob below is written at the ROM's OWN
        // already-resolved default pointer targets — never via reflection — the same approach
        // FEBuilderGBA.Core.Tests's BuiltInRandomMapTestFixture uses.
        // ------------------------------------------------------------------

        const uint MapSettingAddr = 0x00600000;
        const uint ObjTableAddr = 0x00610000;
        const uint PalTableAddr = 0x00611000;
        const uint ConfigTableAddr = 0x00612000;
        const uint MapTableAddr = 0x00613000;
        const uint ObjBlobAddr = 0x00620000;
        const uint PalBlobAddr = 0x00621000;
        const uint ConfigBlobAddr = 0x00622000;
        const uint MapBlobAddr = 0x00623000;

        public static ROM CreateResolvableTilesetRom(
            int width,
            int height,
            ushort fill,
            out uint mapSettingAddr,
            out uint pointerEntryAddr,
            out byte[] originalMapBytes,
            int tilesetSlot = 1)
        {
            var rom = new ROM();
            Assert.True(rom.LoadLow("synthetic.gba", new byte[0x1000000], "BE8E01"));

            WriteU32(rom, rom.RomInfo.map_obj_pointer, 0x08000000 + ObjTableAddr);
            WriteU32(rom, rom.RomInfo.map_pal_pointer, 0x08000000 + PalTableAddr);
            WriteU32(rom, rom.RomInfo.map_config_pointer, 0x08000000 + ConfigTableAddr);
            WriteU32(rom, rom.RomInfo.map_map_pointer_pointer, 0x08000000 + MapTableAddr);

            byte[] objCompressed = LZ77.compress(new byte[] { 1, 2, 3, 4 });
            byte[] configCompressed = LZ77.compress(new byte[] { 5, 6, 7, 8 });
            byte[] palRaw = new byte[512];

            Array.Copy(objCompressed, 0, rom.Data, ObjBlobAddr, objCompressed.Length);
            Array.Copy(palRaw, 0, rom.Data, PalBlobAddr, palRaw.Length);
            Array.Copy(configCompressed, 0, rom.Data, ConfigBlobAddr, configCompressed.Length);

            WriteU32(rom, ObjTableAddr + (uint)tilesetSlot * 4, 0x08000000 + ObjBlobAddr);
            WriteU32(rom, PalTableAddr + (uint)tilesetSlot * 4, 0x08000000 + PalBlobAddr);
            WriteU32(rom, ConfigTableAddr + (uint)tilesetSlot * 4, 0x08000000 + ConfigBlobAddr);

            originalMapBytes = BuildMap(width, height, fill);
            byte[] mapCompressed = LZ77.compress(originalMapBytes);
            Array.Copy(mapCompressed, 0, rom.Data, MapBlobAddr, mapCompressed.Length);
            WriteU32(rom, MapTableAddr + (uint)tilesetSlot * 4, 0x08000000 + MapBlobAddr);

            mapSettingAddr = MapSettingAddr;
            WriteU32(rom, mapSettingAddr + 0, 0x08000001); // dummy valid pointer
            WriteU16(rom, mapSettingAddr + 4, (ushort)tilesetSlot); // objPlist (obj2Plist high byte = 0)
            rom.Data[mapSettingAddr + 6] = (byte)tilesetSlot; // palettePlist
            rom.Data[mapSettingAddr + 7] = (byte)tilesetSlot; // configPlist
            rom.Data[mapSettingAddr + 8] = (byte)tilesetSlot; // mapPointerPlist

            pointerEntryAddr = MapTableAddr + (uint)tilesetSlot * 4;
            return rom;
        }

        /// <summary>
        /// Overwrite the OBJ blob with different (still LZ77-valid) content, changing the
        /// resulting <see cref="TilesetFingerprint"/> without touching the map's own pointer
        /// entry or decompressed bytes — simulates a tileset edit happening concurrently with
        /// random-map generation.
        /// </summary>
        public static void MutateObjTileset(ROM rom, int tilesetSlot = 1)
        {
            byte[] mutated = LZ77.compress(new byte[] { 99, 98, 97, 96, 95 });
            Array.Clear(rom.Data, (int)ObjBlobAddr, 0x1000);
            Array.Copy(mutated, 0, rom.Data, ObjBlobAddr, mutated.Length);
            _ = tilesetSlot;
        }

        public static void WriteU32(ROM rom, uint addr, uint value)
        {
            rom.Data[addr + 0] = (byte)(value & 0xFF);
            rom.Data[addr + 1] = (byte)((value >> 8) & 0xFF);
            rom.Data[addr + 2] = (byte)((value >> 16) & 0xFF);
            rom.Data[addr + 3] = (byte)((value >> 24) & 0xFF);
        }

        public static void WriteU16(ROM rom, uint addr, ushort value)
        {
            rom.Data[addr + 0] = (byte)(value & 0xFF);
            rom.Data[addr + 1] = (byte)((value >> 8) & 0xFF);
        }

        public sealed class RecordingUndoService : UndoService
        {
            readonly System.Collections.Generic.List<string> _sequence;

            public RecordingUndoService(System.Collections.Generic.List<string> sequence)
            {
                _sequence = sequence;
            }

            public int BeginCalls { get; private set; }
            public int CommitCalls { get; private set; }
            public int RollbackCalls { get; private set; }
            public string LastBeginName { get; private set; } = "";
            public bool BeginOnUiThread { get; private set; }
            public bool CommitOnUiThread { get; private set; }

            public override void Begin(string name)
            {
                BeginCalls++;
                LastBeginName = name;
                BeginOnUiThread = Dispatcher.UIThread.CheckAccess();
                _sequence.Add("Begin");
                base.Begin(name);
            }

            public override void Commit()
            {
                CommitCalls++;
                CommitOnUiThread = Dispatcher.UIThread.CheckAccess();
                _sequence.Add("Commit");
                base.Commit();
            }

            public override void Rollback()
            {
                RollbackCalls++;
                _sequence.Add("Rollback");
                base.Rollback();
            }
        }
    }
}
