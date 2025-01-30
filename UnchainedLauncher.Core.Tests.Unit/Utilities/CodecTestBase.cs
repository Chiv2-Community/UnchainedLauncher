using FluentAssertions;
using UnchainedLauncher.Core.Utilities;

public abstract class CodecTestBase<TAbstract> {
    protected readonly Codec<TAbstract> Codec;

    protected CodecTestBase(Codec<TAbstract> codec) {
        Codec = codec;
    }

    public void VerifyCodecRoundtrip<TSpecific>(TSpecific originalObject, params Action<TSpecific>[] assertions)
        where TSpecific : TAbstract {
        var json = Codec.Serialize(originalObject);
        var deserialized = Codec.Deserialize(json);

        deserialized.Result.Should().NotBeNull();
        deserialized.Result.Should().BeOfType<TSpecific>();

        var specificObject = (TSpecific)deserialized.Result;
        specificObject.Should().BeOfType<TSpecific>();
        assertions.ToList().ForEach(assertion => assertion(specificObject));
    }
}