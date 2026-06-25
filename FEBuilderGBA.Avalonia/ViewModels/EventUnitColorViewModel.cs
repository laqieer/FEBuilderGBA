using System.Collections.ObjectModel;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    /// <summary>
    /// 4-slot colour-override picker for the event-script <c>UNIT_COLOR</c>
    /// argument. Ports WinForms <c>EventUnitColorForm</c>: four combos
    /// (Player / Enemy / NPC / Fourth), each <c>0=no change, 1=blue, 2=red,
    /// 3=green, 4=sepia</c>. <see cref="Seed(uint)"/> unpacks a packed value into
    /// the four combos; <see cref="Pack"/> / <see cref="Result"/> repacks the
    /// current selection (<c>a | (b&lt;&lt;4) | (c&lt;&lt;8) | (d&lt;&lt;12)</c>).
    /// All pack/unpack logic delegates to <see cref="EventUnitColorCore"/>.
    /// </summary>
    public class EventUnitColorViewModel : ViewModelBase
    {
        bool _isLoaded;
        int _playerIndex;
        int _enemyIndex;
        int _npcIndex;
        int _fourthIndex;
        string _friendlyText = "";

        public bool IsLoaded { get => _isLoaded; set => SetField(ref _isLoaded, value); }

        /// <summary>Items for every slot combo (identical lists, the five colour options).</summary>
        public ObservableCollection<string> SlotItems { get; } = new();

        public int PlayerIndex
        {
            get => _playerIndex;
            set { if (SetField(ref _playerIndex, value)) UpdateFriendlyText(); }
        }
        public int EnemyIndex
        {
            get => _enemyIndex;
            set { if (SetField(ref _enemyIndex, value)) UpdateFriendlyText(); }
        }
        public int NpcIndex
        {
            get => _npcIndex;
            set { if (SetField(ref _npcIndex, value)) UpdateFriendlyText(); }
        }
        public int FourthIndex
        {
            get => _fourthIndex;
            set { if (SetField(ref _fourthIndex, value)) UpdateFriendlyText(); }
        }

        /// <summary>Live human-readable summary of the current selection (#1444).</summary>
        public string FriendlyText { get => _friendlyText; set => SetField(ref _friendlyText, value); }

        public EventUnitColorViewModel()
        {
            // The five slot options (0..4). Reuse the existing translate entries
            // shipped for the WinForms combo (e.g. "0=No change.", "1=Blue", …).
            SlotItems.Add(R._("0=変更せず"));
            SlotItems.Add(R._("1=青"));
            SlotItems.Add(R._("2=赤"));
            SlotItems.Add(R._("3=緑"));
            SlotItems.Add(R._("4=セピア"));
        }

        /// <summary>Initialize the picker (seed all combos to value 0 = no change).</summary>
        public void Initialize()
        {
            Seed(0);
            IsLoaded = true;
        }

        /// <summary>
        /// Seed the four combos from a packed UNIT_COLOR value
        /// (mirrors WinForms <c>JumpTo</c>; uses the corrected 4th-slot nibble).
        /// </summary>
        public void Seed(uint value)
        {
            var (a, b, c, d) = EventUnitColorCore.Unpack(value);
            PlayerIndex = ClampIndex(a);
            EnemyIndex = ClampIndex(b);
            NpcIndex = ClampIndex(c);
            FourthIndex = ClampIndex(d);
            UpdateFriendlyText();
        }

        /// <summary>Pack the current selection back into a UNIT_COLOR value.</summary>
        public uint Pack()
        {
            return EventUnitColorCore.Pack(
                (uint)_playerIndex, (uint)_enemyIndex, (uint)_npcIndex, (uint)_fourthIndex);
        }

        /// <summary>The packed result (alias of <see cref="Pack"/>) for the dialog caller.</summary>
        public uint Result => Pack();

        void UpdateFriendlyText() => FriendlyText = EventUnitColorCore.GetUNIT_COLOR(Pack());

        static int ClampIndex(uint nibble)
        {
            if (nibble >= EventUnitColorCore.ColorOptionCount) return 0;
            return (int)nibble;
        }
    }
}
