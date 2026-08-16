using global::Avalonia;
using System;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using System.Collections.Generic;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class SongTableView : TranslatedUserControl, IEmbeddableEditor, IPickableEditor, IDataVerifiableView
    {
        readonly SongTableViewModel _vm = new();
        readonly UndoService _undoService = new();
        bool _hasLoadedList;

        public string ViewTitle => "Song Table";
        public new bool IsLoaded => _vm.CanWrite;
        public EditorDescriptor Descriptor => new("Song Table Editor", 1505, 809, SizeToContent: global::Avalonia.Controls.SizeToContent.WidthAndHeight);
        public event EventHandler? CloseRequested;

        public event Action<PickResult>? SelectionConfirmed;

        public SongTableView()
        {
            InitializeComponent();
            SongList.SelectedAddressChanged += OnSongSelected;
            SongList.SelectionConfirmed += result => SelectionConfirmed?.Invoke(result);
        }

        protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
        {
            base.OnAttachedToVisualTree(e);
            if (!_hasLoadedList)
            {
                _hasLoadedList = true;
                LoadList();
            }
        }

        void LoadList()
        {
            _vm.IsLoading = true;
            try
            {
                var items = _vm.LoadSongList();
                SongList.SetItems(items);
                SongTable_Expand_Button.IsVisible =
                    _vm.ShouldShowExpansion();
            }
            catch (Exception ex)
            {
                Log.ErrorF("SongTableView.LoadList failed: {0}", ex.Message);
            }
            finally
            {
                _vm.IsLoading = false;
                _vm.MarkClean();
            }
        }

        async void DataExpansion_Click(object? sender, RoutedEventArgs e)
        {
            try
            {
                if (CoreState.IsDecompMode)
                {
                    CoreState.Services?.ShowError(R._(
                        "This is a source-backed decomp project. The built ROM is a preview and cannot be saved over. Edit the source and rebuild instead."));
                    return;
                }
                if (CoreState.ROM == null)
                {
                    CoreState.Services?.ShowInfo(R._("Load a ROM first."));
                    return;
                }

                uint currentCount = (uint)SongList.ItemCount;
                if (currentCount == 0)
                {
                    CoreState.Services?.ShowError(
                        R._("Cannot expand: list is empty."));
                    return;
                }
                if (currentCount >= SongTableViewModel.MaxSongCount)
                {
                    CoreState.Services?.ShowInfo(R._(
                        "New count ({0}) exceeds the maximum ({1}).",
                        currentCount + 1,
                        SongTableViewModel.MaxSongCount));
                    return;
                }

                uint? chosen = await Dialogs.NumberInputDialog.Show(
                    TopLevel.GetTopLevel(this) as Window,
                    R._("Enter the new entry count for the Song Table (current: {0}, max: {1}).",
                        currentCount, SongTableViewModel.MaxSongCount),
                    R._("Data Expansion"),
                    currentCount + 1,
                    currentCount + 1,
                    SongTableViewModel.MaxSongCount);
                if (chosen == null) return;
                uint newCount = chosen.Value;

                bool hadSelection = SongList.SelectedItem != null;
                uint selectedTag = SongList.SelectedItem?.tag ?? 0;

                _undoService.Begin("Expand Song Table");
                bool expanded = false;
                try
                {
                    DataExpansionCore.ExpandResult result =
                        _vm.ExpandSongTable(newCount);
                    if (!result.Success)
                    {
                        _undoService.Rollback();
                        CoreState.Services?.ShowError(
                            R._("List expansion failed: {0}", result.Error));
                        return;
                    }

                    ReloadSongListPreserveSelection(
                        hadSelection, selectedTag);
                    SongTable_Expand_Button.IsVisible =
                        _vm.ShouldShowExpansion();

                    _undoService.Commit();
                    _vm.MarkClean();
                    expanded = true;
                }
                catch (Exception inner)
                {
                    bool canRollback = _undoService.HasPendingUndo;
                    if (canRollback)
                    {
                        _undoService.Rollback();
                        try
                        {
                            ReloadSongListPreserveSelection(
                                hadSelection, selectedTag);
                            SongTable_Expand_Button.IsVisible =
                                _vm.ShouldShowExpansion();
                        }
                        catch (Exception refreshEx)
                        {
                            Log.Error(
                                "SongTableView.DataExpansion_Click rollback refresh failed: " +
                                refreshEx.ToString());
                        }
                    }
                    Log.Error(
                        "SongTableView.DataExpansion_Click inner failed: " +
                        inner.ToString());
                    if (canRollback)
                    {
                        CoreState.Services?.ShowError(
                            R._("List expansion failed: {0}", inner.Message));
                    }
                }
                if (expanded)
                {
                    CoreState.Services?.ShowInfo(R._(
                        "Expanded Song Table to {0} entries.", newCount));
                }
            }
            catch (Exception ex)
            {
                Log.Error(
                    "SongTableView.DataExpansion_Click failed: " +
                    ex.ToString());
            }
        }

        void ReloadSongListPreserveSelection(
            bool hadSelection,
            uint selectedTag)
        {
            List<AddrResult> items = _vm.LoadSongList();
            uint preserveAddress = 0;
            if (hadSelection)
            {
                foreach (AddrResult item in items)
                {
                    if (item.tag == selectedTag)
                    {
                        preserveAddress = item.addr;
                        break;
                    }
                }
            }

            if (preserveAddress != 0)
                SongList.SetItemsPreserveSelection(items, preserveAddress);
            else
                SongList.SetItems(items);
        }

        void OnSongSelected(uint addr)
        {
            _vm.IsLoading = true;
            try
            {
                // LoadSong derives SongIndex from the table base, so the
                // write-protection guard (IsSongIdZero) recognises Song ID 0
                // regardless of how the entry was selected.
                _vm.LoadSong(addr);
                UpdateUI();
            }
            catch (Exception ex)
            {
                Log.ErrorF("SongTableView.OnSongSelected failed: {0}", ex.Message);
            }
            finally
            {
                _vm.IsLoading = false;
                _vm.MarkClean();
            }
        }

        public void NavigateTo(uint address)
        {
            SongList.SelectAddress(address);
        }

        void UpdateUI()
        {
            AddrLabel.Text = $"0x{_vm.CurrentAddr:X08}";
            HeaderBox.Text = $"0x{_vm.SongHeaderPointer:X08}";
            PlayerTypeBox.Value = _vm.PlayerType;
            TrackCountLabel.Text = _vm.TrackCount.ToString();
            HeaderPriorityLabel.Text = _vm.HeaderPriority.ToString();
            HeaderReverbLabel.Text = _vm.HeaderReverb.ToString();
        }

        void Write_Click(object? sender, RoutedEventArgs e)
        {
            if (!_vm.CanWrite) return;
            // WF parity: SongID 0 is write-protected (UseWriteProtectionID00).
            // Mirrors SongTrackView — the reserved silence entry must not be
            // overwritten (breaks "no music" semantics).
            if (_vm.IsSongIdZero)
            {
                CoreState.Services.ShowError("Song ID 0 is write-protected (silence song).");
                return;
            }

            _undoService.Begin("Edit Song Table");
            try
            {
                _vm.SongHeaderPointer = ParseHexText(HeaderBox.Text);
                _vm.PlayerType = (uint)(PlayerTypeBox.Value ?? 0);
                // Only commit + report success when a write actually occurred.
                // WriteSong() returns false (no ROM mutation) for a protected /
                // out-of-range entry — roll back and surface an error instead of
                // falsely reporting success.
                if (_vm.WriteSong())
                {
                    _undoService.Commit();
                    _vm.MarkClean();
                    CoreState.Services.ShowInfo("Song table data written.");
                }
                else
                {
                    _undoService.Rollback();
                    CoreState.Services.ShowError("Song table data was not written.");
                }
            }
            catch (Exception ex)
            {
                _undoService.Rollback();
                Log.ErrorF("SongTableView.Write_Click failed: {0}", ex.Message);
            }
        }

        public void EnablePickMode() => SongList.EnablePickMode();

        public void SelectFirstItem()
        {
            SongList.SelectFirst();
        }

        public ViewModelBase? DataViewModel => _vm;
        public void RequestClose() => CloseRequested?.Invoke(this, EventArgs.Empty);

        static uint ParseHexText(string? text)
        {
            if (string.IsNullOrWhiteSpace(text)) return 0;
            text = text.Trim();
            if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) text = text[2..];
            return uint.TryParse(text, System.Globalization.NumberStyles.HexNumber, null, out uint v) ? v : 0;
        }
    }
}
