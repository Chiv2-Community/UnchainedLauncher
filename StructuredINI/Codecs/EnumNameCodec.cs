namespace StructuredINI.Codecs;

public sealed class EnumNameCodec<TEnum> : IINICodec<TEnum> where TEnum : struct, Enum {
    public TEnum Decode(string value) {
        if (Enum.TryParse<TEnum>(value, ignoreCase: true, out var result)) {
            return result;
        }

        throw new InvalidOperationException($"Failed to decode '{value}' as {typeof(TEnum).Name}.");
    }

    public string Encode(TEnum value) {
        return value.ToString();
    }
}
