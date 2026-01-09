namespace StructuredINI.Tests {
    public class KeyAnnotationTests {
        [INISection("RenamedSection")]
        public record RenamedProps(
            [property: INIKey("RealName")] int Foo,
            [property: INIKey("AnotherName")] string Bar
        );

        [Fact]
        public void TestSerialization_RenamedKeys() {
            var parser = new StructuredINIParser();
            var instance = new RenamedProps(42, "hello");
            var ini = parser.Serialize(instance);

            Assert.Contains("RealName=42", ini);
            Assert.Contains("AnotherName=hello", ini);
            Assert.DoesNotContain("Foo=", ini);
            Assert.DoesNotContain("Bar=", ini);
        }

        [Fact]
        public void TestDeserialization_RenamedKeys() {
            var parser = new StructuredINIParser();
            var ini = @"
[RenamedSection]
RealName=99
AnotherName=world
";
            var result = parser.Deserialize<RenamedProps>(ini);

            Assert.Equal(99, result.Foo);
            Assert.Equal("world", result.Bar);
        }
    }
}