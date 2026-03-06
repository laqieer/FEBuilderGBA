using System.IO;
using Xunit;

namespace FEBuilderGBA.Tests.Unit
{
    /// <summary>
    /// Source-code verification tests for the 13 Avalonia dialog/tool views
    /// converted from stubs in WU9 (Map dialogs) and WU10 (Hex/DisASM dialogs).
    /// Verifies each view has proper ViewModel wiring, button handlers, and UI.
    /// </summary>
    public class AvaloniaDialogViewTests
    {
        private static string SolutionDir
        {
            get
            {
                var dir = AppContext.BaseDirectory;
                while (dir != null && !File.Exists(Path.Combine(dir, "FEBuilderGBA.sln")))
                    dir = Path.GetDirectoryName(dir);
                return dir ?? throw new InvalidOperationException("Cannot find solution root");
            }
        }

        private string AvaloniaDir => Path.Combine(SolutionDir, "FEBuilderGBA.Avalonia");
        private string ReadVM(string name) => File.ReadAllText(Path.Combine(AvaloniaDir, "ViewModels", name));
        private string ReadView(string name) => File.ReadAllText(Path.Combine(AvaloniaDir, "Views", name));
        private string ReadAxaml(string name) => File.ReadAllText(Path.Combine(AvaloniaDir, "Views", name));

        // ===========================================================================
        // WU9: Map Sub-Dialog Views
        // ===========================================================================

        // --- MapEditorAddMapChangeDialogView ---

        [Fact]
        public void MapEditorAddMapChangeDialog_VM_HasIsLoaded()
        {
            var src = ReadVM("MapEditorAddMapChangeDialogViewModel.cs");
            Assert.Contains("bool IsLoaded", src);
            Assert.Contains("SetField(ref _isLoaded", src);
        }

        [Fact]
        public void MapEditorAddMapChangeDialog_VM_HasMapChangeIdProperty()
        {
            var src = ReadVM("MapEditorAddMapChangeDialogViewModel.cs");
            Assert.Contains("uint MapChangeId", src);
        }

        [Fact]
        public void MapEditorAddMapChangeDialog_View_DelegatesIsLoaded()
        {
            var src = ReadView("MapEditorAddMapChangeDialogView.axaml.cs");
            Assert.Contains("_vm.IsLoaded", src);
            Assert.Contains("IDataVerifiableView", src);
        }

        [Fact]
        public void MapEditorAddMapChangeDialog_View_HasOKCancelHandlers()
        {
            var src = ReadView("MapEditorAddMapChangeDialogView.axaml.cs");
            Assert.Contains("OK_Click", src);
            Assert.Contains("Cancel_Click", src);
        }

        [Fact]
        public void MapEditorAddMapChangeDialog_Axaml_HasNumericInput()
        {
            var src = ReadAxaml("MapEditorAddMapChangeDialogView.axaml");
            Assert.Contains("MapChangeIdInput", src);
            Assert.Contains("Click=\"OK_Click\"", src);
            Assert.Contains("Click=\"Cancel_Click\"", src);
            Assert.DoesNotContain("not yet implemented", src);
        }

        // --- MapEditorMarSizeDialogView ---

        [Fact]
        public void MapEditorMarSizeDialog_VM_HasWidthHeight()
        {
            var src = ReadVM("MapEditorMarSizeDialogViewModel.cs");
            Assert.Contains("uint Width", src);
            Assert.Contains("uint Height", src);
            Assert.Contains("SetField(ref _isLoaded", src);
        }

        [Fact]
        public void MapEditorMarSizeDialog_View_DelegatesIsLoaded()
        {
            var src = ReadView("MapEditorMarSizeDialogView.axaml.cs");
            Assert.Contains("_vm.IsLoaded", src);
            Assert.Contains("IDataVerifiableView", src);
        }

        [Fact]
        public void MapEditorMarSizeDialog_Axaml_HasInputFields()
        {
            var src = ReadAxaml("MapEditorMarSizeDialogView.axaml");
            Assert.Contains("WidthInput", src);
            Assert.Contains("HeightInput", src);
            Assert.Contains("Click=\"OK_Click\"", src);
            Assert.DoesNotContain("not yet implemented", src);
        }

