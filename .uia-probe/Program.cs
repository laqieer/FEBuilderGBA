using System.Diagnostics;
using System.Windows.Automation;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using Avalonia.Themes.Fluent;
using FEBuilderGBA.Avalonia;
using FEBuilderGBA.Avalonia.Services;

internal sealed class ProbeApp : Application
{
    public override void Initialize()
    {
        Styles.Add(new FluentTheme());
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop)
        {
            throw new InvalidOperationException("Classic desktop lifetime was not initialized.");
        }

        var owner = new Window { Title = "UIA Probe Host", Width = 320, Height = 180 };
        desktop.MainWindow = owner;
        WindowManager.Instance.SetService(new DesktopNavigationService());
        var services = new ProbeServices(owner);
        owner.Show();
        Dispatcher.UIThread.Post(() => RunProbe(desktop, services), DispatcherPriority.Background);
        base.OnFrameworkInitializationCompleted();
    }

    static void RunProbe(IClassicDesktopStyleApplicationLifetime desktop, ProbeServices services)
    {
        _ = Task.Run(async () =>
        {
            await Task.Delay(TimeSpan.FromSeconds(20));
            Console.Error.WriteLine("WATCHDOG: modal UIA probe did not finish.");
            Environment.Exit(2);
        });

        try
        {
            Console.WriteLine("START_NO");
            Task noAutomation = Task.Run(() => InspectAndInvoke("No"));
            bool noResult = services.ShowQuestion("UIA smoke test (No)");
            noAutomation.GetAwaiter().GetResult();
            Console.WriteLine($"SHOW_QUESTION_NO={noResult}");
            if (noResult)
            {
                throw new InvalidOperationException("No must return false.");
            }

            Console.WriteLine("START_YES");
            Task yesAutomation = Task.Run(() => InspectAndInvoke("Yes"));
            bool yesResult = services.ShowQuestion("UIA smoke test (Yes)");
            yesAutomation.GetAwaiter().GetResult();
            Console.WriteLine($"SHOW_QUESTION_YES={yesResult}");
            if (!yesResult)
            {
                throw new InvalidOperationException("Yes must return true.");
            }

            desktop.Shutdown();
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception);
            desktop.Shutdown(1);
        }
    }

    static void InspectAndInvoke(string action)
    {
        int processId = Environment.ProcessId;
        var windowCondition = new AndCondition(
            new PropertyCondition(AutomationElement.ProcessIdProperty, processId),
            new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Window));
        var buttonCondition = new AndCondition(
            new PropertyCondition(AutomationElement.ProcessIdProperty, processId),
            new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Button));
        var timer = Stopwatch.StartNew();
        AutomationElement? yes = null;
        AutomationElement? no = null;
        while (timer.Elapsed < TimeSpan.FromSeconds(10))
        {
            var buttons = AutomationElement.RootElement
                .FindAll(TreeScope.Descendants, buttonCondition)
                .Cast<AutomationElement>()
                .ToArray();
            yes = buttons.FirstOrDefault(
                element => element.Current.AutomationId == "MessageBoxContent_Yes_Button");
            no = buttons.FirstOrDefault(
                element => element.Current.AutomationId == "MessageBoxContent_No_Button");
            if (yes != null && no != null)
            {
                break;
            }

            Thread.Sleep(100);
        }

        if (yes == null || no == null)
        {
            string available = string.Join(
                "; ",
                AutomationElement.RootElement
                    .FindAll(TreeScope.Children, windowCondition)
                    .Cast<AutomationElement>()
                    .Select(element => $"Name='{element.Current.Name}', Id='{element.Current.AutomationId}'"));
            throw new InvalidOperationException(
                $"UIA messagebox buttons not found. Process windows: {available}");
        }

        Describe(yes);
        Describe(no);
        AutomationElement target = action == "Yes" ? yes : no;
        ((InvokePattern)target.GetCurrentPattern(InvokePattern.Pattern)).Invoke();
    }

    static void Describe(AutomationElement element)
    {
        var current = element.Current;
        Console.WriteLine(
            $"BUTTON Name='{current.Name}', AutomationId='{current.AutomationId}', " +
            $"ControlType='{current.ControlType.ProgrammaticName}', Enabled={current.IsEnabled}, " +
            $"Offscreen={current.IsOffscreen}");
    }
}

internal sealed class ProbeServices(Window owner) : AvaloniaAppServices
{
    protected override Window? GetMainWindow() => owner;
}

internal static class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        AppBuilder.Configure<ProbeApp>()
            .UsePlatformDetect()
            .StartWithClassicDesktopLifetime(args);
    }
}
