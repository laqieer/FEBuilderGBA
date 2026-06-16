using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace FEBuilderGBA
{
    public class EtcCacheFLag
    {
        Dictionary<uint, string> EtcFlag;
        Dictionary<uint, string> Flag;

        public EtcCacheFLag()
        {
            this.EtcFlag = U.LoadTSVResource1(U.ConfigEtcFilename("flag_"), false);
            this.Flag = U.LoadDicResource(U.ConfigDataFilename("flag_"));
            foreach (var pair in EtcFlag)
            {
                string name = pair.Value;
                if (name.Length > 0)
                {
                    this.Flag[pair.Key] = name;
                }
            }

            U.OrderBy(this.Flag, (x) => { return (int)x.Key; });
        }

        public bool CheckFast(uint num)
        {
            return Flag.ContainsKey(num);
        }
        public bool TryGetValue(uint num, out string out_data)
        {
            return Flag.TryGetValue(num, out out_data);
        }

        public void Update(uint addr, string comment,string baseName)
        {
            if (comment == "")
            {
                if (this.EtcFlag.ContainsKey(addr))
                {
                    this.EtcFlag.Remove(addr);
                }
                this.Flag[addr] = baseName;
            }
            else
            {
                this.EtcFlag[addr] = comment;
                this.Flag[addr] = comment;
            }
        }
        public void Save(string romBaseFilename)
        {
            // Always delegate: SaveConfigEtcTSV1 DELETES the per-ROM file when EtcFlag is
            // empty, so clearing the last customization removes the stale on-disk config
            // instead of leaving it behind (#1191). Writing an empty file is also harmless.
            U.SaveConfigEtcTSV1("flag_", this.EtcFlag, romBaseFilename);
        }

        /// <summary>
        /// Load the SHIPPED base flag names (config/data/flag_*.txt) WITHOUT any user
        /// customizations. The Flag-Name tool needs these to detect a custom name
        /// (current != base) and to revert (pass as baseName to <see cref="Update"/>).
        /// Mirrors what WinForms ToolFlagNameForm loads into its BaseFlag dict. (#1191)
        /// </summary>
        public static Dictionary<uint, string> LoadBaseFlagNames()
        {
            return U.LoadDicResource(U.ConfigDataFilename("flag_"));
        }

        public List<AddrResult> MakeList()
        {
            List<AddrResult> list = new List<AddrResult>();
            foreach (var pair in this.Flag)
            {
                AddrResult ar = new AddrResult(
                     pair.Key
                    , pair.Value
                    );
                list.Add(ar);
            }
            return list;
        }

        //マージ専用
        public void MargeFlags(Dictionary<uint, string> flags)
        {
            foreach (var pair in flags)
            {
                if (this.EtcFlag.ContainsKey(pair.Key))
                {
                    continue;
                }
                this.Flag[pair.Key] = pair.Value;
            }
            U.OrderBy(this.Flag, (x) => { return (int)x.Key; });
        }
    }
}
