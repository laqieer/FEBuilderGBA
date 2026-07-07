using System;
using global::Avalonia;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Controls;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class CCBranchEditorView : TranslatedUserControl, IEmbeddableEditor, IDataVerifiableView
    {
        readonly CCBranchEditorViewModel _vm = new();
        readonly UndoService _undoService = new();
        bool _hasLoadedList;

        public string ViewTitle => "CC Branch Editor";
        public new bool IsLoaded => _vm.CanWrite;
        public EditorDescriptor Descriptor => new("CC Branch Editor", 1264, 684, SizeToContent: true);
        public event EventHandler? CloseRequested;
        public ViewModelBase? DataViewModel => _vm;
        public void RequestClose() => CloseRequested?.Invoke(this, EventArgs.Empty);

        public CCBranchEditorView()
        {
            InitializeComponent();
            BranchList.SelectedAddressChanged += OnBranchSelected;
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
            try
            {
                var items = _vm.LoadCCBranchList();
                BranchList.SetItemsWithIcons(items, i => ListIconLoaders.ClassIconLoader(items, i));
            }
            catch (Exception ex)
            {
                Log.ErrorF("CCBranchEditorView.LoadList failed: {0}", ex.Message);
            }
        }

        void OnBranchSelected(uint addr)
        {
            try
            {
                _vm.LoadCCBranch(addr);
                UpdateUI();
            }
            catch (Exception ex)
            {
                Log.ErrorF("CCBranchEditorView.OnBranchSelected failed: {0}", ex.Message);
            }
        }

        public void NavigateTo(uint address)
        {
            BranchList.SelectAddress(address);
        }

        void UpdateUI()
        {
            AddrLabel.Text = $"0x{_vm.CurrentAddr:X08}";
            Promo1Box.Value = _vm.PromotionClass1;
            Promo2Box.Value = _vm.PromotionClass2;
            // Push VM-resolved names; ValueChanged will also refresh on user input.
            Promo1Box.NameText = _vm.PromoName1;
            Promo2Box.NameText = _vm.PromoName2;
            UpstreamLabel.Text = _vm.UpstreamChain;
        }

        void Write_Click(object? sender, RoutedEventArgs e)
        {
            _vm.PromotionClass1 = Promo1Box.Value;
            _vm.PromotionClass2 = Promo2Box.Value;

            _undoService.Begin("Edit CC Branch");
            try
            {
                _vm.WriteCCBranch();
                _undoService.Commit();
                _vm.MarkClean();
                CoreState.Services.ShowInfo("CC Branch data written.");
            }
            catch (Exception ex)
            {
                _undoService.Rollback();
                Log.ErrorF("CCBranchEditorView.Write_Click failed: {0}", ex.Message);
            }
        }

        public void SelectFirstItem()
        {
            BranchList.SelectFirst();
        }

        // -- IdFieldControl handlers (#366) ----------------------------------

        /// <summary>
        /// Compute the ClassEditorView ROM address for the given class index.
        /// Returns 0 when the class table is unavailable OR when the computed
        /// entry address falls outside ROM bounds (i.e. the id is out of range).
        /// </summary>
        static uint ClassAddrFor(uint classId)
        {
            var rom = CoreState.ROM;
            if (rom?.RomInfo == null) return 0;
            uint classPtr = rom.RomInfo.class_pointer;
            if (classPtr == 0) return 0;
            uint baseAddr = rom.p32(classPtr);
            if (!U.isSafetyOffset(baseAddr, rom)) return 0;
            uint dataSize = rom.RomInfo.class_datasize;
            if (dataSize == 0) return 0;
            uint entryAddr = baseAddr + classId * dataSize;
            // Bounds-check the computed entry — refuse to navigate to invalid data.
            if (!U.isSafetyOffset(entryAddr, rom)) return 0;
            if (!U.isSafetyOffset(entryAddr + dataSize - 1, rom)) return 0;
            return entryAddr;
        }

        void Promo1_Jump(object? sender, RoutedEventArgs e) => JumpToClass(Promo1Box.Value);
        void Promo2_Jump(object? sender, RoutedEventArgs e) => JumpToClass(Promo2Box.Value);

        void JumpToClass(uint classId)
        {
            try
            {
                uint addr = ClassAddrFor(classId);
                if (addr == 0) return;
                WindowManager.Instance.Navigate<ClassEditorView>(addr);
            }
            catch (Exception ex)
            {
                Log.ErrorF("CCBranchEditorView.JumpToClass failed: {0}", ex.Message);
            }
        }

        async void Promo1_Pick(object? sender, RoutedEventArgs e) => await PickClass(Promo1Box);
        async void Promo2_Pick(object? sender, RoutedEventArgs e) => await PickClass(Promo2Box);

        async System.Threading.Tasks.Task PickClass(IdFieldControl target)
        {
            try
            {
                uint addr = ClassAddrFor(target.Value);
                var result = await WindowManager.Instance.PickFromEditor<ClassEditorView>(addr, TopLevel.GetTopLevel(this) as Window);
                if (result != null)
                {
                    target.Value = (uint)result.Index;
                    // NameText refresh happens automatically via ValueChanged.
                }
            }
            catch (Exception ex)
            {
                Log.ErrorF("CCBranchEditorView.PickClass failed: {0}", ex.Message);
            }
        }

        void Promo1_ValueChanged(object? sender, IdFieldValueChangedEventArgs e)
        {
            Promo1Box.NameText = NameResolver.GetClassName(e.NewValue);
        }

        void Promo2_ValueChanged(object? sender, IdFieldValueChangedEventArgs e)
        {
            Promo2Box.NameText = NameResolver.GetClassName(e.NewValue);
        }
    }
}
