using System;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Dialogs;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    /// <summary>
    /// Avalonia counterpart of WinForms <c>ToolTranslateROMForm</c>.
    /// Gap-sweep parity raise (#422) lifts the AXAML control surface from
    /// 3 controls to MEDIUM-verdict density (&gt;=27 of WF's 35 controls) by
    /// reproducing the WF Simple / Detail tab layout, the WF default-checked
    /// state on `useAutoTranslateCheckBox` / `X_MODIFIED_TEXT_ONLY` /
    /// `FontAutoGenelateCheckBox`, and the multibyte-only visibility rule on
    /// the JP-font override checkboxes.
    ///
    /// The action buttons (`Start Translation`, `Export All Texts`,
    /// `Import All Texts`, `Import Font`, `Change` font name) render with
    /// `IsEnabled="False"` plus a ToolTip referencing #536. They become
    /// real handlers once the Core extraction of `ToolTranslateROM` +
    /// `FETextDecode` + `RecycleAddress` + `FindOrignalROMByLang` lands.
    /// </summary>
    public partial class ToolTranslateROMView : TranslatedWindow, IEditorView
    {
        readonly ToolTranslateROMViewModel _vm = new();

        public string ViewTitle => "ROM Translation Tool";
        public bool IsLoaded => _vm.IsLoaded;

        public ToolTranslateROMView()
        {
            InitializeComponent();
            DataContext = _vm;
            _vm.Initialize();
        }

        // ---------- Simple tab browse handlers ----------

        async void SimpleFromRomBrowse_Click(object? sender, RoutedEventArgs e)
        {
            try
            {
                var path = await FileDialogHelper.OpenRomFile(this);
                if (!string.IsNullOrEmpty(path)) _vm.FromRomPath = path;
            }
            catch (Exception ex) { Log.Error("ToolTranslateROMView.SimpleFromRomBrowse: {0}", ex.Message); }
        }

        async void SimpleToRomBrowse_Click(object? sender, RoutedEventArgs e)
        {
            try
            {
                var path = await FileDialogHelper.OpenRomFile(this);
                if (!string.IsNullOrEmpty(path)) _vm.ToRomPath = path;
            }
            catch (Exception ex) { Log.Error("ToolTranslateROMView.SimpleToRomBrowse: {0}", ex.Message); }
        }

        async void ExtraFontRomBrowse_Click(object? sender, RoutedEventArgs e)
        {
            try
            {
                var path = await FileDialogHelper.OpenRomFile(this);
                if (!string.IsNullOrEmpty(path)) _vm.ExtraFontRomPath = path;
            }
            catch (Exception ex) { Log.Error("ToolTranslateROMView.ExtraFontRomBrowse: {0}", ex.Message); }
        }

        async void TranslateDataBrowse_Click(object? sender, RoutedEventArgs e)
        {
            try
            {
                var path = await FileDialogHelper.OpenFile(this,
                    R._("Translation Data"), "*.txt");
                if (!string.IsNullOrEmpty(path)) _vm.TranslateDataPath = path;
            }
            catch (Exception ex) { Log.Error("ToolTranslateROMView.TranslateDataBrowse: {0}", ex.Message); }
        }

        // ---------- Detail tab browse handlers ----------

        async void DetailFromRomBrowse_Click(object? sender, RoutedEventArgs e)
        {
            try
            {
                var path = await FileDialogHelper.OpenRomFile(this);
                if (!string.IsNullOrEmpty(path)) _vm.FromRomPath = path;
            }
            catch (Exception ex) { Log.Error("ToolTranslateROMView.DetailFromRomBrowse: {0}", ex.Message); }
        }

        async void DetailToRomBrowse_Click(object? sender, RoutedEventArgs e)
        {
            try
            {
                var path = await FileDialogHelper.OpenRomFile(this);
                if (!string.IsNullOrEmpty(path)) _vm.ToRomPath = path;
            }
            catch (Exception ex) { Log.Error("ToolTranslateROMView.DetailToRomBrowse: {0}", ex.Message); }
        }

        async void FontRomBrowse_Click(object? sender, RoutedEventArgs e)
        {
            try
            {
                var path = await FileDialogHelper.OpenRomFile(this);
                if (!string.IsNullOrEmpty(path)) _vm.FontRomPath = path;
            }
            catch (Exception ex) { Log.Error("ToolTranslateROMView.FontRomBrowse: {0}", ex.Message); }
        }

        // ---------- Deferred-action stubs ----------
        //
        // The five action handlers below are wired to the XAML Click events
        // but should never run: the corresponding Buttons render with
        // IsEnabled="False" + ToolTip.Tip referencing #536. They exist so
        // the parity tests can statically assert the IsEnabled / ToolTip
        // policy without missing-handler errors at XAML-load time. Real
        // implementations land once the WinForms-coupled ToolTranslateROM /
        // FETextDecode / RecycleAddress helpers move into Core (#536).

        void SimpleFire_Click(object? sender, RoutedEventArgs e) { /* deferred to #536 */ }
        void ExportAllText_Click(object? sender, RoutedEventArgs e) { /* deferred to #536 */ }
        void ImportAllText_Click(object? sender, RoutedEventArgs e) { /* deferred to #536 */ }
        void ImportFont_Click(object? sender, RoutedEventArgs e) { /* deferred to #536 */ }
        void UseFontName_Click(object? sender, RoutedEventArgs e) { /* deferred to #536 */ }

        public void NavigateTo(uint address) { /* tool dialog - nothing to navigate to */ }
        public void SelectFirstItem() { /* tool dialog - no list to seed */ }
    }
}
