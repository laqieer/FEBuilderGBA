using System;
using System.Collections.Generic;
using global::Avalonia.Media.Imaging;
using FEBuilderGBA.Core;

namespace FEBuilderGBA.Avalonia.Services
{
    /// <summary>
    /// Centralized icon loaders for AddressListControl.SetItemsWithIcons().
    /// Each method returns a Bitmap? for a given list item index.
    /// IDs are extracted from item text via U.atoh() to match WinForms DrawXxxAndText behavior.
    /// </summary>
    public static class ListIconLoaders
    {
        /// <summary>
        /// Load class wait icon by extracting class ID from the list item text prefix.
        /// Matches WinForms DrawClassAndText which uses U.atoh(text) to get the class ID.
        /// </summary>
        /// <remarks>
        /// #654: previously short-circuited on <c>classId == 0</c>, which
        /// suppressed the icon for the very first row of every class list
        /// (its prefix parses to 0). WinForms <c>ListBoxEx.DrawClassAndText</c>
        /// has no such guard — class 0 still gets fed to
        /// <c>ClassForm.DrawWaitIcon</c>, which returns
        /// <c>ImageUtil.BlankDummy()</c> for invalid data so the icon column
        /// stays the same width. Removing the guard lets
        /// <see cref="PreviewIconHelper.LoadClassWaitIconByClassId"/> attempt
        /// the lookup for class 0 too (it returns null only when ROM data is
        /// invalid).
        /// </remarks>
        public static Bitmap? ClassIconLoader(List<AddrResult> items, int index)
        {
            if (index < 0 || index >= items.Count) return null;
            try
            {
                uint classId = U.atoh(items[index].name);
                using var img = PreviewIconHelper.LoadClassWaitIconByClassId(classId);
                return ImageConversionHelper.ToAvaloniaBitmap(img);
            }
            catch { return null; }
        }

        /// <summary>
        /// Load class wait icon using an explicit class-id selector instead of
        /// parsing the list-item text prefix. Use this when the displayed row
        /// prefix is the row INDEX (not the class id) but the list still wants
        /// the correct class icon — the prefix and the entity id diverge for
        /// OP Class Demo / Arena Class lists (#939). The selector reads the
        /// real class id directly from the entry's ROM address.
        /// </summary>
        /// <param name="items">The address list items.</param>
        /// <param name="index">Index into the list.</param>
        /// <param name="classIdSelector">
        /// Resolves the real class id for an entry (e.g.
        /// <c>r =&gt; CoreState.ROM.u8(r.addr + 14)</c>). Should guard against a
        /// null ROM and return 0 (the loader then returns null).
        /// </param>
        public static Bitmap? ClassIconLoader(List<AddrResult> items, int index, Func<AddrResult, uint> classIdSelector)
        {
            if (index < 0 || index >= items.Count) return null;
            try
            {
                uint id = classIdSelector(items[index]);
                using var img = PreviewIconHelper.LoadClassWaitIconByClassId(id);
                return ImageConversionHelper.ToAvaloniaBitmap(img);
            }
            catch { return null; }
        }

        /// <summary>
        /// Load item icon by extracting item ID from the list item text prefix.
        /// Matches WinForms DrawItemAndText which uses U.atoh(text) to get the item ID.
        /// </summary>
        /// <remarks>
        /// #654: previously short-circuited on <c>itemId == 0</c>, hiding the
        /// first row's icon in every item list (its prefix parses to 0).
        /// WinForms <c>ListBoxEx.DrawItemAndText</c> has no such guard.
        /// </remarks>
        public static Bitmap? ItemIconLoader(List<AddrResult> items, int index)
        {
            if (index < 0 || index >= items.Count) return null;
            try
            {
                uint itemId = U.atoh(items[index].name);
                using var img = PreviewIconHelper.LoadItemIconByItemId(itemId);
                return ImageConversionHelper.ToAvaloniaBitmap(img);
            }
            catch { return null; }
        }

