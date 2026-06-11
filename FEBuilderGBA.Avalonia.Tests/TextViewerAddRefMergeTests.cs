// SPDX-License-Identifier: GPL-3.0-or-later
// #1028 Slice A review fix (PR #1104): FindCrossReferences must merge the
// CoreState.UseTextIDCache comment so an added reference appears in the
// References list (mirrors WinForms TextForm.UpdateRef appending
// Program.UseTextIDCache.MakeUseTextID(id)).
using System.Collections.Generic;
using System.IO;
using FEBuilderGBA;
using FEBuilderGBA.Avalonia.ViewModels;
using Xunit;
using Xunit.Abstractions;

namespace FEBuilderGBA.Avalonia.Tests
{
    [Collection("SharedState")]
    public class TextViewerAddRefMergeTests : IClassFixture<RomFixture>
    {
        private readonly RomFixture _fixture;
        private readonly ITestOutputHelper _output;

        public TextViewerAddRefMergeTests(RomFixture fixture, ITestOutputHelper output)
        {
            _fixture = fixture;
            _output = output;
        }

        /// <summary>
        /// Minimal in-memory ITextIDCache so the merge behavior is provable
        /// WITHOUT depending on the on-disk config TSV the real TextIDCacheCore
        /// reads. Update/Save are no-ops here; GetName returns the seeded comment.
        /// </summary>
        sealed class StubCache : ITextIDCache
        {
            readonly Dictionary<uint, string> _d = new();
            public void Update(uint textid, string comment)
            {
                if (comment == "") _d.Remove(textid); else _d[textid] = comment;
            }
            public void Save(string romBaseFilename) { }
            public string GetName(uint textid) => _d.TryGetValue(textid, out var c) ? c : "";
        }

        [Fact]
        public void FindCrossReferences_IncludesSeededCacheComment()
        {
            if (!_fixture.IsAvailable)
            {
                _output.WriteLine("ROM not available; skipping integration test.");
                return;
            }

            var prev = CoreState.UseTextIDCache;
            try
            {
                // Use a text id that no ROM table references so the ONLY ref can
                // come from the cache merge (proves the new code path, and the
                // test would FAIL on the pre-merge code which returns empty).
                uint unusedId = 0xFFFE;
                const string comment = "Slice A demo reference";

                var stub = new StubCache();
                stub.Update(unusedId, comment);
                CoreState.UseTextIDCache = stub;

                var vm = new TextViewerViewModel();
                List<string> refs = vm.FindCrossReferences(unusedId);

                Assert.Contains(refs, r => r.Contains(comment));
            }
            finally
            {
                CoreState.UseTextIDCache = prev;
            }
        }

        [Fact]
        public void FindCrossReferences_NoCacheEntry_DoesNotAddRow()
        {
            if (!_fixture.IsAvailable)
            {
                _output.WriteLine("ROM not available; skipping integration test.");
                return;
            }

            var prev = CoreState.UseTextIDCache;
            try
            {
                CoreState.UseTextIDCache = new StubCache(); // empty
                var vm = new TextViewerViewModel();
                // 0xFFFE is unreferenced by ROM tables AND has no cache entry.
                Assert.Empty(vm.FindCrossReferences(0xFFFE));
            }
            finally
            {
                CoreState.UseTextIDCache = prev;
            }
        }

        /// <summary>
        /// End-to-end variant using the REAL TextIDCacheCore (per the review's
        /// wording): seed a user entry through the actual Core cache, set it on
        /// CoreState, and assert FindCrossReferences surfaces it. Cleans up the
        /// per-ROM TSV the cache writes so no stray config is left behind.
        /// </summary>
        [Fact]
        public void FindCrossReferences_IncludesRealCoreCacheComment()
        {
            if (!_fixture.IsAvailable)
            {
                _output.WriteLine("ROM not available; skipping integration test.");
                return;
            }

            ROM rom = CoreState.ROM!;
            var prev = CoreState.UseTextIDCache;
            string romBase = rom.Filename;
            string tsv = U.ConfigEtcFilename("textid_", romBase);
            bool preexisting = File.Exists(tsv);
            try
            {
                uint unusedId = 0xFFFD;
                const string comment = "Core cache merge proof";

                var cache = new TextIDCacheCore();
                cache.Update(unusedId, comment);
                CoreState.UseTextIDCache = cache;

                var vm = new TextViewerViewModel();
                List<string> refs = vm.FindCrossReferences(unusedId);
                Assert.Contains(refs, r => r.Contains(comment));
            }
            finally
            {
                CoreState.UseTextIDCache = prev;
                // Remove the entry we added in-memory and any TSV side effect so
                // the test leaves no stray config behind. We only created the
                // file via a Save() if one ran; this test never calls Save(), so
                // the on-disk TSV is unchanged — but guard anyway: delete only if
                // it did NOT pre-exist (we never want to clobber a real config).
                if (!preexisting && File.Exists(tsv))
                {
                    try { File.Delete(tsv); } catch { }
                }
            }
        }
    }
}
