using System.Collections.Generic;
using FEBuilderGBA.Avalonia.Services;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    /// <summary>
    /// FE8 Split Menu (Menu Extend Split) editor view-model.
    ///
    /// Faithful port of WinForms <c>MenuExtendSplitMenuForm</c> + the master-list
    /// geometry of <c>MenuDefinitionForm</c> (#1413 / #1430).
    ///
    /// Data model (the previous 40-byte / inline-u32-string model was WRONG and
    /// silently corrupted the ROM):
    ///   * A menu-definition entry is a 36-byte HEADER:
    ///       +0 x (u8), +1 y (u8), +2 width (u8), +3 height (u8),
    ///       +4 style (u32),
    ///       +8 POINTER to a separate command array,
    ///       +12/+16/+20/+24/+28/+32 ASM handler function pointers.
    ///   * The text IDs live in the DEREFERENCED command array (36-byte stride):
    ///       command n is at <c>menuaddr + 36*n</c>; its text id is the
    ///       <c>u16</c> at <c>menuaddr + 36*n + 4</c>, its menu id the <c>u8</c>
    ///       at <c>+9</c>, and its display/draw/effect ASM pointers at +12/+16/+20.
    ///   * A menu has 5 or 8 editable commands (<see cref="GetDataLength"/>); a
    ///     freshly allocated menu reserves 9 command slots plus a 0xFFFFFFFF
    ///     terminator (<c>36 + 36*9 + 4</c> bytes).
    ///
    /// Write preserves the header +8 command pointer and the header handler
    /// pointers; it only rewrites the header scalar fields and the text
    /// ids/menu ids/handler pointers INSIDE the dereferenced command array,
    /// exactly like WinForms <c>AllWriteButton_Click</c>.
    /// </summary>
    public class MenuExtendSplitMenuViewModel : ViewModelBase, IDataVerifiable
    {
        // Master-list scan: walk the menu_definiton_split_pointer table the same
        // way MenuDefinitionForm.Init does — 36-byte stride, stop when +8 is no
        // longer a pointer. (The old 40-byte / 32-entry walk fabricated rows.)
        const uint HeaderSize = 36;
        const uint CommandStride = 36;

        // Header field defs (Write uses these — note +8 and the handler
        // pointers are deliberately NOT in this set so they are never clobbered).
        static readonly List<EditorFormRef.FieldDef> _headerFields =
            EditorFormRef.DetectFields(new[] { "B0", "B1", "B2", "D4" });

        uint _currentAddr;
        bool _isLoaded;
        uint _posX, _posY, _width, _style;
        uint _commandPtr;       // p32(addr+8) GBA pointer (display only; never written here)
        int _stringCount;       // 5 or 8 — how many string fields are active
        uint _str0, _str1, _str2, _str3, _str4, _str5, _str6, _str7;

        public uint CurrentAddr { get => _currentAddr; set => SetField(ref _currentAddr, value); }
        public bool IsLoaded { get => _isLoaded; set => SetField(ref _isLoaded, value); }

        public uint PosX { get => _posX; set => SetField(ref _posX, value); }
        public uint PosY { get => _posY; set => SetField(ref _posY, value); }
        public uint Width { get => _width; set => SetField(ref _width, value); }
        public uint Style { get => _style; set => SetField(ref _style, value); }

        /// <summary>The header +8 command-array pointer (GBA form). Read-only here.</summary>
        public uint CommandPtr { get => _commandPtr; set => SetField(ref _commandPtr, value); }

        /// <summary>Number of active text-id fields for the current menu (5 or 8).</summary>
        public int StringCount { get => _stringCount; set => SetField(ref _stringCount, value); }

        // Text ids read/written from the dereferenced command array.
        public uint String0 { get => _str0; set => SetField(ref _str0, value); }
        public uint String1 { get => _str1; set => SetField(ref _str1, value); }
        public uint String2 { get => _str2; set => SetField(ref _str2, value); }
        public uint String3 { get => _str3; set => SetField(ref _str3, value); }
        public uint String4 { get => _str4; set => SetField(ref _str4, value); }
        public uint String5 { get => _str5; set => SetField(ref _str5, value); }
        public uint String6 { get => _str6; set => SetField(ref _str6, value); }
        public uint String7 { get => _str7; set => SetField(ref _str7, value); }

        /// <summary>
        /// Master list of split-menu definitions. Walks the split pointer table
        /// with the canonical 36-byte geometry and the <c>isPointer(u32(+8))</c>
        /// stop predicate (mirrors WinForms <c>MenuDefinitionForm.Init</c>).
        /// FE6/FE7 have the split pointer == 0 and so return an empty list.
        /// </summary>
        public List<AddrResult> LoadList()
        {
            ROM rom = CoreState.ROM;
            var result = new List<AddrResult>();
            if (rom?.RomInfo == null) return result;

            uint ptr = rom.RomInfo.menu_definiton_split_pointer;
            if (ptr == 0) return result;

            uint baseAddr = rom.p32(ptr);
            if (!U.isSafetyOffset(baseAddr, rom)) return result;

            for (uint i = 0; i < 0x100; i++)
            {
                uint addr = baseAddr + i * HeaderSize;
                if (addr + HeaderSize > (uint)rom.Data.Length) break;
                // Stop when the +8 command pointer is no longer a pointer.
                if (!U.isPointer(rom.u32(addr + 8))) break;

                result.Add(new AddrResult(addr, $"0x{i:X02} Split Menu {i}", i));
            }
            return result;
        }

        public void LoadEntry(uint addr)
        {
            ROM rom = CoreState.ROM;
            if (rom == null) return;
            if (!U.isSafetyOffset(addr + 8 + 4, rom)) return;

            CurrentAddr = addr;
            var header = EditorFormRef.ReadFields(rom, addr, _headerFields);
            PosX = header["B0"];
            PosY = header["B1"];
            Width = header["B2"];
            // byte 3 is height/padding
            Style = header["D4"];

            uint menuPtr = rom.u32(addr + 8);
            CommandPtr = menuPtr;
            uint menuaddr = U.toOffset(menuPtr);

            int count = GetDataLength(rom, menuaddr); // 5 or 8
            StringCount = count;

            uint[] slots = new uint[8]; // default 0; only [0..count-1] are read
            for (int n = 0; n < count; n++)
            {
                uint a = menuaddr + (CommandStride * (uint)n) + 4;
                if (!U.isSafetyOffset(a + 2, rom))
                {
                    // An out-of-bounds slot means the 8-command read can't
                    // complete; fall back to the 5-command view (StringCount
                    // stays one of the two documented values {5, 8}).
                    StringCount = 5;
                    break;
                }
                slots[n] = rom.u16(a);
            }

            String0 = slots[0]; String1 = slots[1]; String2 = slots[2]; String3 = slots[3];
            String4 = slots[4]; String5 = slots[5]; String6 = slots[6]; String7 = slots[7];

            IsLoaded = true;
        }

        /// <summary>
        /// Faithful port of WinForms <c>AllWriteButton_Click</c>. Writes the
        /// header scalar fields (NOT the +8 pointer, NOT the header handler
        /// pointers) and the text ids / menu ids / per-command handler pointers
        /// into the DEREFERENCED command array. Returns false (no mutation)
        /// when the menu region or required ASM patch is missing.
        /// </summary>
        public bool Write()
        {
            ROM rom = CoreState.ROM;
            if (rom?.RomInfo == null || CurrentAddr == 0) return false;
            uint addr = CurrentAddr;

            if (!U.isSafetyOffset(addr + 8 + 4, rom)) return false; // menu region

            uint menuaddr = U.toOffset(rom.u32(addr + 8));
            if (!U.isSafetyOffset(menuaddr, rom)) return false; // command array

            uint prEventMenuCommandEffect = FindEventMenuCommandEffect(rom);
            uint prEventMenuDisplayCommand = FindEventMenuDisplayCommand(rom);
            uint prFindEventMenuDrawCommand = FindEventMenuDrawCommand(rom);
            if (prEventMenuCommandEffect == U.NOT_FOUND) return false; // patch not installed

            uint[] texts = GetDataLength(rom, menuaddr) == 8
                ? new[] { String0, String1, String2, String3, String4, String5, String6, String7 }
                : new[] { String0, String1, String2, String3, String4 };

            // VALIDATE-ALL-BEFORE-MUTATE: every command slot must be in-bounds
            // for all 21 written bytes BEFORE we touch any ROM byte (including
            // the header). A command pointer can pass isSafetyOffset(menuaddr)
            // yet sit too close to EOF for all 5/8 rows — refusing here keeps
            // a rejected Write byte-for-byte no-mutation.
            for (uint i = 0; i < texts.Length; i++)
            {
                uint a = menuaddr + (CommandStride * i);
                if (!U.isSafetyOffset(a + 21, rom)) return false;
            }

            // Header scalar fields only — never +8 / handler pointers.
            var header = new Dictionary<string, uint>
            {
                ["B0"] = PosX, ["B1"] = PosY, ["B2"] = Width, ["D4"] = Style,
            };
            EditorFormRef.WriteFields(rom, addr, header, _headerFields);

            for (uint i = 0; i < texts.Length; i++)
            {
                uint a = menuaddr + (CommandStride * i);
                if (rom.RomInfo.is_multibyte)
                {
                    rom.write_p32(a + 0, 0x1f5310); // null string
                }

                rom.write_u16(a + 4, texts[i]); // text id
                rom.write_u8(a + 9, i);         // MenuID
                if (texts[i] == 0)
                {
                    rom.write_u32(a + 12, 0); // display
                    rom.write_u32(a + 16, 0); // draw
                    rom.write_u32(a + 20, 0); // effect
                    break;
                }
                else
                {
                    rom.write_p32(a + 12, prEventMenuDisplayCommand);  // display
                    rom.write_p32(a + 16, prFindEventMenuDrawCommand); // draw
                    rom.write_p32(a + 20, prEventMenuCommandEffect);   // effect
                }
            }
            return true;
        }

        /// <summary>
        /// Allocate a brand-new split menu (36-byte header + 9 command slots +
        /// 0xFFFFFFFF terminator = <c>36 + 36*9 + 4</c> bytes) in free space,
        /// returning the ROM offset of the new header (or <see cref="U.NOT_FOUND"/>
        /// if no free space or the EventMenuCommand patch is not installed).
        /// Faithful port of WinForms <c>MenuExtendSplitMenuForm.NewAlloc</c>
        /// (#1430). Mutates the ROM under the ambient undo scope; the caller
        /// (AllocIfNeed in the View) decides when to invoke it and writes the
        /// resulting pointer back.
        /// </summary>
        public uint NewAlloc()
        {
            ROM rom = CoreState.ROM;
            if (rom?.RomInfo == null) return U.NOT_FOUND;

            uint prEventMenuCommandEffect = FindEventMenuCommandEffect(rom);
            if (prEventMenuCommandEffect == U.NOT_FOUND) return U.NOT_FOUND; // patch not installed
            uint prEventMenuDisplayCommand = FindEventMenuDisplayCommand(rom);
            uint prEventMenuDrawFunction = FindEventMenuDrawCommand(rom);

            uint allocSize = 36 + (36 * 9) + 4;
            // rom.FindFreeSpace accepts 0x00 OR 0xFF runs (matches WinForms
            // InputFormRef.AppendBinaryData); DataExpansionCore.FindFreeSpace
            // only scans 0xFF runs, which an all-zero region would miss.
            uint addr = rom.FindFreeSpace(0x100, allocSize);
            if (addr == U.NOT_FOUND) return U.NOT_FOUND;

            // ZERO-FILL the whole allocated range first (WinForms writes a
            // zeroed `new byte[allocSize]` via AppendBinaryData). Without this,
            // a 0xFF-filled free region would leave the header handler slots
            // (+12..+32) and unused command bytes as 0xFFFFFFFF — invalid
            // handler pointers. The field writes below then overwrite.
            rom.write_range(addr, new byte[allocSize]);

            rom.write_u8(addr + 0, 6);   // x
            rom.write_u8(addr + 1, 8);   // y
            rom.write_u8(addr + 2, 18);  // width
            rom.write_u8(addr + 3, 0);   // height
            rom.write_u32(addr + 4, 1);  // style
            rom.write_p32(addr + 8, addr + 36); // command definitions pointer

            // 9 command slots; only the first two carry defaults, the rest are 0
            // (WinForms uses an 8-element array but breaks at the first 0 entry,
            //  which is index 2, long before it would overrun).
            uint[] texts = rom.RomInfo.is_multibyte
                ? new uint[] { 0xBD5, 0xBD6, 0, 0, 0, 0, 0, 0, 0 }
                : new uint[] { 0xC15, 0xC16, 0, 0, 0, 0, 0, 0, 0 };

            uint a = addr + 36;
            for (uint i = 0; i < 9; i++, a += 36)
            {
                rom.write_u16(a + 4, texts[i]); // text id
                if (texts[i] == 0)
                {
                    break;
                }
                if (rom.RomInfo.is_multibyte)
                {
                    rom.write_p32(a + 0, 0x1f5310); // null string
                }
                rom.write_u8(a + 9, i);                              // MenuID
                rom.write_p32(a + 12, prEventMenuDisplayCommand);    // display
                rom.write_p32(a + 16, prEventMenuDrawFunction);      // draw
                rom.write_p32(a + 20, prEventMenuCommandEffect);     // effect
            }

            // Terminator after the 9 command slots.
            rom.write_u32(addr + 36 + (36 * 9), 0xffffffff);

            return addr;
        }

        // ===== ported WinForms helpers =====

        /// <summary>
        /// Resolve whether a command array has 5 or 8 editable commands.
        /// Verbatim port of WinForms <c>MenuExtendSplitMenuForm.GetDataLength</c>.
        /// </summary>
        public static int GetDataLength(ROM rom, uint menuaddr)
        {
            uint termData = menuaddr + (36 * 9);
            if (termData == 0 || !U.isSafetyOffset(termData - 1, rom))
            {
                return 5;
            }

            for (uint i = 6; i < 8; i++)
            {
                uint addr = menuaddr + (36 * i);
                if (!U.isSafetyOffset(addr, rom)) return 5;
                if (!U.isSafetyOffset(addr + 36 - 1, rom)) return 5;

                uint no = rom.u8(addr + 9);
                if (no == 0)
                {
                    uint textid = rom.u16(addr + 4);
                    if (textid != 0) return 5;

                    if (rom.u32(addr + 12) != 0) return 5;
                    if (rom.u32(addr + 16) != 0) return 5;
                    if (rom.u32(addr + 20) != 0) return 5;
                }
                else
                {
                    if (no != i) return 5; // unknown data → treat as 5

                    if (!U.isPointerASMOrNull(rom.u32(addr + 12))) return 5;
                    if (!U.isPointerASMOrNull(rom.u32(addr + 16))) return 5;
                    if (!U.isPointerASMOrNull(rom.u32(addr + 20))) return 5;
                }
            }
            return 8;
        }

        // FE8-only ASM handler pointer locators (Debug.Assert version==8 in WF).
        static uint FindEventMenuDisplayCommand(ROM rom)
        {
            return rom.RomInfo.is_multibyte ? 0x0501BC + 1u : 0x04F448 + 1u;
        }

        static uint FindEventMenuDrawCommand(ROM rom)
        {
            return rom.RomInfo.is_multibyte ? 0x0887e0 + 1u : 0u;
        }

        static uint FindEventMenuCommandEffect(ROM rom)
        {
            byte[] need = rom.RomInfo.is_multibyte
                ? new byte[] { 0x00, 0xB5, 0x3C, 0x20, 0x08, 0x5C, 0x03, 0x4B, 0x9E, 0x46, 0x00, 0xF8, 0x17, 0x20, 0x02, 0xBC, 0x08, 0x47, 0x00, 0x00, 0xBC, 0xD4, 0x00, 0x08 }
                : new byte[] { 0x00, 0xB5, 0x3C, 0x20, 0x08, 0x5C, 0x03, 0x4B, 0x9E, 0x46, 0x00, 0xF8, 0x17, 0x20, 0x02, 0xBC, 0x08, 0x47, 0x00, 0x00, 0xF8, 0xD1, 0x00, 0x08 };
            uint p = U.Grep(rom.Data, need, 0x10000, 0, 4);
            if (p == U.NOT_FOUND) return U.NOT_FOUND;
            return p + 1;
        }

        public int GetListCount() => LoadList().Count;

        public Dictionary<string, string> GetDataReport()
        {
            return new Dictionary<string, string>
            {
                ["addr"] = $"0x{CurrentAddr:X08}",
                ["PosX"] = $"0x{PosX:X02}",
                ["PosY"] = $"0x{PosY:X02}",
                ["Width"] = $"0x{Width:X02}",
                ["Style"] = $"0x{Style:X08}",
                ["CommandPtr"] = $"0x{CommandPtr:X08}",
                ["StringCount"] = StringCount.ToString(),
                ["String0"] = $"0x{String0:X04}",
                ["String1"] = $"0x{String1:X04}",
                ["String2"] = $"0x{String2:X04}",
                ["String3"] = $"0x{String3:X04}",
                ["String4"] = $"0x{String4:X04}",
                ["String5"] = $"0x{String5:X04}",
                ["String6"] = $"0x{String6:X04}",
                ["String7"] = $"0x{String7:X04}",
            };
        }

        public Dictionary<string, string> GetRawRomReport()
        {
            ROM rom = CoreState.ROM;
            if (rom == null || CurrentAddr == 0) return new Dictionary<string, string>();
            uint a = CurrentAddr;
            var report = new Dictionary<string, string>
            {
                ["addr"] = $"0x{a:X08}",
                ["u8@0x00_PosX"] = $"0x{rom.u8(a + 0):X02}",
                ["u8@0x01_PosY"] = $"0x{rom.u8(a + 1):X02}",
                ["u8@0x02_Width"] = $"0x{rom.u8(a + 2):X02}",
                ["u32@0x04_Style"] = $"0x{rom.u32(a + 4):X08}",
                ["u32@0x08_CommandPtr"] = $"0x{rom.u32(a + 8):X08}",
                ["u32@0x0C_Handler1"] = $"0x{rom.u32(a + 12):X08}",
                ["u32@0x10_Handler2"] = $"0x{rom.u32(a + 16):X08}",
                ["u32@0x14_Handler3"] = $"0x{rom.u32(a + 20):X08}",
            };
            uint menuaddr = U.toOffset(rom.u32(a + 8));
            for (int n = 0; n < 8; n++)
            {
                uint t = menuaddr + (36u * (uint)n) + 4;
                if (!U.isSafetyOffset(t + 2, rom)) break;
                report[$"u16@cmd{n}+4_TextId"] = $"0x{rom.u16(t):X04}";
            }
            return report;
        }

        public Dictionary<string, string> GetFieldOffsetMap() => new()
        {
            ["PosX"] = "u8@0x00_PosX",
            ["PosY"] = "u8@0x01_PosY",
            ["Width"] = "u8@0x02_Width",
            ["Style"] = "u32@0x04_Style",
            ["CommandPtr"] = "u32@0x08_CommandPtr",
            ["String0"] = "u16@cmd0+4_TextId",
            ["String1"] = "u16@cmd1+4_TextId",
            ["String2"] = "u16@cmd2+4_TextId",
            ["String3"] = "u16@cmd3+4_TextId",
            ["String4"] = "u16@cmd4+4_TextId",
            ["String5"] = "u16@cmd5+4_TextId",
            ["String6"] = "u16@cmd6+4_TextId",
            ["String7"] = "u16@cmd7+4_TextId",
        };
    }
}
