# P.L.A.T.E. — Changes vs vanilla

P.L.A.T.E. (Penetration, Lethality, Armor & Trauma Engine) replaces the abstract
damage numbers of the base game with an attempt to reproduce a real physical
model of terminal ballistics, armor interaction and blood loss. Below is an
exhaustive list of what behaves differently from vanilla and the reasoning
behind it. Each section ends with the published work the model rests on, and the
sources are collected under [References](#references) at the end.

The derivations themselves — formulas, constants and calibration anchors — are in
[docs/MODEL.md](docs/MODEL.md).

## Damage: computed at the moment of impact, not taken from a table

**Vanilla:** every round carries a fixed damage number; damage falls off
linearly with speed and can never drop below a hard floor; the hit zone applies
a static multiplier.

**PLATE:** damage is calculated at the instant a projectile strikes flesh, from
its physical state — mass, caliber, actual impact velocity, bullet construction
(solid AP vs expanding) — and from the actual path the projectile takes through
the body part. The model builds a wound channel (crushed tissue along the
penetration depth) plus a temporary stretch cavity that only becomes damaging at
rifle velocities, and the total can never exceed the kinetic energy the
projectile actually brought in. Consequences you will feel:

- **Distance and barrel length genuinely matter.** A small-caliber rifle round
  that arrives slow has lost its violent cavitation and behaves like an ice
  pick. Heavy subsonic pistol rounds, by contrast, keep almost all of their
  effect at range.
- **Grazing hits are scratches.** The path through the body is computed from
  the hit angle and the body geometry: a bullet clipping the edge of a limb
  deposits almost nothing and flies on.
- **Vital zones are honest.** Brain and neck hits are dramatically more
  damaging than muscle; a jaw hit is grave but survivable.
- **Bones matter.** A limb hit can stop a bullet in the bone — with a fracture
  and full energy transfer — or punch through and continue into the torso.
- **Over-penetration is an energy balance.** A bullet exits a body part with
  the velocity physics leaves it, and whatever it hits next receives damage
  computed from that remaining velocity. Nothing is zeroed out by game-logic
  quirks (vanilla's occasional "no damage" pass-through cases are fixed).
- **Bullet fragmentation splits the bullet's mass.** Each fragment continues as
  its own small projectile; fragments that cannot exit the current body part
  deposit their energy there. No bonus damage appears out of thin air.
- **Whether a bullet fragments is derived, not read from the card.** The vanilla
  `FragmentationChance` field takes no part in the wound formulas any more: a
  bullet breaks up where it turns broadside, if it is still faster there than a
  thin jacket can bear (600–700 m/s published band), and only its deformable
  share breaks — a hard core never does. M193 and M855 fragment at their speeds,
  7.62×39 PS, monoliths and pistol rounds do not, with no per-cartridge opinion
  involved.
- The damage and penetration numbers on item cards remain as reference — the
  actual result is always computed from physics at impact. The card value is one
  defined test (a perpendicular centre-chest hit at muzzle velocity, 250 mm of
  tissue) and exists to rank cartridges against each other; a raid hit routinely
  lands anywhere from a small fraction of it (graze, extremity) to half again
  above it (long oblique torso chord), and that spread is the model working as
  designed, not an error. See MODEL.md, "What the number on the card means".
- **Armour wear is probabilistic, not a smooth discount.** A worn plate is
  intact where nothing hit it and broken where something did: the chance a hit
  finds a damaged spot equals the missing durability, and a found spot loses
  thickness by a per-material law — a ceramic tile struck twice in the same
  place is rubble (15% left), hard steel keeps 75%, an aramid pack barely
  notices (99%). Repeat hits into remembered impact points are resolved by
  geometry, no dice. On composite plates the face and the backing wear each by
  their own law.
- **The damage scale is anchored to research, not to vanilla.** The two wound
  constants used to be tuned so two reference cartridges landed near their
  vanilla damage; they are now set from the combat-mortality figure of ~2.3
  rifle hits to the torso to incapacitation (~37 HP a hit). Crush damage roughly
  doubles per unit of cavity and stretch damage falls to about a third per
  joule, so heavy slow bullets gain relative to light fast ones and pistol
  calibres close some of the gap to rifles. Expect every damage number to move.

*What this is based on:* the standard quadratic drag law — a projectile slowing
down in tissue loses velocity exponentially, so how deep it reaches is driven by
its sectional density rather than by raw energy. The depth curve is calibrated
against published ordnance-gelatin penetration data, the same 10% gelatin block
that laboratory and field test series use as a tissue simulant. The split into a
permanent crush cavity plus a temporary stretch cavity, and the fact that stretch
only turns destructive once impact velocity crosses the classic high-velocity
wound boundary, come from Fackler's wound-ballistics work: elastic tissue
survives being stretched slowly, so a slow heavy bullet cuts while a fast light
one tears.

## Anatomy: where the bullet went, not just which limb

**Vanilla:** a body part is a bucket of hit points with a flat multiplier. Every
chest hit is the same chest hit, and how badly a wound bleeds is a number
attached to the cartridge — the same round bleeds identically through a thigh and
through the middle of a chest.

**PLATE:** the hitboxes the game already has are read as anatomy. The middle third
of the upper ribcage is the heart and the great vessels behind it; the right third
of the lower ribcage is the liver; the thin spine collider is the cord. Which way
round a hitbox is gets worked out at the moment of the hit, so the liver stays on
the target's own right whichever way they are facing. Consequences you will feel:

- **Centre mass is genuinely worse than the edge of it.** A channel that runs
  deeper than halfway through the heart or across the cord kills — not by a
  scripted death, but by damage equal to what the body part had left, so the kill
  feed, the statistics and other mods see an ordinary hit.
- **A near miss still counts.** The stretch cavity of a rifle round can reach an
  organ the bullet itself missed, which raises the damage and can stop a heart or
  tear a liver loose. A pistol practically never does either — it has no cavity
  worth the name.
- **Some wounds cannot be bandaged.** An opened liver or a torn great vessel
  bleeds internally, and no bandage, tourniquet or hemostatic reaches it. That is
  a death in tens of seconds rather than instantly, which is where the casualty
  data puts most of it.
- **Bleeding is decided by what was cut.** How much of a plane the channel swept
  and what vessels run where it swept it, instead of a per-cartridge number. A
  clipped forearm and a crossed torso are no longer the same wound.
- **Rifle rounds tumble.** A long bullet enters point-first and goes broadside
  after a stretch of tissue, so it cuts a narrow channel and then a wide one. The
  same round is therefore a different wound in an arm and in a chest. Buckshot and
  fully expanded bullets do not tumble, and the model works that out from their
  shape rather than being told.
- **Two identical hits are not identical.** Where a bullet turns, what tissue it
  crossed and exactly where a person's organs sit all vary shot to shot — drawn
  once per shot, so a wound that passes through two body parts stays one wound.

*What this is based on:* the Abbreviated Injury Scale and the way Injury Severity
Score squares it — ordinal severities, so two moderate wounds must stay lighter
than one severe. Which injuries are unsurvivable comes from a combat autopsy
series of 4,596 deaths, which also measured the split between deaths at the
moment of wounding and deaths over the following minutes, and where on the body
the fatal bleeding came from. The cavity radius is anchored on published gelatin
profiles, and bullet lengths are derived from mass and calibre — which reproduces
the measured lengths of 7.62×51 M80 and 5.56×45 M855 to a tenth of a millimetre.

## Armor: a physical barrier, not a dice roll

**Vanilla:** armor class and durability feed a penetration-chance roll, and
penetrating hits lose a flat percentage of damage.

**PLATE:** a plate or soft panel is an obstacle with material properties, and
the projectile has to defeat it with specific energy:

- **A plate is a thickness of a material, not a class number.** Where the armor is
  modelled on something real — which is nearly all of it — whether a round gets
  through is decided by the speed at which that much of that material stops that
  particular core, and not by a rating. Every cartridge GOST R 50744-95 names is
  fired at a plate of its own class in the test suite and must be stopped, and at
  the plate one class down, where it must get through.
- **A class is what a construction earns, not a label it wears.** Aramid stops
  where aramid stops. A package sewn out of it cannot be rated past class 2 whatever
  the carrier is sold as — getting to class 4 with fabric alone would take around
  200 mm of it — and pressing the same fibre into a helmet shell buys exactly one
  rung more. Vanilla stamps class 3 on 125 of the aramid packages built into vests
  (the Fort Redut-M and Redut-T, the Fort Defender 2, the NFM THOR, the IOTV Gen4,
  the HighCom Trooper, the 6B13 and 6B43, the Gzhel, the Crye carriers and most of
  the rest), class 4 on polycarbonate visors, and class 10 on a development
  balaclava — 141 items in all. They now carry the class their construction holds.
  How they stop a bullet does not change — the model was
  already reading them as the packages they are — but the number on the card stops
  promising rifle protection that was never in the vest, and the anti-fragment
  threshold, which scales with the class, stops being computed off a rating the
  fabric does not have. Nothing hard is touched: plates keep their class, and so do
  appliques that are rifle-rated in their own right, like the Gentex SLAAP.
- **Protection classes are anchored to the real GOST protection standard,** and
  are the fallback for armor nothing is known about. Bottom-tier "class 1" junk
  headwear (construction helmets and the like) is fragment protection only — it
  will not stop a pistol bullet.
- **Hardness decides who wins.** A core softer than the plate flattens on its face
  and stops being a punch. This is the difference between the two 7.62×39 loads
  and between the two 62-grain 5.56 loads, and it is why 25 mm of polyethylene
  shrugs off a mild-steel-cored rifle round and is beaten by a small hard core
  carrying half the energy.
- **Materials behave like themselves.** Ceramic offers the highest threshold
  and grinds down even hard AP cores, but cracks in tiles — a follow-up hit
  into the same segment meets rubble. Armor steel is expensive to defeat and
  flattens soft bullets, but its damage zone is local: the "gong" takes dozens
  of hits. UHMWPE lets a penetrating bullet through nearly intact and is easier
  for sharp-nosed AP to slip through; aramid behaves similarly as soft armor.
  Titanium is viscous and bleeds off an exceptional amount of energy even when
  defeated.
- **Armor piercing comes from the core, not from a bonus.** A plate meets the
  hard core of a bullet, not its calibre. The tungsten-carbide core of an M993 is
  5.5 mm across in a 7.85 mm bullet, so it arrives at twice the energy density of
  the same energy spread over the full jacket — and that is the whole of its
  advantage, taken from the round's published construction. Rounds whose "steel
  core" is too soft to hold its shape against a plate get nothing: an M855 and an
  M855A1 are the same weight in the same case, and the difference between them is
  40 HRC against 58. The other half of the same sentence: a bullet that can deform
  flattens on the face of the panel before it has finished loading it, so a hollow
  point spreads its energy wider and does badly against armor whatever it carries.
- **A penetrating bullet pays for the hole, and leaves its jacket in it.** What
  goes on through is the core — lighter, narrower, and *harder* than the bullet
  was, because the deformable material was the part that stayed behind. An M855
  that defeats a plate arrives on the far side as 0.65 g of steel penetrator.
  Ceramic then grinds down what is left; aramid does not. There is no separate
  "mitigation percent".
- **Angle matters, and by a measured amount.** An oblique hit faces more material
  along its path, so a plate stops `1/cos θ` more velocity: 16% more at 30°, 41%
  more at 45°. That is not a guess — published trials that shot one plate at 0°,
  15°, 30° and 45° found 3%, 16% and 43%, and a dozen further pairs of plates —
  seven aluminium alloys and an ultra-hard steel at two thicknesses — put the 30°
  gain between 6% and 16%, which is the model's other claim about angle: the gain
  does not depend on what the plate is made of. Steep
  angles beyond that push the interaction toward ricochet mechanics.
- **Worn armor protects worse,** and durability loss itself is now driven by
  the energy the armor absorbed — brittle materials wear out in a few stops,
  steel lasts.
- **Blocked hits still hurt.** Behind-armor blunt trauma follows the published
  Sturdivan blunt criterion: energy through the panel produces pain, contusion
  and, at high transfer, internal bleeding and winded breathing — spread over
  the panel area for steel, focused for soft armor.

*What this is based on:* protection classes are anchored to the GOST body-armor
standard — each class threshold is derived from the specific energy of the round
that class is certified against, which is why a class stops what it is rated to
stop and not a tier more. Material behavior follows documented armor engineering
rather than a per-item fudge factor: ceramic's high threshold paired with its
multi-hit fragility, steel's locality of damage, the ease with which a
sharp-nosed core slips through fibrous soft armor. Behind-armor trauma uses the
Blunt Criterion of Sturdivan, Viano and Champion, whose published injury-risk
curves link impact energy, body mass, chest-wall thickness and impactor diameter
to the probability of real chest injury; it was validated in blunt ballistic
impact research at Wayne State, and the symptom spectrum reproduced in game —
from bruising to lung and heart contusion with internal bleeding — follows the
clinical literature on behind-armor blunt trauma and the backface-deformation
limits used in armor certification.

## Ammunition and grenade data: normalized against real prototypes

- **Bullet construction comes from a table of real cartridges,** keyed by the
  round's own name: how much of it can deform, and the frontal area and mass of
  its hard core where one is published. A cartridge is the same cartridge in every
  pack it ships in, which was not true before — the character of a round used to
  be inferred from where it sat among whatever other ammunition an install
  happened to have, so clones of one bullet could come out with different physics
  and a plain M80 could be read as an expanding round. Ammunition the table does
  not name still falls back to that inference.
- Every round in the database — including rounds added by other mods — is
  normalized from its physical data. Shotgun shells receive real pellet counts,
  pellet masses and velocities of their prototypes (vanilla systematically
  under-loads pellet count); flechettes behave like steel needles (deep, narrow,
  armor-piercing, low tissue damage); less-lethal and gas rounds stop being
  accidental hand-cannons.
- Grenade fragments get the mass and initial velocity of their real prototypes,
  and the blast strength is scaled from the actual explosive charge. Fragment
  flight range is extended beyond vanilla's short hard cap (configurable).
- Fragments respect fragment-protection ratings: soft armor reliably stops the
  average fragment, while the rare large fragment (base plate, fuze body) can
  defeat low protection classes near the epicenter.

*What this is based on:* bullet cores come from published construction — core
weight, core diameter and core hardness where a maker or a standard publishes
them, and a core is only entered when one of those exists. Where a core's mass
and length are published but not its diameter, the area follows from the alloy's
density; the two tungsten-carbide cores that different makers publish in
different calibres, the M993 and the 7N37, land within four percent of each other
on that arithmetic, which is the only reason to trust the rest of it. Shell and
grenade figures come from open-source prototype specifications — service manuals
and public reference works — for charge weights, pellet counts, fragment mass and
fragment initial velocity.
Pellet masses are not invented but derived from the density of lead and the
nominal pellet diameter, which is what exposes vanilla's systematic
under-loading of small buckshot. Blast strength scales from the actual explosive
charge by the cube-root law that governs blast effect with charge mass, so a
grenade's blast reflects how much explosive it really carries.

## Blood and trauma system

**Vanilla:** a bleed is a damage-over-time tick that chews on limb HP and
eventually times out on its own.

**PLATE:** bleeding is not damage. It is blood leaving your body, tracked as its
own resource, and it kills you on its own terms.

- **Bleedings no longer reduce HP at all.** They drain blood volume instead.
  You can be at full health on every limb and still be dying, because the number
  that matters is how much blood is left. Cumulative loss walks through the real
  stages of hypovolemia: racing pulse, tremor and tunnel vision, then no sprint
  and no jumping, then collapse, then death. The health tab shows it as blood
  pressure.
- **Every hole in you bleeds** — as it does in life. Any projectile that opens
  the body opens a bleed; how bad it is follows from the wound channel it cut.
  The wide, ragged wounds bleed the worst.
- **Bleedings do not stop by themselves.** There is no timer quietly saving you.
  They stop when you stop them, and not a second earlier.
- **Everyone lives under this rule.** Bots bleed exactly the way you do, from
  the same wounds, on the same clock. If two people meet, trade fire, and both
  break contact — both of them quietly bleeding out over the next minute is a
  perfectly normal outcome. Winning the gunfight and losing the raid is a thing
  that happens now, to you and to them.
- **Pack for it.** Dressings, tourniquets and hemostatics are the only thing that
  closes a bleed, painkillers take the edge off what wounds and fractures do to your
  hands, and a blood transfusion kit (sold by Therapist, craftable at the med
  station) is the only way to put volume back in during a raid. Going in light
  on medical is now a real decision, not a slot you skipped.
- Fractures come from actual bone hits with energy behind them, not from a
  damage-number lottery.
- A destroyed abdomen bleeds into the belly, behind-armor trauma to the torso or
  head bleeds into a cavity, and nearby explosions can add blast barotrauma —
  none of those three can be closed in the field. A destroyed limb bleeds hard
  but out in the open, so a tourniquet or a hemostatic still answers it.
- Blood carries over between raids and regenerates slowly out of raid — walking
  out of one fight half-empty is a problem you take with you into the next one.

*What this is based on:* the blood pressure model follows **ATLS** — Advanced
Trauma Life Support, the American College of Surgeons' trauma protocol taught to
emergency clinicians. Its four classes of hemorrhagic shock are the skeleton of
the whole system: the thresholds where the tiers switch are the ATLS blood-loss
classes, and the symptoms attached to each tier are the ones the protocol lists
for that class — racing pulse and anxiety first, then falling pressure with
confusion and collapsing motor control, then the pre-coma state, then circulatory
arrest. The blood-pressure readout in the health tab is that scale. Total
circulating volume follows the standard estimate per unit of body mass, roughly
five liters for an adult. Bleed rates follow documented trauma figures — a fully
transected major artery empties a person in minutes while venous and soft-tissue
wounds leak orders of magnitude slower — and flow is not constant: it tapers as
volume drops, the way falling pressure and vasoconstriction limit real bleeding,
which is what makes a tourniquet applied late still worth applying.

## Quality of life

- F12 menu holds the gameplay-level settings, including a **damage scale** and a
  **bleed rate** multiplier (from bullet-sponge to instant-kill for the curious),
  set separately for you, PMC bots and Savage-side NPCs — so "I want a fair fight
  but scavs should not bleed out in the woods" is two sliders. Fine-tuning —
  material profiles, model constants — lives in the config files next to the
  mod, server side and client side.
- An event journal (`events.log`, size-capped) records every hit with its full
  physical breakdown — please attach it to bug reports.

## Compatibility note

PLATE derives behavior from physical data: masses, velocities, calibers,
materials. "Fun" mods that ship deliberately unrealistic ammunition or armor
stats (thousand-damage bullets, weightless pellets, paper plates with high
class numbers) will produce unpredictable — sometimes hilarious, sometimes
broken — results in combination with PLATE. Mods that overhaul the same systems
(ballistics/armor/medical overhauls) are incompatible by definition. Co-op
(Fika) is untested.

## Release history

### 1.0.0

- **SPT 4.1.1 only.** Both halves were rebuilt against 4.1 and neither will load on
  4.0: the server compares the `SPTarkov.Server.Core` version a mod was built against
  with its own and refuses a mismatch, and the client references game types that 4.0
  did not have under those names. Stay on 0.10.0 if you are staying on SPT 4.0.
  The server half now targets net10, so building from source needs the .NET 10 SDK.
- **Nothing about the physics changed.** No formula, constant or calibration anchor
  moved in this release — the same wound channel, the same ballistic limit, the same
  blood model, addressed through 4.1's names and APIs. If a number in a raid looks
  different from 0.10.0, that is a bug in the port, not a rebalance.
- Installation path moved with the server: the server half now goes to
  `<SPT>\SPT_Runtime\user\mods\PLATE\` instead of `<SPT>\SPT\user\mods\PLATE\`.

### 0.10.0

- **The body now has an inside.** The hitboxes the game already ships are read as
  anatomy: the middle third of the upper chest is the heart and the great vessels,
  the right third of the lower chest is the liver, the thin spine collider is the
  cord — worked out at the moment of the hit, so the liver stays on the target's
  own right whichever way they face. A channel deep enough through the heart or
  across the cord kills, delivered as ordinary damage the kill feed and other mods
  understand; the stretch cavity of a rifle round can stop a heart the bullet
  itself missed; an opened liver or torn great vessel bleeds internally, beyond
  any bandage. Full account in the "Anatomy" section above.
- **Rifle bullets tumble.** A long bullet cuts a narrow channel until it turns
  broadside, then a wide one, and where it turns — if it is still fast enough —
  its deformable share breaks up. The vanilla `FragmentationChance` field takes
  no part in the formulas any more: M193 and M855 fragment because of what they
  are made of, monoliths and pistol rounds do not. Bullet length is derived from
  mass and calibre, and lands on the measured lengths of M80 and M855 to a tenth
  of a millimetre.
- **No two hits are the same wound.** Where the bullet turns, how dense the
  tissue is and where exactly the organs sit are drawn once per shot; whatever
  continues past the first body part inherits the draw, so a bullet that turned
  in an arm arrives in the chest already broadside.
- **How a wound bleeds is decided by what it cut.** The per-cartridge bleed
  chance is overwritten on every hit by geometry: the plane the channel swept
  through the flesh times the vessels that actually run there — torso, junction
  (neck, groin, shoulder), limb and head each carry their own vessel density.
- **The damage scale is re-anchored from vanilla parity to combat mortality:**
  about 2.3 rifle hits to the torso to incapacitation, ~37 HP a hit
  (`WoundVolumePerHp` 710 → 381, `TcEnergyPerHp` 28 → 74). Heavy slow bullets
  gain relative to light fast ones; expect every damage number to move.
- **Armour wear is a place, not a percentage.** The smooth durability discount
  (`DurabilityFloor`, `DegradeFloor`, per-material `DegradeMult`) is retired. A
  worn plate is intact where nothing hit it and broken where something did: the
  chance a hit finds a damaged spot equals the missing durability, and a found
  spot loses thickness by a per-material law — ceramic struck twice in the same
  place is rubble, steel keeps three quarters, an aramid pack barely notices.
  Repeat hits into remembered impact points are resolved by geometry, no dice.
- **Ductile metal now fails two ways, chosen by its metallurgy.** Shear plugging
  (work grows with thickness squared) versus ductile hole growth (linear in
  thickness) — a property of the alloy on record, not whichever mechanism is
  cheaper; the mass doing the work against a metal plate is the core plus the 5%
  of the jacket that rides it through (Forrestal's measurement). The mechanism
  constants are re-derived from published ladders rather than hand-set:
  `DuctileK` 4.40 → 2.64, `BrittleK` 0.75 → 1.04, `FibrousK` 27.5 → 28.8, new
  `HoleGrowthK` 6.60.
- **The armour reference book records constructions, not solved numbers.**
  Ceramic composite plates are split into their real face and backing (a SAPI is
  ~9 mm of silicon carbide on ~12 mm of UHMWPE, not 21 mm of "ceramic"); class
  ladder steps point at a real certified product where one exists, and the rest
  are re-solved against what a certificate actually demands — zero penetrations
  out of five, which puts the required V50 about 9% over the test velocity.
  Every material strength in the book now names its source document.
- **A fabric pack no longer quietly deletes half the bullet.** Stripping the
  jacket off a bullet takes a hard edge; a sewn aramid pack has none, so what
  comes through it is the whole bullet, not a bare core. The same vest was
  previously being credited with removing half the incoming energy.
- **The raid-end journal audits the model against the literature it stands on:**
  a table of organ-zone encounters, and where the blood actually went — deaths
  from wounds versus bleed-outs against the measured 35/52 split, and blood loss
  by body region against the measured 67/19/13 distribution.
- **A hard plate no longer gets an unearned bonus against a soft bullet.** The
  hardness term — how much a plate is worth for being harder than the core hitting
  it — was clamped at 4.5x, and that clamp stood on two computed figures rather than
  on anything measured: the steel pistol plates it was anchored to have their
  thickness *solved by this same model*, so any clamp value looked confirmed. It has
  been re-derived against the one published certificate where a soft core meets a
  hard plate — a quarter-inch AR500 plate against six shots of M80 ball — which puts
  it at 2.08. Consequences in a raid: ball and hollow-point ammunition now does to
  steel and titanium plates roughly what its energy says it should. A .50 BMG no
  longer bounces off a 6.5 mm titanium class-4 plate carrying seven times the energy
  that plate is certified against; pistol rounds against thin steel are unchanged,
  because the two steel pistol plates were re-solved at the new clamp (1.3 → 1.9 mm
  and 1.7 → 2.5 mm) and still stop exactly what their class says.
- The hit log now records the impact angle on every armour line, and flags when the
  grazing floor rather than the geometry set it. Angle moves a ballistic limit
  harder than any constant in the model — 70° costs a plate three times its
  thickness — and it was the one input a raid log could not show.
- **Soft armor no longer claims a class its fabric cannot reach.** The aramid
  packages built into vests ship stamped class 3 — 125 of them, from the Fort
  Redut-M to the IOTV Gen4 — and a sewn package tops out at 2; the same rule takes
  polycarbonate visors down from 4 and a development balaclava down from 10.
  They already behaved like the packages they are, because the construction was
  read at the ceiling; now the class on the item says so too, which also stops the
  anti-fragment threshold being computed off a rating the fabric does not have.
  Plates keep their class, and so does a rifle-rated applique like the Gentex SLAAP.
- Retired the `GostArmor` module switch. It was a stub that never did anything, and
  what it promised is now part of the armour normalizer.
- **A destroyed arm or leg now bleeds externally instead of internally.** It used
  to open a permanent internal bleed, which put a femoral or brachial wound —
  the textbook indication for a tourniquet, and what a hemostatic is carried
  for — beyond the reach of every item that exists to treat it. Black out a leg,
  patch it, and you were still on a countdown to bleeding out with nothing you
  could do about it. The wound is still severe; it is now treatable. A destroyed
  abdomen is unchanged and still internal.
- Behind-armor trauma only causes internal bleeding on the torso and head. Soft
  armor over an arm bruises muscle; it does not tear anything into a cavity.
- Retired the "Leg destroyed bleed" and "Arm destroyed bleed" settings — those
  wounds now use the ordinary heavy-bleed rates for a leg and an arm.
- The event journal names the victim on every hit line, and every internal bleed
  is recorded with the zone that opened it. Anonymous hit lines in a raid full of
  parallel fights read as yours when they belong to someone across the map.

### 0.9.5

- Damage scale is now set separately for you, PMC bots and Savage-side NPCs.
  An existing setting carries over into all three.
- New bleed rate multiplier (0-10), also per side: scales every bleed, external
  and internal. 0 makes bleeding cosmetic for that side.
- The event journal header lists the settings that differ from their defaults.

### 0.9.4

- Surgical kits (CMS, Surv12) now close the bleedings on the limb they repair.
  Toggleable.
- Internal bleeding — the kind no field medicine can close, from destroyed limbs,
  behind-armor trauma and blast — can now be switched off, separately for you, PMC
  bots and Savage-side NPCs.

### 0.9.3

- Fixed: medical items could not be used in raid on 0.9.2.
- The hook report in the event journal no longer lists active hooks as inactive.
- Added a test suite over the patch layer, validated against the installed game
  assemblies without launching the game (`pwsh -File build/test.ps1`).

### 0.9.2

Withdrawn — superseded by 0.9.3.

- Fixed: the event journal was only written when the debug overlay was enabled.
  It now runs independently, and its setting moved out of the overlay section.
- The journal is written next to the plugin rather than to a fixed folder name,
  so it works regardless of where the plugin is installed or how the folder is
  capitalised. Write failures name the path.
- Added hook telemetry: each hook reports whether it attached and how often it
  ran, summarised in the journal at raid end.

### 0.9.1

- Fixed: the blood transfusion kit had no effect in raid.
- Hooks that fail to attach are now reported at startup instead of passing
  quietly.
- Server startup logs one summary line; per-module detail moved to debug level.

### 0.9.0

- First public release.

## References

The models are built on published, publicly available work rather than on
hand-tuned game feel. The principal sources:

- **ATLS (Advanced Trauma Life Support)**, American College of Surgeons — the
  classification of hemorrhagic shock into four classes by volume lost, with the
  symptom progression for each. Basis of the blood pressure model.
- **Fackler**, wound ballistics — the permanent crush cavity versus temporary
  stretch cavity distinction and the velocity boundary above which stretch
  becomes destructive. Basis of the wound channel model.
- **AIS (Abbreviated Injury Scale)** and the **Injury Severity Score** that squares
  it — ordinal severities per organ, which is why the zone multipliers are ratios
  of squares rather than of the grades themselves. Basis of the organ severities.
- **Eastridge et al.**, *Journal of Trauma* 73:S431 (2012) — 4,596 combat
  fatalities: the anatomical list of injuries incompatible with life, the split
  between deaths at the moment of wounding and deaths over the following minutes,
  and where on the body the potentially survivable bleeding came from. Basis of
  the fatal-zone list and of the balance the blood model is checked against.
- **Sturdivan, Viano & Champion**, *Journal of Trauma* (2004) — the Blunt
  Criterion and injury-risk curves for blunt and ballistic chest impact; with
  the blunt ballistic impact research from **Wayne State University** (Bir,
  Viano) that validated it. Basis of behind-armor blunt trauma.
- **Clinical literature on behind-armor blunt trauma** in military medicine,
  together with the backface-deformation limits used in armor certification —
  basis of the injury spectrum behind a plate that held.
- **GOST body-armor protection classes** and their certification test rounds —
  basis of the armor penetration thresholds.
- **Ordnance gelatin test data** (the standard 10% tissue simulant) — used to
  calibrate penetration depth.
- **Open-source prototype specifications** — service manuals and public
  reference works for shell loads, pellet counts, grenade fragment mass and
  velocity, and explosive charge weights; plus the cube-root scaling law for
  blast effect.

Where reality is documented, PLATE follows it. Where a value had to be chosen to
fit the game (health pools, time scale), it is a config entry rather than a
hidden constant.
