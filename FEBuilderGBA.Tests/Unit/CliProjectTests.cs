using Xunit;
using CliProgram = FEBuilderGBA.CLI.Program;
using FEBuilderGBA.CLI;

namespace FEBuilderGBA.Tests.Unit
{
    [Collection("SharedState")]
    public class CliProjectTests
    {
        [Fact]
        public void ParseArgs_VersionFlag()
        {
            var dic = CliProgram.ParseArgs(new[] { "--version" });
            Assert.True(dic.ContainsKey("--version"));
        }

        [Fact]
        public void ParseArgs_HelpShortFlag()
        {
            var dic = CliProgram.ParseArgs(new[] { "-h" });
            Assert.True(dic.ContainsKey("--help"));
        }

        [Fact]
        public void ParseArgs_KeyValue()
        {
            var dic = CliProgram.ParseArgs(new[] { "--rom=/path/to/rom.gba" });
            Assert.Equal("/path/to/rom.gba", dic["--rom"]);
        }

        [Fact]
        public void ParseArgs_MultipleArgs()
        {
            var dic = CliProgram.ParseArgs(new[] { "--rom=/path/rom.gba", "--makeups=/out.ups", "--fromrom=/clean.gba" });
            Assert.Equal("/path/rom.gba", dic["--rom"]);
            Assert.Equal("/out.ups", dic["--makeups"]);
            Assert.Equal("/clean.gba", dic["--fromrom"]);
        }

        [Fact]
        public void ParseArgs_EmptyArgs()
        {
            var dic = CliProgram.ParseArgs(System.Array.Empty<string>());
            Assert.Empty(dic);
        }

        [Fact]
        public void CliAppServices_ShowError_DoesNotThrow()
        {
            var svc = new CliAppServices();
            svc.ShowError("test error");
        }

        [Fact]
        public void CliAppServices_ShowInfo_DoesNotThrow()
        {
            var svc = new CliAppServices();
            svc.ShowInfo("test info");
        }

        [Fact]
        public void CliAppServices_IsMainThread_ReturnsTrue()
        {
            var svc = new CliAppServices();
            Assert.True(svc.IsMainThread());
        }

        [Fact]
        public void CliAppServices_RunOnUIThread_ExecutesAction()
        {
            var svc = new CliAppServices();
            bool executed = false;
            svc.RunOnUIThread(() => executed = true);
            Assert.True(executed);
        }

        [Fact]
        public void RomLoader_InitEnvironment_RequiresBaseDirectory()
        {
            var origBase = FEBuilderGBA.CoreState.BaseDirectory;
            try
            {
                FEBuilderGBA.CoreState.BaseDirectory = null;
                Assert.Throws<System.InvalidOperationException>(() => RomLoader.InitEnvironment());
            }
            finally
            {
                FEBuilderGBA.CoreState.BaseDirectory = origBase;
            }
        }

        [Fact]
        public void ParseArgs_MakeUpsWithAllRequiredArgs()
        {
            var dic = CliProgram.ParseArgs(new[] { "--makeups=out.ups", "--rom=modified.gba", "--fromrom=original.gba" });
            Assert.Equal("out.ups", dic["--makeups"]);
            Assert.Equal("modified.gba", dic["--rom"]);
            Assert.Equal("original.gba", dic["--fromrom"]);
        }

        // ------------------------------------------------------------------ CLI command source verification

        [Fact]
        public void CliProgram_HasDecreaseColorCommand()
        {
            var src = System.IO.File.ReadAllText(GetCliProgramPath());
            Assert.Contains("--decreasecolor", src);
            Assert.Contains("RunDecreaseColor", src);
        }

        [Fact]
        public void CliProgram_HasPointerCalcCommand()
        {
            var src = System.IO.File.ReadAllText(GetCliProgramPath());
            Assert.Contains("--pointercalc", src);
            Assert.Contains("RunPointerCalc", src);
        }

        [Fact]
        public void CliProgram_HasRebuildCommand()
        {
            var src = System.IO.File.ReadAllText(GetCliProgramPath());
            Assert.Contains("--rebuild", src);
            Assert.Contains("RunRebuild", src);
        }