        // --- MapEditorResizeDialogView ---

        [Fact]
        public void MapEditorResizeDialog_VM_HasNewWidthHeight()
        {
            var src = ReadVM("MapEditorResizeDialogViewModel.cs");
            Assert.Contains("uint NewWidth", src);
            Assert.Contains("uint NewHeight", src);
        }

        [Fact]
        public void MapEditorResizeDialog_View_DelegatesIsLoaded()
        {
            var src = ReadView("MapEditorResizeDialogView.axaml.cs");
            Assert.Contains("_vm.IsLoaded", src);
            Assert.Contains("IDataVerifiableView", src);
        }

        [Fact]
        public void MapEditorResizeDialog_Axaml_HasInputFields()
        {
            var src = ReadAxaml("MapEditorResizeDialogView.axaml");
            Assert.Contains("NewWidthInput", src);
            Assert.Contains("NewHeightInput", src);
            Assert.Contains("Click=\"OK_Click\"", src);
            Assert.DoesNotContain("not yet implemented", src);
        }

        // --- MapPointerNewPLISTPopupView ---

        [Fact]
        public void MapPointerNewPLISTPopup_VM_HasPlistId()
        {
            var src = ReadVM("MapPointerNewPLISTPopupViewModel.cs");
            Assert.Contains("uint PlistId", src);
            Assert.Contains("SetField(ref _isLoaded", src);
        }

        [Fact]
        public void MapPointerNewPLISTPopup_View_DelegatesIsLoaded()
        {
            var src = ReadView("MapPointerNewPLISTPopupView.axaml.cs");
            Assert.Contains("_vm.IsLoaded", src);
            Assert.Contains("IDataVerifiableView", src);
        }

        [Fact]
        public void MapPointerNewPLISTPopup_Axaml_HasPlistInput()
        {
            var src = ReadAxaml("MapPointerNewPLISTPopupView.axaml");
            Assert.Contains("PlistIdInput", src);
            Assert.Contains("Click=\"OK_Click\"", src);
            Assert.DoesNotContain("not yet implemented", src);
        }

        // --- MapSettingDifficultyDialogView ---

        [Fact]
        public void MapSettingDifficultyDialog_VM_HasDifficultyProperties()
        {
            var src = ReadVM("MapSettingDifficultyDialogViewModel.cs");
            Assert.Contains("uint DifficultyLevel", src);
            Assert.Contains("uint EnemyLevelBonus", src);
            Assert.Contains("bool HardModeEnabled", src);
        }

        [Fact]
        public void MapSettingDifficultyDialog_View_DelegatesIsLoaded()
        {
            var src = ReadView("MapSettingDifficultyDialogView.axaml.cs");
            Assert.Contains("_vm.IsLoaded", src);
            Assert.Contains("IDataVerifiableView", src);
        }

        [Fact]
        public void MapSettingDifficultyDialog_Axaml_HasControls()
        {
            var src = ReadAxaml("MapSettingDifficultyDialogView.axaml");
            Assert.Contains("DifficultyLevelInput", src);
            Assert.Contains("EnemyLevelBonusInput", src);
            Assert.Contains("HardModeCheckBox", src);
            Assert.Contains("Click=\"OK_Click\"", src);
            Assert.DoesNotContain("not yet implemented", src);
        }

        // --- MapStyleEditorAppendPopupView ---

        [Fact]
        public void MapStyleEditorAppendPopup_VM_HasConfirmed()
        {
            var src = ReadVM("MapStyleEditorAppendPopupViewModel.cs");
            Assert.Contains("bool Confirmed", src);
            Assert.Contains("SetField(ref _isLoaded", src);
        }

        [Fact]
        public void MapStyleEditorAppendPopup_View_DelegatesIsLoaded()
        {
            var src = ReadView("MapStyleEditorAppendPopupView.axaml.cs");
            Assert.Contains("_vm.IsLoaded", src);
            Assert.Contains("IDataVerifiableView", src);
        }

