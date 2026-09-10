using System.IO.Compression;
using System.Text;
using System.Reflection;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.Platform.Storage;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.Dialogs;
using FEBuilderGBA.Avalonia.ViewModels;
using FEBuilderGBA.Avalonia.Views;

namespace FEBuilderGBA.Avalonia.Tests;

[Collection("SharedState")]
public class PatchDatabaseImportServiceTests
{
    [Fact]
    public void ZipPickerUsesStreamCompatibleZipTypesAndSingleSelection()
    {
        var options = FileDialogHelper.CreatePatchDatabaseZipOpenOptions();
        Assert.False(options.AllowMultiple);
        Assert.Contains(options.FileTypeFilter!, item =>
            item.Patterns!.Contains("*.zip") && item.MimeTypes!.Contains("application/zip"));
    }

    [Fact]
    public async Task ProviderWithoutLocalPathImportsWithoutChangingTheRom()
    {
        using var fixture = new Fixture();
        using var selected = new PickedFile(Zip("FE8U"));
        byte[] original = (byte[])fixture.Rom.Data.Clone();
        bool modified = fixture.Rom.Modified;
        int undoCount = CoreState.Undo.UndoBuffer.Count;
        var identity = PatchDatabaseImportService.CaptureLoadedRom();
        var result = await fixture.Import(selected, identity!, _ => Task.FromResult(true));
        Assert.True(result.Imported, result.Message);
        Assert.Equal(original, fixture.Rom.Data);
        Assert.Equal(modified, fixture.Rom.Modified);
        Assert.Equal(undoCount, CoreState.Undo.UndoBuffer.Count);
        Assert.True(selected.ReadStream!.Disposed);
        Assert.True(File.Exists(Path.Combine(fixture.Root, "config", "patch2", "FE8U", "PATCH_test.txt")));
    }

    [Fact]
    public async Task DecliningConsentDoesNotReplaceTheDatabase()
    {
        using var fixture = new Fixture();
        fixture.SeedOld();
        using var selected = new PickedFile(Zip("FE8U"));
        var result = await fixture.Import(selected, PatchDatabaseImportService.CaptureLoadedRom()!,
            prepared =>
            {
                Assert.True(prepared.ReplacesExisting);
                Assert.Contains("FE8U", PatchDatabaseImportService.ConfirmationMessage(prepared));
                Assert.Contains("No patches will be applied", PatchDatabaseImportService.ConfirmationMessage(prepared));
                return Task.FromResult(false);
            });
        Assert.True(result.Cancelled);
        Assert.Equal("old", File.ReadAllText(fixture.OldFile));
        Assert.Empty(Directory.GetDirectories(Path.Combine(fixture.Root, ".patch2-import")));
    }

    [Fact]
    public async Task ReloadingTheSameRomInstanceDuringConsentAbortsPromotion()
    {
        using var fixture = new Fixture();
        fixture.SeedOld();
        using var selected = new PickedFile(Zip("FE8U"));
        var result = await fixture.Import(selected, PatchDatabaseImportService.CaptureLoadedRom()!,
            _ =>
            {
                fixture.Rom.LoadLow("different.gba", new byte[0x1000000], "BE8E01");
                return Task.FromResult(true);
            });
        Assert.False(result.Imported);
        Assert.Contains("loaded ROM changed", result.Message);
        Assert.Equal("old", File.ReadAllText(fixture.OldFile));
    }

    [Fact]
    public async Task InPlaceRomChangeDuringConsentAlsoAbortsPromotion()
    {
        using var fixture = new Fixture();
        fixture.SeedOld();
        using var selected = new PickedFile(Zip("FE8U"));
        var result = await fixture.Import(selected, PatchDatabaseImportService.CaptureLoadedRom()!,
            _ =>
            {
                fixture.Rom.Data[0x100] = 42;
                return Task.FromResult(true);
            });
        Assert.False(result.Imported);
        Assert.Equal("old", File.ReadAllText(fixture.OldFile));
        Assert.Equal(42, fixture.Rom.Data[0x100]);
    }