        /// <summary>
        /// Load item icon using an explicit item-id selector instead of parsing
        /// the list-item text prefix. Use when the displayed row prefix is the
        /// row INDEX (not the item id) but the correct item icon is still
        /// wanted (#939). The selector reads the real item id directly from the
        /// entry's ROM address.
        /// </summary>
        /// <param name="items">The address list items.</param>
        /// <param name="index">Index into the list.</param>
        /// <param name="itemIdSelector">
        /// Resolves the real item id for an entry. Should guard against a null
        /// ROM and return 0 (the loader then returns null).
        /// </param>
        public static Bitmap? ItemIconLoader(List<AddrResult> items, int index, Func<AddrResult, uint> itemIdSelector)
        {
            if (index < 0 || index >= items.Count) return null;
            try
            {
                uint id = itemIdSelector(items[index]);
                using var img = PreviewIconHelper.LoadItemIconByItemId(id);
                return ImageConversionHelper.ToAvaloniaBitmap(img);
            }
            catch { return null; }
        }

        /// <summary>
        /// Load item icon directly by icon index (not item ID).
        /// Use for icon table views where the list index IS the icon index.
        /// </summary>
        public static Bitmap? DirectItemIconLoader(List<AddrResult> items, int index)
        {
            if (index < 0 || index >= items.Count) return null;
            try
            {
                uint iconIndex = U.atoh(items[index].name);
                using var img = PreviewIconHelper.LoadItemIcon(iconIndex);
                return ImageConversionHelper.ToAvaloniaBitmap(img);
            }
            catch { return null; }
        }

        /// <summary>
        /// Load unit portrait by interpreting items[index].addr as a unit struct address.
        /// ONLY use when the list items are from the actual unit table (addr points to a unit struct).
        /// For other tables (support, palette, summon, etc.), use UnitPortraitByIdLoader instead.
        /// </summary>
        /// <remarks>
        /// #654: <c>portraitId == 0</c> means the unit struct has no portrait
        /// assigned (a NULL portrait pointer in the struct), which is semantically
        /// distinct from "first list row". Keep the early return because the
        /// loader's input is the ROM address, not the row index — index 0 of
        /// the unit table can still have a non-zero portrait_id.
        /// </remarks>
        public static Bitmap? UnitPortraitLoader(List<AddrResult> items, int index)
        {
            if (index < 0 || index >= items.Count) return null;
            try
            {
                uint addr = items[index].addr;
                uint portraitId = PreviewIconHelper.ResolveUnitPortraitId(addr);
                if (portraitId == 0) return null;
                using var img = PreviewIconHelper.LoadPortraitMini(portraitId);
                return ImageConversionHelper.ToAvaloniaBitmap(img);
            }
            catch { return null; }
        }

        /// <summary>
        /// Load unit portrait by extracting the unit ID from the list item text prefix,
        /// then resolving that unit's portrait from the unit table.
        /// Use for views where the list items are NOT unit struct entries (support tables,
        /// summon tables, palette tables, etc.) but the text starts with a unit ID.
        /// Matches WinForms <c>DrawUnitAndText</c> which uses <c>U.atoh(text)</c> to get
        /// the (1-based) unit ID. The 1-based-id helper subtracts 1 internally so the
        /// portrait shown matches the unit name printed in the row, fixing #652/#653.
        /// </summary>
        /// <remarks>
        /// #652/#653: switched to <c>ResolveUnitPortraitIdByOneBasedId</c> so
        /// the 1-based hex prefix produced by <c>U.atoh(items[index].name)</c>
        /// (matching WinForms <c>DrawUnitAndText</c>) is decremented before
        /// indexing the unit table, fixing the off-by-one wrong-portrait bug
        /// in Support Unit / Support Talk lists. The 1-based-id helper still
        /// treats <c>unitId == 0</c> as the "no unit" sentinel and returns 0 —
        /// that is a real semantic (the unit table is 1-indexed in every
        /// supported ROM; there is no "unit 0"), not a row-index artifact, so
        /// we additionally bail on <c>portraitId == 0</c> AFTER resolution
        /// when the resolved unit has no portrait_id set in ROM.
        ///
        /// #654: removed the loader's own <c>unitId == 0</c> guard so every
        /// row gets exercised consistently with the other text-prefix loaders
        /// in this file (the post-resolution <c>portraitId == 0</c> check is
        /// the only short-circuit now).
        /// </remarks>
        public static Bitmap? UnitPortraitByIdLoader(List<AddrResult> items, int index)
        {
            if (index < 0 || index >= items.Count) return null;
            try
            {
                uint unitId = U.atoh(items[index].name);
                uint portraitId = PreviewIconHelper.ResolveUnitPortraitIdByOneBasedId(unitId);
                if (portraitId == 0) return null;
                using var img = PreviewIconHelper.LoadPortraitMini(portraitId);
                return ImageConversionHelper.ToAvaloniaBitmap(img);
            }
            catch { return null; }
        }

