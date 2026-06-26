using System;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    public class ToolWorkSupport_SelectUPSViewModel : ViewModelBase
    {
        bool _isLoaded;
        string _originalFilename = "";
        string _upsFilename = "";
        bool _dialogConfirmed;
        string _instructionText = "";

        public bool IsLoaded { get => _isLoaded; set => SetField(ref _isLoaded, value); }

        /// <summary>
        /// Path to the vanilla (unmodified) ROM file.
        /// WinForms: OrignalFilename TextBoxEx.
        /// </summary>
        public string OriginalFilename { get => _originalFilename; set => SetField(ref _originalFilename, value); }

        /// <summary>
        /// Path to the UPS patch file to be applied.
        /// </summary>
        public string UpsFilename { get => _upsFilename; set => SetField(ref _upsFilename, value); }

        /// <summary>
        /// Whether the user confirmed the dialog (clicked Apply UPS Patch).
        /// Maps to WinForms DialogResult after ApplyUPSPatchButton_Click.
        /// </summary>
        public bool DialogConfirmed { get => _dialogConfirmed; set => SetField(ref _dialogConfirmed, value); }

        /// <summary>
        /// Instruction text at the top of the dialog.
        /// WinForms: label1 - "Select an unmodified ROM to open the UPS patch".
        /// </summary>
        public string InstructionText { get => _instructionText; set => SetField(ref _instructionText, value); }

        public void Initialize()
        {
            InstructionText = "Select an unmodified ROM to open the UPS patch.";
            IsLoaded = true;
        }

        public void OpenUPS(string upsFilename)
        {
            UpsFilename = upsFilename;
            AutoFindOriginal();
        }

        /// <summary>
        /// Auto-find the unmodified vanilla ROM for the staged UPS by its source
        /// CRC32 (mirrors WinForms <c>ToolWorkSupport_SelectUPSForm._Shown</c> →
        /// <c>MainFormUtil.FindOrignalROMByCRC32</c>). Best-effort: leaves
        /// <see cref="OriginalFilename"/> empty when nothing matches.
        /// </summary>
        public void AutoFindOriginal()
        {
            try
            {
                if (string.IsNullOrEmpty(UpsFilename) || !System.IO.File.Exists(UpsFilename))
                {
                    return;
                }
                uint srcCrc32 = UPSUtilCore.GetUPSSrcCRC32(UpsFilename);
                if (srcCrc32 == U.NOT_FOUND)
                {
                    return;
                }
                // Pass the FULL set of Core search dirs (Copilot review finding #4),
                // mirroring WF FindOrignalROMByCRC32's multi-dir search:
                //   currentDir       = the staged UPS directory
                //   romBaseDirectory = the app base directory (CoreState.BaseDirectory)
                //   lastROMFilename  = the loaded ROM (its directory is also scanned)
                //   emulatorDirectory= Windows-only; not meaningfully available cross-platform.
                string dir = System.IO.Path.GetDirectoryName(UpsFilename) ?? "";
                string lastRom = CoreState.ROM?.Filename ?? "";
                string baseDir = CoreState.BaseDirectory ?? "";
                string found = ToolTranslateROMCore.FindOrignalROMByCRC32(dir, srcCrc32, baseDir, lastRom, "") ?? "";
                if (!string.IsNullOrEmpty(found))
                {
                    OriginalFilename = found;
                }
            }
            catch (Exception ex)
            {
                Log.Error("ToolWorkSupport_SelectUPSViewModel.AutoFindOriginal", ex.ToString());
            }
        }

        public string GetOriginalFilename() => OriginalFilename;
    }
}
