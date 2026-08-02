using System;
using System.IO;
using System.Text;
using FEBuilderGBA.CLI;
using FEBuilderGBA.Core;
using Xunit;

namespace FEBuilderGBA.Tests
{
    public sealed class FontLibraryPublicationTests : IDisposable
    {
        readonly string _root = Path.Combine(
            Path.GetTempPath(),
            "febuilder-font-publication-" + Guid.NewGuid().ToString("N"));

        public FontLibraryPublicationTests()
        {
            Directory.CreateDirectory(_root);
        }

        public void Dispose()
        {
            try { Directory.Delete(_root, recursive: true); }
            catch { }
        }

        [Fact]
        public void Publish_ChildMutationAfterFinalValidation_FailsAndRetainsOutput()
        {
            BuildCandidate(
                out FontLibraryPlan plan,
                out FontLibraryPackage package,
                out FontLibraryReport report);
            string output = Path.Combine(_root, "mutated-output");
            var hooks = new FontLibraryPublicationTestHooks
            {
                AfterFinalStagingValidation = stage =>
                    File.WriteAllText(
                        Path.Combine(stage, "alpha", "foreign.txt"),
                        "foreign"),
            };

            bool success = FEBuilderGBA.CLI.Program.TryPublishFontLibrary(
                plan,
                package,
                report,
                output,
                _root,
                hooks,
                out FontLibraryReport publishedReport,
                out string error);

            Assert.False(success);
            Assert.Null(publishedReport);
            Assert.Contains("published output verification failed", error);
            Assert.True(File.Exists(
                Path.Combine(output, "alpha", "foreign.txt")));
        }

        [Fact]
        public void Publish_ChildRemovalAfterFinalValidation_FailsAndRetainsOutput()
        {
            BuildCandidate(
                out FontLibraryPlan plan,
                out FontLibraryPackage package,
                out FontLibraryReport report);
            string output = Path.Combine(_root, "removed-child-output");
            var hooks = new FontLibraryPublicationTestHooks
            {
                AfterFinalStagingValidation = stage =>
                    File.Delete(Path.Combine(
                        stage, "alpha", "item_41.png")),
            };

            bool success = FEBuilderGBA.CLI.Program.TryPublishFontLibrary(
                plan,
                package,
                report,
                output,
                _root,
                hooks,
                out _,
                out string error);

            Assert.False(success);
            Assert.Contains("published output verification failed", error);
            Assert.True(Directory.Exists(output));
            Assert.False(File.Exists(Path.Combine(
                output, "alpha", "item_41.png")));
        }

        [Fact]
        public void Publish_ChildByteMutationAfterFinalValidation_FailsAndRetainsOutput()
        {
            BuildCandidate(
                out FontLibraryPlan plan,
                out FontLibraryPackage package,
                out FontLibraryReport report);
            string output = Path.Combine(_root, "changed-child-output");
            string changed = "";
            var hooks = new FontLibraryPublicationTestHooks
            {
                AfterFinalStagingValidation = stage =>
                {
                    changed = Path.Combine(
                        stage, "alpha", "item_41.png");
                    File.WriteAllBytes(changed, new byte[] { 1, 2, 3 });
                },
            };

            bool success = FEBuilderGBA.CLI.Program.TryPublishFontLibrary(
                plan,
                package,
                report,
                output,
                _root,
                hooks,
                out _,
                out string error);

            Assert.False(success);
            Assert.Contains("published output verification failed", error);
            Assert.Equal(
                new byte[] { 1, 2, 3 },
                File.ReadAllBytes(Path.Combine(
                    output, "alpha", "item_41.png")));
        }

        [Fact]
        public void Publish_StageRootReplacementBeforeMove_FailsWithoutDeletingForeignTree()
        {
            BuildCandidate(
                out FontLibraryPlan plan,
                out FontLibraryPackage package,
                out FontLibraryReport report);
            string output = Path.Combine(_root, "never-published");
            string retained = Path.Combine(_root, "retained-stage");
            string foreign = "";
            var hooks = new FontLibraryPublicationTestHooks
            {
                AfterFinalStagingValidation = stage =>
                {
                    Directory.Move(stage, retained);
                    Directory.CreateDirectory(stage);
                    foreign = Path.Combine(stage, "foreign.txt");
                    File.WriteAllText(foreign, "foreign");
                },
            };

            bool success = FEBuilderGBA.CLI.Program.TryPublishFontLibrary(
                plan,
                package,
                report,
                output,
                _root,
                hooks,
                out _,
                out string error);

            Assert.False(success);
            Assert.Contains("replaced", error);
            Assert.False(Directory.Exists(output));
            Assert.True(File.Exists(foreign));
            Assert.True(File.Exists(Path.Combine(
                retained, FontLibraryBuilderCore.PackageReportPath)));
        }

