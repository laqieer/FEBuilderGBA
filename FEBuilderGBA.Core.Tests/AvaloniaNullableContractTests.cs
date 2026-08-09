using System.Reflection;
using System.Threading;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;
using Xunit;

namespace FEBuilderGBA.Core.Tests
{
    [Collection("SharedState")]
    public class AvaloniaNullableContractTests
    {
        static readonly NullabilityInfoContext Nullability = new();

        static MethodInfo RequiredMethod(Type type, string name, Type[]? parameterTypes = null)
        {
            MethodInfo? method = parameterTypes == null
                ? type.GetMethod(name)
                : type.GetMethod(name, parameterTypes);
            return method ?? throw new InvalidOperationException($"{type.FullName}.{name} was not found.");
        }

        static void AssertNullableReturn(Type type, string name, Type[]? parameterTypes = null)
        {
            MethodInfo method = RequiredMethod(type, name, parameterTypes);
            NullabilityInfo info = Nullability.Create(method.ReturnParameter);
            Assert.Equal(NullabilityState.Nullable, info.ReadState);
        }

        static void AssertNullableParameter(Type type, string methodName, string parameterName, Type[] parameterTypes)
        {
            MethodInfo method = RequiredMethod(type, methodName, parameterTypes);
            ParameterInfo parameter = method.GetParameters().Single(p => p.Name == parameterName);
            NullabilityInfo info = Nullability.Create(parameter);
            Assert.Equal(NullabilityState.Nullable, info.ReadState);
        }

        static void AssertNullableProperty(Type type, string name)
        {
            PropertyInfo property = type.GetProperty(name)
                ?? throw new InvalidOperationException($"{type.FullName}.{name} was not found.");
            NullabilityInfo info = Nullability.Create(property);
            Assert.Equal(NullabilityState.Nullable, info.ReadState);
        }

        [Fact]
        public void ItemShopVectorBuilders_DeclareNullableReturns()
        {
            AssertNullableReturn(typeof(ItemShopViewerViewModel), nameof(ItemShopViewerViewModel.BuildVectorForWrite));
            AssertNullableReturn(typeof(ItemShopViewerViewModel), nameof(ItemShopViewerViewModel.BuildVectorForAppend));
            AssertNullableReturn(typeof(ItemShopViewerViewModel), nameof(ItemShopViewerViewModel.BuildVectorForRemoveLast));
        }

        [Fact]
        public void PatchSkillNameResolver_DeclaresNullableReturn()
        {
            AssertNullableReturn(typeof(PatchDetectionService), nameof(PatchDetectionService.ResolveSkillName));
        }

        [Fact]
        public void SourceRouting_AllowsMissingAsmMap()
        {
            AssertNullableProperty(typeof(CoreState), nameof(CoreState.AsmMapFileAsmCache));

            AssertNullableParameter(
                typeof(DecompShopSourceWriteCore),
                nameof(DecompShopSourceWriteCore.TryRouteShopSaveToSource),
                "asmMap",
                new[]
                {
                    typeof(ROM),
                    typeof(DecompProject),
                    typeof(IAsmMapFile),
                    typeof(uint),
                    typeof(IReadOnlyList<ushort>),
                });
        }

        [Fact]
        public void FEMapCreatorDiscovery_AllowsMissingAssetsRoot()
        {
            AssertNullableParameter(
                typeof(FEMapCreatorTilesetDiscoveryCore),
                nameof(FEMapCreatorTilesetDiscoveryCore.DiscoverTilesets),
                "assetsDir",
                new[]
                {
                    typeof(string),
                    typeof(string),
                    typeof(ProcessRunnerDelegate),
                    typeof(CancellationToken),
                    typeof(ProcessRunnerCancellableDelegate),
                });

            AssertNullableParameter(
                typeof(FEMapCreatorDiscoverDelegate),
                "Invoke",
                "assetsRoot",
                new[]
                {
                    typeof(string),
                    typeof(string),
                    typeof(CancellationToken),
                });
        }
    }
}
