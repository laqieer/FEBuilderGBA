using System.Collections.Concurrent;
using System.Reflection;
using Xunit.Abstractions;
using Xunit.Sdk;

namespace FEBuilderGBA.Avalonia.Tests;

[Collection("SharedState")]
public sealed class SyntheticPatchImportDiscoveryTests
{
    [Fact]
    public async Task DeferredDiscoveryLeavesUnselectedDataUntouchedAndRunsFourSelectedResults()
    {
        Probes.Reset();
        var sink = new NullMessageSink();
        var options = new DiscoveryOptions();
        options.SetValue("xunit.discovery.PreEnumerateTheories", false);
        var cases = Discover(options, sink).ToArray();

        Assert.Equal(4, cases.Length);
        Assert.Equal(2, cases.Count(test => test is XunitTheoryTestCase));
        Assert.Equal(2, cases.Count(test => test.GetType() == typeof(XunitTestCase)));
        Assert.All(cases, test => Assert.False(string.IsNullOrEmpty(test.DisplayName)));
        Assert.Equal(0, Probes.UnselectedProviderCalls);
        Assert.Equal(0, Probes.UnselectedBodyCalls);
        Assert.Equal(0, Probes.FactCalls);
        Assert.Empty(Probes.RowValues);

        var selected = cases.Where(test => test.TestMethod.Method.Name != nameof(Probes.Unselected)).ToArray();
        Assert.Equal(3, selected.Length);
        using var bus = new RecordingMessageBus();
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        int total = 0, failed = 0, skipped = 0;
        foreach (var test in selected)
        {
            var aggregator = new ExceptionAggregator();
            var result = await test.RunAsync(sink, bus, Array.Empty<object>(), aggregator, cancellation);
            Assert.False(aggregator.HasExceptions);
            total += result.Total;
            failed += result.Failed;
            skipped += result.Skipped;
        }

        Assert.Equal(4, total);
        Assert.Equal(0, failed);
        Assert.Equal(0, skipped);
        var passed = bus.Messages.OfType<ITestPassed>().ToArray();
        Assert.Equal(4, passed.Length);
        Assert.Single(passed, message => message.Test.TestCase.TestMethod.Method.Name == nameof(Probes.FirstFact));
        Assert.Single(passed, message => message.Test.TestCase.TestMethod.Method.Name == nameof(Probes.SecondFact));
        Assert.Equal(2, passed.Count(message => message.Test.TestCase.TestMethod.Method.Name == nameof(Probes.SelectedRows)));
        Assert.DoesNotContain(bus.Messages, message => message is ITestFailed or ITestSkipped or IErrorMessage);
        Assert.Equal(2, Probes.FactCalls);
        Assert.Equal(new[] { false, true }, Probes.RowValues.Order().ToArray());
        Assert.Equal(0, Probes.UnselectedProviderCalls);
        Assert.Equal(0, Probes.UnselectedBodyCalls);
    }

    [Fact]
    public void EagerDiscoveryPositiveControlActuallyReachesTheThrowingProvider()
    {
        Probes.Reset();
        var sink = new NullMessageSink();
        var options = new DiscoveryOptions();
        options.SetValue("xunit.discovery.PreEnumerateTheories", true);
        var method = CreateMethod(nameof(Probes.Unselected), theory: true);
        var cases = new TheoryDiscoverer(sink).Discover(options, method, Metadata(theory: true)).ToArray();

        // The discoverer handles the provider exception; never run its returned case.
        Assert.Single(cases);
        Assert.Equal(1, Probes.UnselectedProviderCalls);
        Assert.Equal(0, Probes.UnselectedBodyCalls);
        Assert.Equal(0, Probes.FactCalls);
        Assert.Empty(Probes.RowValues);
    }

    static IEnumerable<IXunitTestCase> Discover(ITestFrameworkDiscoveryOptions options, IMessageSink sink)
    {
        foreach (var name in new[] { nameof(Probes.FirstFact), nameof(Probes.SecondFact),
                     nameof(Probes.SelectedRows), nameof(Probes.Unselected) })
        {
            bool theory = name is nameof(Probes.SelectedRows) or nameof(Probes.Unselected);
            var method = CreateMethod(name, theory);
            IXunitTestCaseDiscoverer discoverer = theory ? new TheoryDiscoverer(sink) : new FactDiscoverer(sink);
            foreach (var test in discoverer.Discover(options, method, Metadata(theory)))
                yield return test;
        }
    }

