// SPDX-License-Identifier: GPL-3.0-or-later
using Xunit;

namespace FEBuilderGBA.Core.Tests
{
    /// <summary>
    /// #2050: ProcessRunnerCore process-tree tests spawn real OS processes (PowerShell /
    /// Python), assert host-global side effects (PIDs, process-tree containment) and measure
    /// wall-clock phase timing. Running them in parallel with each other — xUnit parallelizes
    /// different test classes across threads by default — makes those PID-liveness checks and
    /// timing budgets flaky on shared/loaded CI hosts. This collection runs in xUnit's
    /// non-parallel phase: no other Core.Tests collection overlaps it, while unrelated
    /// collections retain their normal parallel behavior outside that phase. The deliberate
    /// wall-clock cost is measured by the #2050 baseline/branch duration gate.
    /// See docs/ENGINEERING-NOTES.md (#1993 / #2050) for the process-tree containment contract
    /// this guards.
    /// </summary>
    [CollectionDefinition(Name, DisableParallelization = true)]
    public sealed class ProcessRunnerTestCollection
    {
        public const string Name = "ProcessRunnerCore process-tree (serial)";
    }
}
