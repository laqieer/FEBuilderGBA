using System;
using System.Collections.Generic;
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
        readonly List<uint> _entryAddresses = new();

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

        /// <summary>Initialize with a specific base address (called from JumpTo / NavigateTo).</summary>
        public void InitializeWithAddress(uint baseAddr)
        {
            _baseAddress = baseAddr;
            ReadStartAddress = (int)baseAddr;
            IsLoading = true;
            try
            {
                LoadList();
            }
            catch (Exception ex)
            {
                Log.Error("SMEPromoListViewModel.InitializeWithAddress", ex.ToString());
            }
            IsLoaded = true;
            IsLoading = false;
            MarkClean();
        }

        public void Reload()
        {
            _baseAddress = (uint)ReadStartAddress;
            LoadList();
        }

        void LoadList()
        {
            AddressList.Clear();
            _entryAddresses.Clear();
            var rom = CoreState.ROM;
            if (rom == null) return;

            uint baseAddr = _baseAddress;
            if (baseAddr == 0 || baseAddr + 2 > (uint)rom.Data.Length) return;

            // Each entry is 2 bytes: base class ID (B0) + promo class ID (B1)
            // Terminated when u16(addr) == 0x0000
            int maxEntries = ReadCount > 0 ? ReadCount : 256;
            for (int i = 0; i < maxEntries; i++)
            {
                uint addr = baseAddr + (uint)(i * 2);
                if (addr + 2 > (uint)rom.Data.Length) break;
                if (rom.u16(addr) == 0x0000) break;

                uint b0 = rom.u8(addr);
                uint b1 = rom.u8(addr + 1);
                string name0 = NameResolver.GetClassName(b0);
                string name1 = NameResolver.GetClassName(b1);
                string display = $"0x{b0:X2} {name0} -> 0x{b1:X2} {name1}";
                AddressList.Add(display);
                _entryAddresses.Add(addr);
            }
        }

        void OnSelected()
        {
            if (_selectedIndex < 0 || _selectedIndex >= _entryAddresses.Count || CoreState.ROM == null) return;

            IsLoading = true;
            try
            {
                uint addr = _entryAddresses[_selectedIndex];
                CurrentAddress = (int)addr;
                SelectAddress = $"0x{addr:X08}";
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
            if (_selectedIndex < 0 || _selectedIndex >= _entryAddresses.Count || CoreState.ROM == null) return;

            uint addr = _entryAddresses[_selectedIndex];
            CoreState.ROM.write_u8(addr, (uint)B0);
            CoreState.ROM.write_u8(addr + 1, (uint)B1);

            // Update display list entry
            string name0 = NameResolver.GetClassName((uint)B0);
            string name1 = NameResolver.GetClassName((uint)B1);
            ClassName0 = name0;
            ClassName1 = name1;
            if (_selectedIndex < AddressList.Count)
                AddressList[_selectedIndex] = $"0x{(uint)B0:X2} {name0} -> 0x{(uint)B1:X2} {name1}";
        }
    }
}
