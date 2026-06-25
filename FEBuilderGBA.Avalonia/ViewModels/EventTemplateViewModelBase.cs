using System;
using System.Collections.Generic;
using System.Text;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    // Shared logic for the numbered Event Template windows (Template 1..6).
    // Each window is a button grid that generates real event bytes via
    // EventTemplateCore, plus a disassembled hex preview the user copies into
    // the event editor. (#1434)
    public abstract class EventTemplateViewModelBase : ViewModelBase
    {
        string _generatedHex = "";
        string _preview = "";
        string _status = "";

        // The config-style hex lines (one per OneCode) that the event editor's
        // clipboard/paste import consumes.
        public string GeneratedHex { get => _generatedHex; set => SetField(ref _generatedHex, value); }

        // Human-readable disassembled preview shown in the read-only TextBox.
        public string Preview { get => _preview; set => SetField(ref _preview, value); }

        public string Status { get => _status; set => SetField(ref _status, value); }

        public bool HasGenerated => !string.IsNullOrEmpty(_generatedHex);

        protected abstract int TemplateNumber { get; }

        public List<EventTemplateCore.TemplateButton> GetButtons()
        {
            return EventTemplateCore.GetTemplateButtons(TemplateNumber);
        }

        // Generate the bytes for one button and refresh the preview + copy text.
        // Returns false (with Status set) when generation is unavailable.
        public bool GenerateButton(EventTemplateCore.TemplateButton btn)
        {
            GeneratedHex = "";
            Preview = "";
            Status = "";

            ROM rom = CoreState.ROM;
            if (rom?.RomInfo == null)
            {
                Status = R._("No ROM loaded.");
                return false;
            }
            if (btn == null)
            {
                Status = R._("No template selected.");
                return false;
            }

            byte[] bin;
            try
            {
                bin = EventTemplateCore.GenerateButton(rom, btn);
            }
            catch (Exception ex)
            {
                Log.Error("EventTemplate.GenerateButton failed: " + ex.Message);
                Status = R._("Generation failed.");
                return false;
            }

            if (bin == null || bin.Length == 0)
            {
                Status = R._("This template requires the event editor context and is not available here.");
                return false;
            }

            var lines = EventTemplateCore.DisassemblePreview(rom, bin);
            var hex = new StringBuilder();
            var prev = new StringBuilder();
            if (lines.Count > 0)
            {
                foreach (string line in lines)
                {
                    prev.AppendLine(line);
                    int tab = line.IndexOf('\t');
                    hex.AppendLine(tab >= 0 ? line.Substring(0, tab) : line);
                }
            }
            else
            {
                // Fallback: raw hex when disassembly is unavailable.
                var raw = new StringBuilder();
                foreach (byte b in bin) raw.Append(U.ToHexString(b));
                hex.AppendLine(raw.ToString());
                prev.AppendLine(raw.ToString());
            }

            GeneratedHex = hex.ToString().TrimEnd();
            Preview = prev.ToString().TrimEnd();
            Status = string.Format(R._("Generated {0} byte(s)."), bin.Length);
            OnPropertyChanged(nameof(HasGenerated));
            return true;
        }
    }
}
