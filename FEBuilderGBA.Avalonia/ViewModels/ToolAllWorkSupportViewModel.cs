using System;
using System.Collections.Generic;
using System.IO;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    /// <summary>
    /// READ-ONLY aggregator VM for the All-Work-Support tool (#1196). Scans
    /// <c>config/etc/**/worksupport_.txt</c> via <see cref="WorkSupportScannerCore"/>
    /// and exposes one project per file for the View to render as a tile.
    /// Loading never marks the VM dirty (nothing here mutates the ROM).
    ///
    /// <para>This is a multi-project aggregator, not a single-record ROM editor:
    /// it reads NO ROM bytes, so it deliberately does NOT participate in the
    /// data-verification contract (it is an orphan VM by design — see the
    /// orphan-VM check in AvaloniaFieldCompletenessTests).</para>
    /// </summary>
    public class ToolAllWorkSupportViewModel : ViewModelBase
    {
        bool _isLoaded;

        public bool IsLoaded { get => _isLoaded; set => SetField(ref _isLoaded, value); }

        /// <summary>The discovered projects from the last <see cref="LoadList"/>.</summary>
        public List<WorkSupportScannerCore.WorkProject> Projects { get; private set; }
            = new List<WorkSupportScannerCore.WorkProject>();

        /// <summary>
        /// Scan the configured <c>config/etc</c> directory for work-support
        /// projects. Returns an empty list (never null) when no ROM dir is set or
        /// no projects are configured. Read-only: wrapped in <c>IsLoading</c> and
        /// the VM is left clean.
        /// </summary>
        public List<WorkSupportScannerCore.WorkProject> LoadList()
        {
            IsLoading = true;
            try
            {
                string baseDir = CoreState.BaseDirectory;
                if (string.IsNullOrEmpty(baseDir))
                {
                    Projects = new List<WorkSupportScannerCore.WorkProject>();
                }
                else
                {
                    string etcDir = Path.Combine(baseDir, "config", "etc");
                    Projects = WorkSupportScannerCore.Scan(etcDir);
                }

                IsLoaded = true;
                return Projects;
            }
            catch (Exception ex)
            {
                Log.Error("ToolAllWorkSupportViewModel.LoadList failed: " + ex.ToString());
                Projects = new List<WorkSupportScannerCore.WorkProject>();
                return Projects;
            }
            finally
            {
                IsLoading = false;
                MarkClean();
            }
        }

        /// <summary>
        /// Run the work-support update check on every loaded project and set each
        /// project's <c>IsUpdateMark</c>. Mirrors WF <c>UpdateCheck()</c>. The
        /// network/date touches are injected so this stays testable offline.
        /// Returns the number of projects newly marked as updateable. Read-only:
        /// leaves the VM clean.
        /// </summary>
        public int UpdateCheckAll(
            Func<string, string> httpGet,
            Func<string, string> httpHeadLastModified,
            Func<string, DateTime> romDateTime)
        {
            IsLoading = true;
            int updateable = 0;
            try
            {
                foreach (var p in Projects)
                {
                    WorkSupportUpdateCheckCore.UpdateResult ur = WorkSupportUpdateCheckCore.Check(
                        p.UpdateinfoLines, p.RomFilename, httpGet, httpHeadLastModified, romDateTime);
                    bool mark = ur == WorkSupportUpdateCheckCore.UpdateResult.Updateable;
                    p.IsUpdateMark = mark;
                    if (mark) updateable++;
                }
            }
            catch (Exception ex)
            {
                Log.Error("ToolAllWorkSupportViewModel.UpdateCheckAll failed: " + ex.ToString());
            }
            finally
            {
                IsLoading = false;
                MarkClean();
            }
            return updateable;
        }

        /// <summary>Number of discovered projects (for tests / status display).</summary>
        public int GetListCount() => Projects.Count;
    }
}
