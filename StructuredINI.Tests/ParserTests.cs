using System;
using System.Linq;
using Xunit;
using StructuredINI;

namespace StructuredINI.Tests
{
    public class ParserTests
    {
        [INISection("SomeINISectionName")]
        public record SomeINISection(int Foo, string Bar, double Baz, int[] MyConfigArray);

        [Fact]
        public void TestArrayOperations()
        {
            var parser = new StructuredINIParser();
            var iniContent = @"
[SomeINISectionName]
MyConfigArray=7
!MyConfigArray=ClearArray
+MyConfigArray=2
+MyConfigArray=3
+MyConfigArray=4
.MyConfigArray=2
-MyConfigArray=4
";
            var result = parser.Deserialize<SomeINISection>(iniContent);

            Assert.NotNull(result);
            Assert.NotNull(result.MyConfigArray);
            
            // Expected: [2, 3, 2]
            Assert.Equal(3, result.MyConfigArray.Length);
            Assert.Equal(2, result.MyConfigArray[0]);
            Assert.Equal(3, result.MyConfigArray[1]);
            Assert.Equal(2, result.MyConfigArray[2]);
        }

        [Fact]
        public void TestScalars()
        {
            var parser = new StructuredINIParser();
            var iniContent = @"
[SomeINISectionName]
Foo=42
Bar=Hello World
Baz=3.14
";
            var result = parser.Deserialize<SomeINISection>(iniContent);

            Assert.Equal(42, result.Foo);
            Assert.Equal("Hello World", result.Bar);
            Assert.Equal(3.14, result.Baz);
        }

        [Fact]
        public void TestSerialization()
        {
            var parser = new StructuredINIParser();
            var instance = new SomeINISection(42, "Hello", 3.14, new[] { 2, 3, 2 });
            
            var ini = parser.Serialize(instance);
            
            // Normalize line endings
            var lines = ini.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries)
                           .Select(l => l.Trim())
                           .ToArray();

            Assert.Contains("[SomeINISectionName]", lines);
            Assert.Contains("Foo=42", lines);
            Assert.Contains("Bar=Hello", lines);
            Assert.Contains("Baz=3.14", lines);
            
            // Array verification
            // Expected order:
            // MyConfigArray=2
            // +MyConfigArray=3
            // .MyConfigArray=2
            
            int idx1 = Array.IndexOf(lines, "MyConfigArray=2");
            Assert.True(idx1 >= 0, "MyConfigArray=2 not found");
            
            int idx2 = Array.IndexOf(lines, "+MyConfigArray=3");
            Assert.True(idx2 >= 0, "+MyConfigArray=3 not found");
            
            int idx3 = Array.IndexOf(lines, ".MyConfigArray=2");
            Assert.True(idx3 >= 0, ".MyConfigArray=2 not found");
            
            Assert.True(idx1 < idx2, "Order wrong: 2 before 3");
            Assert.True(idx2 < idx3, "Order wrong: 3 before duplicate 2");
        }
    }
}
