// SPDX-License-Identifier: GPL-3.0-or-later
// Decomp music: export a FEBuilder voicegroup (M4A / MusicPlayer2000 instrument
// set) as reviewable decomp SOURCE macro assembly for `sound/voicegroups/voicegroupNNN.s`
// using the macros from fireemblem8u's `asm/macros/music_voice.inc` (#1362).
//
// READ-ONLY: reads the preview ROM to produce a source artifact; NEVER mutates
// the ROM (the source-of-truth), never allocates ROM free space, never throws.
// This is an EXPORT / migration aid (a reviewable `.s`), NOT a byte-pinned ROM
// round-trip and NOT a full M4A re-assembler.
//
// CONSERVATIVE SCOPE (honest — the maintainer reopens over-claims):
//   * FULLY SUPPORTED (a real music_voice.inc macro):
//       0x00 voice_directsound,  0x08 voice_directsound_no_resample,
//       0x10 voice_directsound_alt,
//       0x01 voice_square_1,     0x09 voice_square_1 (alt flag),
//       0x02 voice_square_2,     0x0A voice_square_2 (alt flag),
//       0x03 voice_programmable_wave, 0x0B voice_programmable_wave (alt flag),
//       0x04 voice_noise,        0x0C voice_noise (alt flag),
//       0x40 voice_keysplit,     0x80 voice_keysplit_all.
//   * KEYSPLIT (0x40) / KEYSPLIT_ALL / DRUM (0x80): the correct macro IS emitted,
//     but its sub-voicegroup / keysplit-table POINTER cannot be safely resolved
//     to a decomp symbol from ROM bytes alone. We emit the raw `0x08XXXXXX`
//     pointer as a VALID macro argument and record an "unresolved pointer"
//     diagnostic (provenance) — we do NOT recurse / inline the sub-table (that
//     is a documented manual step). No guessed decomp symbols.
//   * DIRECTSOUND sample pointer: the WaveData sample pointer is emitted as a
//     valid raw `0x08XXXXXX` macro argument + an unresolved-pointer diagnostic.
//   * 0x18 is NOT a music_voice.inc macro (Copilot plan review pt 1) and any
//     UNKNOWN type byte is emitted as a COMMENTED placeholder + an actionable
//     diagnostic — NEVER a guessed / wrong macro byte.
//
// Macro byte<->arg mapping verified verbatim against
// FireEmblemUniverse/fireemblem8u:asm/macros/music_voice.inc and cross-checked
// against the existing 12-byte voice read-loop in SongInstrumentSetCore.cs.
using System;
using System.Collections.Generic;
using System.Text;

namespace FEBuilderGBA
{
    /// <summary>
    /// READ-ONLY decomp-source exporter (#1362): reads a voicegroup from the
    /// preview ROM and emits a reviewable <c>sound/voicegroups/voicegroupNNN.s</c>
    /// using the <c>asm/macros/music_voice.inc</c> macros. Never mutates the ROM,
    /// never throws. The pure <see cref="FormatVoicegroup"/> formatter is split
    /// out so it can be unit-tested ROM-free (mirrors <c>FormatShops</c>/<c>FormatTexts</c>).
    /// </summary>
    public static class VoicegroupAsmExportCore
    {
        const int BlockSize = 12;   // 12-byte voice entry (SongInstrumentSetCore.BlockSize)
        const int MaxVoices = 128;  // WF read-loop cap (SongInstrumentForm.cs L65)

        /// <summary>The decoded kind of a single voice entry.</summary>
        public enum VoiceKind
        {
            DirectSound,        // 0x00 / 0x08 / 0x10
            Square1,            // 0x01 / 0x09
            Square2,            // 0x02 / 0x0A
            ProgrammableWave,   // 0x03 / 0x0B
            Noise,              // 0x04 / 0x0C
            KeySplit,           // 0x40
            KeySplitAll,        // 0x80
            Unsupported,        // 0x18 + any unknown type byte
        }

        /// <summary>
        /// A decoded voice record — the per-type fields plus the raw 12 bytes so a
        /// formatter never has to re-read the ROM. PURE data (no ROM reference) so
        /// the formatter is ROM-free testable.
        /// </summary>
        public sealed class VoiceRecord
        {
            public int Index;
            public byte Type;           // the raw type byte
            public VoiceKind Kind;
            public byte[] Raw;          // the 12 raw bytes (always length 12)

