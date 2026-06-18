// SPDX-License-Identifier: GPL-3.0-or-later
// Core tests for MyTranslateBuild (#1164) — the developer translation builder
// moved from WinForms so the Avalonia Dev Translate tool can reuse it.
//
// Fully offline: the test pre-seeds the <lang>.txt resource with a translation
// for the synthetic non-ASCII literal, so MakeTranslate resolves it locally and
// NEVER calls the Google translate engine (no network). It asserts that ScanCS
// extracts the non-ASCII C# string literal and writes it into the output
// config/translate/<lang>.txt resource. The onProgress callback is exercised too.
using System;
using System.Collections.Generic;
using System.IO;
using FEBuilderGBA;
using Xunit;

namespace FEBuilderGBA.Core.Tests
{
    [Collection("SharedState")]
    public class MyTranslateBuildTests
    {
        // A non-ASCII Japanese literal that ScanCS must extract from C# source.
        const string NonAsciiLiteral = "テスト文字列";
        const string SeededTranslation = "TestString";

        [Fact]
        public void ScanCS_ExtractsNonAsciiLiteral_AndWritesToLangResource()
        {
            string baseDir = Path.Combine(Path.GetTempPath(), "fe_devtranslate_" + Guid.NewGuid().ToString("N"));
            string savedBaseDir = CoreState.BaseDirectory;
            try
            {
                // --- Arrange a minimal config tree ---------------------------
                string translateDir = Path.Combine(baseDir, "config", "translate");
                Directory.CreateDirectory(translateDir);
                // Empty data + patch2 dirs so ScanData/ScanPatch/ScanMOD no-op
                // cleanly (the build VM runs them; here we only call ScanCS).
                Directory.CreateDirectory(Path.Combine(baseDir, "config", "data"));
                Directory.CreateDirectory(Path.Combine(baseDir, "config", "patch2"));

                // Seed en.txt resource so MyTranslateBuild's ctor LoadResource
                // finds a file, and pre-translate the non-ASCII literal so
                // MakeTranslate returns locally (no network call).
                string langFile = Path.Combine(translateDir, "en.txt");
                File.WriteAllText(langFile,
                    ":" + NonAsciiLiteral + "\r\n" + SeededTranslation + "\r\n\r\n");

                CoreState.BaseDirectory = baseDir;

                // A synthetic C# source file containing the non-ASCII literal.
                string sourceDir = Path.Combine(baseDir, "src");
                Directory.CreateDirectory(sourceDir);
                File.WriteAllText(Path.Combine(sourceDir, "Sample.cs"),
                    "public class Sample { string F() { return R._(\"" + NonAsciiLiteral + "\"); } }\r\n");

                // Capture progress callbacks (replaces InputFormRef.DoEvents).
                var progressed = new List<string>();
                Action<string> onProgress = f => progressed.Add(f);

                // --- Act -----------------------------------------------------
                var t = new MyTranslateBuild("en", false, onProgress);
                t.ScanCS(sourceDir);

                // --- Assert --------------------------------------------------
                // ScanCS rewrites config/translate/en.txt with the extracted
                // literals. The output must contain the extracted non-ASCII key
                // line ":<literal>" and its seeded translation.
                Assert.True(File.Exists(langFile), "expected en.txt output to exist");
                string output = File.ReadAllText(langFile);
                Assert.Contains(":" + NonAsciiLiteral, output);
                Assert.Contains(SeededTranslation, output);

                // The progress callback must have fired for the scanned .cs file.
                Assert.Contains(progressed, p => p.EndsWith("Sample.cs", StringComparison.Ordinal));
            }
            finally
            {
                CoreState.BaseDirectory = savedBaseDir;
                try { if (Directory.Exists(baseDir)) Directory.Delete(baseDir, true); } catch { }
            }
        }

        [Fact]
        public void ScanCS_IgnoresPureAsciiLiterals()
        {
            string baseDir = Path.Combine(Path.GetTempPath(), "fe_devtranslate_" + Guid.NewGuid().ToString("N"));
            string savedBaseDir = CoreState.BaseDirectory;
            try
            {
                string translateDir = Path.Combine(baseDir, "config", "translate");
                Directory.CreateDirectory(translateDir);
                string langFile = Path.Combine(translateDir, "en.txt");
                File.WriteAllText(langFile, "");

                CoreState.BaseDirectory = baseDir;

                string sourceDir = Path.Combine(baseDir, "src");
                Directory.CreateDirectory(sourceDir);
                // Only ASCII literals — none should be extracted.
                File.WriteAllText(Path.Combine(sourceDir, "AsciiOnly.cs"),
                    "public class AsciiOnly { string F() { return \"plain ascii\"; } }\r\n");

                var t = new MyTranslateBuild("en", false, null);
                t.ScanCS(sourceDir);

                string output = File.ReadAllText(langFile);
                Assert.DoesNotContain(":plain ascii", output);
            }
            finally
            {
                CoreState.BaseDirectory = savedBaseDir;
                try { if (Directory.Exists(baseDir)) Directory.Delete(baseDir, true); } catch { }
            }
        }
    }
}
