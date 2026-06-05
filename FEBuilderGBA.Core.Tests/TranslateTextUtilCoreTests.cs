using System;
using System.Collections.Generic;
using System.IO;
using Xunit;
using FEBuilderGBA;

namespace FEBuilderGBA.Core.Tests
{
    /// <summary>
    /// Offline unit tests for the cross-platform translate seam
    /// (<see cref="TranslateTextUtilCore"/>) that powers the Avalonia Text Editor
    /// Translate tab (#967, follow-up to #949).
    ///
    /// These run fully offline: <see cref="TranslateTextUtilCore.TranslateText"/>
    /// is exercised with a STUB recording translator so no network call is ever
    /// made, and we can assert exactly which segments reached the translator.
    /// </summary>
    [Collection("SharedState")]
    public class TranslateTextUtilCoreTests
    {
        // ----------------------------------------------------------------
        // SplitEscapeSegments — round-trip + isolation
        // ----------------------------------------------------------------

        public static IEnumerable<object[]> RoundTripCases()
        {
            yield return new object[] { "" };
            yield return new object[] { "plain text only" };
            yield return new object[] { "@0001" };                       // pure code
            yield return new object[] { "@0001hello" };                  // leading code
            yield return new object[] { "hello@0001" };                  // trailing code
            yield return new object[] { "abc@0001def" };                 // code in the middle (WF would drop "def")
            yield return new object[] { "@0001abc@0002def@0003" };       // multiple codes
            yield return new object[] { "@0001@0002@0003" };             // consecutive codes
            yield return new object[] { "line1@0003\r\nline2" };         // @0003 + CRLF bundle
            yield return new object[] { "前半@0001後半@0003\r\nつづき" }; // multibyte + codes
            // #971 — literal '@' must NOT be sliced; round-trip still holds.
            yield return new object[] { "email@example.com" };           // literal at-sign (non-hex tail)
            yield return new object[] { "hello@catworld" };              // literal at-sign (non-hex tail)
            yield return new object[] { "abc@" };                        // trailing bare '@'
            yield return new object[] { "abc@123" };                     // '@' + fewer than 4 hex
            yield return new object[] { "abc@12Z4" };                    // '@' + non-hex within 4 chars
            yield return new object[] { "名前@0001は@example" };         // real code + literal '@'
        }

        [Theory]
        [MemberData(nameof(RoundTripCases))]
        public void SplitEscapeSegments_RoundTrips(string input)
        {
            List<string> segs = TranslateTextUtilCore.SplitEscapeSegments(input);
            // The whole point: re-joining the segments must reproduce the input
            // byte-for-byte so codes (and any trailing literal) are never lost.
            Assert.Equal(input, string.Concat(segs));
        }

        [Fact]
        public void SplitEscapeSegments_IsolatesCodeSegments()
        {
            List<string> segs = TranslateTextUtilCore.SplitEscapeSegments("abc@0001def@0002");
            Assert.Equal(new[] { "abc", "@0001", "def", "@0002" }, segs);
            Assert.True(TranslateTextUtilCore.IsEscapeSegment("@0001"));
            Assert.True(TranslateTextUtilCore.IsEscapeSegment("@0002"));
            Assert.False(TranslateTextUtilCore.IsEscapeSegment("abc"));
            Assert.False(TranslateTextUtilCore.IsEscapeSegment("def"));
        }

        [Fact]
        public void SplitEscapeSegments_BundlesParagraphCodeWithCRLF()
        {
            List<string> segs = TranslateTextUtilCore.SplitEscapeSegments("a@0003\r\nb");
            // The @0003 + CRLF is a SINGLE segment, classified as an escape.
            Assert.Equal(new[] { "a", "@0003\r\n", "b" }, segs);
            Assert.True(TranslateTextUtilCore.IsEscapeSegment("@0003\r\n"));
        }

        [Fact]
        public void IsEscapeSegment_RequiresFourHexDigits()
        {
            Assert.True(TranslateTextUtilCore.IsEscapeSegment("@0001"));
            Assert.True(TranslateTextUtilCore.IsEscapeSegment("@ABCD"));
            Assert.True(TranslateTextUtilCore.IsEscapeSegment("@00FF"));
            // A bare '@' or '@' + non-hex text is a LITERAL (still translated).
            Assert.False(TranslateTextUtilCore.IsEscapeSegment("@"));
            Assert.False(TranslateTextUtilCore.IsEscapeSegment("@cat"));
            Assert.False(TranslateTextUtilCore.IsEscapeSegment("@XYZW"));
            Assert.False(TranslateTextUtilCore.IsEscapeSegment("hello"));
            Assert.False(TranslateTextUtilCore.IsEscapeSegment(""));
        }

