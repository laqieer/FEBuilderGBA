// SPDX-License-Identifier: GPL-3.0-or-later
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Xunit;

namespace FEBuilderGBA.Tests
{
    public sealed class FontFormMappedImportTests
    {
        [Fact]
        public void TryResolveMappedBulkImportMoji_UsesValidatedSlotMapping()
        {
            MethodInfo? method = typeof(FontForm).GetMethod(
                "TryResolveMappedBulkImportMoji",
                BindingFlags.NonPublic | BindingFlags.Static);
            Assert.NotNull(method);
            object?[] arguments =
            {
                "text",
                @"nested\text_1234.png",
                new Dictionary<string, uint>
                {
                    ["text\ntext_1234.png"] = 0x1234,
                },
                0u,
            };

            bool mapped = Assert.IsType<bool>(method.Invoke(null, arguments));

            Assert.True(mapped);
            Assert.Equal(0x1234u, Assert.IsType<uint>(arguments[3]));
        }

        [Fact]
        public void TryResolveMappedBulkImportMoji_LegacyFilenameFallsBack()
        {
            MethodInfo? method = typeof(FontForm).GetMethod(
                "TryResolveMappedBulkImportMoji",
                BindingFlags.NonPublic | BindingFlags.Static);
            Assert.NotNull(method);
            object?[] arguments =
            {
                "text",
                "text_E38182.png",
                new Dictionary<string, uint>(),
                0u,
            };

            bool mapped = Assert.IsType<bool>(method.Invoke(null, arguments));

            Assert.False(mapped);
            Assert.Equal(0u, Assert.IsType<uint>(arguments[3]));
        }

        [Fact]
        public void TryLoadBulkImportSlotMappings_ParsesStrictSibling()
        {
            string root = Path.Combine(
                Path.GetTempPath(),
                "fontform-slots-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            try
            {
                string manifest = Path.Combine(
                    root, "alpha.fontall.txt");
                File.WriteAllText(manifest, "");
                string zeroHash = new string('0', 64);
                File.WriteAllText(
                    Path.Combine(root, "slots.tsv"),
                    "moji\tunicode\tstyle\twidth\tfilename"
                    + "\tpackedSha256\tpngSha256\n"
                    + "1234\tU+0041\ttext\t7\ttext_1234.png\t"
                    + zeroHash + "\t" + zeroHash + "\n");
                MethodInfo? method = typeof(FontForm).GetMethod(
                    "TryLoadBulkImportSlotMappings",
                    BindingFlags.NonPublic | BindingFlags.Static);
                Assert.NotNull(method);
                object?[] arguments =
                {
                    manifest,
                    null,
                    "",
                };

                bool loaded = Assert.IsType<bool>(method.Invoke(null, arguments));

                Assert.True(loaded, Assert.IsType<string>(arguments[2]));
                var mappings =
                    Assert.IsType<Dictionary<string, uint>>(
                        arguments[1]);
                Assert.Equal(
                    0x1234u,
                    mappings["text\ntext_1234.png"]);
            }
            finally
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }
}
