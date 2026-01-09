using StructuredINI.Codecs;

namespace StructuredINI.Tests {
    public class AutoBalanceTests {
        [Fact]
        public void TestAutoBalanceDecoding() {
            var codec = CodecRegistry.Get<AutoBalance>();
            string input = "(MinNumPlayers=0,MaxNumPlayers=21,AllowedNumPlayersDifference=20)";
            var result = codec.Decode(input);

            Assert.NotNull(result);
            Assert.Equal(0, result.MinNumPlayers);
            Assert.Equal(21, result.MaxNumPlayers);
            Assert.Equal(20, result.AllowedNumPlayersDifference);
        }

        [Fact]
        public void TestAutoBalanceEncoding() {
            var codec = CodecRegistry.Get<AutoBalance>();
            var input = new AutoBalance(0, 21, 20);
            string result = codec.Encode(input);

            Assert.Equal("(MinNumPlayers=0,MaxNumPlayers=21,AllowedNumPlayersDifference=20)", result);
        }

        [DeriveCodec]
        private record DefaultsRecord(int Foo = 5, string? Bar = null);

        [Fact]
        public void MissingValues_UseConstructorDefaults() {
            var codec = CodecRegistry.Get<DefaultsRecord>();
            var decoded = codec.Decode("()");
            Assert.Equal(5, decoded.Foo);
            Assert.Null(decoded.Bar);
        }

        [DeriveCodec]
        private record NullableNoDefault(string? Maybe);

        [Fact]
        public void MissingNullableNoDefault_ResolvesToNull() {
            var codec = CodecRegistry.Get<NullableNoDefault>();
            var decoded = codec.Decode("()");
            Assert.Null(decoded.Maybe);
        }

        [Fact]
        public void ExplicitNone_DecodesToNull_ForNullable() {
            var codec = CodecRegistry.Get<NullableNoDefault>();
            var decoded = codec.Decode("(Maybe=None)");
            Assert.Null(decoded.Maybe);
        }
    }
}