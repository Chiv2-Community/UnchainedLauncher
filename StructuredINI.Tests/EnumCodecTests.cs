namespace StructuredINI.Tests {
    public class EnumCodecTests {
        [DeriveCodec] 
        private enum TestEnum {
            Foo,
            BarBaz
        }

        [Fact]
        public void EnumCodec_Encodes_UsingEnumName() {
            var codec = CodecRegistry.Get<TestEnum>();
            Assert.Equal("BarBaz", codec.Encode(TestEnum.BarBaz));
        }

        [Fact]
        public void EnumCodec_Decodes_CaseInsensitive() {
            var codec = CodecRegistry.Get<TestEnum>();
            Assert.Equal(TestEnum.Foo, codec.Decode("foo"));
        }

        [Fact]
        public void EnumCodec_InvalidValue_ThrowsHelpfulError() {
            var codec = CodecRegistry.Get<TestEnum>();

            var ex = Assert.Throws<InvalidOperationException>(() => codec.Decode("nope"));
            Assert.Contains("nope", ex.Message);
            Assert.Contains(nameof(TestEnum), ex.Message);
        }
    }
}
