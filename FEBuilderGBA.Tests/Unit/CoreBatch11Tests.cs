using System;
using System.Collections.Generic;
using Xunit;

namespace FEBuilderGBA.Tests.Unit
{
    /// <summary>
    /// Tests for Phase 0.5 Core migration (batch 11):
    /// Address, RecycleAddress, MagicSplitUtil, SymbolUtil,
    /// EventScript, EtcCache, SystemTextEncoder, GrowSimulator.
    /// </summary>
    [Collection("SharedState")]
    public class CoreBatch11Tests
    {
        private void EnsureMinimalROM()
        {
            if (CoreState.ROM == null)
            {
                // Create a minimal 1MB ROM so address safety checks pass
                var rom = new ROM();
                rom.SwapNewROMDataDirect(new byte[0x100000]);
                CoreState.ROM = rom;
            }
        }

        // ---- Address (WU1) ----

        [Fact]
        public void Address_DataTypeEnum_HasExpectedValues()
        {
            Assert.Equal(0, (int)Address.DataTypeEnum.MIX);
            Assert.Equal(1, (int)Address.DataTypeEnum.BIN);
            Assert.True(Enum.IsDefined(typeof(Address.DataTypeEnum), "Comment"));
            Assert.True(Enum.IsDefined(typeof(Address.DataTypeEnum), "EVENTSCRIPT"));
            Assert.True(Enum.IsDefined(typeof(Address.DataTypeEnum), "ASM"));
            Assert.True(Enum.IsDefined(typeof(Address.DataTypeEnum), "PROCS"));
            Assert.True(Enum.IsDefined(typeof(Address.DataTypeEnum), "AISCRIPT"));
        }

        [Fact]
        public void Address_Constructor_SetsProperties()
        {
            EnsureMinimalROM();
            var addr = new Address(0x1000, 0x20, U.NOT_FOUND, "test info", Address.DataTypeEnum.BIN);
            Assert.Equal(0x1000u, addr.Addr);
            Assert.Equal(0x20u, addr.Length);
            Assert.Equal("test info", addr.Info);
            Assert.Equal(U.NOT_FOUND, addr.Pointer);
            Assert.Equal(Address.DataTypeEnum.BIN, addr.DataType);
            Assert.Equal(0u, addr.BlockSize);
            Assert.Null(addr.PointerIndexes);
        }

        [Fact]
        public void Address_Constructor_WithBlockSizeAndPointerIndexes()
        {
            EnsureMinimalROM();
            var indexes = new uint[] { 4, 8, 12 };
            var addr = new Address(0x2000, 0x40, 0x3000, "IFR data", Address.DataTypeEnum.InputFormRef, 16, indexes);
            Assert.Equal(0x2000u, addr.Addr);
            Assert.Equal(0x40u, addr.Length);
            Assert.Equal(0x3000u, addr.Pointer);
            Assert.Equal(Address.DataTypeEnum.InputFormRef, addr.DataType);
            Assert.Equal(16u, addr.BlockSize);
            Assert.Equal(indexes, addr.PointerIndexes);
        }

        [Fact]
        public void Address_ResizeAddress_UpdatesAddrAndLength()
        {
            EnsureMinimalROM();
            var addr = new Address(0x1000, 0x20, U.NOT_FOUND, "test", Address.DataTypeEnum.BIN);
            addr.ResizeAddress(0x2000, 0x50);
            Assert.Equal(0x2000u, addr.Addr);
            Assert.Equal(0x50u, addr.Length);
        }

        [Fact]
        public void Address_IsLZ77_ReturnsTrueForLZ77Types()
        {
            Assert.True(Address.IsLZ77(Address.DataTypeEnum.LZ77IMG));
            Assert.True(Address.IsLZ77(Address.DataTypeEnum.LZ77TSA));
            Assert.True(Address.IsLZ77(Address.DataTypeEnum.LZ77PAL));
            Assert.True(Address.IsLZ77(Address.DataTypeEnum.BATTLEFRAME));
            Assert.False(Address.IsLZ77(Address.DataTypeEnum.BIN));
            Assert.False(Address.IsLZ77(Address.DataTypeEnum.ASM));
        }

        [Fact]
        public void Address_IsASMOnly_ReturnsTrueForASMTypes()
        {
            Assert.True(Address.IsASMOnly(Address.DataTypeEnum.ASM));
            Assert.True(Address.IsASMOnly(Address.DataTypeEnum.PATCH_ASM));
            Assert.True(Address.IsASMOnly(Address.DataTypeEnum.BL_ASM));
            Assert.False(Address.IsASMOnly(Address.DataTypeEnum.BIN));
        }

        [Fact]
        public void Address_IsIFR_ReturnsTrueForIFRTypes()
        {
            Assert.True(Address.IsIFR(Address.DataTypeEnum.InputFormRef));
            Assert.True(Address.IsIFR(Address.DataTypeEnum.InputFormRef_ASM));
            Assert.True(Address.IsIFR(Address.DataTypeEnum.InputFormRef_MIX));
            Assert.True(Address.IsIFR(Address.DataTypeEnum.InputFormRef_1));
            Assert.False(Address.IsIFR(Address.DataTypeEnum.BIN));
        }

