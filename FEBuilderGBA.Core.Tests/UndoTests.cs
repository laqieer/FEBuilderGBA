using Xunit;
using FEBuilderGBA;

namespace FEBuilderGBA.Core.Tests
{
    [Collection("SharedState")]
    public class UndoTests
    {
        static ROM CreateTestRom()
        {
            var rom = new ROM();
            rom.SwapNewROMDataDirect(new byte[256]);
            CoreState.ROM = rom;
            return rom;
        }

        [Fact]
        public void Undo_CanCreate()
        {
            var undo = new Undo();
            Assert.NotNull(undo);
        }

        [Fact]
        public void IsModified_FalseWhenNoChanges()
        {
            var undo = new Undo();
            // Fresh undo: Postion == 0, PostionWhenFileSaving == 0
            Assert.False(undo.IsModified);
        }

        [Fact]
        public void IsModified_TrueAfterPush()
        {
            var savedRom = CoreState.ROM;
            try
            {
                CreateTestRom();

                var undo = new Undo();
                var ud = undo.NewUndoData("test");
                ud.list.Add(new Undo.UndoPostion(0, new byte[] { 0xFF }));
                undo.Push(ud);

                Assert.True(undo.IsModified);
            }
            finally
            {
                CoreState.ROM = savedRom;
            }
        }

        [Fact]
        public void IsModified_FalseAfterUndoAllChanges()
        {
            var savedRom = CoreState.ROM;
            var savedUndo = CoreState.Undo;
            try
            {
                CreateTestRom();

                var undo = new Undo();
                CoreState.Undo = undo;

                var ud = undo.NewUndoData("test");
                ud.list.Add(new Undo.UndoPostion(0, new byte[] { 0xFF }));
                undo.Push(ud);
                Assert.True(undo.IsModified);

                // Undo the single change — should return to saved state
                undo.RunUndo();
                Assert.False(undo.IsModified);
            }
            finally
            {
                CoreState.ROM = savedRom;
                CoreState.Undo = savedUndo;
            }
        }

        // ---- MarkFileSaved / save-clean dirty tracking (#1914) ----

        static void PushEdit(Undo undo, uint addr, byte val)
        {
            var ud = undo.NewUndoData("test");
            ud.list.Add(new Undo.UndoPostion(addr, new byte[] { val }));
            undo.Push(ud);
        }

        [Fact]
        public void MarkFileSaved_ClearsDirty_ThenNextEditSetsDirtyAgain()
        {
            var savedRom = CoreState.ROM;
            var savedUndo = CoreState.Undo;
            try
            {
                CreateTestRom();
                var undo = new Undo();
                CoreState.Undo = undo;

                PushEdit(undo, 0, 0x11);
                Assert.True(undo.IsModified);

                // Save: the current position becomes the on-disk saved position.
                undo.MarkFileSaved();
                Assert.False(undo.IsModified);

                // A further edit makes it dirty again.
                PushEdit(undo, 1, 0x22);
                Assert.True(undo.IsModified);
            }
            finally
            {
                CoreState.ROM = savedRom;
                CoreState.Undo = savedUndo;
            }
        }

        [Fact]
        public void MarkFileSaved_ThenUndoToBeforeSave_IsDirty_ThenRedoIsClean()
        {
            var savedRom = CoreState.ROM;
            var savedUndo = CoreState.Undo;
            try
            {
                CreateTestRom();
                var undo = new Undo();
                CoreState.Undo = undo;

                PushEdit(undo, 0, 0x11);
                PushEdit(undo, 1, 0x22);
                undo.MarkFileSaved();      // saved at position 2
                Assert.False(undo.IsModified);

                undo.RunUndo();            // back to position 1 (before the save point)
                Assert.True(undo.IsModified);

                undo.RunRedo();            // forward to position 2 (the save point)
                Assert.False(undo.IsModified);
            }
            finally
            {
                CoreState.ROM = savedRom;
                CoreState.Undo = savedUndo;
            }
        }

        [Fact]
        public void MarkFileSaved_ThenUndoAndDivergentEdit_StaysDirty()
        {
            // Regression guard for #1914: after Save, undoing and then making a
            // DIFFERENT edit discards the saved snapshot from the redo branch. The
            // saved-position marker must be invalidated so IsModified stays true —
            // otherwise the close prompt is suppressed on genuinely unsaved work.
            var savedRom = CoreState.ROM;
            var savedUndo = CoreState.Undo;
            try
            {
                CreateTestRom();
                var undo = new Undo();
                CoreState.Undo = undo;

                PushEdit(undo, 0, 0x11); // A
                PushEdit(undo, 1, 0x22); // B
                PushEdit(undo, 2, 0x33); // C  -> position 3
                undo.MarkFileSaved();    // saved at position 3
                Assert.False(undo.IsModified);

                undo.RunUndo();          // back to position 2 (A,B) -> dirty
                Assert.True(undo.IsModified);

                PushEdit(undo, 2, 0x44); // D  -> discards C from the redo branch
                // Content is now A,B,D but position is again 3; without the marker
                // invalidation this would falsely report clean.
                Assert.True(undo.IsModified);
            }
            finally
            {
                CoreState.ROM = savedRom;
                CoreState.Undo = savedUndo;
            }
        }
    }
}
