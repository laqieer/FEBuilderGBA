using System;
using System.Collections.Generic;
using System.IO;
using Xunit;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Core.Tests
{
    /// <summary>
    /// Tests for AI and Procs script category select ViewModels.
    /// Verifies category loading, script listing, filtering, and selection.
    /// </summary>
    [Collection("SharedState")]
    public class AIProcsScriptViewModelTests : IDisposable
    {
        readonly EventScript? _savedAI;
        readonly EventScript? _savedProcs;

        public AIProcsScriptViewModelTests()
        {
            _savedAI = CoreState.AIScript;
            _savedProcs = CoreState.ProcsScript;
        }

        public void Dispose()
        {
            CoreState.AIScript = _savedAI;
            CoreState.ProcsScript = _savedProcs;
        }

        [Fact]
        public void LoadTSVResource_EmptyFile_ReturnsEmptyDic()
        {
            string tmp = Path.GetTempFileName();
            try
            {
                File.WriteAllText(tmp, "");
                var dic = AIScriptCategorySelectViewModel.LoadTSVResource(tmp);
                Assert.Empty(dic);
            }
            finally
            {
                File.Delete(tmp);
            }
        }

        [Fact]
        public void LoadTSVResource_ParsesTSVCorrectly()
        {
            string tmp = Path.GetTempFileName();
            try
            {
                // TSV format: value<tab>key  => dic[sp[1]] = sp[0]
                File.WriteAllText(tmp, "MOVE\tMovement\nATTACK\tCombat\n");
                var dic = AIScriptCategorySelectViewModel.LoadTSVResource(tmp);
                Assert.Equal(2, dic.Count);
                Assert.Equal("MOVE", dic["Movement"]);
                Assert.Equal("ATTACK", dic["Combat"]);
            }
            finally
            {
                File.Delete(tmp);
            }
        }

        [Fact]
        public void LoadTSVResource_SkipsComments()
        {
            string tmp = Path.GetTempFileName();
            try
            {
                File.WriteAllText(tmp, "# comment line\nMOVE\tMovement\n//another comment\n");
                var dic = AIScriptCategorySelectViewModel.LoadTSVResource(tmp);
                Assert.Single(dic);
                Assert.Equal("MOVE", dic["Movement"]);
            }
            finally
            {
                File.Delete(tmp);
            }
        }

        [Fact]
        public void LoadTSVResource_SkipsSingleColumnLines()
        {
            string tmp = Path.GetTempFileName();
            try
            {
                File.WriteAllText(tmp, "noTab\nMOVE\tMovement\n");
                var dic = AIScriptCategorySelectViewModel.LoadTSVResource(tmp);
                Assert.Single(dic);
            }
            finally
            {
                File.Delete(tmp);
            }
        }

        [Fact]
        public void LoadTSVResource_NonexistentFile_ReturnsEmptyDic()
        {
            var dic = AIScriptCategorySelectViewModel.LoadTSVResource("/nonexistent/path.tsv");
            Assert.Empty(dic);
        }

        [Fact]
        public void AIScriptCategorySelect_Load_WithNullScript_LoadsGracefully()
        {
            CoreState.AIScript = null;
            var vm = new AIScriptCategorySelectViewModel();
            vm.Load();
            Assert.True(vm.IsLoaded);
            Assert.True(vm.Categories.Count > 0);
        }

        [Fact]
        public void ProcsScriptCategorySelect_Load_WithNullScript_LoadsGracefully()
        {
            CoreState.ProcsScript = null;
            var vm = new ProcsScriptCategorySelectViewModel();
            vm.Load();
            Assert.True(vm.IsLoaded);
            Assert.True(vm.Categories.Count > 0);
        }

        [Fact]
        public void AIScriptCategorySelect_ConfirmSelection_NoSelection_ReturnsFalse()
        {
            CoreState.AIScript = null;
            var vm = new AIScriptCategorySelectViewModel();
            vm.Load();
            Assert.False(vm.ConfirmSelection());
        }

        [Fact]
        public void ProcsScriptCategorySelect_ConfirmSelection_NoSelection_ReturnsFalse()
        {
            CoreState.ProcsScript = null;
            var vm = new ProcsScriptCategorySelectViewModel();
            vm.Load();
            Assert.False(vm.ConfirmSelection());
        }

        [Fact]
        public void AIScriptCategorySelect_FilterText_DoesNotThrow()
        {
            CoreState.AIScript = null;
            var vm = new AIScriptCategorySelectViewModel();
            vm.Load();
            vm.FilterText = "MOVE";
            Assert.Equal("MOVE", vm.FilterText);
            Assert.NotNull(vm.ScriptNames);
        }

        [Fact]
        public void ProcsScriptCategorySelect_FilterText_DoesNotThrow()
        {
            CoreState.ProcsScript = null;
            var vm = new ProcsScriptCategorySelectViewModel();
            vm.Load();
            vm.FilterText = "PROC";
            Assert.Equal("PROC", vm.FilterText);
            Assert.NotNull(vm.ScriptNames);
        }

        [Fact]
        public void AIScriptCategorySelect_SelectedScriptIndex_NegativeOne_ClearsInfo()
        {
            CoreState.AIScript = null;
            var vm = new AIScriptCategorySelectViewModel();
            vm.Load();
            vm.SelectedScriptIndex = -1;
            Assert.Equal("", vm.InfoText);
            Assert.Null(vm.SelectedScript);
        }

        [Fact]
        public void ProcsScriptCategorySelect_SelectedScriptIndex_NegativeOne_ClearsInfo()
        {
            CoreState.ProcsScript = null;
            var vm = new ProcsScriptCategorySelectViewModel();
            vm.Load();
            vm.SelectedScriptIndex = -1;
            Assert.Equal("", vm.InfoText);
            Assert.Null(vm.SelectedScript);
        }

        [Fact]
        public void AIScriptCategorySelect_SelectedCategory_RefreshesScriptList()
        {
            CoreState.AIScript = null;
            var vm = new AIScriptCategorySelectViewModel();
            vm.Load();

            // Changing category should not crash
            if (vm.Categories.Count > 0)
            {
                vm.SelectedCategory = vm.Categories[0];
                Assert.NotNull(vm.ScriptNames);
            }
        }

        [Fact]
        public void ProcsScriptCategorySelect_SelectedCategory_RefreshesScriptList()
        {
            CoreState.ProcsScript = null;
            var vm = new ProcsScriptCategorySelectViewModel();
            vm.Load();

            if (vm.Categories.Count > 0)
            {
                vm.SelectedCategory = vm.Categories[0];
                Assert.NotNull(vm.ScriptNames);
            }
        }

        [Fact]
        public void AIScriptCategorySelect_ScriptNames_EmptyWhenNoScripts()
        {
            CoreState.AIScript = null;
            var vm = new AIScriptCategorySelectViewModel();
            vm.Load();
            Assert.Empty(vm.ScriptNames);
        }

        [Fact]
        public void ProcsScriptCategorySelect_ScriptNames_EmptyWhenNoScripts()
        {
            CoreState.ProcsScript = null;
            var vm = new ProcsScriptCategorySelectViewModel();
            vm.Load();
            Assert.Empty(vm.ScriptNames);
        }

        [Fact]
        public void AIScriptCategorySelect_OutOfBoundsIndex_ClearsInfo()
        {
            CoreState.AIScript = null;
            var vm = new AIScriptCategorySelectViewModel();
            vm.Load();
            vm.SelectedScriptIndex = 999;
            Assert.Equal("", vm.InfoText);
            Assert.Null(vm.SelectedScript);
        }

        [Fact]
        public void ProcsScriptCategorySelect_OutOfBoundsIndex_ClearsInfo()
        {
            CoreState.ProcsScript = null;
            var vm = new ProcsScriptCategorySelectViewModel();
            vm.Load();
            vm.SelectedScriptIndex = 999;
            Assert.Equal("", vm.InfoText);
            Assert.Null(vm.SelectedScript);
        }
    }
}