        /// <summary>
        /// Load portrait by extracting portrait ID from the list item text prefix.
        /// Matches WinForms DrawImagePortraitAndText which uses U.atoh(text) to get portrait ID.
        /// </summary>
        /// <remarks>
        /// #654: removed <c>portraitId == 0</c> guard so the first row (portrait 0)
        /// gets its icon. WinForms <c>ListBoxEx.DrawImagePortraitAndText</c>
        /// has no such guard.
        /// </remarks>
        public static Bitmap? PortraitLoader(List<AddrResult> items, int index)
        {
            if (index < 0 || index >= items.Count) return null;
            try
            {
                uint portraitId = U.atoh(items[index].name);
                using var img = PreviewIconHelper.LoadPortraitMini(portraitId);
                return ImageConversionHelper.ToAvaloniaBitmap(img);
            }
            catch { return null; }
        }

        /// <summary>
        /// Load item icon by reading item ID from ROM address as u16.
        /// For views where the first field at the entry address is a 16-bit item ID.
        /// Used by AIPerformItem and AIPerformStaff.
        /// </summary>
        public static Bitmap? ItemIconFromAddrU16Loader(List<AddrResult> items, int index)
        {
            if (index < 0 || index >= items.Count) return null;
            try
            {
                ROM rom = CoreState.ROM;
                if (rom?.RomInfo == null) return null;
                uint addr = items[index].addr;
                if (!U.isSafetyOffset(addr + 1)) return null;
                uint itemId = rom.u16(addr);
                if (itemId == 0) return null;
                using var img = PreviewIconHelper.LoadItemIconByItemId(itemId);
                return ImageConversionHelper.ToAvaloniaBitmap(img);
            }
            catch { return null; }
        }

        /// <summary>
        /// Load item icon by reading item ID from ROM address as u8.
        /// For views where the first field at the entry address is an 8-bit item ID.
        /// Used by AIStealItem and ArenaEnemyWeapon.
        /// </summary>
        public static Bitmap? ItemIconFromAddrU8Loader(List<AddrResult> items, int index)
        {
            if (index < 0 || index >= items.Count) return null;
            try
            {
                ROM rom = CoreState.ROM;
                if (rom?.RomInfo == null) return null;
                uint addr = items[index].addr;
                if (!U.isSafetyOffset(addr)) return null;
                uint itemId = rom.u8(addr);
                if (itemId == 0) return null;
                using var img = PreviewIconHelper.LoadItemIconByItemId(itemId);
                return ImageConversionHelper.ToAvaloniaBitmap(img);
            }
            catch { return null; }
        }

        /// <summary>
        /// Load unit portrait by reading a 1-based unit ID from ROM address as u16.
        /// For views where the first field at the entry address is a 16-bit unit ID
        /// (ROM bytes store unit IDs 1-based per WinForms convention).
        /// Used by EventBattleTalk and SupportTalk views.
        /// </summary>
        public static Bitmap? UnitPortraitFromAddrU16Loader(List<AddrResult> items, int index)
        {
            if (index < 0 || index >= items.Count) return null;
            try
            {
                ROM rom = CoreState.ROM;
                if (rom?.RomInfo == null) return null;
                uint addr = items[index].addr;
                if (!U.isSafetyOffset(addr + 1)) return null;
                uint unitId = rom.u16(addr);
                if (unitId == 0) return null;
                uint portraitId = PreviewIconHelper.ResolveUnitPortraitIdByOneBasedId(unitId);
                if (portraitId == 0) return null;
                using var img = PreviewIconHelper.LoadPortraitMini(portraitId);
                return ImageConversionHelper.ToAvaloniaBitmap(img);
            }
            catch { return null; }
        }

