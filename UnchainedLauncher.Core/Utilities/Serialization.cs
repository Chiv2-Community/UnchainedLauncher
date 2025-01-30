using LanguageExt;
using static LanguageExt.Prelude;


namespace UnchainedLauncher.Core.Utilities {

    // N.B. The serializers here seem like duplicated effort from System.Text.Json, but they represent more concretely
    //      typed serializers that we can compose in more effective ways.

    /// <summary>
    /// Represents a typed serializer for an arbitrary format. Usually json.
    /// </summary>
    /// <typeparam name="T"></typeparam>
    public interface ISerializer<in T> {
        string Serialize(T obj);
    }

    /// <summary>
    /// Represents a typed deserializer for an arbitrary format. Usually json.
    /// </summary>
    /// <typeparam name="T"></typeparam>
    public interface IDeserializer<T> {
        DeserializationResult<T> Deserialize(string json);
    }

    /// <summary>
    /// Represents a typed deserializer for an arbitrary format. Usually json.
    /// </summary>
    /// <typeparam name="T"></typeparam>
    public interface ICodec<T> : IDeserializer<T>, ISerializer<T> { }

    public static class SerializerExtensions {
        public static void SerializeFile<T>(this ISerializer<T> serializer, string path, T obj) {
            if (!Directory.Exists(Path.GetDirectoryName(path)))
                Directory.CreateDirectory(Path.GetDirectoryName(path));

            File.WriteAllText(path, serializer.Serialize(obj));
        }

        public static Option<DeserializationResult<T>> DeserializeFile<T>(this IDeserializer<T> deserializer, string path) {
            if (!File.Exists(path)) return None;

            var fileContents = File.ReadAllText(path);
            return Some(deserializer.Deserialize(fileContents));
        }

        public static IDeserializer<T2> Map<T, T2>(this IDeserializer<T> deserializer, Func<T, T2> f) =>
            new Deserializer<T2>(json => deserializer.Deserialize(json).Select(f));

        public static ISerializer<T2> Contramap<T, T2>(this ISerializer<T> serializer, Func<T2, T> f) =>
            new Serializer<T2>(t2 => serializer.Serialize(f(t2)));

        public static Codec<T2> InvariantMap<T, T2>(this Codec<T> codec, Func<T, T2> to, Func<T2, T> from) =>
            new Codec<T2>(
                codec.Contramap(from),
                codec.Map(to)
            );
    }


    public class Deserializer<T> : IDeserializer<T> {
        public readonly Func<string, DeserializationResult<T>> _deserialize;

        public Deserializer(Func<string, DeserializationResult<T>> deserialize) {
            _deserialize = deserialize;
        }

        public DeserializationResult<T> Deserialize(string json) => _deserialize(json);

        public Option<DeserializationResult<T>> DeserializeFile(string path) {
            if (!File.Exists(path)) return None;

            var fileContents = File.ReadAllText(path);
            return Some(Deserialize(fileContents));
        }

    }

    public class Serializer<T> : ISerializer<T> {
        public readonly Func<T, string> _serialize;

        public Serializer(Func<T, string> serialize) => _serialize = serialize;

        public string Serialize(T obj) => _serialize(obj);
    }


    /// <summary>
    /// Given a serializer and deserializaer, constructs a Codec which can handle serialization and deserialization.
    /// This is largely for convenience, for situations where you'll need to do both
    /// </summary>
    public class Codec<T> : ICodec<T> {
        protected readonly ISerializer<T> _serializer;
        protected readonly IDeserializer<T> _deserializer;

        public Codec(ISerializer<T> serializer, IDeserializer<T> deserializer) {
            _deserializer = deserializer;
            _serializer = serializer;
        }

        public string Serialize(T obj) => _serializer.Serialize(obj);
        public DeserializationResult<T> Deserialize(string json) => _deserializer.Deserialize(json);
    }

    /// <summary>
    /// Derives a new codec from a map/contramap function and an underlying codec.
    /// This is kind of ugly, but C# doesn't provide a much nicer way to do this.
    /// </summary>
    /// <typeparam name="TJson"></typeparam>
    /// <typeparam name="T"></typeparam>
    public class DerivedCodec<TSerializable, T> : Codec<T> {
        public DerivedCodec(ICodec<TSerializable> codec, Func<T, TSerializable> contramap, Func<TSerializable, T> map) :
            base(codec.Contramap(contramap), codec.Map(map)) { }
    }
}