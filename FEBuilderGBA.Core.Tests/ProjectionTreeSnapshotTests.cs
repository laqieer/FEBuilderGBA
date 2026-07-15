// SPDX-License-Identifier: GPL-3.0-or-later
using System;
using System.IO;
using System.Linq;
using System.Text;
using Xunit;

namespace FEBuilderGBA.Core.Tests
{
    public sealed class ProjectionTreeSnapshotTests : IDisposable
    {
        readonly string tempDirectory;

        public ProjectionTreeSnapshotTests()
        {
            tempDirectory = Path.Combine(
                Path.GetTempPath(),
                "febuilder-projection-snapshot-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDirectory);
        }

        public void Dispose()
        {
            if (Directory.Exists(tempDirectory))
                Directory.Delete(tempDirectory, recursive: true);
        }

        [Fact]
        public void Capture_ReadsNestedRegularFiles()
        {
            string nested = Path.Combine(tempDirectory, "nested");
            Directory.CreateDirectory(nested);
            File.WriteAllText(
                Path.Combine(tempDirectory, "root.txt"),
                "root",
                new UTF8Encoding(false));
            File.WriteAllText(
                Path.Combine(nested, "caf\u00E9.txt"),
                "nested",
                new UTF8Encoding(false));

            ProjectionTreeSnapshot snapshot = ProjectionTreeSnapshotReader.Capture(
                tempDirectory,
                maxEntries: 10,
                maxBytes: 1024,
                maxTextFileBytes: 1024,
                beforeFileOpen: null);

            Assert.Equal(new[] { "nested" }, snapshot.Directories);
            Assert.Equal(
                new[] { "nested/caf\u00E9.txt", "root.txt" },
                snapshot.Files.Select(file => file.RelativePath));
            Assert.Equal(
                new[] { "nested", "root" },
                snapshot.Files.Select(file => Encoding.UTF8.GetString(file.Data)));
        }
    }
}
