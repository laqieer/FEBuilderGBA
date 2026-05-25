using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;

namespace FEBuilderGBA
{
    public class RecycleAddress
    {
        List<Address> Recycle;
        public RecycleAddress()
        {
            this.Recycle = new List<Address>();
        }
        public RecycleAddress(List<Address> list)
        {
            this.Recycle = list;
            this.RecycleOptimize();
        }

        public bool AlreadyRecycled(uint addr)
        {
            //既に登録されている場合は登録しない.
            foreach (Address a in Recycle)
            {
                if (a.Addr == addr)
                {//すでにある.
                    return true;
                }
            }
            return false;
        }

        //別の領域で使われているので再利用してはいけない領域を消す.
        public void SubRecycle(List<Address> rlist)
        {
            foreach (Address a in rlist)
            {
                SubRecycle(a.Addr, a.Length);
            }
        }
        public bool SubRecycle(uint addr, int length)
        {
            return SubRecycle(addr, (uint)length);
        }
        public bool SubRecycle(uint addr, uint length)
        {
            bool ret = false;
            //既に登録されている場合は削除する
            for (int i = 0; i < this.Recycle.Count; )
            {
                Address a = this.Recycle[i];
                if (a.Addr >= addr
                    && a.Addr < addr + length)
                {//登録されているので解除する.
                    this.Recycle.RemoveAt(i);
                    ret = true;
                    continue;
                }
                if (a.Addr + a.Length > addr
                    && a.Addr + a.Length < addr + length)
                {//登録されているので解除する.
                    this.Recycle.RemoveAt(i);
                    ret = true;
                    continue;
                }
                i++;
            }
            return ret;
        }


        //追加で領域の指定
        public void AddRecycle(List<Address> rlist)
        {
            foreach(Address a in rlist)
            {
                AddRecycle(a);
            }
        }
        void AddRecycle(Address a)
        {
            //既に登録されている場合は無視する
            for (int i = 0; i < this.Recycle.Count; i++)
            {
                Address b = this.Recycle[i];
                if (b.Addr >= a.Addr
                    && b.Addr < a.Addr + a.Length)
                {//登録されているので無視する.
                    return;
                }
                if (b.Addr + b.Length > a.Addr
                    && b.Addr + b.Length < a.Addr + a.Length)
                {//登録されているので無視する.
                    return;
                }
            }
            this.Recycle.Add(a);
        }

        bool RecycleOptimize_List()
        {
            //まずアドレス順に昇順に並べる.
            this.Recycle.Sort((a, b) => { return (int)(((int)a.Addr) - ((int)b.Addr)); });

            bool conflict = false;

            //重複する部分アドレスが含まれている場合除外する
            for (int i = 0; i < this.Recycle.Count - 1; )
            {
                Address p = this.Recycle[i];
                Address p2 = this.Recycle[i + 1];

                if (p.Addr == p2.Addr && p.Length == p2.Length)
                {//完全に一致
                    this.Recycle.RemoveAt(i + 1);
                    continue;
                }

                uint p_end = p.Addr + p.Length;
                uint p_end2 = p2.Addr + p2.Length;
                if (p_end > p2.Addr && p_end2 <= p_end)
                {//重複している
                    //p |---------------------|
                    //p2      |------------|
                    Log.Notify("重複 ", i.ToString(), "P:", U.ToHexString(p.Addr), " l:", U.ToHexString(p.Length), " P2:", U.ToHexString(p2.Addr), " l:", U.ToHexString(p.Length));
                    this.Recycle.RemoveAt(i + 1);
                    continue;
                }
                if (p_end > p2.Addr && p_end2 > p_end)
                {//重複している
                    //p |---------------------|
                    //p2                   |------------|
                    Log.Error(R._("重複だが結合できる "), i.ToString(), "P:", U.ToHexString(p.Addr), " l:", U.ToHexString(p.Length), " P2:", U.ToHexString(p2.Addr), " l:", U.ToHexString(p.Length));

                    Debug.Assert((p_end - p2.Addr) >= 0);
                    Debug.Assert(p2.Length >= (p_end - p2.Addr));

                    uint length = p2.Length - (p_end - p2.Addr);
                    p.ResizeAddress(p.Addr, length);

                    this.Recycle.RemoveAt(i + 1);

                    //結合したリストの妥当性のテストのため再度ループを回す必要がある
                    conflict = true;
                    continue;
                }
                i++;
            }
            return conflict;
        }

