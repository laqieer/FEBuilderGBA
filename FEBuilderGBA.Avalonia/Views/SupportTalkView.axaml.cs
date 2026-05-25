using System;
using System.Threading.Tasks;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Controls;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;
using FEBuilderGBA.Core;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class SupportTalkView : TranslatedWindow, IEditorView, IDataVerifiableView
    {
        readonly SupportTalkViewModel _vm = new();
        readonly UndoService _undoService = new();

        public string ViewTitle => "Support Talk";
        public bool IsLoaded => _vm.IsLoaded;

        public SupportTalkView()
        {
            InitializeComponent();
            EntryList.SelectedAddressChanged += OnSelected;
            WriteButton.Click += Write_Click;
            Opened += (_, _) => LoadList();
        }

        void LoadList()
        {
            _vm.IsLoading = true;
            try
            {
                var items = _vm.LoadSupportTalkList();
                // Issue #361: show BOTH unit portraits per row. FE8 stores
                // partner 2 at addr+2 (partner 1 at addr+0).
                EntryList.SetItemsWithIcons(items, i => ListIconLoaders.UnitPortraitPairFromAddrU8Loader(items, i, unit2Offset: 2));
            }
            catch (Exception ex)
            {
                Log.Error("SupportTalkView.LoadList failed: {0}", ex.Message);
            }
            finally
            {
                _vm.IsLoading = false;
                _vm.MarkClean();
            }
        }

        void OnSelected(uint addr)
        {
            _vm.IsLoading = true;
            try
            {
                _vm.LoadSupportTalk(addr);
                UpdateUI();
            }
            catch (Exception ex)
            {
                Log.Error("SupportTalkView.OnSelected failed: {0}", ex.Message);
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
            SupportPartner1Nud.Value = _vm.SupportPartner1;
            SupportPartner2Nud.Value = _vm.SupportPartner2;
            TextIdCNud.Value = _vm.TextIdC;
            TextIdBNud.Value = _vm.TextIdB;
            TextIdANud.Value = _vm.TextIdA;
            // Refresh name previews on selection change (the routed
            // ValueChanged event fires on programmatic Value writes, but
            // doing this explicitly keeps the previews stable across
            // rapid selection changes).
            try { SupportPartner1Nud.NameText = SupportUnitNavigation.ResolveUnitTableName(CoreState.ROM, _vm.SupportPartner1); }
            catch { /* leave prior text */ }
            try { SupportPartner2Nud.NameText = SupportUnitNavigation.ResolveUnitTableName(CoreState.ROM, _vm.SupportPartner2); }
            catch { /* leave prior text */ }
            try { TextIdCNud.NameText = _vm.TextIdC != 0 ? NameResolver.GetTextById(_vm.TextIdC) : ""; }
            catch { /* leave prior text */ }
            try { TextIdBNud.NameText = _vm.TextIdB != 0 ? NameResolver.GetTextById(_vm.TextIdB) : ""; }
            catch { /* leave prior text */ }
            try { TextIdANud.NameText = _vm.TextIdA != 0 ? NameResolver.GetTextById(_vm.TextIdA) : ""; }
            catch { /* leave prior text */ }
            SongCNud.Value = _vm.SongC;
            SongBNud.Value = _vm.SongB;
            SongANud.Value = _vm.SongA;
        }

        void Write_Click(object? sender, RoutedEventArgs e)
        {
            _undoService.Begin("Edit Support Talk");
            try
            {
                _vm.SupportPartner1 = SupportPartner1Nud.Value;
                _vm.SupportPartner2 = SupportPartner2Nud.Value;
                _vm.TextIdC = TextIdCNud.Value;
                _vm.TextIdB = TextIdBNud.Value;
                _vm.TextIdA = TextIdANud.Value;
                _vm.SongC = (uint)(SongCNud.Value ?? 0);
                _vm.SongB = (uint)(SongBNud.Value ?? 0);
                _vm.SongA = (uint)(SongANud.Value ?? 0);

                _vm.WriteSupportTalk();
                _undoService.Commit();
                _vm.MarkClean();
            }
            catch (Exception ex)
            {
                _undoService.Rollback();
                Log.Error("SupportTalkView.Write_Click failed: {0}", ex.Message);
            }
        }

        public void NavigateTo(uint address)
        {
            EntryList.SelectAddress(address);
        }

        /// <summary>
        /// #358 — select the support-talk row that pairs <paramref name="uid1"/>
        /// and <paramref name="uid2"/> (in either order).  No-op when no row
        /// matches.  Mirrors WinForms <c>SupportTalkForm.JumpTo(unit1, unit2)</c>.
        /// </summary>
        public void JumpToUnitPair(uint uid1, uint uid2)
        {
            uint? addr = _vm.FindAddrForUnitPair(uid1, uid2);
            if (addr != null)
            {
                EntryList.SelectAddress(addr.Value);
            }
        }

        public void SelectFirstItem()
        {
            EntryList.SelectFirst();
        }

        // -- IdFieldControl handlers (#360 final) ---------------------------

        static uint UnitAddrFor(uint unitId)
        {
            var rom = CoreState.ROM;
            if (rom?.RomInfo == null) return 0;
            uint unitPtr = rom.RomInfo.unit_pointer;
            if (unitPtr == 0) return 0;
            uint baseAddr = rom.p32(unitPtr);
            if (!U.isSafetyOffset(baseAddr, rom)) return 0;
            uint dataSize = rom.RomInfo.unit_datasize;
            if (dataSize == 0) return 0;
            if (rom.RomInfo.version == 6) baseAddr += dataSize;
            uint entryAddr = baseAddr + unitId * dataSize;
            if (!U.isSafetyOffset(entryAddr, rom)) return 0;
            if (!U.isSafetyOffset(entryAddr + dataSize - 1, rom)) return 0;
            return entryAddr;
        }

        static uint TextAddrFor(uint textId)
        {
            var rom = CoreState.ROM;
            if (rom?.RomInfo == null) return 0;
            uint ptr = rom.RomInfo.text_pointer;
            if (ptr == 0) return 0;
            uint baseAddr = rom.p32(ptr);
            if (!U.isSafetyOffset(baseAddr, rom)) return 0;
            uint addr = baseAddr + textId * 4;
            if (!U.isSafetyOffset(addr, rom)) return 0;
            return addr;
        }

        void SupportPartner1_Jump(object? sender, RoutedEventArgs e)
        {
            try { uint addr = UnitAddrFor(SupportPartner1Nud.Value); if (addr != 0) WindowManager.Instance.Navigate<UnitEditorView>(addr); }
            catch (Exception ex) { Log.Error("SupportTalkView.SupportPartner1_Jump failed: {0}", ex.Message); }
        }

        async void SupportPartner1_Pick(object? sender, RoutedEventArgs e)
        {
            try
            {
                uint addr = UnitAddrFor(SupportPartner1Nud.Value);
                var result = await WindowManager.Instance.PickFromEditor<UnitEditorView>(addr, this);
                if (result != null) SupportPartner1Nud.Value = (uint)result.Index;
            }
            catch (Exception ex) { Log.Error("SupportTalkView.SupportPartner1_Pick failed: {0}", ex.Message); }
        }

        void SupportPartner1_ValueChanged(object? sender, IdFieldValueChangedEventArgs e)
        {
            try { SupportPartner1Nud.NameText = SupportUnitNavigation.ResolveUnitTableName(CoreState.ROM, e.NewValue); }
            catch { /* leave prior text */ }
        }

        void SupportPartner2_Jump(object? sender, RoutedEventArgs e)
        {
            try { uint addr = UnitAddrFor(SupportPartner2Nud.Value); if (addr != 0) WindowManager.Instance.Navigate<UnitEditorView>(addr); }
            catch (Exception ex) { Log.Error("SupportTalkView.SupportPartner2_Jump failed: {0}", ex.Message); }
        }

        async void SupportPartner2_Pick(object? sender, RoutedEventArgs e)
        {
            try
            {
                uint addr = UnitAddrFor(SupportPartner2Nud.Value);
                var result = await WindowManager.Instance.PickFromEditor<UnitEditorView>(addr, this);
                if (result != null) SupportPartner2Nud.Value = (uint)result.Index;
            }
            catch (Exception ex) { Log.Error("SupportTalkView.SupportPartner2_Pick failed: {0}", ex.Message); }
        }

        void SupportPartner2_ValueChanged(object? sender, IdFieldValueChangedEventArgs e)
        {
            try { SupportPartner2Nud.NameText = SupportUnitNavigation.ResolveUnitTableName(CoreState.ROM, e.NewValue); }
            catch { /* leave prior text */ }
        }

        void TextIdC_Jump(object? sender, RoutedEventArgs e)
        {
            try { uint addr = TextAddrFor(TextIdCNud.Value); if (addr != 0) WindowManager.Instance.Navigate<TextViewerView>(addr); }
            catch (Exception ex) { Log.Error("SupportTalkView.TextIdC_Jump failed: {0}", ex.Message); }
        }

        void TextIdC_ValueChanged(object? sender, IdFieldValueChangedEventArgs e)
        {
            try { TextIdCNud.NameText = e.NewValue != 0 ? NameResolver.GetTextById(e.NewValue) : ""; }
            catch { /* leave prior text */ }
        }

        void TextIdB_Jump(object? sender, RoutedEventArgs e)
        {
            try { uint addr = TextAddrFor(TextIdBNud.Value); if (addr != 0) WindowManager.Instance.Navigate<TextViewerView>(addr); }
            catch (Exception ex) { Log.Error("SupportTalkView.TextIdB_Jump failed: {0}", ex.Message); }
        }

        void TextIdB_ValueChanged(object? sender, IdFieldValueChangedEventArgs e)
        {
            try { TextIdBNud.NameText = e.NewValue != 0 ? NameResolver.GetTextById(e.NewValue) : ""; }
            catch { /* leave prior text */ }
        }

        void TextIdA_Jump(object? sender, RoutedEventArgs e)
        {
            try { uint addr = TextAddrFor(TextIdANud.Value); if (addr != 0) WindowManager.Instance.Navigate<TextViewerView>(addr); }
            catch (Exception ex) { Log.Error("SupportTalkView.TextIdA_Jump failed: {0}", ex.Message); }
        }

        void TextIdA_ValueChanged(object? sender, IdFieldValueChangedEventArgs e)
        {
            try { TextIdANud.NameText = e.NewValue != 0 ? NameResolver.GetTextById(e.NewValue) : ""; }
            catch { /* leave prior text */ }
        }

        public ViewModelBase? DataViewModel => _vm;
    }
}
