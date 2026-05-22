// SPDX-License-Identifier: GPL-3.0-or-later
// Tests for DumpStructSelectDialogViewModel — VM behavior for the
// dispatcher dialog that mirrors WinForms DumpStructSelectDialogForm.
// Part of the gap-sweep fix for #439.
using FEBuilderGBA.Avalonia.ViewModels;
using Xunit;

namespace FEBuilderGBA.Avalonia.Tests
{
    /// <summary>
    /// VM unit tests for DumpStructSelectDialogViewModel. No ROM needed —
    /// these exercise the clipboard-text builders and the Func enum default.
    /// </summary>
    public class DumpStructSelectDialogViewModelTests
    {
        [Fact]
        public void LoadAddress_SetsCurrentAddr()
        {
            var vm = new DumpStructSelectDialogViewModel();
            vm.LoadAddress(0xDEADBEEF);
            Assert.Equal(0xDEADBEEFu, vm.CurrentAddr);
        }

        [Fact]
        public void LoadAddress_SetsIsLoaded()
        {
            var vm = new DumpStructSelectDialogViewModel();
            Assert.False(vm.IsLoaded);
            vm.LoadAddress(0x1234u);
            Assert.True(vm.IsLoaded);
        }

        [Fact]
        public void SelectedFunc_DefaultsToCancel()
        {
            var vm = new DumpStructSelectDialogViewModel();
            Assert.Equal(DumpStructSelectDialogViewModel.Func.Func_Cancel, vm.SelectedFunc);
        }

        [Fact]
        public void CopyPointer_FormatsWithPointerOffset()
        {
            // U.toPointer(0x800ABCD) — already >= 0x02000000, so unchanged.
            // Expected formatted output: "0x0800ABCD" (8-digit hex).
            var vm = new DumpStructSelectDialogViewModel();
            string result = vm.MakeCopyPointerText(0x800ABCDu);
            Assert.Equal("0800ABCD", result);
        }

        [Fact]
        public void CopyPointer_OffsetIsConvertedToPointer()
        {
            // Input < 0x02000000 means U.toPointer adds 0x08000000 offset.
            // 0x0012ABCD + 0x08000000 = 0x0812ABCD.
            var vm = new DumpStructSelectDialogViewModel();
            string result = vm.MakeCopyPointerText(0x12ABCDu);
            Assert.Equal("0812ABCD", result);
        }

        [Fact]
        public void CopyAddress_FormatsAsHexNoPointerConversion()
        {
            // Plain copy of the address (no toPointer); just hex format.
            // U.ToHexString uses variable-width (X02/X04/X06/X08) per the
            // input magnitude — 0x12ABCD <= 0xFFFFFF -> 6 hex digits.
            var vm = new DumpStructSelectDialogViewModel();
            string result = vm.MakeCopyAddressText(0x12ABCDu);
            Assert.Equal("12ABCD", result);
        }

        [Fact]
        public void CopyLittleEndian_ProducesByteSwappedPointer()
        {
            // WF logic (line 1198-1207): toPointer first, then byte-swap.
            // Input 0x00123456 (offset) -> toPointer -> 0x08123456.
            // Byte-swap bytes: 0x56 0x34 0x12 0x08 -> 0x56341208.
            var vm = new DumpStructSelectDialogViewModel();
            string result = vm.MakeCopyLittleEndianText(0x00123456u);
            Assert.Equal("56341208", result);
        }

        [Fact]
        public void CopyNoDollBreakpoint_FormatsAsBracketWithSuffix()
        {
            // WF logic (line 1211): "[" + U.ToHexString(toPointer(addr)) + "]?".
            // Note WF uses ToHexString (variable-width), not To0xHexString:
            // 0x1234 (offset) -> toPointer -> 0x08001234 (> 0xFFFFFF -> X08).
            var vm = new DumpStructSelectDialogViewModel();
            string result = vm.MakeCopyNoDollBreakpointText(0x1234u);
            Assert.Equal("[08001234]?", result);
        }

        [Fact]
        public void SetSelectedFunc_TracksLastClicked()
        {
            // The dispatcher's Func enum should track the last button clicked
            // so callers (or tests) can verify which action was taken.
            var vm = new DumpStructSelectDialogViewModel();
            vm.SelectedFunc = DumpStructSelectDialogViewModel.Func.Func_Binary;
            Assert.Equal(DumpStructSelectDialogViewModel.Func.Func_Binary, vm.SelectedFunc);
        }

        [Fact]
        public void Func_Enum_HasAllTwelveValues()
        {
            // Mirror WinForms DumpStructSelectDialogForm.Func — 12 values.
            // Tests defends against accidental enum value drift.
            var values = System.Enum.GetValues<DumpStructSelectDialogViewModel.Func>();
            Assert.Equal(12, values.Length);
            Assert.Contains(DumpStructSelectDialogViewModel.Func.Func_Cancel, values);
            Assert.Contains(DumpStructSelectDialogViewModel.Func.Func_Binary, values);
            Assert.Contains(DumpStructSelectDialogViewModel.Func.Func_CSV, values);
            Assert.Contains(DumpStructSelectDialogViewModel.Func.Func_TSV, values);
            Assert.Contains(DumpStructSelectDialogViewModel.Func.Func_STRUCT, values);
            Assert.Contains(DumpStructSelectDialogViewModel.Func.Func_EA, values);
            Assert.Contains(DumpStructSelectDialogViewModel.Func.Func_NMM, values);
            Assert.Contains(DumpStructSelectDialogViewModel.Func.Func_Clipbord_Pointer, values);
            Assert.Contains(DumpStructSelectDialogViewModel.Func.Func_Clipbord_Copy, values);
            Assert.Contains(DumpStructSelectDialogViewModel.Func.Func_Clipbord_LittleEndian, values);
            Assert.Contains(DumpStructSelectDialogViewModel.Func.Func_Clipbord_NoDollBreakPoint, values);
            Assert.Contains(DumpStructSelectDialogViewModel.Func.Func_Import, values);
        }
    }
}
