# P.L.A.T.E. — Penetration, Lethality, Armor & Trauma Engine

A physics-driven overhaul of terminal ballistics, armor and trauma for SPT 4.0
(EFT 0.16.9.40087):

- **Penetration** — armor as a physical barrier: specific-energy thresholds
  anchored to real protection standards, material behavior (ceramic, steel,
  UHMWPE, aramid, titanium), hit angle, wear and local damage.
- **Lethality** — damage computed at the moment of impact from projectile
  physics and the actual path through the body; distance, barrel length,
  grazes, bones and vital zones all matter.
- **Armor interaction** — a penetrating bullet pays with energy, mass and
  shape; a blocked one delivers behind-armor blunt trauma.
- **Trauma** — blood volume as a separate resource: bleedings drain blood, not
  limb HP; blood-loss stages bring debuffs up to collapse and death; blood
  persists between raids, transfusions restore it.

Every model is anchored to published work rather than to hand-tuned game feel:
wound ballistics and ordnance-gelatin data for the wound channel, the Blunt
Criterion literature for behind-armor trauma, GOST protection classes for armor
thresholds, and the ATLS classification of hemorrhagic shock for the blood
system.

See [CHANGELOG.md](CHANGELOG.md) for the full list of changes vs vanilla and the
sources behind each model.

## Requirements

| Component | Version |
|---|---|
| SPT | 4.0.13+ |
| EFT client | 0.16.9.40087 |

Both parts are required: the client plugin and the server mod work as a pair.

## Installation

- `PLATE.Client.dll` → `<SPT>\BepInEx\plugins\PLATE\`
- `PLATE.Server.dll` (+ `bundles/`) → `<SPT>\SPT\user\mods\PLATE\`

Server config (`config.jsonc`) and the ammo reference book
(`ammo-reference.jsonc`) are generated next to the server dll on first start.
Client gameplay settings live in the F12 menu; fine-tuning constants that are
hidden there can be edited in `BepInEx\config\com.anamelash.plate.cfg`.

## Building from source

Requires .NET SDK 9. Set your game path in `Directory.Build.props`
(`SptGameDir`), then:

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
