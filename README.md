[**English**](README.md) · [Русский — описание, скачивание и установка](README.ru.md)

# P.L.A.T.E. — Realistic Combat, Ballistics, Armor and Trauma Mod for SPT

**P.L.A.T.E. (Penetration, Lethality, Armor & Trauma Engine)** is a gameplay
overhaul for **SPT (Single Player Tarkov)**. It replaces the vanilla damage,
armor penetration, wound, fracture, bleeding and blood-loss systems with a
single physics-based combat model.

P.L.A.T.E. does not replace the vanilla Damage and Penetration values with
another set of numbers. It calculates each hit from the projectile's
construction, mass, velocity and angle at impact. Barrel length, range, armor,
obstacles crossed before impact and the exact path through the body all affect
the result.

The mod is limited to ballistics, armor and trauma. It does not alter AI,
spawns, quests, progression, loot or graphics.

**Current release:** P.L.A.T.E. 1.4.1

**Supported game:** SPT 4.1.x, including 4.1.3 / EFT 0.16.9.40087

[Download the latest release](https://github.com/Anamelash/spt-plate/releases/latest) ·
[Discord — questions, feedback and ideas](https://discord.gg/w2DpURxtrf) ·
[All versions](https://github.com/Anamelash/spt-plate/releases) ·
[Installation](#installation) ·
[Physical model](docs/MODEL.md) ·
[Changes from vanilla](CHANGELOG.md) ·
[Report a problem](#bugs-balance-and-feedback)

> **Official download and support.** This GitHub repository is the maintained
> source for P.L.A.T.E. releases, documentation and issue tracking. Questions
> and suggestions can be posted in the
> **[P.L.A.T.E. Discord](https://discord.gg/w2DpURxtrf)**. The old Forge page,
> its Discord and third-party forks of that platform are not support channels
> for this mod.

## Download P.L.A.T.E. for SPT

Prebuilt packages are available from
**[GitHub Releases](https://github.com/Anamelash/spt-plate/releases)**. Use the
[latest release](https://github.com/Anamelash/spt-plate/releases/latest) with
SPT 4.1.x, including 4.1.3. For SPT 4.0.13, install
[P.L.A.T.E. 0.11.0](https://github.com/Anamelash/spt-plate/releases/tag/v0.11.0).
That backport has the features of 1.3.2. The cover and obstacle model added in
1.4.0 has not yet been backported.

Always install the client and server components from the same release.

## Where P.L.A.T.E. Fits in an SPT Mod Setup

P.L.A.T.E. is intended to be one part of an SPT mod setup. Its scope is shown
below.

| Mod category | P.L.A.T.E.'s role |
|---|---|
| AI behavior and bot spawning | Outside its scope |
| Quests, traders, progression and economy | Outside its scope |
| Loot, maps, graphics and general quality of life | Outside its scope |
| Additional weapons, ammunition and armor | Reads their physical properties when usable data is present |
| Damage and terminal ballistics | Replaces the vanilla system |
| Armor penetration, blunt trauma and wear | Replaces the vanilla system |
| Wounds, fractures, bleeding and blood loss | Replaces the vanilla system |

Mods for unrelated systems are normally compatible. Do not combine P.L.A.T.E.
with another ballistics, armor or medical overhaul: both mods will process the
same hits, even if the game appears to run normally. Fika support has not been
tested.

## What P.L.A.T.E. Changes

### Damage Is Computed at Impact

P.L.A.T.E. does not use the Damage value on an ammunition card as the final hit
damage. It uses the projectile's mass, caliber, velocity, construction and
orientation at impact, then follows its path through the target.

Velocity changes with barrel length and range. A bullet may be stopped by
bone, deform, fragment or yaw inside the body. If it passes through an arm and
enters the torso, the second wound is calculated from the projectile's reduced
velocity, mass and stability. See the
[wound-channel model](docs/MODEL.md#wound-channel) for the full calculation.

### Armor Is a Physical Barrier

P.L.A.T.E. models armor from its construction, material and thickness. Steel,
titanium, ceramic, UHMWPE, aramid and composite plates respond differently to
the same projectile. Penetration depends on the core, impact velocity and angle
rather than a direct comparison between two item-card values.

The protection class on the item card is a GOST R 50744-95 class: class N means
БрN, class 0 is the anti-fragment tier (glasses, replica helmets, headsets), and
class 6 — the 12.7 mm AP round — is deliberately empty, because no wearable
armor stops it. Each item's class is derived from the real product it is
modelled on: a published certificate first, otherwise what its documented
construction actually stops. The vanilla habit of reading Penetration against
ten times the class does not apply.

The projectile core may remain intact, flatten or fracture on impact. A
penetrating projectile loses energy and may also lose mass or stability. A
stopped projectile can still cause behind-armor blunt trauma. Details are in
the [armor model](docs/MODEL.md#armor) and
[blunt-trauma model](docs/MODEL.md#behind-armor-blunt-trauma).

### Armor Wear Is Local and Material-Specific

Armor damage is recorded around the point of impact. Repeated hits close to an
existing strike encounter the damaged area, while another part of the plate
can remain intact.

Durability loss also depends on material and outcome. Steel and titanium take
little damage from shallow impacts. Aramid and UHMWPE lose durability mainly
when penetrated. Ceramic is damaged when it absorbs a substantial impact,
including successful stops. Each material has its own wear model.

### Cover Is Relative

What you — and the bots — end up eating now depends entirely on the material
between you and the shooter. If that material happens to be cardboard boxes,
Tarkov Insurance LLC trusts that your PMC left a will.

P.L.A.T.E. replaces the vanilla penetration roll for map objects with a
calculation based on material and actual thickness along the line of fire. A
projectile that passes through an obstacle keeps the resulting loss of
velocity for every later impact.

Hollow objects are treated as separate surfaces with air between them. A fuel
drum, for example, presents two thin steel sheets rather than one solid block.
The calculation also uses the geometry of the visible object instead of the
full size of an oversized collider where possible. After penetration, a bullet
may be slower, deformed, unstable or deflected from its original path.

Ricochet depends on the surface and impact angle. Cars, cardboard cargo, brick,
concrete, timber, glass and sheet metal have separate material data. The values
and source notes are stored in the editable `obstacle-reference.jsonc` beside
the client plugin. See the
[environment model](docs/MODEL.md#environment-barriers) for details.

### The Body Has an Inside

Hits are resolved along a path through tissue, bone and vital zones. The model
includes the heart and great vessels, liver, spinal cord, brain, jaw and neck.

This separates the point of entry from the structures actually damaged. A hit
to the jaw is not treated as a hit to the brain, while a wound through the
heart or spinal cord can be fatal even when the remaining body-part health is
high. The same wound path determines the resulting blood loss.

### Blood Is a Separate Resource

Every bullet wound causes bleeding. The rate depends on the wound path and the
vessels it crossed. Bleeding reduces a separate body-wide blood reserve instead
of repeatedly subtracting HP from the affected limb.

As blood volume falls, stamina recovery deteriorates, followed by tremor and
visual effects, then loss of the ability to sprint or jump. Severe blood loss
can kill a PMC whose body-part health is still mostly intact. External wounds
can be treated with the appropriate field medicine; tourniquets do not stop
internal bleeding.

Blood volume persists after a raid and recovers gradually. Blood Transfusion
Kits are sold by Therapist and can also be crafted in the Hideout Medstation.

A high-energy torso impact can temporarily drain stamina whether or not the
armor was penetrated. The effect scales with the energy transferred to the
body. Strong impacts can also disorient bots when the optional setting is
enabled; it is off by default.

See [blood and trauma](docs/MODEL.md#blood-and-trauma) for the full system.

### Fractures, Ammunition, Shotguns and Grenades

- Fracture calculations use the part of the limb that was hit. Blacked limbs
  can fracture, surgery can set the bone, and leg fractures affect bots.
- Ammunition is matched to its real projectile where dependable data exists.
  Barrel length affects muzzle velocity, including barrels from weapon packs.
- Buckshot uses the correct pellet count. Flechettes are modeled as narrow
  steel darts rather than buckshot with adjusted damage values.
- Grenade fragments remain dangerous beyond the short vanilla radius. Soft
  armor can stop smaller fragments, while larger fragments retain more range
  and penetration.

### Configurable Lethality and HUD

The F12 menu has separate settings for the player, PMC bots and Scavs. Damage,
bleeding, direct-hit death and critical-organ lethality can be adjusted for
each group without changing the others.

Ballistics, blood, obstacle physics and the debug overlay can be enabled
independently. Disabling one of the gameplay systems restores vanilla behavior
for that system. The blood HUD can display current volume, ATLS shock class and
loss rate. Its position, scale, color and optional time estimate are
configurable.

## What It Changes in a Raid

- Projectile performance now depends on impact velocity, so barrel length and
  range can change both penetration and wound severity.
- Expanding and soft-point ammunition remains effective against unarmored
  targets but performs poorly against rifle plates.
- Hardened AP cores improve armor penetration, although the resulting narrow
  wound channel may cause less tissue damage than a projectile that expands or
  fragments.
- A stopped projectile can still cause blunt trauma and drain stamina.
- Hits close together weaken one area of a plate instead of reducing protection
  evenly across its entire surface.
- Cardboard boxes, wooden fences and car bodies conceal more reliably than they
  protect. Even a one-brick wall may be penetrated by full-power rifle rounds.
- Bleeding must be treated even when the HP display still looks healthy, and
  blood lost in one raid can affect the next.

## Research, Calibration and Limits

The model draws on published wound-ballistics and metallurgy research, GOST and
NIJ armor standards, trauma medicine and penetration testing. Its main
references include:

- Wound-ballistics and ordnance-gelatin studies for projectile behavior in
  tissue.
- GOST and NIJ standards, armor certificates and material data for protection.
- Blunt Criterion research for behind-armor trauma.
- ATLS hemorrhagic-shock classification for blood loss.
- Published penetration series and multi-hit armor tests for calibration.

Personal balance preferences are not used as calibration targets. When direct
measurements are unavailable, the assumptions and limits are documented with
the rest of the model.

Formulas, constants, calibration points and sources are collected in
**[The P.L.A.T.E. Physical Model](docs/MODEL.md)**. The document also lists
[features deliberately excluded from the model](docs/MODEL.md#what-is-deliberately-not-modelled).
Differences between the documented model and in-game behavior should be
reported as bugs.

For a complete player-facing comparison, see
**[P.L.A.T.E. — Changes vs Vanilla](CHANGELOG.md)**.

## Requirements and Mod Compatibility

| Component | Required version |
|---|---|
| P.L.A.T.E. | 1.4.1 |
| SPT | 4.1.x, including 4.1.3 |
| EFT client | 0.16.9.40087 |
| .NET SDK | 10, only when building from source |

Both components are required:

- `PLATE.Client.dll`, the BepInEx client plugin.
- `PLATE.Server.dll` and `bundles/`, the SPT server mod.

P.L.A.T.E. releases follow the SPT compatibility split below:

| P.L.A.T.E. version | Compatible SPT version |
|---|---|
| 0.x, the backport line | SPT 4.0.13 (`~4.0.0`) |
| 1.0.0 to 1.3.0 | SPT 4.1.0 to 4.1.2 |
| 1.3.1 and newer | SPT 4.1.x (`~4.1.0`), including 4.1.3 |

On SPT 4.1.3, use P.L.A.T.E. 1.3.1 or newer. Earlier builds register their
items too late for that server version and cannot start it.

The leading version number identifies the SPT server line. Version 0.11.0 is
the current backport for SPT 4.0.13 and has the same features as 1.3.2. It does
not include the obstacle model from 1.4.0. Neither branch loads on the other
SPT server line.

Mods that replace the same ballistics, armor or medical systems are
incompatible. Additional weapons, ammunition and armor are supported when
their items contain usable physical data.

## Installation

Download the package for your SPT version from
[GitHub Releases](https://github.com/Anamelash/spt-plate/releases) and unpack
it. Install both components:

- `PLATE.Client.dll` → `<SPT>\BepInEx\plugins\PLATE\`
- `PLATE.Server.dll` and `bundles/` →
  `<SPT>\SPT_Runtime\user\mods\PLATE\`

On first start, the server creates `config.jsonc` and `ammo-reference.jsonc`
beside its DLL. The client creates `obstacle-reference.jsonc` beside the
plugin. Normal gameplay settings are available in F12; advanced client
constants are stored in `BepInEx\config\com.anamelash.plate.cfg`.

## Updating and Uninstalling

Replace both components when updating. A client DLL and server DLL from
different releases are not supported together.

The Blood Transfusion Kit is the only new item stored in the player profile.
Remove every kit from the PMC inventory and stash before uninstalling the mod.
P.L.A.T.E. can then be deleted safely.

## Bugs, Balance and Feedback

Use GitHub Issues for feedback and reports:

- [Report a bug](https://github.com/Anamelash/spt-plate/issues/new?template=bug_report.yml)
- [Report a balance or realism problem](https://github.com/Anamelash/spt-plate/issues/new?template=balance_report.yml)
- [Request a feature](https://github.com/Anamelash/spt-plate/issues/new?template=feature_request.yml)

For an unexpected hit result, attach the relevant lines from the event log.
They record the shooter, weapon, projectile, impact energy, wound channel,
armor calculation and projectile state after the hit.

Other diagnostics:

- Server: `user/logs/spt/spt<date>.log`
- Client and patch self-test: `BepInEx/LogOutput.log`
- All combat events: `BepInEx/plugins/PLATE/events.log`
- Hits produced by the player: `BepInEx/plugins/PLATE/events-player.log`
- Optional obstacle survey: `BepInEx/plugins/PLATE/events-obstacles-hits.log`

## Why the Project Lives on GitHub

The author no longer has access to the Forge website or its Discord. The ban
followed a complaint about targeted harassment, so the old listing cannot be
updated and messages posted there do not reach the project. Development,
releases, documentation and support continue here.

If you found P.L.A.T.E. through an old Forge listing, a mirror or a community
platform fork, check [GitHub Releases](https://github.com/Anamelash/spt-plate/releases)
before installing it.

## Building from Source

The server targets net10, so building requires the .NET 10 SDK. Set the game
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
