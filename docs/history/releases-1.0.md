# Mercy 1.0.x

## 1.0.0 (2026-11-22)

First GitHub release (tag `v1.0.0`, `Mercy-1.0.0.zip`). The code is 0.31.8 with the version
bumped; nothing else changed.

Raid that cleared it (0.31.8, COMET 1.0.0 installed; log copied to that session's scratchpad as
`mercy-0.31.8-raid-LogOutput.log`):

- 106 surrender rolls, 8 natural surrenders (all Scavs), 3 bot custodies, 1 hold-up broken by fire.
- Drops 0.6-1.2 s to the gun on the ground (Mosin bolt cycle the slowest), arms up within 1.5 s.
- One completed captor walk-up (Rena-chan, 16 m -> 8.5 m, no stall); one cancelled by fire.
- Openings: a scramble for the gun at his feet while an unaware captor reloaded; an unclaimed
  man picking his gun back up; a released man fetching his gun 7 m away once out of sight.
- 0 Mercy/COMET warnings, 0 RESTRAINT LEAK. One 14.4 ms Mercy frame on the first surrender
  (Natural 7.6, Layer 3.7); the other seven didn't spike. Watch it in the next Profile log.
- Other mods in the log: acidphantasm Bot Placement System `TryToSpawnInZonePatch`
  ArgumentOutOfRangeException (repeated), SAIN BotGrenadeManager Dispose NRE at raid end.

Still unseen in a raid: the 0.31.8 walk-up `stuck (...)` detail and the gun-fetch detour/door paths.
