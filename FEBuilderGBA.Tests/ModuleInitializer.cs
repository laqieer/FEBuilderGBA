using System.Runtime.CompilerServices;
using System.Reflection;
using System.Runtime.Loader;

namespace FEBuilderGBA.Tests
{
    public static class ModuleInitializer
    {
        [ModuleInitializer]
        public static void Initialize()
        {
            AssemblyLoadContext.Default.Resolving += (context, assemblyName) =>
            {
                if (assemblyName.Name == "FEBuilderGBA")
                {
                    var assemblyPath = Path.Combine(AppContext.BaseDirectory, "FEBuilderGBA.dll");
                    if (File.Exists(assemblyPath))
                    {
                        return context.LoadFromAssemblyPath(assemblyPath);
                    }
                }
                return null;
            };
        }
    }
}