        public void RecycleOptimize()
        {
            if (this.Recycle.Count <= 1)
            {
                return;
            }

            //矛盾点が無くなるまで、最適化ループを回します。
            //念のため1000回で諦めます
            for (int i = 0; i < 1000; i++)
            {
                bool conflict = RecycleOptimize_List();
                if (conflict == false)
                {
                    break;
                }
            }

            //探索しやすいように、サイズで昇順に並べる.
            this.Recycle.Sort((a, b) => { return (int)(((int)a.Length) - ((int)b.Length)); });
        }

        public uint WritePointerOnly(uint write_pointer, uint content_addr, Undo.UndoData undodata)
        {
            CoreState.ROM.write_p32(write_pointer, content_addr, undodata);
            return content_addr;
        }
        public uint WriteAndWritePointer(uint write_pointer, byte[] write_data, Undo.UndoData undodata)
        {
            uint use_addr = Write(write_data, undodata);
            if (use_addr == U.NOT_FOUND)
            {
                return U.NOT_FOUND;
            }
            //ポインタ先に書き込んで領域を入れる
            CoreState.ROM.write_p32(write_pointer, use_addr, undodata);

            return use_addr;
        }

        // -----------------------------------------------------------------
        // Ambient-undo overloads (#524) — these mirror the explicit-undo
        // methods above but route through the no-undo rom.write_* overloads.
        // Callers wrap them in ROM.BeginUndoScope so the ambient scope
        // captures every write exactly once into the active UndoData. Mixing
        // these methods with the explicit-undo overloads while an ambient
        // scope is active would double-record (see Rom.cs:849-883 - the
        // explicit overload appends to undodata.list AND delegates to the
        // no-undo overload which appends to _ambientUndoData.list when they
        // refer to the same UndoData instance).
        // -----------------------------------------------------------------

        /// <summary>
        /// Repoint <paramref name="write_pointer"/> at <paramref name="content_addr"/>
        /// using the no-undo <c>write_p32</c> overload. The active
        /// <see cref="ROM.BeginUndoScope"/> captures the write into the
        /// ambient UndoData. Returns <paramref name="content_addr"/> (the
        /// caller's input) so this method composes with Write below.
        /// </summary>
        public uint WritePointerOnlyAmbient(uint write_pointer, uint content_addr)
        {
            CoreState.ROM.write_p32(write_pointer, content_addr);
            return content_addr;
        }

        /// <summary>
        /// Same as <see cref="WriteAndWritePointer"/> but the write
        /// is routed through the no-undo overloads so the active ambient
        /// undo scope captures each write exactly once.
        /// </summary>
        public uint WriteAndWritePointerAmbient(uint write_pointer, byte[] write_data)
        {
            uint use_addr = WriteAmbient(write_data);
            if (use_addr == U.NOT_FOUND)
            {
                return U.NOT_FOUND;
            }
            CoreState.ROM.write_p32(write_pointer, use_addr);
            return use_addr;
        }

