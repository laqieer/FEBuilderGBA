using System.IO;
using Xunit;
using FEBuilderGBA;

namespace FEBuilderGBA.Core.Tests
{
    public class StructMetadataTests
    {
        [Fact]
        public void LoadFromFile_ParsesStructDef()
        {
            string tmpFile = Path.GetTempFileName();
            try
            {
                File.WriteAllText(tmpFile, @"// Test struct metadata
@UnitData 30
0	B	NameID	Unit name text ID
2	W	ClassID	Unit class
4	B	Level	Unit level
");

                var meta = new StructMetadata();
                meta.LoadFromFile(tmpFile);

                var unit = meta.GetStruct("UnitData");
                Assert.NotNull(unit);
                Assert.Equal("UnitData", unit.Name);
                Assert.Equal(0x30u, unit.DataSize);
                Assert.Equal(3, unit.Fields.Count);

                Assert.Equal("NameID", unit.Fields[0].Name);
                Assert.Equal(0u, unit.Fields[0].Offset);
                Assert.Equal(StructMetadata.FieldType.Byte, unit.Fields[0].Type);

                Assert.Equal("ClassID", unit.Fields[1].Name);
                Assert.Equal(2u, unit.Fields[1].Offset);
                Assert.Equal(StructMetadata.FieldType.Word, unit.Fields[1].Type);

                Assert.Equal("Level", unit.Fields[2].Name);
                Assert.Equal(4u, unit.Fields[2].Offset);
            }
            finally
            {
                File.Delete(tmpFile);
            }
        }

        [Fact]
        public void GetStruct_ReturnsNull_WhenNotLoaded()
        {
            var meta = new StructMetadata();
            Assert.Null(meta.GetStruct("NonExistent"));
        }

        [Fact]
        public void LoadFromFile_SkipsComments()
        {
            string tmpFile = Path.GetTempFileName();
            try
            {
                File.WriteAllText(tmpFile, @"// Comment line
@TestStruct 16
// Another comment
0	B	Field1	test
");

                var meta = new StructMetadata();
                meta.LoadFromFile(tmpFile);

                var s = meta.GetStruct("TestStruct");
                Assert.NotNull(s);
                Assert.Single(s.Fields);
            }
            finally
            {
                File.Delete(tmpFile);
            }
        }

        [Fact]
        public void LoadFromFile_HandlesPointerType()
        {
            string tmpFile = Path.GetTempFileName();
            try
            {
                File.WriteAllText(tmpFile, @"@PtrStruct 8
0	P	DataPointer	Pointer to data
4	D	Value	Some value
");

                var meta = new StructMetadata();
                meta.LoadFromFile(tmpFile);

                var s = meta.GetStruct("PtrStruct");
                Assert.NotNull(s);
                Assert.Equal(2, s.Fields.Count);
                Assert.Equal(StructMetadata.FieldType.Pointer, s.Fields[0].Type);
                Assert.Equal(4, s.Fields[0].Size);
                Assert.Equal(StructMetadata.FieldType.DWord, s.Fields[1].Type);
            }
            finally
            {
                File.Delete(tmpFile);
            }
        }

        [Fact]
        public void LoadFromFile_NonExistentFile_DoesNotThrow()
        {
            var meta = new StructMetadata();
            meta.LoadFromFile("/nonexistent/path.txt");
            Assert.Empty(meta.AllStructs);
        }
    }
}
