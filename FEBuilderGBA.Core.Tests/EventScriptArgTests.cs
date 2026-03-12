using System;
using System.Collections.Generic;
using Xunit;

namespace FEBuilderGBA.Core.Tests
{
    /// <summary>
    /// Tests for EventScript argument parsing and value extraction,
    /// which underpin the Avalonia EventScriptPopup parameter editor.
    /// </summary>
    public class EventScriptArgTests
    {
        /// <summary>Find the first non-FIXED arg with the given symbol.</summary>
        static EventScript.Arg FindVariableArg(EventScript.Script script, char symbol)
        {
            foreach (var a in script.Args)
            {
                if (a.Type != EventScript.ArgType.FIXED && a.Symbol == symbol)
                    return a;
            }
            return null;
        }

        [Fact]
        public void ParseScriptLine_BasicCommand_ParsesArgsCorrectly()
        {
            // A simple 4-byte command: 2 fixed bytes + 2 variable bytes
            // "0100XXXX" means byte[0]=0x01, byte[1]=0x00, bytes 2-3 are variable 'X'
            // Info: "TEST_CMD [X:UNIT:Unit]"
            string line = "0100XXXX\tTEST_CMD [X:UNIT:Unit]";
            var script = EventScript.ParseScriptLine(line);

            Assert.NotNull(script);
            Assert.Equal(4, script.Size);
            Assert.Equal(2, script.Args.Length); // FIXED + variable

            // First arg: FIXED at position 0, size 2
            Assert.Equal(EventScript.ArgType.FIXED, script.Args[0].Type);
            Assert.Equal(0, script.Args[0].Position);
            Assert.Equal(2, script.Args[0].Size);

            // Second arg: UNIT at position 2, size 2
            Assert.Equal(EventScript.ArgType.UNIT, script.Args[1].Type);
            Assert.Equal(2, script.Args[1].Position);
            Assert.Equal(2, script.Args[1].Size);
            Assert.Equal("Unit", script.Args[1].Name);
        }

        [Fact]
        public void ParseScriptLine_MultipleVariables_ParsesDistinctArgs()
        {
            // 8-byte command: 2 fixed + XX=MAPX(2B) + YY=MAPY(2B), total 8 bytes = 16 hex chars
            string line = "02000000XXXXYYYY\tMOVE [X:MAPX:X][Y:MAPY:Y]";
            var script = EventScript.ParseScriptLine(line);

            Assert.NotNull(script);
            Assert.Equal(8, script.Size);
            // Args: FIXED(0,4), X(4,2), Y(6,2)
            Assert.Equal(3, script.Args.Length);

            Assert.Equal(EventScript.ArgType.FIXED, script.Args[0].Type);
            Assert.Equal(EventScript.ArgType.MAPX, script.Args[1].Type);
            Assert.Equal("X", script.Args[1].Name);
            Assert.Equal(EventScript.ArgType.MAPY, script.Args[2].Type);
            Assert.Equal("Y", script.Args[2].Name);
        }

        [Fact]
        public void GetArgValue_1Byte_ReadsCorrectly()
        {
            // Must be 8 hex chars (4 bytes) minimum; "01XX0000" = 4 bytes
            var script = EventScript.ParseScriptLine("01XX0000\tCMD [X:UNIT:Unit]");
            Assert.NotNull(script);

            var code = new EventScript.OneCode
            {
                ByteData = new byte[] { 0x01, 0x42, 0x00, 0x00 },
                Script = script,
            };

            // The variable arg 'X' is at position 1, size 1
            var arg = FindVariableArg(script, 'X');
            Assert.NotNull(arg);
            uint val = EventScript.GetArgValue(code, arg);
            Assert.Equal(0x42u, val);
        }

        [Fact]
        public void GetArgValue_2Bytes_ReadsLittleEndian()
        {
            var script = EventScript.ParseScriptLine("0100XXXX\tCMD [X:ITEM:Item]");
            Assert.NotNull(script);

            var code = new EventScript.OneCode
            {
                ByteData = new byte[] { 0x01, 0x00, 0x34, 0x12 },
                Script = script,
            };

            var arg = script.Args[1]; // variable arg
            uint val = EventScript.GetArgValue(code, arg);
            Assert.Equal(0x1234u, val);
        }

