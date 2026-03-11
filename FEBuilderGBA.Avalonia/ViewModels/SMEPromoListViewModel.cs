using System;
using System.Collections.ObjectModel;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    public class SMEPromoListViewModel : ViewModelBase
    {
        bool _isLoaded;
        int _selectedIndex = -1;
        uint _baseAddress;
        int _b0;
        int _b1;
        string _className0 = "";
        string _className1 = "";
        int _readStartAddress;
        int _readCount = 32;
        int _currentAddress;
        string _blockSize = "2";
        string _selectAddress = "";

        public bool IsLoaded { get => _isLoaded; set => SetField(ref _isLoaded, value); }
        public int SelectedIndex { get => _selectedIndex; set { SetField(ref _selectedIndex, value); OnSelected(); } }
        public int B0 { get => _b0; set => SetField(ref _b0, value); }
        public int B1 { get => _b1; set => SetField(ref _b1, value); }
        public string ClassName0 { get => _className0; set => SetField(ref _className0, value); }
        public string ClassName1 { get => _className1; set => SetField(ref _className1, value); }
        public int ReadStartAddress { get => _readStartAddress; set => SetField(ref _readStartAddress, value); }
        public int ReadCount { get => _readCount; set => SetField(ref _readCount, value); }
        public int CurrentAddress { get => _currentAddress; set => SetField(ref _currentAddress, value); }
        public string BlockSize { get => _blockSize; set => SetField(ref _blockSize, value); }
        public string SelectAddress { get => _selectAddress; set => SetField(ref _selectAddress, value); }
        public ObservableCollection<string> AddressList { get; } = new();

        public void Initialize()
        {
            IsLoading = true;
            try
            {
                LoadList();
            }
            catch (Exception ex)
            {
                Log.Error("SMEPromoListViewModel", ex.ToString());
            }
            IsLoaded = true;
            IsLoading = false;
            MarkClean();
        }

        public void Reload()
        {
            LoadList();
        }

        void LoadList()
        {
            AddressList.Clear();
            if (CoreState.ROM == null) return;

            // SME promo list is typically at a patch-defined address
            // Each entry is 2 bytes: base class ID, promo class ID
            // For now, show as placeholder - actual address depends on patch installation
        }

        void OnSelected()
        {
            if (_selectedIndex < 0 || CoreState.ROM == null) return;

            IsLoading = true;
            try
            {
                uint addr = _baseAddress + (uint)(_selectedIndex * 2);
                CurrentAddress = (int)addr;
                B0 = (int)CoreState.ROM.u8(addr);
                B1 = (int)CoreState.ROM.u8(addr + 1);
                ClassName0 = NameResolver.GetClassName((uint)B0);
                ClassName1 = NameResolver.GetClassName((uint)B1);
            }
            catch (Exception ex)
            {
                Log.Error("SMEPromoListViewModel.OnSelected", ex.ToString());
            }
            IsLoading = false;
            MarkClean();
        }

        public void WriteEntry()
        {
            if (_selectedIndex < 0 || CoreState.ROM == null) return;

            uint addr = _baseAddress + (uint)(_selectedIndex * 2);
            CoreState.ROM.write_u8(addr, (uint)B0);
            CoreState.ROM.write_u8(addr + 1, (uint)B1);
        }
    }
}