        // ----------------------------------------------------------------
        // #971 — literal '@' must NOT be fragmented by the splitter
        // ----------------------------------------------------------------

        [Theory]
        [InlineData("email@example.com")] // non-hex tail after '@'
        [InlineData("hello@catworld")]    // non-hex tail after '@'
        [InlineData("abc@")]              // trailing bare '@'
        [InlineData("abc@123")]           // '@' + fewer than 4 hex digits
        [InlineData("abc@12Z4")]          // '@' + non-hex within the first 4 chars
        public void SplitEscapeSegments_LiteralAtSign_StaysOneSegment(string input)
        {
            List<string> segs = TranslateTextUtilCore.SplitEscapeSegments(input);
            // A literal '@' is not a control code, so the whole string is ONE
            // literal segment (no artificial @XXXX slicing).
            Assert.Equal(new[] { input }, segs);
            Assert.False(TranslateTextUtilCore.IsEscapeSegment(input));
        }

        [Fact]
        public void SplitEscapeSegments_RealCodeAndLiteralAtSign_SplitCorrectly()
        {
            // The real @0001 code is its own segment; the literal "@example"
            // stays attached to its surrounding literal run (#971).
            List<string> segs = TranslateTextUtilCore.SplitEscapeSegments("名前@0001は@example");
            Assert.Equal(new[] { "名前", "@0001", "は@example" }, segs);
            Assert.True(TranslateTextUtilCore.IsEscapeSegment("@0001"));
            Assert.False(TranslateTextUtilCore.IsEscapeSegment("は@example"));
        }

        [Theory]
        [InlineData("email@example.com")]
        [InlineData("hello@catworld")]
        public void TranslateText_LiteralAtSign_RoutedAsOneTranslatorCall(string input)
        {
            var stub = new RecordingTranslator();

            string result = TranslateTextUtilCore.TranslateText(
                input, "ja", "en", dic: null, useGoogle: true, translator: stub.Translate);

            // The whole literal (with its '@') reaches the translator as ONE call,
            // not fragmented into "email" + "@exam" + "ple.com" etc.
            Assert.Equal(new[] { input }, stub.Calls);
            Assert.Equal("[T:" + input + "]", result);
        }

        [Fact]
        public void TranslateText_MixedRealCodeAndLiteralAtSign_ProtectsCodeOnly()
        {
            var stub = new RecordingTranslator();

            string result = TranslateTextUtilCore.TranslateText(
                "名前@0001は@example", "ja", "en", dic: null, useGoogle: true, translator: stub.Translate);

            // @0001 is protected (never translated); the literal "@example" stays
            // glued to "は" and is translated as a single segment.
            Assert.Equal(new[] { "名前", "は@example" }, stub.Calls);
            Assert.Equal("[T:名前]@0001[T:は@example]", result);
        }

        // ----------------------------------------------------------------
        // TranslateText — code protection + dictionary-first (stub translator)
        // ----------------------------------------------------------------

        sealed class RecordingTranslator
        {
            public readonly List<string> Calls = new List<string>();
            public string Translate(string text, string from, string to)
            {
                Calls.Add(text);
                return "[T:" + text + "]"; // distinct, deterministic, offline
            }
        }

        [Fact]
        public void TranslateText_NeverPassesCodesToTranslator_AndKeepsThemVerbatim()
        {
            var stub = new RecordingTranslator();
            string input = "hello@0001world@0003\r\nmore";

            string result = TranslateTextUtilCore.TranslateText(
                input, "ja", "en", dic: null, useGoogle: true, translator: stub.Translate);

            // Only the three literal text segments were translated; codes never were.
            Assert.Equal(new[] { "hello", "world", "more" }, stub.Calls);
            // The codes appear verbatim, in place, in the assembled output.
            Assert.Equal("[T:hello]@0001[T:world]@0003\r\n[T:more]", result);
            Assert.Contains("@0001", result);
            Assert.Contains("@0003\r\n", result);
        }

        [Fact]
        public void TranslateText_DictionaryHit_BypassesTranslator()
        {
            var stub = new RecordingTranslator();
            var dic = new Dictionary<string, string> { ["HELLO"] = "こんにちは" };

            string result = TranslateTextUtilCore.TranslateText(
                "hello@0001world", "ja", "en", dic, useGoogle: true, translator: stub.Translate);

            // "hello" was a glossary hit (case-insensitive) → no translator call;
            // "world" was not → translator called for it only.
            Assert.Equal(new[] { "world" }, stub.Calls);
            Assert.Equal("こんにちは@0001[T:world]", result);
        }

