using System;
using FEBuilderGBA;
using Xunit;
using Xunit.Abstractions;

namespace FEBuilderGBA.Avalonia.Tests
{
    /// <summary>
    /// Tests for the Avalonia "Export World Map Events in EA format" button
    /// (<c>ToolExportEAEventView.ExportWMapEvents_Click</c>), which previously
    /// only worked for FE8 — for FE6/FE7 it always reported "World map events
    /// not available for this ROM version." because it read the FE8-only
    /// <c>worldmap_event_on_stageclear_pointer</c> (0x0 on FE6/FE7).
    /// Issue #1420.
    ///
    /// The view handler delegates to <see cref="WorldMapEventResolverCore"/>;
    /// these tests load each version ROM explicitly and assert the resolver
    /// (the exact call the handler makes) returns a valid event address for
    /// FE6/FE7/FE8 — i.e. the export is no longer "not available" on FE6/FE7.
    /// </summary>
    [Collection("SharedState")]
    public class ToolExportEAEventWorldMapTests
    {
        private readonly ITestOutputHelper _output;
        public ToolExportEAEventWorldMapTests(ITestOutputHelper output) { _output = output; }

        [Theory]
        [InlineData("FE6", 6)]
        [InlineData("FE7U", 7)]
        [InlineData("FE7J", 7)]
        [InlineData("FE8U", 8)]
        [InlineData("FE8J", 8)]
        public void ExportWorldMapEvents_ResolvesForVersion(string version, int expectVersion)
        {
            string? path = TestRomLocator.FindRom(version);
            if (path == null)
            {
                _output.WriteLine($"{version}.gba not available — skipping.");
                return;
            }

            var saved = CoreState.ROM;
            var savedEnc = CoreState.SystemTextEncoder;
            try
            {
                var rom = new ROM();
                if (!rom.Load(path, out string _))
                {
                    _output.WriteLine($"{version}: load failed — skipping.");
                    return;
                }
                CoreState.ROM = rom;
                CoreState.SystemTextEncoder = new HeadlessSystemTextEncoder(rom);

                Assert.Equal(expectVersion, rom.RomInfo.version);

                // The "clear" world-map-event pointer is 0x0 on FE6/FE7 — the old
                // handler bailed here. The resolver must NOT depend on it.
                if (expectVersion != 8)
                {
                    Assert.Equal(0u, rom.RomInfo.worldmap_event_on_stageclear_pointer);
                }

                int hits = 0;
                uint firstAddr = U.NOT_FOUND;
                uint firstMap = 0;
                var maps = MapSettingCore.MakeMapIDList(rom);
                for (uint mapId = 0; mapId < maps.Count; mapId++)
                {
                    uint mapAddr = MapSettingCore.GetMapAddr(rom, mapId);
                    if (!U.isSafetyOffset(mapAddr, rom)) continue;
                    uint plistPos = rom.RomInfo.map_setting_worldmap_plist_pos;
                    if (!U.isSafetyOffset(mapAddr + plistPos, rom)) continue;
                    if (rom.u8(mapAddr + plistPos) == 0) continue;

                    // Exactly the call ExportWMapEvents_Click makes.
                    uint addr = WorldMapEventResolverCore.GetEventByMapID(rom, mapId, isSelect: false);
                    if (addr != U.NOT_FOUND && U.isSafetyOffset(addr, rom))
                    {
                        hits++;
                        if (firstAddr == U.NOT_FOUND) { firstAddr = addr; firstMap = mapId; }
                    }
                }

                _output.WriteLine($"{version} v{rom.RomInfo.version}: world-map-event resolves on {hits} map(s); first map={firstMap} addr=0x{firstAddr:X8}");

                // Every vanilla FE6/FE7/FE8 ROM ships world-map events — the FE6/FE7
                // export must now resolve at least one (the bug being fixed).
                Assert.True(hits > 0, $"{version}: no world-map event resolved (export still 'not available'?)");
            }
            finally
            {
                CoreState.ROM = saved;
                CoreState.SystemTextEncoder = savedEnc;
            }
        }

        [Fact]
        public void Resolver_NullRom_NeverThrows()
        {
            Assert.Equal(U.NOT_FOUND, WorldMapEventResolverCore.GetEventByMapID(null, 0));
        }
    }
}