        /// <summary>
        /// Load unit portrait by reading a 1-based unit ID from ROM address as u8.
        /// For views where the first field at the entry address is an 8-bit unit ID
        /// (ROM bytes store unit IDs 1-based per WinForms convention).
        /// Used by AIUnits (u8 unitId at +0, u8 unknown at +1).
        /// </summary>
        public static Bitmap? UnitPortraitFromAddrU8Loader(List<AddrResult> items, int index)
        {
            if (index < 0 || index >= items.Count) return null;
            try
            {
                ROM rom = CoreState.ROM;
                if (rom?.RomInfo == null) return null;
                uint addr = items[index].addr;
                if (!U.isSafetyOffset(addr)) return null;
                uint unitId = rom.u8(addr);
                if (unitId == 0) return null;
                uint portraitId = PreviewIconHelper.ResolveUnitPortraitIdByOneBasedId(unitId);
                if (portraitId == 0) return null;
                using var img = PreviewIconHelper.LoadPortraitMini(portraitId);
                return ImageConversionHelper.ToAvaloniaBitmap(img);
            }
            catch { return null; }
        }

        /// <summary>
        /// Load a horizontally-stitched 64x32 RGBA pair containing two unit
        /// portraits (left = unit at <c>addr+0</c>, right = unit at
        /// <c>addr+unit2Offset</c>) read directly from ROM via
        /// <see cref="ROM.u8(uint)"/>.
        ///
        /// IMPORTANT: this loader does NOT parse the list-text prefix — it
        /// reads from <c>items[index].addr</c>. The version-specific
        /// <paramref name="unit2Offset"/> parameter selects where partner 2
        /// lives:
        ///   - FE8     : <c>unit2Offset = 2</c>
        ///   - FE6/FE7 : <c>unit2Offset = 1</c>
        /// Matches WinForms <c>ListBoxEx.DrawUnit2AndText</c> used by
        /// <c>SupportTalk{,FE6,FE7}Form</c>.
        ///
        /// Used by <c>SupportTalk{,FE6,FE7}View</c>. Issue #361.
        /// </summary>
        public static Bitmap? UnitPortraitPairFromAddrU8Loader(List<AddrResult> items, int index, int unit2Offset)
        {
            using var img = UnitPortraitPairFromAddrU8LoaderInternal(items, index, unit2Offset);
            return ImageConversionHelper.ToAvaloniaBitmap(img);
        }

        /// <summary>
        /// Test-visible internal helper: returns the raw <see cref="IImage"/>
        /// produced by reading <c>uid1 = u8(addr)</c> and
        /// <c>uid2 = u8(addr + unit2Offset)</c>, resolving each to a portrait
        /// ID, and compositing via <see cref="PreviewIconHelper.LoadPortraitMiniPair"/>.
        ///
        /// Exists so tests can verify the offset-reading behavior without
        /// requiring Avalonia headless rendering (the public method depends
        /// on <see cref="ImageConversionHelper.ToAvaloniaBitmap"/> which
        /// triggers PNG decode in Avalonia). Tests compare
        /// <see cref="IImage.GetPixelData"/> directly. Issue #361.
        /// </summary>
        internal static IImage UnitPortraitPairFromAddrU8LoaderInternal(List<AddrResult> items, int index, int unit2Offset)
        {
            if (index < 0 || index >= items.Count) return null;
            if (unit2Offset <= 0) return null;
            try
            {
                ROM rom = CoreState.ROM;
                if (rom?.RomInfo == null) return null;
                uint addr = items[index].addr;
                // Validate BOTH read positions independently: U.isSafetyOffset
                // requires >= 0x200, but a degenerate addr < 0x200 with
                // unit2Offset=1 would pass the addr+offset check while still
                // reading from an unsafe addr at offset 0 (Copilot review).
                if (!U.isSafetyOffset(addr)) return null;
                if (!U.isSafetyOffset(addr + (uint)unit2Offset)) return null;
                uint uid1 = rom.u8(addr);
                uint uid2 = rom.u8(addr + (uint)unit2Offset);
                // uid1/uid2 are ROM-stored 1-based unit IDs (WinForms DrawUnit2AndText
                // convention). Use the 1-based resolver so the portrait shown lines up
                // with the unit name printed in the row (#653).
                uint pid1 = PreviewIconHelper.ResolveUnitPortraitIdByOneBasedId(uid1);
                uint pid2 = PreviewIconHelper.ResolveUnitPortraitIdByOneBasedId(uid2);
                return PreviewIconHelper.LoadPortraitMiniPair(pid1, pid2);
            }
            catch { return null; }
        }

