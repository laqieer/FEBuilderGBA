// SPDX-License-Identifier: GPL-3.0-or-later
// Core tests for ClassFormCore.SetSimClass (#428).
//
// ClassFormCore.SetSimClass extracts the class-base / class-grow / magic-ext
// inputs out of a class entry in the ROM table and feeds them into a
// GrowSimulator. The WinForms ClassForm.SetSimClass delegates to this helper
// so both Avalonia and WinForms share one source of truth for sim parity.
using FEBuilderGBA;
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

        // ============================================================
        // Unit Wait Icon <-> Class back-references (#991)
        // ============================================================

        static string? FindRom(string romName)
        {
            string thisAssembly = System.Reflection.Assembly.GetExecutingAssembly().Location;
            string? dir = System.IO.Path.GetDirectoryName(thisAssembly);
            for (int i = 0; i < 10 && dir != null; i++)
            {
                if (System.IO.File.Exists(System.IO.Path.Combine(dir, "FEBuilderGBA.sln")))
                {
                    string path = System.IO.Path.Combine(dir, "roms", romName);
                    if (System.IO.File.Exists(path)) return path;
                    break;
                }
                dir = System.IO.Path.GetDirectoryName(dir);
            }
            return null;
        }

        [Fact]
        public void WaitIcon_To_Class_To_MoveIcon_RoundTrips_FE8U()
        {
            string? path = FindRom("FE8U.gba");
            if (path == null) return; // skip when ROM absent

            var saved = CoreState.ROM;
            try
            {
                var rom = new ROM();
                rom.Load(path, out _);
                CoreState.ROM = rom;

                uint classBase = rom.p32(rom.RomInfo.class_pointer);
                uint classSize = rom.RomInfo.class_datasize;

                // Find ANY class with a non-zero wait-icon id (ROM-content
                // agnostic — works on vanilla or modified ROMs).
                uint pickedClass = 0, waitId = 0;
                for (uint c = 1; c <= 128; c++)
                {
                    uint a = classBase + c * classSize;
                    if (a + classSize > (uint)rom.Data.Length) break;
                    uint w = rom.u8(a + 6);
                    if (w > 0) { pickedClass = c; waitId = w; break; }
                }
                Assert.True(pickedClass > 0, "At least one class should have a non-zero wait icon id.");

                // Reverse: wait-icon id -> owning class id. First class wins, so
                // the resolved class's wait-icon must equal waitId (round-trip
                // invariant — the resolved class may differ from pickedClass if
                // an earlier class shares the same wait icon).
                uint cid = ClassFormCore.GetClassIdWhereWaitIconId(rom, waitId);
                Assert.NotEqual(U.NOT_FOUND, cid);
                uint cidWaitId = rom.u8(classBase + cid * classSize + 6);
                Assert.Equal(waitId, cidWaitId);

                // Move icon for that class = u8 @ class+4.
                uint moveIcon = ClassFormCore.GetClassMoveIcon(rom, cid);
                Assert.Equal((uint)rom.u8(classBase + cid * classSize + 4), moveIcon);
            }
            finally { CoreState.ROM = saved; }
        }

        [Fact]
        public void GetClassNameWhereWaitIconId_NonEmptyForOwnedIcon_FE8U()
        {
            string? path = FindRom("FE8U.gba");
            if (path == null) return;

            var saved = CoreState.ROM;
            try
            {
                var rom = new ROM();
                rom.Load(path, out _);
                CoreState.ROM = rom;

                uint classBase = rom.p32(rom.RomInfo.class_pointer);
                uint classSize = rom.RomInfo.class_datasize;

                // Find any class with a non-zero wait icon (ROM-content agnostic).
                uint waitId = 0;
                for (uint c = 1; c <= 128; c++)
                {
                    uint a = classBase + c * classSize;
                    if (a + classSize > (uint)rom.Data.Length) break;
                    uint w = rom.u8(a + 6);
                    if (w > 0) { waitId = w; break; }
                }
                Assert.True(waitId > 0, "At least one class should own a wait icon.");

                string name = ClassFormCore.GetClassNameWhereWaitIconId(rom, waitId);
                Assert.False(string.IsNullOrEmpty(name), "Owned wait icon must resolve a class name.");
            }
            finally { CoreState.ROM = saved; }
        }

        [Fact]
        public void GetClassMoveIcon_ReadsPlusFour_Synthetic()
        {
            var saved = CoreState.ROM;
            try
            {
                ROM rom = MakeSyntheticClassRom(out uint classBase, out uint classSize);
                CoreState.ROM = rom;
                // class 3's move icon byte was planted at class+4.
                Assert.Equal(0x13u, ClassFormCore.GetClassMoveIcon(rom, 3));
            }
            finally { CoreState.ROM = saved; }
        }

        [Fact]
        public void GetClassMoveIcon_ClassZero_ReturnsNotFound()
        {
            var saved = CoreState.ROM;
            try
            {
                ROM rom = MakeSyntheticClassRom(out _, out _);
                CoreState.ROM = rom;
                Assert.Equal(U.NOT_FOUND, ClassFormCore.GetClassMoveIcon(rom, 0));
            }
            finally { CoreState.ROM = saved; }
        }

        [Fact]
        public void GetClassIdWhereWaitIconId_RoundTrips_Synthetic()
        {
            var saved = CoreState.ROM;
            try
            {
                ROM rom = MakeSyntheticClassRom(out _, out _);
                CoreState.ROM = rom;
                // Classes 1/2/3 have wait icons 0x11/0x22/0x33.
                Assert.Equal(2u, ClassFormCore.GetClassIdWhereWaitIconId(rom, 0x22));
            }
            finally { CoreState.ROM = saved; }
        }

        [Fact]
        public void GetClassIdWhereWaitIconId_Miss_ReturnsNotFound_NoThrow()
        {
            var saved = CoreState.ROM;
            try
            {
                ROM rom = MakeSyntheticClassRom(out _, out _);
                CoreState.ROM = rom;
                // No class has wait-icon 0xFE → NOT_FOUND, no throw.
                Assert.Equal(U.NOT_FOUND, ClassFormCore.GetClassIdWhereWaitIconId(rom, 0xFE));
            }
            finally { CoreState.ROM = saved; }
        }

        [Fact]
        public void GetClassIdWhereWaitIconId_NullRom_ReturnsNotFound()
        {
            Assert.Equal(U.NOT_FOUND, ClassFormCore.GetClassIdWhereWaitIconId(null, 1));
        }

        // Build a tiny synthetic ROM with a 4-class table (each 0x10 bytes). The
        // existence callback counts while u8(class+4) != 0, so classes 1..3 have
        // a non-zero +4 (move-icon) byte; class 4's +4 is 0 → terminator.
        static ROM MakeSyntheticClassRom(out uint classBase, out uint classSize)
        {
            const uint CLASS_PTR_SLOT = 0x100;
            classBase = 0x1000;
            classSize = 0x10;

            var rom = new ROM();
            byte[] data = new byte[0x10000];
            rom.LoadLow("synth.gba", data, "BE8E01");
            var info = new ClassStubRomInfo(CLASS_PTR_SLOT, classSize);
            typeof(ROM).GetProperty("RomInfo")?.GetSetMethod(true)?.Invoke(rom, new object[] { info });

            U.write_u32(rom.Data, CLASS_PTR_SLOT, U.toPointer(classBase));
            // classes 1..3 exist (non-zero +4 = move-icon); class 0 always counts.
            for (uint c = 1; c <= 3; c++)
                rom.write_u8(classBase + c * classSize + 4, (byte)(0x10 + c));
            // class 4 terminator (+4 == 0, already zero).
            // Plant distinct wait-icon ids at +6 for classes 1..3.
            rom.write_u8(classBase + 1 * classSize + 6, 0x11);
            rom.write_u8(classBase + 2 * classSize + 6, 0x22);
            rom.write_u8(classBase + 3 * classSize + 6, 0x33);
            return rom;
        }

        sealed class ClassStubRomInfo : ROMFEINFO
        {
            public ClassStubRomInfo(uint classPointer, uint classDataSize)
            {
                this.version = 8;
                this.class_pointer = classPointer;
                this.class_datasize = classDataSize;
            }
        }
    }
}
