// SPDX-License-Identifier: GPL-3.0-or-later
// IAsmMapCache.IsHardCodeItem default-method contract (#409).
//
// The IAsmMapCache interface was extended with `IsHardCodeItem(uint itemId)`
// so the Avalonia ItemEditorView can render a HardCoding warning label that
// matches WinForms ItemForm. Legacy implementors that do not override the
// method must keep compiling and return false (no false positives).
using Xunit;
using FEBuilderGBA;

namespace FEBuilderGBA.Core.Tests
{
    public class AsmMapCacheHardCodeItemTests
    {
        /// <summary>
        /// A minimal IAsmMapCache implementation that does NOT override the
        /// new IsHardCodeItem method — exercises the default interface method.
        /// </summary>
        sealed class LegacyCacheStub : IAsmMapCache
        {
            public void ClearCache() { }
            public bool IsHardCodeUnit(uint unitId) => false;
        }

        /// <summary>
        /// An implementor that overrides IsHardCodeItem with patch-specific
        /// hardcoding state.
        /// </summary>
        sealed class HardCodedCacheStub : IAsmMapCache
        {
            public void ClearCache() { }
            public bool IsHardCodeUnit(uint unitId) => unitId == 1;
            public bool IsHardCodeItem(uint itemId) => itemId == 0x2A;
        }

        [Fact]
        public void IsHardCodeItem_LegacyCache_DefaultsToFalse()
        {
            IAsmMapCache cache = new LegacyCacheStub();
            // Default interface method must keep returning false so heads
            // without ASM-map data degrade gracefully.
            Assert.False(cache.IsHardCodeItem(0));
            Assert.False(cache.IsHardCodeItem(42));
            Assert.False(cache.IsHardCodeItem(0xFF));
        }

        [Fact]
        public void IsHardCodeItem_OverriddenCache_DelegatesToImplementation()
        {
            IAsmMapCache cache = new HardCodedCacheStub();
            Assert.True(cache.IsHardCodeItem(0x2A));
            Assert.False(cache.IsHardCodeItem(0x29));
            Assert.False(cache.IsHardCodeItem(0));
        }
    }
}
