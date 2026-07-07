// SPDX-License-Identifier: GPL-3.0-or-later
// EmulatorMemoryView code-behind - gap-sweep #385 parity rebuild.
//
// Cross-platform Avalonia has no P/Invoke RAM reader (RAM.cs is
// Windows-only), so live-RAM features (Flag/Procs/MemorySlot/RunningEvent
// listboxes, cheat ROM-writes, dynamic event jumps) remain KnownGap in
// the AXAML (IsEnabled=False + #385 tooltip).
//
// What IS wired here: the 9 parameterless cross-editor Open<T>() jumps
// reachable from EmulatorMemoryForm in WF. They are routed through
// WindowManager.Open<TView>() so they work without a running emulator.
//
// There are NO functional ROM-mutating handlers in this view; the
// "All ROM writes wrapped in undo" gap-sweep acceptance criterion is
// vacuously true because no Avalonia handler ever calls
// ROM.SetU8/16/32 - the entire ROM-mutating surface is KnownGap.

using System;
using global::Avalonia;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class EmulatorMemoryView : TranslatedUserControl, IEmbeddableEditor
    {
        readonly EmulatorMemoryViewModel _vm = new();
        public string ViewTitle => "Emulator Memory";
        public new bool IsLoaded => _vm.IsLoaded;
        public EditorDescriptor Descriptor => new("Emulator Memory", 1280, 900);
        public event EventHandler? CloseRequested;
        public void RequestClose() => CloseRequested?.Invoke(this, EventArgs.Empty);

        public EmulatorMemoryView()
        {
            InitializeComponent();
            _vm.Initialize();
            // Bind DataContext so ConnectionStatus / AutoUpdate / NoticeText /
            // IsConnected bindings resolve (Copilot CLI v1 review non-blocking item).
            DataContext = _vm;
        }

        void Close_Click(object? sender, RoutedEventArgs e) => RequestClose();

        // ---- Functional cross-editor open buttons ----
        // Each opens the target editor parameterlessly via
        // WindowManager.Open<TView>(). Mirrors the 9 wired WF jump
        // callsites that don't depend on a live RAM address.

        void OpenEventScript_Click(object? sender, RoutedEventArgs e)
            => WindowManager.Instance.Open<EventScriptView>();

        void OpenProcsScript_Click(object? sender, RoutedEventArgs e)
            => WindowManager.Instance.Open<ProcsScriptView>();

        void OpenHexEditor_Click(object? sender, RoutedEventArgs e)
            => WindowManager.Instance.Open<HexEditorView>();

        void OpenTextViewer_Click(object? sender, RoutedEventArgs e)
            => WindowManager.Instance.Open<TextViewerView>();

        void OpenSongTable_Click(object? sender, RoutedEventArgs e)
            => WindowManager.Instance.Open<SongTableView>();

        void OpenToolBGMMuteDialog_Click(object? sender, RoutedEventArgs e)
            => WindowManager.Instance.Open<ToolBGMMuteDialogView>();

        void OpenMapChange_Click(object? sender, RoutedEventArgs e)
            => WindowManager.Instance.Open<MapChangeView>();

        void OpenRAMRewriteTool_Click(object? sender, RoutedEventArgs e)
            => WindowManager.Instance.Open<RAMRewriteToolView>();

        void OpenRAMRewriteToolMAP_Click(object? sender, RoutedEventArgs e)
            => WindowManager.Instance.Open<RAMRewriteToolMAPView>();

        public void NavigateTo(uint address) { }
        public void SelectFirstItem() { }
    }
}
