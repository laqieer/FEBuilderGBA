// SPDX-License-Identifier: GPL-3.0-or-later
//
// EventTemplateCore (#1434) — cross-platform (Core, no WinForms) event-template
// code generator. Ports the WinForms EventTemplate1-6Form / EventScriptTemplateForm
// generation engine:
//   - EventScriptInnerControl.LineToEventByte / ConverteventTextToBin
//   - EventTemplateImpl.LoadTemplate / GetCodes
//
// The 6 numbered templates are one-click event-byte generators. The "Templates"
// browser lists every template_list_event_ entry and previews its disassembled
// codes. Templates that contain XXXX / YYYY / XXXXXXXX placeholders require the
// parent event-editor's map/label context (WinForms Alloc-Event host) which the
// standalone Avalonia windows do not have — those are surfaced as
// "requires editor context" and NEVER emit partial/truncated bytes.

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace FEBuilderGBA
{
    public static class EventTemplateCore
    {
        // Mirror of EventScriptInnerControl.TermCode.
        public enum TermCode
        {
            NoTerm,
            DefaultTermCode,
            SimpleTermCode,
        }

        /// <summary>
        /// Verbatim port of EventScriptInnerControl.LineToEventByte: read the
        /// leading hex bytes (2 hex chars each) of a config line, stopping at the
        /// first non-hex char (the "\t//comment" tail) or odd boundary.
        /// </summary>
        public static byte[] LineToEventByte(string line)
        {
            var ret = new List<byte>();
            if (line == null)
            {
                return ret.ToArray();
            }
            line = line.Trim();
            int length = line.Length;
            for (int i = 0; i < length; i += 2)
            {
                if (!U.ishex(line[i]))
                {
                    break;
                }
                if (i + 1 >= length)
                {
                    break;
                }
                if (!U.ishex(line[i + 1]))
                {
                    break;
                }
                byte b = (byte)U.atoh(line.Substring(i, 2));
                ret.Add(b);
            }
            return ret.ToArray();
        }

        /// <summary>
        /// Verbatim port of EventScriptInnerControl.ConverteventTextToBin: read a
        /// config file, skip other-language lines, apply XXXX/YYYY substitutions,
        /// concatenate the per-line event bytes, and optionally append the ROM's
        /// default terminator.
        /// </summary>
        public static byte[] ConverteventTextToBin(ROM rom, string filename,
            TermCode addTerm = TermCode.DefaultTermCode,
            string XXXXXXXX = null, string YYYYYYYY = null)
        {
            var binarray = new List<byte>();
            if (!File.Exists(filename))
            {
                return binarray.ToArray();
            }

            string text = File.ReadAllText(filename);
            string[] lines = text.Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i];
                if (U.OtherLangLine(line, rom))
                {
                    continue;
                }
                if (XXXXXXXX != null)
                {
                    line = line.Replace("XXXXXXXX", XXXXXXXX);
                    line = line.Replace("XXXX", XXXXXXXX);
                }
                if (YYYYYYYY != null)
                {
                    line = line.Replace("YYYYYYYY", YYYYYYYY);
                    line = line.Replace("YYYY", YYYYYYYY);
                }
                byte[] bin = LineToEventByte(line);
                if (bin.Length < 4)
                {//broken or non-code line
                    continue;
                }
                binarray.AddRange(bin);
            }

            if (rom?.RomInfo != null &&
                (addTerm == TermCode.DefaultTermCode || addTerm == TermCode.SimpleTermCode))
            {//append terminator
                byte[] term = rom.RomInfo.Default_event_script_term_code;
                if (term != null)
                {
                    binarray.AddRange(term);
                }
            }
            return binarray.ToArray();
        }

        /// <summary>
        /// The BLANK button: the per-chapter toplevel event terminator code.
        /// </summary>
        public static byte[] GetToplevelBlank(ROM rom)
        {
            if (rom?.RomInfo == null)
            {
                return new byte[0];
            }
            return rom.RomInfo.Default_event_script_toplevel_code ?? new byte[0];
        }

        /// <summary>
        /// Resolve a config-type prefix (e.g. "template_event_VILLAGE_TALK_") to a
        /// real file via U.ConfigDataFilename and generate its event bytes. Mirrors
        /// the WinForms button bodies
        /// ConverteventTextToBin(U.ConfigDataFilename("template_event_*_")).
        /// Returns null (never partial/truncated bytes) when the resolved config
        /// still contains an unsubstituted XXXX/YYYY placeholder.
        /// </summary>
        public static byte[] GenerateFromConfigType(ROM rom, string configType,
            TermCode addTerm = TermCode.DefaultTermCode)
        {
            if (rom == null || string.IsNullOrEmpty(configType))
            {
                return null;
            }
            string fullfilename = U.ConfigDataFilename(configType, rom);
            if (!File.Exists(fullfilename))
            {
                return null;
            }
            if (RequiresEditorContext(rom, fullfilename))
            {
                return null;
            }
            return ConverteventTextToBin(rom, fullfilename, addTerm);
        }

        /// <summary>
        /// True when, after dropping other-language lines, an active line still
        /// contains an XXXX/YYYY/XXXXXXXX placeholder. Such templates need the
        /// parent event-editor's map-id / label-allocation context (deferred),
        /// so we must not run LineToEventByte over placeholder text (it would stop
        /// at the first non-hex 'X' and silently drop / truncate the command).
        /// </summary>
        public static bool RequiresEditorContext(ROM rom, string filename)
        {
            if (!File.Exists(filename))
            {
                return false;
            }
            string text = File.ReadAllText(filename);
            string[] lines = text.Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i];
                if (U.OtherLangLine(line, rom))
                {
                    continue;
                }
                if (line.IndexOf("XXXX", StringComparison.Ordinal) >= 0 ||
                    line.IndexOf("YYYY", StringComparison.Ordinal) >= 0)
                {
                    return true;
                }
            }
            return false;
        }

        // --------------------------------------------------------------------
        // Disassembly / preview
        // --------------------------------------------------------------------

        /// <summary>
        /// Lazily ensure CoreState.EventScript holds loaded Event script
        /// definitions (same pattern as the Avalonia EventScriptViewModel).
        /// </summary>
        public static EventScript EnsureEventScriptLoaded()
        {
            EventScript es = CoreState.EventScript;
            if (es == null || es.Scripts == null || es.Scripts.Length == 0)
            {
                es = new EventScript();
                es.Load(EventScript.EventScriptType.Event);
                CoreState.EventScript = es;
            }
            return es;
        }

        /// <summary>
        /// Format one disassembled OneCode as a config-style line:
        ///   "&lt;hex bytes&gt;\t//&lt;readable script text&gt;".
        /// The hex prefix is exactly what the event editor's clipboard/paste
        /// import consumes via LineToEventByte. GUI-free (uses the Core
        /// EventScript.makeCommandComboText formatter, no WinForms name lookups).
        /// </summary>
        public static string EventToConfigLine(EventScript.OneCode code)
        {
            var sb = new StringBuilder();
            if (code?.ByteData != null)
            {
                for (int n = 0; n < code.ByteData.Length; n++)
                {
                    sb.Append(U.ToHexString(code.ByteData[n]));
                }
            }
            sb.Append("\t//");
            if (code?.Script != null && code.Script.Info != null && code.Script.Info.Length >= 1)
            {
                sb.Append(EventScript.makeCommandComboText(code.Script, false));
            }
            return sb.ToString();
        }

        /// <summary>
        /// Disassemble a generated event-byte blob into config-style preview lines
        /// (one line per OneCode). Returns an empty list for null/empty input.
        /// </summary>
        public static List<string> DisassemblePreview(ROM rom, byte[] bin)
        {
            var lines = new List<string>();
            if (rom == null || bin == null || bin.Length == 0)
            {
                return lines;
            }
            EventScript es = EnsureEventScriptLoaded();
            if (es == null)
            {
                return lines;
            }
            uint addr = 0;
            uint limit = (uint)bin.Length;
            while (addr < limit)
            {
                EventScript.OneCode code = es.DisAseemble(bin, addr);
                if (code?.Script == null || code.Script.Size <= 0)
                {
                    break;
                }
                lines.Add(EventToConfigLine(code));
                addr += (uint)code.Script.Size;
            }
            return lines;
        }

        /// <summary>
        /// Disassemble a generated event-byte blob into a list of editable
        /// <see cref="EventScript.OneCode"/> commands (one per command), the form the
        /// Avalonia event editor's <c>InsertTemplate</c> consumes (#1585). GUI-free:
        /// drives the cross-platform <see cref="EnsureEventScriptLoaded"/> disassembler,
        /// no WinForms dependency. Returns an empty list for null/empty input.
        /// <para>Round-trip: for WELL-FORMED input (a whole number of complete commands)
        /// the concatenated <c>OneCode.ByteData</c> equals <paramref name="bin"/>. A
        /// trailing partial command (fewer bytes than the decoded command's size) is
        /// INTENTIONALLY DROPPED — the bounds guard refuses to emit a synthesized
        /// zero-filled UNKNOWN that would fabricate bytes — so a short tail is not
        /// round-tripped. Template generators (<see cref="TryGenerateButtonCodes"/> /
        /// <see cref="TryGenerateBrowserTemplateCodes"/>) always produce whole commands,
        /// so they round-trip exactly.</para>
        /// </summary>
        public static List<EventScript.OneCode> DisassembleToCodes(ROM rom, byte[] bin)
        {
            var codes = new List<EventScript.OneCode>();
            if (rom == null || bin == null || bin.Length == 0)
            {
                return codes;
            }
            EventScript es = EnsureEventScriptLoaded();
            if (es == null)
            {
                return codes;
            }
            uint addr = 0;
            uint limit = (uint)bin.Length;
            int guard = 0;
            while (addr < limit && guard++ < 100000)
            {
                EventScript.OneCode code = es.DisAseemble(bin, addr);
                if (code?.Script == null || code.Script.Size <= 0)
                {
                    break;
                }
                // BOUNDS GUARD (Copilot PR review): DisAseemble synthesizes a full 4-byte
                // UNKNOWN even when fewer than Script.Size bytes remain in `bin` (a short
                // tail), whose ByteData is zero-filled to 4 bytes — adding it would fabricate
                // trailing bytes and break the round-trip guarantee. Stop when the decoded
                // command would read past the end of the blob.
                uint size = (uint)code.Script.Size;
                if (addr + size > limit)
                {
                    break;
                }
                codes.Add(code);
                addr += size;
            }
            return codes;
        }

        /// <summary>
        /// Generate one numbered-template button and return it already disassembled
        /// into editable <see cref="EventScript.OneCode"/> commands, ready to hand to
        /// the Avalonia event editor's in-editor insert (#1585). Reuses
        /// <see cref="TryGenerateButton"/> for the bytes, so the placeholder/context
        /// gating is identical — context-required templates return
        /// <see cref="GenerateResult.RequiresEditorContext"/> with an EMPTY list and
        /// never partial bytes. <paramref name="codes"/> is non-empty only when result
        /// is <see cref="GenerateResult.Ok"/>.
        /// </summary>
        public static GenerateResult TryGenerateButtonCodes(ROM rom, TemplateButton btn, out List<EventScript.OneCode> codes)
        {
            codes = new List<EventScript.OneCode>();
            GenerateResult result = TryGenerateButton(rom, btn, out byte[] bin);
            if (result != GenerateResult.Ok)
            {
                return result;
            }
            codes = DisassembleToCodes(rom, bin);
            return result;
        }

        /// <summary>
        /// Generate a browser template and return it already disassembled into editable
        /// <see cref="EventScript.OneCode"/> commands for the Avalonia event editor's
        /// in-editor insert (#1585). Same placeholder/context gating as
        /// <see cref="TryGenerateBrowserTemplate"/>: context-required templates return
        /// <see cref="GenerateResult.RequiresEditorContext"/> with an EMPTY list.
        /// </summary>
        public static GenerateResult TryGenerateBrowserTemplateCodes(ROM rom, BrowserTemplate et, out List<EventScript.OneCode> codes)
        {
            codes = new List<EventScript.OneCode>();
            GenerateResult result = TryGenerateBrowserTemplate(rom, et, out byte[] bin);
            if (result != GenerateResult.Ok)
            {
                return result;
            }
            codes = DisassembleToCodes(rom, bin);
            return result;
        }

        // --------------------------------------------------------------------
        // Per-template button definitions (ports the WinForms button sets)
        // --------------------------------------------------------------------

        public sealed class TemplateButton
        {
            public string Key;          // stable id / display key
            public string ConfigType;   // template_event_*_ prefix, or null for BLANK
            public TermCode Term;
            public bool IsBlank;        // BLANK => GetToplevelBlank

            public TemplateButton(string key, string configType, TermCode term, bool isBlank)
            {
                Key = key;
                ConfigType = configType;
                Term = term;
                IsBlank = isBlank;
            }
        }

        /// <summary>
        /// Button table for the numbered templates 1..6, matching the WinForms
        /// EventTemplate{N}Form button click handlers exactly. CALL_EndEvent /
        /// CALL_1 buttons are intentionally omitted (require parent-editor map
        /// context — deferred, see the #1434 PR "Deferred" section).
        /// </summary>
        public static List<TemplateButton> GetTemplateButtons(int templateNumber)
        {
            var list = new List<TemplateButton>();
            switch (templateNumber)
            {
                case 1:
                    list.Add(Blank());
                    list.Add(new TemplateButton("VILLAGE_TALK", "template_event_VILLAGE_TALK_", TermCode.DefaultTermCode, false));
                    list.Add(new TemplateButton("VILLAGE_ITEM", "template_event_VILLAGE_ITEM_", TermCode.DefaultTermCode, false));
                    list.Add(new TemplateButton("VILLAGE_GOLD", "template_event_VILLAGE_GOLD_", TermCode.DefaultTermCode, false));
                    list.Add(new TemplateButton("VILLAGE_UNIT", "template_event_VILLAGE_UNIT_", TermCode.DefaultTermCode, false));
                    break;
                case 2:
                    list.Add(Blank());
                    list.Add(new TemplateButton("ENTER_BY_PLAYER", "template_event_ENTER_BY_PLAYER_", TermCode.SimpleTermCode, false));
                    list.Add(new TemplateButton("ENTER_BY_UNIT", "template_event_ENTER_BY_UNIT_", TermCode.SimpleTermCode, false));
                    list.Add(new TemplateButton("ENTER_BY_ENEMY", "template_event_ENTER_BY_ENEMY_", TermCode.SimpleTermCode, false));
                    list.Add(new TemplateButton("ENTER_BY_NPC", "template_event_ENTER_BY_NPC_", TermCode.SimpleTermCode, false));
                    list.Add(new TemplateButton("EnterByUnitToGameOver", "template_event_EnterByUnitToGameOver_", TermCode.SimpleTermCode, false));
                    list.Add(new TemplateButton("DESERTT_REASURE", "template_event_DESERTT_REASURE_", TermCode.SimpleTermCode, false));
                    break;
                case 3:
                    list.Add(Blank());
                    list.Add(new TemplateButton("TalkEvent", "template_event_TalkEvent_", TermCode.DefaultTermCode, false));
                    list.Add(new TemplateButton("EnemyReinforcement", "template_event_EnemyReinforcement_", TermCode.DefaultTermCode, false));
                    list.Add(new TemplateButton("EnemyReinforcementIfHard", "template_event_EnemyReinforcementIfHard_", TermCode.DefaultTermCode, false));
                    list.Add(new TemplateButton("PlayerReinforcement", "template_event_PlayerReinforcement_", TermCode.DefaultTermCode, false));
                    list.Add(new TemplateButton("GAMEOVER", "template_event_GAMEOVER_", TermCode.DefaultTermCode, false));
                    list.Add(new TemplateButton("EnemyReinforcementByCounter", "template_event_EnemyReinforcementByCounter_", TermCode.DefaultTermCode, false));
                    break;
                case 4:
                    list.Add(Blank());
                    list.Add(new TemplateButton("TalkEvent", "template_event_TalkEvent_", TermCode.DefaultTermCode, false));
                    list.Add(new TemplateButton("TalkEventItem", "template_event_TalkEventItem_", TermCode.DefaultTermCode, false));
                    list.Add(new TemplateButton("TalkEventJoin", "template_event_TalkEventJoin_", TermCode.DefaultTermCode, false));
                    break;
                case 5:
                    list.Add(Blank());
                    break;
                case 6:
                    list.Add(Blank());
                    list.Add(new TemplateButton("GAMEOVER", "template_event_GAMEOVER_", TermCode.DefaultTermCode, false));
                    break;
            }
            return list;
        }

        static TemplateButton Blank()
        {
            return new TemplateButton("BLANK", null, TermCode.NoTerm, true);
        }

        /// <summary>
        /// Generate the bytes for one numbered-template button. BLANK returns the
        /// toplevel code; otherwise resolves the config and generates (null when
        /// missing / placeholder-gated).
        /// </summary>
        public static byte[] GenerateButton(ROM rom, TemplateButton btn)
        {
            if (rom == null || btn == null)
            {
                return null;
            }
            if (btn.IsBlank)
            {
                return GetToplevelBlank(rom);
            }
            return GenerateFromConfigType(rom, btn.ConfigType, btn.Term);
        }

        /// <summary>
        /// Why a button/template could not be generated — lets callers show an
        /// accurate status (a missing config file is a different failure than a
        /// placeholder template that needs the event-editor context).
        /// </summary>
        public enum GenerateResult
        {
            Ok,
            NoRom,
            ConfigNotFound,
            RequiresEditorContext,
        }

        /// <summary>
        /// Generate one numbered-template button, returning a precise reason on
        /// failure. <paramref name="bytes"/> is non-null only when result is Ok.
        /// </summary>
        public static GenerateResult TryGenerateButton(ROM rom, TemplateButton btn, out byte[] bytes)
        {
            bytes = null;
            if (rom?.RomInfo == null)
            {
                return GenerateResult.NoRom;
            }
            if (btn == null)
            {
                return GenerateResult.ConfigNotFound;
            }
            if (btn.IsBlank)
            {
                bytes = GetToplevelBlank(rom);
                return GenerateResult.Ok;
            }
            string fullfilename = U.ConfigDataFilename(btn.ConfigType, rom);
            if (!File.Exists(fullfilename))
            {
                return GenerateResult.ConfigNotFound;
            }
            if (RequiresEditorContext(rom, fullfilename))
            {
                return GenerateResult.RequiresEditorContext;
            }
            bytes = ConverteventTextToBin(rom, fullfilename, btn.Term);
            return GenerateResult.Ok;
        }

        /// <summary>
        /// Generate a browser template, returning a precise reason on failure.
        /// </summary>
        public static GenerateResult TryGenerateBrowserTemplate(ROM rom, BrowserTemplate et, out byte[] bytes)
        {
            bytes = null;
            if (rom?.RomInfo == null)
            {
                return GenerateResult.NoRom;
            }
            if (et == null || string.IsNullOrEmpty(et.Filename))
            {
                return GenerateResult.ConfigNotFound;
            }
            string full = Path.Combine(CoreState.BaseDirectory, "config", "data", et.Filename);
            if (!File.Exists(full))
            {
                return GenerateResult.ConfigNotFound;
            }
            if (RequiresEditorContext(rom, full))
            {
                return GenerateResult.RequiresEditorContext;
            }
            bytes = ConverteventTextToBin(rom, full, TermCode.NoTerm);
            return GenerateResult.Ok;
        }

        // --------------------------------------------------------------------
        // "Templates" browser (ports EventTemplateImpl.LoadTemplate / GetCodes)
        // --------------------------------------------------------------------

        public sealed class BrowserTemplate
        {
            public string Filename;             // e.g. "template_event_*.txt"
            public string Info;                 // human-readable label
            public bool RequiresContext;        // placeholder-gated (deferred)
        }

        /// <summary>
        /// Port of EventTemplateImpl.LoadTemplate: read the template_list_event_
        /// config, drop other-language lines, split "filename\tinfo", and flag
        /// each entry as context-required if its config carries a placeholder.
        /// </summary>
        public static List<BrowserTemplate> LoadBrowserTemplates(ROM rom)
        {
            var result = new List<BrowserTemplate>();
            string configFilename = U.ConfigDataFilename("template_list_event_", rom);
            if (!File.Exists(configFilename))
            {
                return result;
            }
            string[] lines = File.ReadAllLines(configFilename);
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i];
                if (U.OtherLangLine(line, rom))
                {
                    continue;
                }
                string[] sp = line.Split('\t');
                if (sp.Length < 2)
                {
                    continue;
                }
                var et = new BrowserTemplate();
                et.Filename = sp[0];
                et.Info = sp[1];
                string full = Path.Combine(CoreState.BaseDirectory, "config", "data", et.Filename);
                et.RequiresContext = RequiresEditorContext(rom, full);
                result.Add(et);
            }
            return result;
        }

        /// <summary>
        /// Generate the raw event bytes for a browser template (no terminator,
        /// like EventTemplateImpl.GetCodes). Returns null for context-required
        /// templates — never partial bytes.
        /// </summary>
        public static byte[] GenerateBrowserTemplate(ROM rom, BrowserTemplate et)
        {
            if (rom == null || et == null || string.IsNullOrEmpty(et.Filename))
            {
                return null;
            }
            string full = Path.Combine(CoreState.BaseDirectory, "config", "data", et.Filename);
            if (!File.Exists(full))
            {
                return null;
            }
            if (RequiresEditorContext(rom, full))
            {
                return null;
            }
            return ConverteventTextToBin(rom, full, TermCode.NoTerm);
        }

        // --------------------------------------------------------------------
        // #1591 — context-required template substitution (the Alloc-Event host
        // path). Ports EventTemplateImpl.GetCodes's XXXX/YYYY/XXXXXXXX branches
        // using the open editor's IEventEditorHostContext (map-id + label
        // allocator). The placeholder-free path is unchanged; this adds the
        // substitution for templates that previously hit RequiresEditorContext.
        // --------------------------------------------------------------------

        /// <summary>
        /// Which host-context substitution a template needs (drives the gate and
        /// the substituted-string source). Classified from the template filename,
        /// exactly like WinForms <c>EventTemplateImpl.GetCodes</c>.
        /// </summary>
        public enum ContextKind
        {
            None,           // no placeholder / placeholder-free template
            Cond,           // _COND_  -> two unused label ids (needs label allocator only)
            Preparation,    // PREPARATION -> player + enemy unit pointers (needs map)
            CallEndEvent,   // CALL_END_EVENT -> end-event pointer (needs map)
            Unknown,        // file still has a placeholder but matches no known family
        }

        /// <summary>
        /// Classify a browser template's required context from its filename,
        /// mirroring <c>EventTemplateImpl.GetCodes</c>'s filename branches. A
        /// template with NO placeholder is <see cref="ContextKind.None"/>; one
        /// that carries a placeholder but matches no known family is
        /// <see cref="ContextKind.Unknown"/> (the gate refuses it — finding #2).
        /// </summary>
        public static ContextKind ClassifyContextKind(ROM rom, string filename)
        {
            if (string.IsNullOrEmpty(filename) || !File.Exists(filename))
            {
                return ContextKind.None;
            }
            if (filename.IndexOf("template_event_CALL_END_EVENT", StringComparison.Ordinal) >= 0)
            {
                return ContextKind.CallEndEvent;
            }
            if (filename.IndexOf("template_event_PREPARATION", StringComparison.Ordinal) >= 0)
            {
                return ContextKind.Preparation;
            }
            if (filename.IndexOf("_COND_", StringComparison.Ordinal) >= 0)
            {
                return ContextKind.Cond;
            }
            // No known family — but does it still carry a placeholder?
            if (RequiresEditorContext(rom, filename))
            {
                return ContextKind.Unknown;
            }
            return ContextKind.None;
        }

        /// <summary>
        /// True if any active (non-other-language) line of <paramref name="filename"/>
        /// STILL contains an XXXX/YYYY placeholder after the given substitutions
        /// were applied. Used as the post-substitution guard so a 'X' can never
        /// reach <see cref="LineToEventByte"/> (which would silently truncate the
        /// command) — preserves the #1589 no-partial-bytes invariant even as the
        /// template configs evolve (Copilot review finding #2).
        /// </summary>
        static bool HasResidualPlaceholder(ROM rom, string filename, string XXXX, string YYYY)
        {
            string text = File.ReadAllText(filename);
            string[] lines = text.Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i];
                if (U.OtherLangLine(line, rom)) continue;
                if (XXXX != null)
                {
                    line = line.Replace("XXXXXXXX", XXXX);
                    line = line.Replace("XXXX", XXXX);
                }
                if (YYYY != null)
                {
                    line = line.Replace("YYYYYYYY", YYYY);
                    line = line.Replace("YYYY", YYYY);
                }
                if (line.IndexOf("XXXX", StringComparison.Ordinal) >= 0 ||
                    line.IndexOf("YYYY", StringComparison.Ordinal) >= 0)
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Generate a browser template WITH an open-editor host context, porting
        /// <c>EventTemplateImpl.GetCodes</c>'s XXXX/YYYY/XXXXXXXX substitution.
        /// <para>SAFETY (never regress #1589):
        /// <list type="bullet">
        /// <item>host == null  -> <see cref="GenerateResult.RequiresEditorContext"/>, EMPTY.</item>
        /// <item>map-required (Preparation / CallEndEvent) but the host cannot
        /// resolve a map -> RequiresEditorContext, EMPTY (no map-0 substitution).</item>
        /// <item>Unknown placeholder family -> RequiresEditorContext, EMPTY.</item>
        /// <item>any placeholder survives substitution -> RequiresEditorContext,
        /// EMPTY (no 'X' ever reaches the hex parser).</item>
        /// </list>
        /// For a placeholder-FREE template the host is ignored and it generates
        /// exactly as <see cref="TryGenerateBrowserTemplate"/>.</para>
        /// </summary>
        public static GenerateResult TryGenerateBrowserTemplateWithContext(
            ROM rom, BrowserTemplate et, IEventEditorHostContext host, out byte[] bytes)
        {
            bytes = null;
            if (rom?.RomInfo == null)
            {
                return GenerateResult.NoRom;
            }
            if (et == null || string.IsNullOrEmpty(et.Filename))
            {
                return GenerateResult.ConfigNotFound;
            }
            string full = Path.Combine(CoreState.BaseDirectory, "config", "data", et.Filename);
            if (!File.Exists(full))
            {
                return GenerateResult.ConfigNotFound;
            }

            ContextKind kind = ClassifyContextKind(rom, full);

            // Placeholder-free template: host is irrelevant, generate as before.
            if (kind == ContextKind.None)
            {
                bytes = ConverteventTextToBin(rom, full, TermCode.NoTerm);
                return GenerateResult.Ok;
            }

            // From here the template needs context. No host => refuse (the gate).
            if (host == null)
            {
                return GenerateResult.RequiresEditorContext;
            }
            // Unknown placeholder family => refuse (finding #2): we don't know how
            // to substitute it, so emitting anything risks a truncated command.
            if (kind == ContextKind.Unknown)
            {
                return GenerateResult.RequiresEditorContext;
            }

            string XXXX = null;
            string YYYY = null;

            if (kind == ContextKind.CallEndEvent)
            {
                if (!host.TryGetMapID(out uint mapid))
                {
                    return GenerateResult.RequiresEditorContext; // no map => refuse
                }
                XXXX = EventEditorHostContext.ToPointerToString(
                    EventEditorHostContext.ResolveEndEvent(rom, mapid));
            }
            else if (kind == ContextKind.Preparation)
            {
                if (!host.TryGetMapID(out uint mapid))
                {
                    return GenerateResult.RequiresEditorContext; // no map => refuse
                }
                XXXX = EventEditorHostContext.ToPointerToString(
                    EventEditorHostContext.ResolvePlayerUnits(rom, mapid));
                YYYY = EventEditorHostContext.ToPointerToString(
                    EventEditorHostContext.ResolveEnemyUnits(rom, mapid));
            }
            else if (kind == ContextKind.Cond)
            {
                // Two distinct unused conditional-label ids from 0x9000, exactly
                // like EventTemplateImpl.GetCodes (needs only the label allocator,
                // no map).
                uint labelX = EventEditorHostContext.GetUnuseLabelID(host, 0x9000);
                XXXX = EventEditorHostContext.ToUShortToString(labelX);
                uint labelY = EventEditorHostContext.GetUnuseLabelID(host, labelX + 1);
                YYYY = EventEditorHostContext.ToUShortToString(labelY);
            }

            // Post-substitution guard: if ANY placeholder survives, refuse rather
            // than let a 'X' reach LineToEventByte (#1589 no-partial-bytes).
            if (HasResidualPlaceholder(rom, full, XXXX, YYYY))
            {
                return GenerateResult.RequiresEditorContext;
            }

            bytes = ConverteventTextToBin(rom, full, TermCode.NoTerm, XXXX, YYYY);
            return GenerateResult.Ok;
        }

        /// <summary>
        /// Context-aware sibling of <see cref="TryGenerateBrowserTemplateCodes"/>:
        /// generate a (possibly context-substituted) browser template and return
        /// it disassembled into editable <see cref="EventScript.OneCode"/> commands
        /// for the Avalonia event editor's in-editor insert (#1591). Same gating as
        /// <see cref="TryGenerateBrowserTemplateWithContext"/>; an EMPTY list is
        /// returned for every refusal (never partial bytes).
        /// </summary>
        public static GenerateResult TryGenerateBrowserTemplateCodesWithContext(
            ROM rom, BrowserTemplate et, IEventEditorHostContext host, out List<EventScript.OneCode> codes)
        {
            codes = new List<EventScript.OneCode>();
            GenerateResult result = TryGenerateBrowserTemplateWithContext(rom, et, host, out byte[] bin);
            if (result != GenerateResult.Ok)
            {
                return result;
            }
            codes = DisassembleToCodes(rom, bin);
            return result;
        }
    }
}
