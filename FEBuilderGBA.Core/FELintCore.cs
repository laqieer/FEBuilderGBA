using System;

namespace FEBuilderGBA
{
    /// <summary>
    /// Core lint types and WinForms-independent validation logic.
    /// Full lint implementation remains in WinForms FELint.cs.
    /// </summary>
    public static class FELintCore
    {
        /// <summary>System map ID constant used by lint checks.</summary>
        public const uint SYSTEM_MAP_ID = 0xEEEEEEEE;

        /// <summary>Lint error/warning category.</summary>
        public enum Type
        {
             EVENT_COND_TURN
            ,EVENT_COND_TALK
            ,EVENT_COND_OBJECT
            ,EVENT_COND_ALWAYS
            ,EVENT_COND_TUTORIAL
            ,EVENT_COND_TRAP
            ,EVENT_COND_PLAYER_UNIT
            ,EVENT_COND_ENEMY_UNIT
            ,EVENT_COND_START_EVENT
            ,EVENT_COND_END_EVENT
            ,EVENTSCRIPT
            ,EVENTUNITS
            ,MAPSETTING
            ,MAPSETTING_PLIST_OBJECT
            ,MAPSETTING_PLIST_CONFIG
            ,MAPSETTING_PLIST_PALETTE
            ,MAPSETTING_PLIST_MAP
            ,MAPSETTING_WORLDMAP
            ,MAPSETTING_PLIST_MAPCHANGE
            ,MAPSETTING_PLIST_ANIMETION1
            ,MAPSETTING_PLIST_ANIMETION2
            ,MAPSETTING_PLIST_EVENT
            ,WORLDMAP_EVENT
            ,BATTLE_ANIME
            ,BATTLE_ANIME_CLASS
            ,PORTRAIT
            ,BG
            ,HAIKU
            ,BATTTLE_TALK
            ,SUPPORT_TALK
            ,SUPPORT_UNIT
            ,MAPCHANGE
            ,SOUND_FOOT_STEPS
            ,UNIT
            ,CLASS
            ,ITEM
            ,ITEM_WEAPON_EFFECT
            ,MOVECOST_NORMAL
            ,MOVECOST_RAIN
            ,MOVECOST_SHOW
            ,MOVECOST_AVOID
            ,MOVECOST_DEF
            ,MOVECOST_RES
            ,OP_CLASS_DEMO
            ,WMAP_BASE_POINT
            ,SOUNDROOM
            ,SENSEKI
            ,DIC
            ,MENU
            ,MENU_DEFINE
            ,STATUS
            ,ED
            ,TERRAIN
            ,SKILL_CONFIG
            ,SKILL_CLASS
            ,SKILL_UNIT
            ,RMENU
            ,ITEM_USAGE_POINTER
            ,PATCH
            ,MAPEXIT
            ,IMAGE_UNIT_MOVE_ICON
            ,IMAGE_UNIT_WAIT_ICON
            ,ITEM_EEFECT_POINTER
            ,IMAGE_UNIT_PALETTE
            ,IMAGE_BATTLE_SCREEN
            ,MAGIC_ANIME_EXTENDS
            ,STATUS_GAME_OPTION
            ,STATUS_UNITS_MENU
            ,PROCS
            ,AISCRIPT
            ,ASM
            ,ASMDATA
            ,POINTER_TALKGROUP
            ,POINTER_MENUEXTENDS
            ,POINTER_UNITSSHORTTEXT
            ,SONGTABLE
            ,SONGTRACK
            ,SONGINST
            ,BOSS_BGM
            ,WORLDMAP_BGM
            ,EVENT_FINAL_SERIF
            ,ROM_HEADER
            ,TEXTID_FOR_SYSTEM
            ,TEXTID_FOR_USER
            ,SE_SYSTEM
            ,MAP_ACTION_ANIMATION
            ,FELINTBUZY_MESSAGE
            ,FELINT_SYSTEM_ERROR
        }

        /// <summary>Lint error record.</summary>
        public class ErrorSt
        {
            public Type DataType { get; private set; }
            public uint Addr { get; private set; }
            public string ErrorMessage { get; private set; }
            public uint Tag { get; private set; }

            public ErrorSt(Type datatype, uint addr, string message, uint tag = U.NOT_FOUND)
            {
                this.DataType = datatype;
                this.Addr = addr;
                this.ErrorMessage = message;
                this.Tag = tag;
            }
        }

        // ---- Pointer Validation (WinForms-free) ----

        /// <summary>
        /// Check if a ROM pointer is valid (points within ROM data range).
        /// </summary>
        public static bool IsValidPointer(ROM rom, uint pointer)
        {
            if (pointer == 0)
                return true; // null pointer is valid in some contexts
            uint addr = U.toOffset(pointer);
            return addr < (uint)rom.Data.Length;
        }

        /// <summary>
        /// Check if a ROM pointer is 4-byte aligned.
        /// </summary>
        public static bool IsAligned4(uint pointer)
        {
            return (pointer & 0x3) == 0;
        }

        /// <summary>
        /// Validate a pointer read from ROM at the given address.
        /// Returns null if valid, or an ErrorSt if invalid.
        /// </summary>
        public static ErrorSt CheckPointer(ROM rom, Type dataType, uint readAddr, string context)
        {
            if (readAddr >= (uint)rom.Data.Length)
                return new ErrorSt(dataType, readAddr, $"Address out of ROM range: {context}");

            uint pointer = rom.u32(readAddr);
            if (pointer == 0)
                return null; // null pointer OK
            if (!U.isPointer(pointer))
                return new ErrorSt(dataType, readAddr, $"Not a valid GBA pointer ({U.To0xHexString(pointer)}): {context}");

            uint offset = U.toOffset(pointer);
            if (offset >= (uint)rom.Data.Length)
                return new ErrorSt(dataType, readAddr, $"Pointer target out of range ({U.To0xHexString(pointer)}): {context}");

            return null; // valid
        }

        /// <summary>
        /// Validate a pointer is 4-byte aligned.
        /// Returns null if valid, or an ErrorSt if misaligned.
        /// </summary>
        public static ErrorSt CheckPointerAligned4(ROM rom, Type dataType, uint readAddr, string context)
        {
            var err = CheckPointer(rom, dataType, readAddr, context);
            if (err != null)
                return err;

            uint pointer = rom.u32(readAddr);
            if (pointer != 0 && !IsAligned4(U.toOffset(pointer)))
                return new ErrorSt(dataType, readAddr, $"Pointer not 4-byte aligned ({U.To0xHexString(pointer)}): {context}");

            return null;
        }
    }
}
