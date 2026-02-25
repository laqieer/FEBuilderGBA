using System.Reflection;
using System.Runtime.Loader;

namespace FEBuilderGBA.Tests.Unit
{
    public class BasicAssemblyTests
    {
        static BasicAssemblyTests()
        {
            // Pre-load the assembly
            var assemblyPath = Path.Combine(AppContext.BaseDirectory, "FEBuilderGBA.dll");
            if (File.Exists(assemblyPath))
            {
                try
                {
                    AssemblyLoadContext.Default.LoadFromAssemblyPath(assemblyPath);
                }
                catch
                {
                    // Ignore if already loaded
                }
            }
        }

        [Fact]
        public void CanLoadFEBuilderGBAAssembly()
        {
            // Try to load the assembly
            var assemblyPath = Path.Combine(AppContext.BaseDirectory, "FEBuilderGBA.dll");
            Assert.True(File.Exists(assemblyPath), $"FEBuilderGBA.dll should exist at {assemblyPath}");

            var assembly = AssemblyLoadContext.Default.LoadFromAssemblyPath(assemblyPath);
            Assert.NotNull(assembly);
            Assert.Equal("FEBuilderGBA", assembly.GetName().Name);
        }

        [Fact]
        public void CanAccessUClass()
        {
            // Try to access U class by loading assembly first
            var assemblyPath = Path.Combine(AppContext.BaseDirectory, "FEBuilderGBA.dll");
            var assembly = AssemblyLoadContext.Default.LoadFromAssemblyPath(assemblyPath);

            var type = assembly.GetType("FEBuilderGBA.U");
            Assert.NotNull(type);
            Assert.True(type.IsPublic);
            Assert.True(type.IsClass);
        }

        [Fact]
        public void CanAccessRegexCacheClass()
        {
            // Try to access RegexCache class
            var assemblyPath = Path.Combine(AppContext.BaseDirectory, "FEBuilderGBA.dll");
            var assembly = AssemblyLoadContext.Default.LoadFromAssemblyPath(assemblyPath);

            var type = assembly.GetType("FEBuilderGBA.RegexCache");
            Assert.NotNull(type);
        }
    }
}