        [Fact]
        public void MapStyleEditorAppendPopup_Axaml_HasButtons()
        {
            var src = ReadAxaml("MapStyleEditorAppendPopupView.axaml");
            Assert.Contains("Click=\"OK_Click\"", src);
            Assert.Contains("Click=\"Cancel_Click\"", src);
            Assert.DoesNotContain("not yet implemented", src);
        }

        // --- MapStyleEditorImportImageOptionView ---

        [Fact]
        public void MapStyleEditorImportImageOption_VM_HasSelectedOption()
        {
            var src = ReadVM("MapStyleEditorImportImageOptionViewModel.cs");
            Assert.Contains("int SelectedOption", src);
            Assert.Contains("SetField(ref _isLoaded", src);
        }

        [Fact]
        public void MapStyleEditorImportImageOption_View_DelegatesIsLoaded()
        {
            var src = ReadView("MapStyleEditorImportImageOptionView.axaml.cs");
            Assert.Contains("_vm.IsLoaded", src);
            Assert.Contains("IDataVerifiableView", src);
        }

        [Fact]
        public void MapStyleEditorImportImageOption_Axaml_HasRadioButtons()
        {
            var src = ReadAxaml("MapStyleEditorImportImageOptionView.axaml");
            Assert.Contains("ReplaceOption", src);
            Assert.Contains("AppendOption", src);
            Assert.Contains("InsertOption", src);
            Assert.Contains("Click=\"OK_Click\"", src);
            Assert.DoesNotContain("not yet implemented", src);
        }

        // --- MapStyleEditorWarningOverrideView ---

        [Fact]
        public void MapStyleEditorWarningOverride_VM_HasWarningMessage()
        {
            var src = ReadVM("MapStyleEditorWarningOverrideViewModel.cs");
            Assert.Contains("string WarningMessage", src);
            Assert.Contains("SetField(ref _isLoaded", src);
        }

        [Fact]
        public void MapStyleEditorWarningOverride_View_DelegatesIsLoaded()
        {
            var src = ReadView("MapStyleEditorWarningOverrideView.axaml.cs");
            Assert.Contains("_vm.IsLoaded", src);
            Assert.Contains("IDataVerifiableView", src);
            Assert.Contains("_vm.WarningMessage", src);
        }

        [Fact]
        public void MapStyleEditorWarningOverride_Axaml_HasWarningUI()
        {
            var src = ReadAxaml("MapStyleEditorWarningOverrideView.axaml");
            Assert.Contains("WarningText", src);
            Assert.Contains("Click=\"OK_Click\"", src);
            Assert.Contains("Click=\"Cancel_Click\"", src);
            Assert.DoesNotContain("not yet implemented", src);
        }

        // ===========================================================================
        // WU10: Hex/Disasm Sub-Dialog Views
        // ===========================================================================

        // --- HexEditorJumpView ---

        [Fact]
        public void HexEditorJump_VM_HasAddress()
        {
            var src = ReadVM("HexEditorJumpViewModel.cs");
            Assert.Contains("string Address", src);
            Assert.Contains("SetField(ref _isLoaded", src);
        }

        [Fact]
        public void HexEditorJump_View_DelegatesIsLoaded()
        {
            var src = ReadView("HexEditorJumpView.axaml.cs");
            Assert.Contains("_vm.IsLoaded", src);
            Assert.Contains("IDataVerifiableView", src);
        }

        [Fact]
        public void HexEditorJump_Axaml_HasAddressInput()
        {
            var src = ReadAxaml("HexEditorJumpView.axaml");
            Assert.Contains("AddrComboBox", src);
            Assert.Contains("LittleEndianCheckBox", src);
            Assert.Contains("Click=\"OK_Click\"", src);
            Assert.DoesNotContain("not yet implemented", src);
        }

        // --- HexEditorMarkView ---

        [Fact]
        public void HexEditorMark_VM_HasMarksCollection()
        {
            var src = ReadVM("HexEditorMarkViewModel.cs");
            Assert.Contains("ObservableCollection<string> Marks", src);
            Assert.Contains("AddMark", src);
            Assert.Contains("RemoveMark", src);
        }

