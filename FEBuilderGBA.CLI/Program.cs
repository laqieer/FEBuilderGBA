using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using FEBuilderGBA;
using FEBuilderGBA.SkiaSharp;

namespace FEBuilderGBA.CLI
{
    static class Program
    {
        static int Main(string[] args)
        {
            var argsDic = ParseArgs(args);

            // Set up base directory (where the exe lives)
            string baseDir = Path.GetDirectoryName(Path.GetFullPath(Environment.GetCommandLineArgs()[0]));
            CoreState.BaseDirectory = baseDir;
            CoreState.IsCommandLine = true;
            CoreState.ArgsDic = argsDic;
            CoreState.Services = new CliAppServices();
            CoreState.ImageService = new SkiaImageService();

            if (argsDic.ContainsKey("--help") || argsDic.ContainsKey("-h") || args.Length == 0)
            {
                PrintHelp();
                return 0;
            }

            if (argsDic.ContainsKey("--version"))
            {
                PrintVersion();
                return 0;
            }

            if (argsDic.ContainsKey("--makeups"))
            {
                return RunMakeUps(argsDic);
            }

            if (argsDic.ContainsKey("--applyups"))
            {
                return RunApplyUps(argsDic);
            }

            if (argsDic.ContainsKey("--lint"))
            {
                return RunLint(argsDic);
            }

            if (argsDic.ContainsKey("--disasm"))
            {
                return RunDisasm(argsDic);
            }

            if (argsDic.ContainsKey("--decreasecolor"))
            {
                return RunDecreaseColor(argsDic);
            }

            if (argsDic.ContainsKey("--pointercalc"))
            {
                return RunPointerCalc(argsDic);
            }

            if (argsDic.ContainsKey("--rebuild"))
            {
                return RunRebuild(argsDic);
            }

            if (argsDic.ContainsKey("--songexchange"))
            {
                return RunSongExchange(argsDic);
            }

            if (argsDic.ContainsKey("--convertmap1picture"))
            {
                return RunConvertMap1Picture(argsDic);
            }

            if (argsDic.ContainsKey("--translate-roundtrip"))
            {
                return RunTranslateRoundTrip(argsDic);
            }

            if (argsDic.ContainsKey("--translate"))
            {
                return RunTranslate(argsDic);
            }

            if (argsDic.ContainsKey("--data-roundtrip"))
            {
                return RunDataRoundTrip(argsDic);
            }

            if (argsDic.ContainsKey("--export-data"))
            {
                return RunExportData(argsDic);
            }

            if (argsDic.ContainsKey("--import-data"))
            {
                return RunImportData(argsDic);
            }

            if (argsDic.ContainsKey("--lastrom"))
            {
                return RunLastRom(argsDic);
            }

            if (argsDic.ContainsKey("--force-detail"))
            {
                // In CLI context, --force-detail just sets the flag and prints info.
                // It is primarily used by Avalonia GUI to skip easy-mode.
                Console.WriteLine("--force-detail: Detail mode flag acknowledged.");
                Console.WriteLine("This flag is primarily used by the Avalonia GUI to force detailed editor mode.");
                return 0;
            }

            if (argsDic.ContainsKey("--translate_batch"))
            {
                return RunTranslateBatch(argsDic);
            }

            if (argsDic.ContainsKey("--resolve-names"))
            {
                return RunResolveNames(argsDic);
            }

            if (argsDic.ContainsKey("--render-portrait"))
            {
                return RunRenderPortrait(argsDic);
            }

            if (argsDic.ContainsKey("--export-portrait-all"))
            {
                return RunExportPortraitAll(argsDic);
            }

            if (argsDic.ContainsKey("--generate-font"))
            {
                return RunGenerateFont(argsDic);
            }

            if (argsDic.ContainsKey("--import-portrait-all"))
            {
                return RunImportPortraitAll(argsDic);
            }

            if (argsDic.ContainsKey("--import-portrait"))
            {
                return RunImportPortrait(argsDic);
            }

            if (argsDic.ContainsKey("--export-midi"))
            {
                return RunExportMidi(argsDic);
            }

            if (argsDic.ContainsKey("--disasm-event"))
            {
                return RunDisasmEvent(argsDic);
            }

            if (argsDic.ContainsKey("--lint-oam"))
            {
                return RunLintOAM(argsDic);
            }

            if (argsDic.ContainsKey("--apply-patch"))
            {
                return RunApplyPatch(argsDic);
            }

            if (argsDic.ContainsKey("--list-patches"))
            {
                return RunListPatches(argsDic);
            }

            if (argsDic.ContainsKey("--list-resources"))
            {
                return RunListResources(argsDic);
            }

            if (argsDic.ContainsKey("--uninstall-patch"))
            {
                return RunUninstallPatch(argsDic);
            }

            if (argsDic.ContainsKey("--expand-table"))
            {
                return RunExpandTable(argsDic);
            }

            if (argsDic.ContainsKey("--merge3"))
            {
                return RunThreeWayMerge(argsDic);
            }

            if (argsDic.ContainsKey("--import-midi"))
            {
                return RunImportMidi(argsDic);
            }

            if (argsDic.ContainsKey("--compile-event"))
            {
                return RunCompileEvent(argsDic);
            }

            if (argsDic.ContainsKey("--import-battle-anime"))
            {
                return RunImportBattleAnime(argsDic);
            }

            if (argsDic.ContainsKey("--export-battle-anime"))
            {
                return RunExportBattleAnime(argsDic);
            }

            if (argsDic.ContainsKey("--freespace"))
            {
                return RunFreeSpace(argsDic);
            }

            if (argsDic.ContainsKey("--hex-dump"))
            {
                return RunHexDump(argsDic);
            }

            if (argsDic.ContainsKey("--search-text"))
            {
                return RunSearchText(argsDic);
            }

            if (argsDic.ContainsKey("--export-map-settings"))
            {
                return RunExportMapSettings(argsDic);
            }

            if (argsDic.ContainsKey("--diff"))
            {
                return RunDiff(argsDic);
            }

            if (argsDic.ContainsKey("--test") || argsDic.ContainsKey("--testonly"))
            {
                return RunSelfTest(argsDic);
            }

            // Other commands not yet implemented
            Console.Error.WriteLine("Command not yet supported in cross-platform CLI.");
            Console.Error.WriteLine("Run with --help for usage information.");
            return 1;
        }

        static void PrintVersion()
        {
            string version = U.getVersion();
            Console.WriteLine($"FEBuilderGBA Version:{version}");
            Console.WriteLine("Copyright: 2017-");
            Console.WriteLine("License: GPLv3");
        }

        static void PrintHelp()
        {
            Console.WriteLine("FEBuilderGBA CLI - Cross-platform ROM hacking tool");
            Console.WriteLine();
            Console.WriteLine("Usage: FEBuilderGBA.CLI [options]");
            Console.WriteLine();
            Console.WriteLine("Options:");
            Console.WriteLine("  --version                Show version information");
            Console.WriteLine("  --help, -h               Show this help message");
            Console.WriteLine("  --rom=<path>             Specify ROM file to load");
            Console.WriteLine("  --force-version=<VER>    Force ROM version detection (FE6, FE7J, FE7U, FE8J, FE8U)");
            Console.WriteLine("  --makeups=<path>         Create UPS patch (requires --rom and --fromrom)");
            Console.WriteLine("  --applyups=<path>        Apply UPS patch (requires --rom and --patch)");
            Console.WriteLine("  --lint                   Run lint checks on ROM (requires --rom)");
            Console.WriteLine("  --disasm=<path>          Disassemble ROM to file (requires --rom)");
            Console.WriteLine("  --decreasecolor          Quantize image palette (requires --in, --out, --paletteno)");
            Console.WriteLine("    --noScale              Do not scale colors to GBA 5-bit range");
            Console.WriteLine("    --noReserve1stColor    Do not reserve palette slot 0 for transparency");
            Console.WriteLine("    --ignoreTSA            Ignore TSA tile deduplication constraints");
            Console.WriteLine("  --pointercalc            Search pointer references (requires --rom, --target, --address)");
            Console.WriteLine("  --rebuild                Rebuild/defragment ROM (requires --rom, --fromrom)");
            Console.WriteLine("  --songexchange           Copy song between ROMs (requires --rom, --fromrom, --fromsong, --tosong)");
            Console.WriteLine("  --convertmap1picture     Convert image to map tiles (requires --in, --outImg, --outTSA)");
            Console.WriteLine("  --translate              Dump or import ROM text (requires --rom)");
            Console.WriteLine("    --out=<path>           Export text to TSV file");
            Console.WriteLine("    --in=<path>            Import text from TSV file and write to ROM");
            Console.WriteLine("  --translate-roundtrip    Validate text export/import losslessness (requires --rom)");
            Console.WriteLine("    --out=<base>           Save before/after TSVs as <base>.export1.tsv and <base>.export2.tsv");
            Console.WriteLine("  --export-data            Export struct data to TSV/CSV/EA (requires --rom, --table)");
            Console.WriteLine("    --table=<name>         Table name: units, classes, items, or all");
            Console.WriteLine("    --out=<path>           Output file path (or base path for --table=all)");
            Console.WriteLine("    --format=<fmt>         Output format: tsv (default), csv, ea");
            Console.WriteLine("  --import-data            Import struct data from TSV (requires --rom, --table, --in)");
            Console.WriteLine("    --table=<name>         Table name: units, classes, items");
            Console.WriteLine("    --in=<path>            Input TSV file path");
            Console.WriteLine("  --data-roundtrip         Validate struct data export/import losslessness (requires --rom)");
            Console.WriteLine("    --table=<name>         Table name: units, classes, items, or all (default: all)");
            Console.WriteLine("  --lastrom                Load last-used ROM from config");
            Console.WriteLine("  --force-detail           Force detailed editor mode (Avalonia GUI)");
            Console.WriteLine("  --translate_batch        Batch translation: export + import all text");
            Console.WriteLine("  --resolve-names          Resolve entity IDs to names (requires --rom, --kind, --ids)");
            Console.WriteLine("    --kind=<type>          Entity type: unit, class, item, song");
            Console.WriteLine("    --ids=<list>           Comma-separated IDs (e.g., 0,1,2,3)");
            Console.WriteLine("  --render-portrait        Render unit portrait to PNG (requires --rom, --unit-id, --out)");
            Console.WriteLine("    --unit-id=<id>         Unit index number");
            Console.WriteLine("    --out=<path>           Output PNG file path");
            Console.WriteLine("  --export-portrait-all    Export all portraits to PNG files (requires --rom, --out)");
            Console.WriteLine("    --out=<dir>            Output directory for portrait PNGs");
            Console.WriteLine("  --generate-font          Generate font bitmap from text using system or .ttf font (requires --out)");
            Console.WriteLine("    --text=<chars>         Characters to generate (e.g., \"ABC\")");
            Console.WriteLine("    --font-file=<path>     Path to .ttf/.otf font file (optional; uses system default if omitted)");
            Console.WriteLine("    --font-size=<float>    Font size in points (default: 12)");
            Console.WriteLine("    --vertical-offset=<int> Vertical pixel offset (-8 to 8, default: 0)");
            Console.WriteLine("    --item-font            Generate item font style (default: text/serif style)");
            Console.WriteLine("  --import-portrait        Import PNG into ROM portrait slot (requires --rom, --portrait-id, --in)");
            Console.WriteLine("    --portrait-id=<id>     Portrait table index number");
            Console.WriteLine("    --in=<path>            Input PNG file path");
            Console.WriteLine("  --import-portrait-all    Batch import PNGs from directory (requires --rom, --dir)");
            Console.WriteLine("    --dir=<path>           Directory with PNGs named {id}_name.png or {id}.png");
            Console.WriteLine("  --export-midi            Export song to MIDI file (requires --rom, --song-id, --out)");
            Console.WriteLine("    --song-id=<hex>        Song ID in hex (e.g., 0x1A)");
            Console.WriteLine("    --out=<path>           Output MIDI file path");
            Console.WriteLine("  --test                   Run self-test diagnostics (requires --rom)");
            Console.WriteLine("  --disasm-event           Disassemble event script (requires --rom, --addr)");
            Console.WriteLine("    --type=<kind>          Script type: event (default), procs, ai");
            Console.WriteLine("    --addr=<hex>           Start address in hex (e.g., 0x9A0000)");
            Console.WriteLine("    --out=<path>           Output file (optional, prints to stdout if omitted)");
            Console.WriteLine("  --lint-oam               Validate battle animation OAM data (requires --rom, --addr)");
            Console.WriteLine("    --addr=<hex>           OAM data address in ROM");
            Console.WriteLine("    --length=<int>         Number of bytes to scan (0=auto, default)");
            Console.WriteLine("  --apply-patch            Apply a BIN patch to ROM (requires --rom, --patch-file)");
            Console.WriteLine("  --merge3                 Three-way merge of ROM files (requires --base, --mine, --theirs, --out)");
            Console.WriteLine("    --base=<path>          Common ancestor ROM");
            Console.WriteLine("    --mine=<path>          Your modified ROM");
            Console.WriteLine("    --theirs=<path>        Their modified ROM");
            Console.WriteLine("  --expand-table           Expand a ROM data table by one entry (requires --rom, --pointer, --entry-size)");
            Console.WriteLine("    --pointer=<hex>        ROM address of the table pointer (e.g., 0x005524 for portraits)");
            Console.WriteLine("    --entry-size=<int>     Size of each table entry in bytes (e.g., 28 for FE8U portraits)");
            Console.WriteLine("    --count=<int>          Current entry count (REQUIRED for safety)");
            Console.WriteLine("  --uninstall-patch        Restore original bytes for fixed-address BIN patches (requires --rom, --patch-file, --original-rom)");
            Console.WriteLine("    --original-rom=<path>  Path to the clean/unmodified ROM for byte restoration");
            Console.WriteLine("                           Note: only reverses fixed BIN:0xADDR=file entries; FREEAREA/JUMP/EA patches need full GUI uninstall");
            Console.WriteLine("  --list-resources         List available resources from FE-Repo/FE-Repo-Music submodules");
            Console.WriteLine("    --category=<name>      Filter by category (e.g., 'Battle Animations', 'Portraits')");
            Console.WriteLine("    --patch-file=<path>    Path to PATCH_*.txt file");
            Console.WriteLine("  --import-midi            Import MIDI file into ROM song slot (requires --rom, --song-id, --in)");
            Console.WriteLine("    --song-id=<hex>        Song ID in hex (e.g., 0x1A)");
            Console.WriteLine("    --in=<path>            Input MIDI file path");
            Console.WriteLine("  --compile-event          Compile event script with EA/ColorzCore (requires --rom, --in)");
            Console.WriteLine("    --in=<path>            Input .event source file");
            Console.WriteLine("    --out=<path>           Output ROM path (default: overwrites input ROM)");
            Console.WriteLine("  --import-battle-anime    Import battle animation from .txt or .bin (requires --rom, --animation-id, --in)");
            Console.WriteLine("    --animation-id=<id>    0-based animation index in ROM table");
            Console.WriteLine("    --in=<path>            Input .txt script or FEditor .bin file");
            Console.WriteLine("  --export-battle-anime    Export battle animation to .txt + PNGs or GIF (requires --rom, --animation-id, --out)");
            Console.WriteLine("    --animation-id=<id>    0-based animation index in ROM table");
            Console.WriteLine("    --out=<path>           Output .txt file path (PNGs saved alongside), or .gif with --gif");
            Console.WriteLine("    --gif                  Export as animated GIF instead of .txt + PNGs");
            Console.WriteLine("    --section=<N>          Section index 0-11 for GIF export (default: 0 = attack body)");
            Console.WriteLine("  --freespace              Scan and report free space in ROM (requires --rom)");
            Console.WriteLine("    --min-size=<int>       Minimum free block size to report (default: 16)");
            Console.WriteLine("  --hex-dump               Dump ROM bytes in hex+ASCII format (requires --rom, --addr)");
            Console.WriteLine("    --addr=<hex>           Start address in hex (e.g., 0x1000)");
            Console.WriteLine("    --length=<int>         Number of bytes to dump (default: 256)");
            Console.WriteLine("  --search-text            Search for text pattern across all ROM text entries (requires --rom, --query)");
            Console.WriteLine("    --query=<text>         Text pattern to search for (case-insensitive)");
            Console.WriteLine("  --list-patches           List available patches and their install status (requires --rom)");
            Console.WriteLine("    --patch-name=<name>    Filter patches by name (substring match)");
            Console.WriteLine("  --export-map-settings    Export all chapter/map settings to TSV (requires --rom, --out)");
            Console.WriteLine("  --diff                   Compare two ROMs byte-by-byte (requires --rom, --rom2)");
            Console.WriteLine("    --rom2=<path>          Second ROM to compare against");
            Console.WriteLine("    --out=<path>           Output TSV file (omit for summary to stdout)");
            Console.WriteLine("  --testonly               Run self-test diagnostics then exit");
            Console.WriteLine();
            Console.WriteLine("Examples:");
            Console.WriteLine("  FEBuilderGBA.CLI --version");
            Console.WriteLine("  FEBuilderGBA.CLI --makeups=patch.ups --rom=modified.gba --fromrom=original.gba");
            Console.WriteLine("  FEBuilderGBA.CLI --applyups=output.gba --rom=original.gba --patch=patch.ups");
            Console.WriteLine("  FEBuilderGBA.CLI --lint --rom=rom.gba");
            Console.WriteLine("  FEBuilderGBA.CLI --lint --rom=rom.gba --force-version=FE8U");
            Console.WriteLine("  FEBuilderGBA.CLI --disasm=output.asm --rom=rom.gba");
            Console.WriteLine("  FEBuilderGBA.CLI --decreasecolor --in=input.png --out=output.png --paletteno=16");
            Console.WriteLine("  FEBuilderGBA.CLI --decreasecolor --in=input.png --out=output.png --paletteno=16 --noScale --noReserve1stColor");
            Console.WriteLine("  FEBuilderGBA.CLI --pointercalc --rom=source.gba --target=target.gba --address=0x100,0x200");
            Console.WriteLine("  FEBuilderGBA.CLI --rebuild --rom=modified.gba --fromrom=original.gba");
            Console.WriteLine("  FEBuilderGBA.CLI --songexchange --rom=dest.gba --fromrom=source.gba --fromsong=0x1A --tosong=0x1A");
            Console.WriteLine("  FEBuilderGBA.CLI --convertmap1picture --in=map.png --outImg=tiles.bin --outTSA=tsa.bin");
            Console.WriteLine("  FEBuilderGBA.CLI --translate --rom=rom.gba --out=texts.tsv");
            Console.WriteLine("  FEBuilderGBA.CLI --translate --rom=rom.gba --in=texts.tsv");
            Console.WriteLine("  FEBuilderGBA.CLI --translate-roundtrip --rom=rom.gba");
            Console.WriteLine("  FEBuilderGBA.CLI --translate-roundtrip --rom=rom.gba --out=diff");
            Console.WriteLine("  FEBuilderGBA.CLI --export-data --rom=rom.gba --table=units --out=units.tsv");
            Console.WriteLine("  FEBuilderGBA.CLI --export-data --rom=rom.gba --table=all --out=data");
            Console.WriteLine("  FEBuilderGBA.CLI --import-data --rom=rom.gba --table=units --in=units.tsv");
            Console.WriteLine("  FEBuilderGBA.CLI --data-roundtrip --rom=rom.gba --table=all");
            Console.WriteLine("  FEBuilderGBA.CLI --import-midi --rom=rom.gba --song-id=0x1A --in=song.mid");
            Console.WriteLine("  FEBuilderGBA.CLI --compile-event --rom=rom.gba --in=script.event --out=modified.gba");
            Console.WriteLine("  FEBuilderGBA.CLI --list-patches --rom=rom.gba --patch-name=SkillSystem");
            Console.WriteLine("  FEBuilderGBA.CLI --import-battle-anime --rom=rom.gba --animation-id=1 --in=anim.txt");
            Console.WriteLine("  FEBuilderGBA.CLI --freespace --rom=rom.gba --min-size=256");
            Console.WriteLine("  FEBuilderGBA.CLI --hex-dump --rom=rom.gba --addr=0x1000 --length=512");
            Console.WriteLine("  FEBuilderGBA.CLI --search-text --rom=rom.gba --query=Eirika");
            Console.WriteLine("  FEBuilderGBA.CLI --lastrom");
            Console.WriteLine("  FEBuilderGBA.CLI --translate_batch --rom=rom.gba --out=texts.tsv --in=translated.tsv");
            Console.WriteLine("  FEBuilderGBA.CLI --test --rom=rom.gba");
            Console.WriteLine("  FEBuilderGBA.CLI --testonly --rom=rom.gba");
        }

