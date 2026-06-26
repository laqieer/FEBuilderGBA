using System;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class EventScriptTemplateView : TranslatedWindow, IEditorView
    {
        readonly EventScriptTemplateViewModel _vm = new();

        public string ViewTitle => "Script Template Browser";
        public bool IsLoaded => true;

        public EventScriptTemplateView()
        {
            InitializeComponent();
            DataContext = _vm;
            Opened += (_, _) => LoadList();
        }

        void LoadList()
        {
            try
            {
                _vm.LoadList();
            }
            catch (Exception ex)
            {
                Log.Error("EventScriptTemplateView.LoadList failed: " + ex.ToString());
            }
        }

        async void CopyHex_Click(object? sender, RoutedEventArgs e)
        {
            try
            {
                if (!_vm.HasGenerated)
                {
                    return;
                }
                var clipboard = Clipboard;
                if (clipboard != null)
                {
                    await clipboard.SetTextAsync(_vm.GeneratedHex);
                    _vm.Status = R._("Copied generated hex to clipboard.");
                }
            }
            catch (Exception ex)
            {
                Log.Error("EventScriptTemplateView.CopyHex failed: " + ex.ToString());
            }
        }

        /// <summary>
        /// Insert the currently-generated (placeholder-free) template directly into the
        /// open Event Script editor (#1585 in-editor template insert). Opens/activates
        /// <see cref="EventScriptView"/> and calls its <c>InsertCurrentTemplate</c>. The
        /// target editor must already have a script DISASSEMBLED — InsertTemplate refuses
        /// against an empty list — so if no script is loaded we guide the user to
        /// disassemble an event first rather than silently doing nothing (Copilot plan
        /// review #1585 finding #2).
        /// </summary>
        void SendToEditor_Click(object? sender, RoutedEventArgs e)
        {
            try
            {
                var codes = _vm.GetGeneratedCodes();
                if (codes == null || codes.Count == 0)
                {
                    _vm.Status = R._("Nothing to send: select a generatable (placeholder-free) template first.");
                    return;
                }

                var view = WindowManager.Instance.Open<EventScriptView>();
                if (view == null)
                {
                    _vm.Status = R._("Could not open the Event Script editor.");
                    return;
                }
                if (!view.HasLoadedScript)
                {
                    _vm.Status = R._("Open the Event Script editor and Disassemble an event address first, then Send to Event Editor.");
                    return;
                }

                bool ok = view.InsertCurrentTemplate(codes);
                _vm.Status = ok
                    ? string.Format(R._("Inserted {0} command(s) into the Event Script editor. Review, then Write All."), codes.Count)
                    : R._("Insert failed: disassemble an event in the Event Script editor first.");
            }
            catch (Exception ex)
            {
                Log.Error("EventScriptTemplateView.SendToEditor failed: " + ex.ToString());
                _vm.Status = R._("Send to editor failed.");
            }
        }

        public void NavigateTo(uint address) { }
        public void SelectFirstItem() { }
    }
}
