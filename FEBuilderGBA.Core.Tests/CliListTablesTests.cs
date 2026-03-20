using System.Linq;
using Xunit;
using FEBuilderGBA;

namespace FEBuilderGBA.Core.Tests
{
    public class CliListTablesTests
    {
        [Fact]
        public void GetTableNames_ContainsUnits()
        {
            var names = StructExportCore.GetTableNames().ToList();
            Assert.Contains("units", names);
        }

        [Fact]
        public void GetTableNames_ContainsClasses()
        {
            var names = StructExportCore.GetTableNames().ToList();
            Assert.Contains("classes", names);
        }

        [Fact]
        public void GetTableNames_ContainsItems()
        {
            var names = StructExportCore.GetTableNames().ToList();
            Assert.Contains("items", names);
        }

        [Fact]
        public void GetTableNames_ContainsPortraits()
        {
            var names = StructExportCore.GetTableNames().ToList();
            Assert.Contains("portraits", names);
        }

        [Fact]
        public void GetTableNames_Has40PlusTables()
        {
            var names = StructExportCore.GetTableNames().ToList();
            Assert.True(names.Count >= 40, $"Expected >= 40 tables but got {names.Count}");
        }
    }
}
