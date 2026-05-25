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
    public partial class SupportTalkFE7View : TranslatedWindow, IEditorView, IDataVerifiableView
    {
        readonly SupportTalkFE7ViewModel _vm = new();
        readonly UndoService _undoService = new();

        public string ViewTitle => "Support Talk (FE7)";
        public bool IsLoaded => _vm.IsLoaded;

        public SupportTalkFE7View()
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
                // Issue #361: show BOTH unit portraits per row. FE7 stores
                // partner 2 at addr+1 (partner 1 at addr+0).
                EntryList.SetItemsWithIcons(items, i => ListIconLoaders.UnitPortraitPairFromAddrU8Loader(items, i, unit2Offset: 1));
            }
            catch (Exception ex)
            {
                Log.Error("SupportTalkFE7View.LoadList failed: {0}", ex.Message);
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
                Log.Error("SupportTalkFE7View.OnSelected failed: {0}", ex.Message);
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
            TextCNud.Value = _vm.TextC;
            TextBNud.Value = _vm.TextB;
            TextANud.Value = _vm.TextA;
            try { SupportPartner1Nud.NameText = SupportUnitNavigation.ResolveUnitTableName(CoreState.ROM, _vm.SupportPartner1); }
            catch { /* leave prior text */ }
            try { SupportPartner2Nud.NameText = SupportUnitNavigation.ResolveUnitTableName(CoreState.ROM, _vm.SupportPartner2); }
            catch { /* leave prior text */ }
            try { TextCNud.NameText = _vm.TextC != 0 ? NameResolver.GetTextById(_vm.TextC) : ""; }
            catch { /* leave prior text */ }
            try { TextBNud.NameText = _vm.TextB != 0 ? NameResolver.GetTextById(_vm.TextB) : ""; }
            catch { /* leave prior text */ }
            try { TextANud.NameText = _vm.TextA != 0 ? NameResolver.GetTextById(_vm.TextA) : ""; }
            catch { /* leave prior text */ }
            SongCNud.Value = _vm.SongC;
            SongBNud.Value = _vm.SongB;
            SongANud.Value = _vm.SongA;
            PaddingNud.Value = _vm.Padding;
        }

        void Write_Click(object? sender, RoutedEventArgs e)
        {
            _undoService.Begin("Edit Support Talk (FE7)");
            try
            {
                _vm.SupportPartner1 = SupportPartner1Nud.Value;
                _vm.SupportPartner2 = SupportPartner2Nud.Value;
                _vm.TextC = TextCNud.Value;
                _vm.TextB = TextBNud.Value;
                _vm.TextA = TextANud.Value;
                _vm.SongC = (uint)(SongCNud.Value ?? 0);
                _vm.SongB = (uint)(SongBNud.Value ?? 0);
                _vm.SongA = (uint)(SongANud.Value ?? 0);
                _vm.Padding = (uint)(PaddingNud.Value ?? 0);

                _vm.WriteSupportTalk();
                _undoService.Commit();
                _vm.MarkClean();
            }
            catch (Exception ex)
            {
                _undoService.Rollback();
                Log.Error("SupportTalkFE7View.Write_Click failed: {0}", ex.Message);
            }
        }

        public void NavigateTo(uint address) => EntryList.SelectAddress(address);
        public void SelectFirstItem() => EntryList.SelectFirst();

        /// <summary>
        /// #358 — select the FE7 support-talk row that pairs the two units
        /// in either order.  Mirrors WinForms <c>SupportTalkFE7Form.JumpTo</c>.
        /// </summary>
        public void JumpToUnitPair(uint uid1, uint uid2)
        {
            uint? addr = _vm.FindAddrForUnitPair(uid1, uid2);
            if (addr != null)
            {
                EntryList.SelectAddress(addr.Value);
            }
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
            catch (Exception ex) { Log.Error("SupportTalkFE7View.SupportPartner1_Jump failed: {0}", ex.Message); }
        }

        async void SupportPartner1_Pick(object? sender, RoutedEventArgs e)
        {
            try
            {
                uint addr = UnitAddrFor(SupportPartner1Nud.Value);
                var result = await WindowManager.Instance.PickFromEditor<UnitEditorView>(addr, this);
                if (result != null) SupportPartner1Nud.Value = (uint)result.Index;
            }
            catch (Exception ex) { Log.Error("SupportTalkFE7View.SupportPartner1_Pick failed: {0}", ex.Message); }
        }

        void SupportPartner1_ValueChanged(object? sender, IdFieldValueChangedEventArgs e)
        {
            try { SupportPartner1Nud.NameText = SupportUnitNavigation.ResolveUnitTableName(CoreState.ROM, e.NewValue); }
            catch { /* leave prior text */ }
        }

        void SupportPartner2_Jump(object? sender, RoutedEventArgs e)
        {
            try { uint addr = UnitAddrFor(SupportPartner2Nud.Value); if (addr != 0) WindowManager.Instance.Navigate<UnitEditorView>(addr); }
            catch (Exception ex) { Log.Error("SupportTalkFE7View.SupportPartner2_Jump failed: {0}", ex.Message); }
        }

        async void SupportPartner2_Pick(object? sender, RoutedEventArgs e)
        {
            try
            {
                uint addr = UnitAddrFor(SupportPartner2Nud.Value);
                var result = await WindowManager.Instance.PickFromEditor<UnitEditorView>(addr, this);
                if (result != null) SupportPartner2Nud.Value = (uint)result.Index;
            }
            catch (Exception ex) { Log.Error("SupportTalkFE7View.SupportPartner2_Pick failed: {0}", ex.Message); }
        }

        void SupportPartner2_ValueChanged(object? sender, IdFieldValueChangedEventArgs e)
        {
            try { SupportPartner2Nud.NameText = SupportUnitNavigation.ResolveUnitTableName(CoreState.ROM, e.NewValue); }
            catch { /* leave prior text */ }
        }

        void TextC_Jump(object? sender, RoutedEventArgs e)
        {
            try { uint addr = TextAddrFor(TextCNud.Value); if (addr != 0) WindowManager.Instance.Navigate<TextViewerView>(addr); }
            catch (Exception ex) { Log.Error("SupportTalkFE7View.TextC_Jump failed: {0}", ex.Message); }
        }

        void TextC_ValueChanged(object? sender, IdFieldValueChangedEventArgs e)
        {
            try { TextCNud.NameText = e.NewValue != 0 ? NameResolver.GetTextById(e.NewValue) : ""; }
            catch { /* leave prior text */ }
        }

        void TextB_Jump(object? sender, RoutedEventArgs e)
        {
            try { uint addr = TextAddrFor(TextBNud.Value); if (addr != 0) WindowManager.Instance.Navigate<TextViewerView>(addr); }
            catch (Exception ex) { Log.Error("SupportTalkFE7View.TextB_Jump failed: {0}", ex.Message); }
        }

        void TextB_ValueChanged(object? sender, IdFieldValueChangedEventArgs e)
        {
            try { TextBNud.NameText = e.NewValue != 0 ? NameResolver.GetTextById(e.NewValue) : ""; }
            catch { /* leave prior text */ }
        }

        void TextA_Jump(object? sender, RoutedEventArgs e)
        {
            try { uint addr = TextAddrFor(TextANud.Value); if (addr != 0) WindowManager.Instance.Navigate<TextViewerView>(addr); }
            catch (Exception ex) { Log.Error("SupportTalkFE7View.TextA_Jump failed: {0}", ex.Message); }
        }

        void TextA_ValueChanged(object? sender, IdFieldValueChangedEventArgs e)
        {
            try { TextANud.NameText = e.NewValue != 0 ? NameResolver.GetTextById(e.NewValue) : ""; }
            catch { /* leave prior text */ }
        }

        public ViewModelBase? DataViewModel => _vm;
    }
}
