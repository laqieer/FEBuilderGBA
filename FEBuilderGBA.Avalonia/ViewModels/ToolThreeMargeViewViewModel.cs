using System;
using System.Collections.ObjectModel;
using System.IO;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    public class ToolThreeMargeViewViewModel : ViewModelBase
    {
        bool _isLoaded;
        string _originalPath = string.Empty;
        string _myPath = string.Empty;
        string _theirsPath = string.Empty;
        string _statusText = "Select three ROM files to merge.";
        bool _hasMergeResult;
        int _changesMine;
        int _changesTheirs;
        int _changesBoth;
        int _conflictBytes;

        ThreeWayMergeCore.MergeResult? _mergeResult;
        byte[]? _theirsData;

        public bool IsLoaded { get => _isLoaded; set => SetField(ref _isLoaded, value); }
        public string OriginalPath { get => _originalPath; set => SetField(ref _originalPath, value); }
        public string MyPath { get => _myPath; set => SetField(ref _myPath, value); }
        public string TheirsPath { get => _theirsPath; set => SetField(ref _theirsPath, value); }
        public string StatusText { get => _statusText; set => SetField(ref _statusText, value); }
        public bool HasMergeResult { get => _hasMergeResult; set => SetField(ref _hasMergeResult, value); }
        public int ChangesMine { get => _changesMine; set => SetField(ref _changesMine, value); }
        public int ChangesTheirs { get => _changesTheirs; set => SetField(ref _changesTheirs, value); }
        public int ChangesBoth { get => _changesBoth; set => SetField(ref _changesBoth, value); }
        public int ConflictBytes { get => _conflictBytes; set => SetField(ref _conflictBytes, value); }

        public ObservableCollection<ConflictItemViewModel> ConflictItems { get; } = new();

        public void Initialize()
        {
            IsLoaded = true;
        }

        public bool CanMerge => !string.IsNullOrEmpty(OriginalPath)
                             && !string.IsNullOrEmpty(MyPath)
                             && !string.IsNullOrEmpty(TheirsPath);

        /// <summary>Execute the three-way merge.</summary>
        public void RunMerge()
        {
            HasMergeResult = false;
            ConflictItems.Clear();

            if (!File.Exists(OriginalPath))
            {
                StatusText = "Original ROM file not found.";
                return;
            }
            if (!File.Exists(MyPath))
            {
                StatusText = "My ROM file not found.";
                return;
            }
            if (!File.Exists(TheirsPath))
            {
                StatusText = "Their ROM file not found.";
                return;
            }

            byte[] original = File.ReadAllBytes(OriginalPath);
            byte[] mine = File.ReadAllBytes(MyPath);
            _theirsData = File.ReadAllBytes(TheirsPath);

            if (original.Length != mine.Length || original.Length != _theirsData.Length)
            {
                StatusText = $"ROM sizes differ: Original={original.Length}, Mine={mine.Length}, Theirs={_theirsData.Length}. All must be the same size.";
                return;
            }

            _mergeResult = ThreeWayMergeCore.Merge(original, mine, _theirsData);

            ChangesMine = _mergeResult.ChangesMine;
            ChangesTheirs = _mergeResult.ChangesTheirs;
            ChangesBoth = _mergeResult.ChangesBoth;
            ConflictBytes = _mergeResult.ConflictBytes;

            foreach (var c in _mergeResult.Conflicts)
            {
                ConflictItems.Add(new ConflictItemViewModel(c));
            }

            HasMergeResult = true;

            if (_mergeResult.Conflicts.Count == 0)
            {
                StatusText = $"Merge complete: {ChangesMine} mine, {ChangesTheirs} theirs, {ChangesBoth} both. No conflicts!";
            }
            else
            {
                StatusText = $"Merge complete: {ChangesMine} mine, {ChangesTheirs} theirs, {ChangesBoth} both. {_mergeResult.Conflicts.Count} conflict(s) ({ConflictBytes} bytes) require resolution.";
            }
        }

        /// <summary>Apply conflict resolutions and save the merged ROM.</summary>
        public bool SaveMerged(string outputPath)
        {
            if (_mergeResult == null || _theirsData == null)
                return false;

            // Copy resolution choices from VM items back to core model
            foreach (var item in ConflictItems)
            {
                item.Conflict.UseMine = item.UseMine;
            }

            ThreeWayMergeCore.ApplyResolutions(_mergeResult, _theirsData);
            File.WriteAllBytes(outputPath, _mergeResult.MergedData);
            StatusText = $"Merged ROM saved to: {outputPath}";
            return true;
        }
    }

    /// <summary>ViewModel wrapper for a single conflict range.</summary>
    public class ConflictItemViewModel : ViewModelBase
    {
        bool _useMine = true;

        public ThreeWayMergeCore.ConflictRange Conflict { get; }

        public uint Offset => Conflict.Offset;
        public uint Length => Conflict.Length;
        public string OffsetHex => $"0x{Conflict.Offset:X08}";
        public string LengthText => $"{Conflict.Length} byte(s)";
        public string OriginalHex => FormatBytes(Conflict.OriginalBytes);
        public string MyHex => FormatBytes(Conflict.MyBytes);
        public string TheirHex => FormatBytes(Conflict.TheirBytes);

        public bool UseMine
        {
            get => _useMine;
            set => SetField(ref _useMine, value);
        }

        public bool UseTheirs
        {
            get => !_useMine;
            set
            {
                if (value != !_useMine)
                    UseMine = !value;
            }
        }

        public ConflictItemViewModel(ThreeWayMergeCore.ConflictRange conflict)
        {
            Conflict = conflict;
            _useMine = conflict.UseMine;
        }

        static string FormatBytes(byte[] bytes)
        {
            if (bytes.Length <= 8)
                return BitConverter.ToString(bytes).Replace("-", " ");
            return BitConverter.ToString(bytes, 0, 8).Replace("-", " ") + " ...";
        }
    }
}
