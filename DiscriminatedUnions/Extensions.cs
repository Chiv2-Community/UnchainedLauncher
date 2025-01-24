using System.Buffers;
using System.Text.Json;

namespace DiscriminatedUnions.Extensions {
    // Shamelessly copied from https://gist.github.com/shadeglare/6b46baa340346e575b2751475733405c#file-complete-cs
    public static class TypeExtensions
    {
        public static Func<Type, Type> CreateConcreteTypeFactory(this Type type)
        {
            if (type.IsGenericType)
            {
                var genericArgs = type.GetGenericArguments();
                return givenType => givenType.MakeGenericType(genericArgs);
            }
            else
            {
                return givenType => givenType;
            }
        }
    }

    public static class JsonExtensions
    {
        public static Object? ToObject(this JsonElement element, Type type, JsonSerializerOptions options)
        {
            var bufferWriter = new ArrayBufferWriter<Byte>();
            using (var writer = new Utf8JsonWriter(bufferWriter))
            {
                element.WriteTo(writer);
            }
            return JsonSerializer.Deserialize(bufferWriter.WrittenSpan, type, options);
        }

        public static Object? ToObject(this JsonDocument document, Type type, JsonSerializerOptions options)
        {
            if (document is null) throw new ArgumentNullException(nameof(document));
            return document.RootElement.ToObject(type, options);
        }
    }
}