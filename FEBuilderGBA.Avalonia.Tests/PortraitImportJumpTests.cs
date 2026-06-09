using System;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using FEBuilderGBA.Avalonia.Controls;
using FEBuilderGBA.Avalonia.Views;
using Xunit;

namespace FEBuilderGBA.Avalonia.Tests
{
    /// <summary>
    /// Tests for the Portrait editor's "Import" jump (#1019, Import half) which
    /// positions the Portrait Import Wizard at the CURRENT portrait's slot:
    ///   - ImagePortraitImporterView.NavigateTo + the pending-navigate timing
    ///     pattern (replay OVERRIDES the row-0 auto-select so the right slot,
    ///     plus its B20-B23 coords loaded into the four NUDs, wins).
    ///   - ImagePortraitView.JumpToImporter_Click forwards CurrentAddr to the
    ///     importer's NavigateTo (source/parity assertion).
    ///
    /// Uses a synthetic FE8U ROM (rom.LoadLow(.., "BE8E01")) so the run is
    /// deterministic and ROM-independent. FE8U RomInfo fixes:
    ///   portrait_pointer = 0x5524 (portrait_datasize = 28).
    /// We write the portrait base pointer plus a couple of portrait entries with
    /// DISTINCT B20-B23 values so the "right slot" assertion is meaningful: the
    /// target (non-row-0) slot's coords differ from row 0's.
    /// </summary>
    [Collection("SharedState")]
    public class PortraitImportJumpTests
    {
        const uint PortraitPtrSlot = 0x5524;
        const uint DefaultPortraitBase = 0x00120000;
        const uint PortraitEntrySize = 28;

        /// <summary>
        /// Build a synthetic FE8U ROM with the portrait base pointer set and a
        /// few portrait entries written. Row 0 and the target slot get DISTINCT
        /// B20-B23 values so the assertion proves the importer lands on the
        /// intended slot, not row 0.
        /// </summary>
        static ROM BuildRom(uint portraitBase = DefaultPortraitBase)
        {
            var rom = new ROM();
            rom.LoadLow("synth-fe8u.gba", new byte[0x1000000], "BE8E01");

            // Portrait base pointer at 0x5524.
            rom.write_p32(PortraitPtrSlot, portraitBase + 0x08000000);

            // Write a handful of non-null 28-byte portrait entries so LoadList()
            // enumerates them. The first u32 of each entry must be non-zero (the
            // null-run terminator scans rom.u32(addr) == 0).
            for (uint i = 0; i < 5; i++)
            {
                uint addr = portraitBase + i * PortraitEntrySize;
                // D0 sheet pointer — any non-zero value keeps the entry "live".
                rom.write_u32(addr, 0x08000000u + 0x200000u + i * 0x1000u);
                // B20-B23 mouth/eye block coords, made distinct per slot
                // (offset by id so row 0 != target slot).
                rom.write_u8(addr + 20, (byte)(0x10 + i)); // MouthBlockX
                rom.write_u8(addr + 21, (byte)(0x20 + i)); // MouthBlockY
                rom.write_u8(addr + 22, (byte)(0x30 + i)); // EyeBlockX
                rom.write_u8(addr + 23, (byte)(0x40 + i)); // EyeBlockY
            }

            return rom;
        }

        static IDisposable UseRom(ROM rom)
        {
            ROM prev = CoreState.ROM;
            CoreState.ROM = rom;
            return new Restore(prev);
        }

        sealed class Restore : IDisposable
        {
            readonly ROM _prev;
            public Restore(ROM prev) => _prev = prev;
            public void Dispose() => CoreState.ROM = _prev;
        }

        // ===================================================================
        // NavigateTo — pending stash + replay overrides row 0
        // ===================================================================

        [AvaloniaFact]
        public void NavigateTo_BeforeListLoads_OverridesRow0AndLoadsTargetSlot()
        {
            var rom = BuildRom();
            using (UseRom(rom))
            {
                var view = new ImagePortraitImporterView();
                var list = view.FindControl<AddressListControl>("EntryList");
                Assert.NotNull(list);

                // List not yet loaded (Opened/LoadList fires on Show()).
                Assert.Equal(0, list!.ItemCount);

                // Target a NON-row-0 slot (3rd entry) so the "right slot"
                // assertion is meaningful — its B20-B23 differ from row 0's.
                const uint targetIdx = 2;
                uint targetAddr = DefaultPortraitBase + targetIdx * PortraitEntrySize;
                uint row0Addr = DefaultPortraitBase; // entry 0

                // NavigateTo BEFORE the list loads -> stashes the pending nav.
                view.NavigateTo(targetAddr);

                // Trigger Opened -> LoadList(), which auto-selects row 0 then
                // replays the pending nav so it overrides row 0.
                view.Show();
                Dispatcher.UIThread.RunJobs();

                Assert.True(list.ItemCount > 0);
                Assert.NotNull(list.SelectedItem);

                // The importer must land on the TARGET slot, NOT row 0.
                Assert.NotEqual(row0Addr, list.SelectedItem!.addr);
                Assert.Equal(targetAddr, list.SelectedItem!.addr);

                // ...and the four Detail NUDs must reflect the target slot's
                // B20-B23 (the core "right coords" assertion).
                var mouthX = view.FindControl<NumericUpDown>("MouthBlockXInput");
                var mouthY = view.FindControl<NumericUpDown>("MouthBlockYInput");
                var eyeX = view.FindControl<NumericUpDown>("EyeBlockXInput");
                var eyeY = view.FindControl<NumericUpDown>("EyeBlockYInput");
                Assert.NotNull(mouthX);
                Assert.NotNull(mouthY);
                Assert.NotNull(eyeX);
                Assert.NotNull(eyeY);

                Assert.Equal(rom.u8(targetAddr + 20), (byte)(mouthX!.Value ?? -1));
                Assert.Equal(rom.u8(targetAddr + 21), (byte)(mouthY!.Value ?? -1));
                Assert.Equal(rom.u8(targetAddr + 22), (byte)(eyeX!.Value ?? -1));
                Assert.Equal(rom.u8(targetAddr + 23), (byte)(eyeY!.Value ?? -1));
            }
        }

