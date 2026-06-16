using System;
using System.IO;
using FEBuilderGBA;
using FEBuilderGBA.Avalonia.ViewModels;
using Xunit;

namespace FEBuilderGBA.Avalonia.Tests
{
    /// <summary>
    /// Tests for the Undo history tool VM (#1190, port of WinForms <c>ToolUndoForm</c>).
    /// The VM is a thin presenter over the Core <see cref="Undo"/> buffer: it renders
    /// <c>UndoBuffer</c> newest-first (matching WinForms <c>Redraw</c>), resolves the
    /// rollback/test-play positions with the same guards as <c>RollbackThisVersion</c>,
    /// and delegates ROM mutation to <c>Undo.Rollback</c>/<c>TestPlayThisVersion</c>.
    ///
    /// Uses a self-contained in-memory ROM so the tests run offline with no .gba file.
    /// </summary>
    [Collection("SharedState")]
    public class ToolUndoViewModelTests : IDisposable
    {
        // Snapshot of CoreState so each test restores global state on dispose.
        readonly ROM? _prevRom;
        readonly Undo? _prevUndo;
        string? _tempRomPath;

        public ToolUndoViewModelTests()
        {
            _prevRom = CoreState.ROM;
            _prevUndo = CoreState.Undo;
        }

        public void Dispose()
        {
            CoreState.ROM = _prevRom;
            CoreState.Undo = _prevUndo;
            // Remove any .emulator sidecar produced by a DoTestPlay test.
            if (_tempRomPath != null)
            {
                TryDelete(_tempRomPath);
                string dir = Path.GetDirectoryName(_tempRomPath) ?? ".";
                string stem = Path.GetFileNameWithoutExtension(_tempRomPath);
                string ext = Path.GetExtension(_tempRomPath);
                TryDelete(Path.Combine(dir, stem + ".emulator" + ext));
            }
        }

        static void TryDelete(string path)
        {
            try { if (File.Exists(path)) File.Delete(path); } catch { /* best-effort cleanup */ }
        }

        // -----------------------------------------------------------------
        // Test scaffolding
        // -----------------------------------------------------------------

        /// <summary>Install a deterministic in-memory ROM + fresh Undo into CoreState.</summary>
        ROM InstallStubRom(int sizeBytes = 0x1000)
        {
            byte[] data = new byte[sizeBytes];
            var rom = new ROM();
            rom.SwapNewROMDataDirect(data);
            // Give the ROM a real on-disk path so DoTestPlay can save the clone.
            _tempRomPath = Path.Combine(Path.GetTempPath(), $"toolundo_{Guid.NewGuid():N}.gba");
            rom.Filename = _tempRomPath;
            CoreState.ROM = rom;
            CoreState.Undo = new Undo();
            return rom;
        }

        /// <summary>Push a named undo record that records the bytes at [addr,addr+size).</summary>
        static void PushEdit(ROM rom, string name, uint addr, uint size)
        {
            // Snapshot BEFORE mutating, like real editors do, so a rollback restores
            // the pre-edit bytes. Using the simple Push(name,addr,size) overload that
            // captures the current bytes into a single UndoPostion.
            CoreState.Undo!.Push(name, addr, size);
            // Now mutate so the buffer represents a real change.
            for (uint i = 0; i < size; i++)
                rom.write_u8(addr + i, (byte)(0xA0 + i));
        }

        // -----------------------------------------------------------------
        // LoadList — ordering, content, current marker
        // -----------------------------------------------------------------

        [Fact]
        public void LoadList_NullUndo_ReturnsEmpty()
        {
            CoreState.ROM = null;
            CoreState.Undo = null;

            var vm = new ToolUndoViewModel();
            var list = vm.LoadList();

            Assert.Empty(list);
            Assert.Empty(vm.Entries);
            Assert.Equal(-1, vm.CurrentDisplayIndex);
            Assert.True(vm.IsLoaded);
        }

