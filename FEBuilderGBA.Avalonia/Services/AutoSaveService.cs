using System;
using System.IO;
using System.Threading.Tasks;
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
        int _lastSavedUndoPosition = -1;
        bool _writing;

        AutoSaveService() { }

        /// <summary>
        /// The current ROM filename tracked by the service (updated on Save As).
        /// </summary>
        public string CurrentRomFilename => _romFilename;

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
            _lastSavedUndoPosition = CoreState.Undo?.Postion ?? -1;

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

        public bool IsRunning => _timer != null;

        public void UpdateRomFilename(string newFilename)
        {
            _romFilename = newFilename;
            // Reset position so we don't skip the first save after rename
            _lastSavedUndoPosition = CoreState.Undo?.Postion ?? -1;
        }

        /// <summary>
        /// Mark the current undo position as "saved" so autosave skips until next edit.
        /// Call this after a successful manual Save or Save As.
        /// </summary>
        public void MarkSaved()
        {
            _lastSavedUndoPosition = CoreState.Undo?.Postion ?? -1;
        }

        void OnTick()
        {
            if (_writing) return;

            var rom = CoreState.ROM;
            if (rom == null) return;

            // Only save if undo position changed since last autosave/manual save
            int currentPos = CoreState.Undo?.Postion ?? -1;
            if (currentPos == _lastSavedUndoPosition) return;

            string sidecar = ComputeSidecarPath(_romFilename);
            if (string.IsNullOrEmpty(sidecar)) return;

            // Belt-and-suspenders: never overwrite the primary ROM
            if (string.Equals(sidecar, rom.Filename, StringComparison.OrdinalIgnoreCase)) return;
            if (string.Equals(sidecar, _romFilename, StringComparison.OrdinalIgnoreCase)) return;

            // Snapshot ROM data on UI thread to avoid torn writes
            byte[] srcData = rom.Data;
            if (srcData == null || srcData.Length == 0) return;
            byte[] data = new byte[srcData.Length];
            Buffer.BlockCopy(srcData, 0, data, 0, srcData.Length);

            _writing = true;
            int savedPos = currentPos;
            Task.Run(() =>
            {
                try
                {
                    // Write to temp file first, then move for atomicity
                    string tempPath = sidecar + ".tmp";
                    File.WriteAllBytes(tempPath, data);
                    File.Move(tempPath, sidecar, true);
                    _lastSavedUndoPosition = savedPos;
                    Log.Notify("Auto-saved to " + Path.GetFileName(sidecar));
                }
                catch (Exception ex)
                {
                    Log.Error("Auto-save failed: {0}", ex.Message);
                }
                finally
                {
                    _writing = false;
                }
            });
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
