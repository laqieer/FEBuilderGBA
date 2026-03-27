using System;
using System.Collections.Generic;
using global::Avalonia.Media.Imaging;

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
        public static Bitmap? ClassIconLoader(List<AddrResult> items, int index)
        {
            if (index < 0 || index >= items.Count) return null;
            try
            {
                uint classId = U.atoh(items[index].name);
                if (classId == 0) return null;
                using var img = PreviewIconHelper.LoadClassWaitIconByClassId(classId);
                return ImageConversionHelper.ToAvaloniaBitmap(img);
            }
            catch { return null; }
        }

        /// <summary>
        /// Load item icon by extracting item ID from the list item text prefix.
        /// Matches WinForms DrawItemAndText which uses U.atoh(text) to get the item ID.
        /// </summary>
        public static Bitmap? ItemIconLoader(List<AddrResult> items, int index)
        {
            if (index < 0 || index >= items.Count) return null;
            try
            {
                uint itemId = U.atoh(items[index].name);
                if (itemId == 0) return null;
                using var img = PreviewIconHelper.LoadItemIconByItemId(itemId);
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
        /// Matches WinForms DrawUnitAndText which uses U.atoh(text) to get the unit ID.
        /// </summary>
        public static Bitmap? UnitPortraitByIdLoader(List<AddrResult> items, int index)
        {
            if (index < 0 || index >= items.Count) return null;
            try
            {
                uint unitId = U.atoh(items[index].name);
                if (unitId == 0) return null;
                uint portraitId = PreviewIconHelper.ResolveUnitPortraitIdByUnitId(unitId);
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
        public static Bitmap? PortraitLoader(List<AddrResult> items, int index)
        {
            if (index < 0 || index >= items.Count) return null;
            try
            {
                uint portraitId = U.atoh(items[index].name);
                if (portraitId == 0) return null;
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
        /// Load unit portrait by reading unit ID from ROM address as u16.
        /// For views where the first field at the entry address is a 16-bit unit ID.
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
                uint portraitId = PreviewIconHelper.ResolveUnitPortraitIdByUnitId(unitId);
                if (portraitId == 0) return null;
                using var img = PreviewIconHelper.LoadPortraitMini(portraitId);
                return ImageConversionHelper.ToAvaloniaBitmap(img);
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
        /// Load a battle animation thumbnail by parsing the animation number from ROM.
        /// For ImageBattleAnime and MantAnimation views.
        /// The animation ID is extracted from the entry (W2 field, offset +2 from entry addr).
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

                const uint CG_ENTRY_SIZE = 12;
                uint cgAddr = cgBase + cgId * CG_ENTRY_SIZE;
                if (cgAddr + CG_ENTRY_SIZE > (uint)rom.Data.Length) return null;

                using var img = PreviewIconHelper.LoadCGThumbnail(cgAddr);
                return ImageConversionHelper.ToAvaloniaBitmap(img);
            }
            catch { return null; }
        }
    }
}
