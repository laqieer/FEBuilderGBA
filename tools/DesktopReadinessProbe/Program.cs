using System.Text;
using FEBuilderGBA.DesktopReadinessProbe;
using FEBuilderGBA.E2ETests.Helpers;

try
{
    using var output = new StreamWriter(Console.OpenStandardOutput(), Encoding.ASCII)
    {
        AutoFlush = true
    };
    return ProbeCommand.Run(args, () => DesktopReadiness.Probe(), output);
}
catch (Exception)
{
    return 4;
}
