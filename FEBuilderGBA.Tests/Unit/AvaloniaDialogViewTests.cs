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
        public void MapEditorAddMapChangeDialog_VM_HasDialogResult()
        {
            var src = ReadVM("MapEditorAddMapChangeDialogViewModel.cs");
            Assert.Contains("string DialogResult", src);
        }

        [Fact]
        public void MapEditorAddMapChangeDialog_View_DelegatesIsLoaded()
        {
            var src = ReadView("MapEditorAddMapChangeDialogView.axaml.cs");
            Assert.Contains("_vm.IsLoaded", src);
            Assert.Contains("IDataVerifiableView", src);
        }

        [Fact]
        public void MapEditorAddMapChangeDialog_View_HasThreeButtonHandlers()
        {
            var src = ReadView("MapEditorAddMapChangeDialogView.axaml.cs");
            Assert.Contains("New_Click", src);
            Assert.Contains("Edit_Click", src);
            Assert.Contains("Cancel_Click", src);
        }

        [Fact]
        public void MapEditorAddMapChangeDialog_Axaml_HasThreeButtons()
        {
            var src = ReadAxaml("MapEditorAddMapChangeDialogView.axaml");
            Assert.Contains("Click=\"New_Click\"", src);
            Assert.Contains("Click=\"Edit_Click\"", src);
            Assert.Contains("Click=\"Cancel_Click\"", src);
            Assert.DoesNotContain("not yet implemented", src);
        }

        // --- MapEditorMarSizeDialogView ---

        [Fact]
        public void MapEditorMarSizeDialog_VM_HasWidth()
        {
            var src = ReadVM("MapEditorMarSizeDialogViewModel.cs");
            Assert.Contains("uint Width", src);
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
            Assert.Contains("Click=\"OK_Click\"", src);
            Assert.DoesNotContain("not yet implemented", src);
        }

        // --- MapEditorResizeDialogView ---

        [Fact]
        public void MapEditorResizeDialog_VM_HasPositionAndPadding()
        {
            var src = ReadVM("MapEditorResizeDialogViewModel.cs");
            Assert.Contains("int X", src);
            Assert.Contains("int Y", src);
            Assert.Contains("int T", src);
            Assert.Contains("int B", src);
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
            Assert.Contains("XInput", src);
            Assert.Contains("YInput", src);
            Assert.Contains("TInput", src);
            Assert.Contains("BInput", src);
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
            Assert.Contains("uint DifficultyValue", src);
            Assert.Contains("string DialogResult", src);
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
            Assert.Contains("HardBoostInput", src);
            Assert.Contains("NormalPenaltyInput", src);
            Assert.Contains("EasyPenaltyInput", src);
            Assert.Contains("DifficultyValueInput", src);
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
        public void MapStyleEditorImportImageOption_Axaml_HasThreeButtons()
        {
            var src = ReadAxaml("MapStyleEditorImportImageOptionView.axaml");
            Assert.Contains("Click=\"WithPalette_Click\"", src);
            Assert.Contains("Click=\"ImageOnly_Click\"", src);
            Assert.Contains("Click=\"OnePicture_Click\"", src);
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

        // ===========================================================================
        // BitFlag Views + PointerToolBatchInput alignment
        // ===========================================================================

        // --- UbyteBitFlagView (#225) ---

        [Fact]
        public void UbyteBitFlag_Axaml_MatchesWinFormsLayout()
        {
            var src = ReadAxaml("UbyteBitFlagView.axaml");
            Assert.Contains("Width=\"708\"", src);
            Assert.Contains("Height=\"420\"", src);
            Assert.Contains("Name=\"MESSAGE\"", src);
            Assert.Contains("Name=\"ApplyButton\"", src);
            Assert.Contains("Width=\"181\"", src);
            Assert.Contains("Name=\"B40\"", src);
            Assert.Contains("Name=\"Bit0Box\"", src);
            Assert.Contains("Name=\"Bit7Box\"", src);
        }

        [Fact]
        public void UbyteBitFlag_View_HasApplyClickAndHexSync()
        {
            var src = ReadView("UbyteBitFlagView.axaml.cs");
            Assert.Contains("OK_Click", src);
            Assert.Contains("OnHexChanged", src);
            Assert.Contains("OnBitChanged", src);
            Assert.Contains("B40.ValueChanged", src);
            Assert.DoesNotContain("Cancel_Click", src);
        }

        // --- UshortBitFlagView (#226) ---

        [Fact]
        public void UshortBitFlag_Axaml_HasTwoColumnLayout()
        {
            var src = ReadAxaml("UshortBitFlagView.axaml");
            Assert.Contains("Width=\"708\"", src);
            Assert.Contains("Height=\"420\"", src);
            Assert.Contains("ColumnDefinitions=\"*,*\"", src);
            Assert.Contains("Name=\"B40\"", src);
            Assert.Contains("Name=\"B41\"", src);
            Assert.Contains("Name=\"Bit0Box\"", src);
            Assert.Contains("Name=\"Bit15Box\"", src);
            Assert.Contains("Name=\"ApplyButton\"", src);
        }

        [Fact]
        public void UshortBitFlag_View_HasTwoHexInputs()
        {
            var src = ReadView("UshortBitFlagView.axaml.cs");
            Assert.Contains("B40.ValueChanged", src);
            Assert.Contains("B41.ValueChanged", src);
            Assert.Contains("OnHexLowChanged", src);
            Assert.Contains("OnHexHighChanged", src);
            Assert.DoesNotContain("Cancel_Click", src);
        }

        // --- UwordBitFlagView (#227) ---

        [Fact]
        public void UwordBitFlag_Axaml_HasFourColumnLayout()
        {
            var src = ReadAxaml("UwordBitFlagView.axaml");
            Assert.Contains("Width=\"1183\"", src);
            Assert.Contains("Height=\"420\"", src);
            Assert.Contains("ColumnDefinitions=\"*,*,*,*\"", src);
            Assert.Contains("Name=\"B40\"", src);
            Assert.Contains("Name=\"B41\"", src);
            Assert.Contains("Name=\"B42\"", src);
            Assert.Contains("Name=\"B43\"", src);
            Assert.Contains("Name=\"Bit0Box\"", src);
            Assert.Contains("Name=\"Bit31Box\"", src);
            Assert.Contains("Name=\"ApplyButton\"", src);
        }

        [Fact]
        public void UwordBitFlag_View_HasFourHexInputs()
        {
            var src = ReadView("UwordBitFlagView.axaml.cs");
            Assert.Contains("B40.ValueChanged", src);
            Assert.Contains("B41.ValueChanged", src);
            Assert.Contains("B42.ValueChanged", src);
            Assert.Contains("B43.ValueChanged", src);
            Assert.Contains("OnHexChanged", src);
            Assert.DoesNotContain("Cancel_Click", src);
        }

        // --- PointerToolBatchInputView (#259) ---

        [Fact]
        public void PointerToolBatchInput_Axaml_MatchesWinFormsLayout()
        {
            var src = ReadAxaml("PointerToolBatchInputView.axaml");
            Assert.Contains("Width=\"918\"", src);
            Assert.Contains("Height=\"585\"", src);
            Assert.Contains("Name=\"RunButton\"", src);
            Assert.Contains("Batch Address Convert", src);
            Assert.Contains("Width=\"235\"", src);
            Assert.Contains("Height=\"34\"", src);
            Assert.Contains("Name=\"BatchInputTextBox\"", src);
            Assert.DoesNotContain("Cancel", src);
        }

        [Fact]
        public void PointerToolBatchInput_View_HasSingleButton()
        {
            var src = ReadView("PointerToolBatchInputView.axaml.cs");
            Assert.Contains("OK_Click", src);
            Assert.DoesNotContain("Cancel_Click", src);
        }

        // --- PointerToolCopyToView (#260) ---

        [Fact]
        public void PointerToolCopyTo_Axaml_HasFiveCopyButtons()
        {
            var src = ReadAxaml("PointerToolCopyToView.axaml");
            Assert.Contains("Width=\"449\"", src);
            Assert.Contains("Height=\"404\"", src);
            Assert.Contains("Name=\"CopyPointer\"", src);
            Assert.Contains("Name=\"CopyClipboard\"", src);
            Assert.Contains("Name=\"CopyLittleEndian\"", src);
            Assert.Contains("Name=\"HexButton\"", src);
            Assert.Contains("Name=\"CopyNoDollGBARadBreakPoint\"", src);
            Assert.Contains("Name=\"ValueTextBox\"", src);
            Assert.DoesNotContain("Cancel", src);
        }

        [Fact]
        public void PointerToolCopyTo_View_HasFiveClickHandlers()
        {
            var src = ReadView("PointerToolCopyToView.axaml.cs");
            Assert.Contains("CopyPointer_Click", src);
            Assert.Contains("CopyClipboard_Click", src);
            Assert.Contains("CopyLittleEndian_Click", src);
            Assert.Contains("HexButton_Click", src);
            Assert.Contains("CopyNoDoll_Click", src);
        }

        // --- TextBadCharPopupView (#191) ---

        [Fact]
        public void TextBadCharPopup_Axaml_HasThreeActionButtons()
        {
            var src = ReadAxaml("TextBadCharPopupView.axaml");
            Assert.Contains("Width=\"849\"", src);
            Assert.Contains("Height=\"469\"", src);
            Assert.Contains("Name=\"ErrorMessageLabel\"", src);
            Assert.Contains("Name=\"Button1\"", src);
            Assert.Contains("Name=\"Button2\"", src);
            Assert.Contains("Name=\"Button3\"", src);
            Assert.Contains("Give Up", src);
            Assert.Contains("Anti-Huffman", src);
            Assert.Contains("Encoding Table", src);
        }

        [Fact]
        public void TextBadCharPopup_View_HasThreeClickHandlers()
        {
            var src = ReadView("TextBadCharPopupView.axaml.cs");
            Assert.Contains("GiveUp_Click", src);
            Assert.Contains("AntiHuffman_Click", src);
            Assert.Contains("EncodingTable_Click", src);
            Assert.Contains("SelectedAction", src);
        }

        // --- MapPointerNewPLISTPopupView (#59) ---

        [Fact]
        public void MapPointerNewPLIST_Axaml_MatchesWinFormsLayout()
        {
            var src = ReadAxaml("MapPointerNewPLISTPopupView.axaml");
            Assert.Contains("Width=\"953\"", src);
            Assert.Contains("Height=\"423\"", src);
            Assert.Contains("Name=\"PLISTExtendsButton\"", src);
            Assert.Contains("Name=\"PlistIdInput\"", src);
            Assert.Contains("Name=\"LinkPlistDisplay\"", src);
            Assert.Contains("Name=\"OKButton\"", src);
            Assert.Contains("Name=\"MyCancelButton\"", src);
            Assert.Contains("Width=\"191\"", src);
        }

        [Fact]
        public void MapPointerNewPLIST_View_HasExtendAndOKCancel()
        {
            var src = ReadView("MapPointerNewPLISTPopupView.axaml.cs");
            Assert.Contains("Extend_Click", src);
            Assert.Contains("OK_Click", src);
            Assert.Contains("Cancel_Click", src);
            Assert.Contains("PlistIdInput.Value", src);
        }

        // --- ToolUndoPopupDialogView (#299) ---

        [Fact]
        public void ToolUndoPopupDialog_Axaml_MatchesWinFormsLayout()
        {
            var src = ReadAxaml("ToolUndoPopupDialogView.axaml");
            Assert.Contains("Width=\"771\"", src);
            Assert.Contains("Height=\"355\"", src);
            Assert.Contains("Name=\"InfoTextBox\"", src);
            Assert.Contains("Name=\"TestPlayButton\"", src);
            Assert.Contains("Name=\"RunUndoButton\"", src);
            Assert.Contains("Name=\"MyCancelButton\"", src);
        }

        [Fact]
        public void ToolUndoPopupDialog_View_HasThreeClickHandlers()
        {
            var src = ReadView("ToolUndoPopupDialogView.axaml.cs");
            Assert.Contains("TestPlay_Click", src);
            Assert.Contains("RunUndo_Click", src);
            Assert.Contains("Cancel_Click", src);
            Assert.Contains("DialogResult", src);
        }

        // --- ToolChangeProjectnameView (#269) ---

        [Fact]
        public void ToolChangeProjectname_Axaml_MatchesWinFormsLayout()
        {
            var src = ReadAxaml("ToolChangeProjectnameView.axaml");
            Assert.Contains("Width=\"791\"", src);
            Assert.Contains("Height=\"360\"", src);
            Assert.Contains("Name=\"CurrentNameTextBox\"", src);
            Assert.Contains("Name=\"NewNameTextBox\"", src);
            Assert.Contains("Name=\"ChangeButton\"", src);
            Assert.Contains("Name=\"HelpTextBox\"", src);
            Assert.DoesNotContain("Cancel", src);
        }

        // --- PackedMemorySlotView (#261) ---

        [Fact]
        public void PackedMemorySlot_Axaml_MatchesWinFormsLayout()
        {
            var src = ReadAxaml("PackedMemorySlotView.axaml");
            Assert.Contains("Width=\"708\"", src);
            Assert.Contains("Height=\"186\"", src);
            Assert.Contains("Name=\"MESSAGE\"", src);
            Assert.Contains("Name=\"ApplyButton\"", src);
            Assert.Contains("Name=\"AComboBox\"", src);
            Assert.Contains("Name=\"BComboBox\"", src);
            Assert.Contains("Name=\"CComboBox\"", src);
        }

        // --- ToolAutomaticRecoveryROMHeaderView (#270) ---

        [Fact]
        public void ToolAutomaticRecoveryROMHeader_Axaml_MatchesWinFormsLayout()
        {
            var src = ReadAxaml("ToolAutomaticRecoveryROMHeaderView.axaml");
            Assert.Contains("Width=\"1193\"", src);
            Assert.Contains("Height=\"298\"", src);
            Assert.Contains("Name=\"OrignalSelectButton\"", src);
            Assert.Contains("Name=\"OrignalFilename\"", src);
            Assert.Contains("Name=\"ApplyButton\"", src);
        }

        // --- GraphicsToolPatchMakerView (#139) ---

        [Fact]
        public void GraphicsToolPatchMaker_Axaml_MatchesWinFormsLayout()
        {
            var src = ReadAxaml("GraphicsToolPatchMakerView.axaml");
            Assert.Contains("Width=\"572\"", src);
            Assert.Contains("Height=\"525\"", src);
            Assert.Contains("Name=\"PatchText\"", src);
            Assert.Contains("Name=\"CloseButton\"", src);
            Assert.Contains("Name=\"SaveButton\"", src);
            Assert.Contains("Width=\"192\"", src);
            Assert.Contains("Width=\"289\"", src);
        }

        // --- ErrorLongMessageDialogView (#233) ---

        [Fact]
        public void ErrorLongMessageDialog_Axaml_MatchesWinFormsLayout()
        {
            var src = ReadAxaml("ErrorLongMessageDialogView.axaml");
            Assert.Contains("Width=\"1120\"", src);
            Assert.Contains("Height=\"691\"", src);
            Assert.Contains("Name=\"MyCloseButton\"", src);
            Assert.Contains("Name=\"ErrorMessage\"", src);
            Assert.Contains("Foreground=\"Red\"", src);
            Assert.Contains("Height=\"43\"", src);
        }

        // --- WelcomeView (#302) ---

        [Fact]
        public void WelcomeView_Axaml_MatchesWinFormsLayout()
        {
            var src = ReadAxaml("WelcomeView.axaml");
            Assert.Contains("Width=\"1172\"", src);
            Assert.Contains("Height=\"588\"", src);
            Assert.Contains("Name=\"OpenLastROMButton\"", src);
            Assert.Contains("Name=\"OpenROMButton\"", src);
            Assert.Contains("Name=\"UpdateCheckButton\"", src);
            Assert.Contains("Name=\"ManButton\"", src);
            Assert.Contains("Width=\"851\"", src);
        }

        [Fact]
        public void WelcomeView_View_HasFourClickHandlers()
        {
            var src = ReadView("WelcomeView.axaml.cs");
            Assert.Contains("OpenLastROM_Click", src);
            Assert.Contains("OpenROM_Click", src);
            Assert.Contains("UpdateCheck_Click", src);
            Assert.Contains("Manual_Click", src);
        }

        // ===========================================================================
        // Image Viewer Forms — Window Size Alignment
        // ===========================================================================

        [Theory]
        [InlineData("BattleBGViewerView.axaml", 853, 441)]
        [InlineData("BigCGViewerView.axaml", 1050, 441)]
        [InlineData("OPPrologueViewerView.axaml", 1225, 471)]
        [InlineData("BattleTerrainViewerView.axaml", 1251, 811)]
        [InlineData("ChapterTitleViewerView.axaml", 1224, 564)]
        [InlineData("SystemIconViewerView.axaml", 966, 656)]
        [InlineData("SystemHoverColorViewerView.axaml", 1238, 604)]
        [InlineData("OPClassDemoViewerView.axaml", 1429, 848)]
        [InlineData("OPClassFontViewerView.axaml", 1179, 475)]
        [InlineData("ItemWeaponEffectViewerView.axaml", 1302, 803)]
        [InlineData("ItemStatBonusesViewerView.axaml", 1291, 587)]
        [InlineData("ItemEffectivenessViewerView.axaml", 1297, 768)]
        [InlineData("ItemPromotionViewerView.axaml", 1150, 669)]
        [InlineData("ItemShopViewerView.axaml", 1207, 682)]
        [InlineData("ItemWeaponTriangleViewerView.axaml", 1290, 648)]
        [InlineData("ItemUsagePointerViewerView.axaml", 1238, 801)]
        [InlineData("ItemEffectPointerViewerView.axaml", 1185, 658)]
        [InlineData("ItemIconViewerView.axaml", 749, 358)]
        [InlineData("ArenaClassViewerView.axaml", 1201, 738)]
        [InlineData("ArenaEnemyWeaponViewerView.axaml", 1257, 809)]
        [InlineData("LinkArenaDenyUnitViewerView.axaml", 1201, 738)]
        [InlineData("MonsterProbabilityViewerView.axaml", 1203, 552)]
        [InlineData("MonsterItemViewerView.axaml", 1645, 935)]
        [InlineData("MonsterWMapProbabilityViewerView.axaml", 1786, 847)]
        [InlineData("SummonUnitViewerView.axaml", 1214, 462)]
        [InlineData("SummonsDemonKingViewerView.axaml", 1237, 570)]
        [InlineData("SoundBossBGMViewerView.axaml", 1392, 722)]
        [InlineData("SoundFootStepsViewerView.axaml", 1245, 636)]
        [InlineData("SoundRoomViewerView.axaml", 1275, 817)]
        [InlineData("TextViewerView.axaml", 1166, 930)]
        [InlineData("PortraitViewerView.axaml", 789, 547)]
        [InlineData("MenuDefinitionView.axaml", 1257, 521)]
        [InlineData("MenuCommandView.axaml", 1238, 604)]
        [InlineData("EDView.axaml", 1200, 773)]
        [InlineData("EDStaffRollView.axaml", 1142, 458)]
        [InlineData("WorldMapPointView.axaml", 1761, 778)]
        [InlineData("WorldMapBGMView.axaml", 1163, 778)]
        [InlineData("WorldMapEventPointerView.axaml", 1219, 872)]
        [InlineData("SongTableView.axaml", 1505, 809)]
        [InlineData("UnitEditorView.axaml", 934, 661)]
        [InlineData("ItemEditorView.axaml", 1408, 856)]
        [InlineData("ClassEditorView.axaml", 973, 726)]
        [InlineData("ClassFE6View.axaml", 1452, 923)]
        [InlineData("CCBranchEditorView.axaml", 1264, 684)]
        [InlineData("MoveCostEditorView.axaml", 1601, 800)]
        [InlineData("TerrainNameEditorView.axaml", 1253, 790)]
        [InlineData("SupportUnitEditorView.axaml", 1197, 699)]
        [InlineData("SupportAttributeView.axaml", 1169, 497)]
        [InlineData("SupportTalkView.axaml", 1279, 720)]
        [InlineData("UnitFE6View.axaml", 1409, 853)]
        [InlineData("UnitFE7View.axaml", 1409, 853)]
        [InlineData("ItemFE6View.axaml", 1408, 856)]
        [InlineData("MoveCostFE6View.axaml", 1536, 634)]
        [InlineData("SupportUnitFE6View.axaml", 1234, 715)]
        [InlineData("SupportTalkFE6View.axaml", 1279, 571)]
        [InlineData("SupportTalkFE7View.axaml", 1279, 708)]
        [InlineData("MapSettingView.axaml", 1773, 1038)]
        [InlineData("MapChangeView.axaml", 1609, 636)]
        [InlineData("MapExitPointView.axaml", 1607, 777)]
        [InlineData("MapPointerView.axaml", 1305, 532)]
        [InlineData("MapTileAnimationView.axaml", 1238, 866)]
        [InlineData("EventCondView.axaml", 1834, 999)]
        [InlineData("SomeClassListView.axaml", 1155, 661)]
        [InlineData("VennouWeaponLockView.axaml", 1155, 661)]
        [InlineData("UnitsShortTextView.axaml", 1155, 551)]
        public void ImageViewerForm_HasCorrectWindowSize(string axamlFile, int width, int height)
        {
            var src = ReadAxaml(axamlFile);
            Assert.Contains($"Width=\"{width}\"", src);
            Assert.Contains($"Height=\"{height}\"", src);
        }

        [Theory]
        [InlineData("BattleBGViewerView.axaml")]
        [InlineData("BigCGViewerView.axaml")]
        [InlineData("OPPrologueViewerView.axaml")]
        [InlineData("BattleTerrainViewerView.axaml")]
        [InlineData("ChapterTitleViewerView.axaml")]
        [InlineData("SystemIconViewerView.axaml")]
        [InlineData("OPClassDemoViewerView.axaml")]
        [InlineData("OPClassFontViewerView.axaml")]
        public void ImageViewerForm_HasAddressListAndStandardLayout(string axamlFile)
        {
            var src = ReadAxaml(axamlFile);
            Assert.Contains("Name=\"EntryList\"", src);
            Assert.Contains("Name=\"AddrLabel\"", src);
        }
    }
}
