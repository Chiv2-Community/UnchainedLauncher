namespace StructuredINI.Codecs;

[DeriveCodec]
public record AutoBalance(int MinNumPlayers, int MaxNumPlayers, int AllowedNumPlayersDifference);