using global::Avalonia;
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
    public partial class ToolASMEditView : TranslatedUserControl, IEmbeddableEditor
    {
        readonly ToolASMEditViewViewModel _vm = new();
        readonly UndoService _undoService = new();
        bool _isCompiling;
        public string ViewTitle => "ASM Edit";
        public new bool IsLoaded => _vm.IsLoaded;
        public EditorDescriptor Descriptor => new("ASM Edit", 1016, 510, SizeToContent: global::Avalonia.Controls.SizeToContent.WidthAndHeight);
        public event EventHandler? CloseRequested;
        public void RequestClose() => CloseRequested?.Invoke(this, EventArgs.Empty);

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

        async void Compile_Click(object? sender, RoutedEventArgs e)
        {
            if (_isCompiling) return;

            var rom = CoreState.ROM;
            if (rom == null)
            {
                _ = MessageBoxWindow.Show(TopLevel.GetTopLevel(this) as Window, "No ROM loaded.", "Error", MessageBoxMode.Ok);
                return;
            }

            var asmTextBox = this.FindControl<TextBox>("AsmCodeTextBox");
            string code = asmTextBox?.Text ?? _vm.AsmCode;
            if (string.IsNullOrWhiteSpace(code))
            {
                _ = MessageBoxWindow.Show(TopLevel.GetTopLevel(this) as Window, "No ASM code to compile.", "Error", MessageBoxMode.Ok);
                return;
            }

            string? assemblerPath = FindAssembler();
            if (assemblerPath == null)
            {
                _ = MessageBoxWindow.Show(TopLevel.GetTopLevel(this) as Window,
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
                _ = MessageBoxWindow.Show(TopLevel.GetTopLevel(this) as Window,
                    "Cannot write to address below 0x100 (ROM header area).",
                    "Error", MessageBoxMode.Ok);
                return;
            }
            uint offset = U.toOffset(targetAddr);

            string tempAsm = Path.GetTempFileName();
            string tempObj = Path.ChangeExtension(tempAsm, ".o");
            string tempBin = Path.ChangeExtension(tempAsm, ".bin");

            _isCompiling = true;
            if (sender is Control compileButton)
                compileButton.IsEnabled = false;

            _undoService.Begin("ASM Compile");
            try
            {
                File.WriteAllText(tempAsm, code);

                // Step 1: Assemble
                string objcopyExe = assemblerPath.Replace("arm-none-eabi-as", "arm-none-eabi-objcopy");
                var assemble = await ExternalLauncher.Current.RunCapturedProcessAsync(new ExternalProcessRequest(assemblerPath)
                {
                    Arguments = $"-o \"{tempObj}\" \"{tempAsm}\"",
                    Timeout = TimeSpan.FromSeconds(30),
                    CaptureStandardOutput = false,
                    CaptureStandardError = true,
                });
                if (assemble.Kind == ExternalProcessResultKind.Unsupported)
                    throw new Exception(assemble.Message);
                if (assemble.Kind == ExternalProcessResultKind.StartFailure)
                    throw new Exception(assemble.Message);
                if (assemble.Kind == ExternalProcessResultKind.TimedOut)
                    throw new Exception("Assembly timed out.");
                if (assemble.ExitCode != 0)
                    throw new Exception($"Assembly failed:\n{assemble.StandardError}");

                // Step 2: Extract binary with objcopy
                var objcopy = await ExternalLauncher.Current.RunCapturedProcessAsync(new ExternalProcessRequest(objcopyExe)
                {
                    Arguments = $"-O binary \"{tempObj}\" \"{tempBin}\"",
                    Timeout = TimeSpan.FromSeconds(30),
                    CaptureStandardOutput = false,
                    CaptureStandardError = true,
                });
                if (objcopy.Kind == ExternalProcessResultKind.Unsupported)
                    throw new Exception(objcopy.Message);
                if (objcopy.Kind == ExternalProcessResultKind.StartFailure)
                    throw new Exception(objcopy.Message);
                if (objcopy.Kind == ExternalProcessResultKind.TimedOut)
                    throw new Exception("objcopy timed out.");
                if (objcopy.ExitCode != 0)
                    throw new Exception($"objcopy failed:\n{objcopy.StandardError}");

                if (!File.Exists(tempBin))
                    throw new Exception("Binary output file not produced.");

                byte[] bin = File.ReadAllBytes(tempBin);
                if (bin.Length == 0)
                    throw new Exception("Compiled binary is empty.");

                // Write to ROM
                rom.write_range(offset, bin);
                _undoService.Commit();

                _ = MessageBoxWindow.Show(TopLevel.GetTopLevel(this) as Window,
                    $"Successfully wrote {bin.Length} bytes at 0x{offset:X08}.",
                    "ASM Compile", MessageBoxMode.Ok);
            }
            catch (Exception ex)
            {
                _undoService.Rollback();
                Log.Error("ToolASMEditView.Compile", ex.ToString());
                _ = MessageBoxWindow.Show(TopLevel.GetTopLevel(this) as Window, ex.Message, "Compile Error", MessageBoxMode.Ok);
            }
            finally
            {
                _isCompiling = false;
                if (sender is Control button)
                    button.IsEnabled = true;
                try { if (File.Exists(tempAsm)) File.Delete(tempAsm); } catch (Exception ex) { Log.ErrorF("ToolASMEditView temp asm cleanup: {0}", ex.Message); }
                try { if (File.Exists(tempObj)) File.Delete(tempObj); } catch (Exception ex) { Log.ErrorF("ToolASMEditView temp obj cleanup: {0}", ex.Message); }
                try { if (File.Exists(tempBin)) File.Delete(tempBin); } catch (Exception ex) { Log.ErrorF("ToolASMEditView temp bin cleanup: {0}", ex.Message); }
            }
        }
        void Close_Click(object? sender, RoutedEventArgs e) => RequestClose();

        public void NavigateTo(uint address) { }
        public void SelectFirstItem() { }
    }
}
