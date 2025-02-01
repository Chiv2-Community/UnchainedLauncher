using FluentAssertions;
using UnchainedLauncher.Core.Utilities;
using Xunit.Abstractions;

namespace UnchainedLauncher.Core.Tests.Unit.Utilities {
    public abstract class CodecTestBase<TAbstract> {
        protected readonly Codec<TAbstract> Codec;

        private readonly ITestOutputHelper _testOutputHelper;

        protected CodecTestBase(Codec<TAbstract> codec, ITestOutputHelper testOutputHelper) {
            Codec = codec;
            _testOutputHelper = testOutputHelper;
        }

        public void VerifyCodecRoundtrip<TSpecific>(TSpecific originalObject, params Action<TSpecific>[] assertions)
            where TSpecific : TAbstract {
            _testOutputHelper.WriteLine($"Original object: {originalObject}");
            var json = Codec.Serialize(originalObject);
            _testOutputHelper.WriteLine($"Serialized json: {json}");

            var deserialized = Codec.Deserialize(json);

            _testOutputHelper.WriteLine($"Deserialized result: {deserialized.Result}");

            deserialized.Result.Should().NotBeNull();
            deserialized.Result.Should().BeOfType<TSpecific>();

            var specificObject = (TSpecific)deserialized.Result;
            specificObject.Should().BeOfType<TSpecific>();
            assertions.ToList().ForEach(assertion => assertion(specificObject));
        }
    }
}