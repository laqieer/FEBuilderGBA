using System;
using System.Collections.Generic;

namespace FEBuilderGBA
{
    public static class EventScriptUtil
    {
        public static void JisageReorder(List<EventScript.OneCode> eventAsm)
        {
            List<uint> needLabel = new List<uint>();
            uint jisageCount = 0;
            bool isBeforeGoto = false;
            for (int i = 0; i < eventAsm.Count; i++)
            {
                EventScript.OneCode code = eventAsm[i];
                if (code.Script.Has == EventScript.ScriptHas.LABEL_CONDITIONAL)
                {
                    if (jisageCount > 0)
                    {
                        uint cond_id = GetScriptLabelID(code);

                        needLabel.RemoveAll((uint x) => { return x == cond_id; });
                        if (needLabel.Count == 0)
                        {//必要なラベルをすべて探索し終わった。おそらく複雑な字下げが行われているのだろう.
                            jisageCount = 0;
                        }
                        else
                        {
                            jisageCount--;
                        }
                        code.JisageCount = jisageCount;
                    }
                    if (needLabel.Count != 0 && isBeforeGoto)
                    {//まだ探索しなければいけないラベルがあり、直前にgotoがあった場合
                        //おそらくこれはelse区です
                        jisageCount++;
                    }
                    isBeforeGoto = false;
                }
                else if (code.Script.Has == EventScript.ScriptHas.IF_CONDITIONAL)
                {
                    code.JisageCount = jisageCount++;
                    uint conditional_id = GetScriptConditionalID(code);
                    if (conditional_id != U.NOT_FOUND)
                    {
                        needLabel.Add(conditional_id);
                    }
                    isBeforeGoto = false;
                }
                else if (code.Script.Has == EventScript.ScriptHas.GOTO_CONDITIONAL)
                {
                    code.JisageCount = jisageCount;
                    uint conditional_id = GetScriptConditionalID(code);
                    if (conditional_id != U.NOT_FOUND)
                    {
                        needLabel.Add(conditional_id);
                    }
                    isBeforeGoto = true;
                }
                else
                {
                    code.JisageCount = jisageCount;
                    isBeforeGoto = false;
                }
            }
        }
        public static uint GetScriptConditionalID(EventScript.OneCode code)
        {
            for (int i = 0; i < code.Script.Args.Length; i++)
            {
                EventScript.Arg arg = code.Script.Args[i];
                if (arg.Type == EventScript.ArgType.IF_CONDITIONAL
                    || arg.Type == EventScript.ArgType.GOTO_CONDITIONAL)
                {
                    uint v = EventScript.GetArgValue(code, arg);
                    return v;
                }
            }
            return U.NOT_FOUND;
        }
        public static uint GetScriptLabelID(EventScript.OneCode code)
        {
            for (int i = 0; i < code.Script.Args.Length; i++)
            {
                EventScript.Arg arg = code.Script.Args[i];
                if (arg.Type == EventScript.ArgType.LABEL_CONDITIONAL)
                {
                    uint v = EventScript.GetArgValue(code, arg);
                    return v;
                }
            }
            return U.NOT_FOUND;
        }

        public static uint GetScriptSomeLabel(EventScript.OneCode code)
        {
            if (code.Script.Has == EventScript.ScriptHas.LABEL_CONDITIONAL
             || code.Script.Has == EventScript.ScriptHas.IF_CONDITIONAL
             || code.Script.Has == EventScript.ScriptHas.GOTO_CONDITIONAL
                )
            {
                for (int i = 0; i < code.Script.Args.Length; i++)
                {
                    EventScript.Arg arg = code.Script.Args[i];
                    if (arg.Type == EventScript.ArgType.LABEL_CONDITIONAL
                        || arg.Type == EventScript.ArgType.IF_CONDITIONAL
                        || arg.Type == EventScript.ArgType.GOTO_CONDITIONAL
                        )
                    {
                        uint v = EventScript.GetArgValue(code, arg);
                        return v;
                    }
                }
            }
            return U.NOT_FOUND;
        }
    }
}