    [Fact]
    public async Task CanonicalLoadedVersionIsNotHardcodedToFe8u()
    {
        using var fixture = new Fixture("AE7E01");
        Assert.Equal("FE7U", fixture.Rom.RomInfo.VersionToFilename);
        using var selected = new PickedFile(Zip("FE7U"));
        var result = await fixture.Import(selected, PatchDatabaseImportService.CaptureLoadedRom()!,
            _ => Task.FromResult(true));
        Assert.True(result.Imported, result.Message);
        Assert.True(File.Exists(Path.Combine(fixture.Root, "config", "patch2", "FE7U", "PATCH_test.txt")));
        Assert.False(Directory.Exists(Path.Combine(fixture.Root, "config", "patch2", "FE8U")));
    }

    [Fact]
    public async Task RomChangedBeforeOpeningSelectedStreamDoesNotReadIt()
    {
        using var fixture = new Fixture();
        var identity = PatchDatabaseImportService.CaptureLoadedRom();
        using var selected = new PickedFile(Zip("FE8U"));
        CoreState.ROM = null!;
        var result = await fixture.Import(selected, identity!, _ => Task.FromResult(true));
        Assert.False(result.Imported);
        Assert.Null(selected.ReadStream);
    }

    [Fact]
    public async Task ProviderCloseFailureReleasesThePreparedTransaction()
    {
        using var fixture = new Fixture();
        using var selected = new PickedFile(Zip("FE8U"), throwOnClose: true);
        var result = await fixture.Import(selected, PatchDatabaseImportService.CaptureLoadedRom()!,
            _ => Task.FromResult(true));
        Assert.False(result.Imported);
        using var retry = new PickedFile(Zip("FE8U"));
        var retried = await fixture.Import(retry, PatchDatabaseImportService.CaptureLoadedRom()!,
            _ => Task.FromResult(true));
        Assert.True(retried.Imported, retried.Message);
    }

    [Fact]
    public void CanonicalBaseDirectoryIsPreferredByActualPatchDiscovery()
    {
        using var fixture = new Fixture();
        string path = Path.Combine(fixture.Root, "config", "patch2", "FE8U");
        Directory.CreateDirectory(path);
        File.WriteAllText(Path.Combine(path, "PATCH_owned.txt"), "NAME=Owned library\nTYPE=EA");
        Assert.Equal(path, PatchManagerViewModel.ResolvePatchDirectory("FE8U"));
        var vm = new PatchManagerViewModel();
        vm.LoadPatchList();
        Assert.Single(vm.FilteredPatches);
        Assert.Equal("Owned library", vm.FilteredPatches[0].Name);
        Assert.False(vm.FilteredPatches[0].IsInstallTypeSupported);
    }

    [Fact]
    public void ImportIsUnavailableWithoutALoadedRom()
    {
        using var fixture = new Fixture();
        CoreState.ROM = null!;
        Assert.Null(PatchDatabaseImportService.CaptureLoadedRom());
        Assert.False(new PatchManagerViewModel().CanImportPatchDatabase);
        Assert.Contains("Load a ROM", PatchDatabaseImportService.AvailabilityMessage);
    }

    [AvaloniaFact]
    public async Task CommittedCleanupFailureRemainsVisibleWhenPatchManagerReopens()
    {
        using var fixture = new Fixture();
        fixture.SeedOld();
        using var selected = new PickedFile(Zip("FE8U"));
        var result = await fixture.Import(selected, PatchDatabaseImportService.CaptureLoadedRom()!,
            _ => Task.FromResult(true), checkpoint: FailCleanup);
        Assert.True(result.Imported);
        Assert.True(result.RecoveryRequired);
        Assert.Contains("backup cleanup is pending", result.Message);
        Assert.Equal(result.Message, App.PatchDatabaseRecoveryNotice);
        for (int i = 0; i < 2; i++)
        {
            var view = new PatchManagerView();
            var host = new Window { Content = view };
            try
            {
                host.Show();
                Dispatcher.UIThread.RunJobs();
                Assert.Equal(result.Message, view.FindControl<TextBlock>("StatusMessageLabel")!.Text);
            }
            finally { host.Close(); }
        }
        Assert.True(Directory.Exists(Assert.Single(Directory.GetDirectories(Path.Combine(fixture.Root, ".patch2-import")))));
    }

