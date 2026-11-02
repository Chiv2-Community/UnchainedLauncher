namespace StructuredINI.Tests {
    public class ParserTests {
        [INISection("SomeINISectionName")]
        public record SomeINISection(int Foo, string Bar, double Baz, float Quux, int[] MyConfigArray);

        [INISection("MissingCodecSection")]
        public record MissingCodecSection(DateTime When);

        [Fact]
        public void TestArrayOperations() {
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
        public void TestScalars() {
            var parser = new StructuredINIParser();
            var iniContent = @"
[SomeINISectionName]
Foo=42
Bar=Hello World
Baz=3.14
Quux=60.5
";
            var result = parser.Deserialize<SomeINISection>(iniContent);

            Assert.Equal(42, result.Foo);
            Assert.Equal("Hello World", result.Bar);
            Assert.Equal(3.14, result.Baz);
            Assert.Equal(60.5f, result.Quux);
        }

        [Fact]
        public void TestSerialization() {
            var parser = new StructuredINIParser();
            var instance = new SomeINISection(42, "Hello", 3.14, 60.5f, new[] { 2, 3, 2 });

            var ini = parser.Serialize(instance, isConfigRoot: false);

            // Normalize line endings
            var lines = ini.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries)
                           .Select(l => l.Trim())
                           .ToArray();

            Assert.Contains("[SomeINISectionName]", lines);
            Assert.Contains("Foo=42", lines);
            Assert.Contains("Bar=Hello", lines);
            Assert.Contains("Baz=3.14", lines);
            Assert.Contains("Quux=60.5", lines);

            Assert.Equal(new[] { "!MyConfigArray=ClearArray", "+MyConfigArray=2", "+MyConfigArray=3", ".MyConfigArray=2" },
                lines.Where(line => line.Contains("MyConfigArray=")));
        }

        [Fact]
        public void MissingCodecErrorIncludesKeyAndFriendlyTypeName() {
            var parser = new StructuredINIParser();
            var iniContent = @"
[MissingCodecSection]
When=2026-01-09T08:24:00
";

            var ex = Assert.Throws<InvalidOperationException>(() => parser.Deserialize<MissingCodecSection>(iniContent));
            Assert.Contains("When", ex.Message);
            Assert.Contains("DateTime", ex.Message);
            Assert.DoesNotContain("System.DateTime", ex.Message);
        }
    }
}
