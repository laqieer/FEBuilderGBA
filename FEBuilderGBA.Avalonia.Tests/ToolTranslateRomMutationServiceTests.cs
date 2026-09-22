using FEBuilderGBA.Avalonia.Services;

namespace FEBuilderGBA.Avalonia.Tests;

[Collection("SharedState")]
public class ToolTranslateRomMutationServiceTests
{
    sealed class ReplaceStateOnCommit : UndoService
    {
        internal ROM? ReplacementRom { get; private set; }
        internal Undo? ReplacementUndo { get; private set; }

        public override bool CommitExternal(ROM rom, Undo expectedUndo, Undo.UndoData undoData)
        {
            ReplacementRom = new ROM();
            ReplacementRom.SwapNewROMDataDirect(new byte[0x200]);
            ReplacementUndo = new Undo();
            CoreState.ROM = ReplacementRom;
            CoreState.Undo = ReplacementUndo;
            return base.CommitExternal(rom, expectedUndo, undoData);
        }
    }

    [Fact]
    public async Task SuccessfulWorkerCommitsCloneAsOneUndoableMutation()
    {
        using var fixture = new PatchManagerOperationGuardTests.Fixture();
        var identity = PatchDatabaseImportService.CaptureLoadedRom()!;
        var result = await ToolTranslateRomMutationService.ExecuteAsync(
            fixture.Rom, identity, new UndoService(), (working, undo) =>
            {
                working.write_u8(0x300, 0x7A, undo);
                return 3;
            });

        Assert.True(result.Applied);
        Assert.Equal(3, result.Total);
        Assert.Equal(0x7Au, fixture.Rom.u8(0x300));
        Undo.UndoData committed = Assert.Single(CoreState.Undo.UndoBuffer);
        Assert.DoesNotContain(committed.list, position =>
            position.addr == 0 && position.data.Length == fixture.Rom.Data.Length);
        CoreState.Undo.RunUndo();
        Assert.Equal(0u, fixture.Rom.u8(0x300));
    }

    [Fact]
    public async Task AppendOnlyWorkerCommitsLengthAsUndoableMutation()
    {
        using var fixture = new PatchManagerOperationGuardTests.Fixture();
        var identity = PatchDatabaseImportService.CaptureLoadedRom()!;
        int originalLength = fixture.Rom.Data.Length;

        var result = await ToolTranslateRomMutationService.ExecuteAsync(
            fixture.Rom, identity, new UndoService(), (working, undo) =>
            {
                Assert.True(working.write_resize_data((uint)(working.Data.Length + 4)));
                return 4;
            });

        Assert.True(result.Applied);
        Assert.Equal(originalLength + 4, fixture.Rom.Data.Length);
        Undo.UndoData committed = Assert.Single(CoreState.Undo.UndoBuffer);
        Assert.Empty(committed.list);
        Assert.Equal((uint)originalLength, committed.filesize);

        CoreState.Undo.RunUndo();
        Assert.Equal(originalLength, fixture.Rom.Data.Length);
    }

    [Fact]
    public async Task StateReplacementAtCommitRestoresOnlySourceRom()
    {
        using var fixture = new PatchManagerOperationGuardTests.Fixture();
        var identity = PatchDatabaseImportService.CaptureLoadedRom()!;
        int originalLength = fixture.Rom.Data.Length;
        var undoService = new ReplaceStateOnCommit();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            ToolTranslateRomMutationService.ExecuteAsync(
                fixture.Rom, identity, undoService, (working, undo) =>
                {
                    Assert.True(working.write_resize_data((uint)(working.Data.Length + 4)));
                    return 4;
                }));

