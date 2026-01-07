using Xunit;
using StructuredINI;

namespace StructuredINI.Tests
{
    public class MultipleEqualsTests
    {
        [INISection("MultipleEquals")]
        public record ConfigWithEquals(string ConnectionString);

        [Fact]
        public void CanParseValueWithEquals()
        {
            var parser = new StructuredINIParser();
            var ini = @"
[MultipleEquals]
ConnectionString=Server=myServerAddress;Database=myDataBase;
";
            var result = parser.Deserialize<ConfigWithEquals>(ini);
            
            Assert.Equal("Server=myServerAddress;Database=myDataBase;", result.ConnectionString);
        }

        [Fact]
        public void CanSerializeValueWithEquals()
        {
            var parser = new StructuredINIParser();
            var config = new ConfigWithEquals("Server=myServerAddress;Database=myDataBase;");
            
            var ini = parser.Serialize(config);
            
            Assert.Contains("ConnectionString=Server=myServerAddress;Database=myDataBase;", ini);
        }
    }
}