        [Fact]
        public void HexEditorMark_View_DelegatesIsLoaded()
        {
            var src = ReadView("HexEditorMarkView.axaml.cs");
            Assert.Contains("_vm.IsLoaded", src);
            Assert.Contains("IDataVerifiableView", src);
        }

        [Fact]
        public void HexEditorMark_View_HasJumpToHandler()
        {
            var src = ReadView("HexEditorMarkView.axaml.cs");
            Assert.Contains("JumpTo_Click", src);
            Assert.Contains("DoubleTapped", src);
            Assert.Contains("AddressList", src);
        }

        [Fact]
        public void HexEditorMark_Axaml_HasListAndButtons()
        {
            var src = ReadAxaml("HexEditorMarkView.axaml");
            Assert.Contains("AddressList", src);
            Assert.Contains("Click=\"JumpTo_Click\"", src);
            Assert.Contains("Marked Addresses", src);
            Assert.DoesNotContain("not yet implemented", src);
        }

        // --- HexEditorSearchView ---

        [Fact]
        public void HexEditorSearch_VM_HasSearchText()
        {
            var src = ReadVM("HexEditorSearchViewModel.cs");
            Assert.Contains("string SearchText", src);
            Assert.Contains("SetField(ref _isLoaded", src);
        }

        [Fact]
        public void HexEditorSearch_View_DelegatesIsLoaded()
        {
            var src = ReadView("HexEditorSearchView.axaml.cs");
            Assert.Contains("_vm.IsLoaded", src);
            Assert.Contains("IDataVerifiableView", src);
        }

        [Fact]
        public void HexEditorSearch_Axaml_HasSearchInput()
        {
            var src = ReadAxaml("HexEditorSearchView.axaml");
            Assert.Contains("AddrComboBox", src);
            Assert.Contains("RevCheckBox", src);
            Assert.Contains("LittleEndianCheckBox", src);
            Assert.Contains("Align4CheckBox", src);
            Assert.Contains("Click=\"OK_Click\"", src);
            Assert.DoesNotContain("not yet implemented", src);
        }

        // --- DisASMDumpAllView ---

        [Fact]
        public void DisASMDumpAll_VM_HasOutputAndAction()
        {
            var src = ReadVM("DisASMDumpAllViewModel.cs");
            Assert.Contains("int SelectedAction", src);
            Assert.Contains("string Output", src);
            Assert.Contains("SetField(ref _isLoaded", src);
        }

        [Fact]
        public void DisASMDumpAll_View_DelegatesIsLoaded()
        {
            var src = ReadView("DisASMDumpAllView.axaml.cs");
            Assert.Contains("_vm.IsLoaded", src);
            Assert.Contains("IDataVerifiableView", src);
        }

        [Fact]
        public void DisASMDumpAll_Axaml_HasOutputAndOptions()
        {
            var src = ReadAxaml("DisASMDumpAllView.axaml");
            Assert.Contains("OutputBox", src);
            Assert.Contains("DisASMOption", src);
            Assert.Contains("IDAOption", src);
            Assert.Contains("NoCashOption", src);
            Assert.Contains("Click=\"Run_Click\"", src);
            Assert.DoesNotContain("not yet implemented", src);
        }

        // --- DisASMDumpAllArgGrepView ---

        [Fact]
        public void DisASMDumpAllArgGrep_VM_HasPatternAndResults()
        {
            var src = ReadVM("DisASMDumpAllArgGrepViewModel.cs");
            Assert.Contains("string SearchPattern", src);
            Assert.Contains("string Results", src);
            Assert.Contains("SetField(ref _isLoaded", src);
        }

        [Fact]
        public void DisASMDumpAllArgGrep_View_DelegatesIsLoaded()
        {
            var src = ReadView("DisASMDumpAllArgGrepView.axaml.cs");
            Assert.Contains("_vm.IsLoaded", src);
            Assert.Contains("IDataVerifiableView", src);
        }