    [Theory]
    [InlineData("decline")]
    [InlineData("cancel")]
    [InlineData("rom-change")]
    public async Task DisposeRecoveryFailureOverridesEarlyReturnAndPersistsNotice(string exit)
    {
        using var fixture = new Fixture();
        fixture.SeedOld();
        using var cancellation = new CancellationTokenSource();
        using var selected = new PickedFile(Zip("FE8U"));
        var result = await fixture.Import(selected, PatchDatabaseImportService.CaptureLoadedRom()!,
            _ =>
            {
                fixture.AddUnexpectedWorkspaceFile();
                if (exit == "cancel") cancellation.Cancel();
                if (exit == "rom-change") fixture.Rom.Data[0x100] = 42;
                return Task.FromResult(exit != "decline");
            }, cancellation.Token);
        Assert.False(result.Imported);
        Assert.True(result.RecoveryRequired);
        Assert.False(result.Cancelled);
        Assert.Equal("old", File.ReadAllText(fixture.OldFile));
        Assert.Equal(result.Message, App.PatchDatabaseRecoveryNotice);
        Assert.Contains(fixture.Root, result.Message);
    }

    [Fact]
    public async Task PreparationRecoveryExceptionAlsoPersistsNotice()
    {
        using var fixture = new Fixture();
        using var selected = new PickedFile(Zip("FE8U"));
        var result = await fixture.Import(selected, PatchDatabaseImportService.CaptureLoadedRom()!,
            _ => throw new InvalidOperationException("Must not confirm"), checkpoint: point =>
            {
                if (point != PatchDatabaseImportCore.Checkpoint.BeforeMaterialization) return;
                fixture.AddUnexpectedWorkspaceFile();
                throw new IOException("Owned preparation failure");
            });
        Assert.True(result.RecoveryRequired);
        Assert.Equal(result.Message, App.PatchDatabaseRecoveryNotice);
        Assert.Contains("Owned preparation failure", result.Message);
    }

    [Fact]
    public async Task SuccessfulRecoveryClearsNoticeEvenWhenNextImportIsDeclined()
    {
        using var fixture = new Fixture();
        fixture.SeedOld();
        using var first = new PickedFile(Zip("FE8U"));
        var result = await fixture.Import(first, PatchDatabaseImportService.CaptureLoadedRom()!,
            _ => Task.FromResult(true), checkpoint: FailCleanup);
        Assert.True(result.RecoveryRequired);
        using var second = new PickedFile(Zip("FE8U"));
        result = await fixture.Import(second, PatchDatabaseImportService.CaptureLoadedRom()!,
            _ => Task.FromResult(false));
        Assert.True(result.Cancelled);
        Assert.False(result.RecoveryRequired);
        Assert.Empty(App.PatchDatabaseRecoveryNotice);
        Assert.Empty(Directory.GetDirectories(Path.Combine(fixture.Root, ".patch2-import")));
    }

