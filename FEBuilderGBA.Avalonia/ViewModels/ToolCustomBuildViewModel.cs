using System;
using System.IO;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    /// <summary>
    /// ViewModel for the Avalonia "Custom Build" tool (WF <c>ToolCustomBuildForm</c>).
    /// Runs a custom build pipeline for the project — either a user CUSTOM_BUILD.cmd
    /// batch script (CMD method) or an Event Assembler target (EA method) — and loads
    /// the freshly-built ROM.
    ///
    /// The actual build+load work lives in the GUI-free Core helper
    /// <see cref="CustomBuildCore"/> (which reuses <see cref="EventAssemblerCompileCore"/>
    /// for the EA path). This VM only holds the form fields and forwards to that helper.
    /// It reads no ROM bytes for display (it only WRITES via the helper) so it is a
    /// read-no-ROM tool VM (no data-verification contract).
    ///
    /// "Marge and Update" (issue #1248 slice 2) runs the build, then
    /// <see cref="CustomBuildCore.MargeAndUpdate"/>: it diffs the built ROM against the
    /// vanilla ROM, assembles a CustomBuild patch under
    /// <c>config/patch2/FE8U/skill_CustomBuild</c>, and auto-installs it via
    /// <see cref="PatchInstallCore"/> (a CustomBuild patch is literal-offset BIN-diffs,
    /// so the slice-1 install — not the full PatchForm dependency-resolution engine — is
    /// what it needs).
    /// </summary>
    public class ToolCustomBuildViewModel : ViewModelBase
    {
        string _targetPath = "";
        string _originalRomPath = "";
        int _buildMethodIndex;              // 0 Auto, 1 CMD, 2 EA (CustomBuildCore.BuildMethod)
        int _takeoverSkillAssignmentIndex = 1; // 0 = don't carry over, 1 = carry over (WF default)
        bool _canUndo;
        string _statusMessage = "";

        /// <summary>Selected build target: a CUSTOM_BUILD.cmd or an EA .event file.</summary>
        public string TargetPath { get => _targetPath; set => SetField(ref _targetPath, value); }

        /// <summary>Selected un-modded (vanilla) ROM, copied to FE8_clean.gba for the CMD build.</summary>
        public string OriginalRomPath { get => _originalRomPath; set => SetField(ref _originalRomPath, value); }

        /// <summary>Build-method index: 0 = auto (by extension), 1 = CMD, 2 = Event Assembler.</summary>
        public int BuildMethodIndex { get => _buildMethodIndex; set => SetField(ref _buildMethodIndex, value); }

        /// <summary>
        /// Take-over-skill-assignment index (WF TakeoverSkillAssignmentComboBox):
        /// 0 = do not carry over the parent patch's skill assignment, 1 = carry it over.
        /// Defaults to 1 to match the WinForms form (SelectedIndex = 1).
        /// </summary>
        public int TakeoverSkillAssignmentIndex
        {
            get => _takeoverSkillAssignmentIndex;
            set => SetField(ref _takeoverSkillAssignmentIndex, value);
        }

        /// <summary>The take-over flag passed to <see cref="CustomBuildCore.MargeAndUpdate"/> (0 or 1).</summary>
        public uint TakeoverSkillAssignment => (uint)ClampIndex(_takeoverSkillAssignmentIndex, 1);

        /// <summary>True when an applied build can be undone.</summary>
        public bool CanUndo { get => _canUndo; set => SetField(ref _canUndo, value); }

        /// <summary>Result / error text shown in the status area.</summary>
        public string StatusMessage { get => _statusMessage; set => SetField(ref _statusMessage, value); }

        /// <summary>
        /// Clamp a ComboBox SelectedIndex into [0, max] before casting to an enum: a
        /// ComboBox reports -1 when nothing is selected, and a stray out-of-range value
        /// would cast to an undefined enum. Defaults to 0 (Auto) in those cases.
        /// </summary>
        static int ClampIndex(int index, int max) => index < 0 ? 0 : (index > max ? max : index);

        /// <summary>The selected build method for the Core helper.</summary>
        public CustomBuildCore.BuildMethod BuildMethod =>
            (CustomBuildCore.BuildMethod)ClampIndex(_buildMethodIndex, 2);

        /// <summary>True when the selected target file exists on disk.</summary>
        public bool TargetExists => !string.IsNullOrEmpty(TargetPath) && File.Exists(TargetPath);

        /// <summary>True when the selected original ROM exists on disk.</summary>
        public bool OriginalRomExists => !string.IsNullOrEmpty(OriginalRomPath) && File.Exists(OriginalRomPath);

        /// <summary>
        /// Prefill the original-ROM field by locating the un-modded ROM next to the
        /// current ROM (WF <c>MainFormUtil.FindOrignalROM</c>, ported to Core as
        /// <see cref="ToolTranslateROMCore.FindOrignalROMByCRC32"/> /
        /// <see cref="ToolTranslateROMCore.FindOrignalROMByLang"/>). Best-effort: a blank
        /// result simply leaves the field empty for the user to fill via Browse.
        /// </summary>
        public void PrefillOriginalRom()
        {
            ROM rom = CoreState.ROM;
            if (rom?.RomInfo == null || string.IsNullOrEmpty(rom.Filename)) return;

            try
            {
                string dir = Path.GetDirectoryName(rom.Filename) ?? string.Empty;
                if (string.IsNullOrEmpty(dir)) return;

                string baseDir = CoreState.BaseDirectory ?? string.Empty;
                string emulatorDir = GetEmulatorDirectory();

                // Match WF MainFormUtil.FindOrignalROM: CRC32 first, then lang fallback.
                string found = ToolTranslateROMCore.FindOrignalROMByCRC32(
                    dir, rom.RomInfo.orignal_crc32, baseDir,
                    lastROMFilename: rom.Filename, emulatorDirectory: emulatorDir);
                if (string.IsNullOrEmpty(found))
                {
                    found = ToolTranslateROMCore.FindOrignalROMByLang(
                        dir, CoreState.Language ?? "", rom.RomInfo.version, baseDir,
                        lastROMFilename: rom.Filename, emulatorDirectory: emulatorDir);
                }

                if (!string.IsNullOrEmpty(found) && File.Exists(found))
                    OriginalRomPath = found;
            }
            catch (Exception ex)
            {
                Log.Error("ToolCustomBuildViewModel.PrefillOriginalRom failed: " + ex.ToString());
            }
        }

        /// <summary>
        /// Resolve the configured emulator directory for the FindOrignalROM last-resort
        /// recursive scan (mirrors WF <c>Path.GetDirectoryName(Program.Config.at("emulator"))</c>).
        /// Empty when unset — the Core helper then skips the recursive scan.
        /// </summary>
        static string GetEmulatorDirectory()
        {
            var cfg = CoreState.Config;
            if (cfg == null) return string.Empty;
            string emulator = cfg.at("emulator", string.Empty);
            if (string.IsNullOrEmpty(emulator)) return string.Empty;
            try { return Path.GetDirectoryName(emulator) ?? string.Empty; }
            catch { return string.Empty; }
        }

        /// <summary>
        /// Build <see cref="TargetPath"/> and load the result using the shared Core
        /// helper. The caller owns the undo scope and passes its active
        /// <c>Undo.UndoData</c>.
        /// </summary>
        public CustomBuildCore.BuildResult Run(Undo.UndoData undo)
        {
            ROM rom = CoreState.ROM;
            return CustomBuildCore.Build(rom, TargetPath, OriginalRomPath, BuildMethod, undo);
        }

        /// <summary>
        /// The full WF <c>ToolCustomBuildForm.Run</c> flow (issue #1248): build the
        /// target, then — on success and when the build produced a ROM file — diff it
        /// against the vanilla ROM and auto-install the merged CustomBuild patch via
        /// <see cref="CustomBuildCore.MargeAndUpdate"/>. Both phases record their ROM
        /// writes into the caller-owned <paramref name="undo"/> (no thread-local ambient
        /// undo), so the caller commits/rolls back the whole operation as one unit.
        ///
        /// Returns the build result plus the marge result (null when the build failed or
        /// produced no on-disk ROM to diff — e.g. the EA in-place path, which has nothing
        /// to MargeAndUpdate from).
        /// </summary>
        public (CustomBuildCore.BuildResult build, CustomBuildCore.MargeResult marge) RunAndMarge(
            Undo.UndoData undo, Action<string> onProgress = null)
        {
            ROM rom = CoreState.ROM;
            var build = CustomBuildCore.Build(rom, TargetPath, OriginalRomPath, BuildMethod, undo, onProgress);
            if (!build.Success)
                return (build, null);

            // MargeAndUpdate needs an on-disk built ROM to diff against vanilla; the
            // CMD path produces SkillsTest.gba (BuiltRomPath). The EA in-place path has
            // no separate ROM file, so there is nothing to marge (the build already
            // wrote into the ROM directly).
            if (string.IsNullOrEmpty(build.BuiltRomPath))
                return (build, null);

            var marge = CustomBuildCore.MargeAndUpdate(
                rom, OriginalRomPath, build.BuiltRomPath, TargetPath,
                TakeoverSkillAssignment, undo, onProgress);
            return (build, marge);
        }
    }
}
