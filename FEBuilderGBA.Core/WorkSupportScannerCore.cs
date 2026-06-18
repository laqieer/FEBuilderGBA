using System;
using System.Collections.Generic;
using System.IO;

namespace FEBuilderGBA
{
    /// <summary>
    /// GUI-free scanner for the All-Work-Support aggregator (#1196). Ports the
    /// READ-ONLY discovery half of WinForms <c>ToolAllWorkSupportForm.ReloadWorks</c>:
    /// walk <c>config/etc/**/worksupport_.txt</c>, read each project's ROM filename,
    /// resolve + parse its <c>.updateinfo.txt</c>, and surface the project name +
    /// logo path. No ROM is read or mutated; nothing here throws.
    ///
    /// <para><see cref="GetUpdateInfo"/> + <see cref="LoadUpdateInfo"/> are ported
    /// verbatim from WinForms <c>ToolWorkSupportForm</c> (which is not in Core).</para>
    /// </summary>
    public static class WorkSupportScannerCore
    {
        /// <summary>One discovered project, mirroring WF <c>ToolAllWorkSupportForm.Work</c>.</summary>
        public sealed class WorkProject
        {
            /// <summary>Absolute path to the project ROM (worksupport field 0).</summary>
            public string RomFilename = "";

            /// <summary>Absolute path to the project logo image, or "" when none.</summary>
            public string LogoFilename = "";

            /// <summary>Display name (update-info NAME, else the ROM filename stem).</summary>
            public string Name = "";

            /// <summary>Raw parsed update-info key/value lines.</summary>
            public Dictionary<string, string> UpdateinfoLines = new Dictionary<string, string>();

            /// <summary>
            /// Whether an update is available. Always <c>false</c> on a plain scan —
            /// WF only sets it after an explicit user-triggered (network) update check.
            /// </summary>
            public bool IsUpdateMark;
        }

        /// <summary>
        /// Scan <paramref name="etcDir"/> recursively for <c>worksupport_.txt</c>
        /// files and return one <see cref="WorkProject"/> per project whose ROM and
        /// update-info both exist on disk. Returns an empty list (never null) when
        /// the dir is missing/empty; a malformed worksupport file is skipped.
        /// </summary>
        public static List<WorkProject> Scan(string etcDir)
        {
            var result = new List<WorkProject>();
            if (string.IsNullOrEmpty(etcDir) || !Directory.Exists(etcDir))
            {
                return result;
            }

            string[] files;
            try
            {
                files = U.Directory_GetFiles_Safe(etcDir, "worksupport_.txt", SearchOption.AllDirectories);
            }
            catch (Exception)
            {
                return result;
            }

            foreach (string filename in files)
            {
                WorkProject w = TryLoadProject(filename);
                if (w != null)
                {
                    result.Add(w);
                }
            }
            return result;
        }

        /// <summary>
        /// Load a single project from one <c>worksupport_.txt</c> path, or return
        /// <c>null</c> when the file is malformed, the ROM is missing, or no
        /// update-info exists. Never throws.
        /// </summary>
        public static WorkProject TryLoadProject(string worksupportFilename)
        {
            try
            {
                if (string.IsNullOrEmpty(worksupportFilename) || !File.Exists(worksupportFilename))
                {
                    return null;
                }

                Dictionary<uint, string> etc = U.LoadTSVResource1(worksupportFilename, false);
                string romfilename = U.at(etc, (uint)0);
                if (string.IsNullOrEmpty(romfilename) || !File.Exists(romfilename))
                {
                    return null;
                }

                string updateinfoFilename = GetUpdateInfo(romfilename);
                if (string.IsNullOrEmpty(updateinfoFilename) || !File.Exists(updateinfoFilename))
                {
                    return null;
                }

                Dictionary<string, string> updateinfo = LoadUpdateInfo(updateinfoFilename);

                var w = new WorkProject();
                w.RomFilename = romfilename;
                w.UpdateinfoLines = updateinfo;

                string name = U.at(updateinfo, "NAME");
                w.Name = string.IsNullOrEmpty(name) ? Path.GetFileNameWithoutExtension(romfilename) : name;

                string logo = U.at(updateinfo, "LOGO_FILENAME");
                if (!string.IsNullOrEmpty(logo))
                {
                    string romdir = Path.GetDirectoryName(romfilename) ?? "";
                    w.LogoFilename = Path.Combine(romdir, logo);
                }
                else
                {
                    w.LogoFilename = "";
                }

                w.IsUpdateMark = false;
                return w;
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>
        /// Resolve a ROM's <c>.updateinfo.txt</c> sidecar. Tries the exact
        /// extension-swap first, then progressively-trimmed name variants
        /// (<c>BSFE_1.0.ups -&gt; BSFE.updateinfo.txt</c>). Ported verbatim from
        /// WinForms <c>ToolWorkSupportForm.GetUpdateInfo</c>. Never throws.
        /// </summary>
        public static string GetUpdateInfo(string romfilename)
        {
            try
            {
                string filename = U.ChangeExtFilename(romfilename, ".updateinfo.txt");
                if (File.Exists(filename))
                {
                    return filename;
                }

                // Trim trailing version/variant tokens off the stem.
                // BSFE_1.0.ups -> BSFE.updateinfo.txt
                // fe8kaitou.en.ups -> fe8kaitou.updateinfo.txt
                filename = Path.GetFileNameWithoutExtension(romfilename);
                var ver = new List<string>();
                for (int i = 0; i < filename.Length; i++)
                {
                    if (filename[i] == '.' || filename[i] == '_' || filename[i] == '-' || filename[i] == ' ')
                    {
                        ver.Add(filename.Substring(0, i));
                    }
                }

                string basedir = Path.GetDirectoryName(romfilename) ?? "";
                for (int i = ver.Count - 1; i >= 0; i--)
                {
                    filename = Path.Combine(basedir, ver[i] + ".updateinfo.txt");
                    if (File.Exists(filename))
                    {
                        return filename;
                    }
                }

                // not found
                return U.ChangeExtFilename(romfilename, ".updateinfo.txt");
            }
            catch (Exception)
            {
                return "";
            }
        }

        /// <summary>
        /// Parse a <c>.updateinfo.txt</c> file into KEY=VALUE pairs, skipping
        /// comments and other-language lines. Ported verbatim from WinForms
        /// <c>ToolWorkSupportForm.LoadUpdateInfo</c>. Never throws.
        /// </summary>
        public static Dictionary<string, string> LoadUpdateInfo(string filename)
        {
            var ret = new Dictionary<string, string>();
            try
            {
                if (string.IsNullOrEmpty(filename) || !File.Exists(filename))
                {
                    return ret;
                }

                string[] lines = File.ReadAllLines(filename);
                for (int i = 0; i < lines.Length; i++)
                {
                    string line = lines[i];
                    if (U.IsComment(line) || U.OtherLangLine(line))
                    {
                        continue;
                    }
                    line = U.ClipComment(line);
                    line = line.Trim();

                    int sep = line.IndexOf('=');
                    if (sep < 0)
                    {
                        continue;
                    }
                    string key = line.Substring(0, sep);
                    string value = line.Substring(sep + 1);
                    if (key == "")
                    {
                        continue;
                    }
                    ret[key] = value;
                }
            }
            catch (Exception)
            {
                // malformed / unreadable -> return whatever parsed so far
            }
            return ret;
        }
    }
}