            // DirectSound / Noise shared:
            public byte BaseMidiKey;    // +1 (DirectSound), +1 (Noise)
            public byte Pan;            // decoded pan arg (raw==0 ? 0 : raw & 0x7F)
            public bool PanNonCanonical;// raw pan nonzero but bit7 clear

            // DirectSound / ProgrammableWave / KeySplit*: a pointer at +4 (and +8 for keysplit).
            public uint Pointer4;       // GBA pointer at +4 (0 if none / unsafe)
            public bool Pointer4Valid;  // +4 holds a plausible GBA pointer
            public uint Pointer8;       // GBA pointer at +8 (KeySplit only)
            public bool Pointer8Valid;

            // Square1 / Square2 / Noise:
            public byte Sweep;          // Square1 +3
            public byte Duty;           // Square1 +4 / Square2 +4 (& 0x3)
            public byte NoiseUnk;       // Noise +3
            public byte NoisePeriod;    // Noise +4 (& 0x1)

            // ADSR (tonal types) at +8..+11:
            public byte Attack, Decay, Sustain, Release;
        }

        /// <summary>The result of an export: the emitted <c>.s</c> text + diagnostics.</summary>
        public sealed class ExportResult
        {
            public bool Ok;
            public string Text;                                 // the voicegroupNNN.s source
            public List<string> Diagnostics = new List<string>();
            public int VoiceCount;
        }

        /// <summary>
        /// Read + format the voicegroup at <paramref name="voicegroupAddr"/> into a
        /// reviewable <c>voicegroupNNN.s</c>. READ-ONLY; never throws; on a guarded /
        /// out-of-range address returns <c>Ok=false</c> with a diagnostic and empty text.
        /// </summary>
        /// <param name="rom">The preview ROM (read-only).</param>
        /// <param name="voicegroupAddr">The voicegroup base (a GBA pointer or an offset).</param>
        /// <param name="voicegroupNumber">The NNN used in the label / .global / comment.</param>
        public static ExportResult Export(ROM rom, uint voicegroupAddr, int voicegroupNumber)
        {
            var result = new ExportResult { Ok = false, Text = "" };
            try
            {
                if (rom == null)
                {
                    result.Diagnostics.Add(R._("ROM is not loaded."));
                    return result;
                }
                if (voicegroupNumber < 0) voicegroupNumber = 0;

                uint baseOffset = U.toOffset(voicegroupAddr);
                if (!U.isSafetyOffset(baseOffset, rom))
                {
                    result.Diagnostics.Add(R._("The voicegroup address 0x{0:X} is out of range.", baseOffset));
                    return result;
                }

                List<VoiceRecord> voices = ReadVoicegroup(rom, baseOffset);
                if (voices.Count == 0)
                {
                    result.Diagnostics.Add(R._("No valid voices were found at 0x{0:X}.", baseOffset));
                    return result;
                }

                result.VoiceCount = voices.Count;
                List<string> diags;
                result.Text = FormatVoicegroup(voices, voicegroupNumber, baseOffset, out diags);
                result.Diagnostics.AddRange(diags);
                result.Ok = true;
                return result;
            }
            catch (Exception ex)
            {
                // never-throws contract: surface as a diagnostic, no text.
                result.Ok = false;
                result.Text = "";
                result.Diagnostics.Add(R._("Voicegroup export failed: {0}", ex.Message));
                return result;
            }
        }

        /// <summary>
        /// Read the voicegroup (12-byte voice entries, max 128, terminator-bounded)
        /// into decoded <see cref="VoiceRecord"/>s. Mirrors the WF / Core read-loop
        /// validity predicate (<see cref="SongInstrumentSetCore"/>). READ-ONLY;
        /// every address is bounds-guarded; never throws.
        /// </summary>
        public static List<VoiceRecord> ReadVoicegroup(ROM rom, uint addr)
        {
            var list = new List<VoiceRecord>();
            if (rom == null) return list;

            uint baseOffset = U.toOffset(addr);
            for (int i = 0; i < MaxVoices; i++)
            {
                uint entry = baseOffset + (uint)(i * BlockSize);
                if (!SafeLen(rom, entry, BlockSize)) break;
                if (!IsValidVoice(rom, entry)) break;
                list.Add(DecodeVoice(rom, entry, i));
            }
            return list;
        }