        [Fact]
        public void Address_IsBINType_ReturnsTrueForBINTypes()
        {
            Assert.True(Address.IsBINType(Address.DataTypeEnum.BIN));
            Assert.True(Address.IsBINType(Address.DataTypeEnum.IMG));
            Assert.True(Address.IsBINType(Address.DataTypeEnum.PAL));
            Assert.True(Address.IsBINType(Address.DataTypeEnum.TSA));
            Assert.False(Address.IsBINType(Address.DataTypeEnum.ASM));
        }

        [Fact]
        public void Address_IsPointerableType_ReturnsTrueForPointerableTypes()
        {
            Assert.True(Address.IsPointerableType(Address.DataTypeEnum.EVENTSCRIPT));
            Assert.True(Address.IsPointerableType(Address.DataTypeEnum.ASM));
            Assert.True(Address.IsPointerableType(Address.DataTypeEnum.InputFormRef));
            Assert.False(Address.IsPointerableType(Address.DataTypeEnum.BIN));
            Assert.False(Address.IsPointerableType(Address.DataTypeEnum.IMG));
        }

        [Fact]
        public void Address_IsFFor00_ReturnsTrueForFFor00()
        {
            Assert.True(Address.IsFFor00(Address.DataTypeEnum.FFor00));
            Assert.False(Address.IsFFor00(Address.DataTypeEnum.BIN));
        }

        [Fact]
        public void Address_AddCommentData_AddsToList()
        {
            EnsureMinimalROM();
            var list = new List<Address>();
            Address.AddCommentData(list, 0x1000, "comment");
            Assert.Single(list);
            Assert.Equal(Address.DataTypeEnum.Comment, list[0].DataType);
            Assert.Equal(0u, list[0].Length);
            Assert.Equal("comment", list[0].Info);
        }

        [Fact]
        public void Address_AddAddress_ValidData()
        {
            EnsureMinimalROM();
            var list = new List<Address>();
            Address.AddAddress(list, 0x1000, 0x20, U.NOT_FOUND, "data", Address.DataTypeEnum.BIN);
            Assert.Single(list);
            Assert.Equal(0x1000u, list[0].Addr);
            Assert.Equal(0x20u, list[0].Length);
        }

        [Fact]
        public void Address_AddAddress_InvalidAddr_SkipsAdd()
        {
            EnsureMinimalROM();
            var list = new List<Address>();
            Address.AddAddress(list, U.NOT_FOUND, 0x20, U.NOT_FOUND, "data", Address.DataTypeEnum.BIN);
            Assert.Empty(list);
        }

        // ---- RecycleAddress (WU1) ----

        [Fact]
        public void RecycleAddress_Constructor_CreatesEmptyList()
        {
            var ra = new RecycleAddress();
            Assert.False(ra.AlreadyRecycled(0x1000));
        }

        [Fact]
        public void RecycleAddress_AlreadyRecycled_FindsExisting()
        {
            EnsureMinimalROM();
            var list = new List<Address>();
            list.Add(new Address(0x1000, 0x20, U.NOT_FOUND, "test", Address.DataTypeEnum.BIN));
            var ra = new RecycleAddress(list);
            Assert.True(ra.AlreadyRecycled(0x1000));
            Assert.False(ra.AlreadyRecycled(0x2000));
        }

        // ---- MagicSplitUtil (WU2) ----

        [Fact]
        public void MagicSplitUtil_Enum_HasExpectedValues()
        {
            Assert.Equal(0, (int)MagicSplitUtil.magic_split_enum.NO);
            Assert.Equal(1, (int)MagicSplitUtil.magic_split_enum.FE8NMAGIC);
            Assert.Equal(2, (int)MagicSplitUtil.magic_split_enum.FE7UMAGIC);
            Assert.Equal(3, (int)MagicSplitUtil.magic_split_enum.FE8UMAGIC);
            Assert.Equal(0xFF, (int)MagicSplitUtil.magic_split_enum.NoCache);
        }

        [Fact]
        public void MagicSplitUtil_ClearCache_DoesNotThrow()
        {
            MagicSplitUtil.ClearCache();
        }

        // ---- SymbolUtil (WU3) ----

        [Fact]
        public void SymbolUtil_DebugSymbol_HasExpectedValues()
        {
            Assert.Equal(0, (int)SymbolUtil.DebugSymbol.None);
            Assert.Equal(1, (int)SymbolUtil.DebugSymbol.SaveSymTxt);
            Assert.Equal(2, (int)SymbolUtil.DebugSymbol.SaveComment);
            Assert.Equal(3, (int)SymbolUtil.DebugSymbol.SaveBoth);
        }

        [Fact]
        public void SymbolUtil_ProcessSymbolToList_EmptySymbol_ProducesEmptyList()
        {
            var list = new List<Address>();
            SymbolUtil.ProcessSymbolToList(list, "test.elf", "", 0);
            Assert.Empty(list);
        }

        // ---- EventScript (WU4) ----

