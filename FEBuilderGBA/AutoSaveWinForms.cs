using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace FEBuilderGBA
{
    /// <summary>
    /// Timer-based auto-save for WinForms. Writes to a sidecar .autosave.gba file.
    /// Disabled by default; controlled via config keys autosave_enabled and autosave_interval_minutes.
    /// </summary>
    public sealed class AutoSaveWinForms
    {
        public static readonly AutoSaveWinForms Instance = new();

        Timer _timer;
        string _romFilename;
        volatile int _lastSavedUndoPosition = -1;
        volatile bool _writing;

        AutoSaveWinForms() { }

        public string CurrentRomFilename => _romFilename;

        /// <summary>
        /// Compute the auto-save sidecar file path from the primary ROM filename.
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
            _lastSavedUndoPosition = Program.Undo?.Postion ?? -1;

            if (intervalMinutes < 1) intervalMinutes = 5;

            _timer = new Timer
            {
                Interval = intervalMinutes * 60 * 1000
            };
            _timer.Tick += (_, _) => OnTick();
            _timer.Start();
        }

        public void Stop()
        {
            _timer?.Stop();
            _timer?.Dispose();
            _timer = null;
        }

        public bool IsRunning => _timer != null;

        public void UpdateRomFilename(string newFilename)
        {
            _romFilename = newFilename;
            _lastSavedUndoPosition = Program.Undo?.Postion ?? -1;
        }

        /// <summary>
        /// Mark the current undo position as saved (call after manual Save/Save As).
        /// </summary>
        public void MarkSaved()
        {
            _lastSavedUndoPosition = Program.Undo?.Postion ?? -1;
        }

        void OnTick()
        {
            if (_writing) return;

            if (Program.ROM == null) return;

            int currentPos = Program.Undo?.Postion ?? -1;
            if (currentPos == _lastSavedUndoPosition) return;

            string sidecar = ComputeSidecarPath(_romFilename);
            if (string.IsNullOrEmpty(sidecar)) return;

            if (string.Equals(sidecar, Program.ROM.Filename, StringComparison.OrdinalIgnoreCase)) return;
            if (string.Equals(sidecar, _romFilename, StringComparison.OrdinalIgnoreCase)) return;

            byte[] srcData = Program.ROM.Data;
            if (srcData == null || srcData.Length == 0) return;
            byte[] data = new byte[srcData.Length];
            Buffer.BlockCopy(srcData, 0, data, 0, srcData.Length);

            _writing = true;
            int savedPos = currentPos;
            var ctx = System.Threading.SynchronizationContext.Current;
            Task.Run(() =>
            {
                try
                {
                    string tempPath = sidecar + ".tmp";
                    File.WriteAllBytes(tempPath, data);
                    File.Move(tempPath, sidecar, true);
                    _lastSavedUndoPosition = savedPos;
                    // Marshal log notification to UI thread to avoid cross-thread ListBox access
                    if (ctx != null)
                        ctx.Post(_ => Log.Notify("Auto-saved to " + Path.GetFileName(sidecar)), null);
                }
                catch (Exception ex)
                {
                    if (ctx != null)
                        ctx.Post(_ => Log.ErrorF("Auto-save failed: {0}", ex.Message), null);
                }
                finally
                {
                    _writing = false;
                }
            });
        }

        /// <summary>
        /// Read auto-save config keys.
        /// </summary>
        public static (bool enabled, int intervalMinutes) ReadConfig()
        {
            bool enabled = Program.Config?.at("autosave_enabled", "false") == "true";
            int interval = 5;
            string val = Program.Config?.at("autosave_interval_minutes", "5");
            if (val != null && int.TryParse(val, out int parsed))
                interval = parsed;
            interval = Math.Clamp(interval, 1, 60);
            return (enabled, interval);
        }
    }
}
