using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

using global::Avalonia.Media.Imaging;

using FEBuilderGBA.Avalonia.Controls;
using FEBuilderGBA.Avalonia.Services;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    /// <summary>
    /// Kind of card to render in the conversation viewer.
    /// </summary>
    public enum ConversationCardKind
    {
        /// <summary>Plain text (no speaker context).</summary>
        Text,
        /// <summary>Dialogue line from a known speaker slot.</summary>
        Serif,
        /// <summary>Display command — a character enters a slot.</summary>
        Display,
        /// <summary>Hide command — a character leaves a slot.</summary>
        Hide,
        /// <summary>Move command — a character changes slots.</summary>
        Move,
        /// <summary>Jump command — a character stays in the same slot.</summary>
        Jump,
    }

    /// <summary>
    /// A single displayable card in the conversation viewer.
    /// </summary>
    public class ConversationCardViewModel
    {
        /// <summary>Card kind (Text, Serif, Display, Hide, Move, Jump).</summary>
        public ConversationCardKind Kind { get; init; }

        /// <summary>Short label like "[Display]" / "[Talk]" / "[Move]".</summary>
        public string KindLabel { get; init; } = "";

        /// <summary>Display name for the speaker, or "(empty)" / "Visitor" / "Face 0xNN".</summary>
        public string SpeakerName { get; init; } = "";

        /// <summary>Slot label such as "left edge", "right mid", "off-screen left".</summary>
        public string SlotLabel { get; init; } = "";

        /// <summary>Optional speaker portrait bitmap (null when unavailable).</summary>
        public Bitmap? SpeakerBitmap { get; init; }

        /// <summary>Bubble text for the Serif / Text cards (empty for action cards).</summary>
        public string Bubble { get; init; } = "";

        /// <summary>True if the speaker slot is on the left half of the screen.</summary>
        public bool IsLeftSide { get; init; }
    }

    /// <summary>
    /// View-model that drives the "Conversation Viewer" tab inside
    /// <c>TextViewerView</c>. Pure projection: takes a decoded text string
    /// (or text id), parses it via
    /// <see cref="ConversationScriptParser.ParseScript"/>, and produces an
    /// <see cref="ObservableCollection{T}"/> of <see cref="ConversationCardViewModel"/>.
    /// </summary>
    public class ConversationViewerTabViewModel : ViewModelBase
    {
        ObservableCollection<ConversationCardViewModel> _cards = new();

        /// <summary>Cards rendered in the conversation tab.</summary>
        public ObservableCollection<ConversationCardViewModel> Cards
        {
            get => _cards;
            private set => SetField(ref _cards, value);
        }

        /// <summary>
        /// Load a text id from ROM, decode it, and project to cards. Used by
        /// <c>TextViewerView</c> when the user selects a text entry.
        /// </summary>
        public void LoadConversation(uint textId)
        {
            try
            {
                ROM rom = CoreState.ROM;
                if (rom?.RomInfo == null)
                {
                    Cards = new ObservableCollection<ConversationCardViewModel>();
                    return;
                }
                string decoded = FETextDecode.Direct(textId) ?? "";
                LoadFromDecodedText(decoded);
            }
            catch (Exception ex)
            {
                Log.Error("ConversationViewerTabViewModel.LoadConversation", ex.ToString());
                Cards = new ObservableCollection<ConversationCardViewModel>();
            }
        }

        /// <summary>
        /// Load directly from an already-decoded dialogue string. Exposed for
        /// pure projection tests that do not need a ROM.
        /// </summary>
        public void LoadFromDecodedText(string decoded)
        {
            bool enableRework = PatchDetection.SearchTextEngineReworkPatch()
                == PatchDetection.TextEngineRework_enum.TeqTextEngineRework;

            List<ConversationStep> steps =
                ConversationScriptParser.ParseScript(decoded ?? "", enableRework);

            var cards = new ObservableCollection<ConversationCardViewModel>();
            foreach (ConversationStep step in steps)
            {
                cards.Add(ProjectStep(step));
            }
            Cards = cards;
        }

        // ====================================================================
        // Projection helpers
        // ====================================================================

        static ConversationCardViewModel ProjectStep(ConversationStep step)
        {
            uint pos = step.Code1 >= 0x8 ? step.Code1 - 0x8 : 0;
            bool isLeftSide = step.Code1 >= 0x8 && (step.Code1 - 0x8) <= 2;
            string slotLabel = step.Code1 >= 0x8 ? GetSlotLabel(step.Code1) : "";

            // Decide kind
            ConversationCardKind kind;
            string kindLabel;
            string bubble = "";
            string speakerName = "";
            Bitmap? speakerBitmap = null;

            if (step.Code2 == 0x10)
            {
                kind = ConversationCardKind.Display;
                kindLabel = "[Display]";
                uint face100 = step.Code3;
                speakerName = ResolveFaceName(face100);
                speakerBitmap = ResolveFaceBitmap(face100);
            }
            else if (step.Code2 == 0x11)
            {
                kind = ConversationCardKind.Hide;
                kindLabel = "[Hide]";
                uint face100 = pos < step.Units.Length ? step.Units[pos] : 0;
                speakerName = ResolveFaceName(face100);
                // Hide cards never draw a portrait — the character is leaving.
            }
            else if (step.IsJump)
            {
                kind = ConversationCardKind.Jump;
                kindLabel = "[Jump]";
                uint face100 = pos < step.Units.Length ? step.Units[pos] : 0;
                speakerName = ResolveFaceName(face100);
            }
            else if (step.Code2 == 0x80 && step.Code3 >= 0xA && step.Code3 <= 0x11)
            {
                kind = ConversationCardKind.Move;
                kindLabel = "[Move]";
                uint face100 = pos < step.Units.Length ? step.Units[pos] : 0;
                speakerName = ResolveFaceName(face100);
            }
            else if (step.Code1 >= 0x8)
            {
                // Serif (dialogue from a known speaker slot)
                kind = ConversationCardKind.Serif;
                kindLabel = "[Talk]";
                uint face100 = pos < step.Units.Length ? step.Units[pos] : 0;
                speakerName = ResolveFaceName(face100);
                speakerBitmap = ResolveFaceBitmap(face100);
                bubble = HumaniseBubbleText(step.SrcText);
            }
            else
            {
                // Plain text with no position context
                kind = ConversationCardKind.Text;
                kindLabel = "[Text]";
                bubble = HumaniseBubbleText(step.SrcText);
            }

            return new ConversationCardViewModel
            {
                Kind = kind,
                KindLabel = kindLabel,
                SpeakerName = speakerName,
                SlotLabel = slotLabel,
                SpeakerBitmap = speakerBitmap,
                Bubble = bubble,
                IsLeftSide = isLeftSide,
            };
        }

        /// <summary>
        /// Resolve a face_id + 0x100 value to a display name, handling the
        /// 0xFFFF visitor sentinel, raw values below 0x100, the empty slot,
        /// and normal face ids.
        /// </summary>
        static string ResolveFaceName(uint face100)
        {
            if (face100 == 0xFFFF) return "Visitor";
            if (face100 == 0) return "(empty)";
            if (face100 < 0x100) return $"Face 0x{face100:X02}";
            uint faceId = face100 - 0x100;
            try
            {
                string name = NameResolver.GetPortraitName(faceId);
                if (!string.IsNullOrEmpty(name)) return name;
            }
            catch { /* fall through */ }
            return $"Portrait 0x{faceId:X02}";
        }

        /// <summary>
        /// Resolve a face_id + 0x100 value to a portrait <see cref="Bitmap"/>.
        /// Returns null for sentinels, malformed values, or missing portraits.
        /// </summary>
        static Bitmap? ResolveFaceBitmap(uint face100)
        {
            if (face100 == 0 || face100 == 0xFFFF) return null;
            if (face100 < 0x100) return null;
            try
            {
                uint faceId = face100 - 0x100;
                IImage img = PortraitRendererCore.DrawPortraitAutoById(faceId);
                return IconBitmapBuilder.FromImage(img);
            }
            catch
            {
                return null;
            }
        }

        static string GetSlotLabel(uint code1)
        {
            return code1 switch
            {
                0x8 => "left edge",
                0x9 => "left mid",
                0xA => "left side",
                0xB => "right side",
                0xC => "right mid",
                0xD => "right edge",
                0xE => "off-screen left",
                0xF => "off-screen right",
                _ => "",
            };
        }

        static string HumaniseBubbleText(string srcText)
        {
            if (string.IsNullOrEmpty(srcText)) return "";
            string s = srcText;
            // Strip leading position / display codes from a serif line so the
            // user just sees the spoken text (mirrors WinForms
            // TextForm.StripFirstCodeBySerifu — we strip leading @00XX codes
            // <= 0x11 since those are display/position commands).
            int i = 0;
            while (i + 5 <= s.Length && s[i] == '@')
            {
                uint code = ParseHex4(s, i + 1);
                if (code <= 0x11 || code >= 0x100)
                {
                    i += 5;
                    continue;
                }
                break;
            }
            if (i > 0) s = s.Substring(i);
            // Humanise remaining @XXXX codes.
            s = TextDisplayFormatter.EscapeRawControlChars(s);
            s = TextDisplayFormatter.ConvertEscapeToFEditor(s);
            // Trim leading newlines that creep in from the parser splitting on
            // mid-script linebreaks.
            return s.TrimStart('\r', '\n', ' ');
        }

        static uint ParseHex4(string s, int start)
        {
            if (start + 4 > s.Length) return 0;
            uint v = 0;
            for (int k = 0; k < 4; k++)
            {
                char c = s[start + k];
                v <<= 4;
                if (c >= '0' && c <= '9') v |= (uint)(c - '0');
                else if (c >= 'a' && c <= 'f') v |= (uint)(c - 'a' + 10);
                else if (c >= 'A' && c <= 'F') v |= (uint)(c - 'A' + 10);
                else return 0;
            }
            return v;
        }
    }
}
