using StructuredINI.Codecs;

namespace StructuredINI {
    public static class CodecRegistry {
        private static readonly Dictionary<Type, object> _codecs = new();

        static CodecRegistry() {
            Register(new StringCodec());
            Register(new IntCodec());
            Register(new DoubleCodec());
        }

        public static void Register<T>(IINICodec<T> codec) {
            _codecs[typeof(T)] = codec;
        }

        public static void Register(Type type, object codec) {
            _codecs[type] = codec;
        }

        public static IINICodec<T> Get<T>() {
            return (IINICodec<T>)Get(typeof(T));
        }

        public static object Get(Type type) {
            if (_codecs.TryGetValue(type, out var codec)) {
                return codec;
            }

            if (TryDeriveAndRegisterCodec(type, out codec)) {
                return codec;
            }

            throw new KeyNotFoundException($"No codec registered for type {type.Name}");
        }

        private static bool TryDeriveAndRegisterCodec(Type type, out object codec) {
            codec = null;

            if (!type.IsDefined(typeof(DeriveCodecAttribute), inherit: false)) {
                return false;
            }

            var codecType = typeof(DerivedRecordCodec<>).MakeGenericType(type);
            codec = Activator.CreateInstance(codecType);
            if (codec == null) {
                return false;
            }

            Register(type, codec);
            return true;
        }
    }
}