        [Fact]
        public void TranslateText_DictionaryHit_IsCaseInsensitiveAndTrimmed()
        {
            var stub = new RecordingTranslator();
            var dic = new Dictionary<string, string> { ["FIRE EMBLEM"] = "ファイアエンブレム" };

            // Mixed case + surrounding spaces still match the upper-cased key.
            string result = TranslateTextUtilCore.TranslateText(
                "  Fire Emblem  ", "ja", "en", dic, useGoogle: true, translator: stub.Translate);

            Assert.Empty(stub.Calls);
            Assert.Equal("ファイアエンブレム", result);
        }

        [Fact]
        public void TranslateText_NonDictionarySegment_IsPassedToTranslator()
        {
            var stub = new RecordingTranslator();
            var dic = new Dictionary<string, string> { ["OTHER"] = "X" };

            string result = TranslateTextUtilCore.TranslateText(
                "needstranslate", "ja", "en", dic, useGoogle: true, translator: stub.Translate);

            Assert.Equal(new[] { "needstranslate" }, stub.Calls);
            Assert.Equal("[T:needstranslate]", result);
        }

        [Fact]
        public void TranslateText_UseGoogleFalse_LeavesTextUnchanged_NoTranslatorCall()
        {
            var stub = new RecordingTranslator();

            string result = TranslateTextUtilCore.TranslateText(
                "keep@0001me", "ja", "en", dic: null, useGoogle: false, translator: stub.Translate);

            Assert.Empty(stub.Calls);
            Assert.Equal("keep@0001me", result);
        }

        [Fact]
        public void TranslateText_SameLanguageOrEmpty_ReturnsInputUnchanged()
        {
            var stub = new RecordingTranslator();
            Assert.Equal("abc", TranslateTextUtilCore.TranslateText(
                "abc", "ja", "ja", dic: null, useGoogle: true, translator: stub.Translate));
            Assert.Equal("", TranslateTextUtilCore.TranslateText(
                "", "ja", "en", dic: null, useGoogle: true, translator: stub.Translate));
            Assert.Empty(stub.Calls);
        }

        // ----------------------------------------------------------------
        // LoadFixedDic — temp file load + missing-file + null-BaseDirectory
        // ----------------------------------------------------------------

        [Fact]
        public void LoadFixedDic_LoadsTempFile_UpperCasesKeys()
        {
            string savedBase = CoreState.BaseDirectory;
            string tempRoot = Path.Combine(Path.GetTempPath(), "feb_dic_" + Guid.NewGuid().ToString("N"));
            try
            {
                string transDir = Path.Combine(tempRoot, "config", "translate");
                Directory.CreateDirectory(transDir);
                File.WriteAllLines(Path.Combine(transDir, "dic_ja_en.txt"), new[]
                {
                    "ファイアエンブレム\tFire Emblem",
                    "聖魔\tThe Sacred Stones",
                });

                CoreState.BaseDirectory = tempRoot;
                TranslateTextUtilCore.ClearCache();

                var dic = TranslateTextUtilCore.LoadFixedDic("ja", "en");
                // Keys are upper-cased (the source term is already upper here, but
                // the value preserves its original casing).
                Assert.True(dic.ContainsKey("ファイアエンブレム"));
                Assert.Equal("Fire Emblem", dic["ファイアエンブレム"]);
                Assert.Equal("The Sacred Stones", dic["聖魔"]);

                // The en->ja reverse direction reuses the same file, swapped.
                TranslateTextUtilCore.ClearCache();
                var rev = TranslateTextUtilCore.LoadFixedDic("en", "ja");
                Assert.True(rev.ContainsKey("FIRE EMBLEM"));
                Assert.Equal("ファイアエンブレム", rev["FIRE EMBLEM"]);
            }
            finally
            {
                CoreState.BaseDirectory = savedBase;
                TranslateTextUtilCore.ClearCache();
                try { if (Directory.Exists(tempRoot)) Directory.Delete(tempRoot, true); } catch { }
            }
        }

        [Fact]
        public void LoadFixedDic_MissingFile_ReturnsEmpty_NoThrow()
        {
            string savedBase = CoreState.BaseDirectory;
            string tempRoot = Path.Combine(Path.GetTempPath(), "feb_dic_missing_" + Guid.NewGuid().ToString("N"));
            try
            {
                Directory.CreateDirectory(tempRoot); // no config/translate/dic file inside
                CoreState.BaseDirectory = tempRoot;
                TranslateTextUtilCore.ClearCache();

                var dic = TranslateTextUtilCore.LoadFixedDic("ja", "en");
                Assert.NotNull(dic);
                Assert.Empty(dic);
            }
            finally
            {
                CoreState.BaseDirectory = savedBase;
                TranslateTextUtilCore.ClearCache();
                try { if (Directory.Exists(tempRoot)) Directory.Delete(tempRoot, true); } catch { }
            }
        }

