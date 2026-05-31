using Xunit;

// Force all test collections to run sequentially.  xUnit serializes tests
// *within* a single collection but runs different collections (and uncollected
// classes) in parallel by default.  These tests share mutable global state on
// CoreState (ROM, caches, encoders, BaseDirectory, AIScript, ...), and only a
// subset of classes opt into [Collection("SharedState")].  Cross-collection
// parallelism therefore interleaves shared-state mutations and produces
// order-dependent, nondeterministic CI failures (see #811).  Disabling
// cross-collection parallelization makes the suite deterministic.  Mirrors the
// existing precedent in FEBuilderGBA.E2ETests/AssemblyInfo.cs.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
