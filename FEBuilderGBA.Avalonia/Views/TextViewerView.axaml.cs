using System;
using global::Avalonia.Controls;
using global::Avalonia.Controls.Documents;
using global::Avalonia.Interactivity;
using global::Avalonia.Media;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class TextViewerView : Window, IEditorView, IDataVerifiableView
    {
        readonly TextViewerViewModel _vm = new();

        public string ViewTitle => "Text Editor";
        public bool IsLoaded => _vm.CanWrite;

        public TextViewerView()
        {
            InitializeComponent();
            TextList.SelectedAddressChanged += OnTextSelected;
            Opened += (_, _) => LoadList();
        }

        void LoadList()
        {
            try
            {
                var items = _vm.LoadTextList();
                TextList.SetItems(items);
            }
            catch (Exception ex)
            {
                Log.Error("TextViewerView.LoadList failed: {0}", ex.Message);
            }
        }

        void OnTextSelected(uint addr)
        {
            try
            {
                // The addr is the pointer table entry address, but we need the text ID (index)
                // The AddrResult.tag contains the index
                ROM rom = CoreState.ROM;
                if (rom?.RomInfo == null) return;

                uint textPtr = rom.RomInfo.text_pointer;
                if (textPtr == 0) return;
                uint baseAddr = rom.p32(textPtr);
                if (baseAddr == 0 || !U.isSafetyOffset(baseAddr)) return;

                uint id = (addr - baseAddr) / 4;
                _vm.LoadText(id);
                UpdateUI();
            }
            catch (Exception ex)
            {
                Log.Error("TextViewerView.OnTextSelected failed: {0}", ex.Message);
            }
        }

        public void NavigateTo(uint address)
        {
            TextList.SelectAddress(address);
        }

        static readonly IBrush BlueBrush = new SolidColorBrush(Colors.Blue);

        void UpdateUI()
        {
            TextIdLabel.Text = $"Text ID: 0x{_vm.CurrentId:X04}";
            ApplyHighlightedText(DecodedTextBlock, _vm.DecodedText);
        }

        /// <summary>
        /// Parse text for [...] control code sequences and render them in blue.
        /// Mirrors WinForms TextForm.KeywordHighLightFEditor() bracket scanning.
        /// </summary>
        static void ApplyHighlightedText(SelectableTextBlock block, string text)
        {
            block.Inlines?.Clear();
            if (block.Inlines == null || string.IsNullOrEmpty(text))
            {
                block.Text = text ?? "";
                return;
            }

            int i = 0;
            int normalStart = 0;
            while (i < text.Length)
            {
                if (text[i] == '[')
                {
                    // Find matching ']'
                    int close = text.IndexOf(']', i + 1);
                    if (close > i)
                    {
                        // Emit any preceding normal text
                        if (i > normalStart)
                            block.Inlines.Add(new Run(text.Substring(normalStart, i - normalStart)));
                        // Emit the bracketed control code in blue
                        block.Inlines.Add(new Run(text.Substring(i, close - i + 1))
                        {
                            Foreground = BlueBrush
                        });
                        i = close + 1;
                        normalStart = i;
                        continue;
                    }
                }
                i++;
            }
            // Emit trailing normal text
            if (normalStart < text.Length)
                block.Inlines.Add(new Run(text.Substring(normalStart)));
        }

        public void SelectFirstItem()
        {
            TextList.SelectFirst();
        }

        public ViewModelBase? DataViewModel => _vm;
    }
}
