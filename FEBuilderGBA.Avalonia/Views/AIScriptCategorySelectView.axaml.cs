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
    public partial class AIScriptCategorySelectView : TranslatedUserControl, IEmbeddableEditor, IDataVerifiableView
    {
        readonly EventScriptPopupViewModel _vm = new()
        {
            ScriptType = EventScript.EventScriptType.AI
        };

        public string ViewTitle => "AI Script Editor";
        public new bool IsLoaded => _vm.IsLoaded;
        public EditorDescriptor Descriptor => new("AI Script Editor", 1000, 750, SizeToContent: global::Avalonia.Controls.SizeToContent.WidthAndHeight);
        public event EventHandler? CloseRequested;
        public ViewModelBase? DataViewModel => _vm;
        public void RequestClose() => CloseRequested?.Invoke(this, EventArgs.Empty);

        public AIScriptCategorySelectView()
        {
            InitializeComponent();
            _vm.Load();
            CommandsList.ItemsSource = _vm.Commands;
        }

        void Close_Click(object? sender, RoutedEventArgs e)
        {
            RequestClose();
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
            SyncControlsToViewModel();

            if (_vm.WriteCommand())
            {
                StatusLabel.Text = _vm.StatusText;
                BuildArgsPanel();
            }
            else
            {
                StatusLabel.Text = _vm.StatusText;
            }
        }

        void Refresh_Click(object? sender, RoutedEventArgs e)
        {
            int idx = _vm.SelectedCommandIndex;
            if (idx >= 0)
            {
                _vm.SelectedCommandIndex = -1;
                _vm.SelectedCommandIndex = idx;
                BuildArgsPanel();
            }
        }

        async void CommandPicker_Click(object? sender, RoutedEventArgs e)
        {
            var result = await WindowManager.Instance.OpenModal<ScriptCommandPickerView, EventScript.Script?>(
                TopLevel.GetTopLevel(this) as Window,
                picker => picker.Configure(EventScript.EventScriptType.AI));
            if (result != null)
            {
                string name = EventScript.makeCommandComboText(result, true);
                StatusLabel.Text = $"Selected: {name}";
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
                var panel = ScriptEditorHelper.CreateArgControl(arg, i, JumpPointer_Click);
                ArgsPanel.Children.Add(panel);

                if (arg.IsEditable)
                    hasEditableArgs = true;
            }

            WriteButton.IsEnabled = hasEditableArgs;
            RefreshButton.IsEnabled = true;
        }

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
