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
    public partial class EventScriptPopupView : TranslatedWindow, IEditorView, IDataVerifiableView
    {
        readonly EventScriptPopupViewModel _vm = new();

        public string ViewTitle => _vm.ScriptType switch
        {
            EventScript.EventScriptType.Procs => "Procs Script Editor",
            EventScript.EventScriptType.AI => "AI Script Editor",
            _ => "Event Script Editor"
        };
        public bool IsLoaded => _vm.IsLoaded;
        public ViewModelBase? DataViewModel => _vm;

        public EventScriptPopupView() : this(EventScript.EventScriptType.Event) { }

        public EventScriptPopupView(EventScript.EventScriptType scriptType)
        {
            _vm.ScriptType = scriptType;
            InitializeComponent();
            Title = ViewTitle;
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
                var panel = ScriptEditorHelper.CreateArgControl(arg, i, JumpPointer_Click, UnitColorPick_Click);
                ArgsPanel.Children.Add(panel);

                if (arg.IsEditable)
                    hasEditableArgs = true;
            }

            WriteButton.IsEnabled = hasEditableArgs;
            RefreshButton.IsEnabled = true;
        }

        /// <summary>
        /// Read values from dynamically-created controls back into ViewModel CommandArgs.
        /// </summary>
        void SyncControlsToViewModel()
        {
            ScriptEditorHelper.SyncControlsToViewModel(_vm.CommandArgs, ArgsPanel);
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

        /// <summary>
        /// Open the 4-slot UNIT_COLOR picker (#1444) seeded with the argument's
        /// current value; on Apply, write the packed result back into the hex box
        /// for that argument so the existing Write path persists it (mirrors
        /// WinForms EventScriptInnerControl → EventUnitColorForm.JumpTo).
        /// </summary>
        async void UnitColorPick_Click(object? sender, RoutedEventArgs e)
        {
            if (sender is not Button btn || btn.Tag is not int argIndex)
                return;
            if (argIndex < 0 || argIndex >= _vm.CommandArgs.Count)
                return;

            // Prefer the live hex-box text (honours an in-flight edit) over the VM value.
            uint current = _vm.CommandArgs[argIndex].Value;
            var hexBox = FindArgHexBox(argIndex);
            if (hexBox != null && TryParseHex(hexBox.Text, out uint typed))
                current = typed;

            try
            {
                var picker = new EventUnitColorView();
                picker.NavigateTo(current);
                uint? result = await picker.ShowDialog<uint?>(this);
                if (result.HasValue)
                {
                    // Preserve sibling in-flight edits, then apply the picked value
                    // and rebuild so every arg's friendly DisplayName (incl. this
                    // UNIT_COLOR summary) re-resolves and shows immediately.
                    SyncControlsToViewModel();
                    _vm.CommandArgs[argIndex].Value = result.Value;
                    BuildArgsPanel();
                }
            }
            catch (Exception ex)
            {
                Log.Error("EventScriptPopupView.UnitColorPick failed: ", ex.ToString());
            }
        }

        /// <summary>Locate the hex TextBox created for argument <paramref name="argIndex"/>.</summary>
        TextBox? FindArgHexBox(int argIndex)
        {
            if (argIndex < 0 || argIndex >= ArgsPanel.Children.Count)
                return null;
            if (ArgsPanel.Children[argIndex] is not Border border || border.Child is not StackPanel stack)
                return null;
            // ScriptEditorHelper tags the hex box as exactly "hex_{index}";
            // match the exact tag so a future second hex input can't be grabbed.
            string wantTag = $"hex_{argIndex}";
            foreach (var child in stack.Children)
            {
                if (child is StackPanel valuePanel)
                {
                    foreach (var ctrl in valuePanel.Children)
                    {
                        if (ctrl is TextBox tb && tb.Tag is string tag && tag == wantTag)
                            return tb;
                    }
                }
            }
            return null;
        }

        static bool TryParseHex(string? text, out uint value)
        {
            value = 0;
            string txt = (text ?? "").Trim();
            if (txt.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                txt = txt.Substring(2);
            return uint.TryParse(txt, System.Globalization.NumberStyles.HexNumber, null, out value);
        }
    }
}