        // Known original ROM CRC32 values (from ROMFE*.cs)
        private static readonly HashSet<uint> KnownOriginalCRC32s = new()
        {
            0xd38763e1, // FE6
            0xf0c10e72, // FE7J
            0x2a524221, // FE7U
            0x9d76826f, // FE8J
            0xa47246ae, // FE8U
        };

        /// <summary>
        /// Search for an original (unmodified) ROM in the same directory as the modified ROM.
        /// Matches by CRC32 against known original ROM CRC32 values.
        /// </summary>
        static string FindOriginalRom(string modifiedRomPath)
        {
            string dir = Path.GetDirectoryName(Path.GetFullPath(modifiedRomPath));
            if (dir == null) return null;

            var crc = new UPSUtilCore.CRC32();
            string modifiedFull = Path.GetFullPath(modifiedRomPath);

            foreach (string file in Directory.GetFiles(dir, "*.gba"))
            {
                if (string.Equals(Path.GetFullPath(file), modifiedFull, StringComparison.OrdinalIgnoreCase))
                    continue;
                try
                {
                    byte[] data = File.ReadAllBytes(file);
                    uint fileCrc = crc.Calc(data);
                    if (KnownOriginalCRC32s.Contains(fileCrc))
                        return file;
                }
                catch { }
            }
            return null;
        }

        static int RunMakeUps(Dictionary<string, string> argsDic)
        {
            string upsPath = argsDic["--makeups"];
            if (string.IsNullOrEmpty(upsPath))
            {
                Console.Error.WriteLine("Error: --makeups requires a path, e.g. --makeups=output.ups");
                return 1;
            }

            if (!argsDic.ContainsKey("--rom") || string.IsNullOrEmpty(argsDic["--rom"]))
            {
                Console.Error.WriteLine("Error: --makeups requires --rom=<modified_rom>");
                return 1;
            }

            string modifiedRomPath = argsDic["--rom"];
            if (!File.Exists(modifiedRomPath))
            {
                Console.Error.WriteLine($"Error: Modified ROM not found: {modifiedRomPath}");
                return 1;
            }

            string originalRomPath;
            if (argsDic.ContainsKey("--fromrom") && !string.IsNullOrEmpty(argsDic["--fromrom"]))
            {
                originalRomPath = argsDic["--fromrom"];
            }
            else
            {
                // Auto-detect original ROM (same as WinForms behavior)
                originalRomPath = FindOriginalRom(modifiedRomPath);
                if (originalRomPath != null)
                {
                    Console.WriteLine($"Auto-detected original ROM: {originalRomPath}");
                }
                else
                {
                    Console.Error.WriteLine("Error: --fromrom not specified and no original ROM found in the same directory.");
                    Console.Error.WriteLine("  Use --fromrom=<original_rom> to specify the unmodified ROM.");
                    return 1;
                }
            }

            if (!File.Exists(originalRomPath))
            {
                Console.Error.WriteLine($"Error: Original ROM not found: {originalRomPath}");
                return 1;
            }

            byte[] src = File.ReadAllBytes(originalRomPath);
            byte[] dst = File.ReadAllBytes(modifiedRomPath);

            UPSUtilCore.MakeUPS(src, dst, upsPath);
            Console.WriteLine($"UPS patch created: {upsPath}");
            Console.WriteLine($"  Source: {originalRomPath} ({src.Length} bytes)");
            Console.WriteLine($"  Target: {modifiedRomPath} ({dst.Length} bytes)");
            return 0;
        }

        static int RunApplyUps(Dictionary<string, string> argsDic)
        {
            string outputPath = argsDic["--applyups"];
            if (string.IsNullOrEmpty(outputPath))
            {
                Console.Error.WriteLine("Error: --applyups requires an output path, e.g. --applyups=output.gba");
                return 1;
            }

            if (!argsDic.ContainsKey("--rom") || string.IsNullOrEmpty(argsDic["--rom"]))
            {
                Console.Error.WriteLine("Error: --applyups requires --rom=<original_rom>");
                return 1;
            }

            if (!argsDic.ContainsKey("--patch") || string.IsNullOrEmpty(argsDic["--patch"]))
            {
                Console.Error.WriteLine("Error: --applyups requires --patch=<patch.ups>");
                return 1;
            }

            string romPath = argsDic["--rom"];
            string patchPath = argsDic["--patch"];

            if (!File.Exists(romPath))
            {
                Console.Error.WriteLine($"Error: ROM file not found: {romPath}");
                return 1;
            }
            if (!File.Exists(patchPath))
            {
                Console.Error.WriteLine($"Error: Patch file not found: {patchPath}");
                return 1;
            }

            byte[] sourceData = File.ReadAllBytes(romPath);
            byte[] patchData = File.ReadAllBytes(patchPath);

            byte[] result = UPSUtilCore.ApplyUPS(sourceData, patchData, out string errorMessage);
            if (result == null)
            {
                Console.Error.WriteLine($"Error applying UPS patch: {errorMessage}");
                return 1;
            }

            if (!string.IsNullOrEmpty(errorMessage))
            {
                Console.Error.WriteLine($"Warning: {errorMessage}");
            }

            File.WriteAllBytes(outputPath, result);
            Console.WriteLine($"UPS patch applied successfully: {outputPath}");
            Console.WriteLine($"  Source: {romPath} ({sourceData.Length} bytes)");
            Console.WriteLine($"  Patch: {patchPath} ({patchData.Length} bytes)");
            Console.WriteLine($"  Output: {outputPath} ({result.Length} bytes)");
            return 0;
        }

        static int RunLint(Dictionary<string, string> argsDic)
        {
            if (!argsDic.ContainsKey("--rom") || string.IsNullOrEmpty(argsDic["--rom"]))
            {
                Console.Error.WriteLine("Error: --lint requires --rom=<rom_file>");
                return 1;
            }

            RomLoader.InitEnvironment();

            string romPath = argsDic["--rom"];
            string forceVersion = argsDic.ContainsKey("--force-version") ? argsDic["--force-version"] : null;
            if (!RomLoader.LoadRom(romPath, forceVersion))
                return 1;

            RomLoader.InitFull();

            var scanner = new FELintScanner();
            var errors = scanner.Scan();

            if (errors.Count == 0)
            {
                Console.WriteLine("Lint: No errors found.");
                return 0;
            }

            Console.WriteLine($"Lint: {errors.Count} issue(s) found:");
            foreach (var err in errors)
            {
                string severity = err.Severity == FELintCore.ErrorType.ERROR ? "ERROR" : "WARNING";
                Console.WriteLine($"  [{severity}] 0x{err.Addr:X08}: {err.ErrorMessage}");
            }
            return errors.Exists(e => e.Severity == FELintCore.ErrorType.ERROR) ? 1 : 0;
        }

        static int RunDisasm(Dictionary<string, string> argsDic)
        {
            string outputPath = argsDic["--disasm"];
            if (string.IsNullOrEmpty(outputPath))
            {
                Console.Error.WriteLine("Error: --disasm requires an output path, e.g. --disasm=output.asm");
                return 1;
            }

            if (!argsDic.ContainsKey("--rom") || string.IsNullOrEmpty(argsDic["--rom"]))
            {
                Console.Error.WriteLine("Error: --disasm requires --rom=<rom_file>");
                return 1;
            }

            RomLoader.InitEnvironment();

            string romPath = argsDic["--rom"];
            string forceVersion = argsDic.ContainsKey("--force-version") ? argsDic["--force-version"] : null;
            if (!RomLoader.LoadRom(romPath, forceVersion))
                return 1;

            RomLoader.InitFull();

            var disassembler = new DisassemblerCore();
            int count = disassembler.DisassembleToFile(outputPath);
            Console.WriteLine($"Disassembly written to: {outputPath} ({count} instructions)");
            return 0;
        }

        static int RunDecreaseColor(Dictionary<string, string> argsDic)
        {
            if (!argsDic.ContainsKey("--in") || string.IsNullOrEmpty(argsDic["--in"]))
            {
                Console.Error.WriteLine("Error: --decreasecolor requires --in=<input_image>");
                return 1;
            }
            if (!argsDic.ContainsKey("--out") || string.IsNullOrEmpty(argsDic["--out"]))
            {
                Console.Error.WriteLine("Error: --decreasecolor requires --out=<output_image>");
                return 1;
            }

            string inputPath = argsDic["--in"];
            string outputPath = argsDic["--out"];
            int maxColors = 16;
            if (argsDic.ContainsKey("--paletteno") && !string.IsNullOrEmpty(argsDic["--paletteno"]))
                int.TryParse(argsDic["--paletteno"], out maxColors);

            // Parse optional flags
            bool noScale = argsDic.ContainsKey("--noScale");
            bool noReserve1stColor = argsDic.ContainsKey("--noReserve1stColor");
            bool ignoreTSA = argsDic.ContainsKey("--ignoreTSA");

            if (noScale)
                Console.WriteLine("  Flag: --noScale (color scaling disabled)");
            if (noReserve1stColor)
                Console.WriteLine("  Flag: --noReserve1stColor (palette slot 0 not reserved for transparency)");
            if (ignoreTSA)
                Console.WriteLine("  Flag: --ignoreTSA (TSA tile constraints ignored)");

            if (!File.Exists(inputPath))
            {
                Console.Error.WriteLine($"Error: Input file not found: {inputPath}");
                return 1;
            }

            // Load image via IImageService
            var imgService = CoreState.ImageService;
            if (imgService == null)
            {
                Console.Error.WriteLine("Error: Image service not available.");
                return 1;
            }

            var image = imgService.LoadImage(inputPath);
            if (image == null)
            {
                Console.Error.WriteLine($"Error: Failed to load image: {inputPath}");
                return 1;
            }

            byte[] rgba = image.GetPixelData();
            int width = image.Width;
            int height = image.Height;

            var result = DecreaseColorCore.Quantize(rgba, width, height, maxColors, noScale, noReserve1stColor, ignoreTSA);
            if (result == null)
            {
                Console.Error.WriteLine("Error: Quantization failed.");
                return 1;
            }

            // Convert indexed data back to RGBA for saving
            byte[] outRgba = new byte[width * height * 4];
            for (int i = 0; i < width * height; i++)
            {
                int palIdx = result.IndexData[i];
                if (palIdx < result.ColorCount)
                {
                    outRgba[i * 4 + 0] = result.RGBAPalette[palIdx * 4 + 0];
                    outRgba[i * 4 + 1] = result.RGBAPalette[palIdx * 4 + 1];
                    outRgba[i * 4 + 2] = result.RGBAPalette[palIdx * 4 + 2];
                    outRgba[i * 4 + 3] = result.RGBAPalette[palIdx * 4 + 3];
                }
            }

            var outImage = imgService.CreateImage(width, height);
            outImage.SetPixelData(outRgba);
            outImage.Save(outputPath);

            Console.WriteLine($"Color reduction complete: {outputPath}");
            Console.WriteLine($"  Input: {inputPath} ({width}x{height})");
            Console.WriteLine($"  Colors: {result.ColorCount} (max {maxColors})");
            return 0;
        }

        static int RunPointerCalc(Dictionary<string, string> argsDic)
        {
            if (!argsDic.ContainsKey("--rom") || string.IsNullOrEmpty(argsDic["--rom"]))
            {
                Console.Error.WriteLine("Error: --pointercalc requires --rom=<source_rom>");
                return 1;
            }
            if (!argsDic.ContainsKey("--target") || string.IsNullOrEmpty(argsDic["--target"]))
            {
                Console.Error.WriteLine("Error: --pointercalc requires --target=<target_rom>");
                return 1;
            }
            if (!argsDic.ContainsKey("--address") || string.IsNullOrEmpty(argsDic["--address"]))
            {
                Console.Error.WriteLine("Error: --pointercalc requires --address=<hex_list_or_file>");
                return 1;
            }

            string sourcePath = argsDic["--rom"];
            string targetPath = argsDic["--target"];
            string addressInput = argsDic["--address"];

            if (!File.Exists(sourcePath))
            {
                Console.Error.WriteLine($"Error: Source ROM not found: {sourcePath}");
                return 1;
            }
            if (!File.Exists(targetPath))
            {
                Console.Error.WriteLine($"Error: Target ROM not found: {targetPath}");
                return 1;
            }

            byte[] sourceData = File.ReadAllBytes(sourcePath);
            byte[] targetData = File.ReadAllBytes(targetPath);
            var addresses = PointerCalcCore.ParseAddressList(addressInput);

            if (addresses.Count == 0)
            {
                Console.Error.WriteLine("Error: No valid addresses provided.");
                return 1;
            }

            int searchLength = 16;
            if (argsDic.ContainsKey("--tracelevel") && !string.IsNullOrEmpty(argsDic["--tracelevel"]))
            {
                if (int.TryParse(argsDic["--tracelevel"], out int level))
                    searchLength = Math.Max(4, level * 4);
            }

            Console.WriteLine($"Searching {addresses.Count} address(es) in target ROM...");
            var results = PointerCalcCore.SearchAddresses(sourceData, targetData, addresses, searchLength);

            if (results.Count == 0)
            {
                Console.WriteLine("No matches found.");
                return 0;
            }

            Console.WriteLine($"Found {results.Count} match(es):");
            foreach (var r in results)
            {
                Console.WriteLine($"  Source: 0x{r.SourceAddress:X08} -> Target: 0x{r.TargetAddress:X08} [{r.MatchType}]");
            }
            return 0;
        }

        static int RunRebuild(Dictionary<string, string> argsDic)
        {
            if (!argsDic.ContainsKey("--rom") || string.IsNullOrEmpty(argsDic["--rom"]))
            {
                Console.Error.WriteLine("Error: --rebuild requires --rom=<modified_rom>");
                return 1;
            }
            string modifiedPath = argsDic["--rom"];
            string vanillaPath;
            if (argsDic.ContainsKey("--fromrom") && !string.IsNullOrEmpty(argsDic["--fromrom"]))
            {
                vanillaPath = argsDic["--fromrom"];
            }
            else
            {
                vanillaPath = FindOriginalRom(modifiedPath);
                if (vanillaPath != null)
                {
                    Console.WriteLine($"Auto-detected original ROM: {vanillaPath}");
                }
                else
                {
                    Console.Error.WriteLine("Error: --fromrom not specified and no original ROM found in the same directory.");
                    Console.Error.WriteLine("  Use --fromrom=<vanilla_rom> to specify the unmodified ROM.");
                    return 1;
                }
            }

            if (!File.Exists(modifiedPath))
            {
                Console.Error.WriteLine($"Error: Modified ROM not found: {modifiedPath}");
                return 1;
            }
            if (!File.Exists(vanillaPath))
            {
                Console.Error.WriteLine($"Error: Vanilla ROM not found: {vanillaPath}");
                return 1;
            }

            byte[] vanillaData = File.ReadAllBytes(vanillaPath);
            byte[] modifiedData = File.ReadAllBytes(modifiedPath);

            var progress = new Progress<string>(msg => Console.WriteLine($"  {msg}"));
            var result = RebuildCore.Rebuild(vanillaData, modifiedData, progress);

            if (!result.Success)
            {
                Console.Error.WriteLine($"Error: {result.Message}");
                return 1;
            }

            Console.WriteLine($"Rebuild analysis complete:");
            Console.WriteLine($"  {result.Message}");
            return 0;
        }