        [Fact]
        public void EventScript_ArgType_HasManyValues()
        {
            var values = Enum.GetValues(typeof(EventScript.ArgType));
            Assert.True(values.Length > 100, $"Expected >100 ArgType values, got {values.Length}");
        }

        [Fact]
        public void EventScript_ArgType_HasKeyTypes()
        {
            Assert.True(Enum.IsDefined(typeof(EventScript.ArgType), "None"));
            Assert.True(Enum.IsDefined(typeof(EventScript.ArgType), "UNIT"));
            Assert.True(Enum.IsDefined(typeof(EventScript.ArgType), "CLASS"));
            Assert.True(Enum.IsDefined(typeof(EventScript.ArgType), "ITEM"));
            Assert.True(Enum.IsDefined(typeof(EventScript.ArgType), "TEXT"));
            Assert.True(Enum.IsDefined(typeof(EventScript.ArgType), "POINTER_EVENT"));
            Assert.True(Enum.IsDefined(typeof(EventScript.ArgType), "MAPX"));
            Assert.True(Enum.IsDefined(typeof(EventScript.ArgType), "MAPY"));
        }

        [Fact]
        public void EventScript_ScriptHas_HasExpectedValues()
        {
            Assert.True(Enum.IsDefined(typeof(EventScript.ScriptHas), "NOTHING"));
            Assert.True(Enum.IsDefined(typeof(EventScript.ScriptHas), "UNKNOWN"));
            Assert.True(Enum.IsDefined(typeof(EventScript.ScriptHas), "TEXT"));
        }

        [Fact]
        public void EventScript_EventScriptType_HasThreeValues()
        {
            Assert.True(Enum.IsDefined(typeof(EventScript.EventScriptType), "Event"));
            Assert.True(Enum.IsDefined(typeof(EventScript.EventScriptType), "Procs"));
            Assert.True(Enum.IsDefined(typeof(EventScript.EventScriptType), "AI"));
        }

        [Fact]
        public void EventScript_OneCode_CanBeCreated()
        {
            var code = new EventScript.OneCode();
            Assert.Null(code.ByteData);
            Assert.Null(code.Script);
            Assert.Null(code.Comment);
            Assert.Equal(0u, code.JisageCount);
        }

        // ---- EtcCache (WU5) ----

        [Fact]
        public void EtcCache_ImplementsIEtcCache()
        {
            Assert.True(typeof(IEtcCache).IsAssignableFrom(typeof(EtcCache)));
        }

        // ---- SystemTextEncoder (WU6) ----

        [Fact]
        public void TextEncodingEnum_HasExpectedValues()
        {
            Assert.Equal(0, (int)TextEncodingEnum.Auto);
            Assert.Equal(1, (int)TextEncodingEnum.LAT1);
            Assert.Equal(2, (int)TextEncodingEnum.Shift_JIS);
            Assert.Equal(3, (int)TextEncodingEnum.UTF8);
            Assert.Equal(4, (int)TextEncodingEnum.ZH_TBL);
            Assert.Equal(5, (int)TextEncodingEnum.EN_TBL);
            Assert.Equal(6, (int)TextEncodingEnum.AR_TBL);
            Assert.Equal(7, (int)TextEncodingEnum.KR_TBL);
            Assert.Equal(8, (int)TextEncodingEnum.KO_TBL);
            Assert.Equal(99, (int)TextEncodingEnum.NoCache);
        }

        [Fact]
        public void SystemTextEncoder_ImplementsISystemTextEncoder()
        {
            Assert.True(typeof(ISystemTextEncoder).IsAssignableFrom(typeof(SystemTextEncoder)));
        }

        // ---- GrowSimulator (WU7) ----

        [Fact]
        public void GrowSimulator_DefaultProperties_AreZero()
        {
            var gs = new GrowSimulator();
            Assert.Equal(0, gs.unit_lv);
            Assert.Equal(0, gs.unit_hp);
            Assert.Equal(0, gs.unit_str);
            Assert.Equal(0, gs.unit_skill);
            Assert.Equal(0, gs.unit_spd);
            Assert.Equal(0, gs.unit_def);
            Assert.Equal(0, gs.unit_res);
            Assert.Equal(0, gs.unit_luck);
        }

        // ---- CoreState callbacks (Phase 0.5) ----

        [Fact]
        public void CoreState_TextEncoding_DefaultIsAuto()
        {
            Assert.Equal(TextEncodingEnum.Auto, CoreState.TextEncoding);
        }

        [Fact]
        public void CoreState_CallbackProperties_AreNullByDefault()
        {
            Assert.Null(CoreState.GetLevelMaxCaps);
            Assert.Null(CoreState.IsHighClass);
            Assert.Null(CoreState.EventScriptPatchLoader);
        }

        [Fact]
        public void CoreState_HasCoreTypeProperties()
        {
            // Verify these properties exist (may be null at test time)
            var _ = CoreState.EventScript;
            var __ = CoreState.ProcsScript;
            var ___ = CoreState.AIScript;
            var ____ = CoreState.FlagCache;
            var _____ = CoreState.ExportFunction;
        }
    }
}