        [Fact]
        public void Publish_OutputRootReplacementAfterMove_FailsAndDeletesNeitherTree()
        {
            BuildCandidate(
                out FontLibraryPlan plan,
                out FontLibraryPackage package,
                out FontLibraryReport report);
            string output = Path.Combine(_root, "replaced-output");
            string retained = Path.Combine(_root, "retained-output");
            string foreign = "";
            var hooks = new FontLibraryPublicationTestHooks
            {
                AfterMoveBeforeVerification = moved =>
                {
                    Directory.Move(moved, retained);
                    Directory.CreateDirectory(moved);
                    foreign = Path.Combine(moved, "foreign.txt");
                    File.WriteAllText(foreign, "foreign");
                },
            };

            bool success = FEBuilderGBA.CLI.Program.TryPublishFontLibrary(
                plan,
                package,
                report,
                output,
                _root,
                hooks,
                out _,
                out string error);

            Assert.False(success);
            Assert.Contains("published output verification failed", error);
            Assert.True(File.Exists(foreign));
            Assert.True(File.Exists(Path.Combine(
                retained, FontLibraryBuilderCore.PackageReportPath)));
        }

        [Fact]
        public void Publish_ChildMutationAfterPublishedValidation_FailsBeforeReportRelease()
        {
            BuildCandidate(
                out FontLibraryPlan plan,
                out FontLibraryPackage package,
                out FontLibraryReport report);
            string output = Path.Combine(_root, "release-race-output");
            var hooks = new FontLibraryPublicationTestHooks
            {
                AfterPublishedValidationBeforeRelease = published =>
                    File.WriteAllText(
                        Path.Combine(published, "alpha", "late.txt"),
                        "foreign"),
            };

            bool success = FEBuilderGBA.CLI.Program.TryPublishFontLibrary(
                plan,
                package,
                report,
                output,
                _root,
                hooks,
                out FontLibraryReport releasedReport,
                out string error);

            Assert.False(success);
            Assert.Null(releasedReport);
            Assert.Contains("published output verification failed", error);
            Assert.True(File.Exists(
                Path.Combine(output, "alpha", "late.txt")));
        }

        [Fact]
        public void Publish_CleanupReplacementAfterFinalCheckRetainsForeignTree()
        {
            BuildCandidate(
                out FontLibraryPlan plan,
                out FontLibraryPackage package,
                out FontLibraryReport report);
            string output = Path.Combine(_root, "cleanup-race-output");
            string retained = Path.Combine(_root, "retained-cleanup-stage");
            string foreign = "";
            var hooks = new FontLibraryPublicationTestHooks
            {
                AfterFinalStagingValidation = _ =>
                    Directory.CreateDirectory(output),
                AfterStagingCleanupFinalIdentityCheck = quarantine =>
                {
                    Directory.Move(quarantine, retained);
                    Directory.CreateDirectory(quarantine);
                    foreign = Path.Combine(quarantine, "foreign.txt");
                    File.WriteAllText(foreign, "foreign");
                },
            };

            bool success = FEBuilderGBA.CLI.Program.TryPublishFontLibrary(
                plan,
                package,
                report,
                output,
                _root,
                hooks,
                out _,
                out string error);

            Assert.False(success);
            Assert.Contains("staging cleanup failed", error);
            Assert.True(File.Exists(foreign));
            Assert.True(File.Exists(Path.Combine(
                retained, FontLibraryBuilderCore.PackageReportPath)));
        }

        [Fact]
        public void ExternalReportRace_RollsBackVerifiedPublishedPackage()
        {
            BuildCandidate(
                out FontLibraryPlan plan,
                out FontLibraryPackage package,
                out FontLibraryReport report);
            string output = Path.Combine(_root, "report-race-package");
            Assert.True(FEBuilderGBA.CLI.Program.TryPublishFontLibrary(
                plan,
                package,
                report,
                output,
                _root,
                hooks: null,
                out FileSystemEntryIdentity publishedIdentity,
                out FontLibraryReport publishedReport,
                out string publishError),
                publishError);
            string reportPath = Path.Combine(_root, "report-race.json");

            bool success =
                FEBuilderGBA.CLI.Program.TryPublishExternalFontLibraryReport(
                    Encoding.UTF8.GetBytes(
                        FontLibraryReportFormatter.Format(
                            publishedReport)),
                    reportPath,
                    plan,
                    publishedReport,
                    output,
                    publishedIdentity,
                    path => File.WriteAllText(path, "foreign"),
                    out string error);

            Assert.False(success);
            Assert.Contains("Destination already exists", error);
            Assert.False(Directory.Exists(output));
            Assert.Equal("foreign", File.ReadAllText(reportPath));
        }

