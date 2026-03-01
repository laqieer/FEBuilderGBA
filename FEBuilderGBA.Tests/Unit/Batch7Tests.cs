using System;
using System.Text;
using Xunit;
using FEBuilderGBA;

namespace FEBuilderGBA.Tests.Unit
{
    [Collection("SharedState")]
    public class Batch7Tests
    {
        // ---- ExportFunction ----

        [Fact]
        public void ExportFunction_AddAndExport()
        {
            // isSafetyOffset needs a ROM for bounds checking
            var savedRom = CoreState.ROM;
            try
            {
                var rom = new ROM();
                rom.LoadLow("test.gba", new byte[0x1000000], "NAZO");
                CoreState.ROM = rom;

                var ef = new ExportFunction();
                ef.Add("TestFunc", 0x08001000);

                var sb = new StringBuilder();
                ef.ExportEA(sb);

                string result = sb.ToString();
                Assert.Contains("ORG", result);
                Assert.Contains("TestFunc", result);
            }
            finally
            {
                CoreState.ROM = savedRom;
            }
        }

        [Fact]
        public void ExportFunction_Clear()
        {
            var ef = new ExportFunction();
            ef.Add("Func1", 0x100);
            ef.Clear();

            var sb = new StringBuilder();
            ef.ExportEA(sb);

            Assert.Equal("", sb.ToString());
        }

        [Fact]
        public void ExportFunction_GetDic()
        {
            var ef = new ExportFunction();
            ef.Add("A", 1);
            ef.Add("B", 2);

            var dic = ef.GetDic();
            Assert.Equal(2, dic.Count);
            Assert.Equal((uint)1, dic["A"]);
            Assert.Equal((uint)2, dic["B"]);
        }

        [Fact]
        public void ExportFunction_One_SkipsInvalidAddress()
        {
            var sb = new StringBuilder();
            ExportFunction.One(sb, "Bad", 0); // addr 0 should be skipped
            Assert.Equal("", sb.ToString());
        }

        // ---- UpdateInfo ----

        [Fact]
        public void UpdateInfo_CompareVersions_Newer()
        {
            Assert.True(UpdateInfo.CompareVersions("20260101.00", "20260201.00") < 0);
        }

        [Fact]
        public void UpdateInfo_CompareVersions_Equal()
        {
            Assert.Equal(0, UpdateInfo.CompareVersions("20260101.00", "20260101.00"));
        }

        [Fact]
        public void UpdateInfo_CompareVersions_Older()
        {
            Assert.True(UpdateInfo.CompareVersions("20260301.00", "20260201.00") > 0);
        }

        [Fact]
        public void UpdateInfo_IsValidVersion()
        {
            Assert.True(UpdateInfo.IsValidVersion("20260301.00"));
            Assert.False(UpdateInfo.IsValidVersion(""));
            Assert.False(UpdateInfo.IsValidVersion(null));
            Assert.False(UpdateInfo.IsValidVersion("abc"));
            Assert.False(UpdateInfo.IsValidVersion("2026030100")); // no dot
        }

        [Fact]
        public void UpdateInfo_DetermineUpdateType()
        {
            var info = new UpdateInfo();
            // Current version is the build date; test with a far-future version
            Assert.Equal(UpdateInfo.PackageType.CoreOnly,
                info.DetermineUpdateType("99991231.23"));
        }

        [Fact]
        public void UpdateInfo_HasUrl_False_WhenNotSet()
        {
            var info = new UpdateInfo();
            Assert.False(info.HasUrl(UpdateInfo.PackageType.CoreOnly));
        }

        [Fact]
        public void UpdateInfo_HasUrl_True_WhenSet()
        {
            var info = new UpdateInfo();
            info.URL_CORE = "https://example.com/update.zip";
            Assert.True(info.HasUrl(UpdateInfo.PackageType.CoreOnly));
            Assert.Equal("https://example.com/update.zip",
                info.GetDownloadUrl(UpdateInfo.PackageType.CoreOnly));
        }

        // ---- NewEventASM ----

        [Fact]
        public void NewEventASM_Constructor_CreatesUnknown()
        {
            var nea = new NewEventASM();
            Assert.NotNull(nea.Unknown);
            Assert.Equal(4, nea.Unknown.Size);
            Assert.Single(nea.Unknown.Args);
        }

        [Fact]
        public void NewEventASM_ArgType_EnumValues()
        {
            // Verify key enum values exist
            Assert.Equal(0, (int)NewEventASM.ArgType.ArgType_None);
            Assert.True(Enum.IsDefined(typeof(NewEventASM.ArgType), NewEventASM.ArgType.ArgType_UNIT));
            Assert.True(Enum.IsDefined(typeof(NewEventASM.ArgType), NewEventASM.ArgType.ArgType_TEXT));
        }

        [Fact]
        public void NewEventASM_GetArgValue_ReadsBytes()
        {
            var code = new NewEventASM.OneCode();
            code.ByteData = new byte[] { 0x01, 0x42, 0x00, 0x00 };
            code.Script = new NewEventASM.Script();
            code.Script.Args = new NewEventASM.Arg[]
            {
                new NewEventASM.Arg { Position = 0, Size = 1, Type = NewEventASM.ArgType.ArgType_None },
                new NewEventASM.Arg { Position = 1, Size = 1, Type = NewEventASM.ArgType.ArgType_None },
            };

            Assert.Equal((uint)0x01, NewEventASM.GetArgValue(code, 0));
            Assert.Equal((uint)0x42, NewEventASM.GetArgValue(code, 1));
        }

        // ---- TranslateManager ----

        [Fact]
        public void TranslateEngineEnum_HasExpectedValues()
        {
            Assert.Equal(0, (int)TranslateEngineEnum.Google);
            Assert.Equal(1, (int)TranslateEngineEnum.Google2);
        }

        [Fact]
        public void TranslateManage_Constructor_DoesNotThrow()
        {
            var tm = new TranslateManage();
            Assert.NotNull(tm);
        }

        // ---- U string utility methods (moved to Core) ----

        [Fact]
        public void U_cut_ExtractsBetweenDelimiters()
        {
            string result = U.cut("hello<start>content<end>world", "<start>", "<end>");
            Assert.Equal("content", result);
        }

        [Fact]
        public void U_cut_ReturnsEmpty_WhenNotFound()
        {
            Assert.Equal("", U.cut("hello world", "<start>", "<end>"));
        }

        [Fact]
        public void U_cut_SingleDelimiter()
        {
            Assert.Equal("hello", U.cut("hello world", " "));
        }

        [Fact]
        public void U_unhtmlspecialchars_DecodesEntities()
        {
            string input = "&gt;&lt;&quot;&apos;&#45;";
            string expected = "><\"'-";
            Assert.Equal(expected, U.unhtmlspecialchars(input));
        }

        [Fact]
        public void U_unhtmlspecialchars_PassesThroughNormal()
        {
            Assert.Equal("hello world", U.unhtmlspecialchars("hello world"));
        }
    }
}
