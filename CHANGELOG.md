# P.L.A.T.E. — Changes vs vanilla

P.L.A.T.E. (Penetration, Lethality, Armor & Trauma Engine) replaces the abstract
damage numbers of the base game with an attempt to reproduce a real physical
model of terminal ballistics, armor interaction and blood loss. Below is an
exhaustive list of what behaves differently from vanilla and the reasoning
behind it. Each section ends with the published work the model rests on, and the
sources are collected under [References](#references) at the end.

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
- The damage and penetration numbers on item cards remain as reference — the
  actual result is always computed from physics at impact.

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

## Armor: a physical barrier, not a dice roll

**Vanilla:** armor class and durability feed a penetration-chance roll, and
penetrating hits lose a flat percentage of damage.

**PLATE:** a plate or soft panel is an obstacle with material properties, and
the projectile has to defeat it with specific energy:

- **Protection classes are anchored to the real GOST protection standard.**
  Class thresholds correspond to what those classes are actually rated to stop.
  Bottom-tier "class 1" junk headwear (construction helmets and the like) is
  fragment protection only — it will not stop a pistol bullet.
- **Materials behave like themselves.** Ceramic offers the highest threshold
  and grinds down even hard AP cores, but cracks in tiles — a follow-up hit
  into the same segment meets rubble. Armor steel is expensive to defeat and
  flattens soft bullets, but its damage zone is local: the "gong" takes dozens
  of hits. UHMWPE lets a penetrating bullet through nearly intact and is easier
  for sharp-nosed AP to slip through; aramid behaves similarly as soft armor.
  Titanium is viscous and bleeds off an exceptional amount of energy even when
  defeated.
- **A penetrating bullet pays for the hole.** It loses energy, may lose mass
  (core erosion), and deforms — what enters the body is a slower, blunter,
  lighter projectile, and the flesh model works from that state. There is no
  separate "mitigation percent".
- **Angle matters.** An oblique hit faces more effective material; steep angles
  push the interaction toward ricochet mechanics.
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

*What this is based on:* shell and grenade figures come from open-source
prototype specifications — service manuals and public reference works — for
charge weights, pellet counts, fragment mass and fragment initial velocity.
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
- **Pack for it.** Bandages and tourniquets are the only thing that closes a
  bleed, painkillers take the edge off what wounds and fractures do to your
  hands, and a blood transfusion kit (sold by Therapist, craftable at the med
  station) is the only way to put volume back in during a raid. Going in light
  on medical is now a real decision, not a slot you skipped.
- Fractures come from actual bone hits with energy behind them, not from a
  damage-number lottery.
- Destroyed body parts cause internal bleeding; nearby explosions can add blast
  barotrauma.
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

- F12 menu holds the gameplay-level settings, including a global **damage
  scale** (from bullet-sponge to instant-kill for the curious). Fine-tuning —
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

## References

The models are built on published, publicly available work rather than on
hand-tuned game feel. The principal sources:

- **ATLS (Advanced Trauma Life Support)**, American College of Surgeons — the
  classification of hemorrhagic shock into four classes by volume lost, with the
  symptom progression for each. Basis of the blood pressure model.
- **Fackler**, wound ballistics — the permanent crush cavity versus temporary
  stretch cavity distinction and the velocity boundary above which stretch
  becomes destructive. Basis of the wound channel model.
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
