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
    }
}