        /// <summary>
        /// Load wait icon directly by parsing the list item text as a wait icon index.
        /// For ImageUnitWaitIcon view where items represent wait icon entries.
        /// </summary>
        public static Bitmap? WaitIconDirectLoader(List<AddrResult> items, int index)
        {
            if (index < 0 || index >= items.Count) return null;
            try
            {
                uint iconIndex = U.atoh(items[index].name);
                using var img = PreviewIconHelper.LoadClassWaitIcon(iconIndex);
                return ImageConversionHelper.ToAvaloniaBitmap(img);
            }
            catch { return null; }
        }

        /// <summary>
        /// Load move icon by parsing the list item text as a move icon index.
        /// Move icon IDs are 1-based; the loader handles the conversion.
        /// For ImageUnitMoveIcon view.
        /// </summary>
        public static Bitmap? MoveIconLoader(List<AddrResult> items, int index)
        {
            if (index < 0 || index >= items.Count) return null;
            try
            {
                uint iconIndex = U.atoh(items[index].name);
                // Move icon LoadMoveIcon expects 1-based ID and handles subtraction internally
                // But list items are 0-based indices, so add 1
                using var img = PreviewIconHelper.LoadMoveIcon(iconIndex + 1);
                return ImageConversionHelper.ToAvaloniaBitmap(img);
            }
            catch { return null; }
        }

        /// <summary>
        /// Load a color swatch from a GBA BGR555 color at the entry address (u16).
        /// For SystemHoverColor, ImageSystemArea, and MapTileAnimation2 views.
        /// </summary>
        public static Bitmap? ColorSwatchLoader(List<AddrResult> items, int index)
        {
            if (index < 0 || index >= items.Count) return null;
            try
            {
                ROM rom = CoreState.ROM;
                if (rom?.RomInfo == null) return null;
                uint addr = items[index].addr;
                if (!U.isSafetyOffset(addr + 1)) return null;
                uint gbaColor = rom.u16(addr);
                using var img = PreviewIconHelper.CreateColorSwatch(gbaColor);
                return ImageConversionHelper.ToAvaloniaBitmap(img);
            }
            catch { return null; }
        }

        /// <summary>
        /// Load a battle animation thumbnail by reading the animation ID from ROM entry.
        /// For ImageBattleAnime view (4-byte entries: B0=type, B1=flags, W2=animeId).
        /// </summary>
        public static Bitmap? BattleAnimeLoader(List<AddrResult> items, int index)
        {
            if (index < 0 || index >= items.Count) return null;
            try
            {
                ROM rom = CoreState.ROM;
                if (rom?.RomInfo == null) return null;
                uint addr = items[index].addr;
                if (!U.isSafetyOffset(addr + 3)) return null;
                uint animeId = rom.u16(addr + 2);
                if (animeId == 0) return null;
                using var img = PreviewIconHelper.LoadBattleAnimeThumbnail(animeId);
                return ImageConversionHelper.ToAvaloniaBitmap(img);
            }
            catch { return null; }
        }

        /// <summary>
        /// Load a battle animation thumbnail by parsing animation ID from the list text.
        /// For MantAnimation view where entries are 4-byte pointers and the text prefix
        /// is the animation row index (same as what WinForms DrawImageBattleAndText uses).
        /// </summary>
        public static Bitmap? BattleAnimeTextLoader(List<AddrResult> items, int index)
        {
            if (index < 0 || index >= items.Count) return null;
            try
            {
                uint animeId = U.atoh(items[index].name);
                if (animeId == 0) return null;
                using var img = PreviewIconHelper.LoadBattleAnimeThumbnail(animeId);
                return ImageConversionHelper.ToAvaloniaBitmap(img);
            }
            catch { return null; }
        }

        /// <summary>
        /// Load a BG image thumbnail for background image entries.
        /// Entry layout: P0=image, P4=TSA, P8=palette.
        /// </summary>
        public static Bitmap? BGThumbnailLoader(List<AddrResult> items, int index)
        {
            if (index < 0 || index >= items.Count) return null;
            try
            {
                uint addr = items[index].addr;
                using var img = PreviewIconHelper.LoadBGThumbnail(addr);
                return ImageConversionHelper.ToAvaloniaBitmap(img);
            }
            catch { return null; }
        }