        Assert.Equal(originalLength, fixture.Rom.Data.Length);
        Assert.NotNull(undoService.ReplacementRom);
        Assert.Equal(0x200, undoService.ReplacementRom.Data.Length);
        Assert.NotNull(undoService.ReplacementUndo);
        Assert.Empty(undoService.ReplacementUndo.UndoBuffer);
    }

    [Theory]
    [InlineData("replace")]
    [InlineData("reload")]
    [InlineData("edit")]
    [InlineData("info")]
    public async Task LoadedRomChangeWhileWorkerRunsDiscardsClone(string change)
    {
        using var fixture = new PatchManagerOperationGuardTests.Fixture();
        var identity = PatchDatabaseImportService.CaptureLoadedRom()!;
        byte[] original = (byte[])fixture.Rom.Data.Clone();
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        Task<ToolTranslateRomMutationService.Result> pending =
            ToolTranslateRomMutationService.ExecuteAsync(
                fixture.Rom, identity, new UndoService(), (working, undo) =>
                {
                    entered.Set();
                    release.Wait();
                    working.write_u8(0x300, 0x7A, undo);
                    return 3;
                });
        Assert.True(entered.Wait(TimeSpan.FromSeconds(10)));

        switch (change)
        {
            case "replace":
                var replacement = new ROM();
                replacement.LoadLow("replacement-synthetic.gba", new byte[0x1000000], "BE8E01");
                CoreState.ROM = replacement;
                break;
            case "reload":
                fixture.Rom.LoadLow("reload-synthetic.gba", (byte[])fixture.Rom.Data.Clone(), "BE8E01");
                break;
            case "edit":
                fixture.Rom.Data[0x400] ^= 1;
                break;
            case "info":
                typeof(ROM).GetProperty(nameof(ROM.RomInfo))!.SetValue(
                    fixture.Rom, new ROMFE8U(fixture.Rom));
                break;
        }
        byte[] changed = (byte[])fixture.Rom.Data.Clone();
        release.Set();

        var result = await pending;

        Assert.False(result.Applied);
        Assert.Equal(changed, fixture.Rom.Data);
        Assert.Empty(CoreState.Undo.UndoBuffer);
        if (change == "replace")
            Assert.Equal(original, fixture.Rom.Data);
    }

    [Fact]
    public async Task WorkerFailureLeavesLoadedRomAndUndoUntouched()
    {
        using var fixture = new PatchManagerOperationGuardTests.Fixture();
        var identity = PatchDatabaseImportService.CaptureLoadedRom()!;
        byte[] before = (byte[])fixture.Rom.Data.Clone();

        await Assert.ThrowsAsync<IOException>(() =>
            ToolTranslateRomMutationService.ExecuteAsync(
                fixture.Rom, identity, new UndoService(), (working, undo) =>
                {
                    working.write_u8(0x300, 0x7A, undo);
                    throw new IOException("Injected worker failure.");
                }));

        Assert.Equal(before, fixture.Rom.Data);
        Assert.Empty(CoreState.Undo.UndoBuffer);
    }

    [Fact]
    public async Task BoundRecycleWritesNeverTouchLoadedRomBeforeApply()
    {
        using var fixture = new PatchManagerOperationGuardTests.Fixture();
        var identity = PatchDatabaseImportService.CaptureLoadedRom()!;
        byte[] before = (byte[])fixture.Rom.Data.Clone();
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        Task<ToolTranslateRomMutationService.Result> pending =
            ToolTranslateRomMutationService.ExecuteAsync(
                fixture.Rom, identity, new UndoService(), (working, undo) =>
                {
                    var recycle = new RecycleAddress(working,
                    [
                        new Address(0x300, 8, U.NOT_FOUND, "", Address.DataTypeEnum.BIN),
                    ]);
                    Assert.Equal(0x300u, recycle.Write([0x7A, 0x7B], undo));
                    recycle.BlackOut(undo);
                    entered.Set();
                    release.Wait();
                    return 2;
                });

        Assert.True(entered.Wait(TimeSpan.FromSeconds(10)));
        Assert.Equal(before, fixture.Rom.Data);
        release.Set();
        var result = await pending;

        Assert.True(result.Applied);
        Assert.Equal(0x7Au, fixture.Rom.u8(0x300));
        Assert.Equal(0x7Bu, fixture.Rom.u8(0x301));
        Assert.Equal(0u, fixture.Rom.u8(0x304));
    }
}
