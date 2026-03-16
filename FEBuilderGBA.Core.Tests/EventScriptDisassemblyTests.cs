using System;
using Xunit;

namespace FEBuilderGBA.Core.Tests
{
    /// <summary>
    /// Tests for EventScript.DisAseemble integration, validating that
    /// the Core disassembler can be called from ViewModel-like code paths.
    /// </summary>
    [Collection("SharedState")]
    public class EventScriptDisassemblyTests : IDisposable
    {
        readonly IEtcCache _prevComment;
        readonly EventScript _prevEs;
        readonly ROM _prevRom;

        public EventScriptDisassemblyTests()
        {
            _prevComment = CoreState.CommentCache;
            _prevEs = CoreState.EventScript;
            _prevRom = CoreState.ROM;

            if (CoreState.CommentCache == null)
                CoreState.CommentCache = new HeadlessEtcCache();

            // Always set up a minimal ROM so U.isSafetyPointer doesn't NullRef
            // (other SharedState tests may leave CoreState.ROM in a bad state)
            {
                var rom = new ROM();
                rom.SwapNewROMDataDirect(new byte[0x200000]); // 2MB fake ROM
                CoreState.ROM = rom;
            }
        }

        public void Dispose()
        {
            CoreState.CommentCache = _prevComment;
            CoreState.EventScript = _prevEs;
            CoreState.ROM = _prevRom;
        }

        static EventScript BuildEventScript(params EventScript.Script[] scripts)
        {
            var es = new EventScript();
            var prop = typeof(EventScript).GetProperty("Scripts");
            prop!.SetValue(es, scripts);
            return es;
        }

        [Fact]
        public void DisAseemble_WithSyntheticScript_MatchesKnownCommand()
        {
            var script = EventScript.ParseScriptLine("0100XXXX\tLOAD1 [X:UNIT:Units]");
            Assert.NotNull(script);

            var es = BuildEventScript(script);

            byte[] data = new byte[] { 0x01, 0x00, 0x42, 0x00 };

            var code = es.DisAseemble(data, 0);

            Assert.NotNull(code);
            Assert.NotNull(code.Script);
            Assert.NotNull(code.ByteData);
            Assert.Equal(4, code.ByteData.Length);
            Assert.Contains("LOAD1", EventScript.makeCommandComboText(code.Script, false));
        }

        [Fact]
        public void DisAseemble_NoMatch_ReturnsUnknown()
        {
            var script = EventScript.ParseScriptLine("FF000000\tNEVER_MATCH [TERM]");
            Assert.NotNull(script);

            var es = BuildEventScript(script);

            byte[] data = new byte[] { 0x01, 0x02, 0x03, 0x04 };

            var code = es.DisAseemble(data, 0);

            Assert.NotNull(code);
            Assert.NotNull(code.Script);
            Assert.Equal(EventScript.ScriptHas.UNKNOWN, code.Script.Has);
        }

        [Fact]
        public void DisAseemble_WithPointerArg_ExtractsValue()
        {
            var script = EventScript.ParseScriptLine("03000000XXXXXXXX\tCALL [X:POINTER_EVENT:Target]");
            Assert.NotNull(script);

            var es = BuildEventScript(script);

            // Pointer 0x08001000 — within our 2MB ROM bounds
            byte[] data = new byte[] { 0x03, 0x00, 0x00, 0x00, 0x00, 0x10, 0x00, 0x08 };

            var code = es.DisAseemble(data, 0);

            Assert.NotNull(code);
            Assert.Contains("CALL", EventScript.makeCommandComboText(code.Script, false));

            var arg = code.Script.Args[1]; // variable arg after FIXED
            uint val = EventScript.GetArgValue(code, arg);
            Assert.Equal(0x08001000u, val);
        }

        [Fact]
        public void DisAseemble_MultipleCommands_CanIterateSequentially()
        {
            var script1 = EventScript.ParseScriptLine("0100XXXX\tLOAD [X:UNIT:Unit]");
            var script2 = EventScript.ParseScriptLine("0A000000\tENDA [TERM]");
            Assert.NotNull(script1);
            Assert.NotNull(script2);

            var es = BuildEventScript(script1, script2);

            byte[] data = new byte[] {
                0x01, 0x00, 0x42, 0x00,
                0x0A, 0x00, 0x00, 0x00
            };

            var code1 = es.DisAseemble(data, 0);
            Assert.NotNull(code1);
            Assert.Contains("LOAD", EventScript.makeCommandComboText(code1.Script, false));

            var code2 = es.DisAseemble(data, 4);
            Assert.NotNull(code2);
            Assert.Contains("ENDA", EventScript.makeCommandComboText(code2.Script, false));
            Assert.Equal(EventScript.ScriptHas.TERM, code2.Script.Has);
        }

        [Fact]
        public void MakeCommandComboText_IncludesCommandName()
        {
            var script = EventScript.ParseScriptLine("0100XXXX\tMOVE [X:UNIT:Unit]");
            Assert.NotNull(script);

            string text = EventScript.makeCommandComboText(script, false);
            Assert.Contains("MOVE", text);

            string textBin = EventScript.makeCommandComboText(script, true);
            Assert.Contains("MOVE", textBin);
        }
    }
}