        [Fact]
        public void CliProgram_HasSongExchangeCommand()
        {
            var src = System.IO.File.ReadAllText(GetCliProgramPath());
            Assert.Contains("--songexchange", src);
            Assert.Contains("RunSongExchange", src);
        }

        [Fact]
        public void CliProgram_HasConvertMap1PictureCommand()
        {
            var src = System.IO.File.ReadAllText(GetCliProgramPath());
            Assert.Contains("--convertmap1picture", src);
            Assert.Contains("RunConvertMap1Picture", src);
        }

        [Fact]
        public void CliProgram_HasTranslateCommand()
        {
            var src = System.IO.File.ReadAllText(GetCliProgramPath());
            Assert.Contains("--translate", src);
            Assert.Contains("RunTranslate", src);
        }

        [Fact]
        public void ParseArgs_ForceVersionFlag()
        {
            var dic = CliProgram.ParseArgs(new[] { "--force-version=FE8U", "--rom=test.gba" });
            Assert.Equal("FE8U", dic["--force-version"]);
            Assert.Equal("test.gba", dic["--rom"]);
        }

        [Fact]
        public void ParseArgs_DecreaseColorSubFlags()
        {
            var dic = CliProgram.ParseArgs(new[] {
                "--decreasecolor", "--in=test.png", "--out=out.png",
                "--noScale", "--noReserve1stColor", "--ignoreTSA"
            });
            Assert.True(dic.ContainsKey("--noScale"));
            Assert.True(dic.ContainsKey("--noReserve1stColor"));
            Assert.True(dic.ContainsKey("--ignoreTSA"));
        }

        [Fact]
        public void ParseArgs_TranslateWithOutFlag()
        {
            var dic = CliProgram.ParseArgs(new[] { "--translate", "--rom=test.gba", "--out=texts.tsv" });
            Assert.True(dic.ContainsKey("--translate"));
            Assert.Equal("texts.tsv", dic["--out"]);
        }

        [Fact]
        public void ParseArgs_TranslateWithInFlag()
        {
            var dic = CliProgram.ParseArgs(new[] { "--translate", "--rom=test.gba", "--in=texts.tsv" });
            Assert.True(dic.ContainsKey("--translate"));
            Assert.Equal("texts.tsv", dic["--in"]);
        }

        [Fact]
        public void CliProgram_HasForceVersionInHelp()
        {
            var src = System.IO.File.ReadAllText(GetCliProgramPath());
            Assert.Contains("--force-version", src);
        }

        [Fact]
        public void CliProgram_HasDecreaseColorSubFlagsInHelp()
        {
            var src = System.IO.File.ReadAllText(GetCliProgramPath());
            Assert.Contains("--noScale", src);
            Assert.Contains("--noReserve1stColor", src);
            Assert.Contains("--ignoreTSA", src);
        }

        [Fact]
        public void CliProgram_TranslateCommandUsesTranslateCore()
        {
            var src = System.IO.File.ReadAllText(GetCliProgramPath());
            Assert.Contains("TranslateCore.DumpTexts", src);
            Assert.Contains("TranslateCore.ExportToTSV", src);
            Assert.Contains("TranslateCore.ImportFromTSV", src);
        }

        [Fact]
        public void RomLoader_LoadRom_HasForceVersionOverload()
        {
            var src = System.IO.File.ReadAllText(GetRomLoaderPath());
            Assert.Contains("LoadForceVersion", src);
            Assert.Contains("forceVersion", src);
        }

        // ------------------------------------------------------------------ New CLI args (cross-platform migration)

        [Fact]
        public void ParseArgs_LastRomFlag()
        {
            var dic = CliProgram.ParseArgs(new[] { "--lastrom" });
            Assert.True(dic.ContainsKey("--lastrom"));
        }

        [Fact]
        public void ParseArgs_ForceDetailFlag()
        {
            var dic = CliProgram.ParseArgs(new[] { "--force-detail" });
            Assert.True(dic.ContainsKey("--force-detail"));
        }

        [Fact]
        public void ParseArgs_TranslateBatchFlag()
        {
            var dic = CliProgram.ParseArgs(new[] { "--translate_batch", "--rom=test.gba", "--out=texts.tsv" });
            Assert.True(dic.ContainsKey("--translate_batch"));
            Assert.Equal("test.gba", dic["--rom"]);
        }