        static int RunSongExchange(Dictionary<string, string> argsDic)
        {
            if (!argsDic.ContainsKey("--rom") || string.IsNullOrEmpty(argsDic["--rom"]))
            {
                Console.Error.WriteLine("Error: --songexchange requires --rom=<dest_rom>");
                return 1;
            }
            if (!argsDic.ContainsKey("--fromrom") || string.IsNullOrEmpty(argsDic["--fromrom"]))
            {
                Console.Error.WriteLine("Error: --songexchange requires --fromrom=<source_rom>");
                return 1;
            }
            if (!argsDic.ContainsKey("--fromsong") || string.IsNullOrEmpty(argsDic["--fromsong"]))
            {
                Console.Error.WriteLine("Error: --songexchange requires --fromsong=<hex_id>");
                return 1;
            }
            if (!argsDic.ContainsKey("--tosong") || string.IsNullOrEmpty(argsDic["--tosong"]))
            {
                Console.Error.WriteLine("Error: --songexchange requires --tosong=<hex_id>");
                return 1;
            }

            string destPath = argsDic["--rom"];
            string sourcePath = argsDic["--fromrom"];

            if (!File.Exists(destPath))
            {
                Console.Error.WriteLine($"Error: Destination ROM not found: {destPath}");
                return 1;
            }
            if (!File.Exists(sourcePath))
            {
                Console.Error.WriteLine($"Error: Source ROM not found: {sourcePath}");
                return 1;
            }

            string fromSongStr = argsDic["--fromsong"].Replace("0x", "").Replace("0X", "");
            string toSongStr = argsDic["--tosong"].Replace("0x", "").Replace("0X", "");

            if (!uint.TryParse(fromSongStr, System.Globalization.NumberStyles.HexNumber, null, out uint fromSongId))
            {
                Console.Error.WriteLine("Error: Invalid --fromsong hex value.");
                return 1;
            }
            if (!uint.TryParse(toSongStr, System.Globalization.NumberStyles.HexNumber, null, out uint toSongId))
            {
                Console.Error.WriteLine("Error: Invalid --tosong hex value.");
                return 1;
            }

            RomLoader.InitEnvironment();
            string forceVersion = argsDic.ContainsKey("--force-version") ? argsDic["--force-version"] : null;
            if (!RomLoader.LoadRom(destPath, forceVersion))
                return 1;

            byte[] sourceData = File.ReadAllBytes(sourcePath);
            byte[] destData = File.ReadAllBytes(destPath);

            uint soundTablePtr = CoreState.ROM.RomInfo.sound_table_pointer;
            uint srcTableAddr = SongExchangeCore.FindSongTablePointer(sourceData, soundTablePtr);
            uint destTableAddr = SongExchangeCore.FindSongTablePointer(destData, soundTablePtr);

            if (srcTableAddr == 0 || destTableAddr == 0)
            {
                Console.Error.WriteLine("Error: Could not find song table in one or both ROMs.");
                return 1;
            }

            var srcSongs = SongExchangeCore.SongTableToSongList(sourceData, srcTableAddr);
            var destSongs = SongExchangeCore.SongTableToSongList(destData, destTableAddr);

            if (fromSongId >= srcSongs.Count)
            {
                Console.Error.WriteLine($"Error: Source song ID 0x{fromSongId:X} out of range (max 0x{srcSongs.Count - 1:X}).");
                return 1;
            }
            if (toSongId >= destSongs.Count)
            {
                Console.Error.WriteLine($"Error: Destination song ID 0x{toSongId:X} out of range (max 0x{destSongs.Count - 1:X}).");
                return 1;
            }

            bool ok = SongExchangeCore.ConvertSong(sourceData, srcSongs[(int)fromSongId],
                                                     destData, destSongs[(int)toSongId]);
            if (!ok)
            {
                Console.Error.WriteLine("Error: Song conversion failed.");
                return 1;
            }

            File.WriteAllBytes(destPath, destData);
            Console.WriteLine($"Song exchange complete: song 0x{fromSongId:X} from {sourcePath} -> song 0x{toSongId:X} in {destPath}");
            return 0;
        }

        static int RunConvertMap1Picture(Dictionary<string, string> argsDic)
        {
            if (!argsDic.ContainsKey("--in") || string.IsNullOrEmpty(argsDic["--in"]))
            {
                Console.Error.WriteLine("Error: --convertmap1picture requires --in=<input_image>");
                return 1;
            }

            string inputPath = argsDic["--in"];
            if (!File.Exists(inputPath))
            {
                Console.Error.WriteLine($"Error: Input file not found: {inputPath}");
                return 1;
            }

            string outImgPath = argsDic.ContainsKey("--outImg") ? argsDic["--outImg"] : "";
            string outTSAPath = argsDic.ContainsKey("--outTSA") ? argsDic["--outTSA"] : "";
            if (string.IsNullOrEmpty(outImgPath) && string.IsNullOrEmpty(outTSAPath))
            {
                Console.Error.WriteLine("Error: --convertmap1picture requires --outImg=<path> and/or --outTSA=<path>");
                return 1;
            }

            var imgService = CoreState.ImageService;
            if (imgService == null)
            {
                Console.Error.WriteLine("Error: Image service not available.");
                return 1;
            }

            var image = imgService.LoadImage(inputPath);
            if (image == null)
            {
                Console.Error.WriteLine($"Error: Failed to load image: {inputPath}");
                return 1;
            }

            byte[] rgba = image.GetPixelData();
            int width = image.Width;
            int height = image.Height;

            var result = MapConvertCore.ConvertImage(rgba, width, height);
            if (result == null)
            {
                Console.Error.WriteLine("Error: Map conversion failed. Image dimensions must be multiples of 8.");
                return 1;
            }

            if (!string.IsNullOrEmpty(outImgPath))
                File.WriteAllBytes(outImgPath, result.TileData);
            if (!string.IsNullOrEmpty(outTSAPath))
            {
                byte[] compressedTSA = LZ77.compress(result.TSAData);
                File.WriteAllBytes(outTSAPath, compressedTSA);
            }

            Console.WriteLine($"Map conversion complete:");
            Console.WriteLine($"  Input: {inputPath} ({width}x{height})");
            Console.WriteLine($"  Tiles: {result.TileCount} unique ({result.WidthTiles}x{result.HeightTiles} grid)");
            if (!string.IsNullOrEmpty(outImgPath))
                Console.WriteLine($"  Tile data: {outImgPath} ({result.TileData.Length} bytes)");
            if (!string.IsNullOrEmpty(outTSAPath))
                Console.WriteLine($"  TSA data: {outTSAPath}");
            return 0;
        }

        static int RunTranslate(Dictionary<string, string> argsDic)
        {
            if (!argsDic.ContainsKey("--rom") || string.IsNullOrEmpty(argsDic["--rom"]))
            {
                Console.Error.WriteLine("Error: --translate requires --rom=<rom_file>");
                return 1;
            }

            string romPath = argsDic["--rom"];
            string forceVersion = argsDic.ContainsKey("--force-version") ? argsDic["--force-version"] : null;

            RomLoader.InitEnvironment();

            if (!RomLoader.LoadRom(romPath, forceVersion))
                return 1;

            RomLoader.InitFull();

            bool hasOut = argsDic.ContainsKey("--out") && !string.IsNullOrEmpty(argsDic["--out"]);
            bool hasIn = argsDic.ContainsKey("--in") && !string.IsNullOrEmpty(argsDic["--in"]);

            if (!hasOut && !hasIn)
            {
                // Default: dump text count info
                uint textCount = TranslateCore.GetTextCount(CoreState.ROM);
                Console.WriteLine($"ROM: {romPath}");
                Console.WriteLine($"Version: {CoreState.ROM.RomInfo.VersionToFilename}");
                Console.WriteLine($"Text entries: {textCount}");
                Console.WriteLine();
                Console.WriteLine("Use --out=<path.tsv> to export text, or --in=<path.tsv> to import.");
                return 0;
            }

            if (hasOut)
            {
                string outputPath = argsDic["--out"];
                Console.WriteLine($"Dumping text from ROM: {romPath}");

                var entries = TranslateCore.DumpTexts(CoreState.ROM);
                TranslateCore.ExportToTSV(entries, outputPath);

                Console.WriteLine($"Exported {entries.Count} text entries to: {outputPath}");
                return 0;
            }

            if (hasIn)
            {
                string inputPath = argsDic["--in"];
                if (!File.Exists(inputPath))
                {
                    Console.Error.WriteLine($"Error: Input file not found: {inputPath}");
                    return 1;
                }

                Console.WriteLine($"Importing text from: {inputPath}");
                var entries = TranslateCore.ImportFromTSV(inputPath);
                Console.WriteLine($"Parsed {entries.Count} text entries from TSV.");

                int written = TranslateCore.WriteTexts(CoreState.ROM, entries);
                if (written == 0)
                {
                    Console.Error.WriteLine("Warning: No text entries were written to the ROM.");
                    Console.Error.WriteLine("The TSV was parsed but all entries were out of range or failed to encode.");
                    return 1;
                }

                Console.WriteLine($"Wrote {written} text entries to ROM.");
                CoreState.ROM.Save(romPath, true);
                Console.WriteLine($"ROM saved: {romPath}");
                return 0;
            }

            return 0;
        }

        static int RunTranslateRoundTrip(Dictionary<string, string> argsDic)
        {
            if (!argsDic.ContainsKey("--rom") || string.IsNullOrEmpty(argsDic["--rom"]))
            {
                Console.Error.WriteLine("Error: --translate-roundtrip requires --rom=<rom_file>");
                return 1;
            }

            string romPath = argsDic["--rom"];
            if (!File.Exists(romPath))
            {
                Console.Error.WriteLine($"Error: ROM file not found: {romPath}");
                return 1;
            }

            string forceVersion = argsDic.ContainsKey("--force-version") ? argsDic["--force-version"] : null;

            // Work on a temporary copy so the original ROM is not modified
            string tempRom = Path.Combine(Path.GetTempPath(), $"roundtrip_{Guid.NewGuid():N}.gba");
            try
            {
                File.Copy(romPath, tempRom, true);

                RomLoader.InitEnvironment();
                if (!RomLoader.LoadRom(tempRom, forceVersion))
                    return 1;
                RomLoader.InitFull();

                Console.WriteLine($"ROM: {romPath}");
                Console.WriteLine($"Version: {CoreState.ROM.RomInfo.VersionToFilename}");
                Console.WriteLine("Running text round-trip validation...");

                // Phase 1: export original texts
                var export1 = TranslateCore.DumpTexts(CoreState.ROM);
                Console.WriteLine($"Export 1: {export1.Count} text entries dumped.");

                // Phase 2: write same texts back to ROM
                int written = TranslateCore.WriteTexts(CoreState.ROM, export1);
                Console.WriteLine($"Write-back: {written} entries written to ROM.");

                // Phase 3: re-export after write-back
                var export2 = TranslateCore.DumpTexts(CoreState.ROM);
                Console.WriteLine($"Export 2: {export2.Count} text entries dumped.");

                // Optionally save TSVs
                bool hasOut = argsDic.ContainsKey("--out") && !string.IsNullOrEmpty(argsDic["--out"]);
                if (hasOut)
                {
                    string basePath = argsDic["--out"];
                    string tsv1 = basePath + ".export1.tsv";
                    string tsv2 = basePath + ".export2.tsv";
                    TranslateCore.ExportToTSV(export1, tsv1);
                    TranslateCore.ExportToTSV(export2, tsv2);
                    Console.WriteLine($"Saved: {tsv1}");
                    Console.WriteLine($"Saved: {tsv2}");
                }

                // Phase 4: compare
                int matches = 0;
                int mismatches = 0;
                var export2Dict = new Dictionary<uint, string>();
                foreach (var (id, text) in export2)
                    export2Dict[id] = text;

                foreach (var (id, text1) in export1)
                {
                    if (export2Dict.TryGetValue(id, out string text2) && text1 == text2)
                    {
                        matches++;
                    }
                    else
                    {
                        mismatches++;
                        string t2 = export2Dict.TryGetValue(id, out string v) ? v : "(missing)";
                        Console.WriteLine($"  MISMATCH id={id}:");
                        Console.WriteLine($"    before: {Truncate(text1, 120)}");
                        Console.WriteLine($"    after:  {Truncate(t2, 120)}");
                    }
                }

                Console.WriteLine();
                Console.WriteLine($"Result: {matches} match, {mismatches} mismatch out of {export1.Count} entries.");

                if (mismatches == 0)
                {
                    Console.WriteLine("PASS: Text round-trip is lossless.");
                    return 0;
                }
                else
                {
                    Console.WriteLine("FAIL: Text round-trip has mismatches.");
                    return 2;
                }
            }
            finally
            {
                try { if (File.Exists(tempRom)) File.Delete(tempRom); } catch { }
            }
        }

        private static string Truncate(string s, int maxLen)
        {
            if (s == null) return "(null)";
            string escaped = s.Replace("\n", "\\n").Replace("\r", "\\r").Replace("\t", "\\t");
            if (escaped.Length <= maxLen) return escaped;
            return escaped.Substring(0, maxLen) + "...";
        }

        static int RunLastRom(Dictionary<string, string> argsDic)
        {
            RomLoader.InitEnvironment();

            if (CoreState.Config == null)
            {
                Console.Error.WriteLine("Error: Config not loaded. Cannot determine last ROM.");
                return 1;
            }

            string lastRom = CoreState.Config.at("Last_Rom_Filename", "");
            if (string.IsNullOrEmpty(lastRom))
            {
                Console.Error.WriteLine("Error: No last ROM filename found in config.");
                return 1;
            }

            if (!File.Exists(lastRom))
            {
                Console.Error.WriteLine($"Error: Last ROM file not found: {lastRom}");
                return 1;
            }

            Console.WriteLine($"Last ROM: {lastRom}");

            // Load and init the ROM
            string forceVersion = argsDic.ContainsKey("--force-version") ? argsDic["--force-version"] : null;
            if (!RomLoader.LoadRom(lastRom, forceVersion))
                return 1;

            RomLoader.InitFull();

            Console.WriteLine($"ROM loaded successfully: {lastRom}");
            Console.WriteLine($"Version: {CoreState.ROM.RomInfo.VersionToFilename}");
            return 0;
        }

        static int RunTranslateBatch(Dictionary<string, string> argsDic)
        {
            if (!argsDic.ContainsKey("--rom") || string.IsNullOrEmpty(argsDic["--rom"]))
            {
                Console.Error.WriteLine("Error: --translate_batch requires --rom=<rom_file>");
                return 1;
            }

            string romPath = argsDic["--rom"];
            string forceVersion = argsDic.ContainsKey("--force-version") ? argsDic["--force-version"] : null;

            RomLoader.InitEnvironment();
            if (!RomLoader.LoadRom(romPath, forceVersion))
                return 1;
            RomLoader.InitFull();

            // Export phase
            string outPath = argsDic.ContainsKey("--out") && !string.IsNullOrEmpty(argsDic["--out"])
                ? argsDic["--out"]
                : Path.ChangeExtension(romPath, ".tsv");

            Console.WriteLine($"Batch translate: Exporting text from {romPath}...");
            var entries = TranslateCore.DumpTexts(CoreState.ROM);
            TranslateCore.ExportToTSV(entries, outPath);
            Console.WriteLine($"Exported {entries.Count} text entries to: {outPath}");

            // Import phase (if --in provided)
            if (argsDic.ContainsKey("--in") && !string.IsNullOrEmpty(argsDic["--in"]))
            {
                string inPath = argsDic["--in"];
                if (!File.Exists(inPath))
                {
                    Console.Error.WriteLine($"Error: Input file not found: {inPath}");
                    return 1;
                }

                Console.WriteLine($"Batch translate: Importing text from {inPath}...");
                var importEntries = TranslateCore.ImportFromTSV(inPath);
                Console.WriteLine($"Parsed {importEntries.Count} text entries.");

                int written = TranslateCore.WriteTexts(CoreState.ROM, importEntries);
                Console.WriteLine($"Wrote {written} text entries to ROM.");

                CoreState.ROM.Save(romPath, true);
                Console.WriteLine($"ROM saved: {romPath}");
            }

            Console.WriteLine("Batch translation complete.");
            return 0;
        }

        static int RunSelfTest(Dictionary<string, string> argsDic)
        {
            bool testOnly = argsDic.ContainsKey("--testonly");
            Console.WriteLine($"Running self-test diagnostics{(testOnly ? " (testonly mode)" : "")}...");

            int passed = 0;
            int failed = 0;

            // Test 1: Config loading
            try
            {
                RomLoader.InitEnvironment();
                Console.WriteLine("  [PASS] Config/environment initialization");
                passed++;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"  [FAIL] Config/environment initialization: {ex.Message}");
                failed++;
            }

            // Test 2: ROM loading (if --rom provided)
            if (argsDic.ContainsKey("--rom") && !string.IsNullOrEmpty(argsDic["--rom"]))
            {
                try
                {
                    string romPath = argsDic["--rom"];
                    string forceVersion = argsDic.ContainsKey("--force-version") ? argsDic["--force-version"] : null;
                    if (RomLoader.LoadRom(romPath, forceVersion))
                    {
                        Console.WriteLine($"  [PASS] ROM load: {romPath}");
                        passed++;

                        // Test 3: Full init
                        try
                        {
                            RomLoader.InitFull();
                            Console.WriteLine("  [PASS] Full ROM initialization");
                            passed++;
                        }
                        catch (Exception ex)
                        {
                            Console.Error.WriteLine($"  [FAIL] Full ROM initialization: {ex.Message}");
                            failed++;
                        }

                        // Test 4: Text system
                        try
                        {
                            uint textCount = TranslateCore.GetTextCount(CoreState.ROM);
                            Console.WriteLine($"  [PASS] Text system ({textCount} entries)");
                            passed++;
                        }
                        catch (Exception ex)
                        {
                            Console.Error.WriteLine($"  [FAIL] Text system: {ex.Message}");
                            failed++;
                        }

                        // Test 5: Event scripts
                        try
                        {
                            bool hasScripts = CoreState.EventScript != null;
                            Console.WriteLine($"  [PASS] Event scripts loaded: {hasScripts}");
                            passed++;
                        }
                        catch (Exception ex)
                        {
                            Console.Error.WriteLine($"  [FAIL] Event scripts: {ex.Message}");
                            failed++;
                        }
                    }
                    else
                    {
                        Console.Error.WriteLine($"  [FAIL] ROM load: {romPath}");
                        failed++;
                    }
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"  [FAIL] ROM load: {ex.Message}");
                    failed++;
                }
            }
            else
            {
                Console.WriteLine("  [SKIP] ROM tests (no --rom provided)");
            }

