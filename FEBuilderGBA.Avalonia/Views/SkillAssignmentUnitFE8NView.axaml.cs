using System;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    /// <summary>
    /// Avalonia counterpart of WinForms <c>SkillAssignmentUnitFE8NForm</c>.
    ///
    /// #1452: the view used to be inert — <c>Initialize()</c> only set
    /// <c>IsLoaded</c>, the editing panel + Write button were hardcoded hidden,
    /// and <c>Write()</c> no-opped on <c>CurrentAddr==0</c>. A user WITH an FE8N
    /// skill patch was wrongly told "no patch installed".
    ///
    /// The view now consults <see cref="PatchDetectionService"/>: when an
    /// FE8N-family skill patch (FE8N / FE8N_Ver2 / FE8N_Ver3 / Yugudora / Midori)
    /// is detected AND a valid unit address has been navigated to, it hides the
    /// warning, reveals the field grid + Write button, and edits the open unit's
    /// Personal Skill / Skill Set 1 / Skill Set 2 bytes (struct offsets
    /// 0x27/0x28/0x29 = B39/B40/B41) — exactly the three bytes the WinForms form
    /// writes. When no FE8N patch is present, the warning stays and the panel
    /// stays hidden (correct behavior).
    ///
    /// The reveal/load logic lives in <see cref="RefreshUiForCurrentAddress"/>
    /// and is called from BOTH <c>Opened</c> and <see cref="NavigateTo"/> so a
    /// REUSED singleton window (WindowManager.Navigate reuses an already-open
    /// window and only fires NavigateTo, not Opened) refreshes for the new unit
    /// instead of showing stale/hidden state.
    /// </summary>
    public partial class SkillAssignmentUnitFE8NView : TranslatedWindow, IEditorView, IDataVerifiableView
    {
        readonly SkillAssignmentUnitFE8NViewViewModel _vm = new();
        readonly UndoService _undoService = new();
        public string ViewTitle => "Skill Assignment - Unit (FE8N)";
        public bool IsLoaded => _vm.IsLoaded;

        public SkillAssignmentUnitFE8NView()
        {
            InitializeComponent();
            WriteButton.Click += OnWrite;
            Opened += (_, _) =>
            {
                _vm.Initialize();
                // When opened WITHOUT a navigated address (the main-menu
                // OpenSkillAssignmentUnitFE8N entry and the --screenshot-all
                // runner both use Open<T>()), seed the first unit so the editor
                // is usable / renders populated instead of an empty window.
                // EditSkills from the Unit Editor always passes an address via
                // Navigate, so this never overrides a real selection.
                if (_vm.CurrentAddr == 0
                    && IsFE8NFamily(PatchDetectionService.Instance.SkillSystem))
                {
                    uint firstUnit = TryGetFirstUnitAddress();
                    if (firstUnit != 0) _vm.CurrentAddr = firstUnit;
                }
                RefreshUiForCurrentAddress();
            };
        }

        /// <summary>
        /// True iff the detected skill system is an FE8N-family variant that this
        /// editor handles. Mirrors the routing in
        /// <c>UnitEditorView.EditSkills_Click</c>.
        /// </summary>
        static bool IsFE8NFamily(PatchDetectionService.SkillSystemType t) =>
            t == PatchDetectionService.SkillSystemType.FE8N ||
            t == PatchDetectionService.SkillSystemType.FE8N_Ver2 ||
            t == PatchDetectionService.SkillSystemType.FE8N_Ver3 ||
            t == PatchDetectionService.SkillSystemType.Yugudora ||
            t == PatchDetectionService.SkillSystemType.Midori;

        /// <summary>
        /// Detect the FE8N patch and, when present + a valid unit address is set,
        /// reveal the editing panel and load the unit's skill bytes into the UI.
        /// Otherwise keep the "no patch" warning and the hidden panel.
        /// Idempotent — safe to call from both Opened and NavigateTo.
        /// </summary>
        void RefreshUiForCurrentAddress()
        {
            bool hasPatch = IsFE8NFamily(PatchDetectionService.Instance.SkillSystem);
            bool hasAddress = _vm.CurrentAddr != 0;
            bool functional = hasPatch && hasAddress;

            // Reveal/hide the editor surface.
            if (FieldsPanel != null) FieldsPanel.IsVisible = functional;
            if (WriteButton != null) WriteButton.IsVisible = functional;
            // The "no patch" warning is only honest when no FE8N patch is present.
            if (WarningBorder != null) WarningBorder.IsVisible = !hasPatch;

            if (!functional) return;

            // Load the unit's skill bytes and populate the controls.
            _vm.LoadEntry(_vm.CurrentAddr);
            if (AddrLabel != null) AddrLabel.Text = $"0x{_vm.CurrentAddr:X08}";
            if (PersonalSkillBox != null) PersonalSkillBox.Value = _vm.PersonalSkill;
            if (SkillSet1Box != null) SkillSet1Box.Value = _vm.SkillSet1;
            if (SkillSet2Box != null) SkillSet2Box.Value = _vm.SkillSet2;
            _vm.MarkClean();
        }

        /// <summary>
        /// Resolve the first real unit row address (index 1; index 0 is the
        /// empty sentinel) for the screenshot-only seed. Returns 0 on any fault.
        /// </summary>
        static uint TryGetFirstUnitAddress()
        {
            try
            {
                ROM rom = CoreState.ROM;
                if (rom?.RomInfo == null) return 0;
                uint baseAddr = rom.p32(rom.RomInfo.unit_pointer);
                if (!U.isSafetyOffset(baseAddr)) return 0;
                uint dataSize = rom.RomInfo.unit_datasize;
                uint addr = baseAddr + dataSize; // unit #1
                return addr + 42 <= (uint)rom.Data.Length ? addr : 0;
            }
            catch { return 0; }
        }

        void OnWrite(object? sender, RoutedEventArgs e)
        {
            if (_vm.CurrentAddr == 0) return;

            _vm.PersonalSkill = (uint)(PersonalSkillBox.Value ?? 0);
            _vm.SkillSet1 = (uint)(SkillSet1Box.Value ?? 0);
            _vm.SkillSet2 = (uint)(SkillSet2Box.Value ?? 0);

            _undoService.Begin("Edit Skill Assignment Unit FE8N");
            try
            {
                _vm.Write();
                _undoService.Commit();
                _vm.MarkClean();
            }
            catch (Exception ex)
            {
                _undoService.Rollback();
                Log.Error("SkillAssignmentUnitFE8NView.Write failed: {0}", ex.Message);
            }
        }

        public void NavigateTo(uint address)
        {
            _vm.CurrentAddr = address;
            RefreshUiForCurrentAddress();
        }

        public void SelectFirstItem() { }
        public ViewModelBase? DataViewModel => _vm;
    }
}