        /// <summary>
        /// Allocate <paramref name="write_data"/> from the recycle pool (or
        /// freespace fallback) and emit the bytes via the no-undo
        /// <c>rom.write_range</c> overload. The active ambient
        /// <see cref="ROM.BeginUndoScope"/> captures each write exactly once.
        /// Mirrors <see cref="Write(byte[], Undo.UndoData)"/> step-for-step
        /// but without the explicit undodata parameter on the inner calls.
        /// </summary>
        public uint WriteAmbient(byte[] write_data)
        {
            for (int i = 0; i < this.Recycle.Count; i++)
            {
                Address p = this.Recycle[i];
                if (p.Length >= write_data.Length)
                {
                    uint use_addr = p.Addr;
                    uint left_size = p.Length;
                    if (!U.isPadding4(use_addr))
                    {
                        if (left_size < 4)
                        {
                            Log.Notify("アドレスが端数値なので補正しようとしましたが、サイズが4未満なので利用しません", U.To0xHexString(p.Addr));
                            continue;
                        }
                        uint diff = 4 - (use_addr % 4);
                        use_addr += diff;
                        left_size -= diff;
                        if (left_size < write_data.Length)
                        {
                            Log.Notify("アドレスが端数値なので補正しようとしたらサイズ不足になりました", U.To0xHexString(p.Addr));
                            continue;
                        }
                        Log.Notify("アドレスが端数値なので補正します。", U.To0xHexString(p.Addr));
                    }

                    CoreState.ROM.write_range(use_addr, write_data);
                    uint next_addr = U.Padding4(use_addr + (uint)write_data.Length);
                    left_size = U.Sub(left_size, (next_addr - use_addr));

                    p.ResizeAddress(next_addr, left_size);
                    if (p.Length < 4)
                    {
                        this.Recycle.RemoveAt(i);
                    }

                    return use_addr;
                }
            }

            // Recycle pool didn't satisfy the request. We deliberately do NOT
            // call CoreState.AppendBinaryData here - that seam is WinForms-
            // specific (its signature takes an explicit undodata which would
            // double-record against the ambient scope, the very condition
            // these Ambient-suffix methods exist to avoid). Fall back to
            // rom.FindFreeSpace + no-undo write_range so #524 BulkImport works
            // headlessly. The tail-resize special case below mirrors
            // Write(_, undodata) for the case where the last recycle range
            // reaches ROM.Data.Length.
            if (this.Recycle.Count <= 0)
            {
                return AllocFreeSpace(write_data);
            }

            int lastI = this.Recycle.Count - 1;
            Address lastP = this.Recycle[lastI];
            if (lastP.Addr + lastP.Length >= CoreState.ROM.Data.Length)
            {
                // Tail-resize special case (matches Write(_,undodata)).
                // Bail out on a failed resize (e.g., >32MB) so the caller
                // gets U.NOT_FOUND instead of a crashing write_range
                // (Copilot bot review on PR #634).
                uint newRomSize = U.Padding4(lastP.Addr + (uint)write_data.Length);
                if (!CoreState.ROM.write_resize_data(newRomSize))
                {
                    return U.NOT_FOUND;
                }
                CoreState.ROM.write_range(lastP.Addr, write_data);
                this.Recycle.RemoveAt(lastI);
                return lastP.Addr;
            }

            return AllocFreeSpace(write_data);
        }

        /// <summary>
        /// Find free space and write the bytes (no undo). Used by
        /// <see cref="WriteAmbient"/> when the recycle pool is exhausted.
        /// Uses rom.FindFreeSpace (upper half then lower half), and finally
        /// resizes the ROM. The active ambient undo scope captures the
        /// no-undo <c>rom.write_range</c> call. We deliberately do NOT call
        /// CoreState.AppendBinaryData here - that seam is WinForms-specific
        /// (its signature takes an explicit undodata which would double-record
        /// against the ambient scope, the very condition the Ambient-suffix
        /// methods exist to avoid).
        /// </summary>
        uint AllocFreeSpace(byte[] write_data)
        {
            var rom = CoreState.ROM;
            uint searchStart = (uint)(rom.Data.Length / 2);
            uint addr = rom.FindFreeSpace(searchStart, (uint)write_data.Length);
            if (addr == U.NOT_FOUND)
            {
                addr = rom.FindFreeSpace(0x100u, (uint)write_data.Length);
            }
            if (addr == U.NOT_FOUND)
            {
                // ROM is full - resize and place at the tail.
                uint newSize = U.Padding4((uint)rom.Data.Length + (uint)write_data.Length);
                if (newSize > 0x02000000u) return U.NOT_FOUND;
                uint tail = (uint)rom.Data.Length;
                if (!rom.write_resize_data(newSize)) return U.NOT_FOUND;
                addr = tail;
            }
            rom.write_range(addr, write_data);
            return addr;
        }

