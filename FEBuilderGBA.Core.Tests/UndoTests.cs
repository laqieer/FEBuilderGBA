using Xunit;
using FEBuilderGBA;

namespace FEBuilderGBA.Core.Tests
{
    [Collection("SharedState")]
    public class UndoTests
    {
        [Fact]
        public void Undo_CanCreate()
        {
            var undo = new Undo();
            Assert.NotNull(undo);
        }
    }
}
