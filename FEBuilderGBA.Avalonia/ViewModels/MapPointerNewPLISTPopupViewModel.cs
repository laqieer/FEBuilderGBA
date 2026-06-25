using System;
using FEBuilderGBA;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    public class MapPointerNewPLISTPopupViewModel : ViewModelBase
    {
        bool _isLoaded;
        uint _plistId;
        string _plistExplanation = "PLIST (Pointer List) assigns a numeric ID to each map.\nChoose an unused PLIST number to add a new map pointer entry.";
        string _alreadyExtendsText = string.Empty;
        bool _alreadyExtendsVisible;
        bool _explanationVisible = true;
        bool _extendVisible;
        string _linkPlistInfo = string.Empty;
        bool _isAlreadyUse;
        uint _plistMaximum = 65535;
        string _dialogResult = "";

        /// <summary>
        /// PLIST search type for the popup. The EventCond Precise-allocate
        /// caller uses <see cref="MapChangeCore.PlistType.EVENT"/> (WF
        /// <c>MapPointerNewPLISTPopupForm.Init(PLIST_TYPE.EVENT)</c>).
        /// </summary>
        public MapChangeCore.PlistType SearchType { get; set; } = MapChangeCore.PlistType.EVENT;

        public bool IsLoaded { get => _isLoaded; set => SetField(ref _isLoaded, value); }
        /// <summary>The PLIST ID number for the new map pointer entry.</summary>
        public uint PlistId { get => _plistId; set => SetField(ref _plistId, value); }
        /// <summary>Explanation text about the PLIST system.</summary>
        public string PlistExplanation { get => _plistExplanation; set => SetField(ref _plistExplanation, value); }
        /// <summary>Text indicating if the PLIST range is already extended.</summary>
        public string AlreadyExtendsText { get => _alreadyExtendsText; set => SetField(ref _alreadyExtendsText, value); }
        /// <summary>Whether the "already extended" note is shown (WF AlreadyExtendsLabel).</summary>
        public bool AlreadyExtendsVisible { get => _alreadyExtendsVisible; set => SetField(ref _alreadyExtendsVisible, value); }
        /// <summary>Whether the plain explanation text is shown (WF PLIST_EXPLAIN).</summary>
        public bool ExplanationVisible { get => _explanationVisible; set => SetField(ref _explanationVisible, value); }
        /// <summary>
        /// Whether the Extend button is shown. The Avalonia split editor flow
        /// is not wired, so the button stays hidden (honestly unavailable
        /// rather than a dead enabled control, #1433).
        /// </summary>
        public bool ExtendVisible { get => _extendVisible; set => SetField(ref _extendVisible, value); }
        /// <summary>Information about what this PLIST ID links to (read-only box).</summary>
        public string LinkPlistInfo { get => _linkPlistInfo; set => SetField(ref _linkPlistInfo, value); }
        /// <summary>True when the selected PLIST is already in use / reserved /
        /// out of range — OK must confirm before overwriting.</summary>
        public bool IsAlreadyUse { get => _isAlreadyUse; set => SetField(ref _isAlreadyUse, value); }
        /// <summary>Maximum selectable PLIST id (WF GetDataCount(EVENT) - 1).</summary>
        public uint PlistMaximum { get => _plistMaximum; set => SetField(ref _plistMaximum, value); }
        public string DialogResult { get => _dialogResult; set => SetField(ref _dialogResult, value); }

        public void Initialize()
        {
            IsLoaded = true;
        }

        /// <summary>
        /// Mirror of WF <c>MapPointerNewPLISTPopupForm.InitUI</c>: set the Extend
        /// note/visibility and the PLIST selector maximum. Then refresh the info
        /// display for the current PLIST value.
        /// </summary>
        public void InitUI(ROM rom)
        {
            // Maximum = event-plist count - 1 (WF GetDataCount(SearchType) - 1).
            uint count = MapPointerPlistUsageCore.GetEventPlistCount(rom);
            PlistMaximum = count > 0 ? count - 1 : 65535;

            MapPointerPlistUsageCore.ExtendState state = MapPointerPlistUsageCore.GetExtendState(rom);
            switch (state)
            {
                case MapPointerPlistUsageCore.ExtendState.AlreadySplit:
                    // WF: button disabled, note shown, plain explanation hidden.
                    AlreadyExtendsVisible = true;
                    ExplanationVisible = false;
                    break;
                case MapPointerPlistUsageCore.ExtendState.AlreadyExtended:
                    // WF: button disabled, note shown, explanation shown.
                    AlreadyExtendsVisible = true;
                    ExplanationVisible = true;
                    break;
                default:
                    AlreadyExtendsVisible = false;
                    ExplanationVisible = true;
                    break;
            }
            // The split/extend editor flow is not wired in Avalonia, so the
            // Extend button is always hidden (honestly unavailable, #1433).
            ExtendVisible = false;
            AlreadyExtendsText = R._("既に拡張済みです");

            UpdatePlistInfo(rom, PlistId);
        }

        /// <summary>
        /// Mirror of WF <c>PLISTnumericUpDown_ValueChanged</c> →
        /// <c>PlistToName</c>: refresh the read-only info box and the
        /// <see cref="IsAlreadyUse"/> flag for the given PLIST value.
        /// </summary>
        public void UpdatePlistInfo(ROM rom, uint plist)
        {
            PlistId = plist;
            MapPointerPlistUsageCore.UsageInfo info =
                MapPointerPlistUsageCore.BuildPlistUsageInfo(rom, SearchType, plist);
            LinkPlistInfo = info.Message;
            IsAlreadyUse = info.IsAlreadyUse;
        }
    }
}
