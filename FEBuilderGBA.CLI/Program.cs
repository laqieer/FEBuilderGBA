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
        // Raw, ORDERED argv — preserved so commands that accept REPEATABLE flags
        // (e.g. --write-source --field=X --value=Y --field=A --value=B) can recover
        // pair order, which the collapsing argsDic dictionary loses (#1141).
        internal static string[] RawArgs = Array.Empty<string>();

        static int Main(string[] args)
        {
            RawArgs = args ?? Array.Empty<string>();
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

            if (argsDic.ContainsKey("--text-refs"))
            {
                return RunTextRefs(argsDic);
            }

            if (argsDic.ContainsKey("--export-map-settings"))
            {
                return RunExportMapSettings(argsDic);
            }

            if (argsDic.ContainsKey("--diff"))
            {
                return RunDiff(argsDic);
            }

            if (argsDic.ContainsKey("--lz77"))
            {
                return RunLZ77(argsDic);
            }

            if (argsDic.ContainsKey("--checksum"))
            {
                return RunChecksum(argsDic);
            }

            if (argsDic.ContainsKey("--repair-header"))
            {
                return RunRepairHeader(argsDic);
            }

            if (argsDic.ContainsKey("--test") || argsDic.ContainsKey("--testonly"))
            {
                return RunSelfTest(argsDic);
            }

            if (argsDic.ContainsKey("--rom-info"))
            {
                return RunRomInfo(argsDic);
            }

            // --project=<dir> --resolve-addr=<hex>: resolve an address to a decomp
            // project symbol (.map/ELF/.sym/JSON merged over shipped). Must be
            // dispatched BEFORE the bare --project rom-info fallthrough (#1130).
            if (argsDic.ContainsKey("--project") && argsDic.ContainsKey("--resolve-addr"))
            {
                return RunResolveAddr(argsDic);
            }

            // --migrate-diff --project=<dir> --rom2=<editedRom> [--out=report.tsv]:
            // decomp diff-to-source migration assistant — classify changed ranges
            // (symbol/category/source/confidence) for migrating edits back to source.
            // Advisory + READ-ONLY. Must precede the bare --project fallthrough (#1131).
            if (argsDic.ContainsKey("--migrate-diff"))
            {
                return RunMigrateDiff(argsDic);
            }

            // --export-asset must precede the bare --project rom-info fallthrough (#1133)
            // — otherwise `--export-asset --project=<dir>` is swallowed by RunRomInfo
            //   and the asset is never exported.
            if (argsDic.ContainsKey("--export-asset"))
            {
                return RunExportAsset(argsDic);
            }

            // --export-voicegroup --rom=<r>|--project=<dir> (--voicegroup-addr=<hex> |
            // --song-id=<n>) --out=voicegroupNNN.s [--number=<N>]: export a FEBuilder
            // voicegroup (M4A instrument set) as reviewable decomp SOURCE macro asm
            // using asm/macros/music_voice.inc (#1362). READ-ONLY — never mutates the
            // ROM. Must precede the bare --project rom-info fallthrough so
            // --export-voicegroup --project is not swallowed by RunRomInfo.
            if (argsDic.ContainsKey("--export-voicegroup"))
            {
                return RunExportVoicegroup(argsDic);
            }

            // --export-battle-anim-decomp --rom=<r>|--project=<dir>
            // (--animation-id=<n> | --banim-addr=<hex>) --out=banim/banim_<TAG>_motion.s
            // [--number=<N>]: export a FEBuilder/FEditor-decoded battle animation as
            // reviewable decomp SOURCE (banim_<TAG>_motion.s) using the fireemblem8u
            // banim macros + .pal/.json sidecars (#1363). READ-ONLY — never mutates
            // the ROM. Must precede the bare --project rom-info fallthrough.
            if (argsDic.ContainsKey("--export-battle-anim-decomp"))
            {
                return RunExportBattleAnimDecomp(argsDic);
            }

            // --write-source --project=<dir> --table=<name> --id=<n> --field=<f> --value=<v>:
            // source-backed writer (#1132) — rewrite the owning C array element of a
            // structured table entry instead of mutating the preview ROM. Must precede
            // the bare --project rom-info fallthrough or it is swallowed by RunRomInfo.
            if (argsDic.ContainsKey("--write-source"))
            {
                return RunWriteSource(argsDic);
            }

            // --write-shop --project=<dir> (--shop-addr=0x.. | --symbol=<name>) --items=<csv>:
            // in-place source-backed shop-list writer (#1347) — rewrite the owning u16
            // ITEM_NONE-terminated C list instead of mutating the preview ROM. Must precede
            // the bare --project rom-info fallthrough or it is swallowed by RunRomInfo.
            if (argsDic.ContainsKey("--write-shop"))
            {
                return RunWriteShop(argsDic);
            }

            // --build-project --project=<dir> [--reload] [--yes] [--timeout=<ms>]:
            // run the decomp project's declared build command + optionally reload
            // the built ROM into CoreState (#1134). Must precede the bare --project
            // rom-info fallthrough so --build-project is not swallowed by RunRomInfo.
            if (argsDic.ContainsKey("--build-project"))
            {
                return RunBuildProject(argsDic);
            }

            // --decomp-audit [--format=tsv|md] [--out=path]: print the maintained decomp
            // round-trip coverage matrix (#1150). READ-ONLY, never loads a ROM. Must
            // precede the bare --project rom-info fallthrough.
            if (argsDic.ContainsKey("--decomp-audit"))
            {
                return RunDecompAudit(argsDic);
            }

            // --nmm-to-manifest --in=x.nmm [--table=name] [--out=path]: parse a No$gba
            // memory map into a manifest tables[] entry JSON (#1150). No ROM.
            if (argsDic.ContainsKey("--nmm-to-manifest"))
            {
                return RunNmmToManifest(argsDic);
            }

            // --manifest-to-nmm --project=<dir> --table=<name> [--out=path]: emit .nmm
            // text for a manifest table owner (#1150). Must precede the bare --project
            // rom-info fallthrough so it is not swallowed by RunRomInfo.
            if (argsDic.ContainsKey("--manifest-to-nmm"))
            {
                return RunManifestToNmm(argsDic);
            }

            // --validate-asset --kind=<...> --in=<src>: structurally validate a decomp
            // IMPORT asset (#1150). READ-ONLY, never loads a ROM.
            if (argsDic.ContainsKey("--validate-asset"))
            {
                return RunValidateAsset(argsDic);
            }

            // --import-asset --kind=map --in=<x.mar> --out=<x.tmap_raw.bin>: re-import a
            // .mar map LAYOUT to a raw uncompressed tilemap blob in the source tree (#1148).
            // NEVER mutates the ROM.
            if (argsDic.ContainsKey("--import-asset"))
            {
                return RunImportAsset(argsDic);
            }

            // --roundtrip-asset --kind=map|mapchange|mapanime2pal|objtiles --in=<asset>: validate +
            // prove the BODY round-trips (#1148/#1355/#1360/#1371). READ-ONLY, never loads a ROM.
            if (argsDic.ContainsKey("--roundtrip-asset"))
            {
                return RunRoundtripAsset(argsDic);
            }

            // --verify-asset --kind=mapchange|mapanime2pal|objtiles --in=<asset> --addr (+ dims/count
            // for mapchange/mapanime2pal) (+ ROM via --project/--rom): ROM-backed mismatch proof for a
            // map-change overlay (#1355), anime-2 palette (#1360), or OBJ tileset (#1371; re-decompress
            // + byte-compare). Reads the ROM READ-ONLY; never mutates it. Must precede the bare
            // --project rom-info fallthrough so --verify-asset --project is not swallowed by RunRomInfo.
            if (argsDic.ContainsKey("--verify-asset"))
            {
                return RunVerifyAsset(argsDic);
            }

            // --project=<dir> [--rom-info]: open a decomp project and report its
            // mode + resolved preview ROM. Both `--project=<dir> --rom-info` and
            // bare `--project=<dir>` route to the rom-info reporter (#1129 slice 1).
            if (argsDic.ContainsKey("--project"))
            {
                return RunRomInfo(argsDic);
            }

            if (argsDic.ContainsKey("--list-tables"))
            {
                return RunListTables(argsDic);
            }

            if (argsDic.ContainsKey("--export-palette"))
            {
                return RunExportPalette(argsDic);
            }

            if (argsDic.ContainsKey("--import-palette"))
            {
                return RunImportPalette(argsDic);
            }

            // Other commands not yet implemented
            Console.Error.WriteLine("Command not yet supported in cross-platform CLI.");
            Console.Error.WriteLine("Run with --help for usage information.");
            return 1;
        }

        static void PrintVersion()
        {
            string version = U.getAppVersion();
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
            Console.WriteLine("  --decreasecolor          Quantize image palette (requires --in, --out, --paletteno; --json for machine output)");
            Console.WriteLine("    --noScale              Do not scale colors to GBA 5-bit range");
            Console.WriteLine("    --noReserve1stColor    Do not reserve palette slot 0 for transparency");
            Console.WriteLine("    --ignoreTSA            Ignore TSA tile deduplication constraints");
            Console.WriteLine("  --pointercalc            Search pointer references (requires --rom, --target, --address)");
            Console.WriteLine("  --rebuild                Rebuild/defragment ROM (requires --rom, --fromrom)");
            Console.WriteLine("  --songexchange           Copy song between ROMs (requires --rom, --fromrom, --fromsong, --tosong)");
            Console.WriteLine("  --convertmap1picture     Convert image to map tiles (requires --in, --outImg, --outTSA; --json for machine output)");
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
            Console.WriteLine("  --text-refs              List ROM entries that reference a text ID (requires --rom, --text-id)");
            Console.WriteLine("    --text-id=<hex|dec>    Text ID to look up (e.g., 0x0213 or 531)");
            Console.WriteLine("  --list-patches           List available patches and their install status (requires --rom)");
            Console.WriteLine("    --patch-name=<name>    Filter patches by name (substring match)");
            Console.WriteLine("  --export-map-settings    Export all chapter/map settings to TSV (requires --rom, --out)");
            Console.WriteLine("  --lz77                   LZ77 compress or decompress a file (requires --in, --out, --compress or --decompress)");
            Console.WriteLine("    --compress             Compress input file");
            Console.WriteLine("    --decompress           Decompress input file");
            Console.WriteLine("  --checksum               Validate GBA ROM header checksum (requires --rom)");
            Console.WriteLine("  --repair-header          Fix GBA ROM header checksum (requires --rom)");
            Console.WriteLine("  --diff                   Compare two ROMs byte-by-byte (requires --rom, --rom2)");
            Console.WriteLine("    --rom2=<path>          Second ROM to compare against");
            Console.WriteLine("    --out=<path>           Output TSV file (omit for summary to stdout)");
            Console.WriteLine("  --testonly               Run self-test diagnostics then exit");
            Console.WriteLine("  --rom-info               Print ROM metadata: version, title, size, CRC32, checksum + Mode line (requires --rom or --project)");
            Console.WriteLine("  --project=<dir>          Open a decomp project directory and load its built ROM for preview; combine with --rom-info");
            Console.WriteLine("  --resolve-addr=<hex>     Resolve an address to a decomp project symbol (requires --project); prints name/source/offset");
            Console.WriteLine("  --migrate-diff           Decomp diff-to-source migration assistant: classify built-vs-edited ROM changes (requires --project, --rom2)");
            Console.WriteLine("    --rom2=<editedRom>     The FEBuilder-edited ROM to compare against the project's built/baseline ROM");
            Console.WriteLine("    --out=<report.tsv>     Optional: write the classified report (range/symbol/category/source/confidence) as TSV");
            Console.WriteLine("    --max-gap=<int>        Optional: small-gap merge distance for range coalescing (default 16)");
            Console.WriteLine("  --list-tables            List all exportable struct table names (no ROM required)");
            Console.WriteLine("  --export-asset           Export a ROM asset to a decomp source-tree path (requires --kind, --out, and --rom or --project)");
            Console.WriteLine("    --kind=mapchange       Raw uncompressed map-change OVERLAY tile data block (needs --addr=<change_mar hex>, --width, --height); NOT the .mar layout, NOT the record chain (#1355)");
            Console.WriteLine("    --kind=objtiles        OBJ tileset LZ77 block — exports the DECOMPRESSED 4bpp payload (needs --addr=<DEREFERENCED OBJ LZ77 stream hex>, NOT RomInfo.map_obj_pointer); FE7 obj2 is a separate stream/--addr; NOT chipset TSA/config, NOT tile animations 1/2 (#1371)");
            Console.WriteLine("    --kind=mapchipconfig   Chipset TSA/config LZ77 block — exports the DECOMPRESSED config payload (needs --addr=<DEREFERENCED config LZ77 stream hex>, NOT RomInfo.map_config_pointer); FE7 split layouts use a separate per-plist --addr; NOT the anime-1/anime-2 entry tables, NOT the map-change record chain (#1375)");
            Console.WriteLine("    --kind=<kind>          Asset kind: graphics|palette|map|mapchange|mapanime2pal|objtiles|mapchipconfig|text|shop (map data is always LZ77-decompressed; shop = EA .event migration artifact)");
            Console.WriteLine("    --out=<path>           Output path (project-relative when --project; absolute or relative when --rom)");
            Console.WriteLine("    --addr=<hex>           ROM address of the asset (required for graphics, palette, map, mapchange, mapanime2pal, objtiles, mapchipconfig)");
            Console.WriteLine("    --colors=<int>         Number of palette colors (default 16; for --kind=palette and --kind=graphics)");
            Console.WriteLine("    --bpp=<int>            Bits per pixel (default 4; 4 or 8; for --kind=graphics)");
            Console.WriteLine("    --width=<int>          Image width in pixels (required for --kind=graphics)");
            Console.WriteLine("    --height=<int>         Image height in pixels (required for --kind=graphics)");
            Console.WriteLine("    --palette-addr=<hex>   ROM address of the palette data (required for --kind=graphics)");
            Console.WriteLine("    --compressed           (graphics only) the source tile data at --addr is LZ77-compressed (flag)");
            Console.WriteLine("  --export-voicegroup      Export a voicegroup (M4A instrument set) as decomp source macro asm (voicegroupNNN.s); READ-ONLY (#1362)");
            Console.WriteLine("    --voicegroup-addr=<hex> ROM offset/pointer of the voicegroup base (exclusive with --song-id)");
            Console.WriteLine("    --song-id=<n>          Resolve the voicegroup from a song id's header (exclusive with --voicegroup-addr)");
            Console.WriteLine("    --out=<voicegroupNNN.s> Output .s path (project-relative when --project; absolute/relative when --rom)");
            Console.WriteLine("    --number=<N>           Voicegroup number used in the label/.global (default: --song-id, else 0)");
            Console.WriteLine("  --export-battle-anim-decomp  Export a battle animation as decomp source macro asm (banim_<TAG>_motion.s) + .pal/.json sidecars; READ-ONLY (#1363)");
            Console.WriteLine("    --animation-id=<n>     0-based animation index in the ROM table (exclusive with --banim-addr)");
            Console.WriteLine("    --banim-addr=<hex>     ROM offset/pointer of the 32-byte animation record (exclusive with --animation-id)");
            Console.WriteLine("    --out=<banim_<TAG>_motion.s>  Output .s path (project-relative when --project; absolute/relative when --rom)");
            Console.WriteLine("    --tag=<name>           Label tag for the emitted symbols (default: anim<NNN>)");
            Console.WriteLine("    --number=<N>           Animation number used in the default tag (default: --animation-id, else 0)");
            Console.WriteLine("  --write-source           Rewrite an owning C/JSON source element for a structured table entry (requires --project, --table, --id, --field, --value)");
            Console.WriteLine("    --project=<dir>        Decomp project directory (the table must declare a source owner in tables[])");
            Console.WriteLine("    --table=<name>         Structured table name (items, units (alias characters), classes, ...)");
            Console.WriteLine("    --id=<n>               Entry index (respecting the array order)");
            Console.WriteLine("    --field=<name>         C/JSON field name to change (must be declared on the owner; REPEATABLE — pair each --field with a following --value; other flags may appear between them; a 2nd --field before its --value, or an unpaired --field/--value, is a usage error)");
            Console.WriteLine("    --value=<int>          New value for the preceding --field (0x hex or decimal; signed fields take the two's-complement magnitude; REPEATABLE)");
            Console.WriteLine("    --out-diff=<path>      Optional: write a unified-diff-ish before/after of the changed element");
            Console.WriteLine("  --write-shop             Rewrite an owning u16 ITEM_NONE-terminated shop LIST in source (requires --project, --items, and one of --shop-addr/--symbol)");
            Console.WriteLine("    --project=<dir>        Decomp project directory (manifest must declare a u16-list list-owner for the shop's symbol)");
            Console.WriteLine("    --shop-addr=<hex>      Shop item-list ROM OFFSET; resolved to a list symbol via the project .map/.elf/.sym + manifest list-owner");
            Console.WriteLine("    --symbol=<name>        OR look up the list-owner directly by name (skips the address resolver)");
            Console.WriteLine("    --items=<csv>          New list as id:qty pairs (e.g. 0x01:5,0x02:3); id/qty hex-or-dec 0..255, id!=0; empty = emptied shop (just terminator)");
            Console.WriteLine("                           A symbolic ITEM_* source list (item-id-only; qty must be 0) is rewritten with ITEM_* macro names resolved from the constants header (owner.constantsHeader / artifacts.itemConstants / include/constants/items.h) (#1354)");
            Console.WriteLine("  --build-project          Run the decomp project's declared build command (requires --project; gated behind --yes)");
            Console.WriteLine("    --project=<dir>        Decomp project directory containing febuilder.project.json with a build section");
            Console.WriteLine("    --yes                  Required to actually execute the build command (explicit opt-in gate)");
            Console.WriteLine("    --reload               After a successful build, reload the built ROM into CoreState and print version info");
            Console.WriteLine("    --timeout=<ms>         Build timeout in milliseconds (default 600000 = 10 minutes)");
            Console.WriteLine("  --decomp-audit           Print the maintained decomp round-trip coverage matrix (no ROM; editor/table/action/coverage/notes)");
            Console.WriteLine("    --format=<tsv|md>      Output format: tsv (default) or md (GitHub markdown table)");
            Console.WriteLine("    --summary              Print the per-tier coverage SUMMARY (counts + Total + Unclassified + release-state note) instead of the table");
            Console.WriteLine("    --out=<path>           Optional: write the matrix/summary to a file (otherwise printed to stdout)");
            Console.WriteLine("  --nmm-to-manifest        Parse a No$gba memory map (.nmm) into a decomp manifest tables[] entry JSON (no ROM)");
            Console.WriteLine("    --in=<x.nmm>           Input .nmm file (the FormatNMM grammar)");
            Console.WriteLine("    --table=<name>         Table name for the emitted entry (default 'table'); unsupported fields are flagged, never dropped");
            Console.WriteLine("    --out=<path>           Optional: write the JSON to a file (otherwise printed to stdout); warnings go to stderr");
            Console.WriteLine("  --manifest-to-nmm        Emit .nmm text for a manifest table owner (requires --project, --table; no ROM mutation)");
            Console.WriteLine("    --project=<dir>        Decomp project directory whose manifest declares the table owner");
            Console.WriteLine("    --table=<name>         Table name to export to .nmm; pointer/var fields are flagged unsafe via warnings (stderr)");
            Console.WriteLine("    --out=<path>           Optional: write the .nmm to a file (otherwise printed to stdout)");
            Console.WriteLine("  --validate-asset         Structurally validate a decomp IMPORT asset on disk (no ROM; never mutates)");
            Console.WriteLine("    --kind=<kind>          Asset kind: graphics|palette|portrait|icon|map|mapchange|mapanime2pal|objtiles|mapchipconfig|portrait-package");
            Console.WriteLine("    --in=<srcAsset>        Input asset file (PNG for graphics/portrait/icon, .pal for palette, .mar for map, .change/.mapanime2pal/.objtiles/.mapchipconfig for those kinds)");
            Console.WriteLine("    --path=<dir>          For --kind=portrait-package: package DIRECTORY (one 128x112 sheet PNG + optional JASC .pal)");
            Console.WriteLine("    --allow-main-only     For --kind=portrait-package: accept a 96x80 main-mug-only sheet (warn instead of error)");
            Console.WriteLine("    --project=<dir>       For --kind=portrait-package: confine --path to the decomp project root (no ROM load)");
            Console.WriteLine("  --import-asset           Re-import a .mar map LAYOUT (or .change overlay, .mapanime2pal palette, .objtiles OBJ tileset, .mapchipconfig chipset config) to a raw blob, OR write-back a portrait PACKAGE dir, in the source tree (no ROM; never mutates)");
            Console.WriteLine("    --kind=map|mapchange|mapanime2pal|objtiles|mapchipconfig|portrait-package  map = .mar layout (+ .mar.json); mapchange = raw u16 overlay block (#1355); mapanime2pal = raw u16 anime-2 palette block (#1360); objtiles = decompressed 4bpp OBJ payload (#1371); mapchipconfig = decompressed chipset config payload (#1375); portrait-package = 128x112 sheet+sidecar identity write-back (#1374)");
            Console.WriteLine("    --in=<x.mar|x.change|x.mapanime2pal|x.objtiles|x.mapchipconfig>  Input asset file (map kinds)");
            Console.WriteLine("    --out=<x.bin|destDir>  Output raw blob (map kinds) or owner package dir (portrait-package); project-relative when --project");
            Console.WriteLine("    --path=<srcDir>       portrait-package: SOURCE package dir (read; not containment-checked)");
            Console.WriteLine("    --allow-main-only     portrait-package: accept a 96x80 main-mug-only sheet (warn not error)");
            Console.WriteLine("    --overwrite           portrait-package: replace an existing single-package owner at --out (else OWNER_EXISTS refusal)");
            Console.WriteLine("  --roundtrip-asset        Validate + prove a map LAYOUT/overlay/anime2pal/objtiles/mapchipconfig body round-trips, OR a portrait PACKAGE matches a baseline (no ROM; never mutates)");
            Console.WriteLine("    --kind=map|mapchange|mapanime2pal|objtiles|mapchipconfig|portrait-package  map = lossless u16 .mar layout; mapchange = structure-exact .change overlay (#1355); mapanime2pal = structure-exact anime-2 palette (#1360); objtiles = structure-exact .objtiles decompressed body (#1371); mapchipconfig = structure-exact .mapchipconfig decompressed body (#1375); portrait-package = byte-identical to an --expect baseline (#1374)");
            Console.WriteLine("    --in=<x.mar|x.change|x.mapanime2pal|x.objtiles|x.mapchipconfig>  Input asset file (map kinds)");
            Console.WriteLine("    --path=<srcDir> --expect=<baselineDir>  portrait-package: source dir + REQUIRED baseline oracle dir (no self-compare)");
            Console.WriteLine("  --verify-asset           ROM-backed mismatch proof for a map-change OVERLAY, anime-2 PALETTE, OBJ tileset, or chipset config (reads ROM READ-ONLY; never mutates) (#1355/#1360/#1371/#1375)");
            Console.WriteLine("    --kind=mapchange|mapanime2pal|objtiles|mapchipconfig  mapchange = byte-exact overlay compare (needs --width/--height); mapanime2pal = byte-exact palette compare (needs --count); objtiles = decompress-and-byte-compare the OBJ payload (no --width/--height/--count); mapchipconfig = decompress-and-byte-compare the chipset config payload (no --width/--height/--count)");
            Console.WriteLine("    --in=<x.change|x.mapanime2pal|x.objtiles|x.mapchipconfig>  Input overlay/anime2pal/objtiles/mapchipconfig file (+ required .json sidecar)");
            Console.WriteLine("    --addr=<hex>           mapchange: change_mar offset; mapanime2pal: anime-2 entry +0 palette ptr; objtiles: DEREFERENCED OBJ LZ77 stream address (FE7 obj2 = separate --addr); mapchipconfig: DEREFERENCED config LZ77 stream address (NOT RomInfo.map_config_pointer)");
            Console.WriteLine("    --width=<int> --height=<int>  Overlay dimensions (mapchange only; record +3/+4)");
            Console.WriteLine("    --count=<int>         Palette color count (mapanime2pal only; entry +5)");
            Console.WriteLine("    --rom=<path> | --project=<dir>  ROM source (the preview build for --project)");
            Console.WriteLine("  --export-palette         Export GBA palette to file (requires --rom, --addr, --out)");
            Console.WriteLine("    --addr=<hex>           Palette data address in ROM (e.g., 0x5524)");
            Console.WriteLine("    --colors=<int>         Number of colors to export (default: 16)");
            Console.WriteLine("    --out=<path>           Output file (.pal=JASC, .act=ACT, .gpl=GIMP, .txt=HexText)");
            Console.WriteLine("  --import-palette         Import palette file into ROM (requires --rom, --addr, --in)");
            Console.WriteLine("    --addr=<hex>           Palette data address in ROM");
            Console.WriteLine("    --in=<path>            Input palette file (format auto-detected from content/extension)");
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
            Console.WriteLine("  FEBuilderGBA.CLI --migrate-diff --project=decomp/ --rom2=edited.gba --out=migrate.tsv");
            Console.WriteLine("  FEBuilderGBA.CLI --convertmap1picture --in=map.png --outImg=tiles.bin --outTSA=tsa.bin");
            Console.WriteLine("  FEBuilderGBA.CLI --translate --rom=rom.gba --out=texts.tsv");
            Console.WriteLine("  FEBuilderGBA.CLI --translate --rom=rom.gba --in=texts.tsv");
            Console.WriteLine("  FEBuilderGBA.CLI --translate-roundtrip --rom=rom.gba");
            Console.WriteLine("  FEBuilderGBA.CLI --translate-roundtrip --rom=rom.gba --out=diff");
            Console.WriteLine("  FEBuilderGBA.CLI --export-asset --kind=palette --rom=rom.gba --addr=0x5524 --out=gfx/palette.pal");
            Console.WriteLine("  FEBuilderGBA.CLI --export-asset --kind=graphics --project=decomp/ --addr=0x123000 --width=64 --height=64 --palette-addr=0x124000 --out=gfx/tiles.png");
            Console.WriteLine("  FEBuilderGBA.CLI --export-asset --kind=map --rom=rom.gba --addr=0x200000 --out=map/chapter1.mar");
            Console.WriteLine("  FEBuilderGBA.CLI --export-asset --kind=mapchange --rom=rom.gba --addr=0x300000 --width=15 --height=10 --out=map/chapter1.change");
            Console.WriteLine("  FEBuilderGBA.CLI --export-asset --kind=objtiles --rom=rom.gba --addr=0x400000 --out=map/chapter1.objtiles");
            Console.WriteLine("  FEBuilderGBA.CLI --export-asset --kind=mapchipconfig --rom=rom.gba --addr=0x500000 --out=map/chapter1.mapchipconfig");
            Console.WriteLine("  FEBuilderGBA.CLI --export-asset --kind=text --rom=rom.gba --out=text/");
            Console.WriteLine("  FEBuilderGBA.CLI --export-asset --kind=shop --rom=rom.gba --out=shops/");
            Console.WriteLine("  FEBuilderGBA.CLI --decomp-audit --format=md --out=docs/decomp-coverage.md");
            Console.WriteLine("  FEBuilderGBA.CLI --decomp-audit --summary");
            Console.WriteLine("  FEBuilderGBA.CLI --nmm-to-manifest --in=items.nmm --table=items --out=items.tables.json");
            Console.WriteLine("  FEBuilderGBA.CLI --manifest-to-nmm --project=decomp/ --table=items --out=items.nmm");
            Console.WriteLine("  FEBuilderGBA.CLI --validate-asset --kind=graphics --in=gfx/tiles.png");
            Console.WriteLine("  FEBuilderGBA.CLI --validate-asset --kind=palette --in=gfx/palette.pal");
            Console.WriteLine("  FEBuilderGBA.CLI --validate-asset --kind=portrait-package --path portraits/eirika/");
            Console.WriteLine("  FEBuilderGBA.CLI --import-asset --kind=map --in=map/chapter1.mar --out=map/chapter1.tmap_raw.bin");
            Console.WriteLine("  FEBuilderGBA.CLI --import-asset --kind=mapchange --in=map/chapter1.change --out=map/chapter1.change_raw.bin");
            Console.WriteLine("  FEBuilderGBA.CLI --import-asset --kind=objtiles --in=map/chapter1.objtiles --out=map/chapter1.objtiles_raw.bin");
            Console.WriteLine("  FEBuilderGBA.CLI --import-asset --kind=mapchipconfig --in=map/chapter1.mapchipconfig --out=map/chapter1.mapchipconfig_raw.bin");
            Console.WriteLine("  FEBuilderGBA.CLI --import-asset --kind=portrait-package --path portraits/src/eirika/ --out portraits/eirika/ --project=decomp/");
            Console.WriteLine("  FEBuilderGBA.CLI --roundtrip-asset --kind=map --in=map/chapter1.mar");
            Console.WriteLine("  FEBuilderGBA.CLI --roundtrip-asset --kind=mapchange --in=map/chapter1.change");
            Console.WriteLine("  FEBuilderGBA.CLI --roundtrip-asset --kind=objtiles --in=map/chapter1.objtiles");
            Console.WriteLine("  FEBuilderGBA.CLI --roundtrip-asset --kind=mapchipconfig --in=map/chapter1.mapchipconfig");
            Console.WriteLine("  FEBuilderGBA.CLI --roundtrip-asset --kind=portrait-package --path portraits/src/eirika/ --expect portraits/eirika/");
            Console.WriteLine("  FEBuilderGBA.CLI --verify-asset --kind=mapchange --rom=rom.gba --addr=0x300000 --width=15 --height=10 --in=map/chapter1.change");
            Console.WriteLine("  FEBuilderGBA.CLI --verify-asset --kind=objtiles --rom=rom.gba --addr=0x400000 --in=map/chapter1.objtiles");
            Console.WriteLine("  FEBuilderGBA.CLI --verify-asset --kind=mapchipconfig --rom=rom.gba --addr=0x500000 --in=map/chapter1.mapchipconfig");
            Console.WriteLine("  FEBuilderGBA.CLI --write-source --project=decomp/ --table=items --id=1 --field=might --value=0x0A");
            Console.WriteLine("  FEBuilderGBA.CLI --write-source --project=decomp/ --table=units --id=1 --field=hp --value=18 --field=pow --value=7");
            Console.WriteLine("  FEBuilderGBA.CLI --write-source --project=decomp/ --table=support_units --id=0 --field=b0 --value=6 --field=b1 --value=3   (leading prefix — safe with minimal fields[])");
            Console.WriteLine("  FEBuilderGBA.CLI --write-source --project=decomp/ --table=support_attributes --id=1 --field=b1 --value=2   (assumes owner declares b0..b1 or uses .bN= initializers)");
            Console.WriteLine("  FEBuilderGBA.CLI --write-source --project=decomp/ --table=support_talks --id=0 --field=w4 --value=0x0A12   (assumes owner declares the ordered prefix up to w4 or uses .bN= initializers)");
            Console.WriteLine("  FEBuilderGBA.CLI --write-shop --project=decomp/ --symbol=ItemList_WM_FluornArmory --items=0x01:5,0x02:3");
            Console.WriteLine("  FEBuilderGBA.CLI --write-shop --project=decomp/ --shop-addr=0xB2A18 --items=0x16:1   (resolves the shop symbol from the project .map/.elf/.sym)");
            Console.WriteLine("  FEBuilderGBA.CLI --write-shop --project=decomp/ --symbol=ItemList_WM_Ide_Armory --items=0x01:0,0x14:0   (symbolic ITEM_* item-id-only list; qty must be 0)");
            Console.WriteLine("  FEBuilderGBA.CLI --build-project --project=decomp/ --reload --yes");
            Console.WriteLine("  FEBuilderGBA.CLI --export-data --rom=rom.gba --table=units --out=units.tsv");
            Console.WriteLine("  FEBuilderGBA.CLI --export-data --rom=rom.gba --table=all --out=data");
            Console.WriteLine("  FEBuilderGBA.CLI --export-data --rom=rom.gba --table=support_units --out=support_units.tsv");
            Console.WriteLine("  FEBuilderGBA.CLI --export-data --rom=rom.gba --table=support_attributes --out=support_attrs.tsv");
            Console.WriteLine("  FEBuilderGBA.CLI --export-data --rom=rom.gba --table=support_talks --out=support_talks.tsv");
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
            bool json = argsDic.ContainsKey("--json");
            int Fail(string msg)
            {
                if (json)
                    Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(
                        new Dictionary<string, object> { ["command"] = "decreasecolor", ["ok"] = false, ["error"] = msg }));
                else
                    Console.Error.WriteLine("Error: " + msg);
                return 1;
            }

            if (!argsDic.ContainsKey("--in") || string.IsNullOrEmpty(argsDic["--in"]))
                return Fail("--decreasecolor requires --in=<input_image>");
            if (!argsDic.ContainsKey("--out") || string.IsNullOrEmpty(argsDic["--out"]))
                return Fail("--decreasecolor requires --out=<output_image>");

            string inputPath = argsDic["--in"];
            string outputPath = argsDic["--out"];
            int maxColors = 16;
            if (argsDic.ContainsKey("--paletteno") && !string.IsNullOrEmpty(argsDic["--paletteno"]))
                int.TryParse(argsDic["--paletteno"], out maxColors);

            // Parse optional flags
            bool noScale = argsDic.ContainsKey("--noScale");
            bool noReserve1stColor = argsDic.ContainsKey("--noReserve1stColor");
            bool ignoreTSA = argsDic.ContainsKey("--ignoreTSA");

            if (!json)
            {
                if (noScale)
                    Console.WriteLine("  Flag: --noScale (color scaling disabled)");
                if (noReserve1stColor)
                    Console.WriteLine("  Flag: --noReserve1stColor (palette slot 0 not reserved for transparency)");
                if (ignoreTSA)
                    Console.WriteLine("  Flag: --ignoreTSA (TSA tile constraints ignored)");
            }

            if (!File.Exists(inputPath))
                return Fail($"Input file not found: {inputPath}");

            // Load image via IImageService
            var imgService = CoreState.ImageService;
            if (imgService == null)
                return Fail("Image service not available.");

            var image = imgService.LoadImage(inputPath);
            if (image == null)
                return Fail($"Failed to load image: {inputPath}");

            byte[] rgba = image.GetPixelData();
            int width = image.Width;
            int height = image.Height;

            var result = DecreaseColorCore.Quantize(rgba, width, height, maxColors, noScale, noReserve1stColor, ignoreTSA);
            if (result == null)
                return Fail("Quantization failed.");

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

            long outBytes = File.Exists(outputPath) ? new FileInfo(outputPath).Length : 0;
            if (json)
            {
                Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(new Dictionary<string, object>
                {
                    ["command"] = "decreasecolor",
                    ["ok"] = true,
                    ["in"] = inputPath,
                    ["out"] = outputPath,
                    ["outBytes"] = outBytes,
                    ["paletteNo"] = maxColors,
                    ["colors"] = result.ColorCount,
                    ["width"] = width,
                    ["height"] = height,
                }));
            }
            else
            {
                Console.WriteLine($"Color reduction complete: {outputPath}");
                Console.WriteLine($"  Input: {inputPath} ({width}x{height})");
                Console.WriteLine($"  Colors: {result.ColorCount} (max {maxColors})");
            }
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
            // Load the DESTINATION ROM into CoreState.ROM — that is the ROM we
            // mutate, and the undo snapshots (Undo.UndoPostion) read CoreState.ROM.
            if (!RomLoader.LoadRom(destPath, forceVersion))
                return 1;

            // Read the SOURCE ROM bytes (the donor); the dest table offsets are
            // resolved against CoreState.ROM.Data so the slot we repoint matches
            // the ROM we Save below.
            byte[] sourceData = File.ReadAllBytes(sourcePath);
            byte[] destData = CoreState.ROM.Data;

            uint soundTablePtr = CoreState.ROM.RomInfo.sound_table_pointer;
            // DEST = the loaded ROM, so use its known sound_table_pointer.
            uint destTableAddr = SongExchangeCore.FindSongTablePointer(destData, soundTablePtr);
            // DONOR may be a DIFFERENT version, so pattern-scan its OWN sound-engine
            // signature (WF SongUtil.FindSongTablePointer(byte[])) instead of assuming
            // the dest's sound_table_pointer address. Fall back to the dest's pointer
            // address if the scan fails (donor==dest version).
            uint srcTableAddr = SongExchangeCore.FindSongTablePointerByScan(sourceData);
            if (srcTableAddr == 0)
            {
                srcTableAddr = SongExchangeCore.FindSongTablePointer(sourceData, soundTablePtr);
            }

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
            if (toSongId == 0)
            {
                Console.Error.WriteLine("Error: Cannot write to SongID 0x0.");
                return 1;
            }

            // Real cross-ROM transplant: build the InstrumentMap, Rip every track,
            // and Burn into the destination ROM under a single undo scope.
            if (CoreState.Undo == null) CoreState.Undo = new Undo();
            Undo.UndoData undo = CoreState.Undo.NewUndoData("SongExchange");
            SongExchangeCore.ConvertResult conv;
            using (ROM.BeginUndoScope(undo))
            {
                conv = SongExchangeCore.ConvertSong(CoreState.ROM, destSongs[(int)toSongId],
                                                    sourceData, srcSongs[(int)fromSongId], undo);
            }

            if (!conv.Success)
            {
                Console.Error.WriteLine($"Error: Song conversion failed. {conv.ErrorMessage}");
                return 1;
            }
            if (conv.HadStructureWarning)
            {
                Console.WriteLine("Warning: the source song's instrument data was partially corrupt; only recognized tracks were transplanted.");
            }

            CoreState.Undo.Push(undo);
            CoreState.ROM.Save(destPath, true);
            Console.WriteLine($"Song exchange complete: song 0x{fromSongId:X} from {sourcePath} -> song 0x{toSongId:X} in {destPath}");
            return 0;
        }

        static int RunConvertMap1Picture(Dictionary<string, string> argsDic)
        {
            bool json = argsDic.ContainsKey("--json");
            int Fail(string msg)
            {
                if (json)
                    Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(
                        new Dictionary<string, object> { ["command"] = "convertmap1picture", ["ok"] = false, ["error"] = msg }));
                else
                    Console.Error.WriteLine("Error: " + msg);
                return 1;
            }

            if (!argsDic.ContainsKey("--in") || string.IsNullOrEmpty(argsDic["--in"]))
                return Fail("--convertmap1picture requires --in=<input_image>");

            string inputPath = argsDic["--in"];
            if (!File.Exists(inputPath))
                return Fail($"Input file not found: {inputPath}");

            string outImgPath = argsDic.ContainsKey("--outImg") ? argsDic["--outImg"] : "";
            string outTSAPath = argsDic.ContainsKey("--outTSA") ? argsDic["--outTSA"] : "";
            if (string.IsNullOrEmpty(outImgPath) && string.IsNullOrEmpty(outTSAPath))
                return Fail("--convertmap1picture requires --outImg=<path> and/or --outTSA=<path>");

            var imgService = CoreState.ImageService;
            if (imgService == null)
                return Fail("Image service not available.");

            var image = imgService.LoadImage(inputPath);
            if (image == null)
                return Fail($"Failed to load image: {inputPath}");

            byte[] rgba = image.GetPixelData();
            int width = image.Width;
            int height = image.Height;

            var result = MapConvertCore.ConvertImage(rgba, width, height);
            if (result == null)
                return Fail("Map conversion failed. Image dimensions must be multiples of 8.");

            if (!string.IsNullOrEmpty(outImgPath))
                File.WriteAllBytes(outImgPath, result.TileData);
            long outTSABytes = 0;
            if (!string.IsNullOrEmpty(outTSAPath))
            {
                byte[] compressedTSA = LZ77.compress(result.TSAData);
                File.WriteAllBytes(outTSAPath, compressedTSA);
                outTSABytes = compressedTSA.Length;
            }

            if (json)
            {
                bool haveImg = !string.IsNullOrEmpty(outImgPath);
                bool haveTSA = !string.IsNullOrEmpty(outTSAPath);
                Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(new Dictionary<string, object>
                {
                    ["command"] = "convertmap1picture",
                    ["ok"] = true,
                    ["in"] = inputPath,
                    ["outImg"] = haveImg ? outImgPath : null,
                    ["outTSA"] = haveTSA ? outTSAPath : null,
                    ["outImgBytes"] = haveImg ? (object)result.TileData.Length : null,
                    ["outTSABytes"] = haveTSA ? (object)outTSABytes : null,
                    ["tiles"] = result.TileCount,
                    ["gridWidth"] = result.WidthTiles,
                    ["gridHeight"] = result.HeightTiles,
                }));
            }
            else
            {
                Console.WriteLine($"Map conversion complete:");
                Console.WriteLine($"  Input: {inputPath} ({width}x{height})");
                Console.WriteLine($"  Tiles: {result.TileCount} unique ({result.WidthTiles}x{result.HeightTiles} grid)");
                if (!string.IsNullOrEmpty(outImgPath))
                    Console.WriteLine($"  Tile data: {outImgPath} ({result.TileData.Length} bytes)");
                if (!string.IsNullOrEmpty(outTSAPath))
                    Console.WriteLine($"  TSA data: {outTSAPath}");
            }
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

            string err = ImportPortraitFromFile(rom, inputPath, portraitAddr);
            if (err != null)
            { Console.Error.WriteLine($"Error: {err}"); return 1; }

            rom.Save(romPath, true);
            Console.WriteLine($"Portrait {portraitId} imported from {inputPath} and saved to {romPath}");
            return 0;
        }

        /// <summary>
        /// Shared portrait import pipeline: load PNG, quantize, encode 4bpp tiles, write to ROM.
        /// Returns null on success, error message string on failure.
        /// </summary>
        static string ImportPortraitFromFile(ROM rom, string pngPath, uint portraitAddr)
        {
            using var skBitmap = global::SkiaSharp.SKBitmap.Decode(pngPath);
            if (skBitmap == null) return "Failed to decode image.";

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
            if (quantResult == null) return "Color quantization failed.";

            byte[] tileData = ImageImportCore.EncodeDirectTiles4bpp(quantResult.IndexData, width, height);
            if (tileData == null) return "Tile encoding failed.";

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
            if (tileAddr == U.NOT_FOUND) return "No free ROM space for tile data.";

            uint palAddr = ImageImportCore.WritePaletteToROM(rom, quantResult.GBAPalette, portraitAddr + 8);
            if (palAddr == U.NOT_FOUND) return "No free ROM space for palette.";

            return null; // success
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

                string err = ImportPortraitFromFile(rom, pngPath, portraitAddr);
                if (err != null)
                { Console.Error.WriteLine($"  Skip {name}.png: {err}"); failed++; continue; }

                Console.WriteLine($"  Imported portrait {portraitId} from {name}.png");
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

            return failed > 0 ? 1 : 0;
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
                    bool isMusic = label.StartsWith("FE-Repo-Music");
                    // Use the EXPLICIT per-repo submodule path, not a bare
                    // `resources/` (which is not a registered submodule and would
                    // fail) (#1669 review). Source clone: init the submodule.
                    // Released build (no git repo / no scripts/ folder):
                    // shallow-clone the public repo into the expected folder (#1644).
                    string submodulePath = isMusic
                        ? "resources/FE-Repo-Music-No-Preview"
                        : "resources/FE-Repo";
                    Console.WriteLine($"{label}: not found at {repoPath}");
                    Console.WriteLine("  Source build: git submodule update --init " + submodulePath);
                    Console.WriteLine("  Released build: " + (isMusic
                        ? FERepoResourceBrowser.MusicCloneCommand
                        : FERepoResourceBrowser.GraphicsCloneCommand));
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

            var rom = CoreState.ROM;
            string gameCode = rom.RomInfo.TitleToFilename;

            Console.WriteLine($"Event Assembler: {eaExe}");
            Console.WriteLine($"Game: {gameCode}");
            Console.WriteLine($"Input: {eventPath}");
            Console.WriteLine($"Compiling...");

            // Shared compile+insert flow (also used by the Avalonia tool). The CLI
            // keeps FreeAreaMode.None (the original plain "#include" wrapper, no
            // FreeSpace define) and does not need undo — pass a throwaway UndoData.
            if (CoreState.Undo == null) CoreState.Undo = new Undo();
            Undo.UndoData undo = CoreState.Undo.NewUndoData("compile-event");

            var result = EventAssemblerCompileCore.CompileAndInsert(
                rom, eventPath,
                EventAssemblerCompileCore.FreeAreaMode.None,
                undo,
                SymbolUtil.DebugSymbol.None,
                onRetry: Console.WriteLine);

            if (!result.Success)
            {
                Console.Error.WriteLine("Compilation failed:");
                Console.Error.WriteLine(string.IsNullOrEmpty(result.Output) ? result.ErrorMessage : result.Output);
                return 1;
            }

            // Compilation succeeded — save the modified ROM to the requested output.
            rom.Save(outputPath, true);
            Console.WriteLine($"Compilation successful.");
            if (!string.IsNullOrEmpty(result.Output.Trim()))
                Console.WriteLine(result.Output.Trim());

            if (result.SymbolCount > 0)
                Console.WriteLine($"Symbols: {result.SymbolCount} entries");

            Console.WriteLine($"Output: {outputPath}");

            return 0;
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
            var gifFrames = new List<GifEncoderCore.GifFrame>();
            foreach (var fi in frames)
            {
                // Get delay from frame data (first 2 bytes of the 0x86 command).
                // Use the Math.Round-based U.GameFrameSecToGifFrameSec helper
                // (Core port of WinForms #499) so all Core consumers share
                // identical rounding semantics.
                uint wait = (uint)(frameData[fi.FrameDataOffset] | (frameData[fi.FrameDataOffset + 1] << 8));
                int delayCs = U.GameFrameSecToGifFrameSec(wait);

                using var image = BattleAnimeRendererCore.RenderSingleFrame(fi, oamData, pal16);
                if (image == null) continue;

                gifFrames.Add(new GifEncoderCore.GifFrame
                {
                    RgbaPixels = image.GetPixelData(),
                    Width = image.Width,
                    Height = image.Height,
                    DelayCs = Math.Max(1, delayCs),
                });
            }

            if (gifFrames.Count == 0)
                return "No frames could be rendered.";

            GifEncoderCore.Encode(gifFrames, outputPath);
            Console.WriteLine($"  {gifFrames.Count} frames, {frames.Count} frame commands");
            return string.Empty;
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
        /// Lists every ROM entry that references the given text ID. Uses
        /// <see cref="TextRefTableRegistry"/> (the same registry that drives
        /// the Avalonia Text Editor cross-references panel — issue #349).
        /// </summary>
        static int RunTextRefs(Dictionary<string, string> argsDic)
        {
            if (!argsDic.ContainsKey("--rom") || string.IsNullOrEmpty(argsDic["--rom"]))
            {
                Console.Error.WriteLine("Error: --text-refs requires --rom=<rom>");
                return 1;
            }
            if (!argsDic.ContainsKey("--text-id") || string.IsNullOrEmpty(argsDic["--text-id"]))
            {
                Console.Error.WriteLine("Error: --text-refs requires --text-id=<hex or dec text id>");
                return 1;
            }

            string romPath = argsDic["--rom"];
            string idStr = argsDic["--text-id"];
            uint textId;
            try
            {
                if (idStr.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                    textId = Convert.ToUInt32(idStr.Substring(2), 16);
                else
                    textId = Convert.ToUInt32(idStr);
            }
            catch
            {
                Console.Error.WriteLine($"Error: invalid --text-id value: {idStr}");
                return 1;
            }

            RomLoader.InitEnvironment();
            string forceVersion = argsDic.ContainsKey("--force-version") ? argsDic["--force-version"] : null;
            if (!RomLoader.LoadRom(romPath, forceVersion))
                return 1;
            RomLoader.InitFull();

            var rom = CoreState.ROM;
            Console.WriteLine($"ROM:     {romPath}");
            Console.WriteLine($"Version: {(rom?.RomInfo != null ? "FE" + rom.RomInfo.version + (rom.RomInfo.is_multibyte ? "J" : "U") : "unknown")}");
            Console.WriteLine($"Text ID: 0x{textId:X04}");

            // Show decoded text content for context. Use NameResolver.GetTextById
            // which is the public stripped-codes entry point.
            try
            {
                string decoded = NameResolver.GetTextById(textId) ?? "";
                if (decoded.Length > 80) decoded = decoded.Substring(0, 77) + "...";
                Console.WriteLine($"Decoded: {decoded}");
            }
            catch { }

            Console.WriteLine();
            Console.WriteLine("Cross-references (via TextRefTableRegistry — #349):");

            var tables = TextRefTableRegistry.BuildForRom(rom);
            Console.WriteLine($"  (scanning {tables.Count} table descriptors)");
            var refs = TextReferenceFinder.Find(rom, textId, tables);
            if (refs.Count == 0)
            {
                Console.WriteLine("  (none — text ID is not referenced by any scanned table)");
            }
            else
            {
                foreach (var r in refs)
                    Console.WriteLine($"  - {r}");
            }
            Console.WriteLine();
            Console.WriteLine($"Total: {refs.Count} reference(s).");
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

        static int RunLZ77(Dictionary<string, string> argsDic)
        {
            if (!argsDic.ContainsKey("--in") || string.IsNullOrEmpty(argsDic["--in"]))
            { Console.Error.WriteLine("Error: --lz77 requires --in=<input_file>"); return 1; }
            if (!argsDic.ContainsKey("--out") || string.IsNullOrEmpty(argsDic["--out"]))
            { Console.Error.WriteLine("Error: --lz77 requires --out=<output_file>"); return 1; }

            bool compress = argsDic.ContainsKey("--compress");
            bool decompress = argsDic.ContainsKey("--decompress");
            if (compress && decompress)
            { Console.Error.WriteLine("Error: --lz77 cannot use both --compress and --decompress"); return 1; }
            if (!compress && !decompress)
            { Console.Error.WriteLine("Error: --lz77 requires --compress or --decompress"); return 1; }

            string inPath = argsDic["--in"];
            string outPath = argsDic["--out"];

            if (!File.Exists(inPath))
            { Console.Error.WriteLine($"Error: Input file not found: {inPath}"); return 1; }

            byte[] input = File.ReadAllBytes(inPath);
            Console.WriteLine($"Input: {inPath} ({input.Length} bytes)");

            if (compress)
            {
                if (input.Length < 3)
                { Console.Error.WriteLine("Error: Input too small for LZ77 compression (minimum 3 bytes)."); return 1; }
                byte[] result = LZ77.compress(input);
                File.WriteAllBytes(outPath, result);
                int ratio = input.Length > 0 ? (int)(100L * result.Length / input.Length) : 0;
                Console.WriteLine($"Compressed: {outPath} ({result.Length} bytes, {ratio}%)");
            }
            else
            {
                if (!LZ77.iscompress(input, 0))
                { Console.Error.WriteLine("Error: Input is not valid LZ77 compressed data."); return 1; }
                byte[] result = LZ77.decompress(input, 0);
                if (result == null || result.Length == 0)
                { Console.Error.WriteLine("Error: LZ77 decompression failed."); return 1; }
                File.WriteAllBytes(outPath, result);
                Console.WriteLine($"Decompressed: {outPath} ({result.Length} bytes)");
            }
            return 0;
        }

        static int RunChecksum(Dictionary<string, string> argsDic)
        {
            if (!argsDic.ContainsKey("--rom") || string.IsNullOrEmpty(argsDic["--rom"]))
            { Console.Error.WriteLine("Error: --checksum requires --rom=<rom>"); return 1; }

            string romPath = argsDic["--rom"];
            if (!File.Exists(romPath))
            { Console.Error.WriteLine($"Error: ROM not found: {romPath}"); return 1; }

            byte[] data = File.ReadAllBytes(romPath);
            if (data.Length < 0xC0)
            { Console.Error.WriteLine("Error: File too small to be a GBA ROM."); return 1; }

            // GBA header checksum: complement of sum of bytes 0xA0-0xBC, stored at 0xBD
            int sum = 0;
            for (int i = 0xA0; i < 0xBD; i++)
                sum += data[i];
            byte expected = (byte)(-(0x19 + sum));
            byte actual = data[0xBD];

            Console.WriteLine($"ROM: {romPath} ({data.Length} bytes)");
            Console.WriteLine($"Title: {System.Text.Encoding.ASCII.GetString(data, 0xA0, 12).TrimEnd('\0')}");
            Console.WriteLine($"Game Code: {System.Text.Encoding.ASCII.GetString(data, 0xAC, 4)}");
            Console.WriteLine($"Header checksum: 0x{actual:X02} (expected: 0x{expected:X02})");

            if (actual == expected)
            {
                Console.WriteLine("Status: VALID");
                return 0;
            }
            else
            {
                Console.WriteLine("Status: INVALID (use --repair-header to fix)");
                return 2;
            }
        }

        static int RunRepairHeader(Dictionary<string, string> argsDic)
        {
            if (!argsDic.ContainsKey("--rom") || string.IsNullOrEmpty(argsDic["--rom"]))
            { Console.Error.WriteLine("Error: --repair-header requires --rom=<rom>"); return 1; }

            string romPath = argsDic["--rom"];
            if (!File.Exists(romPath))
            { Console.Error.WriteLine($"Error: ROM not found: {romPath}"); return 1; }

            byte[] data = File.ReadAllBytes(romPath);
            if (data.Length < 0xC0)
            { Console.Error.WriteLine("Error: File too small to be a GBA ROM."); return 1; }

            int sum = 0;
            for (int i = 0xA0; i < 0xBD; i++)
                sum += data[i];
            byte correct = (byte)(-(0x19 + sum));
            byte current = data[0xBD];

            if (current == correct)
            {
                Console.WriteLine($"Header checksum already valid (0x{current:X02}). No repair needed.");
                return 0;
            }

            data[0xBD] = correct;
            File.WriteAllBytes(romPath, data);
            Console.WriteLine($"Repaired header checksum: 0x{current:X02} -> 0x{correct:X02}");
            Console.WriteLine($"Saved: {romPath}");
            return 0;
        }

        /// <summary>
        /// Run the decomp project's declared build command and optionally reload the
        /// built ROM into CoreState (#1134). Never throws; always returns an exit code.
        ///
        /// Security: the build command is NEVER auto-run. It requires ALL of:
        ///   (1) --project=&lt;dir&gt; pointing at a manifest that opts in via a build section,
        ///   (2) --yes (explicit confirm flag),
        ///   (3) The manifest build section itself (BuildEnabled==true).
        /// Without --yes the command line is printed and the process exits 0 (dry-run).
        /// </summary>
        static int RunBuildProject(Dictionary<string, string> argsDic)
        {
            try
            {
                // Require --project
                if (!argsDic.ContainsKey("--project") || string.IsNullOrEmpty(argsDic["--project"]))
                {
                    Console.Error.WriteLine("Error: --build-project requires --project=<dir>");
                    return 1;
                }
                string dir = argsDic["--project"];

                RomLoader.InitEnvironment();

                var project = DecompProjectDetector.Detect(dir);
                if (project == null)
                {
                    Console.Error.WriteLine($"Error: Not a decomp project directory: {dir}");
                    return 1;
                }

                // Parse timeout
                int timeout = ProcessRunnerCore.DefaultTimeoutMs;
                if (argsDic.ContainsKey("--timeout"))
                    TryParseIntArg(argsDic["--timeout"], out timeout);

                string cmdLine = DecompBuildCore.GetEffectiveCommandLine(project);
                if (string.IsNullOrEmpty(cmdLine))
                {
                    Console.WriteLine("Project has not opted into FEBuilder-managed builds; add a build section to febuilder.project.json.");
                    Console.WriteLine("Example: { \"schemaVersion\": 1, \"builtRom\": \"out.gba\", \"build\": \"make\" }");
                    return 2;
                }
                Console.WriteLine($"Command: {cmdLine}");

                // Build opt-in check
                if (!project.IsBuildEnabled)
                {
                    Console.WriteLine("Project has not opted into FEBuilder-managed builds; add a build section to febuilder.project.json.");
                    return 2;
                }

                // Dry-run gate
                if (!argsDic.ContainsKey("--yes"))
                {
                    Console.WriteLine($"This will execute the above command in: {project.ProjectRoot}");
                    Console.WriteLine("Re-run with --yes to execute.");
                    return 0;
                }

                // Execute the build
                var res = DecompBuildCore.Build(project, timeout);

                // Always print captured output
                if (!string.IsNullOrEmpty(res.Run.Stdout))
                {
                    Console.WriteLine("--- stdout ---");
                    Console.Write(res.Run.Stdout);
                }
                if (!string.IsNullOrEmpty(res.Run.Stderr))
                {
                    Console.Error.WriteLine("--- stderr ---");
                    Console.Error.Write(res.Run.Stderr);
                }

                if (res.Run.Started)
                    Console.WriteLine($"Exit: {res.Run.ExitCode}");

                if (!res.Success)
                {
                    Console.Error.WriteLine($"Build failed: {res.Message}");
                    return 1;
                }

                // Build succeeded
                bool doReload = argsDic.ContainsKey("--reload");
                if (doReload)
                {
                    CoreState.DecompProject = project;
                    var reloadStatus = DecompBuildCore.ReloadBuiltRom(
                        project,
                        (p, fv) => RomLoader.LoadRom(p, fv));

                    if (reloadStatus != DecompResolveStatus.Ok)
                    {
                        Console.Error.WriteLine("Error: no built ROM after build (resolve failed).");
                        return 1;
                    }

                    // Full init for symbol re-parse
                    RomLoader.InitFull();

                    string version = CoreState.ROM?.RomInfo?.VersionToFilename ?? "unknown";
                    Console.WriteLine($"Mode: Decomp (preview ROM {project.BuiltRomPath})");
                    Console.WriteLine($"version={version}");
                    Console.WriteLine($"Stale={DecompBuildCore.IsStale(project)}");
                }
                else
                {
                    Console.WriteLine("Build succeeded (use --reload to load the built ROM)");
                    Console.WriteLine($"Stale={DecompBuildCore.IsStale(project)}");
                }

                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Unhandled error in --build-project: {ex.Message}");
                return 1;
            }
        }

        static int RunRomInfo(Dictionary<string, string> argsDic)
        {
            // Two load sources: classic --rom=<file>, or decomp --project=<dir>.
            bool isProject = argsDic.ContainsKey("--project") && !string.IsNullOrEmpty(argsDic["--project"]);
            string romPath;
            bool decompMode = false;

            if (isProject)
            {
                string projectDir = argsDic["--project"];
                RomLoader.InitEnvironment();
                if (!RomLoader.LoadProject(projectDir))
                {
                    // LoadProject already emitted a ShowError to stderr (CliAppServices).
                    // Re-emit the actionable message so the failure is unambiguous on
                    // both channels for the "run the build first" assertion (#1129).
                    Console.Error.WriteLine($"Error: Could not open decomp project: {projectDir}");
                    return 1;
                }
                romPath = CoreState.DecompProject?.BuiltRomPath ?? "";
                decompMode = true;
            }
            else
            {
                if (!argsDic.ContainsKey("--rom") || string.IsNullOrEmpty(argsDic["--rom"]))
                { Console.Error.WriteLine("Error: --rom-info requires --rom=<rom> or --project=<dir>"); return 1; }

                romPath = argsDic["--rom"];
                if (!File.Exists(romPath))
                { Console.Error.WriteLine($"Error: ROM not found: {romPath}"); return 1; }

                // Try to detect ROM version first — fail fast for non-ROM files
                RomLoader.InitEnvironment();
                if (!RomLoader.LoadRom(romPath, argsDic.ContainsKey("--force-version") ? argsDic["--force-version"] : null))
                {
                    Console.Error.WriteLine($"Error: Not a recognized GBA Fire Emblem ROM: {romPath}");
                    return 1;
                }
            }

            byte[] data = CoreState.ROM.Data;
            long fileSize = new FileInfo(romPath).Length;
            string version = CoreState.ROM.RomInfo?.VersionToFilename ?? "unknown";

            // Title and game code from header
            string title = (data.Length >= 0xC0)
                ? System.Text.Encoding.ASCII.GetString(data, 0xA0, 12).TrimEnd('\0')
                : "unknown";
            string gameCode = (data.Length >= 0xC0)
                ? System.Text.Encoding.ASCII.GetString(data, 0xAC, 4)
                : "unknown";

            // CRC32 using existing helper
            var crc = new UPSUtilCore.CRC32();
            uint crc32 = crc.Calc(data);

            // Header checksum (reuse RunChecksum logic)
            byte expected = 0, actual = 0;
            string checksumStatus = "UNKNOWN";
            if (data.Length >= 0xC0)
            {
                int sum = 0;
                for (int i = 0xA0; i < 0xBD; i++)
                    sum += data[i];
                expected = (byte)(-(0x19 + sum));
                actual = data[0xBD];
                checksumStatus = (actual == expected) ? "VALID" : "INVALID";
            }

            Console.WriteLine($"file={romPath}");
            Console.WriteLine($"size={fileSize}");
            Console.WriteLine($"title={title}");
            Console.WriteLine($"game_code={gameCode}");
            Console.WriteLine($"version={version}");
            Console.WriteLine($"crc32=0x{crc32:X08}");
            Console.WriteLine($"header_checksum=0x{actual:X02}");
            Console.WriteLine($"header_checksum_expected=0x{expected:X02}");
            Console.WriteLine($"header_checksum_status={checksumStatus}");
            if (decompMode)
            {
                Console.WriteLine($"Mode: Decomp (preview ROM {romPath})");
                // #1130: report the decomp symbol-artifact breakdown.
                try
                {
                    if (CoreState.DecompProject != null)
                    {
                        var resolver = DecompSymbolResolver.Load(CoreState.DecompProject);
                        Console.WriteLine(
                            $"Symbols: {resolver.Count} (map={resolver.CountMap} elf={resolver.CountElf} sym={resolver.CountSym} json={resolver.CountJson})");
                    }
                }
                catch { /* never break rom-info on a symbol-load fault */ }
            }
            else
                Console.WriteLine("Mode: Rom");
            return 0;
        }

        // #1130: resolve an address to a decomp project symbol. Loads the project
        // (which wires CoreState.AsmMapFileAsmCache -> MergedAsmMapFile), normalizes
        // the address to a GBA pointer, then resolves exact -> span-near. Never
        // throws; always exits 0.
        static int RunResolveAddr(Dictionary<string, string> argsDic)
        {
            try
            {
                string projectDir = argsDic.ContainsKey("--project") ? argsDic["--project"] : "";
                if (string.IsNullOrEmpty(projectDir))
                {
                    Console.Error.WriteLine("Error: --resolve-addr requires --project=<dir>");
                    return 0;
                }

                RomLoader.InitEnvironment();
                if (!RomLoader.LoadProject(projectDir))
                {
                    Console.Error.WriteLine($"Error: Could not open decomp project: {projectDir}");
                    return 0;
                }

                string addrStr = argsDic.ContainsKey("--resolve-addr") ? argsDic["--resolve-addr"] : "";
                uint rawAddr = U.atoi0x((addrStr ?? "").Trim());
                uint pointer = U.toPointer(rawAddr);

                Console.WriteLine($"addr=0x{pointer:X08}");

                IAsmMapFile asmMap = CoreState.AsmMapFileAsmCache?.GetAsmMapFile();
                if (asmMap == null)
                {
                    Console.WriteLine("symbol=(none)");
                    return 0;
                }

                // Exact match first.
                if (asmMap.TryGetValue(pointer, out var p) && p != null)
                {
                    Console.WriteLine($"symbol={p.ToStringInfo()}");
                    Console.WriteLine($"source={SourceFor(asmMap, pointer)}");
                    Console.WriteLine("offset=+0x0");
                    return 0;
                }

                // Span-covering nearest.
                uint near = asmMap.SearchNear(pointer);
                if (near != U.NOT_FOUND
                    && asmMap.TryGetValue(near, out var np)
                    && np != null
                    && pointer < (ulong)near + np.Length)
                {
                    uint off = pointer - near;
                    Console.WriteLine($"symbol={np.ToStringInfo()}");
                    Console.WriteLine($"source={SourceFor(asmMap, near)}");
                    Console.WriteLine($"offset=+0x{off:X}");
                    return 0;
                }

                Console.WriteLine("symbol=(none)");
                return 0;
            }
            catch (Exception ex)
            {
                // Never throw — report and exit 0.
                Console.Error.WriteLine($"resolve-addr error: {ex.Message}");
                Console.WriteLine("symbol=(none)");
                return 0;
            }
        }

        // Resolve the artifact source label for a key: a project symbol reports its
        // artifact (map/elf/sym/json); anything else is "shipped".
        static string SourceFor(IAsmMapFile asmMap, uint key)
        {
            if (asmMap is MergedAsmMapFile merged
                && merged.TryGetSource(key, out var src))
            {
                switch (src)
                {
                    case DecompArtifactSource.Map: return "map";
                    case DecompArtifactSource.Elf: return "elf";
                    case DecompArtifactSource.Sym: return "sym";
                    case DecompArtifactSource.Json: return "json";
                }
            }
            return "shipped";
        }

        // #1131: decomp diff-to-source migration assistant. Opens the project (built
        // ROM = canonical baseline), reads the edited ROM, classifies each changed
        // range (symbol/category/source/confidence), and prints/writes the advisory
        // report. ADVISORY + READ-ONLY — never writes the ROM or source. Analysis
        // never throws; usage faults return 1, otherwise exit 0.
        static int RunMigrateDiff(Dictionary<string, string> argsDic)
        {
            string projectDir = argsDic.ContainsKey("--project") ? argsDic["--project"] : "";
            if (string.IsNullOrEmpty(projectDir))
            {
                Console.Error.WriteLine("Error: --migrate-diff requires --project=<dir>");
                return 1;
            }
            string editedPath = argsDic.ContainsKey("--rom2") ? argsDic["--rom2"] : "";
            if (string.IsNullOrEmpty(editedPath))
            {
                Console.Error.WriteLine("Error: --migrate-diff requires --rom2=<editedRom>");
                return 1;
            }
            if (!File.Exists(editedPath))
            {
                Console.Error.WriteLine($"Error: edited ROM not found: {editedPath}");
                return 1;
            }

            RomLoader.InitEnvironment();
            if (!RomLoader.LoadProject(projectDir))
            {
                Console.Error.WriteLine($"Error: Could not open decomp project: {projectDir}");
                return 1;
            }

            ROM builtRom = CoreState.ROM;
            if (builtRom == null || builtRom.Data == null || builtRom.Data.Length == 0)
            {
                Console.Error.WriteLine("Error: built/preview ROM unavailable — run the build first.");
                return 1;
            }

            byte[] editedBytes;
            try { editedBytes = File.ReadAllBytes(editedPath); }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Error: could not read edited ROM: {ex.Message}");
                return 1;
            }

            // Pull the merged resolver (project over shipped) from the wired cache,
            // plus a freshly-loaded resolver for the section / object-path hints.
            MergedAsmMapFile map = CoreState.AsmMapFileAsmCache?.GetAsmMapFile() as MergedAsmMapFile;
            DecompSymbolResolver resolver = null;
            try { resolver = DecompSymbolResolver.Load(CoreState.DecompProject); }
            catch { /* analyzer tolerates a null resolver */ }

            int maxGap = DecompDiffMigrationCore.DefaultMaxGap;
            if (argsDic.ContainsKey("--max-gap") && int.TryParse(argsDic["--max-gap"], out int mg) && mg >= 0)
                maxGap = mg;

            Console.WriteLine($"Project: {projectDir}");
            Console.WriteLine($"Built ROM (baseline): {builtRom.Filename} ({builtRom.Data.Length} bytes)");
            Console.WriteLine($"Edited ROM: {editedPath} ({editedBytes.Length} bytes)");
            Console.WriteLine("Analyzing (advisory, read-only)...");

            MigrationReport report = DecompDiffMigrationCore.Analyze(builtRom, editedBytes, map, resolver, maxGap);

            // A requested --out write that fails is a FAILURE, not a warning:
            // automation that asked for a TSV must not be told "success" with no
            // report (Copilot PR #1139 finding 4). Print the summary first so the
            // analysis result is still visible, then exit non-zero.
            int exitCode = 0;
            if (argsDic.ContainsKey("--out") && !string.IsNullOrEmpty(argsDic["--out"]))
            {
                try
                {
                    File.WriteAllText(argsDic["--out"], DecompDiffMigrationCore.FormatTSV(report));
                    Console.WriteLine($"Report written to: {argsDic["--out"]}");
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"Error: could not write report to '{argsDic["--out"]}': {ex.Message}");
                    exitCode = 1;
                }
            }

            Console.WriteLine(DecompDiffMigrationCore.FormatSummary(report));
            return exitCode;
        }

        static int RunListTables(Dictionary<string, string> argsDic)
        {
            // Tables are registered in StructExportCore's static constructor
            var names = StructExportCore.GetTableNames().OrderBy(n => n, StringComparer.Ordinal);
            foreach (string name in names)
            {
                Console.WriteLine(name);
            }
            return 0;
        }

        /// <summary>
        /// --decomp-audit: print the maintained decomp round-trip coverage matrix (#1150).
        /// READ-ONLY; never loads a ROM. With --summary prints the per-tier coverage
        /// summary (counts + Unclassified + release-visibility note) instead of the table.
        /// Otherwise honors --format=tsv|md (default tsv). Writes to --out or stdout.
        /// Exit 0 always (or 1 on a write fault).
        /// </summary>
        static int RunDecompAudit(Dictionary<string, string> argsDic)
        {
            var rows = DecompRoundTripAuditCore.BuildMatrix();
            string text;

            // --summary: print the per-tier coverage summary + completeness/release note
            // (no table). Independent of --format.
            if (argsDic.ContainsKey("--summary"))
            {
                var summary = DecompRoundTripAuditCore.BuildSummary(rows);
                text = DecompRoundTripAuditCore.FormatSummary(summary);

                if (argsDic.ContainsKey("--out") && !string.IsNullOrEmpty(argsDic["--out"]))
                {
                    try
                    {
                        File.WriteAllText(argsDic["--out"], text);
                        Console.WriteLine($"Wrote: {argsDic["--out"]}");
                        return 0;
                    }
                    catch (Exception ex)
                    {
                        Console.Error.WriteLine($"Error: could not write '{argsDic["--out"]}': {ex.Message}");
                        return 1;
                    }
                }

                Console.Write(text);
                return 0;
            }

            string fmt = argsDic.ContainsKey("--format") && !string.IsNullOrEmpty(argsDic["--format"])
                ? argsDic["--format"].Trim().ToLowerInvariant()
                : "tsv";
            if (fmt != "tsv" && fmt != "md")
            {
                Console.Error.WriteLine($"Error: --format must be tsv or md (got '{fmt}')");
                return 1;
            }

            text = DecompRoundTripAuditCore.FormatMatrix(rows, fmt);

            if (argsDic.ContainsKey("--out") && !string.IsNullOrEmpty(argsDic["--out"]))
            {
                try
                {
                    File.WriteAllText(argsDic["--out"], text);
                    Console.WriteLine($"Wrote: {argsDic["--out"]}");
                    return 0;
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"Error: could not write '{argsDic["--out"]}': {ex.Message}");
                    return 1;
                }
            }

            Console.Write(text);
            return 0;
        }

        /// <summary>
        /// --nmm-to-manifest: parse a No$gba memory map into a decomp manifest tables[]
        /// entry JSON (#1150). No ROM. Exit 0 on parse-ok, 1 on usage/file-not-found,
        /// 2 when the NMM header is unusable (Ok=false). Warnings (unsupported fields)
        /// print to stderr.
        /// </summary>
        static int RunNmmToManifest(Dictionary<string, string> argsDic)
        {
            if (!argsDic.ContainsKey("--in") || string.IsNullOrEmpty(argsDic["--in"]))
            { Console.Error.WriteLine("Error: --nmm-to-manifest requires --in=<x.nmm>"); return 1; }
            string inPath = argsDic["--in"];
            if (!File.Exists(inPath))
            { Console.Error.WriteLine($"Error: input file not found: {inPath}"); return 1; }

            string tableName = argsDic.ContainsKey("--table") && !string.IsNullOrEmpty(argsDic["--table"])
                ? argsDic["--table"] : "table";

            string nmmText;
            try { nmmText = File.ReadAllText(inPath); }
            catch (Exception ex)
            { Console.Error.WriteLine($"Error: could not read '{inPath}': {ex.Message}"); return 1; }

            NmmParseResult parsed = NmmSchemaBridgeCore.ParseNmm(nmmText);
            foreach (string w in parsed.Warnings)
                Console.Error.WriteLine($"WARN: {w}");
            foreach (NmmField f in parsed.Fields)
                if (f.Unsupported)
                    Console.Error.WriteLine($"WARN: field '{f.Name}' is unsupported (flagged, not dropped): {f.UnsupportedReason}");

            string json = NmmSchemaBridgeCore.BuildManifestTablesEntry(parsed, tableName);

            if (argsDic.ContainsKey("--out") && !string.IsNullOrEmpty(argsDic["--out"]))
            {
                try
                {
                    File.WriteAllText(argsDic["--out"], json);
                    Console.WriteLine($"Wrote: {argsDic["--out"]}");
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"Error: could not write '{argsDic["--out"]}': {ex.Message}");
                    return 1;
                }
            }
            else
            {
                Console.WriteLine(json);
            }

            return parsed.Ok ? 0 : 2;
        }

        /// <summary>
        /// --manifest-to-nmm: emit .nmm text for a manifest table owner (#1150). Requires
        /// --project + --table. No ROM mutation. Exit 0 on success, 1 on usage/load fault,
        /// 2 when the table has no owner in the manifest. Warnings (pointer/var fields)
        /// print to stderr.
        /// </summary>
        static int RunManifestToNmm(Dictionary<string, string> argsDic)
        {
            string projectDir = argsDic.ContainsKey("--project") ? argsDic["--project"] : "";
            if (string.IsNullOrEmpty(projectDir))
            { Console.Error.WriteLine("Error: --manifest-to-nmm requires --project=<dir>"); return 1; }

            string table = argsDic.ContainsKey("--table") ? argsDic["--table"] : "";
            if (string.IsNullOrEmpty(table))
            { Console.Error.WriteLine("Error: --manifest-to-nmm requires --table=<name>"); return 1; }

            RomLoader.InitEnvironment();
            if (!RomLoader.LoadProject(projectDir))
                return 1;

            DecompProject project = CoreState.DecompProject;
            DecompTableEntry owner = project?.TryGetTableOwner(table);
            if (owner == null)
            {
                Console.Error.WriteLine($"Error: table '{table}' has no source owner in the manifest tables[] section.");
                return 2;
            }

            string nmm = NmmSchemaBridgeCore.ExportTableToNmm(owner, out List<string> warnings);
            foreach (string w in warnings)
                Console.Error.WriteLine($"WARN: {w}");

            if (argsDic.ContainsKey("--out") && !string.IsNullOrEmpty(argsDic["--out"]))
            {
                try
                {
                    File.WriteAllText(argsDic["--out"], nmm);
                    Console.WriteLine($"Wrote: {argsDic["--out"]}");
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"Error: could not write '{argsDic["--out"]}': {ex.Message}");
                    return 1;
                }
            }
            else
            {
                Console.Write(nmm);
            }

            return 0;
        }

        /// <summary>
        /// --validate-asset: structurally validate a decomp IMPORT asset on disk (#1150).
        /// READ-ONLY; NEVER loads a ROM. Exit 0 when there are no errors (warnings ok),
        /// 2 when there are errors, 1 on a usage / bad-kind fault.
        ///
        /// For <c>--kind=portrait-package</c> (#1350) the asset is a DIRECTORY (a multi-file
        /// portrait package: one composite sheet PNG + an optional JASC sidecar), so it takes
        /// <c>--path=&lt;dir&gt;</c> instead of <c>--in</c>, an optional presence flag
        /// <c>--allow-main-only</c>, and an optional <c>--project=&lt;dir&gt;</c> that confines
        /// <c>--path</c> to the project root (the project must open — else exit 2 — but the
        /// preview ROM is NEVER loaded). With <c>--project</c> an ABSOLUTE/escaping <c>--path</c>
        /// is rejected (return 2) — correct containment behaviour.
        /// </summary>
        static int RunValidateAsset(Dictionary<string, string> argsDic)
        {
            if (!argsDic.ContainsKey("--kind") || string.IsNullOrEmpty(argsDic["--kind"]))
            { Console.Error.WriteLine("Error: --validate-asset requires --kind=<graphics|palette|portrait|icon|map|mapchange|mapanime2pal|objtiles|mapchipconfig|mapanime1gfx|portrait-package>"); return 1; }
            AssetKind? kind = DecompAssetValidatorCore.ParseKind(argsDic["--kind"]);
            if (kind == null)
            { Console.Error.WriteLine($"Error: unknown --kind '{argsDic["--kind"]}'. Use: graphics, palette, portrait, icon, map, mapchange, mapanime2pal, objtiles, mapchipconfig, mapanime1gfx, portrait-package"); return 1; }

            // --kind=portrait-package is a DIRECTORY validator: --path (not --in), optional
            // --allow-main-only, optional --project containment. NEVER loads the preview ROM.
            if (kind.Value == AssetKind.PortraitPackage)
            {
                if (!argsDic.ContainsKey("--path") || string.IsNullOrEmpty(argsDic["--path"]))
                { Console.Error.WriteLine("Error: --validate-asset --kind=portrait-package requires --path=<dir>"); return 1; }
                string pathArg = argsDic["--path"];
                bool allowMainOnly = argsDic.ContainsKey("--allow-main-only");

                string dirPath = pathArg;
                if (argsDic.ContainsKey("--project") && !string.IsNullOrEmpty(argsDic["--project"]))
                {
                    // Project-root containment for --path. We resolve the project ROOT only
                    // (DecompProjectDetector.Detect — no ROM load, no RomLoader.LoadProject) so
                    // the preview ROM is NEVER loaded; the project must still be a valid decomp
                    // root (else exit 2 — we never silently drop containment).
                    RomLoader.InitEnvironment();
                    string projectDir = argsDic["--project"];
                    DecompProject project = DecompProjectDetector.Detect(projectDir);
                    if (project == null)
                    { Console.Error.WriteLine($"Error: '{projectDir}' is not a decomp project (containment for --path cannot be enforced)"); return 2; }

                    // ResolveSourcePath rejects absolute / ..-escaping paths under the project root.
                    string absPath = DecompAssetExportCore.ResolveSourcePath(project, pathArg);
                    if (absPath == null)
                    { Console.Error.WriteLine("Error: --path rejected (outside project root or invalid)"); return 2; }
                    dirPath = absPath;
                }

                AssetValidationResult pkgResult = DecompAssetValidatorCore.ValidateAssetPackage(kind.Value, dirPath, allowMainOnly);

                foreach (AssetIssue e in pkgResult.Errors)
                    Console.Error.WriteLine($"ERROR [{e.Code}] {e.Message}");
                foreach (AssetIssue w in pkgResult.Warnings)
                    Console.WriteLine($"WARN [{w.Code}] {w.Message}");

                Console.WriteLine($"Validation: {pkgResult.Errors.Count} error(s), {pkgResult.Warnings.Count} warning(s) — {(pkgResult.Ok ? "OK" : "FAILED")}");
                return pkgResult.Ok ? 0 : 2;
            }

            if (!argsDic.ContainsKey("--in") || string.IsNullOrEmpty(argsDic["--in"]))
            { Console.Error.WriteLine("Error: --validate-asset requires --in=<srcAsset>"); return 1; }
            string inPath = argsDic["--in"];

            AssetValidationResult result = DecompAssetValidatorCore.ValidateAsset(kind.Value, inPath);

            foreach (AssetIssue e in result.Errors)
                Console.Error.WriteLine($"ERROR [{e.Code}] {e.Message}");
            foreach (AssetIssue w in result.Warnings)
                Console.WriteLine($"WARN [{w.Code}] {w.Message}");

            Console.WriteLine($"Validation: {result.Errors.Count} error(s), {result.Warnings.Count} warning(s) — {(result.Ok ? "OK" : "FAILED")}");
            return result.Ok ? 0 : 2;
        }

        /// <summary>
        /// --import-asset: re-import a decomp <c>.mar</c> map LAYOUT to a RAW UNCOMPRESSED tilemap
        /// blob written into the source tree (#1148) — the IMPORT/verify direction that makes the
        /// <c>.mar</c> a genuine source-backed round-trip artifact (export → edit → re-import).
        /// NEVER mutates the ROM (<see cref="DecompAssetExportCore.ImportMap"/> has no ROM parameter and
        /// never touches <c>CoreState.ROM</c>). Only <c>--kind=map</c> is supported. With
        /// <c>--project=&lt;dir&gt;</c> the OUTPUT path (<c>--out</c>) is project-root-contained; the project
        /// MUST open (else exit 2 — we never silently drop containment), even though only its root is
        /// needed for the path check. Without <c>--project</c>, <c>--out</c> resolves against the cwd. The
        /// INPUT path (<c>--in</c>) is intentionally NOT containment-checked: an external <c>.mar</c>
        /// (e.g. one exported elsewhere) may be re-imported INTO the project tree — only writes are
        /// constrained to the project, reads are free.
        /// Exit 0 on success, 2 on import/path fault, 1 on a usage / bad-kind fault.
        /// </summary>
        static int RunImportAsset(Dictionary<string, string> argsDic)
        {
            if (!argsDic.ContainsKey("--kind") || string.IsNullOrEmpty(argsDic["--kind"]))
            { Console.Error.WriteLine("Error: --import-asset requires --kind=map|mapchange|mapanime2pal|objtiles|mapchipconfig|mapanime1gfx|portrait-package"); return 1; }
            AssetKind? kind = DecompAssetValidatorCore.ParseKind(argsDic["--kind"]);
            if (kind == null || (kind.Value != AssetKind.MapLayout && kind.Value != AssetKind.MapChangeOverlay && kind.Value != AssetKind.MapTileAnimation2Palette && kind.Value != AssetKind.ObjTiles && kind.Value != AssetKind.MapChipConfig && kind.Value != AssetKind.MapTileAnimation1Graphics && kind.Value != AssetKind.PortraitPackage))
            { Console.Error.WriteLine("Error: only --kind=map, --kind=mapchange, --kind=mapanime2pal, --kind=objtiles, --kind=mapchipconfig, --kind=mapanime1gfx or --kind=portrait-package is supported for --import-asset"); return 1; }

            // --kind=portrait-package is a DIRECTORY write-back (#1374): --path=<srcDir> (read,
            // not containment-checked) + --out=<destDir> (project-root-confined when --project is
            // given), optional --allow-main-only and --overwrite. ROM-FREE; never mutates the ROM.
            if (kind.Value == AssetKind.PortraitPackage)
                return RunImportPortraitPackage(argsDic);

            if (!argsDic.ContainsKey("--in") || string.IsNullOrEmpty(argsDic["--in"]))
            { Console.Error.WriteLine("Error: --import-asset requires --in=<x.mar|x.change|x.mapanime2pal|x.objtiles>"); return 1; }
            string absIn = argsDic["--in"];

            if (!argsDic.ContainsKey("--out") || string.IsNullOrEmpty(argsDic["--out"]))
            { Console.Error.WriteLine("Error: --import-asset requires --out=<x.bin>"); return 1; }
            string outRel = argsDic["--out"];

            RomLoader.InitEnvironment();

            // Optional --project gives project-root containment for the output path. This import/
            // verify path is ROM-FREE: we resolve the project ROOT with DecompProjectDetector.Detect
            // (no ROM load, no InitFull) rather than RomLoader.LoadProject — so it works for an
            // unbuilt-but-valid project and never sets CoreState.ROM (#1148 review finding). If the
            // user asked for a project it MUST be a valid decomp root, else exit 2 — we never silently
            // fall back to no-containment (cwd-relative), which would let --out escape the project
            // tree. Without --project, --out is resolved against the cwd (classic export parity).
            DecompProject project = null;
            if (argsDic.ContainsKey("--project") && !string.IsNullOrEmpty(argsDic["--project"]))
            {
                string projectDir = argsDic["--project"];
                project = DecompProjectDetector.Detect(projectDir);
                if (project == null)
                {
                    Console.Error.WriteLine($"Error: '{projectDir}' is not a decomp project (containment for --out cannot be enforced)");
                    return 2;
                }
            }

            string absOut = DecompAssetExportCore.ResolveSourcePath(project, outRel);
            if (absOut == null)
            {
                Console.Error.WriteLine("Error: output path rejected (outside project root or invalid)");
                return 2;
            }

            DecompAssetResult result;
            if (kind.Value == AssetKind.ObjTiles)
                result = DecompAssetExportCore.ImportObjTiles(absIn, absOut);
            else if (kind.Value == AssetKind.MapChipConfig)
                result = DecompAssetExportCore.ImportMapChipConfig(absIn, absOut);
            else if (kind.Value == AssetKind.MapTileAnimation1Graphics)
                result = DecompAssetExportCore.ImportMapAnime1Gfx(absIn, absOut);
            else if (kind.Value == AssetKind.MapChangeOverlay)
                result = DecompAssetExportCore.ImportMapChange(absIn, absOut);
            else if (kind.Value == AssetKind.MapTileAnimation2Palette)
                result = DecompAssetExportCore.ImportMapAnime2Pal(absIn, absOut);
            else
                result = DecompAssetExportCore.ImportMap(absIn, absOut);
            if (result.Ok)
            {
                Console.WriteLine(result.Message);
                foreach (string path in result.WrittenPaths)
                    Console.WriteLine($"Wrote: {path}");
                return 0;
            }

            Console.Error.WriteLine($"Error: {result.Message}");
            return 2;
        }

        /// <summary>
        /// --import-asset --kind=portrait-package (#1374): write-back / import an already-validated
        /// portrait PACKAGE directory (one 128x112 composite sheet PNG + optional name-matched JASC
        /// sidecar) INTO the source tree as an identity copy. ROM-FREE — never loads or mutates the
        /// ROM. Takes <c>--path=&lt;srcDir&gt;</c> (read; NOT containment-checked, mirroring --import-asset
        /// --kind=map) and <c>--out=&lt;destDir&gt;</c> (project-root-confined when <c>--project</c> is
        /// given). Optional <c>--allow-main-only</c> (accept a 96x80 main-mug-only sheet) and
        /// <c>--overwrite</c> (replace an existing single-package owner; without it an existing owner
        /// is refused). The destination-owner gate REFUSES ambiguous / already-owned destinations
        /// before any write. Exit 0 on success, 2 on validation/owner/path fault, 1 on usage error.
        /// </summary>
        static int RunImportPortraitPackage(Dictionary<string, string> argsDic)
        {
            if (!argsDic.ContainsKey("--path") || string.IsNullOrEmpty(argsDic["--path"]))
            { Console.Error.WriteLine("Error: --import-asset --kind=portrait-package requires --path=<srcDir>"); return 1; }
            string srcDir = argsDic["--path"];

            if (!argsDic.ContainsKey("--out") || string.IsNullOrEmpty(argsDic["--out"]))
            { Console.Error.WriteLine("Error: --import-asset --kind=portrait-package requires --out=<destDir>"); return 1; }
            string outRel = argsDic["--out"];

            bool allowMainOnly = argsDic.ContainsKey("--allow-main-only");
            bool overwrite = argsDic.ContainsKey("--overwrite");

            RomLoader.InitEnvironment();

            // Optional --project gives project-root containment for the OUTPUT dir. ROM-FREE: resolve
            // the project ROOT with DecompProjectDetector.Detect (no ROM load, no InitFull) so it works
            // for an unbuilt-but-valid project and never sets CoreState.ROM. If the user asked for a
            // project it MUST be a valid decomp root, else exit 2 (never silently drop containment).
            DecompProject project = null;
            if (argsDic.ContainsKey("--project") && !string.IsNullOrEmpty(argsDic["--project"]))
            {
                string projectDir = argsDic["--project"];
                project = DecompProjectDetector.Detect(projectDir);
                if (project == null)
                {
                    Console.Error.WriteLine($"Error: '{projectDir}' is not a decomp project (containment for --out cannot be enforced)");
                    return 2;
                }
            }

            string absOut = DecompAssetExportCore.ResolveSourcePath(project, outRel);
            if (absOut == null)
            {
                Console.Error.WriteLine("Error: output path rejected (outside project root or invalid)");
                return 2;
            }

            DecompAssetResult result = DecompAssetExportCore.ImportPortraitPackage(srcDir, absOut, allowMainOnly, overwrite);
            if (result.Ok)
            {
                Console.WriteLine(result.Message);
                foreach (string path in result.WrittenPaths)
                    Console.WriteLine($"Wrote: {path}");
                return 0;
            }

            Console.Error.WriteLine($"Error: {result.Message}");
            return 2;
        }

        /// <summary>
        /// --roundtrip-asset: validate a decomp <c>.mar</c> map LAYOUT and PROVE its u16 layout
        /// BODY round-trips losslessly (export→import→export is byte-identical) (#1148). READ-ONLY;
        /// NEVER loads a ROM. Exit 0 when the body round-trips, 2 on validation failure or a shift
        /// mismatch, 1 on a usage / bad-kind fault.
        /// </summary>
        static int RunRoundtripAsset(Dictionary<string, string> argsDic)
        {
            if (!argsDic.ContainsKey("--kind") || string.IsNullOrEmpty(argsDic["--kind"]))
            { Console.Error.WriteLine("Error: --roundtrip-asset requires --kind=map|mapchange|mapanime2pal|objtiles|mapchipconfig|mapanime1gfx|portrait-package"); return 1; }
            AssetKind? kind = DecompAssetValidatorCore.ParseKind(argsDic["--kind"]);
            if (kind == null || (kind.Value != AssetKind.MapLayout && kind.Value != AssetKind.MapChangeOverlay && kind.Value != AssetKind.MapTileAnimation2Palette && kind.Value != AssetKind.ObjTiles && kind.Value != AssetKind.MapChipConfig && kind.Value != AssetKind.MapTileAnimation1Graphics && kind.Value != AssetKind.PortraitPackage))
            { Console.Error.WriteLine("Error: only --kind=map, --kind=mapchange, --kind=mapanime2pal, --kind=objtiles, --kind=mapchipconfig, --kind=mapanime1gfx or --kind=portrait-package is supported for --roundtrip-asset"); return 1; }

            // --kind=portrait-package (#1374): a DIRECTORY round-trip against an explicit BASELINE.
            // Requires --path=<srcDir> + --expect=<baselineDir> (the oracle — NOT a self-compare).
            if (kind.Value == AssetKind.PortraitPackage)
                return RunRoundtripPortraitPackage(argsDic);

            if (!argsDic.ContainsKey("--in") || string.IsNullOrEmpty(argsDic["--in"]))
            { Console.Error.WriteLine("Error: --roundtrip-asset requires --in=<x.mar|x.change|x.mapanime2pal|x.objtiles>"); return 1; }
            string inPath = argsDic["--in"];

            AssetValidationResult v = DecompAssetValidatorCore.ValidateAsset(kind.Value, inPath);
            if (!v.Ok)
            {
                foreach (AssetIssue e in v.Errors)
                    Console.Error.WriteLine($"ERROR [{e.Code}] {e.Message}");
                return 2;
            }

            byte[] body;
            try { body = File.ReadAllBytes(inPath); }
            catch (Exception ex)
            { Console.Error.WriteLine($"Error: could not read input: {ex.Message}"); return 2; }

            bool ok;
            string okMsg;
            if (kind.Value == AssetKind.MapChangeOverlay)
            {
                // The overlay body has no intrinsic dims — read them from the REQUIRED sidecar.
                if (!DecompAssetExportCore.TryReadMapChangeDims(inPath + ".json", out int w, out int h))
                { Console.Error.WriteLine("Error: sidecar .change.json required to read dimensions"); return 2; }
                ok = DecompAssetExportCore.RoundTripMapChangeBody(body, w, h);
                okMsg = "Round-trip OK (structure-exact map-change overlay body)";
            }
            else if (kind.Value == AssetKind.MapTileAnimation2Palette)
            {
                // The palette body has no intrinsic count — read it from the REQUIRED sidecar.
                if (!DecompAssetExportCore.TryReadMapAnime2PalCount(inPath + ".json", out int count))
                { Console.Error.WriteLine("Error: sidecar .json required to read count"); return 2; }
                ok = DecompAssetExportCore.RoundTripMapAnime2PalBody(body, count);
                okMsg = "Round-trip OK (structure-exact map tile-animation-2 palette body)";
            }
            else if (kind.Value == AssetKind.ObjTiles)
            {
                // The decompressed OBJ body has no intrinsic length — read it from the REQUIRED sidecar.
                if (!DecompAssetExportCore.TryReadObjTilesLength(inPath + ".json", out int expectedLen))
                { Console.Error.WriteLine("Error: sidecar .objtiles.json required to read length"); return 2; }
                ok = DecompAssetExportCore.RoundTripObjTilesBody(body, expectedLen);
                okMsg = "Round-trip OK (structure-exact OBJ tileset decompressed body)";
            }
            else if (kind.Value == AssetKind.MapChipConfig)
            {
                // The decompressed chipset config body has no intrinsic length — read it from the REQUIRED sidecar.
                if (!DecompAssetExportCore.TryReadMapChipConfigLength(inPath + ".json", out int expectedLen))
                { Console.Error.WriteLine("Error: sidecar .mapchipconfig.json required to read length"); return 2; }
                ok = DecompAssetExportCore.RoundTripMapChipConfigBody(body, expectedLen);
                okMsg = "Round-trip OK (structure-exact map chipset config decompressed body)";
            }
            else if (kind.Value == AssetKind.MapTileAnimation1Graphics)
            {
                // The raw 4bpp graphics body has no intrinsic length — read it from the REQUIRED sidecar.
                if (!DecompAssetExportCore.TryReadMapAnime1GfxLength(inPath + ".json", out int expectedLen))
                { Console.Error.WriteLine("Error: sidecar .mapanime1gfx.json required to read length"); return 2; }
                ok = DecompAssetExportCore.RoundTripMapAnime1GfxBody(body, expectedLen);
                okMsg = "Round-trip OK (structure-exact map tile-animation-1 graphics body)";
            }
            else
            {
                ok = DecompAssetExportCore.RoundTripMarBody(body);
                okMsg = "Round-trip OK (lossless map layout body)";
            }

            if (ok)
            {
                Console.WriteLine(okMsg);
                return 0;
            }

            Console.Error.WriteLine("Round-trip MISMATCH");
            return 2;
        }

        /// <summary>
        /// --roundtrip-asset --kind=portrait-package (#1374): validate a source portrait PACKAGE and
        /// PROVE it is byte-identical to an explicit BASELINE package (the oracle). READ-ONLY; NEVER
        /// loads a ROM. The <c>--expect=&lt;baselineDir&gt;</c> baseline is REQUIRED — this is NOT a
        /// self-compare, so a validation-valid but byte-tampered source genuinely mismatches. Exit 0
        /// when byte-identical to the baseline, 2 on mismatch or either-side validation failure, 1 on
        /// a usage fault.
        /// </summary>
        static int RunRoundtripPortraitPackage(Dictionary<string, string> argsDic)
        {
            if (!argsDic.ContainsKey("--path") || string.IsNullOrEmpty(argsDic["--path"]))
            { Console.Error.WriteLine("Error: --roundtrip-asset --kind=portrait-package requires --path=<srcDir>"); return 1; }
            string srcDir = argsDic["--path"];

            if (!argsDic.ContainsKey("--expect") || string.IsNullOrEmpty(argsDic["--expect"]))
            { Console.Error.WriteLine("Error: --roundtrip-asset --kind=portrait-package requires --expect=<baselineDir> (the oracle; no self-compare)"); return 1; }
            string baselineDir = argsDic["--expect"];

            bool allowMainOnly = argsDic.ContainsKey("--allow-main-only");

            DecompAssetResult result = DecompAssetExportCore.RoundTripPortraitPackageAgainstBaseline(srcDir, baselineDir, allowMainOnly);
            if (result.Ok)
            {
                Console.WriteLine(result.Message);
                return 0;
            }

            Console.Error.WriteLine($"Round-trip FAILED: {result.Message}");
            return 2;
        }

        /// <summary>
        /// --verify-asset: byte-exact ROM-backed mismatch proof for a map-change OVERLAY (#1355), a
        /// map tile-animation-2 PALETTE block (#1360), or an OBJ tileset's LZ77-DECOMPRESSED payload
        /// (#1371). Compares the ROM-side bytes at <c>--addr</c> against the input file body
        /// byte-for-byte. This path DOES load the ROM (READ-ONLY) — it is the ONLY ROM-backed
        /// verification path (export/import never touch the ROM). <c>--kind=mapchange</c> (requires
        /// <c>--width</c>/<c>--height</c>), <c>--kind=mapanime2pal</c> (requires <c>--count</c>), and
        /// <c>--kind=objtiles</c> (only <c>--addr</c>; the ROM block is re-decompressed before the
        /// compare) are supported. With <c>--project=&lt;dir&gt;</c> the ROM is the project's preview
        /// build; otherwise <c>--rom=&lt;path&gt;</c>. Exit 0 byte-identical, 2 mismatch / fault, 1 usage error.
        /// </summary>
        static int RunVerifyAsset(Dictionary<string, string> argsDic)
        {
            if (!argsDic.ContainsKey("--kind") || string.IsNullOrEmpty(argsDic["--kind"]))
            { Console.Error.WriteLine("Error: --verify-asset requires --kind=mapchange|mapanime2pal|objtiles|mapchipconfig|mapanime1gfx"); return 1; }
            AssetKind? kind = DecompAssetValidatorCore.ParseKind(argsDic["--kind"]);
            if (kind == null || (kind.Value != AssetKind.MapChangeOverlay && kind.Value != AssetKind.MapTileAnimation2Palette && kind.Value != AssetKind.ObjTiles && kind.Value != AssetKind.MapChipConfig && kind.Value != AssetKind.MapTileAnimation1Graphics))
            { Console.Error.WriteLine("Error: only --kind=mapchange, --kind=mapanime2pal, --kind=objtiles, --kind=mapchipconfig or --kind=mapanime1gfx is supported for --verify-asset"); return 1; }
            bool isAnime2Pal = kind.Value == AssetKind.MapTileAnimation2Palette;
            bool isObjTiles = kind.Value == AssetKind.ObjTiles;
            bool isChipConfig = kind.Value == AssetKind.MapChipConfig;
            bool isAnime1Gfx = kind.Value == AssetKind.MapTileAnimation1Graphics;

            if (!argsDic.ContainsKey("--in") || string.IsNullOrEmpty(argsDic["--in"]))
            { Console.Error.WriteLine("Error: --verify-asset requires --in=<x.change|x.mapanime2pal|x.objtiles|x.mapchipconfig|x.mapanime1gfx>"); return 1; }
            string absIn = argsDic["--in"];

            if (!argsDic.ContainsKey("--addr") || string.IsNullOrEmpty(argsDic["--addr"]))
            { Console.Error.WriteLine("Error: --verify-asset requires --addr=<hex>"); return 1; }
            if (isObjTiles || isChipConfig)
            {
                // objtiles (#1371) / mapchipconfig (#1375) need ONLY --addr (the DEREFERENCED LZ77
                // stream address); the decompressed length comes from the REQUIRED sidecar, NOT
                // width/height/count.
            }
            else if (isAnime1Gfx)
            {
                // mapanime1gfx (#1389) is a RAW (uncompressed) block — needs --addr (the DEREFERENCED
                // anime-1 entry +4 graphics pointer) + --length (the entry +2 byte length). NOT LZ77,
                // so the length does NOT come from a self-delimiting stream.
                if (!argsDic.ContainsKey("--length") || string.IsNullOrEmpty(argsDic["--length"]))
                { Console.Error.WriteLine("Error: --verify-asset --kind=mapanime1gfx requires --length=<int>"); return 1; }
            }
            else if (isAnime2Pal)
            {
                // mapanime2pal (#1360) uses a single --count, NOT width/height.
                if (!argsDic.ContainsKey("--count") || string.IsNullOrEmpty(argsDic["--count"]))
                { Console.Error.WriteLine("Error: --verify-asset --kind=mapanime2pal requires --count=<int>"); return 1; }
            }
            else
            {
                // mapchange (#1355) uses width/height.
                if (!argsDic.ContainsKey("--width") || string.IsNullOrEmpty(argsDic["--width"]))
                { Console.Error.WriteLine("Error: --verify-asset --kind=mapchange requires --width=<int>"); return 1; }
                if (!argsDic.ContainsKey("--height") || string.IsNullOrEmpty(argsDic["--height"]))
                { Console.Error.WriteLine("Error: --verify-asset --kind=mapchange requires --height=<int>"); return 1; }
            }

            // ---- ROM source: --project or --rom ----
            bool isProject = argsDic.ContainsKey("--project") && !string.IsNullOrEmpty(argsDic["--project"]);
            bool isRom = argsDic.ContainsKey("--rom") && !string.IsNullOrEmpty(argsDic["--rom"]);
            if (!isProject && !isRom)
            { Console.Error.WriteLine("Error: --verify-asset requires --rom=<path> or --project=<dir>"); return 1; }

            RomLoader.InitEnvironment();

            if (isProject)
            {
                string projectDir = argsDic["--project"];
                if (!RomLoader.LoadProject(projectDir))
                    return 1;
            }
            else
            {
                string romPath = argsDic["--rom"];
                if (!File.Exists(romPath))
                { Console.Error.WriteLine($"Error: ROM not found: {romPath}"); return 1; }
                string forceVersion = argsDic.ContainsKey("--force-version") ? argsDic["--force-version"] : null;
                if (!RomLoader.LoadRom(romPath, forceVersion))
                    return 1;
                RomLoader.InitFull();
            }

            // ---- Parse hex address + int dims (same convention as RunExportAsset) ----
            static bool TryParseAddr(string s, out uint result)
            {
                result = 0;
                if (string.IsNullOrEmpty(s)) return false;
                string clean = s.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? s.Substring(2) : s;
                if (!uint.TryParse(clean, System.Globalization.NumberStyles.HexNumber, null, out result))
                    return false;
                result = U.toOffset(result);
                return true;
            }

            if (!TryParseAddr(argsDic["--addr"], out uint addr))
            { Console.Error.WriteLine($"Error: Invalid address: {argsDic["--addr"]}"); return 1; }

            DecompAssetResult result;
            if (isObjTiles)
            {
                // objtiles (#1371): only --addr and --in; the ROM block is re-decompressed and
                // byte-compared against the .objtiles body (read-only, never mutates the ROM).
                result = DecompAssetExportCore.VerifyObjTilesAgainstRom(CoreState.ROM, addr, absIn);
            }
            else if (isChipConfig)
            {
                // mapchipconfig (#1375): only --addr and --in; the ROM block is re-decompressed and
                // byte-compared against the .mapchipconfig body (read-only, never mutates the ROM).
                result = DecompAssetExportCore.VerifyMapChipConfigAgainstRom(CoreState.ROM, addr, absIn);
            }
            else if (isAnime1Gfx)
            {
                // mapanime1gfx (#1389): RAW byte compare (NOT decompress) of the ROM block at --addr
                // for --length bytes against the .mapanime1gfx body (read-only, never mutates the ROM).
                if (!int.TryParse(argsDic["--length"], out int gfxLen) || gfxLen <= 0)
                { Console.Error.WriteLine($"Error: Invalid --length: {argsDic["--length"]}"); return 1; }
                result = DecompAssetExportCore.VerifyMapAnime1GfxAgainstRom(CoreState.ROM, addr, gfxLen, absIn);
            }
            else if (isAnime2Pal)
            {
                if (!int.TryParse(argsDic["--count"], out int count) || count <= 0)
                { Console.Error.WriteLine($"Error: Invalid --count: {argsDic["--count"]}"); return 1; }
                result = DecompAssetExportCore.VerifyMapAnime2PalAgainstRom(CoreState.ROM, addr, count, absIn);
            }
            else
            {
                if (!int.TryParse(argsDic["--width"], out int width) || width <= 0)
                { Console.Error.WriteLine($"Error: Invalid --width: {argsDic["--width"]}"); return 1; }
                if (!int.TryParse(argsDic["--height"], out int height) || height <= 0)
                { Console.Error.WriteLine($"Error: Invalid --height: {argsDic["--height"]}"); return 1; }
                result = DecompAssetExportCore.VerifyMapChangeAgainstRom(CoreState.ROM, addr, width, height, absIn);
            }
            if (result.Ok)
            {
                Console.WriteLine(result.Message);
                return 0;
            }

            Console.Error.WriteLine($"Error: {result.Message}");
            return 2;
        }

        /// <summary>
        /// --export-asset: export a ROM asset (palette/graphics/map/mapchange/text/shop) to a decomp
        /// source-tree path. Supports both --project=&lt;dir&gt; (with path containment) and
        /// --rom=&lt;path&gt; (classic, no containment). READ-ONLY: never modifies the ROM.
        ///
        /// <c>--kind=mapchange</c> (#1355) exports the RAW UNCOMPRESSED map-change OVERLAY tile data
        /// block (requires <c>--addr</c> = the change_mar offset, <c>--width</c>, <c>--height</c>) — NOT
        /// the .mar tile layout and NOT the 12-byte change-record chain.
        ///
        /// <c>--kind=mapanime2pal</c> (#1360) exports the RAW UNCOMPRESSED map tile-animation-2 PALETTE
        /// data block (requires <c>--addr</c> = the anime-2 entry <c>+0</c> pointer offset, <c>--count</c>
        /// = the entry <c>+5</c> color count) — a flat <c>u16</c> LE array of <c>count</c> 15-bit GBA
        /// colors, NOT the anime-2 entry/PLIST table and NOT LZ77.
        /// </summary>
        /// <summary>
        /// --export-voicegroup: export a FEBuilder voicegroup (M4A instrument set) as
        /// reviewable decomp SOURCE macro asm (voicegroupNNN.s) using
        /// asm/macros/music_voice.inc (#1362). READ-ONLY: reads the preview ROM,
        /// writes a .s source file under the project/out root, NEVER mutates the ROM.
        /// </summary>
        static int RunExportVoicegroup(Dictionary<string, string> argsDic)
        {
            // ---- Required: --out ----
            if (!argsDic.ContainsKey("--out") || string.IsNullOrEmpty(argsDic["--out"]))
            { Console.Error.WriteLine("Error: --export-voicegroup requires --out=<voicegroupNNN.s>"); return 1; }
            string outRel = argsDic["--out"];

            // ---- Exactly one of --voicegroup-addr / --song-id ----
            bool hasAddr = argsDic.ContainsKey("--voicegroup-addr") && !string.IsNullOrEmpty(argsDic["--voicegroup-addr"]);
            bool hasSong = argsDic.ContainsKey("--song-id") && !string.IsNullOrEmpty(argsDic["--song-id"]);
            if (hasAddr == hasSong)
            { Console.Error.WriteLine("Error: --export-voicegroup requires exactly one of --voicegroup-addr=<hex> or --song-id=<n>"); return 1; }

            // ---- ROM source: --project or --rom ----
            bool isProject = argsDic.ContainsKey("--project") && !string.IsNullOrEmpty(argsDic["--project"]);
            bool isRom = argsDic.ContainsKey("--rom") && !string.IsNullOrEmpty(argsDic["--rom"]);
            if (!isProject && !isRom)
            { Console.Error.WriteLine("Error: --export-voicegroup requires --rom=<path> or --project=<dir>"); return 1; }

            RomLoader.InitEnvironment();

            DecompProject project = null;
            if (isProject)
            {
                if (!RomLoader.LoadProject(argsDic["--project"]))
                    return 1;
                project = CoreState.DecompProject;
            }
            else
            {
                string romPath = argsDic["--rom"];
                if (!File.Exists(romPath))
                { Console.Error.WriteLine($"Error: ROM not found: {romPath}"); return 1; }
                string forceVersion = argsDic.ContainsKey("--force-version") ? argsDic["--force-version"] : null;
                if (!RomLoader.LoadRom(romPath, forceVersion))
                    return 1;
                RomLoader.InitFull();
            }

            ROM rom = CoreState.ROM;
            if (rom == null)
            { Console.Error.WriteLine("Error: ROM is not loaded."); return 1; }

            // ---- Resolve output path (root-confined in project mode) ----
            string absOut = DecompAssetExportCore.ResolveSourcePath(project, outRel);
            if (absOut == null)
            { Console.Error.WriteLine("Error: output path rejected (outside project root or invalid)"); return 2; }

            // ---- Resolve the voicegroup base offset + number ----
            uint voicegroupOffset;
            int number;
            if (hasAddr)
            {
                if (!TryParseHexAddr(argsDic["--voicegroup-addr"], out voicegroupOffset))
                { Console.Error.WriteLine($"Error: Invalid --voicegroup-addr: {argsDic["--voicegroup-addr"]}"); return 1; }
                number = ParseNumberOrDefault(argsDic, 0);
            }
            else
            {
                if (!int.TryParse(argsDic["--song-id"], out int songId) || songId < 0)
                { Console.Error.WriteLine($"Error: Invalid --song-id: {argsDic["--song-id"]}"); return 1; }
                uint tableAddr = SongExchangeCore.FindSongTablePointer(rom.Data, rom.RomInfo.sound_table_pointer);
                if (tableAddr == 0)
                { Console.Error.WriteLine("Error: could not locate the song table for this ROM."); return 2; }
                var songs = SongExchangeCore.SongTableToSongList(rom.Data, tableAddr);
                var song = songs.Find(s => (int)s.Number == songId);
                if (song == null)
                { Console.Error.WriteLine($"Error: song id {songId} was not found in the song table."); return 2; }
                if (song.Voices == 0)
                { Console.Error.WriteLine($"Error: song id {songId} has no voicegroup pointer."); return 2; }
                voicegroupOffset = song.Voices;
                number = ParseNumberOrDefault(argsDic, songId);
            }

            // ---- Export (READ-ONLY) ----
            // Read-only invariant proof WITHOUT cloning the whole (tens-of-MB) ROM:
            // a length + content hash before/after detects any mutation cheaply
            // (Copilot review).
            int beforeLen = rom.Data.Length;
            byte[] beforeHash = System.Security.Cryptography.SHA256.HashData(rom.Data);

            var result = VoicegroupAsmExportCore.Export(rom, voicegroupOffset, number);

            byte[] afterHash = System.Security.Cryptography.SHA256.HashData(rom.Data);
            if (rom.Data.Length != beforeLen || !ByteArrayEquals(beforeHash, afterHash))
            { Console.Error.WriteLine("Error: internal -- voicegroup export mutated the ROM (aborting, no write)."); return 3; }

            if (!result.Ok)
            {
                Console.Error.WriteLine("Error: voicegroup export failed:");
                foreach (var d in result.Diagnostics) Console.Error.WriteLine("  " + d);
                return 2;
            }

            try
            {
                string dir = Path.GetDirectoryName(absOut);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                File.WriteAllText(absOut, result.Text);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Error: failed to write {absOut}: {ex.Message}");
                return 2;
            }

            Console.WriteLine($"Exported voicegroup{number:D3} ({result.VoiceCount} voices) to {absOut}");
            if (result.Diagnostics.Count > 0)
            {
                Console.WriteLine($"Diagnostics ({result.Diagnostics.Count}):");
                foreach (var d in result.Diagnostics) Console.WriteLine("  " + d);
            }
            return 0;
        }

        /// <summary>
        /// --export-battle-anim-decomp: export a FEBuilder/FEditor-decoded battle
        /// animation as reviewable decomp SOURCE (banim_&lt;TAG&gt;_motion.s) using the
        /// fireemblem8u banim macros + .pal/.json sidecars (#1363). READ-ONLY: reads the
        /// preview ROM, writes source/sidecar files under the project/out root, NEVER
        /// mutates the ROM.
        /// </summary>
        static int RunExportBattleAnimDecomp(Dictionary<string, string> argsDic)
        {
            // ---- Required: --out ----
            if (!argsDic.ContainsKey("--out") || string.IsNullOrEmpty(argsDic["--out"]))
            { Console.Error.WriteLine("Error: --export-battle-anim-decomp requires --out=<banim_<TAG>_motion.s>"); return 1; }
            string outRel = argsDic["--out"];

            // ---- Exactly one of --animation-id / --banim-addr ----
            bool hasId = argsDic.ContainsKey("--animation-id") && !string.IsNullOrEmpty(argsDic["--animation-id"]);
            bool hasAddr = argsDic.ContainsKey("--banim-addr") && !string.IsNullOrEmpty(argsDic["--banim-addr"]);
            if (hasId == hasAddr)
            { Console.Error.WriteLine("Error: --export-battle-anim-decomp requires exactly one of --animation-id=<n> or --banim-addr=<hex>"); return 1; }

            // ---- ROM source: --project or --rom ----
            bool isProject = argsDic.ContainsKey("--project") && !string.IsNullOrEmpty(argsDic["--project"]);
            bool isRom = argsDic.ContainsKey("--rom") && !string.IsNullOrEmpty(argsDic["--rom"]);
            if (!isProject && !isRom)
            { Console.Error.WriteLine("Error: --export-battle-anim-decomp requires --rom=<path> or --project=<dir>"); return 1; }

            RomLoader.InitEnvironment();

            DecompProject project = null;
            if (isProject)
            {
                if (!RomLoader.LoadProject(argsDic["--project"]))
                    return 1;
                project = CoreState.DecompProject;
            }
            else
            {
                string romPath = argsDic["--rom"];
                if (!File.Exists(romPath))
                { Console.Error.WriteLine($"Error: ROM not found: {romPath}"); return 1; }
                string forceVersion = argsDic.ContainsKey("--force-version") ? argsDic["--force-version"] : null;
                if (!RomLoader.LoadRom(romPath, forceVersion))
                    return 1;
                RomLoader.InitFull();
            }

            ROM rom = CoreState.ROM;
            if (rom == null)
            { Console.Error.WriteLine("Error: ROM is not loaded."); return 1; }

            // ---- Resolve output path (root-confined in project mode) ----
            string absOut = DecompAssetExportCore.ResolveSourcePath(project, outRel);
            if (absOut == null)
            { Console.Error.WriteLine("Error: output path rejected (outside project root or invalid)"); return 2; }

            // ---- Resolve the animation record offset + tag ----
            uint animAddr;
            int number;
            if (hasId)
            {
                if (!uint.TryParse(argsDic["--animation-id"], out uint animId))
                { Console.Error.WriteLine($"Error: Invalid --animation-id: {argsDic["--animation-id"]}"); return 1; }
                animAddr = BattleAnimeImportCore.ResolveBattleAnimeAddr(rom, animId);
                if (animAddr == 0 || animAddr == U.NOT_FOUND)
                { Console.Error.WriteLine($"Error: could not resolve animation id {animId} (out of range?)."); return 2; }
                number = ParseNumberOrDefault(argsDic, (int)animId);
            }
            else
            {
                if (!TryParseHexAddr(argsDic["--banim-addr"], out animAddr))
                { Console.Error.WriteLine($"Error: Invalid --banim-addr: {argsDic["--banim-addr"]}"); return 1; }
                number = ParseNumberOrDefault(argsDic, 0);
            }

            // Tag: prefer an explicit --tag, else derive from the number.
            string tag = argsDic.ContainsKey("--tag") && !string.IsNullOrEmpty(argsDic["--tag"])
                ? argsDic["--tag"]
                : ("anim" + number.ToString("D3"));

            // ---- Export (READ-ONLY) ----
            // Read-only invariant proof WITHOUT cloning the whole ROM: a length +
            // content hash before/after detects any mutation cheaply (#1362 invariant).
            int beforeLen = rom.Data.Length;
            byte[] beforeHash = System.Security.Cryptography.SHA256.HashData(rom.Data);

            var result = BattleAnimDecompExportCore.Export(rom, animAddr, tag);

            byte[] afterHash = System.Security.Cryptography.SHA256.HashData(rom.Data);
            if (rom.Data.Length != beforeLen || !ByteArrayEquals(beforeHash, afterHash))
            { Console.Error.WriteLine("Error: internal -- battle-anim decomp export mutated the ROM (aborting, no write)."); return 3; }

            if (!result.Ok)
            {
                Console.Error.WriteLine("Error: battle-anim decomp export failed:");
                foreach (var d in result.Diagnostics) Console.Error.WriteLine("  " + d);
                return 2;
            }

            // ---- Write the .s + sidecars (each path root-confined) ----
            var written = new List<string>();
            try
            {
                string dir = Path.GetDirectoryName(absOut);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                File.WriteAllText(absOut, result.Text);
                written.Add(absOut);

                string baseNoExt = Path.Combine(
                    Path.GetDirectoryName(absOut) ?? "",
                    Path.GetFileNameWithoutExtension(absOut));

                // Palette sidecars (.pal per 16-color team sub-palette).
                var pals = BattleAnimDecompExportCore.BuildPaletteSidecars(result.PaletteRaw);
                foreach (var p in pals)
                {
                    string palPath = baseNoExt + p.Suffix;
                    // Confine sidecar writes to the project root too (project mode).
                    string palAbs = ConfineSibling(project, absOut, palPath);
                    if (palAbs == null) { Console.Error.WriteLine("Error: sidecar path rejected (outside project root)"); return 2; }
                    File.WriteAllBytes(palAbs, p.PalBytes);
                    written.Add(palAbs);
                }

                // JSON manifest (manual-registration checklist + mode/oam/sheet map).
                string manifestPath = baseNoExt + ".json";
                string manifestAbs = ConfineSibling(project, absOut, manifestPath);
                if (manifestAbs == null) { Console.Error.WriteLine("Error: manifest path rejected (outside project root)"); return 2; }
                File.WriteAllText(manifestAbs, BattleAnimDecompExportCore.BuildManifestJson(result.Anime));
                written.Add(manifestAbs);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Error: failed to write output: {ex.Message}");
                return 2;
            }

            Console.WriteLine($"Exported battle animation '{BattleAnimDecompExportCore.SanitizeTag(tag)}' "
                + $"({result.ModeCount} modes, {result.FrameCount} frames, {result.OamEntryCount} OAM entries) to {absOut}");
            foreach (var w in written) if (w != absOut) Console.WriteLine("  sidecar: " + w);
            if (result.Diagnostics.Count > 0)
            {
                Console.WriteLine($"Diagnostics ({result.Diagnostics.Count}):");
                foreach (var d in result.Diagnostics) Console.WriteLine("  " + d);
            }
            return 0;
        }

        // Confine a sibling sidecar path to the project root (project mode), else
        // accept any writable absolute path (rom mode). Returns null if rejected.
        // In project mode, re-route through DecompAssetExportCore.ResolveSourcePath
        // (the SAME robust containment used for the main --out) by computing the
        // sibling's path RELATIVE to the project root — this rejects ..-escape /
        // absolute paths and avoids the weak StartsWith-prefix / case pitfalls
        // (Copilot review). The sidecar is always a sibling of the already-validated
        // main .s, so a valid main --out yields a valid sidecar path.
        static string ConfineSibling(DecompProject project, string mainAbsOut, string siblingAbs)
        {
            if (project == null) return Path.GetFullPath(siblingAbs);
            try
            {
                string rootFull = Path.GetFullPath(project.ProjectRoot);
                string siblingFull = Path.GetFullPath(siblingAbs);
                string rel = Path.GetRelativePath(rootFull, siblingFull);
                // GetRelativePath returns the input unchanged (absolute / different
                // root) or a ..-escaping path when outside the root — ResolveSourcePath
                // rejects both.
                if (Path.IsPathRooted(rel)
                    || rel.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                           .Length == 0)
                    return null;
                return DecompAssetExportCore.ResolveSourcePath(project, rel);
            }
            catch { return null; }
        }

        static bool TryParseHexAddr(string s, out uint result)
        {
            result = 0;
            if (string.IsNullOrEmpty(s)) return false;
            string clean = s.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? s.Substring(2) : s;
            if (!uint.TryParse(clean, System.Globalization.NumberStyles.HexNumber, null, out result))
                return false;
            result = U.toOffset(result);
            return true;
        }

        static int ParseNumberOrDefault(Dictionary<string, string> argsDic, int fallback)
        {
            if (argsDic.ContainsKey("--number") && !string.IsNullOrEmpty(argsDic["--number"])
                && int.TryParse(argsDic["--number"], out int n) && n >= 0)
                return n;
            return fallback;
        }

        static bool ByteArrayEquals(byte[] a, byte[] b)
        {
            if (a == null || b == null || a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) return false;
            return true;
        }

        static int RunExportAsset(Dictionary<string, string> argsDic)
        {
            // ---- Required: --kind ----
            if (!argsDic.ContainsKey("--kind") || string.IsNullOrEmpty(argsDic["--kind"]))
            { Console.Error.WriteLine("Error: --export-asset requires --kind=<graphics|palette|map|mapchange|mapanime2pal|objtiles|mapchipconfig|mapanime1gfx|text|shop>"); return 1; }
            string kind = argsDic["--kind"].ToLowerInvariant();

            // ---- Required: --out ----
            if (!argsDic.ContainsKey("--out") || string.IsNullOrEmpty(argsDic["--out"]))
            { Console.Error.WriteLine("Error: --export-asset requires --out=<path>"); return 1; }
            string outRel = argsDic["--out"];

            // ---- ROM source: --project or --rom ----
            bool isProject = argsDic.ContainsKey("--project") && !string.IsNullOrEmpty(argsDic["--project"]);
            bool isRom = argsDic.ContainsKey("--rom") && !string.IsNullOrEmpty(argsDic["--rom"]);
            if (!isProject && !isRom)
            { Console.Error.WriteLine("Error: --export-asset requires --rom=<path> or --project=<dir>"); return 1; }

            RomLoader.InitEnvironment();

            DecompProject project = null;
            if (isProject)
            {
                string projectDir = argsDic["--project"];
                if (!RomLoader.LoadProject(projectDir))
                    return 1;
                project = CoreState.DecompProject;
            }
            else
            {
                string romPath = argsDic["--rom"];
                if (!File.Exists(romPath))
                { Console.Error.WriteLine($"Error: ROM not found: {romPath}"); return 1; }
                string forceVersion = argsDic.ContainsKey("--force-version") ? argsDic["--force-version"] : null;
                if (!RomLoader.LoadRom(romPath, forceVersion))
                    return 1;
                RomLoader.InitFull();
            }

            // ---- Resolve output path ----
            string absOut = DecompAssetExportCore.ResolveSourcePath(project, outRel);
            if (absOut == null)
            {
                Console.Error.WriteLine("Error: output path rejected (outside project root or invalid)");
                return 2;
            }

            ROM rom = CoreState.ROM;

            // ---- Helper: parse hex address ----
            static bool TryParseAddr(string s, out uint result)
            {
                result = 0;
                if (string.IsNullOrEmpty(s)) return false;
                string clean = s.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? s.Substring(2) : s;
                if (!uint.TryParse(clean, System.Globalization.NumberStyles.HexNumber, null, out result))
                    return false;
                result = U.toOffset(result);
                return true;
            }

            // ---- Dispatch by kind ----
            DecompAssetResult result;

            switch (kind)
            {
                case "palette":
                {
                    if (!argsDic.ContainsKey("--addr") || string.IsNullOrEmpty(argsDic["--addr"]))
                    { Console.Error.WriteLine("Error: --export-asset --kind=palette requires --addr=<hex>"); return 1; }
                    if (!TryParseAddr(argsDic["--addr"], out uint addr))
                    { Console.Error.WriteLine($"Error: Invalid address: {argsDic["--addr"]}"); return 1; }
                    int colors = 16;
                    if (argsDic.ContainsKey("--colors") && !string.IsNullOrEmpty(argsDic["--colors"]))
                    {
                        if (!int.TryParse(argsDic["--colors"], out colors) || colors < 1 || colors > 256)
                        { Console.Error.WriteLine($"Error: --colors must be 1-256"); return 1; }
                    }
                    result = DecompAssetExportCore.ExportPalette(rom, addr, colors, absOut);
                    break;
                }

                case "graphics":
                {
                    if (!argsDic.ContainsKey("--addr") || string.IsNullOrEmpty(argsDic["--addr"]))
                    { Console.Error.WriteLine("Error: --export-asset --kind=graphics requires --addr=<hex>"); return 1; }
                    if (!argsDic.ContainsKey("--width") || string.IsNullOrEmpty(argsDic["--width"]))
                    { Console.Error.WriteLine("Error: --export-asset --kind=graphics requires --width=<int>"); return 1; }
                    if (!argsDic.ContainsKey("--height") || string.IsNullOrEmpty(argsDic["--height"]))
                    { Console.Error.WriteLine("Error: --export-asset --kind=graphics requires --height=<int>"); return 1; }
                    if (!argsDic.ContainsKey("--palette-addr") || string.IsNullOrEmpty(argsDic["--palette-addr"]))
                    { Console.Error.WriteLine("Error: --export-asset --kind=graphics requires --palette-addr=<hex>"); return 1; }
                    if (!TryParseAddr(argsDic["--addr"], out uint addr))
                    { Console.Error.WriteLine($"Error: Invalid --addr: {argsDic["--addr"]}"); return 1; }
                    if (!TryParseAddr(argsDic["--palette-addr"], out uint palAddr))
                    { Console.Error.WriteLine($"Error: Invalid --palette-addr: {argsDic["--palette-addr"]}"); return 1; }
                    if (!int.TryParse(argsDic["--width"], out int width) || width <= 0)
                    { Console.Error.WriteLine($"Error: Invalid --width: {argsDic["--width"]}"); return 1; }
                    if (!int.TryParse(argsDic["--height"], out int height) || height <= 0)
                    { Console.Error.WriteLine($"Error: Invalid --height: {argsDic["--height"]}"); return 1; }
                    int bpp = 4;
                    if (argsDic.ContainsKey("--bpp") && !string.IsNullOrEmpty(argsDic["--bpp"]))
                    {
                        if (!int.TryParse(argsDic["--bpp"], out bpp) || (bpp != 4 && bpp != 8))
                        { Console.Error.WriteLine("Error: --bpp must be 4 or 8"); return 1; }
                    }
                    int colors = 16;
                    if (argsDic.ContainsKey("--colors") && !string.IsNullOrEmpty(argsDic["--colors"]))
                    {
                        if (!int.TryParse(argsDic["--colors"], out colors) || colors < 1 || colors > 256)
                        { Console.Error.WriteLine("Error: --colors must be 1-256"); return 1; }
                    }
                    bool compressed = argsDic.ContainsKey("--compressed");
                    result = DecompAssetExportCore.ExportGraphics(rom, addr, width, height, bpp, compressed, palAddr, colors, absOut);
                    break;
                }

                case "map":
                {
                    if (!argsDic.ContainsKey("--addr") || string.IsNullOrEmpty(argsDic["--addr"]))
                    { Console.Error.WriteLine("Error: --export-asset --kind=map requires --addr=<hex>"); return 1; }
                    if (!TryParseAddr(argsDic["--addr"], out uint addr))
                    { Console.Error.WriteLine($"Error: Invalid address: {argsDic["--addr"]}"); return 1; }
                    result = DecompAssetExportCore.ExportMap(rom, addr, absOut);
                    break;
                }

                case "mapchange":
                case "mapchange-overlay":
                {
                    // Map-change OVERLAY tile data block (#1355): a RAW UNCOMPRESSED u16 LE array.
                    // --addr is the change_mar offset (record +8 pointer, dereferenced), plus dims.
                    if (!argsDic.ContainsKey("--addr") || string.IsNullOrEmpty(argsDic["--addr"]))
                    { Console.Error.WriteLine("Error: --export-asset --kind=mapchange requires --addr=<hex>"); return 1; }
                    if (!argsDic.ContainsKey("--width") || string.IsNullOrEmpty(argsDic["--width"]))
                    { Console.Error.WriteLine("Error: --export-asset --kind=mapchange requires --width=<int>"); return 1; }
                    if (!argsDic.ContainsKey("--height") || string.IsNullOrEmpty(argsDic["--height"]))
                    { Console.Error.WriteLine("Error: --export-asset --kind=mapchange requires --height=<int>"); return 1; }
                    if (!TryParseAddr(argsDic["--addr"], out uint addr))
                    { Console.Error.WriteLine($"Error: Invalid address: {argsDic["--addr"]}"); return 1; }
                    if (!int.TryParse(argsDic["--width"], out int mcWidth) || mcWidth <= 0)
                    { Console.Error.WriteLine($"Error: Invalid --width: {argsDic["--width"]}"); return 1; }
                    if (!int.TryParse(argsDic["--height"], out int mcHeight) || mcHeight <= 0)
                    { Console.Error.WriteLine($"Error: Invalid --height: {argsDic["--height"]}"); return 1; }
                    result = DecompAssetExportCore.ExportMapChange(rom, addr, mcWidth, mcHeight, absOut);
                    break;
                }

                case "mapanime2pal":
                case "map-tileanime2-palette":
                {
                    // Map tile-animation-2 PALETTE block (#1360): a RAW UNCOMPRESSED u16 LE array of
                    // `count` 15-bit GBA colors (count*2 bytes). --addr is the anime-2 entry +0 pointer
                    // (dereferenced) and --count is the entry +5 color count. The CLI takes EXPLICIT
                    // --addr/--count (no entry-index auto-resolve / owner guessing); srcAddr is provenance.
                    if (!argsDic.ContainsKey("--addr") || string.IsNullOrEmpty(argsDic["--addr"]))
                    { Console.Error.WriteLine("Error: --export-asset --kind=mapanime2pal requires --addr=<hex>"); return 1; }
                    if (!argsDic.ContainsKey("--count") || string.IsNullOrEmpty(argsDic["--count"]))
                    { Console.Error.WriteLine("Error: --export-asset --kind=mapanime2pal requires --count=<int>"); return 1; }
                    if (!TryParseAddr(argsDic["--addr"], out uint addr))
                    { Console.Error.WriteLine($"Error: Invalid address: {argsDic["--addr"]}"); return 1; }
                    if (!int.TryParse(argsDic["--count"], out int palCount) || palCount <= 0)
                    { Console.Error.WriteLine($"Error: Invalid --count: {argsDic["--count"]}"); return 1; }
                    result = DecompAssetExportCore.ExportMapAnime2Pal(rom, addr, palCount, absOut);
                    break;
                }

                case "objtiles":
                case "obj-tiles":
                case "obj":
                {
                    // OBJ tileset (#1371): LZ77-DECOMPRESS the block at --addr and write the DECOMPRESSED
                    // 4bpp payload as the source body (the decomp build re-compresses; FEBuilder's LZ77
                    // packer is non-canonical). --addr is the DEREFERENCED OBJ LZ77 stream address (NOT
                    // RomInfo.map_obj_pointer). The FE7 obj2 secondary tileset is a separate stream/address.
                    if (!argsDic.ContainsKey("--addr") || string.IsNullOrEmpty(argsDic["--addr"]))
                    { Console.Error.WriteLine("Error: --export-asset --kind=objtiles requires --addr=<hex> (the DEREFERENCED OBJ LZ77 stream address, NOT RomInfo.map_obj_pointer)"); return 1; }
                    if (!TryParseAddr(argsDic["--addr"], out uint addr))
                    { Console.Error.WriteLine($"Error: Invalid address: {argsDic["--addr"]}"); return 1; }
                    result = DecompAssetExportCore.ExportObjTiles(rom, addr, absOut);
                    break;
                }

                case "mapchipconfig":
                case "mapchip-config":
                case "chipconfig":
                {
                    // Map chipset TSA/config (#1375): LZ77-DECOMPRESS the block at --addr and write the
                    // DECOMPRESSED config payload as the source body (the decomp build re-compresses;
                    // FEBuilder's LZ77 packer is non-canonical). --addr is the DEREFERENCED config LZ77
                    // stream address (e.g. the CONFIG-PLIST pointer dereferenced, NOT RomInfo.map_config_pointer).
                    // FE7 split layouts use a separate per-plist --addr. NOT the anime-1/anime-2 entry
                    // tables, NOT the map-change record chain, NOT the .mar layout.
                    if (!argsDic.ContainsKey("--addr") || string.IsNullOrEmpty(argsDic["--addr"]))
                    { Console.Error.WriteLine("Error: --export-asset --kind=mapchipconfig requires --addr=<hex> (the DEREFERENCED config LZ77 stream address, NOT RomInfo.map_config_pointer)"); return 1; }
                    if (!TryParseAddr(argsDic["--addr"], out uint addr))
                    { Console.Error.WriteLine($"Error: Invalid address: {argsDic["--addr"]}"); return 1; }
                    result = DecompAssetExportCore.ExportMapChipConfig(rom, addr, absOut);
                    break;
                }

                case "mapanime1gfx":
                case "map-tileanime1-graphics":
                case "anime1gfx":
                {
                    // Map tile-animation-1 per-entry RAW 4bpp GRAPHICS block (#1389): a RAW UNCOMPRESSED
                    // 4bpp tile-byte block sized by the entry's +2 length (NOT LZ77 — the WF read/import/
                    // rebuild paths treat it as raw ImageToByte16Tile bytes / a rebuild IMG block). --addr
                    // is the anime-1 entry +4 graphics pointer (dereferenced; the inverse of anime-2's +0)
                    // and --length is the entry +2 byte length. The CLI takes EXPLICIT --addr/--length (no
                    // entry-index auto-resolve / owner guessing); srcAddr is provenance ONLY. NOT the
                    // anime-1 ENTRY/PLIST table, NOT LZ77, NOT the .mar layout.
                    if (!argsDic.ContainsKey("--addr") || string.IsNullOrEmpty(argsDic["--addr"]))
                    { Console.Error.WriteLine("Error: --export-asset --kind=mapanime1gfx requires --addr=<hex> (the DEREFERENCED anime-1 entry +4 graphics pointer)"); return 1; }
                    if (!argsDic.ContainsKey("--length") || string.IsNullOrEmpty(argsDic["--length"]))
                    { Console.Error.WriteLine("Error: --export-asset --kind=mapanime1gfx requires --length=<int> (the anime-1 entry +2 byte length)"); return 1; }
                    if (!TryParseAddr(argsDic["--addr"], out uint addr))
                    { Console.Error.WriteLine($"Error: Invalid address: {argsDic["--addr"]}"); return 1; }
                    if (!int.TryParse(argsDic["--length"], out int gfxLen) || gfxLen <= 0)
                    { Console.Error.WriteLine($"Error: Invalid --length: {argsDic["--length"]}"); return 1; }
                    result = DecompAssetExportCore.ExportMapAnime1Gfx(rom, addr, gfxLen, absOut);
                    break;
                }

                case "text":
                {
                    // --out is treated as a directory for text export
                    result = DecompAssetExportCore.ExportText(rom, absOut);
                    break;
                }

                case "shop":
                {
                    // --out is treated as a directory for shop export (#1149). Shops have no
                    // in-place C-array owner, so the decomp path is an EA .event migration
                    // artifact (shops.event) recreating each sentinel-terminated item list.
                    result = DecompAssetExportCore.ExportShops(rom, absOut);
                    break;
                }

                default:
                    Console.Error.WriteLine($"Error: Unknown --kind '{kind}'. Use: graphics, palette, map, mapchange, mapanime2pal, objtiles, mapchipconfig, mapanime1gfx, text, shop");
                    return 1;
            }

            // ---- Report result ----
            if (result.Ok)
            {
                foreach (string path in result.WrittenPaths)
                    Console.WriteLine($"Wrote: {path}");
                return 0;
            }
            else
            {
                Console.Error.WriteLine($"Error: {result.Message}");
                return result.Status == DecompAssetStatus.PathRejected ? 2 : 1;
            }
        }

        /// <summary>
        /// --write-source: source-backed table-entry writer (#1132). Opens a decomp
        /// project, then rewrites the owning C array element for a structured table
        /// entry's field instead of mutating the preview ROM. READ-ONLY w.r.t. the ROM;
        /// the only mutation is to the project's declared source file.
        ///
        /// Exit codes: 0 = source rewritten; 2 = ROM-only / manual / not owned /
        /// unsupported field / rejected / malformed manifest (advisory, no write);
        /// 1 = usage fault / parse failure / source-not-found / unexpected error.
        ///
        /// #1141: <c>--field</c>/<c>--value</c> are REPEATABLE — pair each
        /// <c>--field=X</c> with a FOLLOWING <c>--value=Y</c> in argv order (other flags may
        /// appear between them; a second <c>--field</c> before its <c>--value</c>, or an
        /// unpaired <c>--field</c>/<c>--value</c>, is a usage error). The dictionary
        /// collapses duplicates, so the raw argv is parsed for pairs. Last value wins on a
        /// duplicate field (a warning is printed). Signed fields are driven off the manifest
        /// <c>fields[].signed</c>; pass the two's-complement magnitude (decimal or 0x hex) —
        /// e.g. <c>--value=255</c> for an int8 -1.
        /// </summary>
        static int RunWriteSource(Dictionary<string, string> argsDic)
        {
            // ---- Required args ----
            string projectDir = argsDic.ContainsKey("--project") ? argsDic["--project"] : "";
            if (string.IsNullOrEmpty(projectDir))
            { Console.Error.WriteLine("Error: --write-source requires --project=<dir>"); return 1; }

            string table = argsDic.ContainsKey("--table") ? argsDic["--table"] : "";
            if (string.IsNullOrEmpty(table))
            { Console.Error.WriteLine("Error: --write-source requires --table=<name>"); return 1; }

            if (!argsDic.ContainsKey("--id") || string.IsNullOrEmpty(argsDic["--id"]))
            { Console.Error.WriteLine("Error: --write-source requires --id=<n>"); return 1; }
            if (!TryParseIntArg(argsDic["--id"], out int entryId) || entryId < 0)
            { Console.Error.WriteLine($"Error: Invalid --id: {argsDic["--id"]}"); return 1; }

            // ---- Field/value pairs (REPEATABLE; ordered from raw argv) ----
            if (!TryExtractFieldValuePairs(RawArgs, out var changedFields, out string pairErr))
            { Console.Error.WriteLine($"Error: {pairErr}"); return 1; }
            if (changedFields.Count == 0)
            { Console.Error.WriteLine("Error: --write-source requires at least one --field=<name> --value=<int> pair"); return 1; }

            // ---- Open the project (sets CoreState.DecompProject + loads built ROM) ----
            RomLoader.InitEnvironment();
            if (!RomLoader.LoadProject(projectDir))
                return 1;

            DecompProject project = CoreState.DecompProject;

            DecompSourceWriteResult res = DecompSourceWriterCore.WriteTableEntry(
                project, table, entryId, changedFields);

            if (res.Ok)
            {
                Console.WriteLine($"Source file: {res.SourceFile}");

                bool anyChanged = res.ChangedFields != null && res.ChangedFields.Count > 0;
                bool anySkipped = res.SkippedFields != null && res.SkippedFields.Count > 0;

                // ALL requested fields were SKIPPED (macro/expression) — nothing writable
                // matched, no source change. This is NOT a clean success: the user must edit
                // those fields manually. Exit 2 (advisory / manual-required) (#1159).
                if (!anyChanged && anySkipped)
                {
                    Console.Error.WriteLine($"No writable change: all requested field(s) map to a macro/expression and were skipped: {string.Join(", ", res.SkippedFields)}. Edit them manually.");
                    Console.WriteLine("NeedsRebuild=false");
                    return 2;
                }

                // A no-op (empty ChangedFields, nothing skipped) means the source already
                // matched the requested value(s): nothing was written, no rebuild is needed
                // (#1132 review finding 2). Report it honestly and skip the diff/rebuild.
                if (!anyChanged)
                {
                    Console.WriteLine("No change needed (source already matches the requested value).");
                    Console.WriteLine("NeedsRebuild=false");
                    return 0;
                }

                Console.WriteLine($"Entry {res.EntryId}, fields changed: {string.Join(", ", res.ChangedFields)}");
                Console.WriteLine($"Lines {res.ChangedLineStart}-{res.ChangedLineEnd}");
                Console.WriteLine("--- BEFORE ---");
                Console.WriteLine(res.BeforeText);
                Console.WriteLine("--- AFTER ---");
                Console.WriteLine(res.AfterText);

                // Optional --out-diff: a simple unified-diff-ish artifact of the element.
                if (argsDic.ContainsKey("--out-diff") && !string.IsNullOrEmpty(argsDic["--out-diff"]))
                {
                    try
                    {
                        string diff = BuildElementDiff(res);
                        File.WriteAllText(argsDic["--out-diff"], diff);
                        Console.WriteLine($"Diff written to: {argsDic["--out-diff"]}");
                    }
                    catch (Exception ex)
                    {
                        Console.Error.WriteLine($"Error: could not write --out-diff '{argsDic["--out-diff"]}': {ex.Message}");
                        return 1;
                    }
                }

                Console.WriteLine("NeedsRebuild=true");

                // PARTIAL write: some numeric field(s) were written to source, but other
                // requested field(s) map to a macro/expression and were SKIPPED. The source
                // IS modified (rebuild needed), but the skipped fields need manual edits — so
                // this is NOT a clean success. Warn + exit 2 (manual-required) (#1159).
                if (anySkipped)
                {
                    Console.Error.WriteLine($"WARNING: {res.SkippedFields.Count} requested field(s) were SKIPPED (macro/expression) and need manual edits: {string.Join(", ", res.SkippedFields)}");
                    return 2;
                }

                return 0;
            }

            // Failure: route to advisory (2) vs error (1) exit codes.
            switch (res.Status)
            {
                case DecompSourceWriteStatus.NotOwned:
                case DecompSourceWriteStatus.RomOnly:
                case DecompSourceWriteStatus.Manual:
                case DecompSourceWriteStatus.UnsupportedField:
                case DecompSourceWriteStatus.Rejected:
                case DecompSourceWriteStatus.MalformedManifest:
                case DecompSourceWriteStatus.NotDecompMode:
                    Console.Error.WriteLine($"ROM-only / manual / not owned: {res.Message}");
                    return 2;
                default:
                    Console.Error.WriteLine($"Error: {res.Message}");
                    return 1;
            }
        }

        /// <summary>
        /// CLI entry for the in-place source-backed SHOP-LIST writer (#1347). Opens a decomp
        /// project, resolves the owning u16-list declaration (by <c>--symbol</c> directly, or
        /// by resolving <c>--shop-addr</c> to a list symbol via the project's merged
        /// .map/.elf/.sym + manifest list-owner), then rewrites the owning variable-length
        /// <c>ITEM_NONE</c>-terminated C list instead of mutating the preview ROM. READ-ONLY
        /// w.r.t. the ROM; the only mutation is to the project's declared source file.
        ///
        /// Exit codes: 0 = source rewritten (or clean no-op); 2 = not owned / ROM-only /
        /// manual / unsupported field / rejected / malformed manifest (advisory, no write);
        /// 1 = usage fault / parse failure / source-not-found / unexpected error.
        /// </summary>
        static int RunWriteShop(Dictionary<string, string> argsDic)
        {
            // ---- Required: --project ----
            string projectDir = argsDic.ContainsKey("--project") ? argsDic["--project"] : "";
            if (string.IsNullOrEmpty(projectDir))
            { Console.Error.WriteLine("Error: --write-shop requires --project=<dir>"); return 1; }

            // ---- Owner selector: exactly one of --symbol / --shop-addr ----
            bool hasSymbol = argsDic.ContainsKey("--symbol") && !string.IsNullOrEmpty(argsDic["--symbol"]);
            bool hasShopAddr = argsDic.ContainsKey("--shop-addr") && !string.IsNullOrEmpty(argsDic["--shop-addr"]);
            if (!hasSymbol && !hasShopAddr)
            { Console.Error.WriteLine("Error: --write-shop requires one of --symbol=<name> or --shop-addr=0x..."); return 1; }
            if (hasSymbol && hasShopAddr)
            { Console.Error.WriteLine("Error: --write-shop takes EITHER --symbol or --shop-addr, not both"); return 1; }

            uint shopAddr = 0;
            if (hasShopAddr && !TryParseUIntArg(argsDic["--shop-addr"], out shopAddr))
            { Console.Error.WriteLine($"Error: Invalid --shop-addr: {argsDic["--shop-addr"]}"); return 1; }

            // ---- Required: --items=<csv> (may be empty for an emptied shop) ----
            if (!argsDic.ContainsKey("--items"))
            { Console.Error.WriteLine("Error: --write-shop requires --items=<id:qty,...> (empty = emptied shop)"); return 1; }
            if (!TryParseShopItems(argsDic["--items"], out ushort[] items, out string itemsErr))
            { Console.Error.WriteLine($"Error: {itemsErr}"); return 1; }

            // ---- Open the project (sets CoreState.DecompProject + loads built ROM) ----
            RomLoader.InitEnvironment();
            if (!RomLoader.LoadProject(projectDir))
                return 1;

            DecompProject project = CoreState.DecompProject;

            // ---- Resolve the list owner ----
            DecompTableEntry owner;
            string symbolName;
            if (hasSymbol)
            {
                symbolName = argsDic["--symbol"];
                owner = project.TryGetListOwner(symbolName);
            }
            else
            {
                IAsmMapFile map = CoreState.AsmMapFileAsmCache?.GetAsmMapFile();
                DecompShopSourceResolver.TryResolveShopOwner(project, map, shopAddr, out owner, out symbolName);
            }

            if (owner == null)
            {
                Console.Error.WriteLine("Not owned: no list-owner declared/resolved for this shop. Use --export-asset --kind=shop to migrate.");
                return 2;
            }

            // ---- Rewrite the owning source list ----
            DecompSourceWriteResult res = DecompSourceWriterCore.WriteListEntries(project, owner, items);

            if (res.Ok)
            {
                Console.WriteLine($"Source file: {res.SourceFile}");

                bool anyChanged = res.ChangedFields != null && res.ChangedFields.Count > 0;
                if (!anyChanged)
                {
                    Console.WriteLine("No change needed (source already matches the requested list).");
                    Console.WriteLine("NeedsRebuild=false");
                    return 0;
                }

                Console.WriteLine($"Shop list: {(string.IsNullOrEmpty(symbolName) ? owner.EffectiveSymbol : symbolName)}, {items.Length} item(s)");
                Console.WriteLine($"Lines {res.ChangedLineStart}-{res.ChangedLineEnd}");
                Console.WriteLine("--- BEFORE ---");
                Console.WriteLine(res.BeforeText);
                Console.WriteLine("--- AFTER ---");
                Console.WriteLine(res.AfterText);
                Console.WriteLine("NeedsRebuild=true");
                return 0;
            }

            // Failure: route to advisory (2) vs error (1) exit codes.
            switch (res.Status)
            {
                case DecompSourceWriteStatus.NotOwned:
                case DecompSourceWriteStatus.RomOnly:
                case DecompSourceWriteStatus.Manual:
                case DecompSourceWriteStatus.UnsupportedField:
                case DecompSourceWriteStatus.Rejected:
                case DecompSourceWriteStatus.MalformedManifest:
                case DecompSourceWriteStatus.NotDecompMode:
                    Console.Error.WriteLine($"Not owned / ROM-only / manual: {res.Message}");
                    return 2;
                default:
                    Console.Error.WriteLine($"Error: {res.Message}");
                    return 1;
            }
        }

        /// <summary>
        /// Parse the <c>--items</c> CSV (<c>id:qty,id:qty,...</c>) into a packed <c>ushort[]</c>
        /// where each element is <c>(qty &lt;&lt; 8) | id</c> (little-endian u16: low byte = item
        /// id, high byte = quantity — matching the ROM itemId,quantity byte pair). id/qty are
        /// hex-or-dec, each 0..255; id must be non-zero (0 == terminator). An EMPTY csv yields an
        /// empty array (an emptied shop). NEVER throws.
        /// </summary>
        static bool TryParseShopItems(string csv, out ushort[] items, out string error)
        {
            items = Array.Empty<ushort>();
            error = "";
            csv = (csv ?? "").Trim();
            if (csv.Length == 0)
                return true;   // emptied shop

            string[] parts = csv.Split(',');
            var list = new List<ushort>(parts.Length);
            foreach (string raw in parts)
            {
                string pair = raw.Trim();
                if (pair.Length == 0) continue;   // tolerate trailing/double commas
                string[] kv = pair.Split(':');
                if (kv.Length != 2)
                { error = $"Invalid --items entry '{pair}' (expected id:qty)"; items = Array.Empty<ushort>(); return false; }

                if (!TryParseUIntArg(kv[0].Trim(), out uint id))
                { error = $"Invalid item id '{kv[0].Trim()}'"; items = Array.Empty<ushort>(); return false; }
                if (!TryParseUIntArg(kv[1].Trim(), out uint qty))
                { error = $"Invalid quantity '{kv[1].Trim()}'"; items = Array.Empty<ushort>(); return false; }

                if (id == 0 || id > 0xFF)
                { error = $"Item id {kv[0].Trim()} out of range (1..255; 0 == ITEM_NONE terminator)"; items = Array.Empty<ushort>(); return false; }
                if (qty > 0xFF)
                { error = $"Quantity {kv[1].Trim()} out of range (0..255)"; items = Array.Empty<ushort>(); return false; }

                list.Add((ushort)((qty << 8) | id));
            }
            items = list.ToArray();
            return true;
        }

        // ---- --write-source helpers ----

        static bool TryParseIntArg(string s, out int result)
        {
            result = 0;
            if (string.IsNullOrEmpty(s)) return false;
            if (s.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                return int.TryParse(s.Substring(2), System.Globalization.NumberStyles.HexNumber, null, out result);
            return int.TryParse(s, out result);
        }

        static bool TryParseUIntArg(string s, out uint result)
        {
            result = 0;
            if (string.IsNullOrEmpty(s)) return false;
            if (s.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                return uint.TryParse(s.Substring(2), System.Globalization.NumberStyles.HexNumber, null, out result);
            return uint.TryParse(s, out result);
        }

        /// <summary>
        /// Extract ordered <c>--field=X --value=Y</c> pairs from the raw argv (#1141).
        /// Each <c>--field</c> must be paired with a FOLLOWING <c>--value</c> (in argv
        /// order); other flags may appear between them — only a SECOND <c>--field</c>
        /// arriving before its <c>--value</c>, an unpaired trailing <c>--field</c>, or a
        /// <c>--value</c> with no preceding <c>--field</c>, is a usage error. Last value
        /// wins on a duplicate field (a warning is printed to stderr). Supports both
        /// <c>--field=X</c> and <c>--field X</c> spellings.
        /// </summary>
        static bool TryExtractFieldValuePairs(
            string[] args, out Dictionary<string, uint> result, out string error)
        {
            result = new Dictionary<string, uint>(StringComparer.Ordinal);
            error = "";
            string pendingField = null;

            for (int i = 0; i < args.Length; i++)
            {
                string a = args[i];
                if (!a.StartsWith("--field", StringComparison.Ordinal)
                    && !a.StartsWith("--value", StringComparison.Ordinal))
                    continue;

                bool isField = a.StartsWith("--field", StringComparison.Ordinal);
                bool isValue = a.StartsWith("--value", StringComparison.Ordinal);
                // Only accept exact "--field"/"--value" or "--field=..."/"--value=..."
                string key = isField ? "--field" : "--value";
                if (a.Length != key.Length && (a.Length <= key.Length || a[key.Length] != '='))
                    continue;   // e.g. --fieldfoo — not our flag

                // Resolve the value for this flag (inline =VALUE, or next argv token).
                string val;
                int eq = a.IndexOf('=');
                if (eq >= 0)
                {
                    val = a.Substring(eq + 1);
                }
                else
                {
                    if (i + 1 >= args.Length || args[i + 1].StartsWith("-", StringComparison.Ordinal))
                    { error = $"{key} requires a value"; return false; }
                    val = args[i + 1];
                    i++;
                }

                if (isField)
                {
                    if (pendingField != null)
                    { error = $"--field={pendingField} has no matching --value"; return false; }
                    if (string.IsNullOrEmpty(val))
                    { error = "--field requires a non-empty name"; return false; }
                    pendingField = val;
                }
                else // isValue
                {
                    if (pendingField == null)
                    { error = "--value has no preceding --field"; return false; }
                    if (!TryParseUIntArg(val, out uint num))
                    { error = $"Invalid --value: {val}"; return false; }
                    if (result.ContainsKey(pendingField))
                        Console.Error.WriteLine($"Warning: --field={pendingField} given twice; last value ({num}) wins.");
                    result[pendingField] = num;
                    pendingField = null;
                }
            }

            if (pendingField != null)
            { error = $"--field={pendingField} has no matching --value"; return false; }
            return true;
        }

        static string BuildElementDiff(DecompSourceWriteResult res)
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"--- {res.SourceFile} (before) entry {res.EntryId}");
            sb.AppendLine($"+++ {res.SourceFile} (after)  entry {res.EntryId}");
            foreach (string line in (res.BeforeText ?? "").Replace("\r\n", "\n").Split('\n'))
                sb.AppendLine("-" + line);
            foreach (string line in (res.AfterText ?? "").Replace("\r\n", "\n").Split('\n'))
                sb.AppendLine("+" + line);
            return sb.ToString();
        }

        static int RunExportPalette(Dictionary<string, string> argsDic)
        {
            if (!argsDic.ContainsKey("--rom") || string.IsNullOrEmpty(argsDic["--rom"]))
            { Console.Error.WriteLine("Error: --export-palette requires --rom=<rom>"); return 1; }
            if (!argsDic.ContainsKey("--addr") || string.IsNullOrEmpty(argsDic["--addr"]))
            { Console.Error.WriteLine("Error: --export-palette requires --addr=<hex>"); return 1; }
            if (!argsDic.ContainsKey("--out") || string.IsNullOrEmpty(argsDic["--out"]))
            { Console.Error.WriteLine("Error: --export-palette requires --out=<path>"); return 1; }

            string romPath = argsDic["--rom"];
            string outPath = argsDic["--out"];

            if (!File.Exists(romPath))
            { Console.Error.WriteLine($"Error: ROM not found: {romPath}"); return 1; }

            // Parse address (supports both raw offsets and 0x08xxxxxx GBA pointers)
            string addrStr = argsDic["--addr"];
            if (addrStr.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                addrStr = addrStr.Substring(2);
            if (!uint.TryParse(addrStr, System.Globalization.NumberStyles.HexNumber, null, out uint addr))
            { Console.Error.WriteLine($"Error: Invalid hex address: {argsDic["--addr"]}"); return 1; }
            addr = U.toOffset(addr);

            // Parse color count (max 256 for GBA palette)
            int colors = 16;
            if (argsDic.ContainsKey("--colors") && !string.IsNullOrEmpty(argsDic["--colors"]))
            {
                if (!int.TryParse(argsDic["--colors"], out colors) || colors <= 0 || colors > 256)
                { Console.Error.WriteLine($"Error: Invalid color count (must be 1-256): {argsDic["--colors"]}"); return 1; }
            }

            byte[] romData = File.ReadAllBytes(romPath);
            int byteCount = colors * 2; // 2 bytes per color (BGR555)
            if (addr + byteCount > romData.Length)
            { Console.Error.WriteLine($"Error: Address 0x{addr:X} + {byteCount} bytes exceeds ROM size ({romData.Length})."); return 1; }

            // Extract raw palette bytes
            byte[] rawPalette = new byte[byteCount];
            Array.Copy(romData, addr, rawPalette, 0, byteCount);

            // Determine format from output file extension — only accept known palette extensions
            string ext = Path.GetExtension(outPath);
            string extLower = (ext ?? "").TrimStart('.').ToLowerInvariant();
            if (extLower != "pal" && extLower != "act" && extLower != "gpl" && extLower != "txt" && extLower != "gbapal")
            { Console.Error.WriteLine($"Error: Unsupported output extension '{ext}'. Use .pal (JASC), .act (ACT), .gpl (GIMP), .txt (Hex), or .gbapal (raw)."); return 1; }
            PaletteFormat format = PaletteFormatConverter.FormatFromExtension(ext);

            try
            {
                byte[] output = PaletteFormatConverter.ExportToFormat(rawPalette, format);
                File.WriteAllBytes(outPath, output);
                Console.WriteLine($"Exported {colors} colors from 0x{addr:X} as {format} to {outPath}");
                return 0;
            }
            catch (ArgumentException ex)
            {
                Console.Error.WriteLine($"Error: {ex.Message}");
                return 1;
            }
        }

        static int RunImportPalette(Dictionary<string, string> argsDic)
        {
            if (!argsDic.ContainsKey("--rom") || string.IsNullOrEmpty(argsDic["--rom"]))
            { Console.Error.WriteLine("Error: --import-palette requires --rom=<rom>"); return 1; }
            if (!argsDic.ContainsKey("--addr") || string.IsNullOrEmpty(argsDic["--addr"]))
            { Console.Error.WriteLine("Error: --import-palette requires --addr=<hex>"); return 1; }
            if (!argsDic.ContainsKey("--in") || string.IsNullOrEmpty(argsDic["--in"]))
            { Console.Error.WriteLine("Error: --import-palette requires --in=<path>"); return 1; }

            string romPath = argsDic["--rom"];
            string inPath = argsDic["--in"];

            if (!File.Exists(romPath))
            { Console.Error.WriteLine($"Error: ROM not found: {romPath}"); return 1; }
            if (!File.Exists(inPath))
            { Console.Error.WriteLine($"Error: Input file not found: {inPath}"); return 1; }

            // Parse address (supports both raw offsets and 0x08xxxxxx GBA pointers)
            string addrStr = argsDic["--addr"];
            if (addrStr.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                addrStr = addrStr.Substring(2);
            if (!uint.TryParse(addrStr, System.Globalization.NumberStyles.HexNumber, null, out uint addr))
            { Console.Error.WriteLine($"Error: Invalid hex address: {argsDic["--addr"]}"); return 1; }
            addr = U.toOffset(addr);

            byte[] fileData = File.ReadAllBytes(inPath);
            string ext = Path.GetExtension(inPath);

            // Strip UTF-8 BOM if present so content-based detection works correctly
            if (fileData.Length >= 3 && fileData[0] == 0xEF && fileData[1] == 0xBB && fileData[2] == 0xBF)
            {
                byte[] stripped = new byte[fileData.Length - 3];
                Array.Copy(fileData, 3, stripped, 0, stripped.Length);
                fileData = stripped;
            }

            // Detect format using content-based detection + extension hints.
            PaletteFormat format = PaletteFormatConverter.DetectFormat(fileData, ext);

            // If DetectFormat returned GbaRaw but the file has a known palette extension,
            // fall back to extension-based detection (handles BOM-prefixed JASC files, etc.)
            // If DetectFormat returns GbaRaw, only override for unambiguous binary extensions.
            // .pal and .txt are already handled by DetectFormat's content sniffing
            // (JASC header, hex text patterns). Since we strip UTF-8 BOM above,
            // BOM-prefixed JASC .pal files are detected correctly by content.
            if (format == PaletteFormat.GbaRaw)
            {
                string extLower = (ext ?? "").TrimStart('.').ToLowerInvariant();
                switch (extLower)
                {
                    case "pal": break; // DetectFormat already checked for JASC header; keep GbaRaw
                    case "act": format = PaletteFormat.AdobeAct; break; // ACT is always binary, no header
                    case "gpl": format = PaletteFormat.GimpGpl; break; // DetectFormat checks header but may miss edge cases
                    case "txt": break; // DetectFormat already checked for hex text; keep GbaRaw
                    case "gbapal": break; // Explicitly raw
                    default:
                        Console.Error.WriteLine($"Error: Unsupported input extension '{ext}'. Use .pal (JASC), .act (ACT), .gpl (GIMP), .txt (Hex), or .gbapal (raw).");
                        return 1;
                }
            }

            // Validate GbaRaw: reject files that are clearly not raw palette data
            if (format == PaletteFormat.GbaRaw)
            {
                if (fileData.Length == 0)
                { Console.Error.WriteLine("Error: Input file is empty."); return 1; }
                if (fileData.Length % 2 != 0)
                { Console.Error.WriteLine($"Error: Input file has odd byte count ({fileData.Length}), not valid raw GBA palette (2 bytes per color). Use .pal, .act, .gpl, or .txt format."); return 1; }
            }

            try
            {
                byte[] gbaPalette = PaletteFormatConverter.ImportFromFormat(fileData, format);
                int importedColors = gbaPalette.Length / 2;

                // For JASC/GPL text formats, validate declared vs actual color count
                if (format == PaletteFormat.JascPal || format == PaletteFormat.GimpGpl)
                {
                    string text = System.Text.Encoding.UTF8.GetString(fileData);
                    string[] lines = text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                    if (format == PaletteFormat.JascPal && lines.Length >= 3 &&
                        int.TryParse(lines[2].Trim(), out int declaredCount) &&
                        importedColors < declaredCount)
                    {
                        Console.Error.WriteLine($"Error: JASC-PAL declares {declaredCount} colors but only {importedColors} were found. File appears truncated.");
                        return 1;
                    }
                }

                // Validate palette size (GBA max: 256 colors = 512 bytes)
                if (gbaPalette.Length > 512)
                { Console.Error.WriteLine($"Error: Palette too large ({gbaPalette.Length / 2} colors). GBA palettes support at most 256 colors."); return 1; }
                if (gbaPalette.Length == 0 || gbaPalette.Length % 2 != 0)
                { Console.Error.WriteLine($"Error: Invalid palette data (size={gbaPalette.Length})."); return 1; }

                byte[] romData = File.ReadAllBytes(romPath);
                if (addr + gbaPalette.Length > romData.Length)
                { Console.Error.WriteLine($"Error: Address 0x{addr:X} + {gbaPalette.Length} bytes exceeds ROM size ({romData.Length})."); return 1; }

                Array.Copy(gbaPalette, 0, romData, addr, gbaPalette.Length);
                File.WriteAllBytes(romPath, romData);

                int colorCount = gbaPalette.Length / 2;
                Console.WriteLine($"Imported {colorCount} colors from {inPath} ({format}) to 0x{addr:X} in {romPath}");
                return 0;
            }
            catch (Exception ex) when (ex is ArgumentException || ex is FormatException || ex is OverflowException)
            {
                Console.Error.WriteLine($"Error: {ex.Message}");
                return 1;
            }
        }
    }
}