        /// <summary>
        /// Load a CG image thumbnail for CG entries.
        /// Entry layout: P0=image, P4=TSA, P8=palette.
        /// </summary>
        public static Bitmap? CGThumbnailLoader(List<AddrResult> items, int index)
        {
            if (index < 0 || index >= items.Count) return null;
            try
            {
                uint addr = items[index].addr;
                using var img = PreviewIconHelper.LoadCGThumbnail(addr);
                return ImageConversionHelper.ToAvaloniaBitmap(img);
            }
            catch { return null; }
        }

        /// <summary>
        /// Load a CG image thumbnail for FE7U CG entries.
        /// Entry layout: B0=type, B1-B3=reserved, P4=image, P8=TSA, P12=palette.
        /// </summary>
        public static Bitmap? CGFE7UThumbnailLoader(List<AddrResult> items, int index)
        {
            if (index < 0 || index >= items.Count) return null;
            try
            {
                uint addr = items[index].addr;
                using var img = PreviewIconHelper.LoadCGFE7UThumbnail(addr);
                return ImageConversionHelper.ToAvaloniaBitmap(img);
            }
            catch { return null; }
        }

        /// <summary>
        /// Load a CG thumbnail for SoundRoomCG entries.
        /// Each entry has a CG ID (u32) which maps to the bigcg_pointer table.
        /// CG entry layout in table: P0=image, P4=TSA, P8=palette (SIZE=12).
        /// </summary>
        public static Bitmap? SoundRoomCGThumbnailLoader(List<AddrResult> items, int index)
        {
            if (index < 0 || index >= items.Count) return null;
            try
            {
                ROM rom = CoreState.ROM;
                if (rom?.RomInfo == null) return null;

                // Read the CG ID from this SoundRoom entry
                uint entryAddr = items[index].addr;
                if (!U.isSafetyOffset(entryAddr + 3)) return null;
                uint cgId = rom.u32(entryAddr);
                if (cgId == 0 || cgId == 0xFFFFFFFF) return null;

                // Resolve CG ID to CG table entry
                uint cgPtr = rom.RomInfo.bigcg_pointer;
                if (cgPtr == 0) return null;
                uint cgBase = rom.p32(cgPtr);
                if (!U.isSafetyOffset(cgBase)) return null;

                // FE7U uses 16-byte bigcg entries with different layout; others use 12-byte
                bool isFE7U = rom.RomInfo.version == 7 && !rom.RomInfo.is_multibyte;
                uint cgEntrySize = isFE7U ? 16u : 12u;
                uint cgAddr = cgBase + cgId * cgEntrySize;
                if (cgAddr + cgEntrySize > (uint)rom.Data.Length) return null;

                using var img = isFE7U
                    ? PreviewIconHelper.LoadCGFE7UThumbnail(cgAddr)
                    : PreviewIconHelper.LoadCGThumbnail(cgAddr);
                return ImageConversionHelper.ToAvaloniaBitmap(img);
            }
            catch { return null; }
        }
        /// <summary>
        /// Load an attribute (affinity) icon by extracting the attribute type from the list item text.
        /// Matches WinForms DrawAtributeAndText which calls ImageSystemIconForm.Attribute(type).
        /// Attribute type is 1-based (1=Fire, 2=Thunder, ..., 7=Anima).
        /// Icon index = type + 0x79 (maps to item icon indices 0x7A-0x80).
        /// Uses weapon palette instead of normal item palette.
        /// </summary>
        public static Bitmap? AttributeIconLoader(List<AddrResult> items, int index)
        {
            if (index < 0 || index >= items.Count) return null;
            try
            {
                uint type = U.atoh(items[index].name);
                if (type == 0) return null;
                // Attribute icon index = type + 0x7A - 1 = type + 0x79
                // (matching WinForms ImageSystemIconForm.Attribute which calls
                //  DrawIconWhereID_UsingWeaponPalette(type + 0x7A - 1))
                using var img = PreviewIconHelper.LoadItemIconWithWeaponPalette(type + 0x79);
                return ImageConversionHelper.ToAvaloniaBitmap(img);
            }
            catch { return null; }
        }

