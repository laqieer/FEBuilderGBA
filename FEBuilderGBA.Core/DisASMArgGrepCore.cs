using System;
using System.Collections.Generic;
using System.Text;

namespace FEBuilderGBA
{
    /// <summary>
    /// READ-ONLY, GUI-free port of the WinForms <c>DisASMDumpAllArgGrepForm</c>
    /// register-flow "Disassembly Argument Grep" algorithm (#1463).
    ///
    /// WinForms ground truth: <c>DisASMDumpAllArgGrepForm.Grep()</c> +
    /// <c>IsSearchRegister()</c>. The algorithm finds a line that "sets" the
    /// target register (a <c>mov</c>/<c>ldr</c> line that mentions the register
    /// token), then within <c>allowNumber</c> rows looks for a line that calls
    /// the target function; when found it emits the argument-setup block from
    /// the register-set line through the call (the call line optionally dropped).
    /// If a closer register-set is seen first it re-anchors; if the window is
    /// exceeded the candidate is discarded and the scan rewinds to just after
    /// the register-set line.
    ///
    /// All string matching reproduces the WinForms behavior VERBATIM, including
    /// its imperfections: matches are case-SENSITIVE plain <c>IndexOf</c>, the
    /// register check is a literal substring test (so searching <c>" r1"</c>
    /// will also match an operand such as <c>r10</c>/<c>r11</c>/<c>r12</c>), and
    /// the opcode gate only requires the line to contain <c>" mov"</c> or
    /// <c>" ldr"</c> regardless of which operand the register appears in.
    /// </summary>
    public static class DisASMArgGrepCore
    {
        /// <summary>
        /// Normalize the target-function search string exactly as the WinForms
        /// <c>GrepButton_Click</c> does: if the text parses as a hex address,
        /// convert it to the canonical <c>0xHHHHHHHH</c> pointer string;
        /// otherwise (a symbol name; <see cref="U.atoh"/> returns 0) pass it
        /// through unchanged.
        /// </summary>
        public static string NormalizeSearchFunction(string searchFunction)
        {
            if (searchFunction == null)
            {
                return string.Empty;
            }
            uint addr = U.atoh(searchFunction);
            if (addr != 0)
            {
                searchFunction = U.To0xHexString(U.toPointer(addr));
            }
            return searchFunction;
        }

        /// <summary>
        /// Build the leading-space-prefixed register search token exactly as the
        /// WinForms form does: <c>" " + SearhRegister.Text</c> (e.g. <c>" r0"</c>).
        /// </summary>
        public static string BuildSearchReg(string registerText)
        {
            return " " + (registerText ?? string.Empty);
        }

        /// <summary>
        /// WinForms <c>IsSearchRegister</c> port (verbatim). Returns true only
        /// when the line contains <c>" mov"</c> or <c>" ldr"</c> AND contains
        /// the (leading-space-prefixed) register token. Case-sensitive literal
        /// substring matching, matching WinForms.
        /// </summary>
        public static bool IsSearchRegister(string line, string searchReg)
        {
            if (line.IndexOf(" mov", StringComparison.Ordinal) < 0)
            {
                if (line.IndexOf(" ldr", StringComparison.Ordinal) < 0)
                {
                    return false;
                }
            }

            return line.IndexOf(searchReg, StringComparison.Ordinal) >= 0;
        }

        /// <summary>
        /// Register-flow argument grep over already-disassembled lines.
        /// Verbatim port of WinForms <c>DisASMDumpAllArgGrepForm.Grep()</c> minus
        /// the file read and the WinForms progress dialog. The caller passes the
        /// cached disassembly lines (e.g. from <c>DisassemblerCore.DisassembleToLines()</c>).
        /// </summary>
        /// <param name="lines">Disassembly lines to scan (one instruction per line).</param>
        /// <param name="searchFunction">
        /// Target function token to find (already normalized via
        /// <see cref="NormalizeSearchFunction"/>, or any raw substring).
        /// </param>
        /// <param name="searchReg">
        /// Leading-space register token (e.g. <c>" r0"</c>), built via
        /// <see cref="BuildSearchReg"/>.
        /// </param>
        /// <param name="allowNumber">Allowed-rows window (WinForms 1..20, default 5).</param>
        /// <param name="hideFunctionCall">Drop the call line from each emitted block.</param>
        /// <param name="hideUnknownArg">
        /// WinForms "show only function calls whose purpose is unknown" filter:
        /// skip register-set lines that contain a <c>'('</c>. A <c>'('</c> marks a
        /// call the disassembler already annotated (a known/resolved purpose), so
        /// dropping those anchors leaves only the calls whose purpose is unknown.
        /// </param>
        /// <returns>The concatenated argument-setup blocks, blank-line separated.</returns>
        public static string Grep(
            IReadOnlyList<string> lines,
            string searchFunction,
            string searchReg,
            int allowNumber,
            bool hideFunctionCall,
            bool hideUnknownArg)
        {
            if (lines == null)
            {
                return string.Empty;
            }

            StringBuilder ret = new StringBuilder();
            // regLine == 0 doubles as the "no active anchor" sentinel, exactly as
            // WinForms. Consequence (preserved verbatim): a register-set on line
            // index 0 can never become an anchor. In practice DisassembleToLines()
            // always emits header/comment lines first, so index 0 is never an
            // instruction — but the quirk is kept to match WinForms bit-for-bit.
            int regLine = 0;

            for (int i = 0; i < lines.Count; i++)
            {
                string line = lines[i];

                if (regLine <= 0)
                {
                    if (IsSearchRegister(line, searchReg))
                    {
                        if (hideUnknownArg)
                        {
                            if (line.IndexOf('(') > 0)
                            {
                                continue;
                            }
                        }
                        regLine = i;
                    }
                }
                else
                {
                    if (line.IndexOf(searchFunction, StringComparison.Ordinal) >= 0)
                    {
                        int limit = i;
                        if (hideFunctionCall)
                        {
                            limit--;
                        }
                        for (int n = regLine; n <= limit; n++)
                        {
                            ret.AppendLine(lines[n].Trim());
                        }
                        // separator blank line
                        ret.AppendLine();

                        regLine = 0;
                    }
                    else if (IsSearchRegister(line, searchReg))
                    {
                        // a nearer reference to the searched register was found
                        if (hideUnknownArg)
                        {
                            if (line.IndexOf('(') > 0)
                            {
                                regLine = 0;
                                continue;
                            }
                        }
                        regLine = i;
                    }
                    else if (i - regLine >= allowNumber)
                    {
                        // window exceeded; discard candidate
                        i = regLine; // rewind to just after the register-set line
                        regLine = 0;
                    }
                }
            }

            return ret.ToString();
        }
    }
}
