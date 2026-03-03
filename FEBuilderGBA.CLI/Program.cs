using System;
using System.Collections.Generic;
using System.IO;
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

            if (argsDic.ContainsKey("--translate"))
            {
                return RunTranslate(argsDic);
            }

            // Other commands not yet implemented
            Console.Error.WriteLine("Command not yet supported in cross-platform CLI.");
            Console.Error.WriteLine("Run with --help for usage information.");
            return 1;
        }

        static void PrintVersion()
        {
            var asm = typeof(U).Assembly;
            var name = asm.GetName().Name;
            var ver = asm.GetName().Version;
            var baseDate = new DateTime(2000, 1, 1);
            string version = baseDate.AddDays(ver.Build).AddSeconds(ver.Revision * 2).ToString("yyyyMMdd.HH");
            Console.WriteLine($"{name} Version:{version}");
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
            Console.WriteLine("  --makeups=<path>         Create UPS patch (requires --rom and --fromrom)");
            Console.WriteLine("  --applyups=<path>        Apply UPS patch (requires --rom and --patch)");
            Console.WriteLine("  --lint                   Run lint checks on ROM (requires --rom)");
            Console.WriteLine("  --disasm=<path>          Disassemble ROM to file (requires --rom)");
            Console.WriteLine("  --decreasecolor          Quantize image palette (requires --in, --out, --paletteno)");
            Console.WriteLine("  --pointercalc            Search pointer references (requires --rom, --target, --address)");
            Console.WriteLine("  --rebuild                Rebuild/defragment ROM (requires --rom, --fromrom)");
            Console.WriteLine("  --songexchange           Copy song between ROMs (requires --rom, --fromrom, --fromsong, --tosong)");
            Console.WriteLine("  --convertmap1picture     Convert image to map tiles (requires --in, --outImg, --outTSA)");
            Console.WriteLine("  --translate              Translate ROM text (not yet fully supported)");
            Console.WriteLine();
            Console.WriteLine("Examples:");
            Console.WriteLine("  FEBuilderGBA.CLI --version");
            Console.WriteLine("  FEBuilderGBA.CLI --makeups=patch.ups --rom=modified.gba --fromrom=original.gba");
            Console.WriteLine("  FEBuilderGBA.CLI --applyups=output.gba --rom=original.gba --patch=patch.ups");
            Console.WriteLine("  FEBuilderGBA.CLI --lint --rom=rom.gba");
            Console.WriteLine("  FEBuilderGBA.CLI --disasm=output.asm --rom=rom.gba");
            Console.WriteLine("  FEBuilderGBA.CLI --decreasecolor --in=input.png --out=output.png --paletteno=16");
            Console.WriteLine("  FEBuilderGBA.CLI --pointercalc --rom=source.gba --target=target.gba --address=0x100,0x200");
            Console.WriteLine("  FEBuilderGBA.CLI --rebuild --rom=modified.gba --fromrom=original.gba");
            Console.WriteLine("  FEBuilderGBA.CLI --songexchange --rom=dest.gba --fromrom=source.gba --fromsong=0x1A --tosong=0x1A");
            Console.WriteLine("  FEBuilderGBA.CLI --convertmap1picture --in=map.png --outImg=tiles.bin --outTSA=tsa.bin");
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

            if (!argsDic.ContainsKey("--fromrom") || string.IsNullOrEmpty(argsDic["--fromrom"]))
            {
                Console.Error.WriteLine("Error: --makeups requires --fromrom=<original_rom>");
                return 1;
            }

            string modifiedRomPath = argsDic["--rom"];
            string originalRomPath = argsDic["--fromrom"];

            if (!File.Exists(modifiedRomPath))
            {
                Console.Error.WriteLine($"Error: Modified ROM not found: {modifiedRomPath}");
                return 1;
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
            if (!RomLoader.LoadRom(romPath))
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
            if (!RomLoader.LoadRom(romPath))
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

            var result = DecreaseColorCore.Quantize(rgba, width, height, maxColors);
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
            if (!argsDic.ContainsKey("--fromrom") || string.IsNullOrEmpty(argsDic["--fromrom"]))
            {
                Console.Error.WriteLine("Error: --rebuild requires --fromrom=<vanilla_rom>");
                return 1;
            }

            string modifiedPath = argsDic["--rom"];
            string vanillaPath = argsDic["--fromrom"];

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
            if (!RomLoader.LoadRom(destPath))
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
            // Stub: Translation requires heavily WinForms-coupled logic that has not yet been
            // extracted to Core. This is marked as a stretch goal.
            Console.Error.WriteLine("The --translate command is not yet fully supported in the cross-platform CLI.");
            Console.Error.WriteLine("Translation requires complex text encoding logic that is still being extracted.");
            Console.Error.WriteLine("For now, please use the WinForms GUI version for translation tasks.");
            return 1;
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
                        dic[arg] = "";
                    }
                }
                else if (arg == "-h")
                {
                    dic["--help"] = "";
                }
            }
            return dic;
        }
    }
}
