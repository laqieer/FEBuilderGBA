using System.Collections.Generic;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    /// <summary>
    /// World Map Road (Path) editor (#1185). Drives the FE8-only interactive
    /// road painter over <see cref="WorldMapPathCore"/>. The live edit buffer
    /// (<see cref="Chips"/>) is painted by the View and packed/written by Core.
    /// </summary>
    public class WorldMapPathEditorViewModel : ViewModelBase
    {
        int _currentPathId = -1;
        uint _currentAddr;
        bool _isLoaded;
        int _selectedChipCol;
        int _selectedChipRow;

        /// <summary>The live road-chip edit buffer for the selected path.</summary>
        public List<PathChip> Chips { get; private set; } = new();

        /// <summary>The selected path id (the table row index, stable under filtering).</summary>
        public int CurrentPathId { get => _currentPathId; set => SetField(ref _currentPathId, value); }

        /// <summary>The resolved path-data ROM offset (for the address label).</summary>
        public uint CurrentAddr { get => _currentAddr; set => SetField(ref _currentAddr, value); }

        public bool IsLoaded { get => _isLoaded; set => SetField(ref _isLoaded, value); }

        /// <summary>Selected chip-palette column (0..3 = flip variants, 4 = erase).</summary>
        public int SelectedChipCol { get => _selectedChipCol; set => SetField(ref _selectedChipCol, value); }

        /// <summary>Selected chip-palette row (the road strip tile row).</summary>
        public int SelectedChipRow { get => _selectedChipRow; set => SetField(ref _selectedChipRow, value); }

        /// <summary>True only on an FE8 ROM (roads are FE8-only).</summary>
        public bool CanEdit
        {
            get
            {
                ROM rom = CoreState.ROM;
                return rom?.RomInfo != null && rom.RomInfo.version == 8;
            }
        }

        /// <summary>True when the erase column (col 4) is selected.</summary>
        public bool IsEraseSelected => SelectedChipCol == 4;

        public List<AddrResult> LoadList()
        {
            ROM rom = CoreState.ROM;
            return WorldMapPathCore.MakePathList(rom);
        }

        /// <summary>
        /// Load the path identified by <paramref name="pathId"/> (carried in the
        /// AddrResult tag — stable under filtering). Populates the chip buffer
        /// and resolves the data offset. Runs under IsLoading so it does not
        /// dirty the editor; MarkClean afterwards.
        /// </summary>
        public void LoadEntry(int pathId)
        {
            IsLoading = true;
            try
            {
                ROM rom = CoreState.ROM;
                CurrentPathId = pathId;
                Chips = WorldMapPathCore.LoadPath(rom, pathId);
                CurrentAddr = WorldMapPathCore.GetPathDataOffset(rom, pathId, out uint off) ? off : 0;
                IsLoaded = true;
            }
            finally
            {
                IsLoading = false;
            }
            MarkClean();
        }

        /// <summary>
        /// Paint (or erase) a chip at the given world pixel coordinates (port of
        /// WF <c>PutPathChip</c>). When <see cref="IsEraseSelected"/>: remove an
        /// existing chip at (x,y). Otherwise: update an existing chip at (x,y) to
        /// the selected palette cell, or add a new one. Marks the editor dirty on
        /// a genuine edit. Returns true if the chip list changed.
        /// </summary>
        public bool PutPathChip(int worldX, int worldY)
        {
            bool erase = IsEraseSelected;
            int pathX = SelectedChipCol * 8;
            int pathY = SelectedChipRow * 8;

            for (int i = 0; i < Chips.Count; i++)
            {
                if (Chips[i].WorldX == worldX && Chips[i].WorldY == worldY)
                {
                    if (erase)
                    {
                        Chips.RemoveAt(i);
                    }
                    else
                    {
                        var c = Chips[i];
                        if (c.PathX == pathX && c.PathY == pathY) return false; // no change
                        c.PathX = pathX;
                        c.PathY = pathY;
                        Chips[i] = c;
                    }
                    MarkDirtyEdit();
                    return true;
                }
            }

            if (erase) return false; // nothing to erase

            Chips.Add(new PathChip(worldX, worldY, pathX, pathY));
            MarkDirtyEdit();
            return true;
        }

        /// <summary>
        /// Eyedropper (port of WF <c>SelectSpointChip</c>): if a chip exists at
        /// (x,y), set the palette selection to its flip/row. Returns true on a hit.
        /// </summary>
        public bool PickChipAt(int worldX, int worldY)
        {
            foreach (var c in Chips)
            {
                if (c.WorldX == worldX && c.WorldY == worldY)
                {
                    SelectedChipCol = c.PathX / 8;
                    SelectedChipRow = c.PathY / 8;
                    return true;
                }
            }
            return false;
        }

        /// <summary>Render the composite world map + road overlay (null on failure).</summary>
        public IImage RenderComposite() => WorldMapPathCore.TryRenderPathComposite(CoreState.ROM, Chips);

        /// <summary>Render the 5-column chip palette (null on failure).</summary>
        public IImage RenderChipPalette(out int cols) => WorldMapPathCore.TryRenderChipPalette(CoreState.ROM, out cols);

        /// <summary>
        /// Write the current chip buffer for the selected path. Returns the empty
        /// string on success (and MarkClean), or a non-empty error message.
        /// </summary>
        public string WritePath()
        {
            if (CurrentPathId < 0) return "No path selected.";
            string err = WorldMapPathCore.WritePath(CoreState.ROM, CurrentPathId, Chips);
            if (err == "") MarkClean();
            return err;
        }

        // A user paint/erase is a genuine edit. SetField on a list-content change
        // would not fire (the List reference is unchanged), so dirty explicitly
        // via a sentinel property toggle that respects IsLoading.
        void MarkDirtyEdit()
        {
            if (IsLoading) return;
            // Bump CurrentAddr's notify without changing it would not dirty;
            // instead set a dedicated flag through SetField on a throwaway field.
            _editCounter++;
            SetField(ref _editSignal, _editCounter, nameof(EditSignal));
        }

        int _editCounter;
        int _editSignal;
        /// <summary>Increments on every genuine paint edit (drives IsDirty).</summary>
        public int EditSignal { get => _editSignal; private set => SetField(ref _editSignal, value); }
    }
}