        /// <summary>
        /// PURE formatter: turn decoded voice records into <c>voicegroupNNN.s</c>
        /// macro-assembly source. ROM-FREE (no ROM reference) so it is unit-tested
        /// with hand-built records. Collects unresolved-pointer + unsupported-type
        /// diagnostics into <paramref name="diagnostics"/>; emits a trailing comment
        /// block that lists them (never an inline comment between macro args —
        /// Copilot plan review pt 3).
        /// </summary>
        public static string FormatVoicegroup(
            IReadOnlyList<VoiceRecord> voices,
            int voicegroupNumber,
            uint provenanceBaseOffset,
            out List<string> diagnostics)
        {
            diagnostics = new List<string>();
            var sb = new StringBuilder();
            if (voicegroupNumber < 0) voicegroupNumber = 0;
            string label = "voicegroup" + voicegroupNumber.ToString("D3");

            sb.Append("@ Exported by FEBuilderGBA (#1362) — review before building.\n");
            sb.Append("@ Source voicegroup ROM offset: 0x" + provenanceBaseOffset.ToString("X") + "\n");
            sb.Append(".include \"asm/macros/music_voice.inc\"\n\n");
            sb.Append("\t.section .rodata\n");
            sb.Append("\t.align 2\n");
            sb.Append("\t.global " + label + "\n");
            sb.Append(label + ":\n");

            if (voices == null)
            {
                sb.Append("@ (no voices)\n");
                return sb.ToString();
            }

            for (int i = 0; i < voices.Count; i++)
            {
                VoiceRecord v = voices[i];
                if (v == null) continue;
                FormatVoiceRow(sb, v, diagnostics);
            }

            // Trailing diagnostics comment block (provenance) — NEVER inline in args.
            if (diagnostics.Count > 0)
            {
                sb.Append("\n@ ===== Export diagnostics (manual review required) =====\n");
                for (int i = 0; i < diagnostics.Count; i++)
                    sb.Append("@   " + diagnostics[i] + "\n");
            }

            return sb.ToString();
        }

        // --------------------------------------------------------------------
        // Per-row formatting.
        // --------------------------------------------------------------------
        static void FormatVoiceRow(StringBuilder sb, VoiceRecord v, List<string> diagnostics)
        {
            switch (v.Kind)
            {
                case VoiceKind.DirectSound:
                {
                    string macro =
                        v.Type == 0x08 ? "voice_directsound_no_resample" :
                        v.Type == 0x10 ? "voice_directsound_alt" :
                                         "voice_directsound";
                    string ptr = FormatPointerArg(v, diagnostics, v.Pointer4, v.Pointer4Valid, "sample");
                    sb.Append("\t" + macro + " " + v.BaseMidiKey + ", " + v.Pan + ", " + ptr
                        + ", " + v.Attack + ", " + v.Decay + ", " + v.Sustain + ", " + v.Release + "\n");
                    NotePanIfNonCanonical(v, diagnostics);
                    break;
                }
                case VoiceKind.Square1:
                {
                    // Both 0x01 and 0x09 use the voice_square_1 macro (the alt flag is
                    // a runtime distinction the macro itself does not vary on its args).
                    sb.Append("\tvoice_square_1 " + v.Sweep + ", " + v.Duty
                        + ", " + v.Attack + ", " + v.Decay + ", " + v.Sustain + ", " + v.Release + "\n");
                    NoteAltFlag(v, diagnostics, 0x09, "voice_square_1");
                    break;
                }
                case VoiceKind.Square2:
                {
                    sb.Append("\tvoice_square_2 " + v.Duty
                        + ", " + v.Attack + ", " + v.Decay + ", " + v.Sustain + ", " + v.Release + "\n");
                    NoteAltFlag(v, diagnostics, 0x0A, "voice_square_2");
                    break;
                }
                case VoiceKind.ProgrammableWave:
                {
                    string ptr = FormatPointerArg(v, diagnostics, v.Pointer4, v.Pointer4Valid, "wave samples");
                    sb.Append("\tvoice_programmable_wave " + ptr
                        + ", " + v.Attack + ", " + v.Decay + ", " + v.Sustain + ", " + v.Release + "\n");
                    NoteAltFlag(v, diagnostics, 0x0B, "voice_programmable_wave");
                    break;
                }
                case VoiceKind.Noise:
                {
                    sb.Append("\tvoice_noise " + v.BaseMidiKey + ", " + v.Pan + ", " + v.NoiseUnk
                        + ", " + v.NoisePeriod
                        + ", " + v.Attack + ", " + v.Decay + ", " + v.Sustain + ", " + v.Release + "\n");
                    NotePanIfNonCanonical(v, diagnostics);
                    NoteAltFlag(v, diagnostics, 0x0C, "voice_noise");
                    break;
                }
                case VoiceKind.KeySplit:
                {
                    string p4 = FormatPointerArg(v, diagnostics, v.Pointer4, v.Pointer4Valid, "sub-voicegroup");
                    string p8 = FormatPointerArg(v, diagnostics, v.Pointer8, v.Pointer8Valid, "keysplit table");
                    sb.Append("\tvoice_keysplit " + p4 + ", " + p8 + "\n");
                    diagnostics.Add(R._("Voice {0}: voice_keysplit points at a sub-voicegroup + keysplit table that were NOT inlined; resolve/import them manually.", v.Index));
                    break;
                }
                case VoiceKind.KeySplitAll:
                {
                    string p4 = FormatPointerArg(v, diagnostics, v.Pointer4, v.Pointer4Valid, "sub-voicegroup");
                    sb.Append("\tvoice_keysplit_all " + p4 + "\n");
                    diagnostics.Add(R._("Voice {0}: voice_keysplit_all (drum) points at a sub-voicegroup that was NOT inlined; resolve/import it manually.", v.Index));
                    break;
                }
                default:
                {
                    // UNSUPPORTED (0x18 + unknown): commented placeholder + the raw 12
                    // bytes for provenance — NEVER a guessed macro byte.
                    sb.Append("\t@ UNSUPPORTED voice type 0x" + v.Type.ToString("X2")
                        + " (raw: " + RawHex(v.Raw) + ") — emit manually\n");
                    diagnostics.Add(R._("Voice {0}: unsupported voice type 0x{1:X2}; emitted as a commented placeholder (manual conversion required).", v.Index, v.Type));
                    break;
                }
            }
        }