        [Fact]
        public void ParseArgs_TestFlag()
        {
            var dic = CliProgram.ParseArgs(new[] { "--test", "--rom=test.gba" });
            Assert.True(dic.ContainsKey("--test"));
        }

        [Fact]
        public void ParseArgs_TestOnlyFlag()
        {
            var dic = CliProgram.ParseArgs(new[] { "--testonly", "--rom=test.gba" });
            Assert.True(dic.ContainsKey("--testonly"));
        }

        [Fact]
        public void CliProgram_HasLastRomCommand()
        {
            var src = System.IO.File.ReadAllText(GetCliProgramPath());
            Assert.Contains("--lastrom", src);
            Assert.Contains("RunLastRom", src);
        }

        [Fact]
        public void CliProgram_HasForceDetailCommand()
        {
            var src = System.IO.File.ReadAllText(GetCliProgramPath());
            Assert.Contains("--force-detail", src);
        }

        [Fact]
        public void CliProgram_HasTranslateBatchCommand()
        {
            var src = System.IO.File.ReadAllText(GetCliProgramPath());
            Assert.Contains("--translate_batch", src);
            Assert.Contains("RunTranslateBatch", src);
        }

        [Fact]
        public void CliProgram_HasTestCommand()
        {
            var src = System.IO.File.ReadAllText(GetCliProgramPath());
            Assert.Contains("--test", src);
            Assert.Contains("RunSelfTest", src);
        }

        [Fact]
        public void CliProgram_HasTestOnlyCommand()
        {
            var src = System.IO.File.ReadAllText(GetCliProgramPath());
            Assert.Contains("--testonly", src);
        }

        [Fact]
        public void CliProgram_LastRomReadsConfig()
        {
            var src = System.IO.File.ReadAllText(GetCliProgramPath());
            Assert.Contains("Last_Rom_Filename", src);
        }

        [Fact]
        public void CliProgram_TranslateBatchExportsAndImports()
        {
            var src = System.IO.File.ReadAllText(GetCliProgramPath());
            Assert.Contains("TranslateCore.DumpTexts", src);
            Assert.Contains("TranslateCore.WriteTexts", src);
        }

        [Fact]
        public void CliProgram_HelpShowsNewCommands()
        {
            var src = System.IO.File.ReadAllText(GetCliProgramPath());
            Assert.Contains("--lastrom", src);
            Assert.Contains("--force-detail", src);
            Assert.Contains("--translate_batch", src);
            Assert.Contains("--test", src);
            Assert.Contains("--testonly", src);
        }

        private static string GetCliProgramPath()
        {
            var dir = System.AppContext.BaseDirectory;
            while (dir != null && !System.IO.File.Exists(System.IO.Path.Combine(dir, "FEBuilderGBA.sln")))
                dir = System.IO.Path.GetDirectoryName(dir);
            if (dir == null) throw new System.InvalidOperationException("Cannot find solution root");
            return System.IO.Path.Combine(dir, "FEBuilderGBA.CLI", "Program.cs");
        }

        private static string GetRomLoaderPath()
        {
            var dir = System.AppContext.BaseDirectory;
            while (dir != null && !System.IO.File.Exists(System.IO.Path.Combine(dir, "FEBuilderGBA.sln")))
                dir = System.IO.Path.GetDirectoryName(dir);
            if (dir == null) throw new System.InvalidOperationException("Cannot find solution root");
            return System.IO.Path.Combine(dir, "FEBuilderGBA.CLI", "RomLoader.cs");
        }

        private static string GetEventAssemblerCompileCorePath()
        {
            var dir = System.AppContext.BaseDirectory;
            while (dir != null && !System.IO.File.Exists(System.IO.Path.Combine(dir, "FEBuilderGBA.sln")))
                dir = System.IO.Path.GetDirectoryName(dir);
            if (dir == null) throw new System.InvalidOperationException("Cannot find solution root");
            return System.IO.Path.Combine(dir, "FEBuilderGBA.Core", "EventAssemblerCompileCore.cs");
        }