    [AvaloniaTheory]
    [InlineData("malformed")]
    [InlineData("unsafe")]
    [InlineData("cancel")]
    public async Task CompletedRecoveryClearsOldNoticeWhenPreparationDoesNotReturn(string exit)
    {
        using var fixture = new Fixture();
        fixture.SeedOld();
        using var first = new PickedFile(Zip("FE8U"));
        var previous = await fixture.Import(first, PatchDatabaseImportService.CaptureLoadedRom()!,
            _ => Task.FromResult(true), checkpoint: FailCleanup);
        Assert.True(previous.RecoveryRequired);
        string operation = Assert.Single(Directory.GetDirectories(Path.Combine(fixture.Root, ".patch2-import")));
        using var cancellation = new CancellationTokenSource();
        using var second = new PickedFile(exit == "malformed" ? new byte[] { 1, 2, 3 } :
            exit == "unsafe" ? Zip("../FE8U") : Zip("FE8U"));
        var result = await fixture.Import(second, PatchDatabaseImportService.CaptureLoadedRom()!,
            _ => throw new InvalidOperationException("Preparation must not reach confirmation"),
            cancellation.Token, point =>
            {
                if (exit == "cancel" && point == PatchDatabaseImportCore.Checkpoint.BeforeMaterialization)
                    cancellation.Cancel();
            });
        Assert.False(result.Imported);
        Assert.False(result.RecoveryRequired);
        Assert.Equal(exit == "cancel", result.Cancelled);
        Assert.False(Directory.Exists(operation));
        Assert.Empty(Directory.GetDirectories(Path.Combine(fixture.Root, ".patch2-import")));
        Assert.Empty(App.PatchDatabaseRecoveryNotice);
        AssertReopenedNotice("");
        Assert.True(File.Exists(Path.Combine(fixture.Root, "config", "patch2", "FE8U", "PATCH_test.txt")));
    }

    [AvaloniaFact]
    public async Task UnresolvedPriorRecoveryRetainsItsNoticeAcrossRejectedPreparationAndReopening()
    {
        using var fixture = new Fixture();
        using var first = new PickedFile(Zip("FE8U"));
        var previous = await fixture.Import(first, PatchDatabaseImportService.CaptureLoadedRom()!,
            _ => Task.FromResult(true), checkpoint: FailCleanup);
        Assert.True(previous.RecoveryRequired);
        fixture.AddUnexpectedWorkspaceFile();
        string operation = Assert.Single(Directory.GetDirectories(Path.Combine(fixture.Root, ".patch2-import")));
        using var second = new PickedFile(new byte[] { 1, 2, 3 });
        var result = await fixture.Import(second, PatchDatabaseImportService.CaptureLoadedRom()!,
            _ => throw new InvalidOperationException("Must not confirm"));
        Assert.False(result.Imported);
        Assert.Equal(previous.Message, App.PatchDatabaseRecoveryNotice);
        Assert.Equal("retain, do not delete", File.ReadAllText(Path.Combine(operation, "owned-unexpected.txt")));
        AssertReopenedNotice(previous.Message);
    }

    [AvaloniaTheory]
    [InlineData("cleanup", "ja")]
    [InlineData("commit", "zh")]
    [InlineData("dispose", "ja")]
    public async Task NewRecoveryConditionReplacesResolvedNoticeAcrossReopenedViews(string failure, string language)
    {
        using var fixture = new Fixture();
        MyTranslateResource.LoadResource(Path.Combine(FindRepoRoot(), "config", "translate", language + ".txt"));
        using var first = new PickedFile(Zip("FE8U"));
        var previous = await fixture.Import(first, PatchDatabaseImportService.CaptureLoadedRom()!,
            _ => Task.FromResult(true), checkpoint: FailCleanup);
        Assert.True(previous.RecoveryRequired);
        string oldOperation = Assert.Single(Directory.GetDirectories(Path.Combine(fixture.Root, ".patch2-import")));
        using var second = new PickedFile(Zip("FE8U"));
        var result = await fixture.Import(second, PatchDatabaseImportService.CaptureLoadedRom()!,
            _ =>
            {
                if (failure == "dispose") fixture.AddUnexpectedWorkspaceFile();
                return Task.FromResult(failure != "dispose");
            }, checkpoint: point =>
            {
                if (failure == "commit" && point == PatchDatabaseImportCore.Checkpoint.Committed)
                    throw new IOException("Owned interrupted commit");
                if (failure != "dispose") FailCleanup(point);
            });
        Assert.True(result.RecoveryRequired);
        Assert.False(Directory.Exists(oldOperation));
        string newOperation = Assert.Single(Directory.GetDirectories(Path.Combine(fixture.Root, ".patch2-import")));
        Assert.NotEqual(oldOperation, newOperation);
        Assert.Contains(newOperation, result.Message);
        Assert.DoesNotContain(oldOperation, result.Message);
        Assert.Equal(result.Message, App.PatchDatabaseRecoveryNotice);
        Assert.Contains(language == "ja" ? "保持された作業領域" : "保留的工作目录", result.Message);
        AssertReopenedNotice(result.Message);
        AssertReopenedNotice(result.Message);
    }

