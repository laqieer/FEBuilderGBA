using Xunit;

namespace FEBuilderGBA.Avalonia.Tests
{
    /// <summary>
    /// xUnit collection definition that ensures all tests accessing CoreState
    /// run sequentially (no parallel execution) and share a single RomFixture.
    /// </summary>
    [CollectionDefinition("SharedState")]
    public class SharedStateCollection : ICollectionFixture<RomFixture>
    {
        // This class has no code; it only anchors the collection definition.
    }
}
