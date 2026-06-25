// SPDX-License-Identifier: GPL-3.0-or-later
// Cross-platform sox resample/normalize step for DirectSound wav import (#1448).
//
// Port of the WinForms MainFormUtil.ConvertWaveBySOX external-tool invocation.
// sox is an EXTERNAL tool on WinForms too: the path comes from config
// (OptionForm.GetSox() -> Program.Config.at("sox")), and FEBuilder never bundles
// it. This Core helper mirrors that model honestly: it reads the path from
// CoreState.Config.at("sox",""), and when the path is unset / the binary is
// missing it returns a CLEAR localized error instead of silently doing nothing.
// No bundled or auto-managed resampler is promised — ordinary 16-bit/44.1 kHz
// import still requires the user to configure sox, exactly as in WinForms.
//
// The argument string is built byte-for-byte like WF ConvertWaveBySOX:
//   [-v vol]  input.wav  [-r hz]  [-b 8 -c chan]  output.wav
//   [silence 1 0.2 {strip-1}% reverse silence 1 0.2 {strip-1}% reverse]  [gain -h]
// and the WF "all parameters at default => just copy the input" early-exit is
// preserved (returns the input bytes verbatim with no sox call).
using System;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace FEBuilderGBA.Core
{
    /// <summary>
    /// Cross-platform sox resample/normalize for the DirectSound wav-import
    /// pipeline (#1448). Port of WinForms <c>MainFormUtil.ConvertWaveBySOX</c>:
    /// an EXTERNAL configured tool (path from <c>CoreState.Config.at("sox")</c>),
    /// not a bundled codec. Never throws.
    /// </summary>
    public static class SongSoxConvertCore
    {
        /// <summary>The resolved sox executable path, or "" when unconfigured.</summary>
        public static string GetSoxPath()
        {
            return CoreState.Config?.at("sox", "") ?? "";
        }

        /// <summary>Is a usable sox binary configured? (path set AND the file exists).</summary>
        public static bool IsSoxAvailable()
        {
            string p = GetSoxPath();
            return !string.IsNullOrEmpty(p) && File.Exists(p);
        }

        /// <summary>
        /// <c>true</c> when every conversion parameter is at its no-op default
        /// (channel/hz/strip/volume all 0), in which case WF copies the input
        /// verbatim and never calls sox. Exposed so the UI can show "no sox needed".
        /// </summary>
        public static bool IsNoOp(uint channel, uint hz, uint strip, uint volume100)
        {
            return channel == 0 && hz == 0 && strip == 0 && volume100 == 0;
        }

        /// <summary>
        /// Convert <paramref name="wavBytes"/> (a RIFF/WAVE byte array) via sox using
        /// the WinForms parameter set and return the resampled/normalized WAV bytes.
        /// <list type="bullet">
        /// <item>All-default params (<see cref="IsNoOp"/>) => returns
        ///   <paramref name="wavBytes"/> verbatim, NO sox call (WF early-exit).</item>
        /// <item>sox unconfigured / missing => <c>null</c> with a clear localized
        ///   <paramref name="error"/> (the honest external-tool block).</item>
        /// <item>sox error / empty output => <c>null</c> with the captured detail.</item>
        /// </list>
        /// Mirrors WF <c>ConvertWaveBySOX</c> argument building exactly. Never throws.
        /// </summary>
        /// <param name="channel">Channel count (WF <c>-c</c>; 0 = leave unchanged).</param>
        /// <param name="hz">Sample rate (WF <c>-r</c>; 0 = leave unchanged).</param>
        /// <param name="strip">Silence-strip percent +1 (WF <c>silence … {strip-1}%</c>; 0 = none).</param>
        /// <param name="volume100">Volume as a percent (WF <c>-v {v/100}</c> + <c>gain -h</c>; 0 = none).</param>
        public static byte[] ConvertWaveBySox(byte[] wavBytes, uint channel, uint hz, uint strip, uint volume100, out string error)
        {
            error = null;
            if (wavBytes == null || wavBytes.Length == 0)
            {
                error = R._("Not a Wave file. The data is too small.");
                return null;
            }

            // WF early-exit: no transformation requested -> copy input unchanged.
            if (IsNoOp(channel, hz, strip, volume100))
            {
                return (byte[])wavBytes.Clone();
            }

            string soxExe = GetSoxPath();
            if (string.IsNullOrEmpty(soxExe) || !File.Exists(soxExe))
            {
                // Honest external-tool block — clear, actionable, localized.
                error = R._("The {0} tool is not configured. Set the {0} path in Settings -> Options to resample/normalize wave files.", "sox");
                return null;
            }

            string tempDir = null;
            string fromFile = null;
            string saveFile = null;
            try
            {
                tempDir = Path.Combine(Path.GetTempPath(), "feb_sox_" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(tempDir);
                fromFile = Path.Combine(tempDir, "in.wav");
                saveFile = Path.Combine(tempDir, "out.wav");
                File.WriteAllBytes(fromFile, wavBytes);

                // ---- VERBATIM WF ConvertWaveBySOX argument construction ----
                StringBuilder args = new StringBuilder();
                if (volume100 != 0)
                {
                    args.Append(string.Format(System.Globalization.CultureInfo.InvariantCulture, " -v {0} ", volume100 / 100.0f));
                }
                args.Append(" ").Append(U.escape_shell_args(fromFile));
                if (hz != 0)
                {
                    args.Append(string.Format(" -r {0}", hz));
                }
                if (channel != 0)
                {
                    args.Append(" -b 8 ");
                    args.Append(string.Format(" -c {0}", channel));
                }
                args.Append(" ").Append(U.escape_shell_args(saveFile));
                if (strip != 0)
                {
                    args.Append(string.Format(" silence 1 0.2 {0}% reverse silence 1 0.2 {0}% reverse", strip - 1));
                }
                if (volume100 != 0)
                {
                    args.Append(" gain -h ");
                }

                int exitCode = RunProcess(soxExe, args.ToString(), tempDir, out string output, out bool timedOut);

                // Treat a timeout, a non-zero exit code, OR an "ERROR..." banner as
                // failure (Copilot review #1537): a sox timeout or a non-zero exit
                // that left a partial out.wav (>0 bytes) must NOT be imported as a
                // corrupted sample. The output-existence check is the LAST gate.
                bool errBanner = output != null && output.IndexOf("ERROR", StringComparison.OrdinalIgnoreCase) == 0;
                if (timedOut || exitCode != 0 || errBanner)
                {
                    error = soxExe + " " + args + " \r\nexit=" + exitCode
                          + (timedOut ? " (timed out)" : "") + "\r\noutput:\r\n" + output;
                    return null;
                }

                if (!File.Exists(saveFile) || U.GetFileSize(saveFile) <= 0)
                {
                    error = soxExe + " " + args + " \r\noutput:\r\n" + output;
                    return null;
                }

                return File.ReadAllBytes(saveFile);
            }
            catch (Exception ex)
            {
                error = R._("Failed to run {0}: {1}", "sox", ex.Message);
                return null;
            }
            finally
            {
                try { if (tempDir != null && Directory.Exists(tempDir)) Directory.Delete(tempDir, true); }
                catch { /* best-effort cleanup */ }
            }
        }

        // Process runner — same shape as AsmCompileCore.RunProcess (120s timeout,
        // combined stdout+stderr). Never throws. Returns the process exit code and
        // sets <paramref name="timedOut"/> so the caller can reject a timeout or a
        // non-zero exit (Copilot review #1537). On a launch exception the exit code
        // is a synthetic -1 with the message in <paramref name="output"/>.
        static int RunProcess(string exePath, string args, string workDir, out string output, out bool timedOut)
        {
            output = "";
            timedOut = false;
            try
            {
                var psi = new ProcessStartInfo(exePath, args)
                {
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                    WorkingDirectory = workDir,
                };

                var sb = new StringBuilder();
                using (var proc = Process.Start(psi))
                {
                    proc.OutputDataReceived += (_, e) => { if (e.Data != null) sb.AppendLine(e.Data); };
                    proc.ErrorDataReceived += (_, e) => { if (e.Data != null) sb.AppendLine(e.Data); };
                    proc.BeginOutputReadLine();
                    proc.BeginErrorReadLine();

                    if (!proc.WaitForExit(120_000))
                    {
                        try { proc.Kill(); } catch { }
                        timedOut = true;
                        output = "Error: sox timed out after 120 seconds.\r\n" + sb.ToString();
                        return -1;
                    }
                    // Drain the async stdout/stderr handlers (a bounded WaitForExit
                    // can return before the last OutputDataReceived fires).
                    proc.WaitForExit();
                    output = sb.ToString();
                    return proc.ExitCode;
                }
            }
            catch (Exception ex)
            {
                output = "Error: " + ex.Message;
                return -1;
            }
        }
    }
}
