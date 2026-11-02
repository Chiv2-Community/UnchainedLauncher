namespace StructuredINI.Tests {
    public class MultipleEqualsTests {
        [INISection("MultipleEquals")]
        public record ConfigWithEquals(string ConnectionString);

        [Fact]
        public void CanParseAndSerializeValuesWithEquals() {
            var parser = new StructuredINIParser();
            var ini = @"
[MultipleEquals]
ConnectionString=Server=myServerAddress;Database=myDataBase;
";
            var result = parser.Deserialize<ConfigWithEquals>(ini);

            Assert.Equal("Server=myServerAddress;Database=myDataBase;", result.ConnectionString);
            var config = new ConfigWithEquals("Server=myServerAddress;Database=myDataBase;");

            var serialized = parser.Serialize(config, isConfigRoot: false);

            Assert.Contains("ConnectionString=Server=myServerAddress;Database=myDataBase;", serialized);
        }
    }
}
