using System;
using System.Collections.ObjectModel;
using global::Avalonia;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using global::Avalonia.Layout;
using global::Avalonia.Media;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    /// <summary>
    /// Shared UI helpers for building script editor argument panels.
    /// Used by EventScriptPopupView, ProcsScriptCategorySelectView, and AIScriptCategorySelectView.
    /// </summary>
    internal static class ScriptEditorHelper
    {
        /// <summary>
        /// Create a control group for a single command argument.
        /// </summary>
        public static Border CreateArgControl(CommandArgEntry arg, int index, EventHandler<RoutedEventArgs>? jumpClickHandler)
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
                    if (jumpClickHandler != null)
                        jumpBtn.Click += jumpClickHandler;
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
        public static void SyncControlsToViewModel(ObservableCollection<CommandArgEntry> commandArgs, StackPanel argsPanel)
        {
            for (int i = 0; i < commandArgs.Count; i++)
            {
                var arg = commandArgs[i];
                if (arg.IsFixed)
                    continue;

                if (i >= argsPanel.Children.Count)
                    continue;

                var border = argsPanel.Children[i] as Border;
                if (border?.Child is not StackPanel stack)
                    continue;

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
    }
}