        /// <summary>
        /// Load a map action animation thumbnail by reading the animation pointer (D0)
        /// from the table entry at items[index].addr.
        /// Renders the first frame of the animation as a 64x64 thumbnail.
        /// </summary>
        public static Bitmap? MapActionAnimationLoader(List<AddrResult> items, int index)
        {
            if (index < 0 || index >= items.Count) return null;
            try
            {
                ROM rom = CoreState.ROM;
                if (rom?.RomInfo == null) return null;
                uint addr = items[index].addr;
                if (!U.isSafetyOffset(addr + 3)) return null;
                // D0 (u32 at offset 0) is the animation pointer
                uint animePointer = rom.u32(addr);
                if (animePointer == 0 || !U.isPointer(animePointer)) return null;
                using var img = PreviewIconHelper.LoadMapActionAnimationThumbnail(animePointer);
                return ImageConversionHelper.ToAvaloniaBitmap(img);
            }
            catch { return null; }
        }

        /// <summary>
        /// Load a horizontally-stitched 32x16 weapon-type icon pair by reading
        /// the two weapon-type IDs at <c>items[index].addr</c> (byte 0) and
        /// <c>items[index].addr + 1</c> (byte 1) directly from ROM.
        ///
        /// IMPORTANT: this loader does NOT parse the list-text prefix — it
        /// reads from the ROM address. This is required because the row text
        /// prefix may not match the actual weapon-type ID (e.g. the WinForms
        /// `DrawWeaponTypeIcon2AndText` semantic uses the prefix as the first
        /// weapon type, but the entry can be re-ordered or hand-edited).
        ///
        /// Mirrors WinForms `ListBoxEx.DrawWeaponTypeIcon2AndText`.
        /// Used by `ItemWeaponTriangleViewerView`. Issue #370.
        /// </summary>
        public static Bitmap? WeaponTypePairFromAddrU8Loader(List<AddrResult> items, int index)
        {
            if (index < 0 || index >= items.Count) return null;
            try
            {
                ROM rom = CoreState.ROM;
                if (rom?.RomInfo == null) return null;
                uint addr = items[index].addr;
                if (!U.isSafetyOffset(addr + 1)) return null;
                uint type1 = rom.u8(addr);
                uint type2 = rom.u8(addr + 1);
                using var img = PreviewIconHelper.LoadWeaponTypePairIcon(type1, type2);
                return ImageConversionHelper.ToAvaloniaBitmap(img);
            }
            catch { return null; }
        }

        /// <summary>
        /// Load a skill icon for the SkillSystem patch.
        /// Dynamically locates the skill icon base address via binary pattern search,
        /// then renders the 16x16 4bpp tile at the given index.
        /// Matches WinForms SkillConfigSkillSystemForm.DrawIcon(index, iconBaseAddress).
        ///
        /// Note: prefer the overload that accepts a pre-resolved
        /// <c>iconBaseAddress</c> to avoid 255 redundant byte-pattern scans
        /// when populating the address list (Copilot bot review on PR #525).
        /// </summary>
        public static Bitmap? SkillIconLoader(List<AddrResult> items, int index)
        {
            if (index < 0 || index >= items.Count) return null;
            try
            {
                uint iconBase = PreviewIconHelper.FindSkillSystemIconBaseAddress();
                if (iconBase == 0) return null;
                return SkillIconLoader(items, index, iconBase);
            }
            catch { return null; }
        }

        /// <summary>
        /// Overload that takes a pre-resolved icon base address so callers
        /// (e.g. the SkillConfigSkillSystemView populating its list) don't
        /// re-run the full byte-pattern scan for every row.
        /// </summary>
        public static Bitmap? SkillIconLoader(List<AddrResult> items, int index, uint iconBaseAddress)
        {
            if (index < 0 || index >= items.Count) return null;
            if (iconBaseAddress == 0) return null;
            try
            {
                using var img = PreviewIconHelper.LoadSkillIcon((uint)index, iconBaseAddress);
                return ImageConversionHelper.ToAvaloniaBitmap(img);
            }
            catch { return null; }
        }

