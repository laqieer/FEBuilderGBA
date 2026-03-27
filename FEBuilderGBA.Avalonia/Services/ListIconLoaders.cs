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
    }
}
