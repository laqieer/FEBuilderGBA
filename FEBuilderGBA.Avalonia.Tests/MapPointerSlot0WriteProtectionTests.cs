// SPDX-License-Identifier: GPL-3.0-or-later
// MapPointerViewModel slot-0 (reserved NULL) write-protection parity (#1416).
//
// WinForms makes a write to PLIST slot 0 physically impossible by default:
//   * MapPointerForm.cs sets InputFormRef.UseWriteProtectionID00 = true;
//   * InputFormRef.CheckWriteProtectionID00 blocks the selected row 0 when the
//     default option func_write_00 == Deny (2);
//   * MapPointerForm.Write_Plsit independently hard-rejects plist == 0.
//
// The Avalonia MapPointerViewModel had NEITHER guard — WriteMapPointer only
// checked CurrentAddr == 0 (a ROM offset, never 0 for a valid table). These
// tests prove the new SelectedId == 0 guard:
//   * rejects the write (returns a non-null error message);
//   * leaves the ROM byte-identical (no mutation);
//   * still allows a non-zero slot to be written;
//   * derives SelectedId from the active table base in LoadMapPointer.
using System.Collections.Generic;
using FEBuilderGBA;
using FEBuilderGBA.Avalonia.ViewModels;
using Xunit;

namespace FEBuilderGBA.Avalonia.Tests
{
    public class MapPointerSlot0WriteProtectionTests : IClassFixture<RomFixture>
    {
        readonly RomFixture _rom;

        public MapPointerSlot0WriteProtectionTests(RomFixture rom)
        {
            _rom = rom;
        }

        // ----------------------------------------------------------------
        // 1. Selecting slot 0 (the NULL row) rejects the write and leaves the
        //    ROM unchanged — even when the user typed a non-zero pointer.
        // ----------------------------------------------------------------
        [Fact]
        public void Slot0_Write_IsRejected_AndRomUnchanged()
        {
            if (!_rom.IsAvailable) return; // skip when no ROM available

            ROM rom = CoreState.ROM!;
            var vm = new MapPointerViewModel();

            // Build the MAP filter list and load the FIRST row (slot 0 = NULL).
            List<AddrResult> rows = vm.LoadMapPointerList(0);
            Assert.NotEmpty(rows);
            AddrResult slot0 = rows[0];
            Assert.Equal(0u, slot0.tag); // row 0's slot id is 0

            uint before = rom.u32(slot0.addr);

            vm.LoadMapPointer(slot0.addr);
            Assert.Equal(0u, vm.SelectedId); // derived from base addr

            // User tries to write a bogus non-zero pointer into the NULL slot.
            vm.MapDataPointer = 0x08123456;
            string? error = vm.WriteMapPointer();

            Assert.NotNull(error); // write rejected
            Assert.Equal(before, rom.u32(slot0.addr)); // ROM untouched
        }

        // ----------------------------------------------------------------
        // 2. The View's authoritative SelectedId (from AddrResult.tag) also
        //    blocks slot 0, independent of the derived value.
        // ----------------------------------------------------------------
        [Fact]
        public void Slot0_ViaSelectedIdTag_IsRejected()
        {
            if (!_rom.IsAvailable) return;

            ROM rom = CoreState.ROM!;
            var vm = new MapPointerViewModel();
            List<AddrResult> rows = vm.LoadMapPointerList(0);
            Assert.NotEmpty(rows);

            AddrResult slot0 = rows[0];
            uint before = rom.u32(slot0.addr);

            vm.LoadMapPointer(slot0.addr);
            // Mirror MapPointerView.OnSelected pushing the row tag authoritatively.
            vm.SelectedId = slot0.tag;
            vm.MapDataPointer = 0xDEADBEEF;

            string? error = vm.WriteMapPointer();
            Assert.NotNull(error);
            Assert.Equal(before, rom.u32(slot0.addr));
        }

        // ----------------------------------------------------------------
        // 3. A non-zero slot still writes successfully — then restore the ROM
        //    so the shared fixture is left byte-identical.
        // ----------------------------------------------------------------
        [Fact]
        public void NonZeroSlot_Write_Succeeds_AndMutatesRom()
        {
            if (!_rom.IsAvailable) return;

            ROM rom = CoreState.ROM!;
            var vm = new MapPointerViewModel();
            List<AddrResult> rows = vm.LoadMapPointerList(0);
            Assert.NotEmpty(rows);

            // Find a writable non-NULL row (slot id > 0). Assert one exists so a
            // regression that collapses the list to only slot 0 (or mistags
            // rows) fails LOUDLY instead of silently passing (Copilot PR #1478).
            AddrResult? target = null;
            foreach (AddrResult r in rows)
            {
                if (r.tag != 0) { target = r; break; }
            }
            Assert.NotNull(target); // the MAP list must have a non-NULL slot
            AddrResult writable = target!;

            uint before = rom.u32(writable.addr);
            try
            {
                vm.LoadMapPointer(writable.addr);
                Assert.NotEqual(0u, vm.SelectedId);

                uint newValue = before ^ 0x4u; // any different, harmless delta
                vm.MapDataPointer = newValue;
                string? error = vm.WriteMapPointer();

                Assert.Null(error); // allowed
                Assert.Equal(newValue, rom.u32(writable.addr));
            }
            finally
            {
                // Restore byte-identical for the shared fixture.
                rom.write_u32(writable.addr, before);
            }
        }

        // ----------------------------------------------------------------
        // 4. WriteMapPointer with no slot loaded returns an error and never
        //    touches the ROM (CurrentAddr == 0 short-circuit).
        // ----------------------------------------------------------------
        [Fact]
        public void NoSlotLoaded_Write_IsRejected()
        {
            if (!_rom.IsAvailable) return;

            var vm = new MapPointerViewModel();
            // CurrentAddr defaults to 0 → no slot loaded.
            Assert.Equal(0u, vm.CurrentAddr);
            string? error = vm.WriteMapPointer();
            Assert.NotNull(error);
        }
    }
}
