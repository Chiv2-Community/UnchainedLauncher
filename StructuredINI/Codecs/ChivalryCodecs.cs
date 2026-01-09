namespace StructuredINI.Codecs;

[DeriveCodec]
public record AutoBalance(int MinNumPlayers, int MaxNumPlayers, int AllowedNumPlayersDifference);

[DeriveCodec]
public enum CharacterClass {
    Archer,
    Vanguard,
    Footman,
    Knight,

    Longbowman,
    Crossbowman,
    Skirmisher,

    Devastator,
    Raider,
    Ambusher,

    Poleman,
    ManAtArms,
    Engineer,

    Officer,
    Guardian,
    Crusader
}

[DeriveCodec]
public record ClassLimit(CharacterClass Class, decimal ClassLimitPercent);