using System;
using System.IO;
using System.Reflection;
using FEBuilderGBA;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.SkiaSharp;
using Xunit;
using Xunit.Abstractions;

namespace FEBuilderGBA.Avalonia.Tests
{
    /// <summary>
    /// Issue #935 regression sweep: the list-icon By*Id helpers must guard on
    /// the ENTITY id being 0 (the WinForms <c>ClassForm.DrawWaitIcon</c>
    /// <c>cid&lt;=0</c> / <c>ItemForm.DrawIcon</c> <c>item_id&lt;=0</c>
    /// semantic) and NOT on the per-entity icon-SLOT field being 0.
    ///
    /// The pre-#935 code short-circuited on the SLOT value being 0, so any
    /// real (nonzero) class/item whose slot field happened to be 0 had its
    /// icon hidden — collapsing the icon column across ~30 list editors
    /// (Class / Item / Arena Enemy Weapon, etc.). The correct behaviour:
    ///   * entity id 0 → null (the null entity stays blank);
    ///   * a nonzero entity whose slot field == 0 → render icon-table slot 0
    ///     (slot 0 is a real sprite/icon).
    ///
    /// Coverage layers:
    ///   1. ROM-backed CLASS sweep — first nonzero class with waitIcon slot 0.
    ///   2. ROM-backed ITEM sweep — first nonzero item with icon slot 0.
    ///   3. id==0 → null (both helpers), per ROM.
    ///   4. SOURCE scan (no ROM) — the removed slot==0 guards must be gone and
    ///      the new entity-id guards present, so a regression that reintroduces
    ///      the slot short-circuit fails even when no ROM is available.
    ///
    /// Tests skip gracefully (no failure) when no ROM is available.
    /// </summary>
    [Collection("SharedState")]
    public class FirstRowIconSweepTests
    {
        private readonly ITestOutputHelper _output;

        public FirstRowIconSweepTests(ITestOutputHelper output)
        {
            _output = output;
        }

        // ==================================================================
        // Layer 1: CLASS — a nonzero class whose wait-icon SLOT field is 0
        // must still render icon-table slot 0 (NOT be hidden).
        // ==================================================================

        [Theory]
        [MemberData(nameof(TestRomLocator.AllRoms), MemberType = typeof(TestRomLocator))]
        public void Class_NonzeroIdWithWaitIconSlot0_RendersSlot0(string version, string? romPath)
        {
            if (romPath == null) { _output.WriteLine($"Skipping {version}: ROM not available"); return; }

            RomTestHelper.WithRom(version, () =>
            {
                EnsureImageService();

                // Slot 0 of the wait-icon table must itself be renderable for
                // this assertion to mean anything; otherwise there is nothing
                // to compare against and we skip (don't fail).
                using var slot0 = PreviewIconHelper.LoadClassWaitIcon(0);
                if (slot0 == null)
                {
                    _output.WriteLine($"{version}: class wait-icon slot 0 not renderable — skipping");
                    return;
                }

                uint classId = FindNonzeroClassWithWaitIcon0();
                if (classId == 0)
                {
                    _output.WriteLine($"{version}: no nonzero class with waitIcon slot 0 — skipping");
                    return;
                }

                using var byId = PreviewIconHelper.LoadClassWaitIconByClassId(classId);

                // The fix: a nonzero class whose slot field is 0 renders slot 0.
                Assert.NotNull(byId);
                AssertSameImage(slot0, byId);

                _output.WriteLine($"{version} class 0x{classId:X} (waitIcon slot 0) → " +
                                  $"{byId.Width}x{byId.Height}, matches slot-0 loader");
            });
        }

        // ==================================================================
        // Layer 2: ITEM — a nonzero item whose icon SLOT field is 0 must still
        // render icon-table slot 0.
        // ==================================================================

        [Theory]
        [MemberData(nameof(TestRomLocator.AllRoms), MemberType = typeof(TestRomLocator))]
        public void Item_NonzeroIdWithIconSlot0_RendersSlot0(string version, string? romPath)
        {
            if (romPath == null) { _output.WriteLine($"Skipping {version}: ROM not available"); return; }

            RomTestHelper.WithRom(version, () =>
            {
                EnsureImageService();

                using var slot0 = PreviewIconHelper.LoadItemIcon(0);
                if (slot0 == null)
                {
                    _output.WriteLine($"{version}: item icon slot 0 not renderable — skipping");
                    return;
                }

                uint itemId = FindNonzeroItemWithIcon0();
                if (itemId == 0)
                {
                    _output.WriteLine($"{version}: no nonzero item with icon slot 0 — skipping");
                    return;
                }

                using var byId = PreviewIconHelper.LoadItemIconByItemId(itemId);

                Assert.NotNull(byId);
                AssertSameImage(slot0, byId);

                _output.WriteLine($"{version} item 0x{itemId:X} (icon slot 0) → " +
                                  $"{byId.Width}x{byId.Height}, matches slot-0 loader");
            });
        }

        // ==================================================================
        // Layer 3: id==0 → null (the new entity-id guard), per ROM.
        // ==================================================================

