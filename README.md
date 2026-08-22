[**English**](README.md) · [Русский — описание, скачивание и установка](README.ru.md)

# P.L.A.T.E. — Realistic Combat, Ballistics, Armor and Trauma Mod for SPT

**P.L.A.T.E. (Penetration, Lethality, Armor & Trauma Engine)** is a gameplay
overhaul mod for **SPT (Single Player Tarkov)**. 

It replaces the game's damage, armor penetration, wound, fracture, bleeding and
blood-loss rules with one physics-driven combat model.

Projectile construction, velocity, barrel length, range, angle of impact,
armor material and the actual path through the body all matter. If the rest of
your SPT mod setup handles AI, spawns, progression, quests, loot or graphics,
P.L.A.T.E. handles what happens when the shooting starts.

**Current release:** P.L.A.T.E. 0.11.0

**Supported game:** SPT 4.0.13 / EFT 0.16.9.40087

[Download the latest release](https://github.com/Anamelash/spt-plate/releases/latest) ·
[Discord — questions, feedback and ideas](https://discord.gg/w2DpURxtrf) ·
[All versions](https://github.com/Anamelash/spt-plate/releases) ·
[Installation](#installation) ·
[Physical model](docs/MODEL.md) ·
[Changes from vanilla](CHANGELOG.md) ·
[Report a problem](#bugs-balance-and-feedback)

> **Official download and support.** This GitHub repository is the canonical
> home of P.L.A.T.E. Releases, documentation and issue tracking live here. For
> questions, feedback or ideas for improving the mod, join the
> **[P.L.A.T.E. Discord](https://discord.gg/w2DpURxtrf)**. Forge, its Discord and
> third-party forks of that platform are not maintained support channels for
> this mod.

## Download P.L.A.T.E. for SPT

Prebuilt packages are published under
**[GitHub Releases](https://github.com/Anamelash/spt-plate/releases)**. This is
the SPT 4.0.13 line: 0.11.0 is the newest build on it, and it carries everything
the 1.x releases carry. On SPT 4.1.x take a 1.x release instead — the two lines
are the same mod against two server APIs, not an old version and a new one.

Do not mix releases. The client plugin and server mod work as a pair and are
tied to the SPT and EFT APIs they were built against. Two matching halves or no
deal — Tapkov already has enough creative failure modes of its own.

## Where P.L.A.T.E. Fits in an SPT Mod Setup

P.L.A.T.E. is a focused combat-system overhaul, not a general modpack. It can
provide the ballistics and trauma layer in a larger SPT mod list while other
mods handle the rest of the game.

| Mod category | P.L.A.T.E.'s role |
|---|---|
| AI behavior and bot spawning | Outside its scope |
| Quests, traders, progression and economy | Outside its scope |
| Loot, maps, graphics and general quality of life | Outside its scope |
| Additional weapons, ammunition and armor | Reads their physical properties when usable data is present |
| Damage and terminal ballistics | Replaces the vanilla system |
| Armor penetration, blunt trauma and wear | Replaces the vanilla system |
| Wounds, fractures, bleeding and blood loss | Replaces the vanilla system |

Mods that touch unrelated systems do not conflict by definition. Mods that
replace ballistics, armor or medical mechanics are incompatible because they
are trying to own the same hit. Specific combinations cannot all be guaranteed;
Fika support is currently untested.

## What P.L.A.T.E. Changes

### Damage Is Computed at Impact

The Damage number on an ammunition card stops being a promise. P.L.A.T.E.
computes the result from the projectile that actually arrived — its mass,
calibre, velocity, construction and orientation — and from what it actually
hit.

Distance and barrel length change delivered velocity. A graze is not a
center-mass hit. Bone can stop a projectile, a rifle bullet can turn broadside
or break up, and a round that exits an arm can continue into the thorax with
whatever it has left. The detailed derivation is in the
[wound-channel model](docs/MODEL.md#wound-channel).

### Armor Is a Physical Barrier

A plate is treated as a thickness of steel, titanium, ceramic, UHMWPE, aramid
or another recorded construction. Penetration is decided by whether that
material can stop that projectile core at that velocity and angle, not by a
single Penetration value rolling against armor class.

The core may remain rigid, crush on the armor face or crack against a material
hard enough to break it. A penetrating projectile pays for the hole in energy,
mass and shape. A blocked one can still deliver behind-armor blunt trauma.
Read the [armor model](docs/MODEL.md#armor) and
[blunt-trauma model](docs/MODEL.md#behind-armor-blunt-trauma) for the numbers.

### Armor Wear Is Local and Material-Specific

A worn plate is intact where nothing hit it and damaged where something did.
Repeat strikes into remembered impact areas meet the damage already there;
hits elsewhere meet the plate that remains.

Steel and titanium can shrug off shallow impacts without becoming disposable.
Aramid and UHMWPE mainly pay when penetrated. Ceramic pays for serious stops
because ceramic really does turn into yesterday's construction material. One
durability curve no longer pretends those are the same thing.

### The Body Has an Inside

Wounds follow the projectile's path through tissue, bones and vital zones. The
heart and great vessels, liver, spinal cord, brain, jaw and neck are resolved
from the target's anatomy at the moment of the hit.

A jaw hit is ugly without automatically becoming a brain hit. A channel through
the heart or cord does not care how green the remaining limb bars look. Where
the projectile turns, what tissue it crosses and which vessels it cuts decide
the wound and its bleeding.

### Blood Is a Separate Resource

Bleedings drain blood volume rather than periodically nibbling limb HP. Blood
loss moves the character through stages based on hemorrhagic shock: stamina
recovery degrades, tremor and visual effects arrive, sprinting and jumping go
away, and enough loss ends in collapse and death.

External wounds can be treated by the appropriate field medicine. Internal
bleeding may not be reachable with a tourniquet. Blood also persists between
raids and recovers gradually; Therapist sells Blood Transfusion Kits, which can
also be crafted at the Hideout Medstation. Check whether you are leaking before
vacuuming six magazines of warm PRS off a body.

A heavy blow to the torso — stopped by armor or not — also knocks the wind out.
Stamina drops in proportion to the energy the body received and refuses to
recover for several seconds: after a blocked 12-gauge slug nobody sprints
anywhere, you and bots alike. Optionally, a full-force blow can leave a bot
disoriented — backing away and firing blind around where it last saw the
shooter (off by default).

The full system is documented under
[blood and trauma](docs/MODEL.md#blood-and-trauma).

### Fractures, Ammunition, Shotguns and Grenades

- Fracture chance follows the limb segment actually struck. Blacked limbs can
  break, surgical kits can set the bone they operate around, and bots with a
  broken leg stay down instead of performing tactical slapstick.
- Ammunition construction and physical properties are normalized against the
  prototypes recorded in the reference book. Barrel length changes muzzle
  velocity rather than serving as furniture trivia.
- Buckshot uses realistic pellet counts. Flechettes behave as narrow steel
  penetrators rather than unusually angry buckshot.
- Grenade fragments remain dangerous beyond vanilla's short boundaries. Soft
  armor catches the small pieces; the large ones can still cancel the raid.

### Configurable Lethality and HUD

The F12 menu exposes separate damage, bleeding and survivability controls for
the player, PMC bots and Savage-side NPCs. Player assists can prevent direct
hit death, disable critical-organ lethality or reduce bleeding chance without
silently changing the model for everyone else.

The blood panel can show volume in millilitres or percent, ATLS tier and current
loss rate. Position, scale, color and an optional time estimate are configurable.

## What It Changes in a Raid

- Cheap flesh ammunition remains vicious against an unarmored target and a bad
  frontal plan against a rifle plate.
- Hardened AP is built to defeat armor, though a narrow penetrator does not
  necessarily cut the broadest wound.
- Short barrels and long distances can strip the magic out of a light, fast
  rifle bullet.
- A stopped round can leave the wearer bruised, concussed, winded or worse
  without granting the projectile a penetration it did not earn.
- An enemy who breaks contact may be flanking. He may also be emptying his
  tactical fluid behind a bush. The same rules apply to you.

Tarkov still Tarkovs you. Now physics helps.

## Research, Calibration and Limits

The model is anchored to published work rather than tuned only by feel:

- Wound ballistics and ordnance-gelatin data for the wound channel.
- GOST and NIJ standards, armor certificates and material data for protection.
- The Blunt Criterion literature for behind-armor trauma.
- ATLS hemorrhagic-shock classification for the blood system.
- Published penetration ladders and multi-hit trials for calibration.

Every formula, constant, calibration anchor and source is collected in
**[The P.L.A.T.E. Physical Model](docs/MODEL.md)**, including
[what is deliberately not modelled](docs/MODEL.md#what-is-deliberately-not-modelled).
If the mod disagrees with that document, it is a bug, not secret balance lore.

For the complete player-facing comparison, see
**[P.L.A.T.E. — Changes vs Vanilla](CHANGELOG.md)**.

## Requirements and Mod Compatibility

| Component | Required version |
|---|---|
| P.L.A.T.E. | 0.11.0 |
| SPT | 4.0.13 |
| EFT client | 0.16.9.40087 |
| .NET SDK | 9, only when building from source |

Both components are required:

- `PLATE.Client.dll`, the BepInEx client plugin.
- `PLATE.Server.dll` and `bundles/`, the SPT server mod.

P.L.A.T.E. releases follow the SPT compatibility split below:

| P.L.A.T.E. version | Compatible SPT version |
|---|---|
| 0.x, this line | SPT 4.0.13 (`~4.0.0`) |
| 1.0.0 to 1.3.0 | SPT 4.1.0 to 4.1.2 |
| 1.3.1 and newer | SPT 4.1.x (`~4.1.0`), including the current 4.1.3 |

The number is the server API, not the amount of mod behind it: 0.11.0 and 1.3.2
model the same physics and ship the same features. Neither line loads on the
other's server — the server checks the API version it was built against, and the
client plugin binds to names the other client does not have.

Mods that overhaul the same ballistics, armor or medical systems are incompatible.
Modded weapons, ammunition and armor are supported when they provide sensible
physical properties.

## Installation

Download the package for your SPT version from
[GitHub Releases](https://github.com/Anamelash/spt-plate/releases) and unpack it.
Install both halves:

- `PLATE.Client.dll` → `<SPT>\BepInEx\plugins\PLATE\`
- `PLATE.Server.dll` and `bundles/` →
  `<SPT>\SPT\user\mods\PLATE\`

On first start, the server generates `config.jsonc` and
`ammo-reference.jsonc` next to its DLL. Client gameplay settings live in the
F12 menu. Advanced client constants can be edited in
`BepInEx\config\com.anamelash.plate.cfg`.

## Updating and Uninstalling

Replace both components when updating; never combine a client DLL from one
release with a server DLL from another.

The Blood Transfusion Kit is the only new item stored in the player profile.
Before uninstalling P.L.A.T.E., delete every Transfusion Kit from the PMC
inventory and stash. Once those items are gone, the mod can be removed safely.

## Bugs, Balance and Feedback

GitHub Issues are the official feedback channel:

- [Report a bug](https://github.com/Anamelash/spt-plate/issues/new?template=bug_report.yml)
- [Report a balance or realism problem](https://github.com/Anamelash/spt-plate/issues/new?template=balance_report.yml)
- [Request a feature](https://github.com/Anamelash/spt-plate/issues/new?template=feature_request.yml)

For a strange hit, attach the relevant lines from
`BepInEx/plugins/PLATE/events.log`. They contain who fired and from what, the
projectile, impact energy, wound channel, armor decision and exit state;
without them, a penetration report is mostly a campfire story.

Other diagnostics:

- Server: `user/logs/spt/spt<date>.log`
- Client and patch self-test: `BepInEx/LogOutput.log`
- Per-hit event journal: `BepInEx/plugins/PLATE/events.log`

## Why the Project Lives on GitHub

The author no longer has access to the Forge website or its Discord after being
banned following a good-faith complaint about targeted harassment. Those pages
cannot be maintained and cannot provide a route for feedback. Development did
not stop: releases, source, documentation and support continue in this
repository.

If you found P.L.A.T.E. through an old Forge listing, a mirror or a community
platform fork, check [GitHub Releases](https://github.com/Anamelash/spt-plate/releases)
before installing anything.

## Building from Source

The server targets net9, so building requires the .NET 9 SDK. Set your game
path in `Directory.Build.props` (`SptGameDir`), then run:

```powershell
pwsh -File build/deploy.ps1
```

The script builds both projects in Release mode and copies them into the game
installation. Close the game and the SPT server before deploying.

Run the test suite with:

```powershell
pwsh -File build/test.ps1
```

## License

[Creative Commons Attribution-NonCommercial-ShareAlike 4.0 International](LICENSE)
(CC BY-NC-SA 4.0).

You may share and adapt the project, including its code and bundled model,
provided you give attribution, do not use it commercially and distribute the
derivative under the same license.
