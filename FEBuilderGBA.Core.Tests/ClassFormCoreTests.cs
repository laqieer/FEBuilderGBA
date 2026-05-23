// SPDX-License-Identifier: GPL-3.0-or-later
// Core tests for ClassFormCore.SetSimClass (#428).
//
// ClassFormCore.SetSimClass extracts the class-base / class-grow / magic-ext
// inputs out of a class entry in the ROM table and feeds them into a
// GrowSimulator. The WinForms ClassForm.SetSimClass delegates to this helper
// so both Avalonia and WinForms share one source of truth for sim parity.
using FEBuilderGBA.Core;
using Xunit;

namespace FEBuilderGBA.Core.Tests
{
    [Collection("SharedState")]
    public class ClassFormCoreTests
    {
        [Fact]
        public void SetSimClass_NullRom_LeavesSimZero()
        {
            var sim = new GrowSimulator();
            ClassFormCore.SetSimClass(ref sim, 1, null);
            Assert.Equal(0, sim.class_hp);
            Assert.Equal(0, sim.class_str);
            Assert.Equal(0, sim.class_grow_hp);
        }

        [Fact]
        public void SetSimClass_ClassIdZero_LeavesSimZero()
        {
            var sim = new GrowSimulator();
            ClassFormCore.SetSimClass(ref sim, 0, null);
            Assert.Equal(0, sim.class_hp);
            Assert.Equal(0, sim.class_grow_hp);
        }
    }
}