        /// <summary>
        /// No-undo BlackOut: clears every remaining recycle range with 0x00.
        /// The active ambient undo scope captures each fill once.
        /// </summary>
        public void BlackOutAmbient()
        {
            foreach (Address p in Recycle)
            {
                CoreState.ROM.write_fill(p.Addr, p.Length, 0x00);
            }
            this.Recycle.Clear();
        }

        public uint Write(byte[] write_data, Undo.UndoData undodata)
        {
            for (int i = 0; i < this.Recycle.Count; i++)
            {
                Address p = this.Recycle[i];
                if (p.Length >= write_data.Length)
                {
                    uint use_addr = p.Addr;
                    uint left_size = p.Length;
                    if (!U.isPadding4(use_addr))
                    {//padding4ではないと端数が出てしまうので補正する
                        if (left_size < 4)
                        {//空きがなさすぎるため利用しない
                            Log.Notify("アドレスが端数値なので補正しようとしましたが、サイズが4未満なので利用しません", U.To0xHexString(p.Addr));
                            continue;
                        }
                        uint diff = 4 - (use_addr % 4);
                        use_addr += diff;
                        left_size -= diff;
                        Debug.Assert(U.isPadding4(use_addr));
                        if (left_size < write_data.Length)
                        {//align 4補正したらサイズが足りん!
                            Log.Notify("アドレスが端数値なので補正しようとしたらサイズ不足になりました", U.To0xHexString(p.Addr));
                            continue;
                        }
                        Log.Notify("アドレスが端数値なので補正します。", U.To0xHexString(p.Addr));
                    }

                    //ちょうど良い領域があったので利用しよう
                    CoreState.ROM.write_range(use_addr, write_data, undodata);
                    uint next_addr = U.Padding4(use_addr + (uint)write_data.Length);
                    left_size = U.Sub(left_size, (next_addr - use_addr));

                    p.ResizeAddress(next_addr, left_size);
                    if (p.Length < 4)
                    {//もう空きがない.
                        this.Recycle.RemoveAt(i);
                    }

                    return use_addr;
                }
            }

            if (this.Recycle.Count <= 0)
            {
                //空き領域から利用.
                if (CoreState.AppendBinaryData != null)
                {
                    return CoreState.AppendBinaryData(write_data, undodata);
                }
                return U.NOT_FOUND;
            }
            else
            {
                int lasiI = this.Recycle.Count - 1;
                Address lastP = this.Recycle[lasiI];
                if (lastP.Addr + lastP.Length >= CoreState.ROM.Data.Length)
                {//自分が最後のデータだった場合
                    //ROMサイズを増設.
                    CoreState.ROM.write_resize_data(U.Padding4(lastP.Addr + (uint)write_data.Length));
                    CoreState.ROM.write_range(lastP.Addr, write_data, undodata);

                    this.Recycle.RemoveAt(lasiI);
                    return lastP.Addr;
                }

                //空き領域から利用.
                if (CoreState.AppendBinaryData != null)
                {
                    return CoreState.AppendBinaryData(write_data, undodata);
                }
                return U.NOT_FOUND;
            }
        }


        //もし、リサイクルできない端数が残ったら、それらは0x00で総クリアする
        public void BlackOut(Undo.UndoData undodata)
        {
            foreach(Address p in Recycle)
            {
                CoreState.ROM.write_fill(p.Addr, p.Length, 0x00, undodata);
            }
            this.Recycle.Clear();
        }
    }
}
