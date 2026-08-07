# P.L.A.T.E. — Penetration, Lethality, Armor & Trauma Engine

> **Note on the Forge**
>
> I was banned from the Forge website and its Discord community after
> submitting a good-faith complaint about targeted harassment and the public
> reposting of my responses. The moderators' handling of the situation was
> unacceptable.
>
> This will not end the project. I will continue to maintain and develop it
> here: releases and issues live in this repository.

A physics-driven overhaul of terminal ballistics, armor and trauma for SPT 4.1
(EFT 0.16.9.40087):

- **Penetration** — armor as a physical barrier: specific-energy thresholds
  anchored to real protection standards, material behavior (ceramic, steel,
  UHMWPE, aramid, titanium), hit angle, wear and local damage.
  ([how it works](docs/MODEL.md#armor))
- **Lethality** — damage computed at the moment of impact from projectile
  physics and the actual path through the body; distance, barrel length,
  grazes, bones and vital zones all matter.
  ([how it works](docs/MODEL.md#wound-channel))
- **Armor interaction** — a penetrating bullet pays with energy, mass and
  shape; a blocked one delivers behind-armor blunt trauma.
  ([how it works](docs/MODEL.md#behind-armor-blunt-trauma))
- **Trauma** — blood volume as a separate resource: bleedings drain blood, not
  limb HP; blood-loss stages bring debuffs up to collapse and death; blood
  persists between raids, transfusions restore it.
  ([how it works](docs/MODEL.md#blood-and-trauma))

Every model is anchored to published work rather than to hand-tuned game feel:
wound ballistics and ordnance-gelatin data for the wound channel, the Blunt
Criterion literature for behind-armor trauma, GOST protection classes for armor
thresholds, and the ATLS classification of hemorrhagic shock for the blood
system. The derivations, the formulas and the
[calibration anchors](docs/MODEL.md#calibration) are written up in
**[docs/MODEL.md](docs/MODEL.md)**, along with
[what is deliberately not modelled](docs/MODEL.md#what-is-deliberately-not-modelled)
and the [sources](docs/MODEL.md#sources).

See [CHANGELOG.md](CHANGELOG.md) for the list of changes vs vanilla.

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
