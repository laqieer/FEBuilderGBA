using System;
using System.Collections.Generic;
using System.IO;
using FEBuilderGBA;

namespace FEBuilderGBA.Core.Tests
{
    [Collection("SharedState")]
    public class FeInfoCodeMapTests
    {
        [Fact]
        public void Parse_BuildsSymbolsWithRegionAddressAndSignature()
        {
            string json = """
[
  {
    "label": "FuncPlain",
    "addr": "8000234",
    "params": [{"type":"u8"}, {"type":"const char*"}],
    "return": {"type":"int"}
  },
  {
    "label": "FuncRegion",
    "addr": {"U":"8000240","J":"8000340"},
    "params": null,
    "return": null
  }
]
""";

            Dictionary<uint, AsmMapSt> map = FeInfoCodeMap.Parse(json, "U");

            Assert.True(map.ContainsKey(U.atoh("8000234")));
            Assert.Equal("FuncPlain", map[U.atoh("8000234")].Name);
            Assert.Equal("RET=int, r0=u8, r1=const char*", map[U.atoh("8000234")].ResultAndArgs);
            Assert.Equal("FuncRegion", map[U.atoh("8000240")].Name);
            Assert.False(map.ContainsKey(U.atoh("8000340")));
        }

        [Fact]
        public void Parse_RegionObjectMissingRegion_SkipsEntry()
        {
            // A J-only symbol must NOT be imported for region U — that would place a wrong
            // label at an address that does not exist in the U ROM. (#1853 review)
            string json = """[{"label":"JOnlyFunc","addr":{"J":"900"},"params":null,"return":null}]""";

            Dictionary<uint, AsmMapSt> mapU = FeInfoCodeMap.Parse(json, "U");
            Assert.Empty(mapU);

            // The SAME entry for region J resolves to its J address.
            Dictionary<uint, AsmMapSt> mapJ = FeInfoCodeMap.Parse(json, "J");
            Assert.Single(mapJ);
            Assert.Equal("JOnlyFunc", mapJ[U.atoh("900")].Name);
        }

        [Fact]
        public void Parse_MoreThanFourParamsUsesPositionalApproximation()
        {
            string json = """
[
  {
    "label": "ManyArgs",
    "addr": "8000300",
    "params": [
      {"type":"p0"}, {"type":"p1"}, {"type":"p2"}, {"type":"p3"}, {"type":"stack_or_extra"}
    ],
    "return": {"type":"void"}
  }
]
""";

            Dictionary<uint, AsmMapSt> map = FeInfoCodeMap.Parse(json, "U");

            Assert.Equal("RET=void, r0=p0, r1=p1, r2=p2, r3=p3, r4=stack_or_extra",
                map[U.atoh("8000300")].ResultAndArgs);
        }

        [Theory]
        [InlineData("")]
        [InlineData("{")]
        [InlineData("{}")]
        [InlineData("""[{"label":"NoAddr"}]""")]
        [InlineData("""[{"addr":"8000234"}]""")]
        [InlineData("""[{"label":"BadAddr","addr":{}}]""")]
        public void Parse_MalformedOrMissingFields_ReturnsEmptyAndNeverThrows(string json)
        {
            Dictionary<uint, AsmMapSt> map = FeInfoCodeMap.Parse(json, "U");

            Assert.Empty(map);
        }

        [Fact]
        public void ResolveCodeJsonPath_MapsSupportedRomRegionsAndRequiresFile()
        {
            string root = CreateArtifactDirectory();
            try
            {
                string fe6Path = Path.Combine(root, "resources", "fe-info", "json", "fe6", "code.json");
                string fe8Path = Path.Combine(root, "resources", "fe-info", "json", "fe8", "code.json");
                Directory.CreateDirectory(Path.GetDirectoryName(fe6Path)!);
                Directory.CreateDirectory(Path.GetDirectoryName(fe8Path)!);
                File.WriteAllText(fe6Path, "[]");
                File.WriteAllText(fe8Path, "[]");

                Assert.Equal(fe6Path, FeInfoCodeMap.ResolveCodeJsonPath(MakeRom("AFEJ01", 0x800000), root, out string fe6Region));
                Assert.Equal("J", fe6Region);
                Assert.Equal(fe8Path, FeInfoCodeMap.ResolveCodeJsonPath(MakeRom("BE8E01", 0x1000000), root, out string fe8uRegion));
                Assert.Equal("U", fe8uRegion);
                Assert.Equal(fe8Path, FeInfoCodeMap.ResolveCodeJsonPath(MakeRom("BE8J01", 0x1000000), root, out string fe8jRegion));
                Assert.Equal("J", fe8jRegion);
                Assert.Null(FeInfoCodeMap.ResolveCodeJsonPath(MakeRom("AE7E01", 0x1000000), root, out string fe7Region));
                Assert.Null(fe7Region);

                File.Delete(fe8Path);
                Assert.Null(FeInfoCodeMap.ResolveCodeJsonPath(MakeRom("BE8E01", 0x1000000), root, out string absentRegion));
                Assert.Equal("U", absentRegion);
            }
            finally
            {
                TryDeleteDirectory(root);
            }
        }

        [Fact]
        public void DisassemblerCoreLoadSymbolMap_MergesFeInfoAndPreservesShippedSymbols()
        {
            string root = CreateArtifactDirectory();
            string originalBase = CoreState.BaseDirectory;
            try
            {
                string dataDir = Path.Combine(root, "config", "data");
                Directory.CreateDirectory(dataDir);
                File.WriteAllText(Path.Combine(dataDir, "asmmap_FE8.txt"),
                    "08000234\tShippedOverlap\tRET=shipped" + Environment.NewLine);

                string feInfoPath = Path.Combine(root, "resources", "fe-info", "json", "fe8", "code.json");
                Directory.CreateDirectory(Path.GetDirectoryName(feInfoPath)!);
                File.WriteAllText(feInfoPath, """
[
  {"label":"FeInfoOverlap","addr":"8000234","params":[{"type":"u8"}],"return":{"type":"int"}},
  {"label":"FeInfoOnly","addr":"8000236","params":[{"type":"u16"}],"return":null}
]
""");

                CoreState.BaseDirectory = root;
                ROM rom = MakeRom("BE8E01", 0x1000000);

                Dictionary<uint, AsmMapSt> map = new DisassemblerCore().LoadSymbolMap(rom);

                Assert.Equal("ShippedOverlap", map[U.atoh("8000234")].Name);
                Assert.Equal("RET=shipped", map[U.atoh("8000234")].ResultAndArgs);
                Assert.Equal("FeInfoOnly", map[U.atoh("8000236")].Name);
                Assert.Equal("r0=u16", map[U.atoh("8000236")].ResultAndArgs);
            }
            finally
            {
                CoreState.BaseDirectory = originalBase;
                TryDeleteDirectory(root);
            }
        }

        static ROM MakeRom(string version, int size)
        {
            byte[] data = new byte[size];
            for (int i = 0; i + 1 < data.Length; i += 2)
            {
                data[i] = 0xC0;
                data[i + 1] = 0x46;
            }

            var rom = new ROM();
            Assert.True(rom.LoadLow("test.gba", data, version));
            return rom;
        }

        static string CreateArtifactDirectory()
        {
            string root = Path.Combine(Directory.GetCurrentDirectory(), "TestArtifacts", "FeInfoCodeMap_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            return root;
        }

        static void TryDeleteDirectory(string path)
        {
            try
            {
                if (Directory.Exists(path))
                    Directory.Delete(path, recursive: true);
            }
            catch
            {
                // Best-effort cleanup only.
            }
        }
    }
}
