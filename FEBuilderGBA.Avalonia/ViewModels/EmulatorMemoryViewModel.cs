// SPDX-License-Identifier: GPL-3.0-or-later
// EmulatorMemoryViewModel - gap-sweep #385 parity rebuild.
//
// The cross-platform Avalonia version of FEBuilderGBA has no P/Invoke
// RAM reader (RAM.cs in WinForms uses Windows-specific OpenProcess +
// ReadProcessMemory). This VM therefore exposes no live RAM bindings;
// it serves only as a structural anchor for the view, holds the
// platform-limitation notice, and (via the
// NavigationTargets.cs partial) declares the cross-editor jump
// manifest required by INavigationTargetSource for the gap-sweep
// Phase 4 parity scanner.

namespace FEBuilderGBA.Avalonia.ViewModels
{
    public partial class EmulatorMemoryViewModel : ViewModelBase
    {
        bool _isLoaded;
        string _noticeText = "Emulator memory reading requires Windows P/Invoke and is not available in the cross-platform Avalonia version.\n\nThis feature uses Windows-specific APIs to read the memory of a running GBA emulator process for live debugging. Please use the Windows (WinForms) version of FEBuilderGBA for this functionality.";
        bool _autoUpdate;
        bool _isConnected;
        string _connectionStatus = "Not Connected";

        public bool IsLoaded { get => _isLoaded; set => SetField(ref _isLoaded, value); }
        /// <summary>Notice text explaining platform limitation.</summary>
        public string NoticeText { get => _noticeText; set => SetField(ref _noticeText, value); }
        /// <summary>Whether auto-update polling is enabled (no-op without live RAM reader).</summary>
        public bool AutoUpdate { get => _autoUpdate; set => SetField(ref _autoUpdate, value); }
        /// <summary>Whether the emulator connection is active (always false in Avalonia).</summary>
        public bool IsConnected { get => _isConnected; set => SetField(ref _isConnected, value); }
        /// <summary>Current connection status text.</summary>
        public string ConnectionStatus { get => _connectionStatus; set => SetField(ref _connectionStatus, value); }

        public void Initialize()
        {
            IsLoaded = true;
        }
    }
}