        [Fact]
        public void ParseArgs_SpaceSeparatedValue()
        {
            var dic = CliProgram.ParseArgs(new[] { "--rom", "/path/to/rom.gba", "--makeups=/out.ups" });
            Assert.Equal("/path/to/rom.gba", dic["--rom"]);
            Assert.Equal("/out.ups", dic["--makeups"]);
        }

        [Fact]
        public void ParseArgs_SpaceSeparated_DoesNotConsumeNextFlag()
        {
            var dic = CliProgram.ParseArgs(new[] { "--translate", "--rom=test.gba" });
            Assert.Equal("", dic["--translate"]);
            Assert.Equal("test.gba", dic["--rom"]);
        }

        // ------------------------------------------------------------------ import-midi, compile-event, list-patches filter

        [Fact]
        public void CliProgram_HasImportMidiCommand()
        {
            var src = System.IO.File.ReadAllText(GetCliProgramPath());
            Assert.Contains("--import-midi", src);
            Assert.Contains("RunImportMidi", src);
        }

        [Fact]
        public void CliProgram_HasCompileEventCommand()
        {
            var src = System.IO.File.ReadAllText(GetCliProgramPath());
            Assert.Contains("--compile-event", src);
            Assert.Contains("RunCompileEvent", src);
        }

        [Fact]
        public void CliProgram_HasPatchNameFilter()
        {
            var src = System.IO.File.ReadAllText(GetCliProgramPath());
            Assert.Contains("--patch-name", src);
        }

        [Fact]
        public void ParseArgs_ImportMidiArgs()
        {
            var dic = CliProgram.ParseArgs(new[] { "--import-midi", "--rom=test.gba", "--song-id=0x1A", "--in=song.mid" });
            Assert.True(dic.ContainsKey("--import-midi"));
            Assert.Equal("test.gba", dic["--rom"]);
            Assert.Equal("0x1A", dic["--song-id"]);
            Assert.Equal("song.mid", dic["--in"]);
        }

        [Fact]
        public void ParseArgs_CompileEventArgs()
        {
            var dic = CliProgram.ParseArgs(new[] { "--compile-event", "--rom=test.gba", "--in=script.event", "--out=modified.gba" });
            Assert.True(dic.ContainsKey("--compile-event"));
            Assert.Equal("test.gba", dic["--rom"]);
            Assert.Equal("script.event", dic["--in"]);
            Assert.Equal("modified.gba", dic["--out"]);
        }

        [Fact]
        public void ParseArgs_ListPatchesWithNameFilter()
        {
            var dic = CliProgram.ParseArgs(new[] { "--list-patches", "--rom=test.gba", "--patch-name=Skill" });
            Assert.True(dic.ContainsKey("--list-patches"));
            Assert.Equal("Skill", dic["--patch-name"]);
        }

        [Fact]
        public void CliProgram_ImportMidiUsesCoreSongMidiCore()
        {
            var src = System.IO.File.ReadAllText(GetCliProgramPath());
            Assert.Contains("SongMidiCore.ImportMidiFile", src);
            Assert.Contains("SongMidiCore.ParseMidiFile", src);
        }

        [Fact]
        public void CliProgram_CompileEventUsesToolPathResolver()
        {
            // The compile-event tool-resolution logic was extracted into the shared
            // Core helper EventAssemblerCompileCore (reused by the CLI --compile-event
            // AND the Avalonia tool). The CLI now DELEGATES to that helper, and the
            // helper is the one that resolves the EA exe via ToolPathResolver — so the
            // guarantee (tool resolution goes through ToolPathResolver, not a hardcoded
            // path) lives at its new location. Assert BOTH the delegation and the
            // resolution.
            var cliSrc = System.IO.File.ReadAllText(GetCliProgramPath());
            Assert.Contains("EventAssemblerCompileCore", cliSrc);

            var coreSrc = System.IO.File.ReadAllText(GetEventAssemblerCompileCorePath());
            Assert.Contains("ToolPathResolver.ResolveEventAssembler", coreSrc);
            Assert.Contains("ToolPathResolver.IsColorzCore", coreSrc);
        }

        [Fact]
        public void CliProgram_HelpShowsNewGapCommands()
        {
            var src = System.IO.File.ReadAllText(GetCliProgramPath());
            Assert.Contains("--import-midi", src);
            Assert.Contains("--compile-event", src);
            Assert.Contains("--patch-name", src);
        }

