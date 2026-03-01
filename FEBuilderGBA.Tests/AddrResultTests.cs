namespace FEBuilderGBA.Tests
{
    /// <summary>
    /// Tests for the top-level AddrResult class (extracted from U.AddrResult).
    /// Verifies that the type is accessible from both Core and WinForms assemblies.
    /// </summary>
    public class AddrResultTests
    {
        [Fact]
        public void AddrResult_DefaultConstructor_InitializesDefaults()
        {
            var ar = new AddrResult();
            Assert.Equal(0u, ar.addr);
            Assert.Null(ar.name);
            Assert.Equal(0u, ar.tag);
        }

        [Fact]
        public void AddrResult_TwoArgConstructor_SetsAddrAndName()
        {
            var ar = new AddrResult(0x100u, "TestEntry");
            Assert.Equal(0x100u, ar.addr);
            Assert.Equal("TestEntry", ar.name);
            Assert.Equal(0u, ar.tag);
        }

        [Fact]
        public void AddrResult_ThreeArgConstructor_SetsAll()
        {
            var ar = new AddrResult(0x200u, "Tagged", 42u);
            Assert.Equal(0x200u, ar.addr);
            Assert.Equal("Tagged", ar.name);
            Assert.Equal(42u, ar.tag);
        }

        [Fact]
        public void IsNULL_ReturnsTrueWhenAddrZero()
        {
            var ar = new AddrResult(0u, "name");
            Assert.True(ar.isNULL());
        }

        [Fact]
        public void IsNULL_ReturnsTrueWhenNameNull()
        {
            var ar = new AddrResult(1u, null!);
            Assert.True(ar.isNULL());
        }

        [Fact]
        public void IsNULL_ReturnsFalseWhenBothSet()
        {
            var ar = new AddrResult(1u, "name");
            Assert.False(ar.isNULL());
        }

        [Fact]
        public void AddrResult_IsTopLevelPublicType()
        {
            // Verify AddrResult is a top-level class in FEBuilderGBA namespace
            var type = typeof(AddrResult);
            Assert.Equal("FEBuilderGBA", type.Namespace);
            Assert.Null(type.DeclaringType); // Not nested
            Assert.True(type.IsPublic);
        }

        [Fact]
        public void AddrResult_SameTypeAcrossAssemblies()
        {
            // Both Core (EtcCacheFLag) and WinForms (InputFormRef) should use
            // the same AddrResult type from Core assembly.
            var coreType = typeof(FEBuilderGBA.AddrResult);
            Assert.Equal("FEBuilderGBA.Core", coreType.Assembly.GetName().Name);
        }

        [Fact]
        public void FindList_FindsByAddr()
        {
            var list = new List<AddrResult>
            {
                new AddrResult(10u, "A"),
                new AddrResult(20u, "B"),
                new AddrResult(30u, "C"),
            };
            Assert.Equal(1u, U.FindList(list, 20u));
        }

        [Fact]
        public void FindList_ReturnsNotFoundForMissingAddr()
        {
            var list = new List<AddrResult>
            {
                new AddrResult(10u, "A"),
            };
            Assert.Equal(U.NOT_FOUND, U.FindList(list, 99u));
        }

        [Fact]
        public void FindList_FindsByName()
        {
            var list = new List<AddrResult>
            {
                new AddrResult(10u, "Alpha"),
                new AddrResult(20u, "Beta"),
            };
            Assert.Equal(1u, U.FindList(list, "Beta"));
        }

        [Fact]
        public void FindList_ReturnsNotFoundForMissingName()
        {
            var list = new List<AddrResult>
            {
                new AddrResult(10u, "Alpha"),
            };
            Assert.Equal(U.NOT_FOUND, U.FindList(list, "Gamma"));
        }
    }
}