        // A pointer becomes a valid raw macro arg (0x08XXXXXX). Provenance/unresolved
        // notes go in the diagnostics block, never inline between macro args.
        static string FormatPointerArg(VoiceRecord v, List<string> diagnostics, uint ptr, bool valid, string what)
        {
            if (!valid || ptr == 0)
            {
                diagnostics.Add(R._("Voice {0}: the {1} pointer is missing or out of range; emitted as 0.", v.Index, what));
                return "0";
            }
            diagnostics.Add(R._("Voice {0}: the {1} pointer 0x{2:X8} is an unresolved raw ROM address (no decomp symbol inferred).", v.Index, what, ptr));
            return "0x" + ptr.ToString("X8");
        }

        static void NotePanIfNonCanonical(VoiceRecord v, List<string> diagnostics)
        {
            if (v.PanNonCanonical)
                diagnostics.Add(R._("Voice {0}: a non-canonical pan byte was read (high bit clear); decoded pan emitted, verify manually.", v.Index));
        }

        static void NoteAltFlag(VoiceRecord v, List<string> diagnostics, byte altType, string macro)
        {
            if (v.Type == altType)
                diagnostics.Add(R._("Voice {0}: ROM type 0x{1:X2} (alt) emitted as {2}; the alt flag is not separately expressed by the macro.", v.Index, v.Type, macro));
        }

        static string RawHex(byte[] raw)
        {
            if (raw == null) return "";
            var sb = new StringBuilder();
            for (int i = 0; i < raw.Length; i++)
            {
                if (i > 0) sb.Append(' ');
                sb.Append(raw[i].ToString("X2"));
            }
            return sb.ToString();
        }

