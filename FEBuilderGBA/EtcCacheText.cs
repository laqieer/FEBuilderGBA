using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace FEBuilderGBA
{
    class EtcCacheTextID : ITextIDCache
    {
        Dictionary<uint, string> EtcTextID;
        Dictionary<uint, string> TextID;

        public EtcCacheTextID()
        {
            this.EtcTextID = U.LoadTSVResource1(U.ConfigEtcFilename("textid_"), false);
            this.TextID = U.LoadDicResource(U.ConfigDataFilename("textid_"));
        }

        public void Update(uint textid, string comment)
        {
            if (comment == "")
            {
                if (this.EtcTextID.ContainsKey(textid))
                {
                    this.EtcTextID.Remove(textid);
                }
            }
            else
            {
                this.EtcTextID[textid] = comment;
            }
        }
        public void Save(string romBaseFilename)
        {
            if (this.EtcTextID.Count >= 1)
            {
                U.SaveConfigEtcTSV1("textid_", this.EtcTextID, romBaseFilename);
            }
        }

        //マージ専用
        public void AppendList(List<UseValsID> list)
        {
            foreach (var pair in this.EtcTextID)
            {
                UseValsID.AppendTextID(list, FELint.Type.TEXTID_FOR_USER, U.NOT_FOUND, pair.Value, pair.Key);
            }
            foreach (var pair in this.TextID)
            {
                UseValsID.AppendTextID(list, FELint.Type.TEXTID_FOR_SYSTEM, U.NOT_FOUND, pair.Value, pair.Key);
            }

            if (Program.ROM.RomInfo.version == 8)
            {
                if (Program.ROM.RomInfo.is_multibyte)
                {
                    for (uint textid = 0xE00; textid <= 0xEFF; textid++)
                    {
                        UseValsID.AppendTextID(list, FELint.Type.TEXTID_FOR_SYSTEM, U.NOT_FOUND, "", textid);
                    }
                }
                else
                {
                    for (uint textid = 0xE00; textid <= 0xFFF; textid++)
                    {
                        UseValsID.AppendTextID(list, FELint.Type.TEXTID_FOR_SYSTEM, U.NOT_FOUND, "", textid);
                    }
                }
            }
        }
        public UseValsID MakeUseTextID(uint textid)
        {
            string name;
            if (this.EtcTextID.TryGetValue(textid , out name))
            {
                return new UseValsID(FELint.Type.TEXTID_FOR_USER, U.NOT_FOUND, name, textid, UseValsID.TargetTypeEnum.TEXTID);
            }
            if (this.TextID.TryGetValue(textid, out name))
            {
                return new UseValsID(FELint.Type.TEXTID_FOR_SYSTEM, U.NOT_FOUND, name, textid, UseValsID.TargetTypeEnum.TEXTID);
            }
            if (Program.ROM.RomInfo.version == 8)
            {
                if (Program.ROM.RomInfo.is_multibyte)
                {
                    if (textid >= 0xE00 && textid <= 0xEFF)
                    {
                        return new UseValsID(FELint.Type.TEXTID_FOR_SYSTEM, U.NOT_FOUND, name, textid, UseValsID.TargetTypeEnum.TEXTID);
                    }
                }
                else
                {
                    if (textid >= 0xE00 && textid <= 0xFFF)
                    {
                        return new UseValsID(FELint.Type.TEXTID_FOR_SYSTEM, U.NOT_FOUND, name, textid, UseValsID.TargetTypeEnum.TEXTID);
                    }
                }
            }

            return null;
        }

        public string GetName(uint textid)
        {
            UseValsID p = MakeUseTextID(textid);
            if (p == null)
            {
                return "";
            }
            return p.Info;
        }

        // #1027 — ITextIDCache cache enumeration. Mirrors AppendList exactly:
        // user ids + system ids + FE8 reserved patch-text-slot ranges (0xE00..0xEFF
        // JP / 0xE00..0xFFF non-JP). Each id passes the WF AppendTextID guard
        // (id != 0 && id < 0x7FFF). Returns COPIED ids in a fresh set.
        public IEnumerable<uint> EnumerateUsedTextIds(ROM rom)
        {
            var ids = new HashSet<uint>();
            foreach (var pair in this.EtcTextID)
            {
                AddGuarded(ids, pair.Key);
            }
            foreach (var pair in this.TextID)
            {
                AddGuarded(ids, pair.Key);
            }
            if (rom != null && rom.RomInfo != null && rom.RomInfo.version == 8)
            {
                uint last = rom.RomInfo.is_multibyte ? 0xEFFu : 0xFFFu;
                for (uint textid = 0xE00; textid <= last; textid++)
                {
                    AddGuarded(ids, textid);
                }
            }
            return ids;
        }

        static void AddGuarded(HashSet<uint> ids, uint id)
        {
            if (id == 0 || id >= 0x7FFF) return;
            ids.Add(id);
        }
    }
}
