namespace FEBuilderGBA
{
    public static class TextToSpeechTextUtil
    {
        static readonly string[] ConvertTable = new string[]{
                 "・",""    ///No Translate
                ,"【",""    ///No Translate
                ,"】",""    ///No Translate
                ,"「",""    ///No Translate
                ,"」",""    ///No Translate
                ,"！","。"    ///No Translate
                ,"!","."    ///No Translate
                ,"\r\n","、"    ///No Translate
                ,"。。","。"    ///No Translate
                ,"、、","、"    ///No Translate
                ,",,",","    ///No Translate
                ,"..","."    ///No Translate
                ,"\"",""    ///No Translate
        };
        static readonly string[] ConvertTableEN = new string[]{
                 "・",""    ///No Translate
                ,"【",""    ///No Translate
                ,"】",""    ///No Translate
                ,"「",""    ///No Translate
                ,"」",""    ///No Translate
                ,"！","。"    ///No Translate
                ,"!","."    ///No Translate
                ,"\r\n"," "    ///No Translate
                ,"。。","."    ///No Translate
                ,"、、","、"    ///No Translate
                ,",,",","    ///No Translate
                ,"..","."    ///No Translate
                ,"\"",""    ///No Translate
        };

        public static string TextJoinCopy(string str, bool useSentensLineBreak)
        {
            return TextJoinCopy(str, useSentensLineBreak, Program.ROM.RomInfo.is_multibyte);
        }

        public static string TextJoinCopy(string str, bool useSentensLineBreak, bool isMultibyte)
        {
            string text;
            if (isMultibyte)
            {
                text = U.table_replace(str, ConvertTable);
            }
            else
            {
                text = U.table_replace(str, ConvertTableEN);
            }
            if (useSentensLineBreak)
            {
                if (isMultibyte)
                {
                    text = text.Replace("。", "。\r\n");   ///No Translate
                    text = text.Replace("\r\n、", "\r\n");   ///No Translate
                }
                else
                {
                    text = text.Replace(".", ".\r\n");   ///No Translate
                    text = text.Replace("\r\n,", "\r\n");   ///No Translate
                }
            }

            return text;
        }
    }
}
