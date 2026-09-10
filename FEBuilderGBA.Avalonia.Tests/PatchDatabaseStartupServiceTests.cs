using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using FEBuilderGBA.Avalonia.Services;

namespace FEBuilderGBA.Avalonia.Tests;

[Collection("SharedState")]
public class PatchDatabaseStartupServiceTests
{
    [AvaloniaTheory]
    [InlineData("success")]
    [InlineData("retained")]
    [InlineData("fault")]
    [InlineData("closed")]
    [InlineData("replaced")]
    public async Task LoadingLifetimePublishesOnlyAfterRecoveryAndSuppressesLateContinuation(string outcome)
    {
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        bool owns = true;
        int consumers = 0, failures = 0;
        var receipt = new PatchDatabaseImportCore.Result { Success = outcome == "success", RecoveryRequired = outcome == "retained" };
        var service = new PatchDatabaseStartupService(_ =>
        {
            entered.Set();
            release.Wait();
            if (outcome == "fault") throw new IOException("observed recovery fault");
            return receipt;
        });
        var task = service.CompleteAsync("captured", () => owns,
            result => { Assert.Same(receipt, result); consumers++; },
            ex => { Assert.Contains("observed recovery", ex.Message); failures++; });
        try
        {
            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
            Assert.True(entered.Wait(TimeSpan.FromSeconds(10)));
            Assert.Equal(0, consumers);
            Assert.False(task.IsCompleted);
            if (outcome is "closed" or "replaced") owns = false;
        }
        finally { release.Set(); await task; }
        Assert.Equal(outcome is "success" or "retained" ? 1 : 0, consumers);
        Assert.Equal(outcome == "fault" ? 1 : 0, failures);
    }

    [AvaloniaFact]
    public async Task RecoveryRunsOffDispatcherAndIsStartedExactlyOnce()
    {
        int ui = Environment.CurrentManagedThreadId;
        int worker = 0, calls = 0;
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var receipt = new PatchDatabaseImportCore.Result { Success = true };
        var service = new PatchDatabaseStartupService(_ =>
        {
            worker = Environment.CurrentManagedThreadId;
            Interlocked.Increment(ref calls);
            entered.Set();
            // The baseline synchronous implementation fails without blocking the UI forever.
            if (worker != ui) release.Wait();
            return receipt;
        });
        Task<PatchDatabaseImportCore.Result>? task = null;
        try
        {
            task = service.StartAsync("captured-base");
            Assert.True(entered.Wait(TimeSpan.FromSeconds(10)));
            Assert.NotEqual(ui, worker);
            Assert.Same(task, service.StartAsync("reattachment"));
            bool heartbeat = false;
            await Dispatcher.UIThread.InvokeAsync(() => heartbeat = true);
            Assert.True(heartbeat);
            Assert.False(task.IsCompleted);
        }
        finally
        {
            release.Set();
            if (task != null) Assert.Same(receipt, await task);
        }
        Assert.Equal(1, calls);
    }
}
