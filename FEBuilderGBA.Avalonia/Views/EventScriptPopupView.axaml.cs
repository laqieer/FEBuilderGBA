using System;
using global::Avalonia;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using global::Avalonia.Layout;
using global::Avalonia.Media;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class EventScriptPopupView : Window, IEditorView, IDataVerifiableView
    {
        readonly EventScriptPopupViewModel _vm = new();

        public string ViewTitle => "Event Script Editor";
        public bool IsLoaded => _vm.IsLoaded;
        public ViewModelBase? DataViewModel => _vm;

        public EventScriptPopupView()
        {
            InitializeComponent();
            _vm.Load();
            CommandsList.ItemsSource = _vm.Commands;
        }

        void Close_Click(object? sender, RoutedEventArgs e)
        {
            Close();
        }

        void Disassemble_Click(object? sender, RoutedEventArgs e)
        {
            _vm.AddressText = AddressBox.Text ?? "";
            RunDisassemble();
        }

        void RunDisassemble()
        {
            if (_vm.TryParseAddress(out uint address))
            {
                _vm.DisassembleAt(address);
                StatusLabel.Text = _vm.StatusText;
                ClearArgsPanel();
            }
            else
            {
                StatusLabel.Text = "Invalid address. Enter a hex value like 0x08001234 or 1234.";
            }
        }

        void CommandsList_SelectionChanged(object? sender, SelectionChangedEventArgs e)
        {
            int index = CommandsList.SelectedIndex;
            _vm.SelectedCommandIndex = index;
            BuildArgsPanel();
        }

        void Write_Click(object? sender, RoutedEventArgs e)
        {
            // Read values from controls back into CommandArgs before writing
            SyncControlsToViewModel();

            if (_vm.WriteCommand())
            {
                StatusLabel.Text = _vm.StatusText;
                // Rebuild args panel to show refreshed data
                BuildArgsPanel();
            }
            else
            {
                StatusLabel.Text = _vm.StatusText;
            }
        }

        void Refresh_Click(object? sender, RoutedEventArgs e)
        {
            // Re-select to refresh args from ROM
            int idx = _vm.SelectedCommandIndex;
            if (idx >= 0)
            {
                _vm.SelectedCommandIndex = -1;
                _vm.SelectedCommandIndex = idx;
                BuildArgsPanel();
            }
        }

        /// <summary>Navigate to a specific address and disassemble.</summary>
        public void NavigateTo(uint address)
        {
            _vm.AddressText = $"0x{address:X08}";
            AddressBox.Text = _vm.AddressText;
            RunDisassemble();
        }

        public void SelectFirstItem()
        {
            if (CommandsList.ItemCount > 0)
                CommandsList.SelectedIndex = 0;
        }

        void ClearArgsPanel()
        {
            ArgsPanel.Children.Clear();
            CommandNameLabel.Text = "(select a command)";
            WriteButton.IsEnabled = false;
            RefreshButton.IsEnabled = false;
        }

        /// <summary>
        /// Dynamically build controls in ArgsPanel for each argument of the selected command.
        /// </summary>
        void BuildArgsPanel()
        {
            ArgsPanel.Children.Clear();

            if (!_vm.HasSelectedCommand || _vm.CommandArgs.Count == 0)
            {
                CommandNameLabel.Text = "(select a command)";
                WriteButton.IsEnabled = false;
                RefreshButton.IsEnabled = false;
                return;
            }

            CommandNameLabel.Text = _vm.SelectedCommandName;
            bool hasEditableArgs = false;

            for (int i = 0; i < _vm.CommandArgs.Count; i++)
            {
                var arg = _vm.CommandArgs[i];
                var panel = CreateArgControl(arg, i);
                ArgsPanel.Children.Add(panel);

                if (arg.IsEditable)
                    hasEditableArgs = true;
            }

            WriteButton.IsEnabled = hasEditableArgs;
            RefreshButton.IsEnabled = true;
        }

        /// <summary>
        /// Create a control group for a single argument.
        /// </summary>
        Border CreateArgControl(CommandArgEntry arg, int index)
        {
            var border = new Border
            {
                BorderBrush = new SolidColorBrush(Color.FromArgb(40, 128, 128, 128)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(8, 6),
                Margin = new Thickness(0, 0, 0, 2),
            };

            if (arg.IsFixed)
            {
                border.Background = new SolidColorBrush(Color.FromArgb(15, 128, 128, 128));
            }

            var stack = new StackPanel { Spacing = 4 };

            // Header row: name + type
            var headerPanel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            headerPanel.Children.Add(new TextBlock
            {
                Text = arg.Name,
                FontWeight = FontWeight.SemiBold,
                FontSize = 13,
            });
            headerPanel.Children.Add(new TextBlock
            {
                Text = $"[{arg.TypeLabel}]",
                FontSize = 11,
                Foreground = new SolidColorBrush(Colors.Gray),
                VerticalAlignment = VerticalAlignment.Center,
            });
            headerPanel.Children.Add(new TextBlock
            {
                Text = $"{arg.ByteSize}B @{arg.ByteOffset}",
                FontSize = 10,
                Foreground = new SolidColorBrush(Colors.DarkGray),
                VerticalAlignment = VerticalAlignment.Center,
            });
            stack.Children.Add(headerPanel);

            if (arg.IsFixed)
            {
                // Show fixed bytes as read-only hex
                var fixedLabel = new TextBlock
                {
                    Text = $"0x{arg.Value:X} (fixed constant)",
                    FontFamily = new FontFamily("Consolas"),
                    FontSize = 12,
                    Foreground = new SolidColorBrush(Colors.Gray),
                };
                stack.Children.Add(fixedLabel);
            }
            else
            {
                // Value row: hex input + decimal display (or vice versa)
                var valuePanel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };

                if (arg.IsPointer)
                {
                    // Pointer: hex text box + Jump button
                    var hexBox = new TextBox
                    {
                        Text = $"0x{arg.Value:X08}",
                        FontFamily = new FontFamily("Consolas"),
                        FontSize = 12,
                        Width = 160,
                        Tag = $"hex_{index}",
                        Name = $"ArgHex_{index}",
                    };
                    valuePanel.Children.Add(hexBox);

                    var jumpBtn = new Button
                    {
                        Content = "Jump",
                        FontSize = 11,
                        Padding = new Thickness(8, 2),
                        Tag = index,
                    };
                    jumpBtn.Click += JumpPointer_Click;
                    valuePanel.Children.Add(jumpBtn);
                }
                else if (arg.IsDecimal)
                {
                    // Decimal-preferred: NumericUpDown in decimal + hex label
                    var nud = new NumericUpDown
                    {
                        Value = arg.Value,
                        Minimum = 0,
                        Maximum = arg.MaxValue,
                        Increment = 1,
                        FontFamily = new FontFamily("Consolas"),
                        FontSize = 12,
                        Width = 140,
                        Tag = $"dec_{index}",
                        Name = $"ArgDec_{index}",
                        FormatString = "0",
                    };
                    valuePanel.Children.Add(nud);

                    valuePanel.Children.Add(new TextBlock
                    {
                        Text = $"(0x{arg.Value:X})",
                        FontFamily = new FontFamily("Consolas"),
                        FontSize = 11,
                        Foreground = new SolidColorBrush(Colors.Gray),
                        VerticalAlignment = VerticalAlignment.Center,
                    });
                }
                else
                {
                    // Hex-preferred: hex text box + decimal label
                    string hexFmt = arg.ByteSize switch
                    {
                        1 => $"0x{arg.Value:X02}",
                        2 => $"0x{arg.Value:X04}",
                        3 => $"0x{arg.Value:X06}",
                        _ => $"0x{arg.Value:X08}",
                    };
                    var hexBox = new TextBox
                    {
                        Text = hexFmt,
                        FontFamily = new FontFamily("Consolas"),
                        FontSize = 12,
                        Width = 140,
                        Tag = $"hex_{index}",
                        Name = $"ArgHex_{index}",
                    };
                    valuePanel.Children.Add(hexBox);

                    valuePanel.Children.Add(new TextBlock
                    {
                        Text = $"({arg.Value})",
                        FontFamily = new FontFamily("Consolas"),
                        FontSize = 11,
                        Foreground = new SolidColorBrush(Colors.Gray),
                        VerticalAlignment = VerticalAlignment.Center,
                    });
                }

                stack.Children.Add(valuePanel);

                // Display name row (for UNIT, CLASS, ITEM, TEXT, etc.)
                if (!string.IsNullOrEmpty(arg.DisplayName))
                {
                    var nameLabel = new TextBlock
                    {
                        Text = arg.DisplayName,
                        FontSize = 12,
                        Foreground = new SolidColorBrush(Color.FromRgb(80, 140, 200)),
                        TextWrapping = global::Avalonia.Media.TextWrapping.Wrap,
                    };
                    stack.Children.Add(nameLabel);
                }
            }

            border.Child = stack;
            return border;
        }

        /// <summary>
        /// Read values from dynamically-created controls back into ViewModel CommandArgs.
        /// </summary>
        void SyncControlsToViewModel()
        {
            for (int i = 0; i < _vm.CommandArgs.Count; i++)
            {
                var arg = _vm.CommandArgs[i];
                if (arg.IsFixed)
                    continue;

                // Find the control by scanning ArgsPanel children
                if (i >= ArgsPanel.Children.Count)
                    continue;

                var border = ArgsPanel.Children[i] as Border;
                if (border?.Child is not StackPanel stack)
                    continue;

                // Find value control within the stack
                foreach (var child in stack.Children)
                {
                    if (child is StackPanel valuePanel)
                    {
                        foreach (var ctrl in valuePanel.Children)
                        {
                            if (ctrl is TextBox tb && tb.Tag is string tagStr && tagStr.StartsWith("hex_"))
                            {
                                string txt = (tb.Text ?? "").Trim();
                                if (txt.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                                    txt = txt.Substring(2);
                                if (uint.TryParse(txt, System.Globalization.NumberStyles.HexNumber, null, out uint v))
                                    arg.Value = v;
                            }
                            else if (ctrl is NumericUpDown nud && nud.Tag is string nudTag && nudTag.StartsWith("dec_"))
                            {
                                if (nud.Value.HasValue)
                                    arg.Value = (uint)(decimal)nud.Value.Value;
                            }
                        }
                    }
                }
            }
        }

        void JumpPointer_Click(object? sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is int argIndex)
            {
                uint ptr = _vm.GetArgPointerValue(argIndex);
                if (ptr != U.NOT_FOUND)
                {
                    // Navigate: set address and re-disassemble at the pointer target
                    _vm.AddressText = $"0x{ptr:X08}";
                    AddressBox.Text = _vm.AddressText;
                    RunDisassemble();
                }
                else
                {
                    StatusLabel.Text = "Invalid or null pointer.";
                }
            }
        }
    }
}
