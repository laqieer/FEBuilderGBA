using System.Windows.Forms;

namespace FEBuilderGBA
{
    class ConfigWinForms : Config
    {
        public Keys[] ShortCutKeys { get; private set; }
        public void UpdateShortcutKeys()
        {
            CheckDefault();

            ShortCutKeys = new Keys[15];
            U.CheckKeys(this.at("ShortCutKey1"), out ShortCutKeys[0]);
            U.CheckKeys(this.at("ShortCutKey2"), out ShortCutKeys[1]);
            U.CheckKeys(this.at("ShortCutKey3"), out ShortCutKeys[2]);
            U.CheckKeys(this.at("ShortCutKey4"), out ShortCutKeys[3]);
            U.CheckKeys(this.at("ShortCutKey5"), out ShortCutKeys[4]);
            U.CheckKeys(this.at("ShortCutKey6"), out ShortCutKeys[5]);
            U.CheckKeys(this.at("ShortCutKey7"), out ShortCutKeys[6]);
            U.CheckKeys(this.at("ShortCutKey8"), out ShortCutKeys[7]);
            U.CheckKeys(this.at("ShortCutKey9"), out ShortCutKeys[8]);
            U.CheckKeys(this.at("ShortCutKey10"), out ShortCutKeys[9]);
            U.CheckKeys(this.at("ShortCutKey11"), out ShortCutKeys[10]);
            U.CheckKeys(this.at("ShortCutKey12"), out ShortCutKeys[11]);
            U.CheckKeys(this.at("ShortCutKey13"), out ShortCutKeys[12]);
            U.CheckKeys(this.at("ShortCutKey14"), out ShortCutKeys[13]);
            U.CheckKeys(this.at("ShortCutKey15"), out ShortCutKeys[14]);
        }

        void CheckDefault()
        {
            SetDefaultIfEmpty("ShortCutKey1", "F5");
            SetDefaultIfEmpty("ShortCutValue1", "1");  //F5 エミュレータ
            SetDefaultIfEmpty("ShortCutKey2", U.GetCtrlKeyName() + "+F5");
            SetDefaultIfEmpty("ShortCutValue2", "2");  //Ctrl+F5 デバッガー
            SetDefaultIfEmpty("ShortCutKey3", U.GetCtrlKeyName() + "+K");
            SetDefaultIfEmpty("ShortCutValue3", "12"); //Ctrl+K 書き込み
            SetDefaultIfEmpty("ShortCutKey4", "Pause");
            SetDefaultIfEmpty("ShortCutValue4", "11"); //Pause メインへ
            SetDefaultIfEmpty("ShortCutKey5", "F11");
            SetDefaultIfEmpty("ShortCutValue5", "3"); //F11 バイナリエディタ
            SetDefaultIfEmpty("ShortCutKey6", "F3");
            SetDefaultIfEmpty("ShortCutValue6", "19"); //リストから次を検索
            SetDefaultIfEmpty("ShortCutKey7", "");
            SetDefaultIfEmpty("ShortCutValue7", "");
            SetDefaultIfEmpty("ShortCutKey8", "");
            SetDefaultIfEmpty("ShortCutValue8", "");
            SetDefaultIfEmpty("ShortCutKey9", "");
            SetDefaultIfEmpty("ShortCutValue9", "");
            SetDefaultIfEmpty("ShortCutKey10", "");
            SetDefaultIfEmpty("ShortCutValue10", "");
            SetDefaultIfEmpty("ShortCutKey11", U.GetCtrlKeyName() + "+W");
            SetDefaultIfEmpty("ShortCutValue11", "13"); //Ctrl+W 閉じる
            SetDefaultIfEmpty("ShortCutKey12", "F10"); //F10 ソースコードを表示
            SetDefaultIfEmpty("ShortCutValue12", "23");
            SetDefaultIfEmpty("ShortCutKey13", "");
            SetDefaultIfEmpty("ShortCutValue13", "");
            SetDefaultIfEmpty("ShortCutKey14", "");
            SetDefaultIfEmpty("ShortCutValue14", "");
            SetDefaultIfEmpty("ShortCutKey15", "");
            SetDefaultIfEmpty("ShortCutValue15", "");
        }

        void SetDefaultIfEmpty(string key, string def)
        {
            if (def == "")
            {
                return;
            }
            if (!this.ContainsKey(key))
            {
                this[key] = def;
            }
        }
    }
}
