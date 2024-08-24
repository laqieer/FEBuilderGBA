using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.IO;
using System.Diagnostics;
using System.Text;
using System.Windows.Forms;

namespace FEBuilderGBA
{
    public partial class SkillConfigCSkillSystem09xForm : Form
    {
        static uint gpSkillInfos = 0xB2A614;

        static uint GetSkillInfo(uint index)
        {
            return Program.ROM.p32(gpSkillInfos) + 8 * index;
        }

        static uint GetSkillDescMsg(uint index)
        {
            return Program.ROM.u16(GetSkillInfo(index) + 6);
        }

        static uint GetSkillNameMsg(uint index)
        {
            return Program.ROM.u16(GetSkillInfo(index) + 4);
        }

        static string SkillTextToName(string name)
        {
            int i = name.IndexOf(':');
            if (i < 0)
            {
                return "";
            }
            return name.Substring(0,i).Trim();
        }

        public static string GetSkillDesc(uint index)
        {
            return TextForm.Direct(GetSkillDescMsg(index));;
        }

        public static string GetSkillName(uint index)
        {
            uint name_msg = GetSkillNameMsg(index);
            if (name_msg != 0)
                return TextForm.Direct(name_msg);

            return SkillTextToName(GetSkillDesc(index));
        }
    }
}
