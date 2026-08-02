// SPDX-License-Identifier: GPL-3.0-or-later
using System;
using System.IO;
using System.Linq;
using Xunit;

namespace FEBuilderGBA.Tests
{
    public sealed class ToolInitWizardDownloadRouteTests
    {
        const string SappyDropboxId =
            "sh/723s9jdkfkx7pwa";

        [Fact]
        public void SettingStep3_VGMusicStudioHasNoHiddenSappyDownload()
        {
            string root = FindRepoRoot();
            Assert.False(
                string.IsNullOrEmpty(root),
                "Repository root with FEBuilderGBA.sln is required.");
            string sourcePath = Path.Combine(
                root, "FEBuilderGBA", "ToolInitWizardForm.cs");
            Assert.True(
                File.Exists(sourcePath),
                "ToolInitWizardForm.cs is required.");
            string source = File.ReadAllText(sourcePath);
            string method = ExtractMethodBody(
                source, "void SettingStep3()");
            string active = string.Join(
                "\n",
                method.Split('\n')
                    .Where(line => !line.TrimStart()
                        .StartsWith("//", StringComparison.Ordinal)));

            Assert.Equal(
                1,
                CountOccurrences(active, SappyDropboxId));
            string sappyBranch = Between(
                active,
                "else if (this.Step3 == Step3_Enum.DOWNLOAD_SAPPY)",
                "else if (this.Step3 == Step3_Enum.DOWNLOAD_GBA_MUSIC_STDIO)");
            Assert.Contains(SappyDropboxId, sappyBranch);

            string vgMusicStudioBranch = Between(
                active,
                "else if (this.Step3 == Step3_Enum.DOWNLOAD_GBA_MUSIC_STDIO)",
                "else if (this.Step3 == Step3_Enum.DO_NOT_SELECT)");
            Assert.Contains(
                "https://github.com/FEBuilderGBA/DirectPlayS/"
                    + "releases/download/20230504/DirectPlayS.7z",
                vgMusicStudioBranch);
            Assert.Contains(
                "\"VG Music Studio.exe\"",
                vgMusicStudioBranch);
            Assert.Contains("IsErrorResult(r)", vgMusicStudioBranch);
            Assert.Contains("R.ShowStopError(r)", vgMusicStudioBranch);
            Assert.Contains("return;", vgMusicStudioBranch);
            Assert.Contains("sappy = r;", vgMusicStudioBranch);
            Assert.DoesNotContain(SappyDropboxId, vgMusicStudioBranch);

            Assert.Contains(
                "Program.Config[\"sappy\"] = sappy;",
                active);
            Assert.Contains(
                "GrepFile(Path.GetDirectoryName(sappy), \"mid2agb.exe\")",
                active);
            Assert.Contains(
                "Program.Config[\"mid2agb\"] = r;",
                active);
            Assert.Contains(
                "Program.Config[\"func_midi_importer\"] = \"1\";",
                active);
        }

        static string Between(
            string source,
            string startMarker,
            string endMarker)
        {
            int start = source.IndexOf(
                startMarker, StringComparison.Ordinal);
            Assert.True(start >= 0, "Missing marker: " + startMarker);
            int end = source.IndexOf(
                endMarker,
                start + startMarker.Length,
                StringComparison.Ordinal);
            Assert.True(end > start, "Missing marker: " + endMarker);
            return source.Substring(start, end - start);
        }

        static int CountOccurrences(string source, string value)
        {
            int count = 0;
            int index = 0;
            while ((index = source.IndexOf(
                value, index, StringComparison.Ordinal)) >= 0)
            {
                count++;
                index += value.Length;
            }
            return count;
        }

        static string ExtractMethodBody(
            string source,
            string signature)
        {
            int signatureIndex = source.IndexOf(
                signature, StringComparison.Ordinal);
            Assert.True(
                signatureIndex >= 0,
                "Missing method signature: " + signature);
            int open = source.IndexOf('{', signatureIndex);
            Assert.True(open >= 0, "Missing method body.");
            int depth = 0;
            bool inString = false;
            bool inChar = false;
            bool inLineComment = false;
            bool inBlockComment = false;
            bool escaped = false;
            for (int i = open; i < source.Length; i++)
            {
                char current = source[i];
                char next = i + 1 < source.Length
                    ? source[i + 1] : '\0';
                if (inLineComment)
                {
                    if (current == '\n')
                        inLineComment = false;
                    continue;
                }
                if (inBlockComment)
                {
                    if (current == '*' && next == '/')
                    {
                        inBlockComment = false;
                        i++;
                    }
                    continue;
                }
                if (inString || inChar)
                {
                    if (escaped)
                    {
                        escaped = false;
                        continue;
                    }
                    if (current == '\\')
                    {
                        escaped = true;
                        continue;
                    }
                    if (inString && current == '"')
                        inString = false;
                    else if (inChar && current == '\'')
                        inChar = false;
                    continue;
                }
                if (current == '/' && next == '/')
                {
                    inLineComment = true;
                    i++;
                    continue;
                }
                if (current == '/' && next == '*')
                {
                    inBlockComment = true;
                    i++;
                    continue;
                }
                if (current == '"')
                {
                    inString = true;
                    continue;
                }
                if (current == '\'')
                {
                    inChar = true;
                    continue;
                }
                if (current == '{')
                    depth++;
                else if (current == '}' && --depth == 0)
                    return source.Substring(
                        open + 1, i - open - 1);
            }
            throw new InvalidDataException(
                "Unterminated method body: " + signature);
        }

        static string FindRepoRoot()
        {
            var directory = new DirectoryInfo(
                AppContext.BaseDirectory);
            while (directory != null)
            {
                if (File.Exists(Path.Combine(
                    directory.FullName, "FEBuilderGBA.sln")))
                {
                    return directory.FullName;
                }
                directory = directory.Parent;
            }
            return null;
        }
    }
}