        [AvaloniaFact]
        public void NavigateTo_AfterListLoads_SelectsImmediatelyAndLatestWins()
        {
            var rom = BuildRom();
            using (UseRom(rom))
            {
                var view = new ImagePortraitImporterView();
                var list = view.FindControl<AddressListControl>("EntryList");
                Assert.NotNull(list);

                view.Show(); // list loads now (auto-selects row 0)
                Dispatcher.UIThread.RunJobs();
                Assert.True(list!.ItemCount > 0);

                uint addr1 = DefaultPortraitBase + 1 * PortraitEntrySize;
                uint addr3 = DefaultPortraitBase + 3 * PortraitEntrySize;

                // First navigation selects immediately.
                view.NavigateTo(addr1);
                Dispatcher.UIThread.RunJobs();
                Assert.NotNull(list.SelectedItem);
                Assert.Equal(addr1, list.SelectedItem!.addr);

                // Repeated navigation: latest wins (idempotent on re-call).
                view.NavigateTo(addr3);
                Dispatcher.UIThread.RunJobs();
                Assert.Equal(addr3, list.SelectedItem!.addr);
            }
        }

        // ===================================================================
        // JumpToImporter_Click — source/parity (handler navigates to CurrentAddr)
        // ===================================================================

        [Fact]
        public void JumpToImporter_Click_NavigatesToCurrentAddr_Source()
        {
            // Parity assertion: the Portrait editor's Import jump opens the
            // importer AND navigates to the edited portrait's slot, not a blank
            // open. Verified at source level (the handler body forwards
            // _vm.CurrentAddr to view.NavigateTo when non-zero) — a headless GUI
            // Show()/WindowManager round-trip is not deterministic here.
            string src = System.IO.File.ReadAllText(LocateView("ImagePortraitView.axaml.cs"));
            int idx = src.IndexOf("void JumpToImporter_Click", StringComparison.Ordinal);
            Assert.True(idx >= 0, "JumpToImporter_Click handler not found");
            int end = src.IndexOf("void JumpToStatusHeight_Click", idx, StringComparison.Ordinal);
            Assert.True(end > idx, "could not bound JumpToImporter_Click body");
            string body = src.Substring(idx, end - idx);

            Assert.Contains("Open<ImagePortraitImporterView>()", body);
            Assert.Contains("_vm.CurrentAddr != 0", body);
            Assert.Contains("view.NavigateTo(_vm.CurrentAddr)", body);
        }

        // ===================================================================
        // CurrentAddr == 0 -> no navigation, no throw
        // ===================================================================

        [Fact]
        public void JumpToImporter_Click_CurrentAddrZero_NoNavigationGuard_Source()
        {
            // The handler must guard navigation on CurrentAddr != 0 so a blank
            // (no-slot) Portrait editor opens the importer without forwarding a
            // 0 address (which would no-op against the list anyway) and never
            // throws.
            string src = System.IO.File.ReadAllText(LocateView("ImagePortraitView.axaml.cs"));
            int idx = src.IndexOf("void JumpToImporter_Click", StringComparison.Ordinal);
            int end = src.IndexOf("void JumpToStatusHeight_Click", idx, StringComparison.Ordinal);
            string body = src.Substring(idx, end - idx);

            // NavigateTo is inside the `if (_vm.CurrentAddr != 0)` guard.
            int guardPos = body.IndexOf("_vm.CurrentAddr != 0", StringComparison.Ordinal);
            int navPos = body.IndexOf("view.NavigateTo", StringComparison.Ordinal);
            Assert.True(guardPos >= 0 && navPos > guardPos,
                "view.NavigateTo must be gated behind the CurrentAddr != 0 guard");
        }

        [AvaloniaFact]
        public void NavigateTo_AddressNotInList_NoSelectionChange_NoThrow()
        {
            // A NavigateTo with an address that matches no row (e.g. the
            // CurrentAddr==0 path forwards nothing, but an out-of-table addr
            // could still arrive) must no-op gracefully: SelectAddress simply
            // finds no match and leaves the auto-selected row 0 in place.
            var rom = BuildRom();
            using (UseRom(rom))
            {
                var view = new ImagePortraitImporterView();
                var list = view.FindControl<AddressListControl>("EntryList");
                view.Show();
                Dispatcher.UIThread.RunJobs();
                uint row0 = DefaultPortraitBase;
                Assert.Equal(row0, list!.SelectedItem!.addr);

                // Unknown address — no exception, selection unchanged.
                view.NavigateTo(0xDEADBEEF);
                Dispatcher.UIThread.RunJobs();
                Assert.Equal(row0, list.SelectedItem!.addr);
            }
        }

        // Resolve the source file path of an Avalonia View for source-parity
        // assertions, walking up from the test bin dir to the repo root.
        static string LocateView(string fileName)
        {
            string dir = AppContext.BaseDirectory;
            for (int i = 0; i < 10 && dir != null; i++)
            {
                string candidate = System.IO.Path.Combine(dir, "FEBuilderGBA.Avalonia", "Views", fileName);
                if (System.IO.File.Exists(candidate)) return candidate;
                dir = System.IO.Path.GetDirectoryName(dir);
            }
            throw new System.IO.FileNotFoundException($"Could not locate {fileName} for source-parity assertion.");
        }
    }
}
