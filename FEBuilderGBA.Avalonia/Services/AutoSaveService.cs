using System;
using System.IO;
using global::Avalonia.Threading;

namespace FEBuilderGBA.Avalonia.Services
{
    /// <summary>
    /// Timer-based auto-save service for the Avalonia editor.
    /// Writes to a sidecar .autosave.gba file (never overwrites the primary ROM).
    /// Disabled by default; controlled via config keys autosave_enabled and autosave_interval_minutes.
    /// </summary>
    public sealed class AutoSaveService
    {
        public static readonly AutoSaveService Instance = new();

        DispatcherTimer _timer;
        string _romFilename;

        AutoSaveService() { }

        /// <summary>
        /// Compute the auto-save sidecar file path from the primary ROM filename.
        /// Returns {dir}/{base}.autosave.gba.
        /// </summary>
        public static string ComputeSidecarPath(string romFilename)
        {
            if (string.IsNullOrEmpty(romFilename)) return null;
            string dir = Path.GetDirectoryName(romFilename) ?? ".";
            string baseName = Path.GetFileNameWithoutExtension(romFilename);
            return Path.Combine(dir, baseName + ".autosave.gba");
        }

        public void Start(int intervalMinutes, string romFilename)
        {
            Stop();
            _romFilename = romFilename;

            if (intervalMinutes < 1) intervalMinutes = 5;

            _timer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMinutes(intervalMinutes)
            };
            _timer.Tick += (_, _) => OnTick();
            _timer.Start();
        }

        public void Stop()
        {
            _timer?.Stop();
            _timer = null;
        }

        public void UpdateRomFilename(string newFilename)
        {
            _romFilename = newFilename;
        }

        void OnTick()
        {
            var rom = CoreState.ROM;
            if (rom == null) return;

            // Only save if there are unsaved changes
            if (CoreState.Undo == null || !CoreState.Undo.IsModified) return;

            string sidecar = ComputeSidecarPath(_romFilename);
            if (string.IsNullOrEmpty(sidecar)) return;

            // Belt-and-suspenders: never overwrite the primary ROM
            if (string.Equals(sidecar, rom.Filename, StringComparison.OrdinalIgnoreCase)) return;

            try
            {
                File.WriteAllBytes(sidecar, rom.Data);
                Log.Notify("Auto-saved to " + Path.GetFileName(sidecar));
            }
            catch (Exception ex)
            {
                Log.Error("Auto-save failed: {0}", ex.Message);
            }
        }

        /// <summary>
        /// Read auto-save config and return (enabled, intervalMinutes).
        /// </summary>
        public static (bool enabled, int intervalMinutes) ReadConfig()
        {
            bool enabled = CoreState.Config?.at("autosave_enabled", "false") == "true";
            int interval = 5;
            string val = CoreState.Config?.at("autosave_interval_minutes", "5");
            if (val != null) int.TryParse(val, out interval);
            if (interval < 1) interval = 1;
            if (interval > 60) interval = 60;
            return (enabled, interval);
        }
    }
}