        // --------------------------------------------------------------------
        // Decoding (ROM-reading). Every read is SafeLen-guarded by the caller.
        // --------------------------------------------------------------------
        static VoiceRecord DecodeVoice(ROM rom, uint addr, int index)
        {
            byte[] raw = rom.getBinaryData(addr, BlockSize);
            byte type = (byte)rom.u8(addr);
            var v = new VoiceRecord { Index = index, Type = type, Raw = raw };

            if (type == 0x00 || type == 0x08 || type == 0x10)
            {//DirectSound (0x18 is intentionally NOT a macro — Copilot plan review pt 1)
                v.Kind = VoiceKind.DirectSound;
                v.BaseMidiKey = (byte)rom.u8(addr + 1);
                DecodePan(v, (byte)rom.u8(addr + 3));
                ReadPointer4(rom, addr, v);
                ReadAdsr(rom, addr, v);
            }
            else if (type == 0x01 || type == 0x09)
            {//Square1
                v.Kind = VoiceKind.Square1;
                v.Sweep = (byte)rom.u8(addr + 3);
                v.Duty = (byte)(rom.u8(addr + 4) & 0x3);
                ReadAdsr(rom, addr, v);
            }
            else if (type == 0x02 || type == 0x0A)
            {//Square2
                v.Kind = VoiceKind.Square2;
                v.Duty = (byte)(rom.u8(addr + 4) & 0x3);
                ReadAdsr(rom, addr, v);
            }
            else if (type == 0x03 || type == 0x0B)
            {//ProgrammableWave
                v.Kind = VoiceKind.ProgrammableWave;
                ReadPointer4(rom, addr, v);
                ReadAdsr(rom, addr, v);
            }
            else if (type == 0x04 || type == 0x0C)
            {//Noise
                v.Kind = VoiceKind.Noise;
                v.BaseMidiKey = (byte)rom.u8(addr + 1);
                DecodePan(v, (byte)rom.u8(addr + 2));
                v.NoiseUnk = (byte)rom.u8(addr + 3);
                v.NoisePeriod = (byte)(rom.u8(addr + 4) & 0x1);
                ReadAdsr(rom, addr, v);
            }
            else if (type == 0x40)
            {//KeySplit
                v.Kind = VoiceKind.KeySplit;
                ReadPointer4(rom, addr, v);
                uint p8 = rom.u32(addr + 8);
                v.Pointer8 = p8;
                v.Pointer8Valid = U.isSafetyPointer(p8, rom);
            }
            else if (type == 0x80)
            {//KeySplitAll / Drum
                v.Kind = VoiceKind.KeySplitAll;
                ReadPointer4(rom, addr, v);
            }
            else
            {//0x18 + unknown
                v.Kind = VoiceKind.Unsupported;
            }
            return v;
        }

        static void DecodePan(VoiceRecord v, byte rawPan)
        {
            // Macro encodes pan as (0x80 | pan) when nonzero, else 0 (Copilot plan
            // review pt 2). Decode back to the macro arg; flag a non-canonical raw
            // value (nonzero with bit7 clear) for manual review.
            if (rawPan == 0)
            {
                v.Pan = 0;
                v.PanNonCanonical = false;
            }
            else
            {
                v.Pan = (byte)(rawPan & 0x7F);
                v.PanNonCanonical = (rawPan & 0x80) == 0;
            }
        }

        static void ReadPointer4(ROM rom, uint addr, VoiceRecord v)
        {
            uint p = rom.u32(addr + 4);
            v.Pointer4 = p;
            v.Pointer4Valid = U.isSafetyPointer(p, rom);
        }

        static void ReadAdsr(ROM rom, uint addr, VoiceRecord v)
        {
            v.Attack = (byte)rom.u8(addr + 8);
            v.Decay = (byte)rom.u8(addr + 9);
            v.Sustain = (byte)rom.u8(addr + 10);
            v.Release = (byte)rom.u8(addr + 11);
        }

        // --------------------------------------------------------------------
        // Guards (mirror SongInstrumentSetCore — explicit-ROM, overflow-safe).
        // --------------------------------------------------------------------
        static bool SafeLen(ROM rom, uint addr, uint length)
        {
            if (rom == null) return false;
            long end = (long)addr + length;
            return addr >= 0x00000100 && addr < 0x02000000
                && end <= rom.Data.Length && length < 0x00200000;
        }

        // WF read-loop validity predicate (SongInstrumentSetCore.IsValidVoice).
        static bool IsValidVoice(ROM rom, uint addr)
        {
            uint type = rom.u8(addr);
            if (type == 0x00 || type == 0x08 || type == 0x10 || type == 0x18
                || type == 0x03 || type == 0x0B || type == 0x80)
            {
                uint p = rom.u32(addr + 4);
                return U.isSafetyPointer(p, rom);
            }
            if (type == 0x40)
            {
                uint p1 = rom.u32(addr + 4);
                if (!U.isSafetyPointer(p1, rom)) return false;
                uint p2 = rom.u32(addr + 8);
                return U.isSafetyPointer(p2, rom);
            }
            if (type == 0x01 || type == 0x02 || type == 0x04
                || type == 0x09 || type == 0x0A || type == 0x0C)
            {
                return true;
            }
            return false;
        }
    }
}
