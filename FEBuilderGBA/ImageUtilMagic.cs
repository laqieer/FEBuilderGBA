using System;
using System.Collections.Generic;
using System.Text;
using System.Windows.Forms;
using System.Drawing;
using System.Diagnostics;
using System.ComponentModel;
using System.Drawing.Imaging;
using System.Reflection;
using System.IO;

namespace FEBuilderGBA
{
    class ImageUtilMagic
    {
        static magic_system_enum  g_Cache_magic_system_enum;
        public static void ClearCache()
        {
            g_Cache_magic_system_enum = magic_system_enum.NoCache;
            g_Cache_CSASpellTableAddr = U.NOT_FOUND;
            g_Cache_CSASpellTablePointer = U.NOT_FOUND;
        }

        //魔法システムの判別.
        public enum magic_system_enum
        {
             NO = 0
           , FEDITOR_ADV = 1
           , CSA_CREATOR = 2
           , NoCache = 0xFF
        };
        public static magic_system_enum SearchMagicSystem()
        {
            if (g_Cache_magic_system_enum == magic_system_enum.NoCache)
            {
                g_Cache_magic_system_enum = SearchMagicSystemLow();
            }
            return g_Cache_magic_system_enum;
        }
        static magic_system_enum SearchMagicSystemLow()
        {
            uint baseaddr, dimaddr, nodimaddr;
            return SearchMagicSystem(out baseaddr, out dimaddr, out nodimaddr);
        }

        // MagicPatchTableSt struct moved to FEBuilderGBA.Core.ImageUtilMagicCore (#418).
        public static magic_system_enum SearchMagicSystem(out uint baseaddr, out uint dimaddr, out uint nodimaddr)
        {
            // Delegate detection to FEBuilderGBA.Core.ImageUtilMagicCore so
            // the same logic runs from Avalonia / CLI (#418). We still
            // populate the WinForms-side cache fields so cross-form lookups
            // via GetCSASpellTableAddr() / GetCSASpellTablePointer() work.
            var coreSystem = ImageUtilMagicCore.SearchMagicSystem(
                Program.ROM, out baseaddr, out dimaddr, out nodimaddr);

            if (coreSystem == ImageUtilMagicCore.MagicSystem.No)
            {
                baseaddr = U.NOT_FOUND;
                dimaddr = U.NOT_FOUND;
                nodimaddr = U.NOT_FOUND;
                // Reset CSA cache fields so a previously-populated cache
                // doesn't leak stale addresses to callers if ClearCache()
                // wasn't called (Copilot CLI re-review on PR #554).
                g_Cache_CSASpellTableAddr = U.NOT_FOUND;
                g_Cache_CSASpellTablePointer = U.NOT_FOUND;
                g_Cache_magic_system_enum = magic_system_enum.NO;
                return g_Cache_magic_system_enum;
            }

            // Re-resolve the CSA pointer so the WF cache stays in sync.
            uint csaPointer;
            uint csaAddr = ImageUtilMagicCore.FindCSASpellTable(
                Program.ROM, coreSystem, out csaPointer);
            g_Cache_CSASpellTableAddr = csaAddr;
            g_Cache_CSASpellTablePointer = csaPointer;

            g_Cache_magic_system_enum = coreSystem == ImageUtilMagicCore.MagicSystem.FEditorAdv
                ? magic_system_enum.FEDITOR_ADV
                : magic_system_enum.CSA_CREATOR;
            return g_Cache_magic_system_enum;
        }

        static uint g_Cache_CSASpellTableAddr = U.NOT_FOUND;
        static uint g_Cache_CSASpellTablePointer = U.NOT_FOUND;
        public static uint GetCSASpellTableAddr()
        {
            return g_Cache_CSASpellTableAddr;
        }
        public static uint GetCSASpellTablePointer()
        {
            return g_Cache_CSASpellTablePointer;
        }
        
        // SpellTableSt struct + FindCSASpellTableLow logic moved to
        // FEBuilderGBA.Core.ImageUtilMagicCore.FindCSASpellTable (#418).

        //魔法拡張は大量の0x00地帯が生れるので、フリー領域と誤認しないように確認する.
        public static bool IsMagicArea(ref uint addr)
        {
            magic_system_enum magicType = SearchMagicSystem();
            if (magicType == magic_system_enum.NO)
            {
                return false;
            }
            uint csaSpellTable = ImageUtilMagic.GetCSASpellTableAddr();
            if (csaSpellTable == U.NOT_FOUND)
            {
                return false;
            }

            uint effect_table_addr = Program.ROM.p32(Program.ROM.RomInfo.magic_effect_pointer);
            if (!U.isSafetyOffset(effect_table_addr))
            {
                return false;
            }

            uint end = effect_table_addr + (0x4 * 0xff);
            if (addr >= effect_table_addr && addr < end)
            {
                addr = end;
                return true;
            }
            end = csaSpellTable + (0x4 * 0x5 * 0xff);
            if (addr >= csaSpellTable && addr < end)
            {
                addr = end;
                return true;
            }
            return false;
        }
    }
}