    [AvaloniaFact]
    public async Task CompletedRecoveryCannotClearANewerNoticeWithIdenticalText()
    {
        using var fixture = new Fixture();
        fixture.SeedOld();
        var previous = new PatchDatabaseImportCore.RecoveryException(fixture.Root, new IOException("Owned diagnostic"));
        var newer = new PatchDatabaseImportCore.RecoveryException(fixture.Root, new IOException("Owned diagnostic"));
        App.RecordPatchDatabaseRecovery(previous);
        string expected = App.PatchDatabaseRecoveryNotice;
        using var selected = new PickedFile(new byte[] { 1, 2, 3 });
        await fixture.Import(selected, PatchDatabaseImportService.CaptureLoadedRom()!,
            _ => throw new InvalidOperationException("Must not confirm"),
            afterRecovery: () => App.RecordPatchDatabaseRecovery(newer));
        Assert.Same(newer, App.PatchDatabaseRecoveryException);
        Assert.Equal(expected, App.PatchDatabaseRecoveryNotice);
        AssertReopenedNotice(expected);
    }

    static void AssertReopenedNotice(string expected)
    {
        var view = new PatchManagerView();
        var host = new Window { Content = view };
        try
        {
            host.Show();
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(expected, view.FindControl<TextBlock>("StatusMessageLabel")!.Text);
        }
        finally { host.Close(); }
    }

    [Fact]
    public async Task RefusalBeforePreparationDoesNotClearExistingRecoveryNotice()
    {
        using var fixture = new Fixture();
        App.RecordPatchDatabaseRecovery(new PatchDatabaseImportCore.RecoveryException(
            fixture.Root, new IOException("Existing retained workspace")));
        string previous = App.PatchDatabaseRecoveryNotice;
        var identity = PatchDatabaseImportService.CaptureLoadedRom()!;
        fixture.Rom.Data[0x100] = 1;
        using var selected = new PickedFile(Zip("FE8U"));
        await fixture.Import(selected, identity, _ => Task.FromResult(true));
        Assert.Equal(previous, App.PatchDatabaseRecoveryNotice);
        Assert.Null(selected.ReadStream);
    }