        [Fact]
        public void DisASMDumpAllArgGrep_Axaml_HasSearchAndResults()
        {
            var src = ReadAxaml("DisASMDumpAllArgGrepView.axaml");
            Assert.Contains("GrepPatternInput", src);
            Assert.Contains("ResultsBox", src);
            Assert.Contains("Click=\"Search_Click\"", src);
            Assert.DoesNotContain("not yet implemented", src);
        }

        // ===========================================================================
        // Cross-cutting: All 13 dialogs implement IDataVerifiable in VM
        // ===========================================================================

        [Theory]
        [InlineData("MapEditorAddMapChangeDialogViewModel.cs")]
        [InlineData("MapEditorMarSizeDialogViewModel.cs")]
        [InlineData("MapEditorResizeDialogViewModel.cs")]
        [InlineData("MapPointerNewPLISTPopupViewModel.cs")]
        [InlineData("MapSettingDifficultyDialogViewModel.cs")]
        [InlineData("MapStyleEditorAppendPopupViewModel.cs")]
        [InlineData("MapStyleEditorImportImageOptionViewModel.cs")]
        [InlineData("MapStyleEditorWarningOverrideViewModel.cs")]
        [InlineData("HexEditorJumpViewModel.cs")]
        [InlineData("HexEditorMarkViewModel.cs")]
        [InlineData("HexEditorSearchViewModel.cs")]
        [InlineData("DisASMDumpAllViewModel.cs")]
        [InlineData("DisASMDumpAllArgGrepViewModel.cs")]
        public void DialogVM_ImplementsIDataVerifiable(string vmFile)
        {
            var src = ReadVM(vmFile);
            Assert.Contains("IDataVerifiable", src);
            Assert.Contains("GetListCount()", src);
            Assert.Contains("GetDataReport()", src);
            Assert.Contains("GetRawRomReport()", src);
        }

        [Theory]
        [InlineData("MapEditorAddMapChangeDialogView.axaml.cs")]
        [InlineData("MapEditorMarSizeDialogView.axaml.cs")]
        [InlineData("MapEditorResizeDialogView.axaml.cs")]
        [InlineData("MapPointerNewPLISTPopupView.axaml.cs")]
        [InlineData("MapSettingDifficultyDialogView.axaml.cs")]
        [InlineData("MapStyleEditorAppendPopupView.axaml.cs")]
        [InlineData("MapStyleEditorImportImageOptionView.axaml.cs")]
        [InlineData("MapStyleEditorWarningOverrideView.axaml.cs")]
        [InlineData("HexEditorJumpView.axaml.cs")]
        [InlineData("HexEditorMarkView.axaml.cs")]
        [InlineData("HexEditorSearchView.axaml.cs")]
        [InlineData("DisASMDumpAllView.axaml.cs")]
        [InlineData("DisASMDumpAllArgGrepView.axaml.cs")]
        public void DialogView_ImplementsIDataVerifiableView(string viewFile)
        {
            var src = ReadView(viewFile);
            Assert.Contains("IDataVerifiableView", src);
            Assert.Contains("DataViewModel", src);
        }

        [Theory]
        [InlineData("MapEditorAddMapChangeDialogView.axaml.cs")]
        [InlineData("MapEditorMarSizeDialogView.axaml.cs")]
        [InlineData("MapEditorResizeDialogView.axaml.cs")]
        [InlineData("MapPointerNewPLISTPopupView.axaml.cs")]
        [InlineData("MapSettingDifficultyDialogView.axaml.cs")]
        [InlineData("MapStyleEditorAppendPopupView.axaml.cs")]
        [InlineData("MapStyleEditorImportImageOptionView.axaml.cs")]
        [InlineData("MapStyleEditorWarningOverrideView.axaml.cs")]
        [InlineData("HexEditorJumpView.axaml.cs")]
        [InlineData("HexEditorMarkView.axaml.cs")]
        [InlineData("HexEditorSearchView.axaml.cs")]
        [InlineData("DisASMDumpAllView.axaml.cs")]
        [InlineData("DisASMDumpAllArgGrepView.axaml.cs")]
        public void DialogView_CallsVmInitialize(string viewFile)
        {
            var src = ReadView(viewFile);
            Assert.Contains("_vm.Initialize()", src);
        }
    }
}
