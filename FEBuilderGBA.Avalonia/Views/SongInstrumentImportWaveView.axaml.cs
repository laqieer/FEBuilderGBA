using System;
using System.IO;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Dialogs;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    /// <summary>
    /// DirectSound wav-import conversion dialog (#1448) — the Avalonia port of
    /// WinForms <c>SongInstrumentImportWaveForm</c>. Replaces the previous empty
    /// stub. Launched modally from <see cref="SongInstrumentView"/>'s N00/N08/N10/
    /// N18 import buttons: seeded with the chosen <c>.wav</c> bytes, it exposes the
    /// sox-resample / DPCM-compress / SNR-preview options and, on Import, returns
    /// the ready GBA DirectSound sample bytes via <c>ShowDialog&lt;byte[]?&gt;</c>.
    /// Cancel returns <c>null</c> (strict no-op — #1448 review pt 2).
    ///
    /// <para>Reachable standalone from the main menu too: when opened without a
    /// seed it prompts for a <c>.wav</c> file first so the window is never an empty
    /// placeholder.</para>
    /// </summary>
    public partial class SongInstrumentImportWaveView : TranslatedWindow, IEditorView
    {
        readonly SongInstrumentImportWaveViewModel _vm = new();
        bool _seeded;

        public string ViewTitle => "Wave Import";
        public bool IsLoaded => _vm.IsLoaded;

        public SongInstrumentImportWaveView()
        {
            InitializeComponent();
            DataContext = _vm;
            Opened += async (_, _) =>
            {
                // Standalone open (no seed): prompt for a .wav so the window is a
                // working tool, not a dead placeholder. Skip the prompt in the
                // headless screenshot/smoke runner so the options panel renders
                // unobstructed (the runner instantiates views without a user).
                if (!_seeded && !App.SmokeTestMode)
                {
                    await PromptForSourceAsync();
                }
            };
        }

        /// <summary>Seed the dialog with the chosen source .wav bytes + filename
        /// (called by <see cref="SongInstrumentView"/> before <c>ShowDialog</c>).</summary>
        public void Seed(byte[] sourceWav, string fileName)
        {
            _seeded = true;
            _vm.Seed(sourceWav, fileName);
        }

        async System.Threading.Tasks.Task PromptForSourceAsync()
        {
            try
            {
                // No ROM guard here (Copilot review #1537): Convert/Preview do not
                // need a ROM — HasHqMixer(null) safely returns false, so DPCM is
                // simply unavailable. The standalone window is a working convert +
                // preview tool even without a ROM; only the Import (write) step
                // needs one, and ImportSampleBytes fails gracefully if absent.
                string? path = await FileDialogHelper.OpenFile(this, R._("Import Wave"), "*.wav");
                if (string.IsNullOrEmpty(path))
                {
                    // No file chosen for the standalone window — close it cleanly.
                    Close((byte[]?)null);
                    return;
                }
                byte[] bytes = File.ReadAllBytes(path);
                _seeded = true;
                _vm.Seed(bytes, Path.GetFileName(path));
            }
            catch (Exception ex)
            {
                Log.Error("SongInstrumentImportWaveView.PromptForSourceAsync failed: " + ex.ToString());
                _vm.PreviewText = R._("Wave import failed: {0}", ex.Message);
            }
        }

        void Preview_Click(object? sender, RoutedEventArgs e)
        {
            try
            {
                _vm.RunPreview();
            }
            catch (Exception ex)
            {
                Log.Error("SongInstrumentImportWaveView.Preview_Click failed: " + ex.ToString());
                _vm.PreviewText = R._("Wave conversion failed: {0}", ex.Message);
            }
        }

        void Ok_Click(object? sender, RoutedEventArgs e)
        {
            try
            {
                byte[]? sample = _vm.Convert(out string err);
                if (sample == null)
                {
                    _vm.PreviewText = err ?? R._("Wave conversion failed.");
                    return; // keep the dialog open so the user can adjust options
                }
                Close(sample); // return the ready GBA-sample bytes to the caller
            }
            catch (Exception ex)
            {
                Log.Error("SongInstrumentImportWaveView.Ok_Click failed: " + ex.ToString());
                _vm.PreviewText = R._("Wave conversion failed: {0}", ex.Message);
            }
        }

        // Cancel = strict no-op: return null, mutate nothing (#1448 review pt 2).
        void Cancel_Click(object? sender, RoutedEventArgs e) => Close((byte[]?)null);

        public void NavigateTo(uint address) { /* options dialog — no list */ }
        public void SelectFirstItem() { /* options dialog — no list */ }
    }
}