        /// <summary>
        /// Load a skill icon for the CSkillSys 0.9.x patch. Each skill row
        /// has its own per-skill icon pointer at entry+0 (different from
        /// SkillSystem which stripes icons sequentially after a single base).
        /// We read that pointer from the row's address and dereference.
        /// Mirrors WinForms `SkillConfigCSkillSystem09xForm.DrawSkillIcon(index)`.
        /// </summary>
        public static Bitmap? CSkillSysSkillIconLoader(List<AddrResult> items, int index)
        {
            if (index < 0 || index >= items.Count) return null;
            try
            {
                ROM rom = CoreState.ROM;
                if (rom == null) return null;
                uint rowAddr = items[index].addr;
                if (!U.isSafetyOffset(rowAddr + 4, rom)) return null;
                uint iconGbaPointer = rom.u32(rowAddr + 0);
                if (iconGbaPointer == 0) return null;
                using var img = PreviewIconHelper.LoadCSkillSysIcon(iconGbaPointer);
                return ImageConversionHelper.ToAvaloniaBitmap(img);
            }
            catch { return null; }
        }

        /// <summary>
        /// Load a skill icon for the FE8N v2 skill expansion. Unlike SkillSystem
        /// (separate striped tile table) and CSkillSys 0.9.x (per-row GBA
        /// pointer), FE8N v2 uses the WF-standard
        /// <c>rom.p32(RomInfo.icon_pointer) + 128 * (0x100 + id)</c> path with
        /// palette selection driven by the per-skill W2 palette field.
        /// Mirrors WinForms <c>SkillConfigFE8NVer2SkillForm.DrawSkillIconLow(id)</c>.
        /// </summary>
        /// <param name="items">List of skill rows from the address list.</param>
        /// <param name="index">Index into the list.</param>
        public static Bitmap? FE8NVer2SkillIconLoader(List<AddrResult> items, int index)
        {
            if (index < 0 || index >= items.Count) return null;
            try
            {
                ROM rom = CoreState.ROM;
                if (rom == null) return null;
                uint rowAddr = items[index].addr;
                if (!U.isSafetyOffset(rowAddr + 4, rom)) return null;
                // W2 = palette field at row+2.
                uint paletteIndex = rom.u16(rowAddr + 2);
                using var img = PreviewIconHelper.LoadFE8NVer2SkillIcon((uint)index, paletteIndex);
                return ImageConversionHelper.ToAvaloniaBitmap(img);
            }
            catch { return null; }
        }

        /// <summary>
        /// Load a skill icon for the FE8N v3 skill expansion. Identical
        /// formula to v2 - palette selection driven by the per-skill W2
        /// palette field, icon storage uses
        /// <c>rom.p32(RomInfo.icon_pointer) + 128 * (0x100 + id)</c>.
        /// Mirrors WinForms <c>SkillConfigFE8NVer3SkillForm.DrawSkillIconLow(id)</c>.
        /// </summary>
        public static Bitmap? FE8NVer3SkillIconLoader(List<AddrResult> items, int index)
        {
            if (index < 0 || index >= items.Count) return null;
            try
            {
                ROM rom = CoreState.ROM;
                if (rom == null) return null;
                uint rowAddr = items[index].addr;
                if (!U.isSafetyOffset(rowAddr + 4, rom)) return null;
                // W2 = palette field at row+2.
                uint paletteIndex = rom.u16(rowAddr + 2);
                using var img = PreviewIconHelper.LoadFE8NVer3SkillIcon((uint)index, paletteIndex);
                return ImageConversionHelper.ToAvaloniaBitmap(img);
            }
            catch { return null; }
        }

        /// <summary>
        /// Render the main-font glyph at the row's address into a list-row icon so
        /// the Font editor's list reads as a visual glyph grid (#1165). The glyph
        /// is the 16x16 2bpp bitmap at <c>addr+8</c>; <paramref name="isItemFont"/>
        /// selects the item vs serif background tint. Returns null on any failure
        /// (the row still shows its character label).
        /// </summary>
        public static Bitmap? FontGlyphLoader(List<AddrResult> items, int index, bool isItemFont)
        {
            if (index < 0 || index >= items.Count) return null;
            try
            {
                ROM rom = CoreState.ROM;
                if (rom == null) return null;
                using var img = FontGlyphRenderCore.RenderGlyph(rom, items[index].addr, isItemFont);
                return ImageConversionHelper.ToAvaloniaBitmap(img);
            }
            catch { return null; }
        }
    }
}