    [Theory]
    [InlineData("ja", "保持された作業領域", "新しいデータベースはインストール済み")]
    [InlineData("zh", "保留的工作目录", "新数据库已安装")]
    public void EveryStructuredOutcomeUsesShippedLocalizedTemplates(string language, string retained, string cleanup)
    {
        using var fixture = new Fixture();
        var results = new[]
        {
            new PatchDatabaseImportCore.Result { Kind = PatchDatabaseImportCore.ResultKind.Completed, Success = true,
                RecoveryRequired = true, CleanupDetail = "owned cleanup detail" },
            new PatchDatabaseImportCore.Result { Kind = PatchDatabaseImportCore.ResultKind.CommittedAfterInterruption,
                Success = true, Detail = "owned commit detail" },
            new PatchDatabaseImportCore.Result { Kind = PatchDatabaseImportCore.ResultKind.StoppedBeforeCommit,
                Detail = "owned stop detail", RecoveryRequired = true, CleanupDetail = "owned cleanup detail" },
            new PatchDatabaseImportCore.Result { Kind = PatchDatabaseImportCore.ResultKind.RecoveryFailed,
                Detail = "owned failure detail", RecoveryDetail = "owned recovery detail", RecoveryRequired = true },
            new PatchDatabaseImportCore.Result { Kind = PatchDatabaseImportCore.ResultKind.RecoveryBlocked,
                Detail = "owned startup detail", RecoveryRequired = true },
        };
        string[] english = results.Select(PatchDatabaseImportService.FormatResult).ToArray();
        MyTranslateResource.LoadResource(Path.Combine(FindRepoRoot(), "config", "translate", language + ".txt"));
        for (int i = 0; i < results.Length; i++)
        {
            var result = results[i];
            result.Message = "DO NOT FORWARD THE CORE ENGLISH MESSAGE";
            result.RetainedPath = fixture.Root;
            string message = PatchDatabaseImportService.FormatResult(result);
            Assert.Contains(retained, message);
            Assert.Contains(fixture.Root, message);
            Assert.DoesNotContain("DO NOT FORWARD", message);
            Assert.NotEqual(english[i], message[..message.LastIndexOf('\n')].TrimEnd('\r'));
            if (result.Detail.Length != 0) Assert.Contains(result.Detail, message);
            if (result.RecoveryDetail.Length != 0) Assert.Contains(result.RecoveryDetail, message);
            if (result.CleanupDetail.Length != 0) Assert.Contains(result.CleanupDetail, message);
        }
        Assert.Contains(cleanup, PatchDatabaseImportService.FormatResult(results[0]));
        App.RecordPatchDatabaseRecovery(results[0]);
        Assert.Contains(cleanup, App.PatchDatabaseRecoveryNotice);
        MyTranslateResource.Clear();
        Assert.Contains("The new database is installed", App.PatchDatabaseRecoveryNotice);
        MyTranslateResource.LoadResource(Path.Combine(FindRepoRoot(), "config", "translate", language + ".txt"));
        App.RecordPatchDatabaseRecovery(new PatchDatabaseImportCore.RecoveryException(
            fixture.Root, new IOException("owned exception detail")));
        Assert.Contains(retained, App.PatchDatabaseRecoveryNotice);
        Assert.Contains("owned exception detail", App.PatchDatabaseRecoveryNotice);
        Assert.DoesNotContain("The owned import workspace could not be cleaned up", App.PatchDatabaseRecoveryNotice);
    }

    static void FailCleanup(PatchDatabaseImportCore.Checkpoint point)
    {
        if (point == PatchDatabaseImportCore.Checkpoint.BeforeCleanup)
            throw new IOException("Owned cleanup failure");
    }

