# P.L.A.T.E. — Penetration, Lethality, Armor & Trauma Engine

> **Where this project lives.** I was banned from the Forge website and its
> Discord after filing a complaint about targeted harassment. The project
> continues here: releases, issues and everything else are in this repository.

A physics overhaul of terminal ballistics, armor and trauma for SPT 4.1
(EFT 0.16.9.40087). The damage number on an ammo card stops being a promise:
everything that matters is computed at the moment of impact, from the bullet
that actually arrived — its mass, calibre, velocity and construction — and from
what it actually hit.

What that buys you in a raid:

- **Armor is an obstacle, not a dice roll.** A plate is a thickness of a real
  material, and whether a round gets through is decided by whether that much
  steel, ceramic or polyethylene can stop *that* core at *that* speed and
  angle. The hard core of an AP round is what meets the plate — not the
  calibre, and not a penetration stat. A carbide core shatters on a face hard
  enough to crack it; a soft bullet dies on the plate and pays for it.
  ([how it works](docs/MODEL.md#armor))
- **Damage is a wound, not a subtraction.** A bullet crushes a channel through
  the actual path it takes — so distance, barrel length, grazes, bones, organs
  and over-penetration all matter, and a round that barely clipped an arm did
  exactly that. ([how it works](docs/MODEL.md#wound-channel))
- **Getting through costs the bullet.** A penetrating round leaves the plate
  slower, lighter and stripped of its jacket; a blocked one still hammers you
  through the armor — behind-armor blunt trauma, following the published
  injury criteria. ([how it works](docs/MODEL.md#behind-armor-blunt-trauma))
- **Blood is a resource.** Bleedings drain blood volume, not limb HP. Lose
  enough and the debuffs walk you down the real stages of hemorrhagic shock,
  to collapse and death; blood persists between raids, and Therapist sells
  transfusions. ([how it works](docs/MODEL.md#blood-and-trauma))

None of it is tuned by feel. The wound channel stands on wound-ballistics and
ordnance-gelatin data, behind-armor trauma on the Blunt Criterion literature,
armor on GOST and NIJ protection standards, the blood system on the ATLS
classification of hemorrhagic shock. Every formula, constant and calibration
anchor is written up in **[docs/MODEL.md](docs/MODEL.md)** — including
[what is deliberately not modelled](docs/MODEL.md#what-is-deliberately-not-modelled)
and the [sources](docs/MODEL.md#sources). If a number in the mod disagrees with
that document, it is a bug.

See [CHANGELOG.md](CHANGELOG.md) for the full list of changes vs vanilla, with
the reasoning behind each.

## Requirements

| Component | Version |
|---|---|
| SPT | 4.1.1 |
| EFT client | 0.16.9.40087 |

Both parts are required: the client plugin and the server mod work as a pair.

## Installation

- `PLATE.Client.dll` → `<SPT>\BepInEx\plugins\PLATE\`
- `PLATE.Server.dll` (+ `bundles/`) → `<SPT>\SPT_Runtime\user\mods\PLATE\`

Server config (`config.jsonc`) and the ammo reference book
(`ammo-reference.jsonc`) are generated next to the server dll on first start.
Client gameplay settings live in the F12 menu; fine-tuning constants that are
hidden there can be edited in `BepInEx\config\com.anamelash.plate.cfg`.

## Building from source

Requires .NET SDK 10 — the server half targets net10, the framework SPT 4.1 is built
against. Set your game path in `Directory.Build.props` (`SptGameDir`), then:

```bash
pwsh -File build/deploy.ps1
```

The script builds both projects (Release) and copies them into the game
installation. Close the game and the SPT server before deploying.

## Troubleshooting

- Server side: look for `[PLATE]` lines in `user/logs/spt/spt<date>.log`.
- Client side: `BepInEx/LogOutput.log` contains a patch-target self-test; a
  FAIL there usually means an SPT update changed remapped class names.
- `BepInEx/plugins/PLATE/events.log` records every hit with its physical
  breakdown — please attach it to bug reports.

## License

[Creative Commons Attribution-NonCommercial-ShareAlike 4.0 International](LICENSE)
(CC BY-NC-SA 4.0).

You may share and adapt this work, including the code and the bundled model,
provided you give attribution, do not use it commercially, and release your
derivative under the same license.
