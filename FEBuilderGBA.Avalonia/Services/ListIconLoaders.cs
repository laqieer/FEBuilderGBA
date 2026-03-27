using System;
using System.Collections.Generic;
using global::Avalonia.Media.Imaging;

namespace FEBuilderGBA.Avalonia.Services
{
    /// <summary>
    /// Centralized icon loaders for AddressListControl.SetItemsWithIcons().
    /// Each method returns a Bitmap? for a given list item index.
    /// </summary>
    public static class ListIconLoaders
    {
        public static Bitmap? ClassIconLoader(List<AddrResult> items, int index)
        {
            if (index < 0 || index >= items.Count) return null;
            try
            {
                uint classId = (uint)index;
                if (classId == 0) return null;
                using var img = PreviewIconHelper.LoadClassWaitIconByClassId(classId);
                return ImageConversionHelper.ToAvaloniaBitmap(img);
            }
            catch { return null; }
        }

        public static Bitmap? ItemIconLoader(List<AddrResult> items, int index)
        {
            if (index < 0 || index >= items.Count) return null;
            try
            {
                uint itemId = (uint)index;
                if (itemId == 0) return null;
                using var img = PreviewIconHelper.LoadItemIconByItemId(itemId);
                return ImageConversionHelper.ToAvaloniaBitmap(img);
            }
            catch { return null; }
        }

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

        public static Bitmap? PortraitLoader(List<AddrResult> items, int index)
        {
            if (index < 0 || index >= items.Count) return null;
            try
            {
                uint portraitId = (uint)index;
                if (portraitId == 0) return null;
                using var img = PreviewIconHelper.LoadPortraitMini(portraitId);
                return ImageConversionHelper.ToAvaloniaBitmap(img);
            }
            catch { return null; }
        }
    }
}
