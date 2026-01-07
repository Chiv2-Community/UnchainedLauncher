namespace StructuredINI.Codecs;

public record CallbackList(string[] Callbacks);
public class CallbackListCodec : IINICodec<CallbackList> {
    public CallbackList Decode(string value) =>
        value.StartsWith("(") && value.EndsWith(")")
            ? new CallbackList(value.Split(",").Select(x => x.Trim()).ToArray())
            : throw new Exception("Invalid CallbackList. Expected comma separated list of strings wrapped in parens.");

    public string Encode(CallbackList value) =>
        value.Callbacks.Length == 0 ? "()" : $"({string.Join(", ", value.Callbacks)})";
}