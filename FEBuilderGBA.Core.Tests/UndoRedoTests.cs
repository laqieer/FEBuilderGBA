using Xunit;
using FEBuilderGBA;

namespace FEBuilderGBA.Core.Tests
{
    /// <summary>
    /// Tests for the forward-step (redo) path on <see cref="Undo"/> added
    /// for #692 partial slice (Avalonia Map Style Editor Redo button).
    ///
    /// The tests verify:
    /// - <see cref="Undo.CanRedo"/> reports the cursor position correctly.
    /// - <see cref="Undo.RunRedo"/> replays a record and advances the cursor.
    /// - <see cref="Undo.RunRedo"/> returns false at the end of the buffer
    ///   (so the editor's "Nothing to redo" path fires).
    /// - <see cref="Undo.RunRedo"/> restores the post-write state when called
    ///   after a matching <see cref="Undo.RunUndo"/> (round-trip).
    /// </summary>
    [Collection("SharedState")]
    public class UndoRedoTests
    {
        static ROM CreateTestRom()
        {
            var rom = new ROM();
            rom.SwapNewROMDataDirect(new byte[256]);
            CoreState.ROM = rom;
            return rom;
        }

        /// <summary>
        /// Empty buffer + Postion 0 should NOT permit redo and RunRedo should
        /// return false (no rollback attempted).
        /// </summary>
        [Fact]
        public void CanRedo_EmptyStack_ReturnsFalse()
        {
            var undo = new Undo();
            Assert.False(undo.CanRedo);
            Assert.False(undo.RunRedo());
        }

        /// <summary>
        /// After a single Push, the cursor is at the end so CanRedo is false
        /// and RunRedo returns false (mirrors WF behavior — redo only fires
        /// after at least one undo step).
        /// </summary>
        [Fact]
        public void CanRedo_AtMaxPosition_ReturnsFalse()
        {
            var savedRom = CoreState.ROM;
            var savedUndo = CoreState.Undo;
            try
            {
                CreateTestRom();
                var undo = new Undo();
                CoreState.Undo = undo;

                var ud = undo.NewUndoData("test");
                ud.list.Add(new Undo.UndoPostion(0, new byte[] { 0xAA }));
                undo.Push(ud);

                // Postion == UndoBuffer.Count == 1 after Push → no redo possible.
                Assert.False(undo.CanRedo);
                Assert.False(undo.RunRedo());
            }
            finally
            {
                CoreState.ROM = savedRom;
                CoreState.Undo = savedUndo;
            }
        }

        /// <summary>
        /// Write a byte through Undo, then undo (cursor moves back to 0),
        /// then redo and assert the post-write byte is restored. This is the
        /// primary correctness guarantee for the Map Style Editor Redo
        /// button — without it, the user would see the pre-write byte after
        /// pressing Redo.
        /// </summary>
        [Fact]
        public void RunRedo_AfterUndo_RestoresOriginalState()
        {
            var savedRom = CoreState.ROM;
            var savedUndo = CoreState.Undo;
            try
            {
                var rom = CreateTestRom();
                var undo = new Undo();
                CoreState.Undo = undo;

                // Snapshot the pre-write byte (0x00) into an UndoData
                // record, mutate ROM directly to 0xAA, then Push so the
                // buffer holds the pre-write byte at Postion 1.
                var ud = undo.NewUndoData("test");
                ud.list.Add(new Undo.UndoPostion(addr: 0x10, size: 1));
                rom.write_u8(0x10, 0xAA);
                undo.Push(ud);

                Assert.Equal(0xAA, rom.Data[0x10]);
                Assert.Equal(1, undo.Postion);
                Assert.False(undo.CanRedo);

                // Undo → ROM byte returns to 0x00, cursor moves to 0.
                undo.RunUndo();
                Assert.Equal(0x00, rom.Data[0x10]);
                Assert.Equal(0, undo.Postion);
                Assert.True(undo.CanRedo);

                // Redo → ROM byte returns to 0xAA, cursor advances to 1,
                // and RunRedo reports success.
                bool redoOk = undo.RunRedo();
                Assert.True(redoOk);
                Assert.Equal(0xAA, rom.Data[0x10]);
                Assert.Equal(1, undo.Postion);
                Assert.False(undo.CanRedo);
            }
            finally
            {
                CoreState.ROM = savedRom;
                CoreState.Undo = savedUndo;
            }
        }

        /// <summary>
        /// Empty buffer scenario: with a real ROM but no Push, RunRedo
        /// must short-circuit on the CanRedo guard and not attempt any
        /// rollback (which would Debug.Assert on the underlying
        /// <see cref="Undo.Rollback(int)"/>).
        /// </summary>
        [Fact]
        public void RunRedo_EmptyStack_ReturnsFalse()
        {
            var savedRom = CoreState.ROM;
            var savedUndo = CoreState.Undo;
            try
            {
                CreateTestRom();
                var undo = new Undo();
                CoreState.Undo = undo;

                Assert.False(undo.RunRedo());
                Assert.Equal(0, undo.Postion);
            }
            finally
            {
                CoreState.ROM = savedRom;
                CoreState.Undo = savedUndo;
            }
        }
    }
}