            // Test 6: Image service
            try
            {
                bool hasImageService = CoreState.ImageService != null;
                Console.WriteLine($"  [PASS] Image service available: {hasImageService}");
                passed++;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"  [FAIL] Image service: {ex.Message}");
                failed++;
            }

            Console.WriteLine($"\nSelf-test results: {passed} passed, {failed} failed");

            if (testOnly)
            {
                Console.WriteLine("Test-only mode: exiting.");
            }

            return failed > 0 ? 1 : 0;
        }

        static int RunExportData(Dictionary<string, string> argsDic)
        {
            if (!argsDic.ContainsKey("--rom") || string.IsNullOrEmpty(argsDic["--rom"]))
            {
                Console.Error.WriteLine("Error: --export-data requires --rom=<rom_file>");
                return 1;
            }
            if (!argsDic.ContainsKey("--table") || string.IsNullOrEmpty(argsDic["--table"]))
            {
                Console.Error.WriteLine("Error: --export-data requires --table=<name> (units, classes, items, or all)");
                return 1;
            }

            string romPath = argsDic["--rom"];
            string tableName = argsDic["--table"];
            string forceVersion = argsDic.ContainsKey("--force-version") ? argsDic["--force-version"] : null;

            RomLoader.InitEnvironment();
            if (!RomLoader.LoadRom(romPath, forceVersion))
                return 1;
            RomLoader.InitFull();

            Console.WriteLine($"ROM: {romPath}");
            Console.WriteLine($"Version: {CoreState.ROM.RomInfo.VersionToFilename}");

            var tableNames = tableName.Equals("all", StringComparison.OrdinalIgnoreCase)
                ? new List<string>(StructExportCore.GetTableNames())
                : new List<string> { tableName };

            foreach (string tName in tableNames)
            {
                var table = StructExportCore.GetTable(tName);
                if (table == null)
                {
                    Console.Error.WriteLine($"Error: Unknown table '{tName}'. Available: {string.Join(", ", StructExportCore.GetTableNames())}");
                    return 1;
                }

                var structDef = StructExportCore.LoadStructDef(CoreState.ROM, table);
                if (structDef == null)
                {
                    Console.Error.WriteLine($"Error: Could not load struct definition for table '{tName}'.");
                    return 1;
                }

                var entries = StructExportCore.ExportTable(CoreState.ROM, table, structDef);

                string format = "tsv";
                if (argsDic.ContainsKey("--format") && !string.IsNullOrEmpty(argsDic["--format"]))
                    format = argsDic["--format"].ToLowerInvariant();

                string ext = format switch { "csv" => ".csv", "ea" => ".ea", _ => ".tsv" };

                string outPath;
                if (argsDic.ContainsKey("--out") && !string.IsNullOrEmpty(argsDic["--out"]))
                {
                    if (tableNames.Count > 1)
                        outPath = argsDic["--out"] + "." + tName + ext;
                    else
                        outPath = argsDic["--out"];
                }
                else
                {
                    outPath = Path.ChangeExtension(romPath, "." + tName + ext);
                }

                switch (format)
                {
                    case "csv":
                        StructExportCore.ExportToCSV(entries, structDef, outPath);
                        break;
                    case "ea":
                        StructExportCore.ExportToEA(entries, structDef, outPath);
                        break;
                    default:
                        StructExportCore.ExportToTSV(entries, structDef, outPath);
                        break;
                }
                Console.WriteLine($"Exported {entries.Count} {tName} entries ({format}) to: {outPath}");
            }

            return 0;
        }

        static int RunImportData(Dictionary<string, string> argsDic)
        {
            if (!argsDic.ContainsKey("--rom") || string.IsNullOrEmpty(argsDic["--rom"]))
            {
                Console.Error.WriteLine("Error: --import-data requires --rom=<rom_file>");
                return 1;
            }
            if (!argsDic.ContainsKey("--table") || string.IsNullOrEmpty(argsDic["--table"]))
            {
                Console.Error.WriteLine("Error: --import-data requires --table=<name> (units, classes, items)");
                return 1;
            }
            if (!argsDic.ContainsKey("--in") || string.IsNullOrEmpty(argsDic["--in"]))
            {
                Console.Error.WriteLine("Error: --import-data requires --in=<path.tsv>");
                return 1;
            }

            string romPath = argsDic["--rom"];
            string tableName = argsDic["--table"];
            string inputPath = argsDic["--in"];
            string forceVersion = argsDic.ContainsKey("--force-version") ? argsDic["--force-version"] : null;

            if (!File.Exists(inputPath))
            {
                Console.Error.WriteLine($"Error: Input file not found: {inputPath}");
                return 1;
            }

            RomLoader.InitEnvironment();
            if (!RomLoader.LoadRom(romPath, forceVersion))
                return 1;
            RomLoader.InitFull();

            Console.WriteLine($"ROM: {romPath}");
            Console.WriteLine($"Version: {CoreState.ROM.RomInfo.VersionToFilename}");

            var table = StructExportCore.GetTable(tableName);
            if (table == null)
            {
                Console.Error.WriteLine($"Error: Unknown table '{tableName}'. Available: {string.Join(", ", StructExportCore.GetTableNames())}");
                return 1;
            }

            var structDef = StructExportCore.LoadStructDef(CoreState.ROM, table);
            if (structDef == null)
            {
                Console.Error.WriteLine($"Error: Could not load struct definition for table '{tableName}'.");
                return 1;
            }

            var entries = StructExportCore.ImportFromTSV(inputPath, structDef);
            Console.WriteLine($"Parsed {entries.Count} entries from TSV.");

            int written = StructExportCore.WriteTable(CoreState.ROM, table, structDef, entries);
            Console.WriteLine($"Wrote {written} entries to ROM.");

            CoreState.ROM.Save(romPath, true);
            Console.WriteLine($"ROM saved: {romPath}");
            return 0;
        }

        static int RunDataRoundTrip(Dictionary<string, string> argsDic)
        {
            if (!argsDic.ContainsKey("--rom") || string.IsNullOrEmpty(argsDic["--rom"]))
            {
                Console.Error.WriteLine("Error: --data-roundtrip requires --rom=<rom_file>");
                return 1;
            }

            string romPath = argsDic["--rom"];
            if (!File.Exists(romPath))
            {
                Console.Error.WriteLine($"Error: ROM file not found: {romPath}");
                return 1;
            }

            string tableName = argsDic.ContainsKey("--table") && !string.IsNullOrEmpty(argsDic["--table"])
                ? argsDic["--table"]
                : "all";
            string forceVersion = argsDic.ContainsKey("--force-version") ? argsDic["--force-version"] : null;

            // Work on a temporary copy
            string tempRom = Path.Combine(Path.GetTempPath(), $"data_roundtrip_{Guid.NewGuid():N}.gba");
            try
            {
                File.Copy(romPath, tempRom, true);

                RomLoader.InitEnvironment();
                if (!RomLoader.LoadRom(tempRom, forceVersion))
                    return 1;
                RomLoader.InitFull();

                Console.WriteLine($"ROM: {romPath}");
                Console.WriteLine($"Version: {CoreState.ROM.RomInfo.VersionToFilename}");
                Console.WriteLine("Running struct data round-trip validation...");

                List<StructExportCore.DataRoundTripResult> results;
                if (tableName.Equals("all", StringComparison.OrdinalIgnoreCase))
                {
                    results = StructExportCore.ValidateRoundTripAll(CoreState.ROM);
                }
                else
                {
                    results = new List<StructExportCore.DataRoundTripResult>
                    {
                        StructExportCore.ValidateRoundTrip(CoreState.ROM, tableName)
                    };
                }

                bool allLossless = true;
                foreach (var result in results)
                {
                    Console.WriteLine($"\nTable: {result.TableName}");
                    Console.WriteLine($"  Entries: {result.TotalEntries}");
                    Console.WriteLine($"  Match: {result.MatchCount}");
                    Console.WriteLine($"  Mismatch: {result.MismatchCount}");

                    if (!result.IsLossless)
                    {
                        allLossless = false;
                        int shown = 0;
                        foreach (var (idx, fieldName, before, after) in result.Mismatches)
                        {
                            if (shown >= 20)
                            {
                                Console.WriteLine($"  ... and {result.Mismatches.Count - 20} more mismatches");
                                break;
                            }
                            Console.WriteLine($"  MISMATCH [{idx}].{fieldName}: {before} → {after}");
                            shown++;
                        }
                    }
                }

                Console.WriteLine();
                if (allLossless)
                {
                    Console.WriteLine("RESULT: All tables are lossless (round-trip OK).");
                    return 0;
                }
                else
                {
                    Console.WriteLine("RESULT: Some tables have mismatches.");
                    return 2;
                }
            }
            finally
            {
                try { if (File.Exists(tempRom)) File.Delete(tempRom); } catch { }
            }
        }

        static int RunResolveNames(Dictionary<string, string> argsDic)
        {
            if (!argsDic.ContainsKey("--rom") || string.IsNullOrEmpty(argsDic["--rom"]))
            {
                Console.Error.WriteLine("Error: --resolve-names requires --rom=<rom>");
                return 1;
            }
            if (!argsDic.ContainsKey("--kind") || string.IsNullOrEmpty(argsDic["--kind"]))
            {
                Console.Error.WriteLine("Error: --resolve-names requires --kind=<unit|class|item|song>");
                return 1;
            }
            if (!argsDic.ContainsKey("--ids") || string.IsNullOrEmpty(argsDic["--ids"]))
            {
                Console.Error.WriteLine("Error: --resolve-names requires --ids=<0,1,2,...>");
                return 1;
            }

            string romPath = argsDic["--rom"];
            string kind = argsDic["--kind"].ToLower();
            string idsStr = argsDic["--ids"];

            RomLoader.InitEnvironment();
            string forceVersion = argsDic.ContainsKey("--force-version") ? argsDic["--force-version"] : null;
            if (!RomLoader.LoadRom(romPath, forceVersion))
                return 1;
            RomLoader.InitFull();

            string[] idParts = idsStr.Split(',');
            foreach (string idStr in idParts)
            {
                if (!uint.TryParse(idStr.Trim(), out uint id))
                {
                    Console.Error.WriteLine($"Warning: Invalid ID '{idStr}', skipping.");
                    continue;
                }
                string name;
                switch (kind)
                {
                    case "unit": name = NameResolver.GetUnitName(id); break;
                    case "class": name = NameResolver.GetClassName(id); break;
                    case "item": name = NameResolver.GetItemName(id); break;
                    case "song": name = NameResolver.GetSongName(id); break;
                    default:
                        Console.Error.WriteLine($"Error: Unknown kind '{kind}'. Use: unit, class, item, song");
                        return 1;
                }
                Console.WriteLine($"{id}\t{name}");
            }
            return 0;
        }

        static int RunRenderPortrait(Dictionary<string, string> argsDic)
        {
            if (!argsDic.ContainsKey("--rom") || string.IsNullOrEmpty(argsDic["--rom"]))
            {
                Console.Error.WriteLine("Error: --render-portrait requires --rom=<rom>");
                return 1;
            }
            if (!argsDic.ContainsKey("--unit-id") || string.IsNullOrEmpty(argsDic["--unit-id"]))
            {
                Console.Error.WriteLine("Error: --render-portrait requires --unit-id=<id>");
                return 1;
            }
            if (!argsDic.ContainsKey("--out") || string.IsNullOrEmpty(argsDic["--out"]))
            {
                Console.Error.WriteLine("Error: --render-portrait requires --out=<output.png>");
                return 1;
            }

            string romPath = argsDic["--rom"];
            string outputPath = argsDic["--out"];

            if (!uint.TryParse(argsDic["--unit-id"], out uint unitId))
            {
                Console.Error.WriteLine("Error: --unit-id must be a valid number.");
                return 1;
            }

            RomLoader.InitEnvironment();
            string forceVersion = argsDic.ContainsKey("--force-version") ? argsDic["--force-version"] : null;
            if (!RomLoader.LoadRom(romPath, forceVersion))
                return 1;
            RomLoader.InitFull();

            var rom = CoreState.ROM;
            if (rom?.RomInfo == null)
            {
                Console.Error.WriteLine("Error: ROM not loaded correctly.");
                return 1;
            }

            // Bounds check unit ID
            uint unitBase = rom.p32(U.toOffset(rom.RomInfo.unit_pointer));
            uint unitDataSize = rom.RomInfo.unit_datasize;
            uint unitAddr = unitBase + (unitId * unitDataSize);
            if (!U.isSafetyOffset(unitAddr + unitDataSize - 1, rom))
            {
                Console.Error.WriteLine($"Error: Unit ID {unitId} is out of range.");
                return 1;
            }

            // Portrait ID at offset +6 in unit struct
            uint portraitId = rom.u8(unitAddr + 6);

            // Portrait struct layout:
            //   +0: face pointer (4), +4: mini portrait (4), +8: palette (4), +12: mouth frames (4)
            //   FE7/8: +20: eyeX(1), +21: eyeY(1), +22: mouthX(1), +23: mouthY(1)
            uint portraitBase = rom.p32(U.toOffset(rom.RomInfo.portrait_pointer));
            uint portraitDataSize = rom.RomInfo.portrait_datasize;
            uint portraitAddr = portraitBase + (portraitId * portraitDataSize);
            if (!U.isSafetyOffset(portraitAddr + portraitDataSize - 1, rom))
            {
                Console.Error.WriteLine($"Error: Portrait ID {portraitId} is out of range.");
                return 1;
            }

            uint facePtr = rom.p32(portraitAddr + 0);
            uint palettePtr = rom.p32(portraitAddr + 8);  // palette at +8, not +4

            if (facePtr == 0 || palettePtr == 0)
            {
                Console.Error.WriteLine($"Error: Unit {unitId} has no portrait data (facePtr=0x{facePtr:X}, palettePtr=0x{palettePtr:X}).");
                return 1;
            }

            // Eye position: FE7/8 at +20,+21; FE6 has different layout (shorter struct)
            byte eyeX = (portraitDataSize > 21) ? (byte)rom.u8(portraitAddr + 20) : (byte)0;
            byte eyeY = (portraitDataSize > 21) ? (byte)rom.u8(portraitAddr + 21) : (byte)0;

            var image = PortraitRendererCore.DrawPortraitUnit(facePtr, palettePtr, eyeX, eyeY, 0);
            if (image == null)
            {
                Console.Error.WriteLine($"Error: Failed to render portrait for unit {unitId}.");
                return 1;
            }

            image.Save(outputPath);
            var fileInfo = new FileInfo(outputPath);
            Console.WriteLine($"Portrait rendered: {outputPath} ({fileInfo.Length:N0} bytes, {image.Width}x{image.Height})");
            return 0;
        }

        static int RunExportPortraitAll(Dictionary<string, string> argsDic)
        {
            if (!argsDic.ContainsKey("--rom") || string.IsNullOrEmpty(argsDic["--rom"]))
            {
                Console.Error.WriteLine("Error: --export-portrait-all requires --rom=<rom>");
                return 1;
            }
            if (!argsDic.ContainsKey("--out") || string.IsNullOrEmpty(argsDic["--out"]))
            {
                Console.Error.WriteLine("Error: --export-portrait-all requires --out=<directory>");
                return 1;
            }

            string romPath = argsDic["--rom"];
            string outputDir = argsDic["--out"];

            RomLoader.InitEnvironment();
            string forceVersion = argsDic.ContainsKey("--force-version") ? argsDic["--force-version"] : null;
            if (!RomLoader.LoadRom(romPath, forceVersion))
                return 1;
            RomLoader.InitFull();

            var rom = CoreState.ROM;
            if (rom?.RomInfo == null)
            {
                Console.Error.WriteLine("Error: ROM not loaded correctly.");
                return 1;
            }

            try { Directory.CreateDirectory(outputDir); }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Error: Cannot create output directory: {ex.Message}");
                return 1;
            }

            uint portraitBase = rom.p32(U.toOffset(rom.RomInfo.portrait_pointer));
            uint portraitDataSize = rom.RomInfo.portrait_datasize;

            // Determine entry count by scanning for valid portrait entries
            // Stop at first entry where both face and palette pointers are invalid (non-pointer, non-zero)
            int maxEntries = 0;
            int consecutiveInvalid = 0;
            for (uint id = 0; id < 0x400; id++)
            {
                uint addr = portraitBase + (id * portraitDataSize);
                if (!U.isSafetyOffset(addr + portraitDataSize - 1, rom))
                    break;

                uint faceVal = rom.u32(addr + 0);
                uint palVal = rom.u32(addr + 8);
                bool faceOk = faceVal == 0 || U.isPointer(faceVal);
                bool palOk = palVal == 0 || U.isPointer(palVal);

                if (!faceOk && !palOk)
                {
                    consecutiveInvalid++;
                    if (consecutiveInvalid >= 3) break; // 3 consecutive invalid = end of table
                }
                else
                {
                    consecutiveInvalid = 0;
                }
                maxEntries = (int)id + 1;
            }

            int exported = 0;
            int errors = 0;
            for (uint id = 0; id < (uint)maxEntries; id++)
            {
                uint portraitAddr = portraitBase + (id * portraitDataSize);

                uint facePtr = rom.p32(portraitAddr + 0);
                uint palettePtr = rom.p32(portraitAddr + 8);
                if (facePtr == 0 || palettePtr == 0)
                    continue;

                try
                {
                    IImage image;
                    if (rom.RomInfo.version == 6)
                    {
                        // FE6: portrait struct has mouth at +12/+13
                        byte fe6MouthX = (portraitDataSize > 13) ? (byte)rom.u8(portraitAddr + 12) : (byte)0;
                        byte fe6MouthY = (portraitDataSize > 13) ? (byte)rom.u8(portraitAddr + 13) : (byte)0;
                        image = PortraitRendererCoreFE6.DrawPortraitUnitFE6(facePtr, palettePtr, fe6MouthX, fe6MouthY, 0);
                    }
                    else
                    {
                        // FE7/8 portrait struct: +20=mouthX, +21=mouthY, +22=eyeX, +23=eyeY, +24=state
                        byte eyeX = (portraitDataSize > 23) ? (byte)rom.u8(portraitAddr + 22) : (byte)0;
                        byte eyeY = (portraitDataSize > 23) ? (byte)rom.u8(portraitAddr + 23) : (byte)0;
                        byte state = (portraitDataSize > 24) ? (byte)rom.u8(portraitAddr + 24) : (byte)0;
                        image = PortraitRendererCore.DrawPortraitUnit(facePtr, palettePtr, eyeX, eyeY, state);
                    }
                    using (image)
                    {
                        if (image == null) { Console.Error.WriteLine($"  Portrait {id}: render returned null"); errors++; continue; }

                        string outPath = Path.Combine(outputDir, $"portrait_{id:D3}.png");
                        image.Save(outPath);
                        exported++;
                    }
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"  Portrait {id}: {ex.Message}");
                    errors++;
                }
            }

            Console.WriteLine($"Exported {exported} portraits to {outputDir}/ ({errors} errors, {maxEntries} entries scanned)");
            return errors > 0 ? 2 : 0;
        }

        static int RunGenerateFont(Dictionary<string, string> argsDic)
        {
            if (!argsDic.ContainsKey("--out") || string.IsNullOrEmpty(argsDic["--out"]))
            {
                Console.Error.WriteLine("Error: --generate-font requires --out=<output.png>");
                return 1;
            }

            string text = argsDic.ContainsKey("--text") ? argsDic["--text"] : "A";
            string outputPath = argsDic["--out"];

            float fontSize = 12f;
            if (argsDic.ContainsKey("--font-size"))
                float.TryParse(argsDic["--font-size"], out fontSize);

            int verticalOffset = 0;
            if (argsDic.ContainsKey("--vertical-offset"))
                int.TryParse(argsDic["--vertical-offset"], out verticalOffset);
            verticalOffset = Math.Clamp(verticalOffset, -8, 8);

            // Load font via SkiaSharp (cross-platform)
            global::SkiaSharp.SKTypeface typeface = null;
            if (argsDic.ContainsKey("--font-file") && !string.IsNullOrEmpty(argsDic["--font-file"]))
            {
                string fontFile = argsDic["--font-file"];
                if (!File.Exists(fontFile))
                {
                    Console.Error.WriteLine($"Error: Font file not found: {fontFile}");
                    return 1;
                }
                typeface = global::SkiaSharp.SKTypeface.FromFile(fontFile);
                if (typeface == null)
                {
                    Console.Error.WriteLine($"Error: Failed to load font from {fontFile}");
                    return 1;
                }
            }

            using var paint = new global::SkiaSharp.SKPaint
            {
                TextSize = fontSize,
                IsAntialias = true,
                Color = global::SkiaSharp.SKColors.Black,
                Typeface = typeface ?? global::SkiaSharp.SKTypeface.Default
            };

            // Render each character to a 16x16 tile
            int charSize = 16;
            int totalWidth = text.Length * charSize;
            using var bitmap = new global::SkiaSharp.SKBitmap(totalWidth, charSize);
            using var canvas = new global::SkiaSharp.SKCanvas(bitmap);
            canvas.Clear(global::SkiaSharp.SKColors.White);

            for (int i = 0; i < text.Length; i++)
            {
                string ch = text[i].ToString();
                float x = i * charSize;
                float y = charSize - 2 + verticalOffset; // baseline offset
                canvas.DrawText(ch, x, y, paint);
            }

            // Save as PNG
            using var image = global::SkiaSharp.SKImage.FromBitmap(bitmap);
            using var data = image.Encode(global::SkiaSharp.SKEncodedImageFormat.Png, 100);
            using var stream = File.OpenWrite(outputPath);
            data.SaveTo(stream);

            typeface?.Dispose();
            var fileInfo = new FileInfo(outputPath);
            Console.WriteLine($"Font generated: {outputPath} ({text.Length} chars, {totalWidth}x{charSize}, {fileInfo.Length} bytes)");
            return 0;
        }

        static int RunImportPortrait(Dictionary<string, string> argsDic)
        {
            if (!argsDic.ContainsKey("--rom") || string.IsNullOrEmpty(argsDic["--rom"]))
            { Console.Error.WriteLine("Error: --import-portrait requires --rom=<rom>"); return 1; }
            // Accept both --portrait-id and --unit-id for backward compatibility
            string pidKey = argsDic.ContainsKey("--portrait-id") ? "--portrait-id" : "--unit-id";
            if (!argsDic.ContainsKey(pidKey) || string.IsNullOrEmpty(argsDic[pidKey]))
            { Console.Error.WriteLine("Error: --import-portrait requires --portrait-id=<id>"); return 1; }
            if (!argsDic.ContainsKey("--in") || string.IsNullOrEmpty(argsDic["--in"]))
            { Console.Error.WriteLine("Error: --import-portrait requires --in=<input.png>"); return 1; }

            string romPath = argsDic["--rom"];
            string inputPath = argsDic["--in"];

            if (!uint.TryParse(argsDic[pidKey], out uint portraitId))
            { Console.Error.WriteLine("Error: --unit-id must be a valid number."); return 1; }
            if (!File.Exists(inputPath))
            { Console.Error.WriteLine($"Error: Input file not found: {inputPath}"); return 1; }

            RomLoader.InitEnvironment();
            string forceVersion = argsDic.ContainsKey("--force-version") ? argsDic["--force-version"] : null;
            if (!RomLoader.LoadRom(romPath, forceVersion)) return 1;
            RomLoader.InitFull();

            var rom = CoreState.ROM;
            if (rom?.RomInfo == null)
            { Console.Error.WriteLine("Error: ROM not loaded correctly."); return 1; }

            uint portraitBase = rom.p32(U.toOffset(rom.RomInfo.portrait_pointer));
            uint portraitDataSize = rom.RomInfo.portrait_datasize;
            uint portraitAddr = portraitBase + (portraitId * portraitDataSize);
            if (!U.isSafetyOffset(portraitAddr + portraitDataSize - 1, rom))
            { Console.Error.WriteLine($"Error: Portrait ID {portraitId} is out of range."); return 1; }

            // Load PNG via SkiaSharp and convert to tightly packed RGBA
            byte[] rgbaPixels;
            int width, height;
            using (var skBitmap = global::SkiaSharp.SKBitmap.Decode(inputPath))
            {
                if (skBitmap == null)
                { Console.Error.WriteLine("Error: Failed to load image."); return 1; }

                width = skBitmap.Width;
                height = skBitmap.Height;
                // Ensure tightly packed RGBA (no stride padding)
                rgbaPixels = new byte[width * height * 4];
                using var converted = skBitmap.Copy(global::SkiaSharp.SKColorType.Rgba8888);
                for (int y = 0; y < height; y++)
                {
                    var span = converted.GetPixelSpan();
                    int srcOffset = y * converted.RowBytes;
                    int dstOffset = y * width * 4;
                    for (int x = 0; x < width * 4; x++)
                        rgbaPixels[dstOffset + x] = span[srcOffset + x];
                }
            }

            // Quantize to 16 colors using Core's DecreaseColorCore
            var quantResult = DecreaseColorCore.Quantize(rgbaPixels, width, height, 16);
            if (quantResult == null)
            { Console.Error.WriteLine("Error: Color quantization failed."); return 1; }

            // Encode as 4bpp tiles
            byte[] tileData = ImageImportCore.EncodeDirectTiles4bpp(quantResult.IndexData, width, height);
            if (tileData == null)
            { Console.Error.WriteLine("Error: Tile encoding failed."); return 1; }

            // Determine compression: FE6 always compressed, others check existing data
            uint currentFacePtr = rom.p32(portraitAddr + 0);
            bool isFE6 = rom.RomInfo.version == 6;
            bool isCompressed = isFE6 || (currentFacePtr == 0) ||
                (U.isSafetyOffset(U.toOffset(currentFacePtr)) && LZ77.iscompress(rom.Data, U.toOffset(currentFacePtr)));

            // Write tile data
            uint tileAddr;
            if (isCompressed)
            {
                tileAddr = ImageImportCore.WriteCompressedToROM(rom, tileData, portraitAddr + 0);
            }
            else
            {
                // Preserve existing header if present, otherwise use standard 4-byte header
                byte[] existingHeader = new byte[] { 0x00, 0x04, 0x10, 0x00 };
                if (U.isSafetyOffset(U.toOffset(currentFacePtr)))
                {
                    uint off = U.toOffset(currentFacePtr);
                    existingHeader[0] = (byte)rom.u8(off + 0);
                    existingHeader[1] = (byte)rom.u8(off + 1);
                    existingHeader[2] = (byte)rom.u8(off + 2);
                    existingHeader[3] = (byte)rom.u8(off + 3);
                }
                byte[] withHeader = new byte[4 + tileData.Length];
                Array.Copy(existingHeader, 0, withHeader, 0, 4);
                Array.Copy(tileData, 0, withHeader, 4, tileData.Length);
                tileAddr = ImageImportCore.WriteRawToROM(rom, withHeader, portraitAddr + 0);
            }
            if (tileAddr == U.NOT_FOUND)
            { Console.Error.WriteLine("Error: No free ROM space for tile data."); return 1; }

            // Write palette
            uint palAddr = ImageImportCore.WritePaletteToROM(rom, quantResult.GBAPalette, portraitAddr + 8);
            if (palAddr == U.NOT_FOUND)
            { Console.Error.WriteLine("Error: No free ROM space for palette."); return 1; }

            // Save ROM
            rom.Save(romPath, true);
            Console.WriteLine($"Portrait {portraitId} imported from {inputPath} ({width}x{height}) and saved to {romPath}");
            return 0;
        }

        static int RunImportPortraitAll(Dictionary<string, string> argsDic)
        {
            if (!argsDic.ContainsKey("--rom") || string.IsNullOrEmpty(argsDic["--rom"]))
            { Console.Error.WriteLine("Error: --import-portrait-all requires --rom=<rom>"); return 1; }
            if (!argsDic.ContainsKey("--dir") || string.IsNullOrEmpty(argsDic["--dir"]))
            { Console.Error.WriteLine("Error: --import-portrait-all requires --dir=<directory>"); return 1; }

            string romPath = argsDic["--rom"];
            string dir = argsDic["--dir"];

            if (!Directory.Exists(dir))
            { Console.Error.WriteLine($"Error: Directory not found: {dir}"); return 1; }

            RomLoader.InitEnvironment();
            string forceVersion = argsDic.ContainsKey("--force-version") ? argsDic["--force-version"] : null;
            if (!RomLoader.LoadRom(romPath, forceVersion)) return 1;
            RomLoader.InitFull();

            var rom = CoreState.ROM;
            if (rom?.RomInfo == null)
            { Console.Error.WriteLine("Error: ROM not loaded correctly."); return 1; }

            uint portraitBase = rom.p32(U.toOffset(rom.RomInfo.portrait_pointer));
            uint portraitDataSize = rom.RomInfo.portrait_datasize;

            string[] pngFiles = Directory.GetFiles(dir, "*.png");
            Array.Sort(pngFiles);

            int imported = 0;
            int failed = 0;

            foreach (string pngPath in pngFiles)
            {
                string name = Path.GetFileNameWithoutExtension(pngPath);
                // Parse portrait ID from filename: "123_Name.png" or "123.png" or "0x7B.png"
                string idPart = name.Split('_')[0];
                uint portraitId;
                if (idPart.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                {
                    if (!uint.TryParse(idPart.Substring(2), System.Globalization.NumberStyles.HexNumber, null, out portraitId))
                    { Console.Error.WriteLine($"  Skip {name}.png: cannot parse hex ID from filename"); failed++; continue; }
                }
                else
                {
                    if (!uint.TryParse(idPart, out portraitId))
                    { Console.Error.WriteLine($"  Skip {name}.png: cannot parse ID from filename"); failed++; continue; }
                }

                uint portraitAddr = portraitBase + (portraitId * portraitDataSize);
                if (!U.isSafetyOffset(portraitAddr + portraitDataSize - 1, rom))
                { Console.Error.WriteLine($"  Skip {name}.png: portrait ID {portraitId} out of range"); failed++; continue; }

                // Load and quantize
                using var skBitmap = global::SkiaSharp.SKBitmap.Decode(pngPath);
                if (skBitmap == null)
                { Console.Error.WriteLine($"  Skip {name}.png: failed to decode image"); failed++; continue; }

                int width = skBitmap.Width;
                int height = skBitmap.Height;
                byte[] rgbaPixels = new byte[width * height * 4];
                using (var converted = skBitmap.Copy(global::SkiaSharp.SKColorType.Rgba8888))
                {
                    var span = converted.GetPixelSpan();
                    for (int y = 0; y < height; y++)
                    {
                        int srcOffset = y * converted.RowBytes;
                        int dstOffset = y * width * 4;
                        for (int x = 0; x < width * 4; x++)
                            rgbaPixels[dstOffset + x] = span[srcOffset + x];
                    }
                }

                var quantResult = DecreaseColorCore.Quantize(rgbaPixels, width, height, 16);
                if (quantResult == null)
                { Console.Error.WriteLine($"  Skip {name}.png: color quantization failed"); failed++; continue; }

                byte[] tileData = ImageImportCore.EncodeDirectTiles4bpp(quantResult.IndexData, width, height);
                if (tileData == null)
                { Console.Error.WriteLine($"  Skip {name}.png: tile encoding failed"); failed++; continue; }

                // Determine compression
                uint currentFacePtr = rom.p32(portraitAddr + 0);
                bool isFE6 = rom.RomInfo.version == 6;
                bool isCompressed = isFE6 || (currentFacePtr == 0) ||
                    (U.isSafetyOffset(U.toOffset(currentFacePtr)) && LZ77.iscompress(rom.Data, U.toOffset(currentFacePtr)));

                uint tileAddr;
                if (isCompressed)
                {
                    tileAddr = ImageImportCore.WriteCompressedToROM(rom, tileData, portraitAddr + 0);
                }
                else
                {
                    byte[] existingHeader = new byte[] { 0x00, 0x04, 0x10, 0x00 };
                    if (U.isSafetyOffset(U.toOffset(currentFacePtr)))
                    {
                        uint off = U.toOffset(currentFacePtr);
                        existingHeader[0] = (byte)rom.u8(off);
                        existingHeader[1] = (byte)rom.u8(off + 1);
                        existingHeader[2] = (byte)rom.u8(off + 2);
                        existingHeader[3] = (byte)rom.u8(off + 3);
                    }
                    byte[] withHeader = new byte[4 + tileData.Length];
                    Array.Copy(existingHeader, 0, withHeader, 0, 4);
                    Array.Copy(tileData, 0, withHeader, 4, tileData.Length);
                    tileAddr = ImageImportCore.WriteRawToROM(rom, withHeader, portraitAddr + 0);
                }
                if (tileAddr == U.NOT_FOUND)
                { Console.Error.WriteLine($"  Skip {name}.png: no free ROM space for tiles"); failed++; continue; }

                uint palAddr = ImageImportCore.WritePaletteToROM(rom, quantResult.GBAPalette, portraitAddr + 8);
                if (palAddr == U.NOT_FOUND)
                { Console.Error.WriteLine($"  Skip {name}.png: no free ROM space for palette"); failed++; continue; }

                Console.WriteLine($"  Imported portrait {portraitId} from {name}.png ({width}x{height})");
                imported++;
            }

            if (imported > 0)
            {
                rom.Save(romPath, true);
                Console.WriteLine($"Batch import complete: {imported} imported, {failed} failed. Saved to {romPath}");
            }
            else
            {
                Console.WriteLine($"No portraits imported ({failed} failed).");
            }

            return failed > 0 && imported == 0 ? 1 : 0;
        }

        static int RunExportMidi(Dictionary<string, string> argsDic)
        {
            if (!argsDic.ContainsKey("--rom") || string.IsNullOrEmpty(argsDic["--rom"]))
            {
                Console.Error.WriteLine("Error: --export-midi requires --rom=<rom>");
                return 1;
            }
            if (!argsDic.ContainsKey("--song-id") || string.IsNullOrEmpty(argsDic["--song-id"]))
            {
                Console.Error.WriteLine("Error: --export-midi requires --song-id=<hex_id>");
                return 1;
            }
            if (!argsDic.ContainsKey("--out") || string.IsNullOrEmpty(argsDic["--out"]))
            {
                Console.Error.WriteLine("Error: --export-midi requires --out=<output.mid>");
                return 1;
            }

            string romPath = argsDic["--rom"];
            string outputPath = argsDic["--out"];
            string songIdStr = argsDic["--song-id"].Replace("0x", "").Replace("0X", "");

            if (!uint.TryParse(songIdStr, System.Globalization.NumberStyles.HexNumber, null, out uint songId))
            {
                Console.Error.WriteLine("Error: Invalid --song-id hex value.");
                return 1;
            }

            RomLoader.InitEnvironment();
            string forceVersion = argsDic.ContainsKey("--force-version") ? argsDic["--force-version"] : null;
            if (!RomLoader.LoadRom(romPath, forceVersion))
                return 1;
            RomLoader.InitFull();

            var rom = CoreState.ROM;
            uint soundTablePtr = rom.RomInfo.sound_table_pointer;
            uint tableAddr = rom.p32(U.toOffset(soundTablePtr));

            // Each song table entry is 8 bytes: pointer to song header (4) + extra (4)
            uint songAddr = tableAddr + (songId * 8);
            if (!U.isSafetyOffset(songAddr + 3, rom))
            {
                Console.Error.WriteLine($"Error: Song 0x{songId:X} is out of range.");
                return 1;
            }
            uint songHeaderPtr = rom.p32(songAddr);

            if (songHeaderPtr == 0 || !U.isSafetyOffset(songHeaderPtr + 7, rom))
            {
                Console.Error.WriteLine($"Error: Song 0x{songId:X} not found or invalid pointer.");
                return 1;
            }

            // Song header: numTracks(1), numBlks(1), priority(1), reverb(1), voicegroup(4)
            uint numTracks = rom.u8(songHeaderPtr);
            uint numBlks = rom.u8(songHeaderPtr + 1);
            uint priority = rom.u8(songHeaderPtr + 2);
            uint reverb = rom.u8(songHeaderPtr + 3);
            uint voicegroupPtr = rom.p32(songHeaderPtr + 4);

            if (numTracks == 0 || numTracks > 16)
            {
                Console.Error.WriteLine($"Error: Song 0x{songId:X} has invalid track count ({numTracks}).");
                return 1;
            }

            // Parse GBA tracks using SongMidiCore
            var tracks = SongMidiCore.ParseTracks(rom, songHeaderPtr, numTracks);

            SongMidiCore.ExportMidiFile(outputPath, tracks, (int)numBlks, (int)priority, (int)reverb, voicegroupPtr);
            var fileInfo = new FileInfo(outputPath);
            Console.WriteLine($"MIDI exported: {outputPath} ({fileInfo.Length:N0} bytes, {numTracks} tracks)");
            return 0;
        }

        static int RunDisasmEvent(Dictionary<string, string> argsDic)
        {
            if (!argsDic.ContainsKey("--rom") || string.IsNullOrEmpty(argsDic["--rom"]))
            {
                Console.Error.WriteLine("Error: --disasm-event requires --rom=<rom>");
                return 1;
            }
            if (!argsDic.ContainsKey("--addr") || string.IsNullOrEmpty(argsDic["--addr"]))
            {
                Console.Error.WriteLine("Error: --disasm-event requires --addr=<hex_address>");
                return 1;
            }

            string romPath = argsDic["--rom"];
            string addrStr = argsDic["--addr"].Replace("0x", "").Replace("0X", "");
            string scriptType = argsDic.ContainsKey("--type") ? argsDic["--type"].ToLower() : "event";
            string outputPath = argsDic.ContainsKey("--out") ? argsDic["--out"] : null;

            if (!uint.TryParse(addrStr, System.Globalization.NumberStyles.HexNumber, null, out uint addr))
            {
                Console.Error.WriteLine("Error: Invalid --addr hex value.");
                return 1;
            }

            RomLoader.InitEnvironment();
            string forceVersion = argsDic.ContainsKey("--force-version") ? argsDic["--force-version"] : null;
            if (!RomLoader.LoadRom(romPath, forceVersion))
                return 1;
            RomLoader.InitFull();

            EventScript es;
            switch (scriptType)
            {
                case "event": es = CoreState.EventScript; break;
                case "procs": es = CoreState.ProcsScript; break;
                case "ai": es = CoreState.AIScript; break;
                default:
                    Console.Error.WriteLine($"Error: Unknown script type '{scriptType}'. Use: event, procs, ai");
                    return 1;
            }
            if (es == null)
            {
                Console.Error.WriteLine("Error: Event script system not initialized.");
                return 1;
            }

            var rom = CoreState.ROM;
            var lines = new List<string>();
            uint currentAddr = U.toOffset(addr);
            int maxOps = 500; // safety limit
            int consecutiveUnknowns = 0;

            for (int i = 0; i < maxOps; i++)
            {
                if (currentAddr >= (uint)rom.Data.Length)
                    break;

                var code = es.DisAseemble(rom.Data, currentAddr + 0x08000000);
                if (code == null || code.Script == null)
                {
                    lines.Add($"0x{currentAddr:X08}\t???");
                    break;
                }

                // Stop after consecutive unknown instructions (invalid data region)
                if (code.Script.Has == EventScript.ScriptHas.UNKNOWN)
                {
                    consecutiveUnknowns++;
                    if (consecutiveUnknowns >= 3)
                    {
                        lines.Add($"0x{currentAddr:X08}\t(stopped: {consecutiveUnknowns} consecutive unknown instructions)");
                        break;
                    }
                }
                else
                {
                    consecutiveUnknowns = 0;
                }

                // Format: address \t command_name \t arg1 \t arg2 ...
                var sb = new System.Text.StringBuilder();
                string scriptName = (code.Script.Info != null && code.Script.Info.Length > 0) ? code.Script.Info[0] : "???";
                sb.Append($"0x{currentAddr:X08}\t{scriptName}");

                for (int a = 0; a < code.Script.Args.Length; a++)
                {
                    if (code.Script.Args[a].Type != EventScript.ArgType.FIXED)
                    {
                        uint v;
                        string argStr = EventScript.GetArg(code, a, out v);
                        sb.Append($"\t{argStr}");
                    }
                }
                lines.Add(sb.ToString());

                currentAddr += (uint)code.Script.Size;

                // Stop on terminator instructions
                if (code.Script.Has == EventScript.ScriptHas.TERM
                    || code.Script.Has == EventScript.ScriptHas.MAPTERM)
                    break;
            }

            string output = string.Join("\n", lines);

            if (!string.IsNullOrEmpty(outputPath))
            {
                File.WriteAllText(outputPath, output);
                Console.WriteLine($"Event script disassembled: {outputPath} ({lines.Count} instructions)");
            }
            else
            {
                Console.Write(output);
                if (!output.EndsWith("\n")) Console.WriteLine();
                Console.Error.WriteLine($"Disassembled {lines.Count} instructions from 0x{U.toOffset(addr):X08}");
            }
            return 0;
        }

        static int RunLintOAM(Dictionary<string, string> argsDic)
        {
            if (!argsDic.ContainsKey("--rom") || string.IsNullOrEmpty(argsDic["--rom"]))
            {
                Console.Error.WriteLine("Error: --lint-oam requires --rom=<rom>");
                return 1;
            }
            if (!argsDic.ContainsKey("--addr") || string.IsNullOrEmpty(argsDic["--addr"]))
            {
                Console.Error.WriteLine("Error: --lint-oam requires --addr=<hex_address>");
                return 1;
            }

            string romPath = argsDic["--rom"];
            string addrStr = argsDic["--addr"].Replace("0x", "").Replace("0X", "");
            int length = 0;
            if (argsDic.ContainsKey("--length") && !string.IsNullOrEmpty(argsDic["--length"]))
            {
                int.TryParse(argsDic["--length"], out length);
            }

            if (!uint.TryParse(addrStr, System.Globalization.NumberStyles.HexNumber, null, out uint addr))
            {
                Console.Error.WriteLine("Error: Invalid --addr hex value.");
                return 1;
            }

            RomLoader.InitEnvironment();
            string forceVersion = argsDic.ContainsKey("--force-version") ? argsDic["--force-version"] : null;
            if (!RomLoader.LoadRom(romPath, forceVersion))
                return 1;

            var rom = CoreState.ROM;
            uint offset = U.toOffset(addr);
            if (!U.isSafetyOffset(offset, rom))
            {
                Console.Error.WriteLine($"Error: Address 0x{addr:X} is out of ROM range.");
                return 1;
            }

            var errors = BattleAnimeCompositionCore.LintOAM(rom.Data, (int)offset, length);

            if (errors.Count == 0)
            {
                Console.WriteLine($"OAM lint: CLEAN (no issues at 0x{addr:X})");
                return 0;
            }
            else
            {
                Console.WriteLine($"OAM lint: {errors.Count} issue(s) at 0x{addr:X}");
                foreach (var err in errors)
                    Console.WriteLine($"  {err}");
                return 1;
            }
        }

        static int RunApplyPatch(Dictionary<string, string> argsDic)
        {
            if (!argsDic.ContainsKey("--rom") || string.IsNullOrEmpty(argsDic["--rom"]))
            {
                Console.Error.WriteLine("Error: --apply-patch requires --rom=<rom>");
                return 1;
            }
            if (!argsDic.ContainsKey("--patch-file") || string.IsNullOrEmpty(argsDic["--patch-file"]))
            {
                Console.Error.WriteLine("Error: --apply-patch requires --patch-file=<PATCH_xxx.txt>");
                return 1;
            }

            string romPath = argsDic["--rom"];
            string patchFile = argsDic["--patch-file"];

            if (!File.Exists(patchFile))
            {
                Console.Error.WriteLine($"Error: Patch file not found: {patchFile}");
                return 1;
            }

            RomLoader.InitEnvironment();
            string forceVersion = argsDic.ContainsKey("--force-version") ? argsDic["--force-version"] : null;
            if (!RomLoader.LoadRom(romPath, forceVersion))
                return 1;
            RomLoader.InitFull();

            // Check dependencies first
            var deps = PatchMetadataCore.CheckDependencies(CoreState.ROM, patchFile);
            if (deps.Count > 0)
            {
                var unsatisfied = deps.Where(d => !d.IsSatisfied).ToList();
                if (unsatisfied.Count > 0)
                {
                    Console.Error.WriteLine("Error: Unsatisfied patch dependencies:");
                    foreach (var d in unsatisfied)
                        Console.Error.WriteLine($"  {d.Condition} — {d.Comment}");
                    return 1;
                }
            }

            // Create backup before applying
            string backupPath = romPath + ".backup";
            File.Copy(romPath, backupPath, true);
            Console.WriteLine($"Backup saved: {backupPath}");

            var result = PatchMetadataCore.ApplyPatch(CoreState.ROM, patchFile);

            if (result.Success)
            {
                // Save the modified ROM
                File.WriteAllBytes(romPath, CoreState.ROM.Data);
                Console.WriteLine($"Patch applied: {result.Message} ({result.BytesWritten} bytes written)");
                return 0;
            }
            else
            {
                Console.Error.WriteLine($"Error: {result.Message}");
                // Restore from backup
                File.Copy(backupPath, romPath, true);
                Console.Error.WriteLine("ROM restored from backup.");
                return 1;
            }
        }

        static int RunListPatches(Dictionary<string, string> argsDic)
        {
            if (!argsDic.ContainsKey("--rom") || string.IsNullOrEmpty(argsDic["--rom"]))
            {
                Console.Error.WriteLine("Error: --list-patches requires --rom=<rom>");
                return 1;
            }

            string romPath = argsDic["--rom"];
            RomLoader.InitEnvironment();
            string forceVersion = argsDic.ContainsKey("--force-version") ? argsDic["--force-version"] : null;
            if (!RomLoader.LoadRom(romPath, forceVersion))
                return 1;

            string version = CoreState.ROM.RomInfo?.VersionToFilename;
            if (string.IsNullOrEmpty(version))
            {
                Console.Error.WriteLine("Error: Could not detect ROM version.");
                return 1;
            }

            // Try multiple locations for patch2 directory
            string baseDir = CoreState.BaseDirectory ?? AppDomain.CurrentDomain.BaseDirectory;
            string patchDir = Path.Combine(baseDir, "config", "patch2", version);

            // Fallback: check repo-root config/patch2 (for development runs)
            if (!Directory.Exists(patchDir) || !Directory.GetFiles(patchDir, "PATCH_*.txt", SearchOption.AllDirectories).Any())
            {
                string repoRoot = FindRepoRoot(baseDir);
                if (repoRoot != null)
                {
                    string altDir = Path.Combine(repoRoot, "config", "patch2", version);
                    if (Directory.Exists(altDir))
                        patchDir = altDir;
                }
            }

            if (!Directory.Exists(patchDir))
            {
                Console.Error.WriteLine($"Error: Patch directory not found: {patchDir}");
                return 1;
            }

            string lang = CoreState.Language ?? "en";
            var patches = PatchMetadataCore.EnumeratePatches(patchDir, CoreState.ROM, lang);

            // Apply --patch-name filter if specified
            string patchNameFilter = argsDic.ContainsKey("--patch-name") ? argsDic["--patch-name"] : null;
            if (!string.IsNullOrEmpty(patchNameFilter))
            {
                patches = patches.Where(p =>
                    p.DirectoryName != null &&
                    p.DirectoryName.IndexOf(patchNameFilter, StringComparison.OrdinalIgnoreCase) >= 0
                ).ToList();
            }

            Console.WriteLine($"ROM: {romPath}");
            Console.WriteLine($"Version: {version}");
            Console.WriteLine($"Patch directory: {patchDir}");
            if (!string.IsNullOrEmpty(patchNameFilter))
                Console.WriteLine($"Filter: {patchNameFilter}");
            Console.WriteLine();

            int installed = 0, unknown = 0;
            foreach (var p in patches)
            {
                string status;
                switch (p.Status)
                {
                    case PatchMetadataCore.PatchStatus.Installed:
                        status = "[INSTALLED]";
                        installed++;
                        break;
                    case PatchMetadataCore.PatchStatus.NotInstalled:
                        status = "[         ]";
                        break;
                    default:
                        status = "[  ???    ]";
                        unknown++;
                        break;
                }
                Console.WriteLine($"  {status} {p.DirectoryName}");
            }

            Console.WriteLine();
            Console.WriteLine($"Total: {patches.Count} patches, {installed} installed" +
                (unknown > 0 ? $", {unknown} unknown" : ""));
            return 0;
        }

        static int RunThreeWayMerge(Dictionary<string, string> argsDic)
        {
            string[] required = { "--base", "--mine", "--theirs", "--out" };
            foreach (string key in required)
            {
                if (!argsDic.ContainsKey(key) || string.IsNullOrEmpty(argsDic[key]))
                {
                    Console.Error.WriteLine($"Error: --merge3 requires {key}=<path>");
                    return 1;
                }
            }

            string basePath = argsDic["--base"];
            string minePath = argsDic["--mine"];
            string theirsPath = argsDic["--theirs"];
            string outPath = argsDic["--out"];

            if (!File.Exists(basePath)) { Console.Error.WriteLine($"Error: Base ROM not found: {basePath}"); return 1; }
            if (!File.Exists(minePath)) { Console.Error.WriteLine($"Error: Mine ROM not found: {minePath}"); return 1; }
            if (!File.Exists(theirsPath)) { Console.Error.WriteLine($"Error: Theirs ROM not found: {theirsPath}"); return 1; }

            byte[] baseRom = File.ReadAllBytes(basePath);
            byte[] mineRom = File.ReadAllBytes(minePath);
            byte[] theirsRom = File.ReadAllBytes(theirsPath);

            // Pad to same size if needed
            int maxLen = Math.Max(baseRom.Length, Math.Max(mineRom.Length, theirsRom.Length));
            if (baseRom.Length < maxLen) { byte[] tmp = new byte[maxLen]; Array.Copy(baseRom, tmp, baseRom.Length); for (int i = baseRom.Length; i < maxLen; i++) tmp[i] = 0xFF; baseRom = tmp; }
            if (mineRom.Length < maxLen) { byte[] tmp = new byte[maxLen]; Array.Copy(mineRom, tmp, mineRom.Length); for (int i = mineRom.Length; i < maxLen; i++) tmp[i] = 0xFF; mineRom = tmp; }
            if (theirsRom.Length < maxLen) { byte[] tmp = new byte[maxLen]; Array.Copy(theirsRom, tmp, theirsRom.Length); for (int i = theirsRom.Length; i < maxLen; i++) tmp[i] = 0xFF; theirsRom = tmp; }

            Console.WriteLine($"Base:   {basePath} ({new FileInfo(basePath).Length:N0} bytes)");
            Console.WriteLine($"Mine:   {minePath} ({new FileInfo(minePath).Length:N0} bytes)");
            Console.WriteLine($"Theirs: {theirsPath} ({new FileInfo(theirsPath).Length:N0} bytes)");
            Console.WriteLine();

            var result = ThreeWayMergeCore.Merge(baseRom, mineRom, theirsRom);

            File.WriteAllBytes(outPath, result.MergedData);

            Console.WriteLine($"Merged: {outPath} ({result.MergedData.Length:N0} bytes)");
            Console.WriteLine($"Changes from Mine:   {result.ChangesMine:N0} bytes");
            Console.WriteLine($"Changes from Theirs: {result.ChangesTheirs:N0} bytes");
            Console.WriteLine($"Both same change:    {result.ChangesBoth:N0} bytes");
            Console.WriteLine($"Conflicts:           {result.ConflictBytes:N0} bytes ({result.Conflicts.Count} ranges)");

            if (result.Conflicts.Count > 0)
            {
                Console.WriteLine();
                Console.WriteLine("Conflict ranges (defaulted to Mine's values):");
                foreach (var c in result.Conflicts)
                {
                    Console.WriteLine($"  0x{c.Offset:X} - 0x{c.Offset + c.Length - 1:X} ({c.Length} bytes)");
                }
                return 2; // Exit code 2 = merged with conflicts
            }

            return 0;
        }

        static int RunExpandTable(Dictionary<string, string> argsDic)
        {
            if (!argsDic.ContainsKey("--rom") || string.IsNullOrEmpty(argsDic["--rom"]))
            {
                Console.Error.WriteLine("Error: --expand-table requires --rom=<rom>");
                return 1;
            }
            if (!argsDic.ContainsKey("--pointer") || string.IsNullOrEmpty(argsDic["--pointer"]))
            {
                Console.Error.WriteLine("Error: --expand-table requires --pointer=<hex>");
                return 1;
            }
            if (!argsDic.ContainsKey("--entry-size") || string.IsNullOrEmpty(argsDic["--entry-size"]))
            {
                Console.Error.WriteLine("Error: --expand-table requires --entry-size=<int>");
                return 1;
            }

            string romPath = argsDic["--rom"];
            string ptrStr = argsDic["--pointer"].Replace("0x", "").Replace("0X", "");
            if (!uint.TryParse(ptrStr, System.Globalization.NumberStyles.HexNumber, null, out uint pointerAddr))
            {
                Console.Error.WriteLine("Error: Invalid pointer address.");
                return 1;
            }
            if (!uint.TryParse(argsDic["--entry-size"], out uint entrySize) || entrySize == 0)
            {
                Console.Error.WriteLine("Error: Invalid entry size.");
                return 1;
            }

            RomLoader.InitEnvironment();
            string forceVersion = argsDic.ContainsKey("--force-version") ? argsDic["--force-version"] : null;
            if (!RomLoader.LoadRom(romPath, forceVersion))
                return 1;

            var rom = CoreState.ROM;
            var info = DataExpansionCore.GetTableInfo(rom, pointerAddr, entrySize);
            if (info == null)
            {
                Console.Error.WriteLine("Error: Could not read table at pointer 0x{0:X}.", pointerAddr);
                return 1;
            }

            uint currentCount;
            if (argsDic.ContainsKey("--count") && uint.TryParse(argsDic["--count"], out uint userCount))
            {
                currentCount = userCount;
            }
            else
            {
                Console.Error.WriteLine("Error: --count is required for safety. Use --count=<N> to specify the current entry count.");
                Console.Error.WriteLine($"  Hint: auto-detected estimate is {info.EstimatedCount} (stops at first all-zero entry).");
                return 1;
            }

            Console.WriteLine($"ROM: {romPath}");
            Console.WriteLine($"Table pointer: 0x{pointerAddr:X}");
            Console.WriteLine($"Table base: 0x{info.BaseAddress:X}");
            Console.WriteLine($"Entry size: {entrySize} bytes");
            Console.WriteLine($"Current count: {currentCount}");

            // Create backup
            string backupPath = romPath + ".backup";
            File.Copy(romPath, backupPath, true);
            Console.WriteLine($"Backup: {backupPath}");

            var result = DataExpansionCore.ExpandTable(rom, pointerAddr, entrySize, currentCount);
            if (result.Success)
            {
                File.WriteAllBytes(romPath, rom.Data);
                Console.WriteLine($"Expanded: new base 0x{result.NewBaseAddress:X}, count {result.NewCount}");
                return 0;
            }
            else
            {
                Console.Error.WriteLine($"Error: {result.Error}");
                File.Copy(backupPath, romPath, true);
                Console.Error.WriteLine("ROM restored from backup.");
                return 1;
            }
        }

        static int RunUninstallPatch(Dictionary<string, string> argsDic)
        {
            if (!argsDic.ContainsKey("--rom") || string.IsNullOrEmpty(argsDic["--rom"]))
            {
                Console.Error.WriteLine("Error: --uninstall-patch requires --rom=<rom>");
                return 1;
            }
            if (!argsDic.ContainsKey("--patch-file") || string.IsNullOrEmpty(argsDic["--patch-file"]))
            {
                Console.Error.WriteLine("Error: --uninstall-patch requires --patch-file=<PATCH_xxx.txt>");
                return 1;
            }
            if (!argsDic.ContainsKey("--original-rom") || string.IsNullOrEmpty(argsDic["--original-rom"]))
            {
                Console.Error.WriteLine("Error: --uninstall-patch requires --original-rom=<clean_rom.gba>");
                return 1;
            }

            string romPath = argsDic["--rom"];
            string patchFile = argsDic["--patch-file"];
            string originalRomPath = argsDic["--original-rom"];

            if (!File.Exists(patchFile))
            {
                Console.Error.WriteLine($"Error: Patch file not found: {patchFile}");
                return 1;
            }
            if (!File.Exists(originalRomPath))
            {
                Console.Error.WriteLine($"Error: Original ROM not found: {originalRomPath}");
                return 1;
            }

            byte[] modifiedRom = File.ReadAllBytes(romPath);
            byte[] originalRom = File.ReadAllBytes(originalRomPath);

            // Parse BIN lines from the patch file to find addresses that were patched
            int restoredRanges = 0;
            int restoredBytes = 0;
            bool hasUnsupportedDirectives = false;
            string patchDir = Path.GetDirectoryName(patchFile);

            foreach (string line in File.ReadLines(patchFile))
            {
                string trimmed = line.Trim();
                if (trimmed.StartsWith("//") || trimmed.StartsWith("#")) continue;

                // Detect unsupported directives
                if (trimmed.StartsWith("BIN:$FREEAREA") || trimmed.StartsWith("JUMP:") ||
                    trimmed.StartsWith("EA:") || trimmed.StartsWith("CLEAR:"))
                {
                    hasUnsupportedDirectives = true;
                    continue;
                }

                // BIN:0xADDR=filename.bin — restore original bytes at that address
                if (trimmed.StartsWith("BIN:"))
                {
                    string rest = trimmed.Substring(4);
                    int eqIdx = rest.IndexOf('=');
                    if (eqIdx < 0) continue;

                    string addrStr = rest.Substring(0, eqIdx).Trim();
                    string binFile = rest.Substring(eqIdx + 1).Trim();

                    if (!uint.TryParse(addrStr.Replace("0x", "").Replace("0X", ""),
                        System.Globalization.NumberStyles.HexNumber, null, out uint addr))
                        continue;

                    // Determine size of patched region from bin file
                    int size;
                    string binPath = Path.Combine(patchDir, binFile);
                    if (File.Exists(binPath))
                    {
                        size = (int)new FileInfo(binPath).Length;
                    }
                    else
                    {
                        // If bin file not found, skip this range
                        Console.Error.WriteLine($"  Warning: {binFile} not found, skipping 0x{addr:X}");
                        continue;
                    }

                    if (addr + size > originalRom.Length || addr + size > modifiedRom.Length)
                    {
                        Console.Error.WriteLine($"  Warning: range 0x{addr:X}+{size} exceeds ROM size, skipping");
                        continue;
                    }

                    // Restore original bytes
                    Array.Copy(originalRom, addr, modifiedRom, addr, size);
                    restoredRanges++;
                    restoredBytes += size;
                    Console.WriteLine($"  Restored 0x{addr:X} ({size} bytes)");
                }
            }

            if (restoredRanges == 0)
            {
                Console.Error.WriteLine("No BIN ranges found in patch file. Only BIN-type patches can be uninstalled.");
                return 1;
            }

            // Create backup before writing
            string backupPath = romPath + ".backup";
            File.Copy(romPath, backupPath, true);
            Console.WriteLine($"Backup saved: {backupPath}");

            File.WriteAllBytes(romPath, modifiedRom);
            Console.WriteLine($"Patch uninstalled: {restoredRanges} ranges, {restoredBytes} bytes restored");
            if (hasUnsupportedDirectives)
            {
                Console.Error.WriteLine("Warning: This patch contains FREEAREA/JUMP/EA/CLEAR directives that");
                Console.Error.WriteLine("could not be reversed. The uninstall may be incomplete.");
                Console.Error.WriteLine("For full uninstall, use the GUI patch manager.");
            }
            return 0;
        }

        /// <summary>
        /// Walk up from a directory to find the repo root (contains .git).
        /// </summary>
        static string FindRepoRoot(string startDir)
        {
            string dir = startDir;
            while (!string.IsNullOrEmpty(dir))
            {
                if (Directory.Exists(Path.Combine(dir, ".git")))
                    return dir;
                dir = Path.GetDirectoryName(dir);
            }
            return null;
        }

        static int RunListResources(Dictionary<string, string> argsDic)
        {
            string baseDir = CoreState.BaseDirectory ?? AppDomain.CurrentDomain.BaseDirectory;
            string repoRoot = FindRepoRoot(baseDir) ?? baseDir;

            string category = argsDic.ContainsKey("--category") ? argsDic["--category"] : null;

            // Resource directories to scan
            var repoDirs = new (string path, string label)[]
            {
                (Path.Combine(repoRoot, "resources", "FE-Repo"), "FE-Repo (Graphics)"),
                (Path.Combine(repoRoot, "resources", "FE-Repo-Music-No-Preview"), "FE-Repo-Music"),
            };

            int totalResources = 0;
            foreach (var (repoPath, label) in repoDirs)
            {
                if (!Directory.Exists(repoPath))
                {
                    Console.WriteLine($"{label}: not found at {repoPath}");
                    Console.WriteLine("  Run: git submodule update --init resources/");
                    Console.WriteLine();
                    continue;
                }

                Console.WriteLine($"{label}: {repoPath}");

                string[] categories = Directory.GetDirectories(repoPath)
                    .Where(d => !Path.GetFileName(d).StartsWith("ZZ") &&
                                !Path.GetFileName(d).StartsWith("."))
                    .OrderBy(d => Path.GetFileName(d))
                    .ToArray();

                foreach (string catDir in categories)
                {
                    string catName = Path.GetFileName(catDir);

                    // Filter by category if specified
                    if (!string.IsNullOrEmpty(category) &&
                        !catName.Contains(category, StringComparison.OrdinalIgnoreCase))
                        continue;

                    int fileCount = Directory.GetFiles(catDir, "*", SearchOption.AllDirectories).Length;
                    int subDirCount = Directory.GetDirectories(catDir).Length;
                    Console.WriteLine($"  {catName}: {subDirCount} folders, {fileCount} files");
                    totalResources += fileCount;
                }
                Console.WriteLine();
            }

            Console.WriteLine($"Total resource files: {totalResources}");
            return 0;
        }

        static int RunImportMidi(Dictionary<string, string> argsDic)
        {
            if (!argsDic.ContainsKey("--rom") || string.IsNullOrEmpty(argsDic["--rom"]))
            {
                Console.Error.WriteLine("Error: --import-midi requires --rom=<rom>");
                return 1;
            }
            if (!argsDic.ContainsKey("--song-id") || string.IsNullOrEmpty(argsDic["--song-id"]))
            {
                Console.Error.WriteLine("Error: --import-midi requires --song-id=<hex_id>");
                return 1;
            }
            if (!argsDic.ContainsKey("--in") || string.IsNullOrEmpty(argsDic["--in"]))
            {
                Console.Error.WriteLine("Error: --import-midi requires --in=<midi_file>");
                return 1;
            }

            string romPath = argsDic["--rom"];
            string midiPath = argsDic["--in"];
            string songIdStr = argsDic["--song-id"].Replace("0x", "").Replace("0X", "");

            if (!uint.TryParse(songIdStr, System.Globalization.NumberStyles.HexNumber, null, out uint songId))
            {
                Console.Error.WriteLine("Error: Invalid --song-id hex value.");
                return 1;
            }

            if (!File.Exists(midiPath))
            {
                Console.Error.WriteLine($"Error: MIDI file not found: {midiPath}");
                return 1;
            }

            RomLoader.InitEnvironment();
            string forceVersion = argsDic.ContainsKey("--force-version") ? argsDic["--force-version"] : null;
            if (!RomLoader.LoadRom(romPath, forceVersion))
                return 1;
            RomLoader.InitFull();

            var rom = CoreState.ROM;
            uint soundTablePtr = rom.RomInfo.sound_table_pointer;
            uint tableAddr = rom.p32(U.toOffset(soundTablePtr));

            // Each song table entry is 8 bytes: pointer to song header (4) + extra (4)
            uint songAddr = tableAddr + (songId * 8);
            if (!U.isSafetyOffset(songAddr + 7, rom))
            {
                Console.Error.WriteLine($"Error: Song 0x{songId:X} is out of range.");
                return 1;
            }

            uint songHeaderPtr = rom.p32(songAddr);
            if (songHeaderPtr == 0 || !U.isSafetyOffset(songHeaderPtr + 7, rom))
            {
                Console.Error.WriteLine($"Error: Song 0x{songId:X} not found or invalid pointer.");
                return 1;
            }

            // Read instrument pointer from song header (+4) as raw GBA pointer
            // ImportMidiFile expects the raw 0x08xxxxxx pointer, not a ROM offset
            uint instrumentPtr = rom.u32(songHeaderPtr + 4);

            // Parse MIDI info for display
            var midiInfo = SongMidiCore.ParseMidiFile(midiPath);
            if (midiInfo != null)
            {
                Console.WriteLine($"MIDI: {midiPath}");
                Console.WriteLine($"  Format: {midiInfo.Format}, Tracks: {midiInfo.TrackCount}, TPQN: {midiInfo.TicksPerQuarterNote}");
                if (midiInfo.TempoBPM > 0)
                    Console.WriteLine($"  Tempo: {midiInfo.TempoBPM:F1} BPM");
            }

            // Import MIDI to ROM
            string error = SongMidiCore.ImportMidiFile(midiPath, songAddr, instrumentPtr);
            if (!string.IsNullOrEmpty(error))
            {
                Console.Error.WriteLine($"Error: {error}");
                return 1;
            }

            // Save ROM
            rom.Save(romPath, true);
            Console.WriteLine($"Song 0x{songId:X} imported from {midiPath} and saved to {romPath}");
            return 0;
        }

        static int RunCompileEvent(Dictionary<string, string> argsDic)
        {
            if (!argsDic.ContainsKey("--rom") || string.IsNullOrEmpty(argsDic["--rom"]))
            {
                Console.Error.WriteLine("Error: --compile-event requires --rom=<rom>");
                return 1;
            }
            if (!argsDic.ContainsKey("--in") || string.IsNullOrEmpty(argsDic["--in"]))
            {
                Console.Error.WriteLine("Error: --compile-event requires --in=<event_file>");
                return 1;
            }

            string romPath = argsDic["--rom"];
            string eventPath = Path.GetFullPath(argsDic["--in"]);
            string outputPath = argsDic.ContainsKey("--out") ? argsDic["--out"] : romPath;

            if (!File.Exists(eventPath))
            {
                Console.Error.WriteLine($"Error: Event file not found: {eventPath}");
                return 1;
            }

            // Resolve EA/ColorzCore executable
            RomLoader.InitEnvironment();
            string forceVersion = argsDic.ContainsKey("--force-version") ? argsDic["--force-version"] : null;
            if (!RomLoader.LoadRom(romPath, forceVersion))
                return 1;

            string eaExe = ToolPathResolver.ResolveEventAssembler();
            if (string.IsNullOrEmpty(eaExe) || !File.Exists(eaExe))
            {
                Console.Error.WriteLine("Error: Event Assembler / ColorzCore not found.");
                Console.Error.WriteLine("  Set the path via config, or build the submodule:");
                Console.Error.WriteLine("  git submodule update --init tools/Event-Assembler tools/ColorzCore");
                Console.Error.WriteLine("  dotnet build tools/ColorzCore/ColorzCore/ColorzCore.csproj -c Release");
                return 1;
            }

            bool isColorzCore = ToolPathResolver.IsColorzCore(eaExe);
            var rom = CoreState.ROM;
            string gameCode = rom.RomInfo.TitleToFilename;

            // Write current ROM to temp file for EA to modify
            string tempRomPath = Path.Combine(Path.GetTempPath(), $"febuilder_ea_{DateTime.Now.Ticks}.gba");
            File.WriteAllBytes(tempRomPath, rom.Data);

            // Generate minimal auto-def wrapper
            string wrapperContent = $"#include \"{Path.GetFileName(eventPath)}\"\n";

            string wrapperPath = Path.Combine(Path.GetDirectoryName(eventPath),
                $"_FBG_CLI_{DateTime.Now.Ticks}.event");
            File.WriteAllText(wrapperPath, wrapperContent);

            // Build EA arguments
            string symFile = Path.GetTempFileName();
            string toolDir = Path.GetDirectoryName(eaExe);

            string eaArgs;
            if (isColorzCore)
            {
                eaArgs = $"A {gameCode} " +
                    $"\"-input:{wrapperPath}\" " +
                    $"\"-output:{tempRomPath}\" " +
                    $"\"--nocash-sym:{symFile}\"";
            }
            else
            {
                eaArgs = $"A {gameCode} " +
                    $"\"-input:{wrapperPath}\" " +
                    $"\"-output:{tempRomPath}\" " +
                    $"\"-symOutput:{symFile}\"";
            }

            Console.WriteLine($"Event Assembler: {eaExe}");
            Console.WriteLine($"Game: {gameCode}");
            Console.WriteLine($"Input: {eventPath}");
            Console.WriteLine($"Compiling...");

            try
            {
                string output = RunEAProcess(eaExe, eaArgs, toolDir);

                // Check for compilation errors
                bool hasError = !IsEASuccess(output);

                // Fallback for older EA: retry without -symOutput if it's unsupported
                if (hasError && !isColorzCore &&
                    (output.Contains("symOutput doesn't exist.") || output.Contains("Unrecognized flag: symOutput")))
                {
                    Console.WriteLine("Retrying without -symOutput (older EA detected)...");
                    // Re-copy ROM since first attempt may have corrupted it
                    File.WriteAllBytes(tempRomPath, rom.Data);

                    string fallbackArgs = $"A {gameCode} " +
                        $"\"-input:{wrapperPath}\" " +
                        $"\"-output:{tempRomPath}\"";

                    output = RunEAProcess(eaExe, fallbackArgs, toolDir);
                    hasError = !IsEASuccess(output);
                }

                if (hasError)
                {
                    Console.Error.WriteLine("Compilation failed:");
                    Console.Error.WriteLine(output);
                    return 1;
                }

                // Compilation succeeded — save the modified ROM
                if (File.Exists(tempRomPath))
                {
                    File.Copy(tempRomPath, outputPath, overwrite: true);
                    Console.WriteLine($"Compilation successful.");
                    if (!string.IsNullOrEmpty(output.Trim()))
                        Console.WriteLine(output.Trim());

                    // Print symbol info if available
                    if (File.Exists(symFile))
                    {
                        string symbols = File.ReadAllText(symFile).Trim();
                        if (!string.IsNullOrEmpty(symbols))
                        {
                            int symCount = symbols.Split('\n').Length;
                            Console.WriteLine($"Symbols: {symCount} entries");
                        }
                    }

                    Console.WriteLine($"Output: {outputPath}");
                }
                else
                {
                    Console.Error.WriteLine("Error: EA did not produce output ROM.");
                    return 1;
                }
            }
            finally
            {
                // Cleanup temp files
                try { if (File.Exists(wrapperPath)) File.Delete(wrapperPath); } catch { }
                try { if (File.Exists(tempRomPath)) File.Delete(tempRomPath); } catch { }
                try { if (File.Exists(symFile)) File.Delete(symFile); } catch { }
            }

            return 0;
        }

        static string RunEAProcess(string exePath, string args, string workDir)
        {
            var psi = new System.Diagnostics.ProcessStartInfo(exePath, args)
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                WorkingDirectory = workDir,
            };

            var sb = new System.Text.StringBuilder();
            using (var proc = System.Diagnostics.Process.Start(psi))
            {
                proc.OutputDataReceived += (_, e) => { if (e.Data != null) sb.AppendLine(e.Data); };
                proc.ErrorDataReceived += (_, e) => { if (e.Data != null) sb.AppendLine(e.Data); };
                proc.BeginOutputReadLine();
                proc.BeginErrorReadLine();

                if (!proc.WaitForExit(120_000))
                {
                    proc.Kill();
                    return "Error: Event Assembler timed out after 120 seconds.";
                }
            }
            return sb.ToString();
        }

        static int RunImportBattleAnime(Dictionary<string, string> argsDic)
        {
            if (!argsDic.ContainsKey("--rom") || string.IsNullOrEmpty(argsDic["--rom"]))
            {
                Console.Error.WriteLine("Error: --import-battle-anime requires --rom=<rom>");
                return 1;
            }
            if (!argsDic.ContainsKey("--animation-id") || string.IsNullOrEmpty(argsDic["--animation-id"]))
            {
                Console.Error.WriteLine("Error: --import-battle-anime requires --animation-id=<id> (0-based)");
                return 1;
            }
            if (!argsDic.ContainsKey("--in") || string.IsNullOrEmpty(argsDic["--in"]))
            {
                Console.Error.WriteLine("Error: --import-battle-anime requires --in=<script.txt>");
                return 1;
            }

            string romPath = argsDic["--rom"];
            string scriptPath = argsDic["--in"];

            if (!uint.TryParse(argsDic["--animation-id"], out uint animId))
            {
                Console.Error.WriteLine("Error: Invalid --animation-id value (must be a decimal number).");
                return 1;
            }

            if (!File.Exists(scriptPath))
            {
                Console.Error.WriteLine($"Error: Script file not found: {scriptPath}");
                return 1;
            }

            RomLoader.InitEnvironment();
            string forceVersion = argsDic.ContainsKey("--force-version") ? argsDic["--force-version"] : null;
            if (!RomLoader.LoadRom(romPath, forceVersion))
                return 1;

            var rom = CoreState.ROM;

            // Resolve animation record address
            var (tableBase, tableEnd) = BattleAnimeImportCore.GetTableBounds(rom);
            if (tableBase == 0)
            {
                Console.Error.WriteLine("Error: Battle animation table not found in this ROM.");
                return 1;
            }
            uint animAddr = BattleAnimeImportCore.ResolveBattleAnimeAddr(rom, animId);
            if (animAddr == U.NOT_FOUND)
            {
                uint entryCount = (tableEnd - tableBase) / 32;
                Console.Error.WriteLine($"Error: Animation ID {animId} is out of range (table has {entryCount} entries, 0-{entryCount - 1}).");
                return 1;
            }

            Console.WriteLine($"ROM: {romPath}");
            Console.WriteLine($"Animation ID: {animId} (address: 0x{animAddr:X08})");
            Console.WriteLine($"Script: {scriptPath}");
            Console.WriteLine($"Importing...");

            // Image loader using SkiaSharp (dispose IImage after extracting pixels)
            Func<string, (byte[] rgba, int w, int h)?> imageLoader = (path) =>
            {
                if (!File.Exists(path)) return null;
                using var img = CoreState.ImageService?.LoadImage(path);
                if (img == null) return null;
                return (img.GetPixelData(), img.Width, img.Height);
            };

            // Detect format: check FEditor .bin header first, then fall back to extension
            string error;
            bool isFEditorBin = false;
            string ext = Path.GetExtension(scriptPath).ToUpperInvariant();
            if (ext == ".BIN" || ext == "")
            {
                // Check for FEditor serialization header
                byte[] header = new byte[8];
                using (var fs = File.OpenRead(scriptPath))
                    fs.Read(header, 0, Math.Min(8, (int)fs.Length));
                // FEditor header: 5C 78 78 75 72 or 5C 78 70
                isFEditorBin = (header[0] == 0x5C && header[1] == 0x78);
            }
            if (isFEditorBin)
            {
                error = BattleAnimeImportCore.ImportFEditorBin(
                    scriptPath, animAddr, imageLoader);
            }
            else
            {
                error = BattleAnimeImportCore.ImportBattleAnime(
                    scriptPath, animAddr, imageLoader);
            }

            if (!string.IsNullOrEmpty(error))
            {
                Console.Error.WriteLine($"Error: {error}");
                return 1;
            }

            rom.Save(romPath, true);
            Console.WriteLine($"Battle animation {animId} imported and saved to {romPath}");
            return 0;
        }

        static int RunExportBattleAnime(Dictionary<string, string> argsDic)
        {
            if (!argsDic.ContainsKey("--rom") || string.IsNullOrEmpty(argsDic["--rom"]))
            {
                Console.Error.WriteLine("Error: --export-battle-anime requires --rom=<rom>");
                return 1;
            }
            if (!argsDic.ContainsKey("--animation-id") || string.IsNullOrEmpty(argsDic["--animation-id"]))
            {
                Console.Error.WriteLine("Error: --export-battle-anime requires --animation-id=<id> (0-based)");
                return 1;
            }
            if (!argsDic.ContainsKey("--out") || string.IsNullOrEmpty(argsDic["--out"]))
            {
                Console.Error.WriteLine("Error: --export-battle-anime requires --out=<output.txt>");
                return 1;
            }

            string romPath = argsDic["--rom"];
            string outputPath = argsDic["--out"];

            if (!uint.TryParse(argsDic["--animation-id"], out uint animId))
            {
                Console.Error.WriteLine("Error: Invalid --animation-id value.");
                return 1;
            }

            RomLoader.InitEnvironment();
            string forceVersion = argsDic.ContainsKey("--force-version") ? argsDic["--force-version"] : null;
            if (!RomLoader.LoadRom(romPath, forceVersion))
                return 1;

            var rom = CoreState.ROM;
            uint animAddr = BattleAnimeImportCore.ResolveBattleAnimeAddr(rom, animId);
            if (animAddr == U.NOT_FOUND)
            {
                Console.Error.WriteLine($"Error: Animation ID {animId} is out of range.");
                return 1;
            }

            Console.WriteLine($"ROM: {romPath}");
            Console.WriteLine($"Animation ID: {animId} (address: 0x{animAddr:X08})");

            bool gifMode = argsDic.ContainsKey("--gif");

            if (gifMode)
            {
                int section = 0;
                if (argsDic.ContainsKey("--section") && !string.IsNullOrEmpty(argsDic["--section"]))
                {
                    if (!int.TryParse(argsDic["--section"], out section))
                    {
                        Console.Error.WriteLine($"Error: Invalid --section value: {argsDic["--section"]}");
                        return 1;
                    }
                }
                if (section < 0 || section > 11)
                {
                    Console.Error.WriteLine("Error: --section must be 0-11.");
                    return 1;
                }

                Console.WriteLine($"Exporting GIF (section {section})...");
                string gifError = ExportBattleAnimeGif(rom, animAddr, outputPath, section);
                if (!string.IsNullOrEmpty(gifError))
                {
                    Console.Error.WriteLine($"Error: {gifError}");
                    return 1;
                }
                Console.WriteLine($"Battle animation {animId} section {section} exported to {outputPath}");
            }
            else
            {
                Console.WriteLine($"Exporting...");
                string error = BattleAnimeExportCore.ExportBattleAnime(rom, animAddr, outputPath);
                if (!string.IsNullOrEmpty(error))
                {
                    Console.Error.WriteLine($"Error: {error}");
                    return 1;
                }
                Console.WriteLine($"Battle animation {animId} exported to {outputPath}");
            }
            return 0;
        }

        static string ExportBattleAnimeGif(ROM rom, uint animAddr, string outputPath, int section)
        {
            // Read animation record pointers
            uint sectionDataPtr = rom.u32(animAddr + 12);
            uint frameDataPtr = rom.u32(animAddr + 16);
            uint oamPtr = rom.u32(animAddr + 20);
            uint palettePtr = rom.u32(animAddr + 28);

            if (!U.isPointer(sectionDataPtr) || !U.isPointer(oamPtr) || !U.isPointer(palettePtr))
                return "Animation record contains invalid pointers.";

            uint sectionDataOff = U.toOffset(sectionDataPtr);

            // Decompress frame data
            byte[] frameData = BattleAnimeRendererCore.DecompressFrameData(rom, frameDataPtr);
            if (frameData == null || frameData.Length == 0)
                return "Failed to decompress frame data.";

            // Get section range
            BattleAnimeRendererCore.GetSectionRange(section, sectionDataOff,
                (uint)frameData.Length, rom, out uint start, out uint end);

            // Parse frames in section
            var frames = BattleAnimeRendererCore.ParseFramesInRange(frameData, start, end);
            if (frames.Count == 0)
                return $"No frames found in section {section}.";

            // Decompress OAM and palette
            uint oamOff = U.toOffset(oamPtr);
            byte[] oamData = LZ77.decompress(rom.Data, oamOff);
            if (oamData == null || oamData.Length == 0)
                return "Failed to decompress OAM data.";

            uint paletteOff = U.toOffset(palettePtr);
            byte[] paletteData = LZ77.decompress(rom.Data, paletteOff);
            if (paletteData == null || paletteData.Length == 0)
                return "Failed to decompress palette data.";

            // First 32 bytes = player team palette
            byte[] pal16 = new byte[32];
            Array.Copy(paletteData, 0, pal16, 0, Math.Min(32, paletteData.Length));

            // Render each frame
            var gifFrames = new List<GifEncoder.GifFrame>();
            foreach (var fi in frames)
            {
                // Get delay from frame data (first 2 bytes of the 0x86 command)
                uint wait = (uint)(frameData[fi.FrameDataOffset] | (frameData[fi.FrameDataOffset + 1] << 8));
                int delayCs = (int)(wait * 100 / 60); // GBA frames (60fps) → centiseconds

                using var image = BattleAnimeRendererCore.RenderSingleFrame(fi, oamData, pal16);
                if (image == null) continue;

                gifFrames.Add(new GifEncoder.GifFrame
                {
                    RgbaPixels = image.GetPixelData(),
                    Width = image.Width,
                    Height = image.Height,
                    DelayCs = Math.Max(1, delayCs),
                });
            }

            if (gifFrames.Count == 0)
                return "No frames could be rendered.";

            GifEncoder.Encode(gifFrames, outputPath);
            Console.WriteLine($"  {gifFrames.Count} frames, {frames.Count} frame commands");
            return string.Empty;
        }

        static bool IsEASuccess(string output)
        {
            return output.IndexOf("No errors or warnings.", StringComparison.Ordinal) >= 0
                || output.IndexOf("No errors. Please continue being awesome.", StringComparison.Ordinal) >= 0;
        }

        static int RunFreeSpace(Dictionary<string, string> argsDic)
        {
            if (!argsDic.ContainsKey("--rom") || string.IsNullOrEmpty(argsDic["--rom"]))
            {
                Console.Error.WriteLine("Error: --freespace requires --rom=<rom>");
                return 1;
            }

            string romPath = argsDic["--rom"];
            int minSize = 16;
            if (argsDic.ContainsKey("--min-size") && !string.IsNullOrEmpty(argsDic["--min-size"]))
            {
                if (!int.TryParse(argsDic["--min-size"], out minSize) || minSize < 1)
                {
                    Console.Error.WriteLine("Error: Invalid --min-size value.");
                    return 1;
                }
            }

            RomLoader.InitEnvironment();
            string forceVersion = argsDic.ContainsKey("--force-version") ? argsDic["--force-version"] : null;
            if (!RomLoader.LoadRom(romPath, forceVersion))
                return 1;

            var rom = CoreState.ROM;
            var data = rom.Data;
            int length = data.Length;

            Console.WriteLine($"ROM: {romPath} ({length:N0} bytes, {length / 1024}KB)");
            Console.WriteLine($"Minimum block size: {minSize} bytes");
            Console.WriteLine();

            // Scan for free space blocks (runs of 0x00 or 0xFF)
            var blocks = new List<(uint addr, int size, byte fill)>();
            int i = 0;
            while (i < length)
            {
                byte b = data[i];
                if (b == 0x00 || b == 0xFF)
                {
                    int start = i;
                    byte fill = b;
                    while (i < length && data[i] == fill)
                        i++;
                    int blockSize = i - start;
                    if (blockSize >= minSize)
                        blocks.Add(((uint)start, blockSize, fill));
                }
                else
                {
                    i++;
                }
            }

            long totalFree = 0;
            foreach (var block in blocks)
            {
                string fillStr = block.fill == 0xFF ? "0xFF" : "0x00";
                Console.WriteLine($"  0x{block.addr:X08}  {block.size,8:N0} bytes  ({fillStr})");
                totalFree += block.size;
            }

            Console.WriteLine();
            Console.WriteLine($"Free blocks: {blocks.Count} (>= {minSize} bytes)");
            Console.WriteLine($"Total free: {totalFree:N0} bytes ({totalFree * 100 / length}% of ROM)");
            return 0;
        }

        static int RunHexDump(Dictionary<string, string> argsDic)
        {
            if (!argsDic.ContainsKey("--rom") || string.IsNullOrEmpty(argsDic["--rom"]))
            {
                Console.Error.WriteLine("Error: --hex-dump requires --rom=<rom>");
                return 1;
            }
            if (!argsDic.ContainsKey("--addr") || string.IsNullOrEmpty(argsDic["--addr"]))
            {
                Console.Error.WriteLine("Error: --hex-dump requires --addr=<hex_address>");
                return 1;
            }

            string romPath = argsDic["--rom"];
            string addrStr = argsDic["--addr"].Replace("0x", "").Replace("0X", "");
            int dumpLength = 256;

            if (!uint.TryParse(addrStr, System.Globalization.NumberStyles.HexNumber, null, out uint addr))
            {
                Console.Error.WriteLine("Error: Invalid --addr hex value.");
                return 1;
            }

            if (argsDic.ContainsKey("--length") && !string.IsNullOrEmpty(argsDic["--length"]))
            {
                if (!int.TryParse(argsDic["--length"], out dumpLength) || dumpLength < 1)
                {
                    Console.Error.WriteLine("Error: Invalid --length value.");
                    return 1;
                }
            }

            RomLoader.InitEnvironment();
            string forceVersion = argsDic.ContainsKey("--force-version") ? argsDic["--force-version"] : null;
            if (!RomLoader.LoadRom(romPath, forceVersion))
                return 1;

            var rom = CoreState.ROM;
            uint offset = U.toOffset(addr);
            int end = Math.Min((int)offset + dumpLength, rom.Data.Length);

            Console.WriteLine($"ROM: {romPath}");
            Console.WriteLine($"Hex dump: 0x{offset:X08} - 0x{end - 1:X08} ({end - (int)offset} bytes)");
            Console.WriteLine();

            // Hex dump in xxd-style format: address | hex bytes | ASCII
            for (uint pos = offset; pos < end; pos += 16)
            {
                var hexPart = new System.Text.StringBuilder();
                var asciiPart = new System.Text.StringBuilder();
                int lineBytes = Math.Min(16, (int)(end - pos));

                for (int j = 0; j < 16; j++)
                {
                    if (j == 8) hexPart.Append(' ');
                    if (j < lineBytes)
                    {
                        byte b = rom.Data[pos + j];
                        hexPart.Append($"{b:X02} ");
                        asciiPart.Append(b >= 0x20 && b < 0x7F ? (char)b : '.');
                    }
                    else
                    {
                        hexPart.Append("   ");
                    }
                }

                Console.WriteLine($"  {pos:X08}  {hexPart} |{asciiPart}|");
            }

            return 0;
        }

        static int RunSearchText(Dictionary<string, string> argsDic)
        {
            if (!argsDic.ContainsKey("--rom") || string.IsNullOrEmpty(argsDic["--rom"]))
            {
                Console.Error.WriteLine("Error: --search-text requires --rom=<rom>");
                return 1;
            }
            if (!argsDic.ContainsKey("--query") || string.IsNullOrEmpty(argsDic["--query"]))
            {
                Console.Error.WriteLine("Error: --search-text requires --query=<text>");
                return 1;
            }

            string romPath = argsDic["--rom"];
            string query = argsDic["--query"];

            RomLoader.InitEnvironment();
            string forceVersion = argsDic.ContainsKey("--force-version") ? argsDic["--force-version"] : null;
            if (!RomLoader.LoadRom(romPath, forceVersion))
                return 1;
            RomLoader.InitFull();

            Console.WriteLine($"ROM: {romPath}");
            Console.WriteLine($"Loading and decoding all text entries...");

            var entries = TranslateCore.DumpTexts(CoreState.ROM);

            Console.WriteLine($"Searching {entries.Count} text entries for: \"{query}\"");
            Console.WriteLine();

            int matches = 0;
            foreach (var (textId, text) in entries)
            {
                if (string.IsNullOrEmpty(text))
                    continue;

                if (text.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    // Truncate long texts for display
                    string display = text.Replace("\n", "\\n").Replace("\r", "");
                    if (display.Length > 120)
                        display = display.Substring(0, 117) + "...";
                    Console.WriteLine($"  0x{textId:X04}  {display}");
                    matches++;
                }
            }

            Console.WriteLine();
            Console.WriteLine($"Found {matches} matches in {entries.Count} text entries.");
            return 0;
        }

        /// <summary>
        /// Parse CLI arguments into a dictionary. Supports --key=value and --flag forms.
        /// </summary>
        internal static Dictionary<string, string> ParseArgs(string[] args)
        {
            var dic = new Dictionary<string, string>();
            for (int i = 0; i < args.Length; i++)
            {
                string arg = args[i];
                if (arg.StartsWith("--"))
                {
                    int eq = arg.IndexOf('=');
                    if (eq >= 0)
                    {
                        dic[arg.Substring(0, eq)] = arg.Substring(eq + 1);
                    }
                    else
                    {
                        // Support --key value (space-separated) when the next arg is not a flag
                        if (i + 1 < args.Length && !args[i + 1].StartsWith("-"))
                        {
                            dic[arg] = args[i + 1];
                            i++;
                        }
                        else
                        {
                            dic[arg] = "";
                        }
                    }
                }
                else if (arg == "-h")
                {
                    dic["--help"] = "";
                }
            }
            return dic;
        }

        static int RunDiff(Dictionary<string, string> argsDic)
        {
            if (!argsDic.ContainsKey("--rom") || string.IsNullOrEmpty(argsDic["--rom"]))
            {
                Console.Error.WriteLine("Error: --diff requires --rom=<rom1>");
                return 1;
            }
            if (!argsDic.ContainsKey("--rom2") || string.IsNullOrEmpty(argsDic["--rom2"]))
            {
                Console.Error.WriteLine("Error: --diff requires --rom2=<rom2>");
                return 1;
            }

            string rom1Path = argsDic["--rom"];
            string rom2Path = argsDic["--rom2"];

            if (!File.Exists(rom1Path))
            {
                Console.Error.WriteLine($"Error: ROM file not found: {rom1Path}");
                return 1;
            }
            if (!File.Exists(rom2Path))
            {
                Console.Error.WriteLine($"Error: ROM file not found: {rom2Path}");
                return 1;
            }

            byte[] data1 = File.ReadAllBytes(rom1Path);
            byte[] data2 = File.ReadAllBytes(rom2Path);

            Console.WriteLine($"ROM1: {rom1Path} ({data1.Length} bytes)");
            Console.WriteLine($"ROM2: {rom2Path} ({data2.Length} bytes)");
            Console.WriteLine("Comparing...");

            var diff = RomDiffCore.Compare(data1, data2);

            if (argsDic.ContainsKey("--out") && !string.IsNullOrEmpty(argsDic["--out"]))
            {
                string outPath = argsDic["--out"];
                string tsv = RomDiffCore.FormatTSV(diff, data1, data2);
                File.WriteAllText(outPath, tsv);
                Console.WriteLine($"Diff written to: {outPath}");
            }

            Console.WriteLine(RomDiffCore.FormatSummary(diff));
            return 0;
        }

        static int RunExportMapSettings(Dictionary<string, string> argsDic)
        {
            if (!argsDic.ContainsKey("--rom") || string.IsNullOrEmpty(argsDic["--rom"]))
            {
                Console.Error.WriteLine("Error: --export-map-settings requires --rom=<rom>");
                return 1;
            }
            if (!argsDic.ContainsKey("--out") || string.IsNullOrEmpty(argsDic["--out"]))
            {
                Console.Error.WriteLine("Error: --export-map-settings requires --out=<output.tsv>");
                return 1;
            }

            string romPath = argsDic["--rom"];
            string outputPath = argsDic["--out"];

            RomLoader.InitEnvironment();
            string forceVersion = argsDic.ContainsKey("--force-version") ? argsDic["--force-version"] : null;
            if (!RomLoader.LoadRom(romPath, forceVersion)) return 1;
            RomLoader.InitFull();

            var rom = CoreState.ROM;
            if (rom?.RomInfo == null)
            {
                Console.Error.WriteLine("Error: ROM not loaded correctly.");
                return 1;
            }

            Console.WriteLine($"ROM: {romPath}");
            Console.WriteLine($"Version: {rom.RomInfo.VersionToFilename}");

            var maps = MapSettingCore.MakeMapIDList();
            if (maps.Count == 0)
            {
                Console.Error.WriteLine("Error: No maps found in ROM.");
                return 1;
            }

            uint dataSize = rom.RomInfo.map_setting_datasize;
            Console.WriteLine($"Found {maps.Count} maps (entry size: {dataSize} bytes)");

            // Export as TSV: Index, Name, then raw hex bytes of the map setting struct
            var sb = new System.Text.StringBuilder();
            sb.Append("Index\tName\tAddress");
            for (uint f = 0; f < dataSize; f += 4)
                sb.Append($"\tOffset_0x{f:X02}");
            sb.AppendLine();

            foreach (var map in maps)
            {
                string name = map.name ?? "";
                sb.Append($"0x{map.tag:X02}\t{name}\t0x{map.addr:X08}");
                for (uint f = 0; f < dataSize; f += 4)
                {
                    uint val = rom.u32(map.addr + f);
                    sb.Append($"\t0x{val:X08}");
                }
                sb.AppendLine();
            }

            File.WriteAllText(outputPath, sb.ToString(), System.Text.Encoding.UTF8);
            Console.WriteLine($"Exported {maps.Count} map settings to: {outputPath}");
            return 0;
        }
    }
}