        [Theory]
        [MemberData(nameof(TestRomLocator.AllRoms), MemberType = typeof(TestRomLocator))]
        public void Id0_BothHelpers_ReturnNull(string version, string? romPath)
        {
            if (romPath == null) { _output.WriteLine($"Skipping {version}: ROM not available"); return; }

            RomTestHelper.WithRom(version, () =>
            {
                EnsureImageService();

                using var c0 = PreviewIconHelper.LoadClassWaitIconByClassId(0);
                using var i0 = PreviewIconHelper.LoadItemIconByItemId(0);

                Assert.Null(c0);
                Assert.Null(i0);

                _output.WriteLine($"{version}: LoadClassWaitIconByClassId(0)=null, LoadItemIconByItemId(0)=null OK");
            });
        }

        // ==================================================================
        // Layer 4: SOURCE scan — no ROM needed. Asserts the removed slot==0
        // short-circuits are gone and the new entity-id guards are present.
        // Reintroducing the slot guard fails here even with no ROM available.
        // ==================================================================

        [Fact]
        public void Source_PreviewIconHelper_HasEntityIdGuards_NotSlotGuards()
        {
            string src = ReadSource("FEBuilderGBA.Avalonia", "Services", "PreviewIconHelper.cs");

            // The OLD slot==0 short-circuits must be gone.
            Assert.DoesNotContain("if (waitIconIndex == 0) return null", src);
            Assert.DoesNotContain("if (iconIndex == 0) return null", src);

            // The NEW entity-id guards must be present.
            Assert.Contains("if (classId == 0) return null", src);
            Assert.Contains("if (itemId == 0) return null", src);

            _output.WriteLine("OK: PreviewIconHelper has entity-id==0 guards and no icon-slot==0 short-circuits");
        }

        // ==================================================================
        // Helpers
        // ==================================================================

        /// <summary>
        /// Walk the class table; return the first class id &gt; 0 whose
        /// wait-icon SLOT field (offset +6) is 0. Returns 0 if none found.
        /// </summary>
        private static uint FindNonzeroClassWithWaitIcon0()
        {
            var rom = CoreState.ROM;
            if (rom?.RomInfo == null) return 0;
            uint classPtr = rom.RomInfo.class_pointer;
            if (classPtr == 0) return 0;
            uint classBase = rom.p32(classPtr);
            if (!U.isSafetyOffset(classBase)) return 0;
            uint classSize = rom.RomInfo.class_datasize;
            if (classSize == 0) return 0;
            for (uint id = 1; id < 0x100; id++)
            {
                uint classAddr = classBase + id * classSize;
                if (classAddr + classSize > (uint)rom.Data.Length) break;
                if (rom.u8(classAddr + 6) == 0)
                    return id;
            }
            return 0;
        }

        /// <summary>
        /// Walk the item table; return the first item id &gt; 0 whose icon
        /// SLOT field (offset +29) is 0. Returns 0 if none found.
        /// </summary>
        private static uint FindNonzeroItemWithIcon0()
        {
            var rom = CoreState.ROM;
            if (rom?.RomInfo == null) return 0;
            uint itemPtr = rom.RomInfo.item_pointer;
            if (itemPtr == 0) return 0;
            uint itemBase = rom.p32(itemPtr);
            if (!U.isSafetyOffset(itemBase)) return 0;
            uint itemSize = rom.RomInfo.item_datasize;
            if (itemSize == 0) return 0;
            for (uint id = 1; id < 0x100; id++)
            {
                uint itemAddr = itemBase + id * itemSize;
                if (itemAddr + itemSize > (uint)rom.Data.Length) break;
                if (rom.u8(itemAddr + 29) == 0)
                    return id;
            }
            return 0;
        }

        /// <summary>
        /// Assert two indexed images represent the same icon: same dimensions
        /// and byte-identical pixel data. Confirms the By*Id helper produced
        /// the exact slot-0 sprite (not merely "some non-null image").
        /// </summary>
        private static void AssertSameImage(IImage expected, IImage actual)
        {
            Assert.NotNull(expected);
            Assert.NotNull(actual);
            Assert.Equal(expected.Width, actual.Width);
            Assert.Equal(expected.Height, actual.Height);
            Assert.Equal(expected.GetPixelData(), actual.GetPixelData());
        }

        /// <summary>
        /// Read a production source file from the repo, walking up from the
        /// test assembly to locate FEBuilderGBA.sln then descending into the
        /// named project subdirectory. Mirrors
        /// <c>ListIconLoadersFirstRowTests.ReadSource</c>.
        /// </summary>
        private static string ReadSource(params string[] pathSegments)
        {
            string? dir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            for (int i = 0; i < 10 && dir != null; i++)
            {
                if (File.Exists(Path.Combine(dir, "FEBuilderGBA.sln")))
                {
                    string p = Path.Combine(pathSegments);
                    string full = Path.Combine(dir, p);
                    if (File.Exists(full))
                        return File.ReadAllText(full);
                    Assert.Fail($"Source file not found: {full}");
                }
                dir = Path.GetDirectoryName(dir);
            }
            Assert.Fail("Could not locate FEBuilderGBA.sln from test assembly");
            return string.Empty;
        }

        private static void EnsureImageService()
        {
            if (CoreState.ImageService == null)
                CoreState.ImageService = new SkiaImageService();
        }
    }
}
