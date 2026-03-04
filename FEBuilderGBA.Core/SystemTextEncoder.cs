using System;
using System.Collections.Generic;
using System.IO;
using System.Diagnostics;
using System.Text;

namespace FEBuilderGBA
{
    public class SystemTextEncoder : ISystemTextEncoder
    {
        Encoding Encoder;
        SystemTextEncoderTBLEncodeInterface TBLEncode;
        public SystemTextEncoder()
        {
            Build();
        }
        public SystemTextEncoder(TextEncodingEnum textencoding, ROM rom)
        {
            Build(textencoding, rom);
        }

        public void Build()
        {
            TextEncodingEnum textencoding = CoreState.TextEncoding;
            Build(textencoding, CoreState.ROM);
        }

        public void Build(TextEncodingEnum textencoding, ROM rom)
        {
            // Ensure code page encodings (Shift_JIS, etc.) are registered
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

            bool r = LoadTBL(textencoding, rom);
            if (r)
            {//TBLを利用.
                return;
            }

            if (textencoding == TextEncodingEnum.Auto)
            {//自動選択
                PatchDetection.PRIORITY_CODE priorityCode = PatchDetection.SearchPriorityCode(rom);
                if (priorityCode == PatchDetection.PRIORITY_CODE.UTF8)
                {
                    this.Encoder = System.Text.Encoding.GetEncoding("UTF-8");
                }
                else if (priorityCode == PatchDetection.PRIORITY_CODE.LAT1)
                {
                    this.Encoder = System.Text.Encoding.GetEncoding("iso-8859-1");
                }
                else
                {
                    //ディフォルトは日本語.
                    this.Encoder = System.Text.Encoding.GetEncoding("Shift_JIS");
                }
            }
            else if (textencoding == TextEncodingEnum.LAT1)
            {
                this.Encoder = System.Text.Encoding.GetEncoding("iso-8859-1");
            }
            else if (textencoding == TextEncodingEnum.UTF8)
            {
                this.Encoder = System.Text.Encoding.GetEncoding("UTF-8");
            }
            else if (textencoding == TextEncodingEnum.Shift_JIS)
            {
                this.Encoder = System.Text.Encoding.GetEncoding("Shift_JIS");
            }

            this.TBLEncode = null;
        }
        bool LoadTBL(TextEncodingEnum textencoding, ROM rom)
        {
            if (rom == null)
            {
                return false;
            }

            if (textencoding == TextEncodingEnum.ZH_TBL)
            {
                string resoucefilename = System.IO.Path.Combine(CoreState.BaseDirectory, "config", "translate", "zh_tbl", rom.RomInfo.TitleToFilename + ".tbl");
                if (! File.Exists(resoucefilename))
                {
                    Log.Error("tbl not found. filename:{0}", resoucefilename);
                    return false;
                }
                this.TBLEncode = new SystemTextEncoderTBLEncode(resoucefilename);
                this.Encoder = null;
                return true;
            }
            else if (textencoding == TextEncodingEnum.EN_TBL)
            {
                string resoucefilename = System.IO.Path.Combine(CoreState.BaseDirectory, "config", "translate", "en_tbl", rom.RomInfo.TitleToFilename + ".tbl");
                if (! File.Exists(resoucefilename))
                {
                    Log.Error("tbl not found. filename:{0}", resoucefilename);
                    return false;
                }
                this.TBLEncode = new SystemTextEncoderTBLEncode(resoucefilename);
                this.Encoder = null;
                return true;
            }
            else if (textencoding == TextEncodingEnum.KO_TBL)
            {
                string resoucefilename = System.IO.Path.Combine(CoreState.BaseDirectory, "config", "translate", "ko_tbl", rom.RomInfo.TitleToFilename + ".tbl");
                if (!File.Exists(resoucefilename))
                {
                    Log.Error("tbl not found. filename:{0}", resoucefilename);
                    return false;
                }
                this.TBLEncode = new SystemTextEncoderTBLEncode(resoucefilename);
                this.Encoder = null;
                return true;
            }
            else if (textencoding == TextEncodingEnum.AR_TBL)
            {
                string resoucefilename = System.IO.Path.Combine(CoreState.BaseDirectory, "config", "translate", "ar_tbl", rom.RomInfo.TitleToFilename + ".arabic_tbl");
                if (! File.Exists(resoucefilename))
                {
                    Log.Error("tbl not found. filename:{0}", resoucefilename);
                    return false;
                }
                SystemTextEncoderTBLEncode inner = null;
                if (rom.RomInfo.version == 6)
                {
                    string resoucefilename_inner = System.IO.Path.Combine(CoreState.BaseDirectory, "config", "translate", "en_tbl", rom.RomInfo.TitleToFilename + ".tbl");
                    if (! File.Exists(resoucefilename))
                    {
                        return false;
                    }
                    inner = new SystemTextEncoderTBLEncode(resoucefilename_inner);
                }
                this.TBLEncode = new SystemTextEncoderArabianTBLEncode(resoucefilename,inner);
                this.Encoder = null;
                return true;
            }
            else if (textencoding == TextEncodingEnum.KR_TBL)
            {
                string resoucefilename = System.IO.Path.Combine(CoreState.BaseDirectory, "config", "translate", "kr_tbl", rom.RomInfo.TitleToFilename + ".tbl");
                if (!File.Exists(resoucefilename))
                {
                    Log.Error("tbl not found. filename:{0}", resoucefilename);
                    return false;
                }
                this.TBLEncode = new SystemTextEncoderTBLEncode(resoucefilename);
                this.Encoder = null;
                return true;
            }
            return false;
        }

        static readonly Encoding FallbackEncoding = Encoding.GetEncoding("iso-8859-1");

        public string Decode(byte[] str)
        {
            if (this.Encoder != null)
                return this.Encoder.GetString(str);
            if (this.TBLEncode != null)
                return this.TBLEncode.Decode(str);
            return FallbackEncoding.GetString(str);
        }
        public string Decode(byte[] str,int start,int len)
        {
            if (this.Encoder != null)
                return this.Encoder.GetString(str, start, len);
            if (this.TBLEncode != null)
                return this.TBLEncode.Decode(str, start, len);
            return FallbackEncoding.GetString(str, start, len);
        }
        public byte[] Encode(string str)
        {
            if (this.Encoder != null)
                return this.Encoder.GetBytes(str);
            if (this.TBLEncode != null)
                return this.TBLEncode.Encode(str);
            return FallbackEncoding.GetBytes(str);
        }

        public Dictionary<string, uint> GetTBLEncodeDicLow()
        {
            if (this.Encoder == null)
            {
                return this.TBLEncode.GetEncodeDicLow();
            }
            else
            {
                return new Dictionary<string, uint>();
            }
        }
    }
}
