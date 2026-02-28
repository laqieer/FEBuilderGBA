using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;

namespace FEBuilderGBA
{
    public class Undo
    {
        public class UndoPostion
        {
            public uint addr { get; internal set; }
            public byte[] data { get; internal set; }

            public UndoPostion()
            {
            }
            public UndoPostion(uint addr, byte[] data)
            {
                this.addr = addr;
                this.data = data;
            }
            public UndoPostion(uint addr, uint size)
            {
                var rom = CoreState.ROM;
                if (rom == null || addr >= rom.Data.Length)
                {
                    this.data = new byte[0];
                    return;
                }
                if (addr + size >= rom.Data.Length)
                {
                    size = (uint)(rom.Data.Length - addr);
                }

                this.addr = addr;
                this.data = rom.getBinaryData(addr, size);
            }
        };
        public class UndoData
        {
            public DateTime time { get; internal set; }
            public String name { get; internal set; }
            public List<UndoPostion> list { get; internal set; }
            public uint filesize { get; internal set; }
            public bool is_f5test { get; internal set; }

        };

        /// <summary>
        /// Callbacks for platform-specific operations.
        /// WinForms/Avalonia sets these during startup.
        /// </summary>
        public static Action OnCacheDataCountCleared { get; set; }
        public static Action OnAllFormsInvalidated { get; set; }
        public static Func<string> GetActiveFormName { get; set; }
        public static Action<string, string> OnRunEmulator { get; set; }

        public List<UndoData> UndoBuffer { get; private set; }
        public int Postion { get; private set; } //UndoBufferの位置 通常 終端を指す.
        public int PostionWhenFileSaving { get; private set; } //ファイルを保存したときの終端
        byte[] RollBackCancelBackup; //ロールバックをキャンセルするためのバックアップ

        public Undo()
        {
            Postion = 0;
            UndoBuffer = new List<UndoData>();
        }

        public UndoData NewUndoData(object f, params string[] args)
        {
            string formname = f?.GetType().Name ?? "";
            return NewUndoDataLow(formname + " " + string.Join(" ", args));
        }
        public UndoData NewUndoData(string name,params string[] args)
        {
            string formname = GetActiveFormName?.Invoke() ?? "";
            return NewUndoDataLow(formname + " =" + name + " " + string.Join(" ", args));
        }

        UndoData NewUndoDataLow(string name)
        {
            UndoData ud = new UndoData();
            ud.time = DateTime.Now.ToLocalTime();
            ud.name = name;
            ud.list = new List<UndoPostion>();
            ud.filesize = (uint)CoreState.ROM.Data.Length;
            ud.is_f5test = false;
            return ud;
        }
        void DumpLog(UndoData ud)
        {
            StringBuilder sb = new StringBuilder();

            foreach (UndoPostion p in ud.list)
            {
                sb.Append(U.To0xHexString(p.addr));
                sb.Append('@');
                sb.Append(p.data.Length);
                sb.Append(' ');
            }
            sb.Append("Write:");
            sb.Append(ud.name);
            sb.Append(' ');
            sb.Append(ud.time.ToString("yyyyMMddHHmmss"));
            Log.Notify(sb.ToString());
        }
        public void Push(UndoData ud)
        {
            DumpLog(ud);

            if (this.Postion < this.UndoBuffer.Count)
            {//常に先頭に追加したいので、リスト中に戻っている場合は、それ以降を消す.
                this.UndoBuffer.RemoveRange(this.Postion, this.UndoBuffer.Count - this.Postion);
                //状況が変わるので、ロールバックバッファを破棄
                this.RollBackCancelBackup = null;
            }
            this.UndoBuffer.Add(ud);
            this.Postion = this.UndoBuffer.Count;
            //書き込んだのでデータの長さキャッシュをクリアする.
            OnCacheDataCountCleared?.Invoke();
            //EVENTとASMのキャッシュをクリア
            CoreState.AsmMapFileAsmCache?.ClearCache();
        }
        public void Rollback(UndoData ud)
        {
            Push(ud);
            RunUndo();
        }

        public void Push(string name,uint addr,uint size)
        {
            UndoData ud = new UndoData();
            ud.time = DateTime.Now.ToLocalTime();
            ud.name = name;
            ud.list = new List<UndoPostion>();
            ud.filesize = (uint)CoreState.ROM.Data.Length;

            UndoPostion up = new UndoPostion(addr,size);
            ud.list.Add(up);

            Push(ud);
        }