        [Fact]
        public void LoadFixedDic_MissingFile_NotPoisonCached_PicksUpFileIfItLaterAppears()
        {
            // Same BaseDirectory throughout: a resolvable-but-missing glossary file
            // must NOT be cached as an empty dict, so once the file appears a later
            // call re-reads it. (Copilot #968 review catch.)
            string savedBase = CoreState.BaseDirectory;
            string tempRoot = Path.Combine(Path.GetTempPath(), "feb_dic_appear_" + Guid.NewGuid().ToString("N"));
            try
            {
                string transDir = Path.Combine(tempRoot, "config", "translate");
                Directory.CreateDirectory(transDir); // dir exists, file does NOT yet
                CoreState.BaseDirectory = tempRoot;
                TranslateTextUtilCore.ClearCache();

                // 1) File missing → empty result, and must NOT poison-cache.
                var before = TranslateTextUtilCore.LoadFixedDic("ja", "en");
                Assert.Empty(before);

                // 2) The glossary file now appears (BaseDirectory UNCHANGED).
                File.WriteAllLines(Path.Combine(transDir, "dic_ja_en.txt"), new[] { "必殺\tCritical" });

                // 3) A subsequent call re-reads the file (not stuck on the empty cache).
                var after = TranslateTextUtilCore.LoadFixedDic("ja", "en");
                Assert.True(after.ContainsKey("必殺"));
                Assert.Equal("Critical", after["必殺"]);
            }
            finally
            {
                CoreState.BaseDirectory = savedBase;
                TranslateTextUtilCore.ClearCache();
                try { if (Directory.Exists(tempRoot)) Directory.Delete(tempRoot, true); } catch { }
            }
        }

        [Fact]
        public void LoadFixedDic_NullBaseDirectory_ReturnsEmpty_NoPoisonCache()
        {
            string savedBase = CoreState.BaseDirectory;
            string tempRoot = Path.Combine(Path.GetTempPath(), "feb_dic_null_" + Guid.NewGuid().ToString("N"));
            try
            {
                TranslateTextUtilCore.ClearCache();

                // 1) Null BaseDirectory → empty, no throw, and must NOT poison the cache.
                CoreState.BaseDirectory = null;
                var empty = TranslateTextUtilCore.LoadFixedDic("ja", "en");
                Assert.NotNull(empty);
                Assert.Empty(empty);

                // 2) A subsequent valid call (after BaseDirectory is set) STILL loads
                //    — proving the null-base failure did not poison-cache an empty dict.
                string transDir = Path.Combine(tempRoot, "config", "translate");
                Directory.CreateDirectory(transDir);
                File.WriteAllLines(Path.Combine(transDir, "dic_ja_en.txt"), new[] { "聖魔\tThe Sacred Stones" });
                CoreState.BaseDirectory = tempRoot;

                var dic = TranslateTextUtilCore.LoadFixedDic("ja", "en");
                Assert.True(dic.ContainsKey("聖魔"));
                Assert.Equal("The Sacred Stones", dic["聖魔"]);
            }
            finally
            {
                CoreState.BaseDirectory = savedBase;
                TranslateTextUtilCore.ClearCache();
                try { if (Directory.Exists(tempRoot)) Directory.Delete(tempRoot, true); } catch { }
            }
        }

        [Fact]
        public void LoadFixedDic_EndToEnd_DictionaryHitInTranslateText()
        {
            // Integration: a glossary loaded from disk is consumed by TranslateText
            // and short-circuits the (stub) translator for a matching segment.
            string savedBase = CoreState.BaseDirectory;
            string tempRoot = Path.Combine(Path.GetTempPath(), "feb_dic_e2e_" + Guid.NewGuid().ToString("N"));
            try
            {
                string transDir = Path.Combine(tempRoot, "config", "translate");
                Directory.CreateDirectory(transDir);
                File.WriteAllLines(Path.Combine(transDir, "dic_ja_en.txt"), new[] { "必殺\tCritical" });
                CoreState.BaseDirectory = tempRoot;
                TranslateTextUtilCore.ClearCache();

                var dic = TranslateTextUtilCore.LoadFixedDic("ja", "en");
                var stub = new RecordingTranslator();

                string result = TranslateTextUtilCore.TranslateText(
                    "必殺@0001残り", "ja", "en", dic, useGoogle: true, translator: stub.Translate);

                Assert.Equal("Critical@0001[T:残り]", result);
                Assert.Equal(new[] { "残り" }, stub.Calls); // 必殺 was a glossary hit, not translated
            }
            finally
            {
                CoreState.BaseDirectory = savedBase;
                TranslateTextUtilCore.ClearCache();
                try { if (Directory.Exists(tempRoot)) Directory.Delete(tempRoot, true); } catch { }
            }
        }
    }
}
