using System;
using System.Collections.Generic;
using System.IO;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    /// <summary>
    /// ViewModel for the Avalonia ROM Diff Tool. Provides cross-platform parity
    /// with WinForms ToolDiffForm for both 2-ROM diff and 3-ROM diff modes.
    /// All diff work is delegated to the cross-platform DiffToolCore helper.
    /// </summary>
    public class ToolDiffViewModel : ViewModelBase
    {
        string _otherPath = string.Empty;
        string _aFilePath = string.Empty;
        string _bFilePath = string.Empty;
        uint _recoverMissMatch = 32;
        bool _isCollectFreeSpace;
        string _statusText = string.Empty;
        bool _isLoaded;

        public string OtherPath { get => _otherPath; set => SetField(ref _otherPath, value); }
        public string AFilePath { get => _aFilePath; set => SetField(ref _aFilePath, value); }
        public string BFilePath { get => _bFilePath; set => SetField(ref _bFilePath, value); }
        public uint RecoverMissMatch { get => _recoverMissMatch; set => SetField(ref _recoverMissMatch, value); }
        public bool IsCollectFreeSpace { get => _isCollectFreeSpace; set => SetField(ref _isCollectFreeSpace, value); }
        public string StatusText { get => _statusText; set => SetField(ref _statusText, value); }
        public bool IsLoaded { get => _isLoaded; set => SetField(ref _isLoaded, value); }

        public void Initialize()
        {
            IsLoaded = true;
        }

        /// <summary>Stub LoadList for editor-list compatibility. Diff tool has no list of entries.</summary>
        public List<AddrResult> LoadList()
        {
            return new List<AddrResult>();
        }

        // ---------- 2-ROM diff ----------

        /// <summary>Generate a binary patch from the current ROM vs. another ROM file.</summary>
        public void RunMakeBinPatch(string outPath)
        {
            if (string.IsNullOrWhiteSpace(OtherPath))
            {
                StatusText = R._("2-ROM Diff: 'other ROM' path required.");
                return;
            }
            if (string.IsNullOrWhiteSpace(outPath))
            {
                StatusText = R._("2-ROM Diff: output path required.");
                return;
            }
            if (!File.Exists(OtherPath))
            {
                StatusText = R._("2-ROM Diff: 'other ROM' file not found.");
                return;
            }
            var rom = CoreState.ROM;
            if (rom == null)
            {
                StatusText = R._("2-ROM Diff: no ROM loaded.");
                return;
            }

            try
            {
                byte[] otherBin = File.ReadAllBytes(OtherPath);
                DiffToolCore.MakeDiff(outPath, rom.Data, otherBin, RecoverMissMatch,
                    IsCollectFreeSpace,
                    version: rom.RomInfo.version,
                    isMultibyte: rom.RomInfo.is_multibyte);
                StatusText = R._("2-ROM Diff: wrote patch file {0}.", Path.GetFileName(outPath));
            }
            catch (Exception ex)
            {
                StatusText = R._("2-ROM Diff failed: {0}", ex.Message);
            }
        }

        // ---------- 3-ROM diff ----------

        /// <summary>Generate a 3-way binary patch (A and B agree but differ from current ROM).</summary>
        public void RunMakeBinPatch3(string outPath)
        {
            if (string.IsNullOrWhiteSpace(AFilePath) || string.IsNullOrWhiteSpace(BFilePath))
            {
                StatusText = R._("3-ROM Diff: both A and B paths required.");
                return;
            }
            if (string.IsNullOrWhiteSpace(outPath))
            {
                StatusText = R._("3-ROM Diff: output path required.");
                return;
            }
            if (!File.Exists(AFilePath))
            {
                StatusText = R._("3-ROM Diff: ROM A file not found.");
                return;
            }
            if (!File.Exists(BFilePath))
            {
                StatusText = R._("3-ROM Diff: ROM B file not found.");
                return;
            }
            var rom = CoreState.ROM;
            if (rom == null)
            {
                StatusText = R._("3-ROM Diff: no ROM loaded.");
                return;
            }

            try
            {
                byte[] a = File.ReadAllBytes(AFilePath);
                byte[] b = File.ReadAllBytes(BFilePath);
                DiffToolCore.MakeDiff3(outPath, rom.Data, a, b, RecoverMissMatch);
                StatusText = R._("3-ROM Diff: wrote patch file {0}.", Path.GetFileName(outPath));
            }
            catch (Exception ex)
            {
                StatusText = R._("3-ROM Diff failed: {0}", ex.Message);
            }
        }
    }
}
