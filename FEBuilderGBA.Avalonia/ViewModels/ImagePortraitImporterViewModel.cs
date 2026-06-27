// SPDX-License-Identifier: GPL-3.0-or-later
// Portrait Import Wizard view-model — first meaningful slice (#657).
//
// Holds the picked / quantized image plus the currently selected portrait
// slot address. The view delegates the actual ROM write to
// PortraitImportHelper, so the VM stays a thin state holder.
using System.Collections.Generic;
using FEBuilderGBA.Avalonia.Services;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    public class ImagePortraitImporterViewModel : ViewModelBase
    {
        uint _currentAddr;
        bool _isLoaded;
        ImageImportService.LoadResult _loadedImage;

        /// <summary>Currently selected portrait slot address (0 if none).</summary>
        public uint CurrentAddr { get => _currentAddr; set => SetField(ref _currentAddr, value); }

        /// <summary>Whether a slot has been selected (entries list loaded).</summary>
        public bool IsLoaded { get => _isLoaded; set => SetField(ref _isLoaded, value); }

        /// <summary>
        /// The most recently picked & quantized image (or null if no file
        /// chosen yet). The View builds the preview and the Import handler
        /// reads this when writing to ROM.
        /// </summary>
        public ImageImportService.LoadResult LoadedImage
        {
            get => _loadedImage;
            set => SetField(ref _loadedImage, value);
        }

        /// <summary>
        /// Enumerate the portrait slots in the loaded ROM. Mirrors
        /// <see cref="ImagePortraitViewModel.LoadList"/> so the wizard's left
        /// list shows the same per-slot entries as the main portrait editor.
        /// Returns an empty list when no ROM is loaded.
        /// </summary>
        public List<AddrResult> LoadList()
        {
            ROM rom = CoreState.ROM;
            if (rom?.RomInfo == null) return new List<AddrResult>();

            uint pointer = rom.RomInfo.portrait_pointer;
            uint dataSize = rom.RomInfo.portrait_datasize;
            if (pointer == 0 || dataSize == 0) return new List<AddrResult>();

            uint baseAddr = rom.p32(pointer);
            if (!U.isSafetyOffset(baseAddr, rom)) return new List<AddrResult>();

            var result = new List<AddrResult>();
            int nullCount = 0;
            for (int i = 0; i < 512; i++)
            {
                uint addr = baseAddr + (uint)(i * dataSize);
                if (addr + dataSize > (uint)rom.Data.Length) break;
                if (rom.u32(addr) == 0) { nullCount++; if (nullCount > 3) break; }
                else nullCount = 0;

                string name = $"0x{i:X2}";
                // #656 — resolve the portrait OWNER's name (scans unit/class tables
                // for a unit/class with portrait_id == i) instead of treating the
                // portrait index as a 0-based unit-table row. Mirrors the editor
                // fix at ImagePortraitViewModel.LoadList (#654/#673) and the
                // WinForms reference ImagePortraitForm.GetPortraitNameFast.
                try
                {
                    string pname = NameResolver.GetPortraitName((uint)i);
                    if (!string.IsNullOrEmpty(pname)) name += $" {pname}";
                }
                catch (System.Exception ex)
                {
                    Log.ErrorF("ImagePortraitImporterViewModel.LoadList portrait name resolve: {0}", ex.Message);
                }
                result.Add(new AddrResult(addr, name, (uint)i));
            }
            IsLoaded = result.Count > 0;
            return result;
        }

        /// <summary>
        /// Set the current slot address when the user selects an entry in
        /// the address list. The wizard's Import handler uses this address
        /// as the write target.
        /// </summary>
        public void LoadEntry(uint addr)
        {
            ROM rom = CoreState.ROM;
            if (rom == null) return;

            CurrentAddr = addr;
            IsLoaded = true;
        }

        /// <summary>
        /// Whether the wizard currently has both an image AND a slot, i.e.
        /// the Import button can be enabled. The View binds the button's
        /// IsEnabled to this value via <c>RefreshImportButtonState</c>.
        /// </summary>
        public bool CanImport => _loadedImage != null && _loadedImage.Success && _currentAddr != 0;
    }
}
