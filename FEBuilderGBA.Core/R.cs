using System;

namespace FEBuilderGBA
{
    /// <summary>
    /// Core translation + logging facade.
    /// Provides R._(), R.Error(), R.ShowStopError() etc. for Core code.
    /// The WinForms project defines a full R class that adds MessageBox
    /// dialogs and clipboard integration (CS0436 shadowing).
    /// </summary>
    static class R
    {
        public static string Notify(string str, params object[] args)
        {
            string s = MyTranslateResource.str(str, args);
            Log.Notify(s);
            return s;
        }

        public static string _(string str, params object[] args)
        {
            if (args.Length > 0)
            {
                return MyTranslateResource.str(str, args);
            }
            else
            {
                return MyTranslateResource.str(str);
            }
        }

        public static string Error(string str, params object[] args)
        {
            string s = MyTranslateResource.str(str, args);
            Log.Error(s);
            return s;
        }

        public static string Debug(string str, params object[] args)
        {
            string s = MyTranslateResource.str(str, args);
#if DEBUG
            Log.Debug(s);
#endif
            return s;
        }

        /// <summary>
        /// In Core, ShowStopError simply logs the error.
        /// The WinForms R class overrides this to show a MessageBox.
        /// </summary>
        public static void ShowStopError(string str, params object[] args)
        {
            R.Error(MyTranslateResource.str(str, args));
        }

        public static void ShowStopError(string str)
        {
            R.Error(MyTranslateResource.str(str));
        }

        public static void ShowStopError(string str, Exception ex)
        {
            ShowStopError("{0}\r\n\r\n{1}:\r\n{2}", str, ex.GetType().ToString(), ex.ToString());
        }
    }
}
