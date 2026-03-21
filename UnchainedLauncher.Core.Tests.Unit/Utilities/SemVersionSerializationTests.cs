using FluentAssertions;
using Semver;
using System.Text.Json.Serialization;
using UnchainedLauncher.Core.Utilities;

namespace UnchainedLauncher.Core.Tests.Unit.Utilities {
    public class SemVersionSerializationTests {
        public record TestModel(
            [property: JsonPropertyName("version")] SemVersion Version
        );

        [Fact]
        public void Should_Deserialize_SemVersion() {
            var json = "{\"version\": \"1.2.3-alpha.1+metadata\"}";
            var result = JsonHelpers.Deserialize<TestModel>(json);

            result.Exception.Should().BeNull();
            result.Success.Should().BeTrue();
            result.Result.Should().NotBeNull();
            result.Result!.Version.ToString().Should().Be("1.2.3-alpha.1+metadata");
        }

        [Fact]
        public void Should_Serialize_SemVersion() {
            var model = new TestModel(SemVersion.Parse("1.2.3-alpha.1+metadata", SemVersionStyles.Any));
            var json = JsonHelpers.Serialize(model);

            json.Should().Contain("\"version\": \"1.2.3-alpha.1+metadata\"");
        }
    }
}