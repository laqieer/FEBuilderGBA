using FEBuilderGBA;
using Xunit;

namespace FEBuilderGBA.Avalonia.Tests
{
    /// <summary>
    /// Tests verifying that the ROM test infrastructure (TestRomLocator, RomFixture) works correctly.
    /// Tests skip gracefully when ROMs are not available.
    /// </summary>
    public class RomInfrastructureTests : IClassFixture<RomFixture>
    {
        private readonly RomFixture _fixture;

        public RomInfrastructureTests(RomFixture fixture)
        {
            _fixture = fixture;
        }

        [Fact]
        public void TestRomLocator_RomsDirIsNullOrValid()
        {
            // RomsDir should be null (no roms found) or a valid directory
            if (TestRomLocator.RomsDir != null)
            {
                Assert.True(System.IO.Directory.Exists(TestRomLocator.RomsDir),
                    $"RomsDir '{TestRomLocator.RomsDir}' does not exist");
            }
        }

        [Fact]
        public void TestRomLocator_FindRom_InvalidVersionReturnsNull()
        {
            Assert.Null(TestRomLocator.FindRom("INVALID"));
        }

        [Fact]
        public void TestRomLocator_FindRom_EmptyVersionReturnsNull()
        {
            Assert.Null(TestRomLocator.FindRom(""));
        }

        [Fact]
        public void TestRomLocator_DetectVersion_NonexistentFileReturnsNull()
        {
            Assert.Null(TestRomLocator.DetectVersion("/nonexistent/path/rom.gba"));
        }

        [Fact]
        public void TestRomLocator_AllRoms_YieldsFiveEntries()
        {
            int count = 0;
            foreach (var entry in TestRomLocator.AllRoms)
            {
                Assert.Equal(2, entry.Length);
                Assert.NotNull(entry[0]); // version name is always non-null
                count++;
            }
            Assert.Equal(5, count);
        }

        [Theory]
        [MemberData(nameof(TestRomLocator.AllRoms), MemberType = typeof(TestRomLocator))]
        public void TestRomLocator_FindRom_DetectsCorrectVersion(string version, string? romPath)
        {
            if (romPath == null)
            {
                // ROM not available -- skip
                return;
            }

            Assert.True(System.IO.File.Exists(romPath), $"ROM path does not exist: {romPath}");

            // Verify DetectVersion returns the expected version
            string? detected = TestRomLocator.DetectVersion(romPath);
            Assert.Equal(version, detected);
        }

        [Fact]
        public void RomFixture_LoadsSuccessfully_WhenRomAvailable()
        {
            if (!_fixture.IsAvailable)
            {
                // No ROMs on this machine -- skip
                return;
            }

            Assert.NotNull(_fixture.ROM);
            Assert.NotNull(_fixture.Version);
            Assert.NotNull(_fixture.RomPath);
            Assert.True(System.IO.File.Exists(_fixture.RomPath));
        }

        [Fact]
        public void RomFixture_CoreStateIsWired_WhenRomAvailable()
        {
            if (!_fixture.IsAvailable)
                return;

            Assert.NotNull(CoreState.ROM);
            Assert.Same(_fixture.ROM, CoreState.ROM);
            Assert.NotNull(CoreState.ROM.RomInfo);
            Assert.NotNull(CoreState.CommentCache);
            Assert.NotNull(CoreState.LintCache);
            Assert.NotNull(CoreState.WorkSupportCache);
            Assert.NotNull(CoreState.SystemTextEncoder);
        }

        [Fact]
        public void RomFixture_VersionMatchesRomInfo_WhenRomAvailable()
        {
            if (!_fixture.IsAvailable)
                return;

            var romInfo = _fixture.ROM!.RomInfo;
            int ver = romInfo.version;

            // Verify the numeric version matches the string version
            switch (_fixture.Version)
            {
                case "FE6":
                    Assert.Equal(6, ver);
                    break;
                case "FE7J":
                case "FE7U":
                    Assert.Equal(7, ver);
                    break;
                case "FE8J":
                case "FE8U":
                    Assert.Equal(8, ver);
                    break;
                default:
                    Assert.Fail($"Unexpected version: {_fixture.Version}");
                    break;
            }
        }

        [Fact]
        public void RomFixture_RomDataIsNonEmpty_WhenRomAvailable()
        {
            if (!_fixture.IsAvailable)
                return;

            Assert.NotNull(_fixture.ROM!.Data);
            Assert.True(_fixture.ROM.Data.Length >= 0x800000,
                "ROM data should be at least 8MB (minimum FE6 size)");
        }
    }
}
