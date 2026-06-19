using FEBuilderGBA;
using FEBuilderGBA.Avalonia.ViewModels;
using Xunit;

namespace FEBuilderGBA.Avalonia.Tests
{
    /// <summary>
    /// Unit tests for <see cref="ToolCustomBuildViewModel"/>, the form-field holder for
    /// the Avalonia "Custom Build" tool. The build/load work itself lives in the
    /// GUI-free Core helper <c>CustomBuildCore</c> (covered by CustomBuildCoreTests and
    /// NOT re-run here — a real build needs an external toolchain not present in CI).
    /// These tests cover the VM's pure field-mapping logic: the index→enum projection
    /// (with clamping) and the file-exists guards.
    /// </summary>
    public class ToolCustomBuildViewModelTests
    {
        [Fact]
        public void Defaults_AreEmptyAndAuto()
        {
            var vm = new ToolCustomBuildViewModel();

            Assert.Equal(0, vm.BuildMethodIndex);            // Auto
            Assert.Equal(CustomBuildCore.BuildMethod.Auto, vm.BuildMethod);
            Assert.Equal("", vm.TargetPath);
            Assert.Equal("", vm.OriginalRomPath);
            Assert.False(vm.CanUndo);
            Assert.False(vm.TargetExists);
            Assert.False(vm.OriginalRomExists);
        }

        [Theory]
        [InlineData(0, CustomBuildCore.BuildMethod.Auto)]
        [InlineData(1, CustomBuildCore.BuildMethod.Cmd)]
        [InlineData(2, CustomBuildCore.BuildMethod.EventAssembler)]
        public void BuildMethod_MapsFromIndex(int idx, CustomBuildCore.BuildMethod expected)
        {
            var vm = new ToolCustomBuildViewModel { BuildMethodIndex = idx };
            Assert.Equal(expected, vm.BuildMethod);
        }

        // A ComboBox reports -1 (no selection) and could be out of range; the
        // index→enum cast must clamp to a defined enum, not produce garbage.
        [Theory]
        [InlineData(-1, CustomBuildCore.BuildMethod.Auto)]            // no selection → first (safe)
        [InlineData(99, CustomBuildCore.BuildMethod.EventAssembler)]  // out of range → max
        public void BuildMethod_ClampsOutOfRangeIndices(int badIdx, CustomBuildCore.BuildMethod expected)
        {
            var vm = new ToolCustomBuildViewModel { BuildMethodIndex = badIdx };
            Assert.True(System.Enum.IsDefined(typeof(CustomBuildCore.BuildMethod), vm.BuildMethod));
            Assert.Equal(expected, vm.BuildMethod);
        }

        [Fact]
        public void TargetExists_FalseForBlankOrMissingPath()
        {
            var vm = new ToolCustomBuildViewModel();
            Assert.False(vm.TargetExists);

            vm.TargetPath = "Z:\\does\\not\\exist\\CUSTOM_BUILD.cmd";
            Assert.False(vm.TargetExists);
        }

        [Fact]
        public void OriginalRomExists_FalseForBlankOrMissingPath()
        {
            var vm = new ToolCustomBuildViewModel();
            Assert.False(vm.OriginalRomExists);

            vm.OriginalRomPath = "Z:\\does\\not\\exist\\base.gba";
            Assert.False(vm.OriginalRomExists);
        }

        // ---- TakeoverSkillAssignment (Marge and Update, #1248 slice 2) ----------

        [Fact]
        public void TakeoverSkillAssignment_DefaultsToCarryOver()
        {
            // Matches the WF form (TakeoverSkillAssignmentComboBox.SelectedIndex = 1).
            var vm = new ToolCustomBuildViewModel();
            Assert.Equal(1, vm.TakeoverSkillAssignmentIndex);
            Assert.Equal(1u, vm.TakeoverSkillAssignment);
        }

        [Theory]
        [InlineData(0, 0u)]   // do not carry over
        [InlineData(1, 1u)]   // carry over
        public void TakeoverSkillAssignment_MapsFromIndex(int idx, uint expected)
        {
            var vm = new ToolCustomBuildViewModel { TakeoverSkillAssignmentIndex = idx };
            Assert.Equal(expected, vm.TakeoverSkillAssignment);
        }

        [Theory]
        [InlineData(-1, 0u)]  // no selection → 0 (do not carry over)
        [InlineData(99, 1u)]  // out of range → clamps to 1 (carry over)
        public void TakeoverSkillAssignment_ClampsOutOfRangeIndices(int badIdx, uint expected)
        {
            var vm = new ToolCustomBuildViewModel { TakeoverSkillAssignmentIndex = badIdx };
            Assert.Equal(expected, vm.TakeoverSkillAssignment);
        }
    }
}