        [Fact]
        public void GetArgValue_4Bytes_ReadsPointer()
        {
            // 8 bytes = 16 hex chars: 4 fixed + 4 variable
            var script = EventScript.ParseScriptLine("03000000XXXXXXXX\tCALL [X:POINTER_EVENT:Target]");
            Assert.NotNull(script);

            var code = new EventScript.OneCode
            {
                ByteData = new byte[] { 0x03, 0x00, 0x00, 0x00, 0x78, 0x56, 0x34, 0x08 },
                Script = script,
            };

            var arg = FindVariableArg(script, 'X');
            Assert.NotNull(arg);
            uint val = EventScript.GetArgValue(code, arg);
            Assert.Equal(0x08345678u, val);
        }

        [Fact]
        public void IsDecimal_MapCoordinates_ReturnsTrue()
        {
            Assert.True(EventScript.IsDecimal(EventScript.ArgType.MAPX));
            Assert.True(EventScript.IsDecimal(EventScript.ArgType.MAPY));
            Assert.True(EventScript.IsDecimal(EventScript.ArgType.DECIMAL));
        }

        [Fact]
        public void IsDecimal_UnitClassItem_ReturnsFalse()
        {
            Assert.False(EventScript.IsDecimal(EventScript.ArgType.UNIT));
            Assert.False(EventScript.IsDecimal(EventScript.ArgType.CLASS));
            Assert.False(EventScript.IsDecimal(EventScript.ArgType.ITEM));
        }

        [Fact]
        public void IsPointerArgs_PointerTypes_ReturnsTrue()
        {
            Assert.True(EventScript.IsPointerArgs(EventScript.ArgType.POINTER));
            Assert.True(EventScript.IsPointerArgs(EventScript.ArgType.POINTER_EVENT));
            Assert.True(EventScript.IsPointerArgs(EventScript.ArgType.POINTER_ASM));
            Assert.True(EventScript.IsPointerArgs(EventScript.ArgType.POINTER_UNIT));
        }

        [Fact]
        public void IsPointerArgs_NonPointerTypes_ReturnsFalse()
        {
            Assert.False(EventScript.IsPointerArgs(EventScript.ArgType.UNIT));
            Assert.False(EventScript.IsPointerArgs(EventScript.ArgType.MAPX));
            Assert.False(EventScript.IsPointerArgs(EventScript.ArgType.TEXT));
            Assert.False(EventScript.IsPointerArgs(EventScript.ArgType.FIXED));
        }

        [Fact]
        public void MakeCommandComboText_ReturnsCommandName()
        {
            var script = EventScript.ParseScriptLine("0100XXXX\tLOAD1 [X:POINTER_UNIT:Units]");
            Assert.NotNull(script);

            string text = EventScript.makeCommandComboText(script, false);
            Assert.Contains("LOAD1", text);
        }

        [Fact]
        public void ParseScriptLine_AllFixedBytes_HasOnlyFixedArg()
        {
            // A command that is entirely fixed (e.g. ENDA = 0x0A000000)
            string line = "0A000000\tENDA [TERM]";
            var script = EventScript.ParseScriptLine(line);

            Assert.NotNull(script);
            Assert.Equal(4, script.Size);
            // All bytes are fixed constants
            Assert.Single(script.Args);
            Assert.Equal(EventScript.ArgType.FIXED, script.Args[0].Type);
            Assert.Equal(4, script.Args[0].Size);
        }

        [Fact]
        public void GetArg_DecimalType_ReturnsDecimalString()
        {
            var script = EventScript.ParseScriptLine("0100XXXX\tCAMERA [X:MAPX:X]");
            Assert.NotNull(script);

            var code = new EventScript.OneCode
            {
                ByteData = new byte[] { 0x01, 0x00, 0x0A, 0x00 },
                Script = script,
            };

            string result = EventScript.GetArg(code, 1, out uint v);
            Assert.Equal(10u, v);
            Assert.Equal("10", result); // Decimal representation for MAPX
        }

        [Fact]
        public void GetArg_HexType_ReturnsHexString()
        {
            var script = EventScript.ParseScriptLine("0100XXXX\tCMD [X:UNIT:Unit]");
            Assert.NotNull(script);

            var code = new EventScript.OneCode
            {
                ByteData = new byte[] { 0x01, 0x00, 0x2A, 0x00 },
                Script = script,
            };

            string result = EventScript.GetArg(code, 1, out uint v);
            Assert.Equal(0x002Au, v);
            Assert.Equal("0x2A", result); // Hex representation for UNIT
        }
    }
}
