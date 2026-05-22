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
    /// <see cref="ConversationScriptParser.ParseScript"/>, and mutates a stable
    /// <see cref="ObservableCollection{T}"/> of
    /// <see cref="ConversationCardViewModel"/> in place so callers can bind
    /// <see cref="Cards"/> once and never re-wire.
    /// </summary>
    public class ConversationViewerTabViewModel : ViewModelBase
    {
        readonly ObservableCollection<ConversationCardViewModel> _cards = new();
        // Per-load portrait cache: avoids re-decoding the same speaker's
        // portrait multiple times across cards in a single conversation.
        readonly Dictionary<uint, Bitmap?> _portraitCache = new();
        uint _currentTextId;
        bool _isCurrent;

        /// <summary>Cards rendered in the conversation tab.</summary>
        public ObservableCollection<ConversationCardViewModel> Cards => _cards;

        /// <summary>The text id this VM is currently presenting (0 when none).</summary>
        public uint CurrentTextId
        {
            get => _currentTextId;
            private set => SetField(ref _currentTextId, value);
        }

        /// <summary>
        /// Mark a new text id as pending WITHOUT decoding / projecting it.
        /// Used by <c>TextViewerView.OnTextSelected</c> to defer the heavy
        /// portrait + parse work until the user actually opens the
        /// Conversation Viewer tab.
        /// </summary>
        public void SetPendingTextId(uint textId)
        {
            if (CurrentTextId != textId)
            {
                CurrentTextId = textId;
                _isCurrent = false;
            }
        }

        /// <summary>
        /// Ensure the cards collection reflects <see cref="CurrentTextId"/>.
        /// Cheap when nothing has changed; otherwise reloads from ROM.
        /// </summary>
        public void EnsureCurrent()
        {
            if (_isCurrent) return;
            LoadConversation(CurrentTextId);
        }

        /// <summary>
        /// Load a text id from ROM, decode it, and project to cards. Used by
        /// <c>TextViewerView</c> when the Conversation Viewer tab is activated.
        /// </summary>
        public void LoadConversation(uint textId)
        {
            CurrentTextId = textId;
            try
            {
                ROM rom = CoreState.ROM;
                if (rom?.RomInfo == null)
                {
                    ResetCards();
                    _isCurrent = true;
                    return;
                }
                string decoded = FETextDecode.Direct(textId) ?? "";
                LoadFromDecodedText(decoded);
            }
            catch (Exception ex)
            {
                Log.Error("ConversationViewerTabViewModel.LoadConversation", ex.ToString());
                ResetCards();
                _isCurrent = true;
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

            ResetCards();
            foreach (ConversationStep step in steps)
            {
                _cards.Add(ProjectStep(step, _portraitCache));
            }
            _isCurrent = true;
        }

        void ResetCards()
        {
            _cards.Clear();
            // Per-load cache: clear so a different conversation does not
            // accidentally show a stale portrait if a face id slot is reused.
            _portraitCache.Clear();
        }

        // ====================================================================
        // Projection helpers
        // ====================================================================

        static ConversationCardViewModel ProjectStep(
            ConversationStep step, Dictionary<uint, Bitmap?> portraitCache)
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
                speakerBitmap = ResolveFaceBitmap(face100, portraitCache);
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
                speakerBitmap = ResolveFaceBitmap(face100, portraitCache);
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
        /// Resolve a face_id + 0x100 value to a portrait <see cref="Bitmap"/>,
        /// caching results in <paramref name="cache"/> so repeated speakers
        /// in the same conversation reuse the decoded bitmap.
        /// Returns null for sentinels, malformed values, or missing portraits.
        /// </summary>
        static Bitmap? ResolveFaceBitmap(uint face100, Dictionary<uint, Bitmap?> cache)
        {
            if (face100 == 0 || face100 == 0xFFFF) return null;
            if (face100 < 0x100) return null;
            if (cache.TryGetValue(face100, out Bitmap? cached)) return cached;

            Bitmap? built = null;
            try
            {
                uint faceId = face100 - 0x100;
                IImage img = PortraitRendererCore.DrawPortraitAutoById(faceId);
                built = IconBitmapBuilder.FromImage(img);
            }
            catch
            {
                built = null;
            }
            cache[face100] = built;
            return built;
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
