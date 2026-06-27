using System;
using System.Threading.Tasks;
using global::Avalonia.Controls;
using global::Avalonia.Input;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Controls;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;
using FEBuilderGBA.Core;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class SoundRoomViewerView : TranslatedWindow, IEditorView, IDataVerifiableView
    {
        readonly SoundRoomViewerViewModel _vm = new();
        readonly UndoService _undoService = new();

        public string ViewTitle => "Sound Room";
        public bool IsLoaded => _vm.CanWrite;

        public SoundRoomViewerView()
        {
            InitializeComponent();
            EntryList.SelectedAddressChanged += OnSelected;
            TextIdBox.ValueChanged += OnTextIdChanged;
            Opened += (_, _) => LoadList();
        }

        void OnTextIdChanged(object? sender, NumericUpDownValueChangedEventArgs e)
        {
            if (_vm.IsLoading) return;
            uint id = (uint)(TextIdBox.Value ?? 0);
            try { TextIdPreview.Text = id != 0 ? NameResolver.GetTextById(id) : ""; }
            catch { TextIdPreview.Text = ""; }
        }

        void LoadList()
        {
            _vm.IsLoading = true;
            try
            {
                var items = _vm.LoadSoundRoomList();
                EntryList.SetItems(items);
                EnableStructuralEdit();
            }
            catch (Exception ex)
            {
                Log.ErrorF("SoundRoomViewerView.LoadList failed: {0}", ex.Message);
            }
            finally
            {
                _vm.IsLoading = false;
                _vm.MarkClean();
            }
        }

        /// <summary>
        /// Wire the shared <see cref="AddressListControl"/>'s opt-in structural-edit
        /// context menu (Copy block / Paste / Swap Up / Swap Down) — the WinForms
        /// <c>SoundRoomForm</c>/<c>SoundRoomFE6Form</c> get this via
        /// <c>InputFormRef.MakeGeneralAddressListContextMenu(true)</c>, i.e.
        /// <c>useUpDown:true, useClear:false</c> (#1539). EXACT parity: SoundRoom does
        /// NOT enable the DEL/Invalidate clear action, so <c>useClear:false</c> here too.
        /// Block size is the WF <c>BlockSize</c> = <c>sound_room_datasize</c> (12 or 16).
        /// The clipboard identity header matches WinForms (<c>AddressList@SoundRoomForm</c>,
        /// or <c>AddressList@SoundRoomFE6Form</c> on FE6) so copy/paste interoperates
        /// across the two apps. No row-0 guard — WF SoundRoomForm does not set
        /// <c>UseWriteProtectionID00</c>. Idempotent on the control, so calling from
        /// every <c>LoadList</c> is safe.
        /// </summary>
        void EnableStructuralEdit()
        {
            var rom = CoreState.ROM;
            if (rom?.RomInfo == null) return;
            int blockSize = (int)rom.RomInfo.sound_room_datasize;
            if (blockSize <= 0) return;
            string formName = rom.RomInfo.version == 6 ? "SoundRoomFE6Form" : "SoundRoomForm";
            EntryList.EnableStructuralEdit(
                blockSize,
                reload: () => _vm.LoadSoundRoomList(),
                writeProtectId00: null,
                useSwap: true,
                useClear: false, // WF SoundRoomForm = MakeGeneralAddressListContextMenu(true) → useClear default false
                clipboardListName: "AddressList",
                clipboardFormName: formName);
        }

        /// <summary>
        /// List-expansion handler (#1450). Prompts for a new row count, delegates
        /// to <see cref="SoundRoomViewerViewModel.ExpandList"/> inside an
        /// <see cref="UndoService"/> scope, then reloads the list preserving the
        /// current selection. Mirrors WinForms <c>SoundRoomForm</c>'s "List
        /// Expansion" button (255, or 1000 with the soundroom_over255 patch) and
        /// the <c>ImageMapActionAnimationView.ListExpand_Click</c> flow.
        /// </summary>
        async void ListExpand_Click(object? sender, RoutedEventArgs e)
        {
            try
            {
                if (!_vm.CanWrite || CoreState.ROM == null)
                {
                    CoreState.Services?.ShowInfo(R._("Load a ROM first."));
                    return;
                }
                if (_vm.ReadCount == 0)
                {
                    CoreState.Services?.ShowInfo(R._("Cannot expand: list is empty."));
                    return;
                }

                uint cap = _vm.GetExpandsCap();
                uint defaultCount = _vm.ReadCount + 1;
                if (defaultCount > cap) defaultCount = cap;

                uint? chosen = await Dialogs.NumberInputDialog.Show(
                    this,
                    R._("Enter the new entry count for the sound room list (current: {0}, max: {1}).",
                        _vm.ReadCount, cap),
                    R._("List Expansion"),
                    defaultCount,
                    _vm.ReadCount,
                    cap);
                if (chosen == null) return; // user cancelled
                uint newCount = chosen.Value;
                if (newCount == _vm.ReadCount)
                {
                    CoreState.Services?.ShowInfo(R._("No change: new count equals current count."));
                    return;
                }

                // Capture the selected row's TAG (its 0-based index), NOT its
                // addr: ExpandList relocates the table to a new base, so every
                // row's addr changes. The tag (row index) is stable across the
                // relocation, so SelectByTag after reload re-selects the same
                // logical row (Copilot review on PR #1540).
                var selectedItem = EntryList.SelectedItem;
                bool hadSelection = selectedItem != null;
                uint selectedTag = selectedItem?.tag ?? 0;

                _undoService.Begin("Expand Sound Room List");
                try
                {
                    string err = _vm.ExpandList(newCount, _undoService.GetActiveUndoData());
                    if (!string.IsNullOrEmpty(err))
                    {
                        _undoService.Rollback();
                        CoreState.Services?.ShowError(err);
                        return;
                    }
                    _undoService.Commit();
                    _vm.MarkClean();

                    // Reload, preserving the previous selection by TAG (row index)
                    // since the table relocated and addresses changed. SetItems
                    // selects row 0 by default; re-select the original row by tag
                    // when there was a prior selection (falls back to row 0 if the
                    // tag no longer exists, which can't happen on a grow-only expand).
                    _vm.IsLoading = true;
                    try
                    {
                        var items = _vm.LoadSoundRoomList();
                        EntryList.SetItems(items);
                        if (hadSelection)
                            EntryList.SelectByTag(selectedTag);
                    }
                    finally
                    {
                        _vm.IsLoading = false;
                        _vm.MarkClean();
                    }

                    CoreState.Services?.ShowInfo(
                        R._("Expanded sound room list to {0} entries.", newCount));
                }
                catch (Exception inner)
                {
                    _undoService.Rollback();
                    // Log.Error joins args with spaces (no composite formatting),
                    // so pass a single interpolated string + the full exception.
                    Log.Error("SoundRoomViewerView.ListExpand_Click inner failed: " + inner.ToString());
                    CoreState.Services?.ShowError(R._("List expansion failed: {0}", inner.Message));
                }
            }
            catch (Exception ex)
            {
                Log.Error("SoundRoomViewerView.ListExpand_Click failed: " + ex.ToString());
            }
        }

        void OnSelected(uint addr)
        {
            _vm.IsLoading = true;
            try
            {
                _vm.LoadSoundRoom(addr);
                UpdateUI();
            }
            catch (Exception ex)
            {
                Log.ErrorF("SoundRoomViewerView.OnSelected failed: {0}", ex.Message);
            }
            finally
            {
                _vm.IsLoading = false;
                _vm.MarkClean();
            }
        }

        void UpdateUI()
        {
            AddrLabel.Text = $"0x{_vm.CurrentAddr:X08}";
            SongIdBox.Value = _vm.SongId;
            // NameResolver.GetSongName returns a fallback on failure; no try/catch needed (Copilot review #638).
            SongIdBox.NameText = NameResolver.GetSongName(_vm.SongId);
            Raw4Box.Value = _vm.Raw4;
            Raw8Box.Value = _vm.Raw8;
            TextIdBox.Value = _vm.TextId;
            try { TextIdPreview.Text = _vm.TextId != 0 ? NameResolver.GetTextById(_vm.TextId) : ""; }
            catch { TextIdPreview.Text = ""; }
        }

        void Write_Click(object? sender, RoutedEventArgs e)
        {
            if (!_vm.CanWrite) return;

            _undoService.Begin("Edit Sound Room");
            try
            {
                _vm.SongId = SongIdBox.Value;
                _vm.Raw4 = (uint)(Raw4Box.Value ?? 0);
                _vm.Raw8 = (uint)(Raw8Box.Value ?? 0);
                _vm.TextId = (uint)(TextIdBox.Value ?? 0);
                _vm.WriteSoundRoom();
                _undoService.Commit();
                _vm.MarkClean();
                CoreState.Services.ShowInfo("Sound room data written.");
            }
            catch (Exception ex)
            {
                _undoService.Rollback();
                Log.ErrorF("SoundRoomViewerView.Write_Click failed: {0}", ex.Message);
            }
        }

        // -- IdFieldControl handlers (#360 final) ---------------------------

        static uint SongAddrFor(uint songId)
        {
            var rom = CoreState.ROM;
            if (rom?.RomInfo == null) return 0;
            uint ptr = rom.RomInfo.sound_table_pointer;
            if (ptr == 0) return 0;
            uint baseAddr = rom.p32(ptr);
            if (!U.isSafetyOffset(baseAddr, rom)) return 0;
            // Compute the offset in ulong to detect wrap-around; the AXAML
            // Maximum allows up to int.MaxValue which means songId * 8 in
            // uint arithmetic could wrap past 4 GiB and still satisfy
            // isSafetyOffset on a 32 MB ROM (Copilot review #638).
            ulong entryAddr64 = (ulong)baseAddr + (ulong)songId * 8UL;
            if (entryAddr64 > uint.MaxValue) return 0;
            uint entryAddr = (uint)entryAddr64;
            if (!U.isSafetyOffset(entryAddr, rom)) return 0;
            // Validate the full 8-byte entry range so an id near the ROM
            // boundary doesn't half-fall outside.
            if (!U.isSafetyOffset(entryAddr + 7, rom)) return 0;
            return entryAddr;
        }

        void SongId_Jump(object? sender, RoutedEventArgs e)
        {
            try
            {
                uint addr = SongAddrFor(SongIdBox.Value);
                if (addr == 0) return;
                WindowManager.Instance.Navigate<SongTableView>(addr);
            }
            catch (Exception ex) { Log.ErrorF("SoundRoomViewerView.SongId_Jump failed: {0}", ex.Message); }
        }

        async void SongId_Pick(object? sender, RoutedEventArgs e)
        {
            try
            {
                uint addr = SongAddrFor(SongIdBox.Value);
                var result = await WindowManager.Instance.PickFromEditor<SongTableView>(addr, this);
                if (result != null) SongIdBox.Value = (uint)result.Index;
            }
            catch (Exception ex) { Log.ErrorF("SoundRoomViewerView.SongId_Pick failed: {0}", ex.Message); }
        }

        void SongId_ValueChanged(object? sender, IdFieldValueChangedEventArgs e)
        {
            // NameResolver returns a fallback on failure (Copilot review #638).
            SongIdBox.NameText = NameResolver.GetSongName(e.NewValue);
        }

        public void NavigateTo(uint address) => EntryList.SelectAddress(address);
        public void SelectFirstItem() => EntryList.SelectFirst();
        public ViewModelBase? DataViewModel => _vm;
    }
}