        // ------------------------------------------------------------------ freespace, hex-dump, search-text

        [Fact]
        public void CliProgram_HasFreeSpaceCommand()
        {
            var src = System.IO.File.ReadAllText(GetCliProgramPath());
            Assert.Contains("--freespace", src);
            Assert.Contains("RunFreeSpace", src);
        }

        [Fact]
        public void CliProgram_HasHexDumpCommand()
        {
            var src = System.IO.File.ReadAllText(GetCliProgramPath());
            Assert.Contains("--hex-dump", src);
            Assert.Contains("RunHexDump", src);
        }

        [Fact]
        public void CliProgram_HasSearchTextCommand()
        {
            var src = System.IO.File.ReadAllText(GetCliProgramPath());
            Assert.Contains("--search-text", src);
            Assert.Contains("RunSearchText", src);
        }

        [Fact]
        public void ParseArgs_FreeSpaceArgs()
        {
            var dic = CliProgram.ParseArgs(new[] { "--freespace", "--rom=test.gba", "--min-size=256" });
            Assert.True(dic.ContainsKey("--freespace"));
            Assert.Equal("256", dic["--min-size"]);
        }

        [Fact]
        public void ParseArgs_HexDumpArgs()
        {
            var dic = CliProgram.ParseArgs(new[] { "--hex-dump", "--rom=test.gba", "--addr=0x1000", "--length=512" });
            Assert.True(dic.ContainsKey("--hex-dump"));
            Assert.Equal("0x1000", dic["--addr"]);
            Assert.Equal("512", dic["--length"]);
        }

        [Fact]
        public void ParseArgs_SearchTextArgs()
        {
            var dic = CliProgram.ParseArgs(new[] { "--search-text", "--rom=test.gba", "--query=Eirika" });
            Assert.True(dic.ContainsKey("--search-text"));
            Assert.Equal("Eirika", dic["--query"]);
        }

        // ------------------------------------------------------------------ import-battle-anime

        [Fact]
        public void CliProgram_HasImportBattleAnimeCommand()
        {
            var src = System.IO.File.ReadAllText(GetCliProgramPath());
            Assert.Contains("--import-battle-anime", src);
            Assert.Contains("RunImportBattleAnime", src);
        }

        [Fact]
        public void CliProgram_ImportBattleAnimeUsesCoreClass()
        {
            var src = System.IO.File.ReadAllText(GetCliProgramPath());
            Assert.Contains("BattleAnimeImportCore.ImportBattleAnime", src);
            Assert.Contains("BattleAnimeImportCore.ResolveBattleAnimeAddr", src);
        }

        [Fact]
        public void ParseArgs_ImportBattleAnimeArgs()
        {
            var dic = CliProgram.ParseArgs(new[] { "--import-battle-anime", "--rom=test.gba", "--animation-id=5", "--in=anim.txt" });
            Assert.True(dic.ContainsKey("--import-battle-anime"));
            Assert.Equal("test.gba", dic["--rom"]);
            Assert.Equal("5", dic["--animation-id"]);
            Assert.Equal("anim.txt", dic["--in"]);
        }

        // ------------------------------------------------------------------ export-battle-anime + .bin import

        [Fact]
        public void CliProgram_HasExportBattleAnimeCommand()
        {
            var src = System.IO.File.ReadAllText(GetCliProgramPath());
            Assert.Contains("--export-battle-anime", src);
            Assert.Contains("RunExportBattleAnime", src);
        }

        // ------------------------------------------------------------------ CLI refinement #180: new commands

        [Fact]
        public void CliProgram_HasRomInfoCommand()
        {
            var src = System.IO.File.ReadAllText(GetCliProgramPath());
            Assert.Contains("--rom-info", src);
            Assert.Contains("RunRomInfo", src);
        }

        [Fact]
        public void CliProgram_HasListTablesCommand()
        {
            var src = System.IO.File.ReadAllText(GetCliProgramPath());
            Assert.Contains("--list-tables", src);
            Assert.Contains("RunListTables", src);
        }