        [Fact]
        public void LoadList_EmptyBuffer_HasOnlyLatestSentinel()
        {
            InstallStubRom();

            var vm = new ToolUndoViewModel();
            vm.LoadList();

            // WinForms iterates count..0 -> with 0 records, that is a single row
            // (the "latest" sentinel at position 0).
            Assert.Single(vm.Entries);
            Assert.Equal(0, vm.Entries[0].Position);
            Assert.True(vm.Entries[0].IsCurrent); // Postion starts at 0
            Assert.Equal(0, vm.CurrentDisplayIndex);
        }

        [Fact]
        public void LoadList_IsNewestFirst_AndMatchesMakeName()
        {
            var rom = InstallStubRom();
            PushEdit(rom, "first", 0x10, 4);
            PushEdit(rom, "second", 0x20, 4);
            PushEdit(rom, "third", 0x30, 4);

            var vm = new ToolUndoViewModel();
            vm.LoadList();

            // 3 records -> 4 rows (positions 3,2,1,0 top-to-bottom).
            Assert.Equal(4, vm.Entries.Count);
            Assert.Equal(3, vm.Entries[0].Position); // latest sentinel on top
            Assert.Equal(2, vm.Entries[1].Position);
            Assert.Equal(1, vm.Entries[2].Position);
            Assert.Equal(0, vm.Entries[3].Position);

            // Each display name must equal the Core MakeName for that position.
            Undo undo = CoreState.Undo!;
            foreach (var row in vm.Entries)
                Assert.Equal(undo.MakeName(row.Position, showAllowMark: false), row.DisplayName);

            // Current cursor is at the end (Postion == count == 3) -> top row.
            Assert.True(vm.Entries[0].IsCurrent);
            Assert.Equal(0, vm.CurrentDisplayIndex);
        }

        [Fact]
        public void LoadList_RowMetadata_MatchesUndoData()
        {
            var rom = InstallStubRom();
            PushEdit(rom, "edit-a", 0x40, 2);

            var vm = new ToolUndoViewModel();
            vm.LoadList();

            // Sentinel row (position 1 == count) has no time/change count.
            var sentinel = vm.Entries[0];
            Assert.Equal(1, sentinel.Position);
            Assert.Equal("", sentinel.TimeText);
            Assert.Equal("-", sentinel.ChangeCountText);

            // Data row (position 0) reflects the single recorded UndoPostion.
            var dataRow = vm.Entries[1];
            Assert.Equal(0, dataRow.Position);
            Undo.UndoData ud = CoreState.Undo!.UndoBuffer[0];
            Assert.Equal(ud.time.ToString(), dataRow.TimeText);
            Assert.Equal(ud.list.Count.ToString(), dataRow.ChangeCountText);
        }

        // -----------------------------------------------------------------
        // RollbackPositionFor / TestPlayPositionFor — guards
        // -----------------------------------------------------------------

        [Fact]
        public void RollbackPositionFor_CurrentPosition_ReturnsMinusOne()
        {
            var rom = InstallStubRom();
            PushEdit(rom, "edit", 0x50, 4);

            var vm = new ToolUndoViewModel();
            vm.LoadList();

            // Top row is the current position (Postion == count). Rolling back to
            // the current position is a no-op in WinForms -> -1.
            Assert.Equal(-1, vm.RollbackPositionFor(0));
        }

        [Fact]
        public void RollbackPositionFor_OlderRow_ReturnsThatPosition()
        {
            var rom = InstallStubRom();
            PushEdit(rom, "e1", 0x60, 4);
            PushEdit(rom, "e2", 0x70, 4);

            var vm = new ToolUndoViewModel();
            vm.LoadList();

            // Rows: [0]=pos2(current), [1]=pos1, [2]=pos0. Rolling to display row 2.
            Assert.Equal(0, vm.RollbackPositionFor(2));
            Assert.Equal(1, vm.RollbackPositionFor(1));
        }

