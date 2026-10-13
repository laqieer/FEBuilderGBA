// SPDX-License-Identifier: GPL-3.0-or-later
using System.Runtime.ExceptionServices;
using Avalonia;
using Avalonia.Media;
using Avalonia.Threading;

namespace FEBuilderGBA.Avalonia.Tests;

public sealed class RealBitmapTestHost : IAsyncLifetime
{
    static readonly TimeSpan Deadline = TimeSpan.FromSeconds(30);
    readonly object gate = new();
    readonly CancellationTokenSource stop = new();
    readonly TaskCompletionSource<Dispatcher> ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    readonly TaskCompletionSource exited = new(TaskCreationOptions.RunContinuationsAsynchronously);
    readonly Func<AppBuilder> appFactory;
    readonly Thread worker;
    Task? shutdown;
    bool stopping;

    public RealBitmapTestHost() : this(ImageUnitMoveIconDisplayTests.ImageUnitMoveIconThumbnailTestApp.BuildAvaloniaApp)
    {
    }

    internal RealBitmapTestHost(Func<AppBuilder> appFactory)
    {
        this.appFactory = appFactory;
        // No worker can see partially published ownership, unlike Task.Run's captured return value.
        worker = new Thread(WorkerMain) { IsBackground = true, Name = "Isolated real-bitmap UI" };
        worker.Start();
    }

    public async Task InitializeAsync()
    {
        try
        {
            await ready.Task.WaitAsync(Deadline);
        }
        catch (Exception error)
        {
            await StopAfterFailureAsync(error);
        }
    }

    public async Task RunAsync(Action action)
    {
        lock (gate)
            ObjectDisposedException.ThrowIf(stopping, this);
        Dispatcher dispatcher = await ready.Task.WaitAsync(Deadline);
        Task operation;
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(stopping, this);
            operation = QueueDispatch(exited.Task, () => dispatcher.InvokeAsync(action).GetTask());
        }

        try
        {
            await Task.WhenAny(operation, exited.Task).WaitAsync(Deadline);
        }
        catch (TimeoutException error)
        {
            await StopAfterFailureAsync(error);
            return;
        }
        Exception? failure = null;
        if (operation.IsCompleted)
        {
            try
            {
                await operation;
            }
            catch (Exception error)
            {
                failure = error;
            }
        }
        if (exited.Task.IsCompleted)
        {
            try
            {
                await exited.Task;
            }
            catch (Exception error)
            {
                failure = Combine(failure, error);
            }
        }
        if (failure != null)
            ExceptionDispatchInfo.Capture(failure).Throw();
        if (!operation.IsCompleted)
            throw new InvalidOperationException("Bitmap worker stopped before dispatch completed.");
    }

    internal static Task QueueDispatch(Task workerCompletion, Func<Task> queue)
    {
        if (!workerCompletion.IsCompleted)
            return queue();
        workerCompletion.GetAwaiter().GetResult();
        throw new InvalidOperationException("Bitmap worker stopped before dispatch completed.");
    }

    public Task DisposeAsync()
    {
        lock (gate)
        {
            stopping = true;
            return shutdown ??= StopAndJoinAsync();
        }
    }

    async Task StopAfterFailureAsync(Exception error)
    {
        try
        {
            await DisposeAsync();
        }
        catch (Exception cleanupError)
        {
            error = Combine(error, cleanupError);
        }
        ExceptionDispatchInfo.Capture(error).Throw();
    }

    async Task StopAndJoinAsync()
    {
        Exception? failure = null;
        try
        {
            stop.Cancel();
        }
        catch (Exception error)
        {
            failure = error;
        }
        try
        {
            await exited.Task.WaitAsync(Deadline);
        }
        catch (Exception error)
        {
            failure = Combine(failure, error);
        }
        if (!worker.Join(Deadline))
            failure = Combine(failure, new TimeoutException("Bitmap worker did not join; process cleanup is incomplete."));
        else
            stop.Dispose();

        if (failure != null)
            ExceptionDispatchInfo.Capture(failure).Throw();
    }

    void WorkerMain()
    {
        Dispatcher? dispatcher = null;
        IDisposable? fonts = null;
        Exception? failure = null;
        try
        {
            appFactory().SetupWithoutStarting();
            dispatcher = Dispatcher.UIThread;
            fonts = (IDisposable)FontManager.Current;
            ready.TrySetResult(dispatcher);
            dispatcher.MainLoop(stop.Token);
        }
        catch (Exception error)
        {
            failure = error;
            ready.TrySetException(error);
        }
        finally
        {
            try
            {
                fonts?.Dispose();
            }
            catch (Exception error)
            {
                failure = Combine(failure, error);
            }
            try
            {
                dispatcher?.InvokeShutdown();
            }
            catch (Exception error)
            {
                failure = Combine(failure, error);
            }

            if (failure != null)
                exited.TrySetException(failure);
            else
                exited.TrySetResult();
        }
    }

    static Exception Combine(Exception? first, Exception second)
        => first == null || ReferenceEquals(first, second) ? second
            : new AggregateException("Bitmap action/startup and cleanup failures.", first, second);
}