        [Fact]
        public void CliProgram_HasExportPaletteCommand()
        {
            var src = System.IO.File.ReadAllText(GetCliProgramPath());
            Assert.Contains("--export-palette", src);
            Assert.Contains("RunExportPalette", src);
        }

        [Fact]
        public void CliProgram_HasImportPaletteCommand()
        {
            var src = System.IO.File.ReadAllText(GetCliProgramPath());
            Assert.Contains("--import-palette", src);
            Assert.Contains("RunImportPalette", src);
        }

        [Fact]
        public void ParseArgs_RomInfoFlag()
        {
            var dic = CliProgram.ParseArgs(new[] { "--rom-info", "--rom=test.gba" });
            Assert.True(dic.ContainsKey("--rom-info"));
            Assert.Equal("test.gba", dic["--rom"]);
        }

        [Fact]
        public void ParseArgs_ListTablesFlag()
        {
            var dic = CliProgram.ParseArgs(new[] { "--list-tables" });
            Assert.True(dic.ContainsKey("--list-tables"));
        }

        [Fact]
        public void ParseArgs_ExportPaletteArgs()
        {
            var dic = CliProgram.ParseArgs(new[] { "--export-palette", "--rom=test.gba", "--addr=0x5524", "--out=pal.pal", "--colors=16" });
            Assert.True(dic.ContainsKey("--export-palette"));
            Assert.Equal("0x5524", dic["--addr"]);
            Assert.Equal("pal.pal", dic["--out"]);
            Assert.Equal("16", dic["--colors"]);
        }

        [Fact]
        public void ParseArgs_ImportPaletteArgs()
        {
            var dic = CliProgram.ParseArgs(new[] { "--import-palette", "--rom=test.gba", "--addr=0x5524", "--in=pal.pal" });
            Assert.True(dic.ContainsKey("--import-palette"));
            Assert.Equal("0x5524", dic["--addr"]);
            Assert.Equal("pal.pal", dic["--in"]);
        }

        [Fact]
        public void CliProgram_HelpShowsRefinementCommands()
        {
            var src = System.IO.File.ReadAllText(GetCliProgramPath());
            Assert.Contains("--rom-info", src);
            Assert.Contains("--list-tables", src);
            Assert.Contains("--export-palette", src);
            Assert.Contains("--import-palette", src);
        }

        [Fact]
        public void CliProgram_ImportPaletteUsesDetectFormat()
        {
            var src = System.IO.File.ReadAllText(GetCliProgramPath());
            Assert.Contains("PaletteFormatConverter.DetectFormat", src);
            Assert.Contains("PaletteFormatConverter.ImportFromFormat", src);
        }

        [Fact]
        public void CliProgram_ExportPaletteUsesFormatFromExtension()
        {
            var src = System.IO.File.ReadAllText(GetCliProgramPath());
            Assert.Contains("PaletteFormatConverter.FormatFromExtension", src);
            Assert.Contains("PaletteFormatConverter.ExportToFormat", src);
        }

        [Fact]
        public void CliProgram_RomInfoUsesExistingHelpers()
        {
            var src = System.IO.File.ReadAllText(GetCliProgramPath());
            Assert.Contains("UPSUtilCore.CRC32", src);
            Assert.Contains("VersionToFilename", src);
        }

        [Fact]
        public void CliProgram_ListTablesUsesStructExportCore()
        {
            var src = System.IO.File.ReadAllText(GetCliProgramPath());
            Assert.Contains("StructExportCore.GetTableNames", src);
        }

        [Fact]
        public void CliProgram_ImportBattleAnimeDetectsBinFormat()
        {
            var src = System.IO.File.ReadAllText(GetCliProgramPath());
            Assert.Contains("ImportFEditorBin", src);
            Assert.Contains(".BIN", src);
        }

        [Fact]
        public void ParseArgs_ExportBattleAnimeArgs()
        {
            var dic = CliProgram.ParseArgs(new[] { "--export-battle-anime", "--rom=test.gba", "--animation-id=3", "--out=anim.txt" });
            Assert.True(dic.ContainsKey("--export-battle-anime"));
            Assert.Equal("3", dic["--animation-id"]);
            Assert.Equal("anim.txt", dic["--out"]);
        }
    }
}
