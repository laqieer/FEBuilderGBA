using System;
using System.Collections.Generic;
using System.IO;
using System.Diagnostics;
using System.Text;

namespace FEBuilderGBA
{
    public class EAUtilLynDumpMode
    {
        public struct Data
        {
            public string Name;
            public uint StartLow;
            public uint Start;
        }
        List<Data> List;
        List<byte> Bin;
        uint LastORG;

        public EAUtilLynDumpMode()
        {
            this.List = new List<Data>();
            this.Bin = new List<byte>();
            this.LastORG = 0;
        }
        public bool ParseLine(string line)
        {
            if (CheckEnd(line))
            {//Lynモードの終了
                return false;
            }

            if (line.IndexOf("ORG CURRENTOFFSET+") == 0)
            {
                ParseORG(line);
            }
            else if (line.IndexOf("SHORT ") == 0)
            {
                ParseSHORT(line);
            }
            else if (line.IndexOf("BYTE ") == 0)
            {
                ParseBYTE(line);
            }
            else if (line.IndexOf("POIN ") == 0)
            {
                this.Bin.Add(0);
                this.Bin.Add(0);
                this.Bin.Add(0);
                this.Bin.Add(0);
            }
            else if (line.IndexOf("WORD ") == 0)
            {
                ParseWORD(line);
            }

            return true;
        }
        void ParseWORD(string line)
        {
            string[] sp = line.Split(' ');
            for (int i = 1; i < sp.Length; i++)
            {
                U.append_u32(this.Bin, U.atoi0x(sp[i]));
            }
        }
        void ParseSHORT(string line)
        {
            string[] sp = line.Split(' ');
            for (int i = 1; i < sp.Length; i++)
            {
                U.append_u16(this.Bin,U.atoi0x(sp[i]));
            }
        }
        void ParseBYTE(string line)
        {
            string[] sp = line.Split(' ');
            for (int i = 1; i < sp.Length; i++)
            {
                this.Bin.Add((byte)U.atoi0x(sp[i]));
            }
        }

        bool CheckEnd(string line)
        {
            return (line == "");
        }

        void ParseORG(string line)
        {
            string startString = U.cut(line, "+", ";");
            if (startString == "")
            {
                return;
            }
            uint start = U.atoi0x(startString);
            start += this.LastORG;

            if (this.LastORG == 0 && start > 1)
            {
                Data d = new Data();
                d.Name = "NONAME";
                d.StartLow = 0;
                d.Start = 0;
                this.List.Add(d);
            }

            {
                string labelString = U.cut(line, ";", ":");

                Data d = new Data();
                d.Name = labelString;
                d.StartLow = start;
                if (U.IsValueOdd(start))
                {
                    d.Start = start - 1;
                }
                else
                {
                    d.Start = start;
                }
                this.List.Add(d);
            }
            this.LastORG = start;
        }
        public byte[] GetDataAll()
        {
            return this.Bin.ToArray();
        }

        public int GetCount()
        {
            return this.List.Count;
        }
        public byte[] GetData(int index)
        {
            int next = index + 1;
            uint end;
            if (this.List.Count <= next)
            {
                end = (uint)this.Bin.Count;
            }
            else
            {
                end = this.List[next].Start;
            }
            uint start = this.List[index].Start;
            return this.Bin.GetRange((int)start, (int)(end - start)).ToArray();
        }
        public string GetName(int index)
        {
            return this.List[index].Name;
        }
    }
}
