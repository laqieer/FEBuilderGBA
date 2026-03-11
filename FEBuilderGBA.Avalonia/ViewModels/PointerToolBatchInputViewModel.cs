using System;
using System.Globalization;
using System.Text;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    public class PointerToolBatchInputViewModel : ViewModelBase
    {
        bool _isLoaded;
        string _batchInput = string.Empty;
        string _batchOutput = string.Empty;
        string _dialogResult = "";

        public bool IsLoaded { get => _isLoaded; set => SetField(ref _isLoaded, value); }
        /// <summary>Multi-line text input with addresses to batch-convert.</summary>
        public string BatchInput { get => _batchInput; set => SetField(ref _batchInput, value); }
        /// <summary>Result of the batch address conversion.</summary>
        public string BatchOutput { get => _batchOutput; set => SetField(ref _batchOutput, value); }
        public string DialogResult { get => _dialogResult; set => SetField(ref _dialogResult, value); }

        public void Initialize()
        {
            IsLoaded = true;
        }

        /// <summary>Process batch input: convert each line from address to GBA pointer.</summary>
        public void ProcessBatch()
        {
            if (string.IsNullOrWhiteSpace(BatchInput))
            {
                BatchOutput = "";
                return;
            }

            var sb = new StringBuilder();
            foreach (string rawLine in BatchInput.Split('\n'))
            {
                string line = rawLine.Trim();
                if (string.IsNullOrEmpty(line))
                {
                    sb.AppendLine();
                    continue;
                }

                string hex = line;
                if (hex.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                    hex = hex.Substring(2);

                if (uint.TryParse(hex, NumberStyles.HexNumber, null, out uint addr))
                    sb.AppendLine($"0x{(addr + 0x08000000):X08}");
                else
                    sb.AppendLine($"ERROR: {line}");
            }

            BatchOutput = sb.ToString().TrimEnd();
        }
    }
}
