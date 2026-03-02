using Xunit;

// Force all test classes to run sequentially.  Each E2E test launches a real
// app process; concurrent launches on a single desktop cause resource contention
// and flaky failures (window detection races, slow form load, etc.).
[assembly: CollectionBehavior(DisableTestParallelization = true)]
