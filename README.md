# DMU P4 Debuff Helper

DMU P4 Debuff Helper is a Dalamud plugin for watching your debuffs during P4 of Dancing Mad Ultimate.

It is DMU-only, watches your P4 debuffs, and reads the boss tell status used to tag those debuffs as real, fake, or unknown. The helper window shows compact icon-based debuff timing with details on hover, while Buff Summary records what the helper saw for troubleshooting after a wipe. Debug chat can print the raw boss tell params alongside each interpreted assignment so the mapping can be verified from live pulls.

## Current Tracking

- Grand Cross: Compressed Water, Forked Lightning, Cursed Shriek, Acceleration Bomb.
- Chaos: Dynamic Fluid, Entropy.
- Flood: Black Wound, White Wound, Allagan Field, Beyond Death.

## Commands

Open settings:

```text
/dmup4
```

Open the helper window:

```text
/dmup4h
/dmup4helper
```

## Dalamud Repository

Add this custom plugin repository URL in Dalamud:

```text
https://puni.sh/api/repository/nainai
```

Then install `DMU P4 Debuff Helper` from Dalamud's plugin installer.
