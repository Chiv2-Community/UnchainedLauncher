# Inline map with a different preset map

The `wardenglade` preset selects `FFA_Wardenglade` and supplies INI settings. Two rotation slots override its map with `FFA_Courtyard`.

Map rules must match the final map, not the preset's original map. Exact map rules beat `FFA_~` even though the wildcard is declared last. Preset INI remains active; inline INI takes precedence.

Generated conditional files:

```text
unchained.yaml
ini-overrides/
  maps/FFA_~/Game.ini
  maps/FFA_Courtyard/Game.ini
  maps/FFA_Wardenglade/Game.ini
  profiles/wardenglade/Game.ini
  profiles/wardenglade.slot-1/Game.ini
  profiles/wardenglade.slot-4/Game.ini
  indices/0/Game.ini
```

The three preset files contain identical preset-only settings; their references are distinct because the plugin requires unique profile names. Only `indices/0/Game.ini` contains the inline match-wait override. Baseline settings remain outside this tree in the server's saved `Game.ini`.

| Slot | Final map | MaxPlayers | LowBots | HighBots | Spectate | MaxWait |
| --- | --- | --- | --- | --- | --- | --- |
| 0 | FFA_Courtyard | 16 | 8 | 6 | 180 | 35 |
| 1 | FFA_Wardenglade | 12 | 8 | 9 | 99 | 25 |
| 2 | FFA_Courtyard | 16 | 2 | 6 | 180 | 15 |
| 3 | TDM_Wardenglade | 24 | 2 | 2 | 180 | 15 |
| 4 | FFA_Courtyard | 16 | 8 | 6 | 180 | 25 |
| 5 | FFA_Bazaar | 20 | 2 | 4 | 180 | 15 |

`LowBots` and `HighBots` denote `BotBackfillLowBots` and `BotBackfillHighBots`; `Spectate` denotes `IdleKickTimerSpectate`; `MaxWait` denotes `MaxTimeBeforeStartingMatch`.

`SettingsResolutionTests.NativeHierarchyPreservesLayerBoundariesAndResolvesAllSixRotationSlots` checks separate map, profile, and index files, absence of baseline copies, and all six compositions using generated root metadata and a test-only model of the plugin's folder order. It does not prove live GameMode imports or restoration across travel.