        [Theory]
        [InlineData(-1)]
        [InlineData(999)]
        public void RollbackPositionFor_OutOfRange_ReturnsMinusOne(int displayIndex)
        {
            var rom = InstallStubRom();
            PushEdit(rom, "e", 0x80, 4);

            var vm = new ToolUndoViewModel();
            vm.LoadList();

            Assert.Equal(-1, vm.RollbackPositionFor(displayIndex));
        }

        [Fact]
        public void TestPlayPositionFor_AllowsCurrentPosition()
        {
            var rom = InstallStubRom();
            PushEdit(rom, "e", 0x90, 4);

            var vm = new ToolUndoViewModel();
            vm.LoadList();

            // Unlike rollback, test-play does NOT skip the current position.
            Assert.Equal(vm.Entries[0].Position, vm.TestPlayPositionFor(0));
        }

        // -----------------------------------------------------------------
        // MakeVersionName
        // -----------------------------------------------------------------

        [Fact]
        public void MakeVersionName_MatchesMakeName()
        {
            var rom = InstallStubRom();
            PushEdit(rom, "named-edit", 0xA0, 4);

            var vm = new ToolUndoViewModel();
            vm.LoadList();

            Undo undo = CoreState.Undo!;
            Assert.Equal(undo.MakeName(0, false), vm.MakeVersionName(0));
            Assert.Equal("", vm.MakeVersionName(-1));      // out of range -> empty
            Assert.Equal("", vm.MakeVersionName(999));     // out of range -> empty
        }

        // -----------------------------------------------------------------
        // DoRollback / DoTestPlay — behaviour + null safety
        // -----------------------------------------------------------------

        [Fact]
        public void DoRollback_MovesPositionAndRestoresBytes()
        {
            var rom = InstallStubRom();
            // Original byte at 0xB0 is 0; PushEdit records 0 then writes 0xA0.
            PushEdit(rom, "edit", 0xB0, 1);
            Assert.Equal(0xA0u, rom.u8(0xB0));
            Assert.Equal(1, CoreState.Undo!.Postion);

            var vm = new ToolUndoViewModel();
            vm.LoadList();

            vm.DoRollback(0); // roll back to before the edit
            Assert.Equal(0, CoreState.Undo.Postion);
            Assert.Equal(0x00u, rom.u8(0xB0)); // original byte restored
        }

        [Fact]
        public void DoRollback_NullRom_NoThrow()
        {
            CoreState.ROM = null;
            CoreState.Undo = new Undo();

            var vm = new ToolUndoViewModel();
            var ex = Record.Exception(() => vm.DoRollback(0));
            Assert.Null(ex);
        }

        [Fact]
        public void DoRollback_NullUndo_NoThrow()
        {
            InstallStubRom();
            CoreState.Undo = null;

            var vm = new ToolUndoViewModel();
            var ex = Record.Exception(() => vm.DoRollback(0));
            Assert.Null(ex);
        }

        [Fact]
        public void DoTestPlay_WritesEmulatorSidecar_NoThrow()
        {
            var rom = InstallStubRom();
            PushEdit(rom, "edit", 0xC0, 4);

            var vm = new ToolUndoViewModel();
            vm.LoadList();

            var ex = Record.Exception(() => vm.DoTestPlay(0));
            Assert.Null(ex);

            // TestPlayThisVersion saves a "<stem>.emulator<ext>" clone.
            string dir = Path.GetDirectoryName(_tempRomPath!) ?? ".";
            string stem = Path.GetFileNameWithoutExtension(_tempRomPath!);
            string ext = Path.GetExtension(_tempRomPath!);
            string emuPath = Path.Combine(dir, stem + ".emulator" + ext);
            Assert.True(File.Exists(emuPath), $"emulator sidecar not written: {emuPath}");
        }

        [Fact]
        public void DoTestPlay_NullRom_NoThrow()
        {
            CoreState.ROM = null;
            CoreState.Undo = new Undo();

            var vm = new ToolUndoViewModel();
            var ex = Record.Exception(() => vm.DoTestPlay(0));
            Assert.Null(ex);
        }
    }
}