        public void RunUndo()
        {
            if (this.Postion <= 0)
            {
                return; //無理
            }
            Rollback(this.Postion - 1);
        }
        public void Rollback(int pos)
        {
            if (pos < 0)
            {
                Debug.Assert(false);
                return;
            }
            Log.Notify("Undo.Rollback:"+ pos);

            RollbackROM(pos, CoreState.ROM);
            //書き込んだのでデータの長さキャッシュをクリアする.
            OnCacheDataCountCleared?.Invoke();
            //EVENTとASMのキャッシュをクリア
            CoreState.AsmMapFileAsmCache?.ClearCache();
            //すべてのフォームを再描画
            OnAllFormsInvalidated?.Invoke();
        }
        public void TestPlayThisVersion(int pos)
        {
            if (pos < 0)
            {
                Debug.Assert(false);
                return;
            }

            ROM rom = CoreState.ROM.Clone();
            //undoの実行
            for (int i = this.UndoBuffer.Count - 1; i >= pos; i--)
            {
                Patch(this.UndoBuffer[i], rom);
            }

            string dir = System.IO.Path.GetDirectoryName(CoreState.ROM.Filename);
            string filename = System.IO.Path.GetFileNameWithoutExtension(CoreState.ROM.Filename);
            string ext = CoreState.ROM.IsVirtualROM ? ".gba" : System.IO.Path.GetExtension(CoreState.ROM.Filename);
            string t = System.IO.Path.Combine(dir, filename + ".emulator" + ext);
            rom.Save(t, true);

            OnRunEmulator?.Invoke("emulator", t);
        }

        bool RollbackROM(int pos,ROM rom)
        {
            if (pos < 0 || pos > this.UndoBuffer.Count)
            {
                Debug.Assert(false);
                return false;
            }

            if (this.Postion == this.UndoBuffer.Count)
            {
                //ロールバックをキャンセルできるようにするために、現状のROMを記録しておく.
                this.RollBackCancelBackup = (byte[])rom.Data.Clone();
            }
            else
            {
                if (this.RollBackCancelBackup == null)
                {//無理
                    Debug.Assert(false);
                    return false;
                }

                //まずロールバックする前の状態に戻す.
                if (this.RollBackCancelBackup.Length != rom.Data.Length)
                {//長さが増える場合、ROMを増設する.
                    rom.write_resize_data((uint)this.RollBackCancelBackup.Length);
                }
                rom.write_range(0, this.RollBackCancelBackup);
            }

            //undoの実行
            for (int i = this.UndoBuffer.Count - 1; i >= pos; i--)
            {
                Patch(this.UndoBuffer[i], rom);
            }

            this.Postion = pos;
            return true;
        }
        void Patch(UndoData ud,ROM rom)
        {
            if (rom.Data.Length < ud.filesize)
            {//ROMサイズが増やさないといけないなら増やす.
                rom.write_resize_data(ud.filesize);
            }

            for(int i = 0 ; i < ud.list.Count ; i++)
            {
                rom.write_range(ud.list[i].addr, ud.list[i].data);
            }

            if (rom.Data.Length > ud.filesize)
            {//ROMサイズを減らさないといけないなら、ここで減らす.
                rom.write_resize_data(ud.filesize);
            }
        }

        //メモリ上だけでのUndo
        public static byte[] RollbackMemoryData(Undo u,int pos,byte[] romdata)
        {
            byte[] ret = (byte[])romdata.Clone();

            //undoの実行
            for (int i = u.UndoBuffer.Count - 1; i >= pos; i--)
            {
                UndoData ud = u.UndoBuffer[i];
                if (ret.Length < ud.filesize)
                {//ROMサイズが増やさないといけないなら増やす.
                    Array.Resize(ref ret, (int)U.Padding4(ud.filesize));
                }

                for (int n = 0; n < ud.list.Count; n++)
                {
                    U.write_range(ret,ud.list[n].addr, ud.list[n].data);
                }

                if (ret.Length > ud.filesize)
                {//ROMサイズを減らさないといけないなら、ここで減らす.
                    Array.Resize(ref ret, (int)U.Padding4(ud.filesize));
                }
            }

            return ret;
        }

        public string MakeName(int pos,bool showAllowMark = true)
        {
            if (pos > UndoBuffer.Count || pos < 0)
            {
                Debug.Assert(false);
                return "";
            }

            string name;
            if (pos == UndoBuffer.Count)
            {
                name = "最新版";
            }
            else
            {
                UndoData ud = UndoBuffer[pos];
                StringBuilder sb = new StringBuilder();
                sb.Append(ud.time.ToString());
                sb.Append(" ");
                if (ud.is_f5test)
                {
                    sb.Append("[F5]");
                }
                sb.Append(ud.name);
                name = sb.ToString();
            }

            if (showAllowMark)
            {
                if (pos == this.Postion)
                {
                    name = "->" + name;
                }
            }
            return name;
        }
        //最終変更日時を取得
        public DateTime getLastModify()
        {
            int count = this.UndoBuffer.Count;
            if (count <= 0)
            {//未更新
                return new DateTime(1970,1,1);
            }
            return this.UndoBuffer[count - 1].time;
        }

        //現在の変更履歴でエミュレータテストが行われたことを記録する
        public void SetF5()
        {
            if (this.Postion == 0)
            {//何も変更していないので記録しない.
                return;
            }
            if (this.UndoBuffer.Count < this.Postion - 1)
            {//ロールバックしている状態なので記録しない.
                return;
            }
            this.UndoBuffer[this.Postion - 1].is_f5test = true;
        }
    }
}
