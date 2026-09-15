using System.Text;
using FEBuilderGBA.DesktopReadinessProbe;
using FEBuilderGBA.E2ETests.Helpers;

try
{
    using var output = new StreamWriter(Console.OpenStandardOutput(), Encoding.ASCII)
    {
        AutoFlush = true
    };
    return args.Length > 0 && args[0] == "--supervise-own-desktop"
        ? ProbeSupervisor.Run(args, output)
        : ProbeCommand.Run(args, () => DesktopReadiness.Probe(), output);
}
catch (Exception)
{
    return 4;
}
