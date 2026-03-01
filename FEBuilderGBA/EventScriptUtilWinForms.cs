using System.Collections.Generic;

namespace FEBuilderGBA
{
    /// <summary>
    /// WinForms-specific EventScriptUtil methods that depend on ListBoxEx.
    /// Pure logic methods are in FEBuilderGBA.Core/EventScriptUtil.cs.
    /// </summary>
    public static class EventScriptUtilWinForms
    {
        public static void UpdateRelatedLine(ListBoxEx addressList, List<EventScript.OneCode> eventAsm)
        {
            addressList.ClearAllSetRelatedLine();

            int index = addressList.SelectedIndex;
            if (index < 0)
            {
                return;
            }
            if (index >= eventAsm.Count)
            {
                return;
            }
            EventScript.OneCode current = eventAsm[index];
            uint needLabelID = EventScriptUtil.GetScriptSomeLabel(current);
            if (needLabelID == U.NOT_FOUND)
            {
                return;
            }

            for (int i = 0; i < eventAsm.Count; i++)
            {
                if (i == index)
                {//自分自身を調べても意味がない
                    continue;
                }
                EventScript.OneCode code = eventAsm[i];
                uint labelID = EventScriptUtil.GetScriptSomeLabel(code);
                if (labelID == needLabelID)
                {
                    addressList.SetRelatedLine(i);
                }
            }
        }
    }
}