    static string FindRepoRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "FEBuilderGBA.sln"))) return dir.FullName;
        throw new InvalidOperationException("Could not locate FEBuilderGBA.sln.");
    }

    static byte[] Zip(string version)
    {
        using var stream = new MemoryStream();
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, true))
        {
            using var writer = new StreamWriter(zip.CreateEntry(version + "/PATCH_test.txt").Open(), new UTF8Encoding(false));
            writer.Write("NAME=Owned offline library\nTYPE=EA\nPATCHED_IF:0x100=AA");
        }
        return stream.ToArray();
    }

    sealed class PickedFile : IDisposable
    {
        readonly PickedFileProxy proxy;
        public IStorageFile File { get; }
        public NonSeekableStream? ReadStream => proxy.ReadStream;
        public PickedFile(byte[] bytes, bool throwOnClose = false)
        {
            File = DispatchProxy.Create<IStorageFile, PickedFileProxy>();
            proxy = (PickedFileProxy)(object)File;
            proxy.Bytes = bytes;
            proxy.ThrowOnClose = throwOnClose;
        }
        public void Dispose() => File.Dispose();
    }

    public class PickedFileProxy : DispatchProxy
    {
        internal byte[] Bytes { get; set; } = Array.Empty<byte>();
        internal bool ThrowOnClose { get; set; }
        internal NonSeekableStream? ReadStream { get; private set; }
        protected override object? Invoke(MethodInfo? method, object?[]? arguments)
        {
            if (method?.Name == nameof(IStorageFile.OpenReadAsync))
            {
                ReadStream = new NonSeekableStream(Bytes, ThrowOnClose);
                return Task.FromResult<Stream>(ReadStream);
            }
            if (method?.Name == nameof(IDisposable.Dispose)) return null;
            throw new InvalidOperationException("The importer must not use this provider operation: " + method?.Name);
        }
    }

    internal sealed class NonSeekableStream : MemoryStream
    {
        public bool Disposed { get; private set; }
        readonly bool throwOnClose;
        public NonSeekableStream(byte[] bytes, bool throwOnClose = false) : base(bytes, false)
            => this.throwOnClose = throwOnClose;
        public override bool CanSeek => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        protected override void Dispose(bool disposing)
        {
            Disposed = true;
            base.Dispose(disposing);
            if (throwOnClose) throw new IOException("Injected provider close failure.");
        }
    }

    sealed class Fixture : IDisposable
    {
        readonly ROM savedRom = CoreState.ROM;
        readonly Undo savedUndo = CoreState.Undo;
        readonly string savedBase = CoreState.BaseDirectory;
        readonly string savedLanguage = CoreState.Language;
        readonly PatchDatabaseImportCore.Result? savedRecovery = App.PatchDatabaseRecoveryResult;
        readonly PatchDatabaseImportCore.RecoveryException? savedRecoveryException = App.PatchDatabaseRecoveryException;
        static readonly FieldInfo translationField = typeof(MyTranslateResource).GetField("Resource", BindingFlags.Static | BindingFlags.NonPublic)!;
        readonly object savedTranslation = translationField.GetValue(null)!;
        PatchDatabaseImportCore.PreparedImport? lastPrepared;
        public ROM Rom { get; } = new ROM();
        public string Root { get; } = Path.Combine(AppContext.BaseDirectory, "TestResults", "zip-service-" + Guid.NewGuid().ToString("N"));
        public string OldFile => Path.Combine(Root, "config", "patch2", "FE8U", "old.txt");

        public Fixture(string header = "BE8E01")
        {
            Directory.CreateDirectory(Root);
            Rom.LoadLow("owned-synthetic.gba", new byte[0x1000000], header);
            CoreState.ROM = Rom;
            CoreState.Undo = new Undo();
            CoreState.BaseDirectory = Root;
            CoreState.Language = "en";
            translationField.SetValue(null, new MyTranslateResourceLow());
            App.ClearPatchDatabaseRecoveryNotice();
        }

        public void SeedOld()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(OldFile)!);
            File.WriteAllText(OldFile, "old");
        }

        public Task<PatchDatabaseImportService.Outcome> Import(PickedFile selected,
            PatchDatabaseImportService.RomIdentity identity, Func<PatchDatabaseImportCore.PreparedImport, Task<bool>> confirm,
            CancellationToken cancellationToken = default, Action<PatchDatabaseImportCore.Checkpoint>? checkpoint = null,
            Action? afterRecovery = null)
            => PatchDatabaseImportService.ImportForTestAsync(selected.File, identity, Root, confirm, cancellationToken,
                async (stream, root, version, token, recovered) =>
                    lastPrepared = await PatchDatabaseImportCore.PrepareForTestAsync(stream, root, version, token, _ => false,
                        checkpoint, recoveryCompleted: () =>
                        {
                            recovered();
                            afterRecovery?.Invoke();
                        }));

        public void AddUnexpectedWorkspaceFile()
        {
            string operation = Assert.Single(Directory.GetDirectories(Path.Combine(Root, ".patch2-import")));
            File.WriteAllText(Path.Combine(operation, "owned-unexpected.txt"), "retain, do not delete");
        }

        public void Dispose()
        {
            lastPrepared?.Dispose();
            CoreState.ROM = savedRom;
            CoreState.Undo = savedUndo;
            CoreState.BaseDirectory = savedBase;
            CoreState.Language = savedLanguage;
            translationField.SetValue(null, savedTranslation);
            App.ClearPatchDatabaseRecoveryNotice();
            if (savedRecovery != null) App.RecordPatchDatabaseRecovery(savedRecovery);
            if (savedRecoveryException != null) App.RecordPatchDatabaseRecovery(savedRecoveryException);
            Directory.Delete(Root, true);
        }
    }
}
