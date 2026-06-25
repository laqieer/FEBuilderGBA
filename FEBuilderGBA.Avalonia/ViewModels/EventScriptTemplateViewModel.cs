using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Text;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    // "Templates" browser: lists every template_list_event_ entry and previews
    // its disassembled event codes. Templates that require the parent event
    // editor's map/label context (XXXX/YYYY placeholders) are flagged and not
    // generatable here. (#1434)
    public class EventScriptTemplateViewModel : ViewModelBase
    {
        public ObservableCollection<string> TemplateInfos { get; } = new();

        List<EventTemplateCore.BrowserTemplate> _templates = new();

        int _selectedIndex = -1;
        string _preview = "";
        string _generatedHex = "";
        string _status = "";
        string _filename = "";

        public int SelectedIndex
        {
            get => _selectedIndex;
            set
            {
                if (SetField(ref _selectedIndex, value))
                {
                    OnSelectionChanged();
                }
            }
        }

        public string Preview { get => _preview; set => SetField(ref _preview, value); }
        public string GeneratedHex
        {
            get => _generatedHex;
            // HasGenerated is computed from this; notify it on every change so the
            // Copy button disables when switching to a context-required/unavailable
            // template (which clears the field).
            set { if (SetField(ref _generatedHex, value)) OnPropertyChanged(nameof(HasGenerated)); }
        }
        public string Status { get => _status; set => SetField(ref _status, value); }
        public string Filename { get => _filename; set => SetField(ref _filename, value); }
        public bool HasGenerated => !string.IsNullOrEmpty(_generatedHex);

        public void LoadList()
        {
            TemplateInfos.Clear();
            _templates = new List<EventTemplateCore.BrowserTemplate>();

            ROM rom = CoreState.ROM;
            if (rom?.RomInfo == null)
            {
                Status = R._("No ROM loaded.");
                return;
            }
            try
            {
                _templates = EventTemplateCore.LoadBrowserTemplates(rom);
            }
            catch (Exception ex)
            {
                Log.Error("EventScriptTemplate.LoadList failed: " + ex.ToString());
                return;
            }
            foreach (var et in _templates)
            {
                string label = et.Info;
                if (et.RequiresContext)
                {
                    label += "  " + R._("[requires event editor context]");
                }
                TemplateInfos.Add(label);
            }
            if (TemplateInfos.Count > 0)
            {
                SelectedIndex = 0;
            }
        }

        void OnSelectionChanged()
        {
            Preview = "";
            GeneratedHex = "";
            Status = "";
            Filename = "";

            if (_selectedIndex < 0 || _selectedIndex >= _templates.Count)
            {
                return;
            }
            ROM rom = CoreState.ROM;
            if (rom?.RomInfo == null)
            {
                return;
            }
            var et = _templates[_selectedIndex];
            Filename = et.Filename;

            if (et.RequiresContext)
            {
                Status = R._("This template requires the event editor context (map/label) and is not available here.");
                return;
            }

            byte[] bin;
            try
            {
                bin = EventTemplateCore.GenerateBrowserTemplate(rom, et);
            }
            catch (Exception ex)
            {
                Log.Error("EventScriptTemplate.Generate failed: " + ex.ToString());
                Status = R._("Generation failed.");
                return;
            }
            if (bin == null || bin.Length == 0)
            {
                Status = R._("This template requires the event editor context and is not available here.");
                return;
            }

            var lines = EventTemplateCore.DisassemblePreview(rom, bin);
            var prev = new StringBuilder();
            var hex = new StringBuilder();
            foreach (string line in lines)
            {
                prev.AppendLine(line);
                int tab = line.IndexOf('\t');
                hex.AppendLine(tab >= 0 ? line.Substring(0, tab) : line);
            }
            Preview = prev.ToString().TrimEnd();
            GeneratedHex = hex.ToString().TrimEnd();
            Status = string.Format(R._("Generated {0} byte(s)."), bin.Length);
        }
    }
}
