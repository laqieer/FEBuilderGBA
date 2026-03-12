using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Dialogs;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class ToolASMEditView : Window, IEditorView
    {
        readonly ToolASMEditViewViewModel _vm = new();
        readonly UndoService _undoService = new();
        public string ViewTitle => "ASM Edit";
        public bool IsLoaded => _vm.IsLoaded;

        public ToolASMEditView()
        {
            InitializeComponent();
            DataContext = _vm;
            _vm.IsLoading = true;
            _vm.Initialize();
            _vm.IsLoading = false;
            _vm.MarkClean();
        }

        /// <summary>
        /// Try to find arm-none-eabi-as in well-known locations or PATH.
        /// </summary>
        static string? FindAssembler()
        {
            // Check config first
            var cfg = CoreState.Config;
            if (cfg != null)
            {
                string cfgPath = cfg.at("devkitpro_eabi", "");
                if (!string.IsNullOrEmpty(cfgPath) && File.Exists(cfgPath))
                    return cfgPath;
            }

            string exeName = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
                ? "arm-none-eabi-as.exe"
                : "arm-none-eabi-as";

            // Check PATH
            string? pathEnv = Environment.GetEnvironmentVariable("PATH");
            if (pathEnv != null)
            {
                char sep = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? ';' : ':';
                foreach (string dir in pathEnv.Split(sep))
                {
                    string candidate = Path.Combine(dir, exeName);
                    if (File.Exists(candidate))
                        return candidate;
                }
            }

            // Check devkitARM standard locations
            string? devkitArm = Environment.GetEnvironmentVariable("DEVKITARM");
            if (!string.IsNullOrEmpty(devkitArm))
            {
                string candidate = Path.Combine(devkitArm, "bin", exeName);
                if (File.Exists(candidate))
                    return candidate;
            }

            return null;
        }

        void Compile_Click(object? sender, RoutedEventArgs e)
        {
            var rom = CoreState.ROM;
            if (rom == null)
            {
                _ = MessageBoxWindow.Show(this, "No ROM loaded.", "Error", MessageBoxMode.Ok);
                return;
            }

            var asmTextBox = this.FindControl<TextBox>("AsmCodeTextBox");
            string code = asmTextBox?.Text ?? _vm.AsmCode;
            if (string.IsNullOrWhiteSpace(code))
            {
                _ = MessageBoxWindow.Show(this, "No ASM code to compile.", "Error", MessageBoxMode.Ok);
                return;
            }

            string? assemblerPath = FindAssembler();
            if (assemblerPath == null)
            {
                _ = MessageBoxWindow.Show(this,
                    "ASM compiler not found.\n\n" +
                    "Please install devkitARM and ensure arm-none-eabi-as is in your PATH,\n" +
                    "or set the path in Options > devkitpro_eabi.",
                    "ASM Compiler Not Found", MessageBoxMode.Ok);
                return;
            }

            // Parse target address from the code (look for ".equ origin, 0xNNNNNNNN")
            uint targetAddr = 0x100; // default safe minimum
            foreach (string line in code.Split('\n'))
            {
                string trimmed = line.Trim();
                if (trimmed.StartsWith(".equ origin,", StringComparison.OrdinalIgnoreCase))
                {
                    string addrStr = trimmed.Substring(12).Trim();
                    targetAddr = U.atoh(addrStr);
                    break;
                }
            }

            if (targetAddr < 0x100)
            {
                _ = MessageBoxWindow.Show(this,
                    "Cannot write to address below 0x100 (ROM header area).",
                    "Error", MessageBoxMode.Ok);
                return;
            }
            uint offset = U.toOffset(targetAddr);

            string tempAsm = Path.GetTempFileName();
            string tempObj = Path.ChangeExtension(tempAsm, ".o");
            string tempBin = Path.ChangeExtension(tempAsm, ".bin");

            _undoService.Begin("ASM Compile");
            try
            {
                File.WriteAllText(tempAsm, code);

                // Step 1: Assemble
                string objcopyExe = assemblerPath.Replace("arm-none-eabi-as", "arm-none-eabi-objcopy");
                var psi = new ProcessStartInfo(assemblerPath, $"-o \"{tempObj}\" \"{tempAsm}\"")
                {
                    UseShellExecute = false,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };
                using (var proc = Process.Start(psi))
                {
                    if (proc == null) throw new Exception("Failed to start assembler process.");
                    string stderr = proc.StandardError.ReadToEnd();
                    proc.WaitForExit(30000);
                    if (proc.ExitCode != 0)
                        throw new Exception($"Assembly failed:\n{stderr}");
                }

                // Step 2: Extract binary with objcopy
                var psi2 = new ProcessStartInfo(objcopyExe, $"-O binary \"{tempObj}\" \"{tempBin}\"")
                {
                    UseShellExecute = false,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };
                using (var proc2 = Process.Start(psi2))
                {
                    if (proc2 == null) throw new Exception("Failed to start objcopy process.");
                    string stderr2 = proc2.StandardError.ReadToEnd();
                    proc2.WaitForExit(30000);
                    if (proc2.ExitCode != 0)
                        throw new Exception($"objcopy failed:\n{stderr2}");
                }

                if (!File.Exists(tempBin))
                    throw new Exception("Binary output file not produced.");

                byte[] bin = File.ReadAllBytes(tempBin);
                if (bin.Length == 0)
                    throw new Exception("Compiled binary is empty.");

                // Write to ROM
                rom.write_range(offset, bin);
                _undoService.Commit();

                _ = MessageBoxWindow.Show(this,
                    $"Successfully wrote {bin.Length} bytes at 0x{offset:X08}.",
                    "ASM Compile", MessageBoxMode.Ok);
            }
            catch (Exception ex)
            {
                _undoService.Rollback();
                Log.Error("ToolASMEditView.Compile", ex.ToString());
                _ = MessageBoxWindow.Show(this, ex.Message, "Compile Error", MessageBoxMode.Ok);
            }
            finally
            {
                try { if (File.Exists(tempAsm)) File.Delete(tempAsm); } catch { }
                try { if (File.Exists(tempObj)) File.Delete(tempObj); } catch { }
                try { if (File.Exists(tempBin)) File.Delete(tempBin); } catch { }
            }
        }
        void Close_Click(object? sender, RoutedEventArgs e) => Close();

        public void NavigateTo(uint address) { }
        public void SelectFirstItem() { }
    }
}
