using System;
using System.Collections.Generic;
using System.IO;
using System.Diagnostics;
using System.Text;

namespace FEBuilderGBA
{
    //C#のリソースはなんだかいけていないので、
    //自前リソースを作る.
    //
    //:文字列
    //翻訳
    //
    //:文字列
    //翻訳
    //
    //
    public static class MyTranslateResource
    {
        static MyTranslateResourceLow Resource = new MyTranslateResourceLow();

        //翻訳文字列の取得
        public static string str(string src)
        {
            return Resource.str(src);
        }
        public static string str(string src, params object[] args)
        {
            string trans = str(src);

#if !DEBUG
            try
            {
#endif
                if (args.Length <= 0)
                {
                    return trans;
                }
                return string.Format(trans, args);
#if !DEBUG
            }
            catch (FormatException e)
            {
                Log.ErrorF("Translate Error! {0}->{1} @@ {2}" , src,trans , e.ToString() );
                try
                {
                    return string.Format(src, args);
                }
                catch (FormatException e2)
                {
                    Log.ErrorF("Translate Error2! {0} @@ {1}", src, e2.ToString());
                    return src;
                }
            }
#endif
        }
        public static void LoadResource(string fullfilename)
        {
            Resource.LoadResource(fullfilename);
        }

        /// <summary>
        /// Clear all translation entries so str() returns keys as-is (built-in Japanese).
        /// Use this instead of LoadResource("") which would trigger a missing-file error.
        /// </summary>
        public static void Clear()
        {
            Resource.Clear();
        }

        /// <summary>
        /// Load a reverse English→Japanese lookup map so that Avalonia English keys
        /// can be resolved to Japanese keys, then to the target language translation.
        /// </summary>
        public static void LoadReverseEnglishMap(string enFilePath)
        {
            Resource.LoadReverseEnglishMap(enFilePath);
        }
    }
}
