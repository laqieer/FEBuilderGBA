// SPDX-License-Identifier: GPL-3.0-or-later
using System;
using System.Reflection;
using Xunit;

namespace FEBuilderGBA.Tests
{
    public sealed class FontFormMappedImportTests
    {
        [Fact]
        public void ResolveBulkImportMoji_PrefersCanonicalFilenameSuffix()
        {
            MethodInfo method = typeof(FontForm).GetMethod(
                "ResolveBulkImportMoji",
                BindingFlags.NonPublic | BindingFlags.Static);
            Assert.NotNull(method);
            Type priorityType =
                method.GetParameters()[2].ParameterType;
            object[] arguments =
            {
                "A",
                @"nested\text_1234.png",
                Enum.ToObject(priorityType, 0),
                false,
            };

            uint moji = (uint)method.Invoke(null, arguments);

            Assert.Equal(0x1234u, moji);
            Assert.True((bool)arguments[3]);
        }
    }
}