        [Fact]
        public void ExternalReportRace_RetainsByteIdenticalReplacementPackage()
        {
            BuildCandidate(
                out FontLibraryPlan plan,
                out FontLibraryPackage package,
                out FontLibraryReport report);
            string output = Path.Combine(_root, "replaced-package");
            Assert.True(FEBuilderGBA.CLI.Program.TryPublishFontLibrary(
                plan,
                package,
                report,
                output,
                _root,
                hooks: null,
                out FileSystemEntryIdentity publishedIdentity,
                out FontLibraryReport publishedReport,
                out string publishError),
                publishError);
            string retained = Path.Combine(_root, "retained-package");
            Directory.Move(output, retained);
            CopyTree(retained, output);
            string reportPath = Path.Combine(
                _root, "replaced-package-report.json");

            bool success =
                FEBuilderGBA.CLI.Program.TryPublishExternalFontLibraryReport(
                    Encoding.UTF8.GetBytes(
                        FontLibraryReportFormatter.Format(
                            publishedReport)),
                    reportPath,
                    plan,
                    publishedReport,
                    output,
                    publishedIdentity,
                    path => File.WriteAllText(path, "foreign report"),
                    out string error);

            Assert.False(success);
            Assert.Contains("published package retained", error);
            Assert.True(File.Exists(Path.Combine(
                output, FontLibraryBuilderCore.PackageReportPath)));
            Assert.True(File.Exists(Path.Combine(
                retained, FontLibraryBuilderCore.PackageReportPath)));
            Assert.Equal(
                "foreign report", File.ReadAllText(reportPath));
        }

        static void CopyTree(string source, string destination)
        {
            Directory.CreateDirectory(destination);
            foreach (string directory in Directory.GetDirectories(
                source, "*", SearchOption.AllDirectories))
            {
                Directory.CreateDirectory(Path.Combine(
                    destination,
                    Path.GetRelativePath(source, directory)));
            }
            foreach (string file in Directory.GetFiles(
                source, "*", SearchOption.AllDirectories))
            {
                string target = Path.Combine(
                    destination,
                    Path.GetRelativePath(source, file));
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.Copy(file, target);
            }
        }

        static void BuildCandidate(
            out FontLibraryPlan plan,
            out FontLibraryPackage package,
            out FontLibraryReport report)
        {
            byte[] font = { 1, 2, 3 };
            byte[] license = { 4, 5, 6 };
            var manifest = new FontLibraryJobManifest
            {
                Id = "alpha",
                Locale = "en-US",
                Format = FontLibraryFormat.Main16x16,
                Font = new FontLibraryFontManifest
                {
                    Path = "font.ttf",
                    Sha256 = FontLibraryHashCore.ComputeSha256(font),
                    ByteLength = font.Length,
                    Family = "Test",
                    Version = "Version 1",
                    SourceUrl = "https://example.invalid/font",
                    Size = 12,
                },
                License = new FontLibraryLicenseManifest
                {
                    LicenseId = "OFL-1.1",
                    LicenseFile = "OFL.txt",
                    Sha256 = FontLibraryHashCore.ComputeSha256(license),
                    SourceUrl = "https://example.invalid/license",
                },
                Mapping = new FontLibraryMappingManifest
                {
                    Mode = FontLibraryMappingMode.Fixed,
                },
            };
            manifest.Styles.Add(FontLibraryStyle.Item);
            manifest.Mapping.Entries.Add(new FontLibraryFixedMapping
            {
                Scalar = 'A',
                Moji = 0x41,
            });
            var plannedJob = new FontLibraryPlannedJob
            {
                Manifest = manifest,
            };
            plannedJob.Slots.Add(new FontLibraryPlannedSlot
            {
                UnicodeScalar = 'A',
                Character = "A",
                Moji = 0x41,
                Style = FontLibraryStyle.Item,
            });
            plan = new FontLibraryPlan { SlotCount = 1 };
            plan.Jobs.Add(plannedJob);
            var input = new FontLibraryBuildInput
            {
                Plan = plan,
                FaceFactory = new FakeFaceFactory(),
            };
            input.Assets.Add("alpha", new FontLibraryJobAssets
            {
                FontFileData = font,
                LicenseFileData = license,
            });
            FontLibraryBuildResult built =
                FontLibraryBuilderCore.Build(input);
            Assert.True(built.Success, built.Error);
            package = built.Package;
            report = built.Report;
        }

        sealed class FakeFaceFactory : IFontLibraryFaceFactory
        {
            public bool TryOpenFace(
                FontLibraryFaceSpec spec,
                out IFontLibraryFace face,
                out string error)
            {
                face = new FakeFace();
                error = "";
                return true;
            }
        }

        sealed class FakeFace : IFontLibraryFace
        {
            public bool HasGlyph(int unicodeScalar) => unicodeScalar == 'A';

            public bool TryRasterizeGlyph(
                int unicodeScalar,
                bool isItemFont,
                int verticalOffset,
                out byte[] engineTile,
                out int glyphWidth,
                out string error)
            {
                engineTile = new byte[64];
                engineTile[0] = 0x1B;
                glyphWidth = 8;
                error = "";
                return true;
            }

            public void Dispose() { }
        }
    }
}
