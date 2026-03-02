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

            // Other commands not yet implemented
            Console.Error.WriteLine("Command not yet supported in cross-platform CLI.");
            Console.Error.WriteLine("Supported commands: --version, --help, --makeups, --applyups, --lint, --disasm");
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
            Console.WriteLine();
            Console.WriteLine("Examples:");
            Console.WriteLine("  FEBuilderGBA.CLI --version");
            Console.WriteLine("  FEBuilderGBA.CLI --makeups=patch.ups --rom=modified.gba --fromrom=original.gba");
            Console.WriteLine("  FEBuilderGBA.CLI --applyups=output.gba --rom=original.gba --patch=patch.ups");
            Console.WriteLine("  FEBuilderGBA.CLI --lint --rom=rom.gba");
            Console.WriteLine("  FEBuilderGBA.CLI --disasm=output.asm --rom=rom.gba");
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