    static TestMethod CreateMethod(string name, bool theory)
    {
        var type = new ReflectionTypeInfo(typeof(Probes));
        var assembly = new TestAssembly(new ReflectionAssemblyInfo(typeof(Probes).Assembly));
        var collection = new TestCollection(assembly, null, "Controlled deferred discovery");
        var testClass = new TestClass(collection, type);
        return new TestMethod(testClass,
            new ProbeMethod(new ReflectionMethodInfo(typeof(Probes).GetMethod(name)!), theory));
    }

    static IAttributeInfo Metadata(bool theory) => new ProbeAttribute(theory ? new TheoryAttribute() : new FactAttribute());

    sealed class DiscoveryOptions : ITestFrameworkDiscoveryOptions
    {
        readonly Dictionary<string, object?> values = new();
        public TValue GetValue<TValue>(string name) => values.TryGetValue(name, out var value) ? (TValue)value! : default!;
        public void SetValue<TValue>(string name, TValue value) => values[name] = value;
    }

    sealed class RecordingMessageBus : IMessageBus
    {
        public ConcurrentQueue<IMessageSinkMessage> Messages { get; } = new();
        public bool QueueMessage(IMessageSinkMessage message)
        {
            Messages.Enqueue(message);
            return true;
        }
        public void Dispose() { }
    }

    sealed class ProbeAttribute(FactAttribute attribute) : LongLivedMarshalByRefObject, IAttributeInfo
    {
        public IEnumerable<object> GetConstructorArguments() => Array.Empty<object>();
        public IEnumerable<IAttributeInfo> GetCustomAttributes(string assemblyQualifiedAttributeTypeName) =>
            Array.Empty<IAttributeInfo>();
        public TValue GetNamedArgument<TValue>(string argumentName) =>
            (TValue)typeof(FactAttribute).GetProperty(argumentName)!.GetValue(attribute)!;
    }

    // Composition keeps real parameters/data/MethodInfo while satisfying Initialize's
    // second FactAttribute lookup; ReflectionMethodInfo.GetCustomAttributes is final.
    sealed class ProbeMethod(ReflectionMethodInfo inner, bool theory) : LongLivedMarshalByRefObject, IReflectionMethodInfo
    {
        public MethodInfo MethodInfo => inner.MethodInfo;
        public bool IsAbstract => inner.IsAbstract;
        public bool IsGenericMethodDefinition => inner.IsGenericMethodDefinition;
        public bool IsPublic => inner.IsPublic;
        public bool IsStatic => inner.IsStatic;
        public string Name => inner.Name;
        public ITypeInfo ReturnType => inner.ReturnType;
        public ITypeInfo Type => inner.Type;
        public IEnumerable<ITypeInfo> GetGenericArguments() => inner.GetGenericArguments();
        public IEnumerable<IParameterInfo> GetParameters() => inner.GetParameters();
        public IMethodInfo MakeGenericMethod(params ITypeInfo[] typeArguments) => inner.MakeGenericMethod(typeArguments);
        public IEnumerable<IAttributeInfo> GetCustomAttributes(string assemblyQualifiedAttributeTypeName)
        {
            if (assemblyQualifiedAttributeTypeName == typeof(FactAttribute).AssemblyQualifiedName ||
                (theory && assemblyQualifiedAttributeTypeName == typeof(TheoryAttribute).AssemblyQualifiedName))
                return new[] { Metadata(theory) };
            return inner.GetCustomAttributes(assemblyQualifiedAttributeTypeName);
        }
    }

    public sealed class Probes
    {
        public static int FactCalls;
        public static int UnselectedProviderCalls;
        public static int UnselectedBodyCalls;
        public static List<bool> RowValues { get; } = new();

        public static void Reset()
        {
            FactCalls = UnselectedProviderCalls = UnselectedBodyCalls = 0;
            RowValues.Clear();
        }

        public void FirstFact() => FactCalls++;
        public void SecondFact() => FactCalls++;

#pragma warning disable xUnit1008 // Data-only probes deliberately excluded from outer discovery.
        [InlineData(false)]
        [InlineData(true)]
        public void SelectedRows(bool value) => RowValues.Add(value);

        [MemberData(nameof(UnselectedRows))]
        public void Unselected(int value)
        {
            UnselectedBodyCalls++;
            throw new InvalidOperationException("Unselected probe body must not run.");
        }
#pragma warning restore xUnit1008

        public static IEnumerable<object[]> UnselectedRows()
        {
            UnselectedProviderCalls++;
            throw new InvalidOperationException("Unselected provider reached during eager discovery.");
        }
    }
}
