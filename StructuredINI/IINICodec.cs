namespace StructuredINI {
    public interface IINICodec<T> {
        T Decode(string value);
        string Encode(T value);
    }
}