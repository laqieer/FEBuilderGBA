using System;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using global::Avalonia.Threading;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;
using FEBuilderGBA.Core;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class ImageBattleAnimeView : TranslatedWindow, IEditorView, IDataVerifiableView
    {
        readonly ImageBattleAnimeViewModel _vm = new();
        readonly UndoService _undoService = new();
        bool _suppressFrameEvents;
        DispatcherTimer? _animTimer;
        bool _isPlaying;
        bool _listLoaded;

        public string ViewTitle => "Battle Animation Editor";
        public bool IsLoaded => _vm.IsLoaded;

        public ImageBattleAnimeView()
        {
            InitializeComponent();
            EntryList.SelectedAddressChanged += OnSelected;
            Opened += (_, _) => LoadList();
            Closed += (_, _) => StopAnimation();

            // Populate section combo with mode names
            for (int i = 0; i < BattleAnimeRendererCore.SectionNames.Length; i++)
                SectionCombo.Items.Add(BattleAnimeRendererCore.SectionNames[i]);
            SectionCombo.SelectedIndex = 0;
        }

        void LoadList()
        {
            _vm.IsLoading = true;
            try
            {
                var items = _vm.LoadList();
                EntryList.SetItemsWithIcons(items, i => ListIconLoaders.BattleAnimeLoader(items, i));
                _listLoaded = true;

                // Show total animation count in summary
                int count = _vm.CountAnimations();
                AnimeCountLabel.Text = $"Total animations in table: {count}";
            }
            catch (Exception ex)
            {
                Log.Error("ImageBattleAnimeView.LoadList failed: " + ex.ToString());
            }
            finally { _vm.IsLoading = false; _vm.MarkClean(); }
        }

        void OnSelected(uint addr) => LoadAndShowEntry(addr);

        // Load the SP-record at <paramref name="addr"/> into the VM and refresh
        // the whole editor UI. Shared by the EntryList selection path
        // (OnSelected) and the Class-Editor Jump direct-load fallback (#1377) so
        // a direct load goes through the SAME StopAnimation / IsLoading /
        // UpdateUI / MarkClean sequence as a normal list selection — otherwise
        // the VM could hold the correct AnimationNumber while the visible
        // detail panel still showed the previous row.
        void LoadAndShowEntry(uint addr)
        {
            StopAnimation();
            _vm.IsLoading = true;
            try
            {
                _vm.LoadEntry(addr);
                UpdateUI();
            }
            catch (Exception ex)
            {
                Log.Error("ImageBattleAnimeView.LoadAndShowEntry failed: " + ex.ToString());
            }
            finally { _vm.IsLoading = false; _vm.MarkClean(); }
        }

        void UpdateUI()
        {
            AddrLabel.Text = $"0x{_vm.CurrentAddr:X08}";
            WeaponTypeBox.Value = _vm.WeaponType;
            SpecialBox.Value = _vm.Special;
            AnimationNumberBox.Value = _vm.AnimationNumber;
            WeaponTypeNameLabel.Text = _vm.WeaponTypeName;

            // Update animation details panel
            if (_vm.HasAnimeDetails)
            {
                AnimeDetailsPanel.IsVisible = true;
                NoAnimeDetailsLabel.IsVisible = false;

                AnimeNameLabel.Text = _vm.AnimeName;
                AnimeDataAddrLabel.Text = $"0x{_vm.AnimeDataAddr:X08}";
                SectionPointerLabel.Text = _vm.SectionPointer;
                FramePointerLabel.Text = _vm.FramePointer;
                OamRtLPointerLabel.Text = _vm.OamRtLPointer;
                OamLtRPointerLabel.Text = _vm.OamLtRPointer;
                PalettePointerLabel.Text = _vm.PalettePointer;
                FrameLZ77Label.Text = _vm.FrameLZ77Info;
                OamLZ77Label.Text = _vm.OamLZ77Info;

                // Tile sheet image
                if (_vm.TileSheetImage != null)
                {
                    TileSheetPanel.IsVisible = true;
                    TileSheetInfoLabel.Text = _vm.TileSheetInfo;
                    TileSheetImage.SetImage(_vm.TileSheetImage);
                }
                else
                {
                    TileSheetPanel.IsVisible = false;
                    TileSheetImage.SetImage(null);
                }

                // Frame navigation
                _vm.InitFrameNavigation();
                UpdateFrameUI();
            }
            else
            {
                AnimeDetailsPanel.IsVisible = false;
                TileSheetPanel.IsVisible = false;
                FrameNavPanel.IsVisible = false;
                TileSheetImage.SetImage(null);
                FrameImageControl.SetImage(null);
                NoAnimeDetailsLabel.IsVisible = _vm.AnimationNumber == 0
                    ? false  // ID 0 means "none", no need to show error
                    : true;
                // For ID 0, show nothing; for invalid IDs, show the "not found" message
                if (_vm.AnimationNumber == 0)
                    NoAnimeDetailsLabel.IsVisible = false;
            }
        }

        void UpdateFrameUI()
        {
            FrameNavPanel.IsVisible = _vm.HasFrameData;
            if (!_vm.HasFrameData)
            {
                FrameImageControl.SetImage(null);
                return;
            }

            _suppressFrameEvents = true;
            try
            {
                FrameUpDown.Maximum = Math.Max(0, _vm.FrameCount - 1);
                FrameUpDown.Value = _vm.CurrentFrame;
            }
            finally { _suppressFrameEvents = false; }

            FrameInfoLabel.Text = _vm.FrameInfoText;
            FrameImageControl.SetImage(_vm.FrameImage);
        }

        void Write_Click(object? sender, RoutedEventArgs e)
        {
            _undoService.Begin("Edit Battle Animation");
            try
            {
                _vm.WeaponType = (uint)(WeaponTypeBox.Value ?? 0);
                _vm.Special = (uint)(SpecialBox.Value ?? 0);
                _vm.AnimationNumber = (uint)(AnimationNumberBox.Value ?? 0);
                _vm.Write();
                _undoService.Commit();
                _vm.MarkClean();

                // Refresh animation details after write
                _vm.LoadAnimationDetails(_vm.AnimationNumber);
                _vm.WeaponTypeName = ImageBattleAnimeViewModel.ResolveSPTypeName(_vm.WeaponType, _vm.Special);
                UpdateUI();
            }
            catch (Exception ex)
            {
                _undoService.Rollback();
                CoreState.Services.ShowError($"Write failed: {ex.Message}");
            }
        }

        async void ExportTileSheet_Click(object? sender, RoutedEventArgs e)
        {
            if (TileSheetImage.HasImage)
            {
                string name = _vm.AnimeName.Replace("\0", "").Trim();
                if (string.IsNullOrEmpty(name)) name = "tilesheet";
                await TileSheetImage.ExportPng(this, $"{name}_tilesheet");
            }
        }

        void SectionCombo_SelectionChanged(object? sender, SelectionChangedEventArgs e)
        {
            StopAnimation();
            if (_suppressFrameEvents || !_vm.HasFrameData) return;
            int idx = SectionCombo.SelectedIndex;
            if (idx < 0) return;

            try
            {
                _vm.LoadSectionFrames(idx);
                UpdateFrameUI();
            }
            catch (Exception ex)
            {
                Log.Error("SectionCombo_SelectionChanged: {0}", ex.Message);
            }
        }

        void FrameUpDown_ValueChanged(object? sender, NumericUpDownValueChangedEventArgs e)
        {
            if (_suppressFrameEvents || !_vm.HasFrameData) return;
            int frame = (int)(FrameUpDown.Value ?? 0);

            try
            {
                _vm.GoToFrame(frame);
                _suppressFrameEvents = true;
                FrameUpDown.Value = _vm.CurrentFrame;
                _suppressFrameEvents = false;
                FrameInfoLabel.Text = _vm.FrameInfoText;
                FrameImageControl.SetImage(_vm.FrameImage);
            }
            catch (Exception ex)
            {
                Log.Error("FrameUpDown_ValueChanged: {0}", ex.Message);
            }
        }

        void PrevFrame_Click(object? sender, RoutedEventArgs e)
        {
            if (!_vm.HasFrameData || _vm.FrameCount == 0) return;
            _vm.GoToFrame(_vm.CurrentFrame - 1);
            UpdateFrameUI();
        }

        void NextFrame_Click(object? sender, RoutedEventArgs e)
        {
            if (!_vm.HasFrameData || _vm.FrameCount == 0) return;
            _vm.GoToFrame(_vm.CurrentFrame + 1);
            UpdateFrameUI();
        }

        void PlayStop_Click(object? sender, RoutedEventArgs e)
        {
            if (_isPlaying)
                StopAnimation();
            else
                StartAnimation();
        }

        void StartAnimation()
        {
            if (!_vm.HasFrameData || _vm.FrameCount == 0) return;

            _isPlaying = true;
            PlayStopBtn.Content = "Stop";
            _animTimer = new DispatcherTimer { Interval = GetFrameInterval() };
            _animTimer.Tick += OnAnimTick;
            _animTimer.Start();
        }

        void StopAnimation()
        {
            _isPlaying = false;
            if (PlayStopBtn != null)
                PlayStopBtn.Content = "Play";
            _animTimer?.Stop();
            _animTimer = null;
        }

        void OnAnimTick(object? sender, EventArgs e)
        {
            if (!_vm.HasFrameData || _vm.FrameCount == 0)
            {
                StopAnimation();
                return;
            }

            int next = _vm.CurrentFrame + 1;
            if (next >= _vm.FrameCount) next = 0; // loop
            _vm.GoToFrame(next);
            UpdateFrameUI();

            // Update interval in case user changed speed slider
            if (_animTimer != null)
                _animTimer.Interval = GetFrameInterval();
        }

        TimeSpan GetFrameInterval()
        {
            double speed = SpeedSlider?.Value ?? 5;
            int ms = (int)(200 / speed); // 5 → 40ms (25fps), 1 → 200ms, 10 → 20ms
            return TimeSpan.FromMilliseconds(Math.Max(16, ms));
        }

        /// <summary>
        /// Navigate to <paramref name="address"/>. The left list is now
        /// CLASS-centric (#1377): every row's address is a class's battle-anime
        /// SETTING pointer (<c>p32(classAddr + 52)</c> FE7/8 / <c>+48</c> FE6),
        /// the exact offset <see cref="ImageBattleAnimeViewModel.LoadEntry"/>
        /// reads its 4-byte SP record from. So the Class-Editor Jump (which
        /// passes <c>U.toOffset(settingPtr)</c>) lands on a real row. Two cases,
        /// mirroring WF <c>ImageBattleAnimeForm.JumpToAnimeSettingPointer</c>:
        /// <list type="number">
        /// <item>The address matches a list row — SELECT it (keeps the list
        /// selection and the detail panel in sync; OnSelected loads the SP
        /// record). Covers both a same-editor round-trip and the class jump.</item>
        /// <item>The address matches no row but a class genuinely owns it
        /// (<see cref="ClassFormCore.GetIDWhereBattleAnimeAddr"/> resolves a
        /// class id) — e.g. a class whose unsafe/edge setting pointer was skipped
        /// from the list. Deselect and DIRECT-LOAD it as the safe fallback,
        /// mirroring WF re-initialising at <c>toOffset(ptr)</c>.</item>
        /// </list>
        /// An address that is neither a list row nor any class's setting pointer
        /// is left untouched (no spurious direct-load of arbitrary ROM bytes).
        /// </summary>
        public void NavigateTo(uint address)
        {
            // Defensive: the list normally loads on Opened before Navigate calls
            // NavigateTo, but if this runs first (or on a freshly cached window
            // whose list was cleared) an empty EntryList would make a real class
            // row look like a miss and get direct-loaded. Ensure the list is
            // populated before deciding.
            if (!_listLoaded)
                LoadList();

            // Case 1: the address IS one of the class rows — select it so the
            // list highlight and the detail panel stay in sync after the jump.
            // Row addresses are ROM OFFSETS; callers normally pass an offset
            // (ClassEditorView/ClassFE6View pass U.toOffset(rawPtr)), but also try
            // the normalized offset so a raw GBA pointer still selects its row.
            if (EntryList.SelectAddress(address))
                return;
            ROM rom = CoreState.ROM;
            if (rom == null) return;
            uint off = U.toOffset(address);
            if (off != address && EntryList.SelectAddress(off))
                return;

            // Case 2: not a row. Direct-load only when a class genuinely owns
            // this battle-anime setting pointer (the row was skipped, e.g. an
            // edge/unsafe pointer). Avoids loading an arbitrary unowned address.
            // Require a full 4-byte readable SP record (weapon/special/animeNo).
            if (!U.isSafetyOffset(off, rom)) return;
            if ((ulong)off + 4 > (ulong)rom.Data.Length) return;

            // GetIDWhereBattleAnimeAddr resolves the owning class for this setting
            // pointer; if no class owns it, leave the current selection as-is.
            if (ClassFormCore.GetIDWhereBattleAnimeAddr(rom, off) == U.NOT_FOUND)
                return;

            // A class owns this pointer but it isn't a list row — clear the
            // selection and load the setting pointer directly.
            EntryList.Deselect();
            LoadAndShowEntry(off);
        }

        /// <summary>
        /// Navigate by battle-anime ID (#1377). The editor's left list is now
        /// CLASS-centric (rows are per-class SP-record setting pointers, NOT the
        /// 32-byte ANIME-DATA-table slots), so a jump that only knows an anime id
        /// (the Mant Animation editor's "Jump to Battle Anime") must land on a
        /// CLASS that USES that anime — its SP-record row — rather than the
        /// obsolete <c>animelist base + id*4</c> slot address (which is no longer
        /// a row and would silently no-op).
        /// <list type="number">
        /// <item>Resolve the first class whose anime == <paramref name="animeId"/>
        /// (<see cref="ClassFormCore.GetFirstClassSettingPointerByAnimeId"/>) and
        /// select that class row.</item>
        /// <item>If no class uses the anime id, the SP-record list cannot show it,
        /// so directly load the 32-byte ANIME-DATA record for that id into the
        /// detail/preview panels (deselecting the list) so the user still sees the
        /// target animation.</item>
        /// </list>
        /// </summary>
        public void NavigateToAnimeId(uint animeId)
        {
            if (!_listLoaded)
                LoadList();

            ROM rom = CoreState.ROM;
            if (rom == null) return;

            // Case 1: a class uses this anime — select that class's row.
            uint settingOffset = ClassFormCore.GetFirstClassSettingPointerByAnimeId(rom, animeId);
            if (settingOffset != U.NOT_FOUND && EntryList.SelectAddress(settingOffset))
                return;

            // Case 2: no class uses it — show the anime DATA record directly so
            // the target animation is still visible (no SP-record row exists).
            ShowAnimeDetailsOnly(animeId);
        }

        // Render the detail/preview panels for a bare anime id when no class row
        // can host it (the #1377 Mant-Animation jump fallback). Mirrors the
        // detail half of UpdateUI but without an SP record: the SP fields read 0
        // (no class owns it) and the animation panel shows the resolved anime.
        void ShowAnimeDetailsOnly(uint animeId)
        {
            StopAnimation();
            EntryList.Deselect();
            _vm.IsLoading = true;
            try
            {
                _vm.CurrentAddr = 0;
                _vm.WeaponType = 0;
                _vm.Special = 0;
                _vm.AnimationNumber = animeId;
                _vm.WeaponTypeName = "";
                _vm.LoadAnimationDetails(animeId);
                UpdateUI();
            }
            catch (Exception ex)
            {
                Log.Error("ImageBattleAnimeView.ShowAnimeDetailsOnly failed: " + ex.ToString());
            }
            finally { _vm.IsLoading = false; _vm.MarkClean(); }
        }
        public void SelectFirstItem() => EntryList.SelectFirst();
        public ViewModelBase? DataViewModel => _vm;
    }
}
