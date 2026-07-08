using System.Collections.Generic;
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
    /// Issue #654 first-row icon resolution regression tests with true
    /// differential coverage: every list-prefix icon loader must attempt the
    /// icon lookup for the first row (whose prefix parses to ID 0).
    /// Previously the loaders short-circuited on <c>id == 0</c>, causing
    /// the first row of every class/item/portrait list view to render with
    /// a missing icon (and the icon column to collapse, shifting text left).
    ///
    /// The tests assert behaviour at three layers so that reintroducing
    /// the guard fails in at least one place:
    ///
    /// 1. <b>Source scan</b> — assert the production source files no
    ///    longer contain <c>if (xxxId == 0) return null;</c> guard lines
    ///    in the affected methods. Reintroducing the guard fails the
    ///    test directly at the source level.
    /// 2. <see cref="ListIconLoaders"/> — the per-loader public API used
    ///    by every Avalonia list view. Tested per ROM variant using
    ///    <c>RomTestHelper.WithRom</c>: when the underlying ROM data for
    ///    id 0 is valid (non-zero icon/portrait pointers), the loader
    ///    MUST return a non-null bitmap. Under the OLD code the loader
    ///    returned null even when the data was valid — these assertions
    ///    fail on the old code for any ROM where id-0 data is valid.
    /// 3. <see cref="PreviewIconHelper"/> — the underlying ROM-aware
    ///    helpers. Same per-ROM differential coverage.
    ///
    /// Tests skip gracefully when no ROM is available.
    /// </summary>
    [Collection("SharedState")]
    public class ListIconLoadersFirstRowTests : IClassFixture<RomFixture>
    {
        private readonly RomFixture _fixture;
        private readonly ITestOutputHelper _output;

        public ListIconLoadersFirstRowTests(RomFixture fixture, ITestOutputHelper output)
        {
            _fixture = fixture;
            _output = output;
        }

        // ==================================================================
        // Layer 0: source-text scan — rock-solid differential test that
        // catches re-introduction of the `if (id == 0) return null;` guard
        // at the SOURCE level. If the old code (with the guard) were in
        // place today, these tests would fail.
        // ==================================================================

        [Fact]
        public void ListIconLoaders_FixedLoaders_DoNotShortCircuitOnIdZero()
        {
            // The 3 list-prefix loaders fixed by #654 (ClassIconLoader,
            // ItemIconLoader, PortraitLoader) originally parsed the id from the
            // row text via U.atoh(items[index].name). (PortraitLoader now resolves
            // the id via ResolvePortraitId — AddrResult.tag, else the label — see
            // #1911 — but still has no id==0 short-circuit.) Pre-#654, each had an
            // `if (xxxId == 0) return null;` immediately after the parse;
            // the fix removed those lines.
            //
            // We extract each method's body and assert it does NOT contain
            // the specific guard line that was removed. Other loaders in
            // this file (e.g. UnitPortraitByIdLoader, Item*FromAddr*Loader)
            // legitimately keep an `if (xxxId == 0) return null;` because
            // their id comes from a ROM address, not a list prefix —
            // so we scope the assertion to the THREE fixed methods only.
            string src = ReadSource("FEBuilderGBA.Avalonia", "Services", "ListIconLoaders.cs");

            string classBody = ExtractMethodBody(src, "public static Bitmap? ClassIconLoader(List<AddrResult> items, int index)");
            Assert.DoesNotContain("if (classId == 0) return null;", classBody);

            string itemBody = ExtractMethodBody(src, "public static Bitmap? ItemIconLoader(List<AddrResult> items, int index)");
            Assert.DoesNotContain("if (itemId == 0) return null;", itemBody);

            string portraitBody = ExtractMethodBody(src, "public static Bitmap? PortraitLoader(List<AddrResult> items, int index)");
            Assert.DoesNotContain("if (portraitId == 0) return null;", portraitBody);

            _output.WriteLine("OK: ClassIconLoader/ItemIconLoader/PortraitLoader bodies do NOT contain id==0 short-circuit");
        }

        [Fact]
        public void PreviewIconHelper_FixedHelpers_DoNotShortCircuitOnIdZero()
        {
            // The 3 helpers fixed by #654 (LoadPortraitMini,
            // LoadClassWaitIconByClassId, LoadItemIconByItemId) had an
            // `xxxId == 0` disjunction in their `rom?.RomInfo == null ||
            // xxxId == 0` early-return guard. The fix removed the
            // disjunction; the helper now attempts the lookup for id 0.
            //
            // Other helpers in this file (e.g. BlitPortraitHalfRgba,
            // ResolveUnitPortraitIdByUnitId) legitimately keep a 0 check
            // because for them 0 has real "no portrait/no unit" semantics
            // — so we scope the assertion to the THREE fixed methods only.
            string src = ReadSource("FEBuilderGBA.Avalonia", "Services", "PreviewIconHelper.cs");

            string portraitBody = ExtractMethodBody(src, "public static IImage LoadPortraitMini(uint portraitId)");
            Assert.DoesNotContain("|| portraitId == 0", portraitBody);
            Assert.DoesNotContain("portraitId == 0 ||", portraitBody);

            string classBody = ExtractMethodBody(src, "public static IImage LoadClassWaitIconByClassId(uint classId)");
            Assert.DoesNotContain("|| classId == 0", classBody);
            Assert.DoesNotContain("classId == 0 ||", classBody);

            string itemBody = ExtractMethodBody(src, "public static IImage LoadItemIconByItemId(uint itemId)");
            Assert.DoesNotContain("|| itemId == 0", itemBody);
            Assert.DoesNotContain("itemId == 0 ||", itemBody);

            _output.WriteLine("OK: LoadPortraitMini/LoadClassWaitIconByClassId/LoadItemIconByItemId bodies have no id==0 disjunction");
        }

        // ==================================================================
        // Layer 1: ListIconLoaders — public API used by every list view.
        // Per-ROM theory: for any ROM where id-0 data is valid, the loader
        // MUST return non-null. The OLD code returned null regardless.
        // ==================================================================

        // #935: the row whose prefix parses to id 0 is the NULL entity. The
        // loader must agree with the By*Id helper, and BOTH must now be null
        // for id 0 (WinForms guards cid<=0 / item_id<=0 → blank). The old
        // assertion (`slot field != 0 ⟹ NotNull` for id 0) is the slot==0
        // behaviour #935 reverses, so it is removed here.

        [Theory]
        [MemberData(nameof(TestRomLocator.AllRoms), MemberType = typeof(TestRomLocator))]
        public void ClassIconLoader_FirstRowId0_MatchesPreviewIconHelper(string version, string? romPath)
        {
            if (romPath == null) { _output.WriteLine($"Skipping {version}: ROM not available"); return; }

            RomTestHelper.WithRom(version, () =>
            {
                EnsureImageService();
                var items = new List<AddrResult>
                {
                    new AddrResult(0x100, "00 PlaceholderClass", 0),
                };

                using var helperImg = PreviewIconHelper.LoadClassWaitIconByClassId(0);
                using var bmp = ListIconLoaders.ClassIconLoader(items, 0);

                // The loader resolves classId=0 from the "00" prefix and calls
                // the By*Id helper; their null-states MUST match.
                Assert.Equal(helperImg == null, bmp == null);

                // #935: id 0 is the null class → both helper and loader null.
                Assert.Null(helperImg);
                Assert.Null(bmp);

                _output.WriteLine($"{version} ClassIconLoader(0): id 0 → " +
                                  $"helper={(helperImg == null ? "null" : "Image")}, " +
                                  $"loader={(bmp == null ? "null" : "Bitmap")}");
            });
        }

        [Theory]
        [MemberData(nameof(TestRomLocator.AllRoms), MemberType = typeof(TestRomLocator))]
        public void ItemIconLoader_FirstRowId0_MatchesPreviewIconHelper(string version, string? romPath)
        {
            if (romPath == null) { _output.WriteLine($"Skipping {version}: ROM not available"); return; }

            RomTestHelper.WithRom(version, () =>
            {
                EnsureImageService();
                var items = new List<AddrResult>
                {
                    new AddrResult(0x100, "00 NullItem", 0),
                };

                using var helperImg = PreviewIconHelper.LoadItemIconByItemId(0);
                using var bmp = ListIconLoaders.ItemIconLoader(items, 0);

                Assert.Equal(helperImg == null, bmp == null);

                // #935: id 0 is the null item → both helper and loader null.
                Assert.Null(helperImg);
                Assert.Null(bmp);

                _output.WriteLine($"{version} ItemIconLoader(0): id 0 → " +
                                  $"helper={(helperImg == null ? "null" : "Image")}, " +
                                  $"loader={(bmp == null ? "null" : "Bitmap")}");
            });
        }

        [Theory]
        [MemberData(nameof(TestRomLocator.AllRoms), MemberType = typeof(TestRomLocator))]
        public void PortraitLoader_FirstRowId0_MatchesPreviewIconHelper(string version, string? romPath)
        {
            if (romPath == null) { _output.WriteLine($"Skipping {version}: ROM not available"); return; }

            RomTestHelper.WithRom(version, () =>
            {
                EnsureImageService();
                var items = new List<AddrResult>
                {
                    new AddrResult(0x100, "00 NullPortrait", 0),
                };

                bool ptrsValid = ArePortrait0PointersValid();
                using var helperImg = PreviewIconHelper.LoadPortraitMini(0);
                using var bmp = ListIconLoaders.PortraitLoader(items, 0);

                Assert.Equal(helperImg == null, bmp == null);

                if (ptrsValid)
                {
                    Assert.NotNull(helperImg);
                    Assert.NotNull(bmp);
                }
                _output.WriteLine($"{version} PortraitLoader(0): ptrsValid={ptrsValid}, " +
                                  $"helper={(helperImg == null ? "null" : "Image")}, " +
                                  $"loader={(bmp == null ? "null" : "Bitmap")}");
            });
        }

        // ==================================================================
        // Layer 2: PreviewIconHelper — ROM-derived expectations per ROM.
        // For each ROM where the ROM data is valid (non-zero), the helper
        // MUST return non-null. Under the OLD code the helper returned null
        // unconditionally for id 0, so any ROM with valid id-0 data fails
        // the old code.
        // ==================================================================

        [Theory]
        [MemberData(nameof(TestRomLocator.AllRoms), MemberType = typeof(TestRomLocator))]
        public void LoadPortraitMini_Id0_RespectsRomData(string version, string? romPath)
        {
            if (romPath == null) { _output.WriteLine($"Skipping {version}: ROM not available"); return; }

            RomTestHelper.WithRom(version, () =>
            {
                EnsureImageService();
                bool ptrsValid = ArePortrait0PointersValid();
                using var p0 = PreviewIconHelper.LoadPortraitMini(0);
                using var p1 = PreviewIconHelper.LoadPortraitMini(1);

                if (ptrsValid)
                {
                    // OLD code: null. NEW code: real portrait. This is the
                    // assertion that demonstrates the fix.
                    Assert.NotNull(p0);
                }
                else
                {
                    // Portrait 0 has null/invalid pointers — even post-fix
                    // the helper returns null. Assert it explicitly so a
                    // regression that masks every portrait 0 result as null
                    // would still be caught by other ROMs in this theory.
                    Assert.Null(p0);
                }
                _output.WriteLine($"{version} LoadPortraitMini(0): ptrsValid={ptrsValid}, " +
                                  $"p0={(p0 == null ? "null" : $"{p0.Width}x{p0.Height}")}, " +
                                  $"p1={(p1 == null ? "null" : "Image")}");
            });
        }

        // #935 NEW CONTRACT: the By*Id helpers now guard ONLY on entity id 0
        // (matching WinForms ClassForm.DrawWaitIcon `cid<=0` / ItemForm.DrawIcon
        // `item_id<=0`), NOT on the per-entity icon-SLOT field being 0. So:
        //   * id 0 → always null (the new entity-id guard).
        //   * a NONZERO entity whose slot field == 0 → must render icon-table
        //     slot 0, i.e. equal the direct slot-0 loader (non-null whenever
        //     slot 0 itself is renderable, null only when slot 0 is not).
        // These two tests previously asserted the OLD slot==0 behaviour
        // (By*Id(0) tracked ReadXIconIndexForId0()); they now lock in the fix.

        [Theory]
        [MemberData(nameof(TestRomLocator.AllRoms), MemberType = typeof(TestRomLocator))]
        public void LoadClassWaitIconByClassId_Id0_ReturnsNullAndSlot0EntityMatchesSlot0(string version, string? romPath)
        {
            if (romPath == null) { _output.WriteLine($"Skipping {version}: ROM not available"); return; }

            RomTestHelper.WithRom(version, () =>
            {
                EnsureImageService();

                // (1) id 0 → always null (new entity-id guard).
                using (var c0 = PreviewIconHelper.LoadClassWaitIconByClassId(0))
                {
                    Assert.Null(c0);
                }

                // (2) A nonzero class whose wait-icon SLOT field == 0 must
                // render the SAME result as the direct slot-0 loader: non-null
                // when slot 0 is renderable, null only when it is not.
                using var slot0Direct = PreviewIconHelper.LoadClassWaitIcon(0);
                bool slot0Renderable = slot0Direct != null;

                uint nonzeroClassWithSlot0 = FindNonzeroClassWithWaitIcon0();
                if (nonzeroClassWithSlot0 == 0)
                {
                    _output.WriteLine($"{version}: no nonzero class with waitIcon slot 0 — skipping slot-0 assertion");
                    return;
                }

                using var entityImg = PreviewIconHelper.LoadClassWaitIconByClassId(nonzeroClassWithSlot0);
                Assert.Equal(slot0Renderable, entityImg != null);
                if (slot0Renderable)
                {
                    Assert.NotNull(entityImg);
                }
                _output.WriteLine($"{version} LoadClassWaitIconByClassId: id0=null OK; " +
                                  $"class 0x{nonzeroClassWithSlot0:X} (slot0) → " +
                                  $"{(entityImg == null ? "null" : "Image")}, slot0Renderable={slot0Renderable}");
            });
        }

        [Theory]
        [MemberData(nameof(TestRomLocator.AllRoms), MemberType = typeof(TestRomLocator))]
        public void LoadItemIconByItemId_Id0_ReturnsNullAndSlot0EntityMatchesSlot0(string version, string? romPath)
        {
            if (romPath == null) { _output.WriteLine($"Skipping {version}: ROM not available"); return; }

            RomTestHelper.WithRom(version, () =>
            {
                EnsureImageService();

                // (1) id 0 → always null (new entity-id guard).
                using (var i0 = PreviewIconHelper.LoadItemIconByItemId(0))
                {
                    Assert.Null(i0);
                }

                // (2) A nonzero item whose icon SLOT field == 0 must render the
                // SAME result as the direct slot-0 loader.
                using var slot0Direct = PreviewIconHelper.LoadItemIcon(0);
                bool slot0Renderable = slot0Direct != null;

                uint nonzeroItemWithSlot0 = FindNonzeroItemWithIcon0();
                if (nonzeroItemWithSlot0 == 0)
                {
                    _output.WriteLine($"{version}: no nonzero item with icon slot 0 — skipping slot-0 assertion");
                    return;
                }

                using var entityImg = PreviewIconHelper.LoadItemIconByItemId(nonzeroItemWithSlot0);
                Assert.Equal(slot0Renderable, entityImg != null);
                if (slot0Renderable)
                {
                    Assert.NotNull(entityImg);
                }
                _output.WriteLine($"{version} LoadItemIconByItemId: id0=null OK; " +
                                  $"item 0x{nonzeroItemWithSlot0:X} (slot0) → " +
                                  $"{(entityImg == null ? "null" : "Image")}, slot0Renderable={slot0Renderable}");
            });
        }

        // ==================================================================
        // ItemShopCore prefix fix (#654): the per-slot row text MUST be
        // prefixed with the actual item ID, NOT the slot index. The Avalonia
        // shop icon loader extracts the icon ID from this prefix via U.atoh,
        // and a slot-index prefix would (a) make slot 0 hash to id 0
        // (yielding a null icon) and (b) load the icon for slot index N
        // rather than the actual item at that slot.
        // ==================================================================

        [Fact]
        public void ItemShopCore_ReadShopItems_FirstRowPrefixIsItemId()
        {
            if (!_fixture.IsAvailable)
            {
                _output.WriteLine($"SKIP: no ROM available");
                return;
            }

            var rom = _fixture.ROM!;
            var shops = ItemShopCore.MakeShopList(rom);
            if (shops.Count == 0)
            {
                _output.WriteLine($"SKIP: ROM {_fixture.Version} has no shops");
                return;
            }

            foreach (var shop in shops)
            {
                var items = ItemShopCore.ReadShopItems(rom, shop.addr);
                if (items.Count == 0) continue;

                uint itemId0 = rom.u8(items[0].addr);
                Assert.Equal(itemId0, U.atoh(items[0].name));
                Assert.StartsWith(U.ToHexString(itemId0), items[0].name);
                _output.WriteLine($"Shop @ 0x{shop.addr:X8}: slot 0 = '{items[0].name}', " +
                                  $"itemId at addr=0x{itemId0:X2}");
                return;
            }
            _output.WriteLine("SKIP: no non-empty shop found");
        }

        // ==================================================================
        // Helpers
        // ==================================================================

        /// <summary>
        /// Extract the body of a C# method from a source string by locating
        /// the method's signature and returning everything between the
        /// opening <c>{</c> and the matching closing <c>}</c>. Uses simple
        /// brace counting — good enough for the scope of these tests.
        /// </summary>
        private static string ExtractMethodBody(string src, string signature)
        {
            int sigIdx = src.IndexOf(signature);
            Assert.True(sigIdx >= 0, $"Method signature not found in source: {signature}");
            int openBrace = src.IndexOf('{', sigIdx);
            Assert.True(openBrace >= 0, $"Opening brace not found after signature: {signature}");

            int depth = 0;
            for (int i = openBrace; i < src.Length; i++)
            {
                if (src[i] == '{') depth++;
                else if (src[i] == '}')
                {
                    depth--;
                    if (depth == 0)
                        return src.Substring(openBrace + 1, i - openBrace - 1);
                }
            }
            Assert.Fail($"Closing brace not found for method: {signature}");
            return string.Empty;
        }

        /// <summary>
        /// Read a production source file from the repo, walking up from the
        /// test assembly to locate FEBuilderGBA.sln then descending into
        /// the named project subdirectory.
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

        /// <summary>
        /// Walk the class table and return the first class id &gt; 0 whose
        /// wait-icon SLOT field (offset +6) is 0. This is the exact case the
        /// #935 fix targets: a real (nonzero) class whose slot field happens to
        /// be 0 must still render icon-table slot 0. Returns 0 if no such class
        /// exists on the current ROM (caller skips the slot-0 assertion).
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
            // Scan a generous upper bound; bail at the end of ROM data.
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
        /// Walk the item table and return the first item id &gt; 0 whose icon
        /// SLOT field (offset +29) is 0. The #935 fix means such an item must
        /// still render icon-table slot 0. Returns 0 if none found.
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
        /// Check whether portrait id 0's image (+4) and palette (+8)
        /// pointers are both valid GBA pointers — the precondition for
        /// <see cref="PreviewIconHelper.LoadPortraitMini"/> returning a
        /// non-null bitmap post-fix.
        /// </summary>
        private static bool ArePortrait0PointersValid()
        {
            var rom = CoreState.ROM;
            if (rom?.RomInfo == null) return false;
            uint ptr = rom.RomInfo.portrait_pointer;
            if (ptr == 0) return false;
            uint portraitBase = rom.p32(ptr);
            if (!U.isSafetyOffset(portraitBase)) return false;
            uint dataSize = rom.RomInfo.portrait_datasize;
            if (dataSize == 0) dataSize = 28;
            uint portraitAddr = portraitBase + 0 * dataSize;
            if (portraitAddr + dataSize > (uint)rom.Data.Length) return false;
            uint imgPtr = rom.u32(portraitAddr + 4);
            uint palPtr = rom.u32(portraitAddr + 8);
            if (!U.isPointer(imgPtr) || !U.isPointer(palPtr)) return false;
            uint imgAddr = imgPtr - 0x08000000;
            uint palAddr = palPtr - 0x08000000;
            return U.isSafetyOffset(imgAddr) && U.isSafetyOffset(palAddr);
        }

        static void EnsureImageService()
        {
            if (CoreState.ImageService == null)
                CoreState.ImageService = new SkiaImageService();
        }
    }
}
