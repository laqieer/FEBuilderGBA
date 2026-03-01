using System;
using System.Collections.Generic;
using Xunit;

namespace FEBuilderGBA.Tests.Unit
{
    /// <summary>
    /// Tests for EventScriptUtil pure methods (Core migration batch 12).
    /// </summary>
    public class EventScriptUtilTests
    {
        /// <summary>
        /// Helper to create a minimal OneCode with specified ScriptHas and args.
        /// </summary>
        static EventScript.OneCode MakeOneCode(
            EventScript.ScriptHas has,
            EventScript.ArgType? argType = null,
            uint argValue = 0,
            int argPosition = 0,
            int argSize = 2)
        {
            var code = new EventScript.OneCode();
            var script = new EventScript.Script();
            script.Has = has;

            if (argType.HasValue)
            {
                var arg = new EventScript.Arg();
                arg.Type = argType.Value;
                arg.Position = argPosition;
                arg.Size = argSize;
                script.Args = new EventScript.Arg[] { arg };

                // OneCode.ByteData must have enough bytes to read the arg value
                code.ByteData = new byte[argPosition + argSize];
                code.ByteData[argPosition] = (byte)(argValue & 0xFF);
                if (argSize >= 2)
                    code.ByteData[argPosition + 1] = (byte)((argValue >> 8) & 0xFF);
            }
            else
            {
                script.Args = new EventScript.Arg[0];
                code.ByteData = new byte[4];
            }

            code.Script = script;
            return code;
        }

        [Fact]
        public void GetScriptLabelID_WithLabelArg_ReturnsValue()
        {
            var code = MakeOneCode(EventScript.ScriptHas.LABEL_CONDITIONAL,
                EventScript.ArgType.LABEL_CONDITIONAL, argValue: 42);
            uint result = EventScriptUtil.GetScriptLabelID(code);
            Assert.Equal(42u, result);
        }

        [Fact]
        public void GetScriptLabelID_WithNoLabelArg_ReturnsNotFound()
        {
            var code = MakeOneCode(EventScript.ScriptHas.NOTHING);
            uint result = EventScriptUtil.GetScriptLabelID(code);
            Assert.Equal(U.NOT_FOUND, result);
        }

        [Fact]
        public void GetScriptConditionalID_WithIfArg_ReturnsValue()
        {
            var code = MakeOneCode(EventScript.ScriptHas.IF_CONDITIONAL,
                EventScript.ArgType.IF_CONDITIONAL, argValue: 99);
            uint result = EventScriptUtil.GetScriptConditionalID(code);
            Assert.Equal(99u, result);
        }

        [Fact]
        public void GetScriptConditionalID_WithGotoArg_ReturnsValue()
        {
            var code = MakeOneCode(EventScript.ScriptHas.GOTO_CONDITIONAL,
                EventScript.ArgType.GOTO_CONDITIONAL, argValue: 7);
            uint result = EventScriptUtil.GetScriptConditionalID(code);
            Assert.Equal(7u, result);
        }

        [Fact]
        public void GetScriptConditionalID_NoMatchingArg_ReturnsNotFound()
        {
            var code = MakeOneCode(EventScript.ScriptHas.NOTHING);
            uint result = EventScriptUtil.GetScriptConditionalID(code);
            Assert.Equal(U.NOT_FOUND, result);
        }

        [Fact]
        public void GetScriptSomeLabel_LabelConditional_ReturnsLabelID()
        {
            var code = MakeOneCode(EventScript.ScriptHas.LABEL_CONDITIONAL,
                EventScript.ArgType.LABEL_CONDITIONAL, argValue: 10);
            uint result = EventScriptUtil.GetScriptSomeLabel(code);
            Assert.Equal(10u, result);
        }

        [Fact]
        public void GetScriptSomeLabel_IfConditional_ReturnsConditionalID()
        {
            var code = MakeOneCode(EventScript.ScriptHas.IF_CONDITIONAL,
                EventScript.ArgType.IF_CONDITIONAL, argValue: 20);
            uint result = EventScriptUtil.GetScriptSomeLabel(code);
            Assert.Equal(20u, result);
        }

        [Fact]
        public void GetScriptSomeLabel_NonConditional_ReturnsNotFound()
        {
            var code = MakeOneCode(EventScript.ScriptHas.NOTHING);
            uint result = EventScriptUtil.GetScriptSomeLabel(code);
            Assert.Equal(U.NOT_FOUND, result);
        }

        [Fact]
        public void JisageReorder_EmptyList_DoesNotThrow()
        {
            var list = new List<EventScript.OneCode>();
            var ex = Record.Exception(() => EventScriptUtil.JisageReorder(list));
            Assert.Null(ex);
        }

        [Fact]
        public void JisageReorder_SingleNonConditional_SetsJisageZero()
        {
            var list = new List<EventScript.OneCode>
            {
                MakeOneCode(EventScript.ScriptHas.NOTHING)
            };
            EventScriptUtil.JisageReorder(list);
            Assert.Equal(0u, list[0].JisageCount);
        }

        [Fact]
        public void JisageReorder_IfThenLabel_SetsCorrectIndentation()
        {
            // IF (conditional id=5) -> body -> LABEL (id=5)
            var ifCode = MakeOneCode(EventScript.ScriptHas.IF_CONDITIONAL,
                EventScript.ArgType.IF_CONDITIONAL, argValue: 5);
            var bodyCode = MakeOneCode(EventScript.ScriptHas.NOTHING);
            var labelCode = MakeOneCode(EventScript.ScriptHas.LABEL_CONDITIONAL,
                EventScript.ArgType.LABEL_CONDITIONAL, argValue: 5);

            var list = new List<EventScript.OneCode> { ifCode, bodyCode, labelCode };
            EventScriptUtil.JisageReorder(list);

            Assert.Equal(0u, ifCode.JisageCount);   // IF itself at indent 0
            Assert.Equal(1u, bodyCode.JisageCount);  // body indented
            Assert.Equal(0u, labelCode.JisageCount); // label back to 0
        }
    }
}
