using System;
using System.Collections.Generic;
using System.Text;
using FEBuilderGBA.Avalonia.Services;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    public partial class CCBranchEditorViewModel : ViewModelBase, IDataVerifiable
    {
        static readonly List<EditorFormRef.FieldDef> _fields =
            EditorFormRef.DetectFields(new[] { "B0", "B1" });

        uint _currentAddr;
        string _className = "";
        uint _promotionClass1, _promotionClass2;
        string _promoName1 = "", _promoName2 = "";
        bool _canWrite;
        string _upstreamChain = "";

        public uint CurrentAddr { get => _currentAddr; set => SetField(ref _currentAddr, value); }
        public string ClassName { get => _className; set => SetField(ref _className, value); }
        public uint PromotionClass1 { get => _promotionClass1; set => SetField(ref _promotionClass1, value); }
        public uint PromotionClass2 { get => _promotionClass2; set => SetField(ref _promotionClass2, value); }
        public string PromoName1 { get => _promoName1; set => SetField(ref _promoName1, value); }
        public string PromoName2 { get => _promoName2; set => SetField(ref _promoName2, value); }
        public bool CanWrite { get => _canWrite; set => SetField(ref _canWrite, value); }
        /// <summary>Classes that can promote INTO the currently selected class.</summary>
        public string UpstreamChain { get => _upstreamChain; set => SetField(ref _upstreamChain, value); }

        /// <summary>
        /// Compute the number of CC branch entries to iterate, matching the
        /// WinForms <c>ClassForm.DataCount()</c> semantics: class index 0 is
        /// always counted, scanning stops at the first <c>u8(class_addr+4) == 0</c>
        /// sentinel for <c>i &gt; 0</c>, and we fall back to <c>0x80</c> when
        /// detection yields zero.
        /// </summary>
        static int ComputeClassCount(ROM rom)
        {
            if (rom?.RomInfo == null) return 0x80;
            uint classPtr = rom.RomInfo.class_pointer;
            uint classBase = (classPtr != 0) ? rom.p32(classPtr) : 0;
            uint classDataSize = rom.RomInfo.class_datasize;
            int classCount = 0;
            if (classBase != 0 && U.isSafetyOffset(classBase) && classDataSize > 0)
            {
                for (uint i = 0; i <= 0xFF; i++)
                {
                    uint classAddr = (uint)(classBase + i * classDataSize);
                    if (classAddr + classDataSize > (uint)rom.Data.Length) break;
                    if (i > 0 && rom.u8(classAddr + 4) == 0) break;
                    classCount++;
                }
            }
            if (classCount == 0) classCount = 0x80;
            return classCount;
        }

        public List<AddrResult> LoadCCBranchList()
        {
            ROM rom = CoreState.ROM;
            if (rom?.RomInfo == null) return new List<AddrResult>();

            uint ptr = rom.RomInfo.ccbranch_pointer;
            if (ptr == 0) return new List<AddrResult>();

            uint baseAddr = rom.p32(ptr);
            if (!U.isSafetyOffset(baseAddr)) return new List<AddrResult>();

            uint classPtr = rom.RomInfo.class_pointer;
            uint classBase = (classPtr != 0) ? rom.p32(classPtr) : 0;
            uint classDataSize = rom.RomInfo.class_datasize;
            int classCount = ComputeClassCount(rom);

            var result = new List<AddrResult>();
            for (uint i = 0; i < (uint)classCount; i++)
            {
                uint addr = (uint)(baseAddr + i * 2);
                if (addr + 1 >= (uint)rom.Data.Length) break;

                // Try to get class name
                string className;
                try
                {
                    if (classBase != 0 && classDataSize > 0)
                    {
                        uint classAddr = (uint)(classBase + i * classDataSize);
                        if (classAddr + 2 <= (uint)rom.Data.Length)
                        {
                            uint nameId = rom.u16(classAddr);
                            className = NameResolver.GetTextById(nameId);
                        }
                        else className = "???";
                    }
                    else className = "???";
                }
                catch { className = "???"; }

                string name = U.ToHexString(i) + " " + className;
                result.Add(new AddrResult(addr, name, i));
            }
            return result;
        }

        public void LoadCCBranch(uint addr)
        {
            ROM rom = CoreState.ROM;
            if (rom == null) return;

            if (addr + 1 >= (uint)rom.Data.Length) return;

            IsLoading = true;
            CurrentAddr = addr;
            var v = EditorFormRef.ReadFields(rom, addr, _fields);
            PromotionClass1 = v["B0"];
            PromotionClass2 = v["B1"];
            PromoName1 = NameResolver.GetClassName(PromotionClass1);
            PromoName2 = NameResolver.GetClassName(PromotionClass2);

            // Calculate upstream chain: which classes promote TO the current class index
            UpstreamChain = BuildUpstreamChain(addr);

            CanWrite = true;
            IsLoading = false;
            MarkClean();
        }

        /// <summary>
        /// Build the comma-separated list of classes that promote INTO the
        /// class identified by <paramref name="currentAddr"/>. This mirrors the
        /// WinForms <see cref="CCBranchForm.AddressList_SelectedIndexChanged"/>
        /// logic exactly:
        ///   - scan only the valid class count (NOT the hardcoded 0xFF used previously)
        ///   - class 0 has no upstream (WinForms guards with <c>if (class_id &gt;= 1)</c>)
        ///   - guard against out-of-range / misaligned addresses
        /// Regression: issue #365.
        /// </summary>
        string BuildUpstreamChain(uint currentAddr)
        {
            ROM rom = CoreState.ROM;
            if (rom?.RomInfo == null) return "(none)";

            uint ptr = rom.RomInfo.ccbranch_pointer;
            if (ptr == 0) return "(none)";
            uint baseAddr = rom.p32(ptr);
            if (!U.isSafetyOffset(baseAddr, rom)) return "(none)";

            // Validate the input address before deriving classIndex:
            //  - must be at or past baseAddr (otherwise the unsigned subtraction underflows)
            //  - must be 2-byte aligned to the table
            if (currentAddr < baseAddr) return "(none)";
            uint delta = currentAddr - baseAddr;
            if ((delta & 1) != 0) return "(none)";
            uint classIndex = delta / 2;

            // Class 0 has no upstream — match the WinForms `if (class_id >= 1)` guard.
            if (classIndex == 0) return "(none)";

            int classCount = ComputeClassCount(rom);
            if (classIndex >= (uint)classCount) return "(none)";

            // Scan only the valid class range (NOT 0..0xFE) — replicates WinForms
            // iteration over `list.Count`.
            var sb = new StringBuilder();
            for (uint i = 0; i < (uint)classCount; i++)
            {
                uint addr = baseAddr + i * 2;
                if (addr + 2 > (uint)rom.Data.Length) break;
                uint promo1 = rom.u8(addr + 0);
                uint promo2 = rom.u8(addr + 1);
                if (promo1 == classIndex || promo2 == classIndex)
                {
                    if (sb.Length > 0) sb.Append(", ");
                    sb.Append($"0x{i:X2} {NameResolver.GetClassName(i)}");
                }
            }
            return sb.Length > 0 ? sb.ToString() : "(none)";
        }

        public void WriteCCBranch()
        {
            ROM rom = CoreState.ROM;
            if (rom == null || CurrentAddr == 0) return;

            var values = new Dictionary<string, uint>
            {
                ["B0"] = PromotionClass1, ["B1"] = PromotionClass2,
            };
            EditorFormRef.WriteFields(rom, CurrentAddr, values, _fields);
        }

        public int GetListCount() => LoadCCBranchList().Count;

        public Dictionary<string, string> GetDataReport()
        {
            return new Dictionary<string, string>
            {
                ["addr"] = $"0x{CurrentAddr:X08}",
                ["PromotionClass1"] = $"0x{PromotionClass1:X02}",
                ["PromotionClass2"] = $"0x{PromotionClass2:X02}",
            };
        }

        public Dictionary<string, string> GetRawRomReport()
        {
            ROM rom = CoreState.ROM;
            if (rom == null || CurrentAddr == 0) return new Dictionary<string, string>();
            uint a = CurrentAddr;
            return new Dictionary<string, string>
            {
                ["addr"] = $"0x{a:X08}",
                ["u8@0x00"] = $"0x{rom.u8(a + 0):X02}",
                ["u8@0x01"] = $"0x{rom.u8(a + 1):X02}",
            };
        }

        public Dictionary<string, string> GetFieldOffsetMap()
        {
            return new Dictionary<string, string>
            {
                ["PromotionClass1"] = "u8@0x00",
                ["PromotionClass2"] = "u8@0x01",
            };
        }
    }
}
