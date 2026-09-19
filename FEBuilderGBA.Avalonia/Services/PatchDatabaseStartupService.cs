using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;

namespace FEBuilderGBA.Avalonia.Services;

internal sealed class PatchDatabaseStartupService
{
    readonly Func<string, PatchDatabaseImportCore.Result> recover;
    Task<PatchDatabaseImportCore.Result>? recovery;

    internal PatchDatabaseStartupService(Func<string, PatchDatabaseImportCore.Result>? recover = null)
        => this.recover = recover ?? PatchDatabaseImportCore.RecoverPending;

    internal Task<PatchDatabaseImportCore.Result> StartAsync(string baseDirectory)
        => recovery ??= Task.Run(() => recover(baseDirectory));

    internal async Task CompleteAsync(string baseDirectory, Func<bool> ownsLoadingRoot,
        Action<PatchDatabaseImportCore.Result> publish, Action<Exception> failed)
    {
        // Let the non-async framework override finish base initialization even on a fast recovery.
        await Task.Yield();
        try
        {
            var result = await StartAsync(baseDirectory);
            if (ownsLoadingRoot()) publish(result);
        }
        catch (Exception ex)
        {
            if (ownsLoadingRoot()) failed(ex);
        }
    }

    internal static void Handoff(IClassicDesktopStyleApplicationLifetime lifetime, Window loading,
        Func<Window> createShell)
    {
        Window? shell = null;
        var previousManagedMain = WindowManager.Instance.MainWindow;
        try
        {
            shell = createShell();
            lifetime.MainWindow = shell;
            shell.Show();
        }
        catch
        {
            lifetime.MainWindow = loading;
            try { shell?.Close(); }
            finally { WindowManager.Instance.MainWindow = previousManagedMain; }
            throw;
        }
        loading.Close();
    }
}
