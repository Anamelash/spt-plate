# The physical model

This document describes how P.L.A.T.E. computes what it computes. It is written
for people who want to check the reasoning, argue with it, or retune it — not for
players deciding whether to install the mod. For that, read the
[changelog](../CHANGELOG.md).

Every constant named here lives in a config file and can be changed: server-side
values in `config.jsonc` next to the server mod, client-side ones in the F12 menu
or in `BepInEx/config/com.anamelash.plate.cfg`. Numbers in the text are the
shipped defaults, not hardcoded truths.

## Contents

- [Principle](#principle)
- [Notation](#notation)
- [Wound channel](#wound-channel)
- [Path through the body](#path-through-the-body)
- [Overpenetration and fragments](#overpenetration-and-fragments)
- [Penetration](#penetration)
- [Armor](#armor)
- [Behind-armor blunt trauma](#behind-armor-blunt-trauma)
- [Blood and trauma](#blood-and-trauma)
- [Ammunition normalization](#ammunition-normalization)
- [Calibration](#calibration)
- [What is deliberately not modelled](#what-is-deliberately-not-modelled)
- [Sources](#sources)

## Principle

Vanilla stores damage and penetration as per-cartridge constants and degrades
them by distance. PLATE treats those numbers as display values only. Everything
that matters is derived, at the moment of impact, from the projectile's physical
state and the geometry of what it hits.

The state carried through the whole pipeline is four values: mass `m`, diameter
`d`, velocity at impact `v`, and a deformable fraction `X`. Armor does not
"reduce damage" — it consumes energy, strips the projectile down to its core and
hands a modified state to the flesh model. The same four values then decide
the wound, the penetration, and what continues out the far side.

This is why the mod is sensitive to distance, barrel length and armor in ways
vanilla is not: they all act on the state rather than on a lookup.

## Notation

| Symbol | Meaning | Unit |
|---|---|---|
| `m` | projectile mass | g |
| `d` | diameter | mm |
| `A` | cross-section, `πd²/4` | mm² |
| `v` | velocity at impact | m/s |
| `E` | kinetic energy, `½mv²` | J |
| `X` | deformable fraction, 0 = solid AP, 1 = fully expanding | — |
| `L` | full wound channel length the projectile could cut | mm |
| `T` | path actually available inside the body part (the chord) | mm |
| `U` | specific energy on the impact area, `E/A` | J/mm² |
| `CoreAreaFrac` | hard core's frontal area as a fraction of the bullet's | — |
| `CoreMassFrac` | hard core's mass as a fraction of the bullet's | — |

## Wound channel

Damage is the sum of two mechanisms plus an energy ceiling.

### Depth

A projectile decelerating in tissue is dominated by quadratic drag, `F = ½ρC_dAv²`,
which gives exponential velocity decay and therefore a logarithmic penetration
depth:

```
L = GelDepthK · (m/A) · ln(v / v_stop) · (1 − ExpansionDepthFactor · X)
```

`m/A` is sectional density — the reason a heavy narrow bullet outreaches a light
wide one at the same energy. `v_stop` (default 50 m/s) is the speed below which
tissue is no longer being cut. Expansion shortens the channel and widens it,
which is the whole trade an expanding bullet makes.

A model linear in velocity was tried first and gave rifle rounds over two metres
of gelatin against a real ~0.7 m. The logarithmic form is not a curve fit, it
falls out of the drag law.

### Permanent cavity

Crushed tissue along the channel, proportional to the volume actually swept:

```
PC = A · (1 + ExpansionAreaFactor · X) · min(L, T) / WoundVolumePerHp
```

`min(L, T)` holds whether the projectile exits or stops inside: a bullet caught
by bone still only wounds the tissue it passed through, and no channel can be
longer than the body in front of it.

### Temporary cavity

Tissue is elastic and survives being stretched slowly. Stretch only becomes
destructive above the classic high-velocity wound boundary, so the term is gated
by a sigmoid in impact velocity rather than switched on at a threshold:

```
eff(v) = 1 / (1 + exp(−(v − TcVelocityCenter) / TcVelocityWidth))
TC      = eff(v) · E · φ · (1 + TcFragBonus · frag) / TcEnergyPerHp
```

`φ` is the share of the projectile's energy left in this body part. The share of
the *path* is not the share of the *energy*: the same quadratic drag that gives
the logarithmic depth gives `v(s) = v · exp(−s/λ)` with `λ = GelDepthK · (m/A) ·
(1 − ExpansionDepthFactor · X)`, so energy falls off twice as fast as velocity
and most of it is spent in the first hand's width of tissue.

```
φ = 1 − exp(−2 · min(L,T) / λ)     projectile exits the part
φ = 1                              projectile stops in it (bone, lodged)
```

A rifle bullet crossing a 250 mm chest leaves about 80% of its energy behind, not
the 25% its share of the channel would suggest. `frag` is the round's
fragmentation chance: fragmentation converts stretching into tearing, so the
tissue no longer springs back.

### Ceiling and the contact case

```
W = min(PC + TC, E / EnergyCapPerHp)
```

You cannot destroy more tissue than the energy delivered. The cap is what keeps
slow buckshot and light birdshot honest.

Below `v_stop` there is no channel at all; the remaining energy becomes a contact
bruise worth `E / EnergyCapPerHp`. Without this branch the logarithm goes
negative and the model produces nonsense at the low end.

### Vital zones

The result is multiplied by a zone factor taken from the collider that was hit,
not from the body part: brain zones (eyes, parietal, back of head, ears) carry the
largest multiplier, neck is next, jaw is grievous but survivable. The head is
several distinct colliders in EFT, and treating them as one is what makes helmet
and head hits feel arbitrary.

## Path through the body

Damage depends on how much body the projectile actually crossed, so a graze must
not be a torso hit.

EFT's hitboxes are a mix of volumes and thin plates — measured in game, `SpineTop`
is about 17 mm thick and `SideChestUp` about 11 mm, while `HeadCommon` is a sphere
of 73 mm radius. Using a single collider's thickness therefore gives absurd
results depending on which plate the ray happened to enter.

Instead the body is treated as solid between its outer surfaces. From the actual
entry point the model casts back through **every** collider belonging to the same
body part and takes the farthest exit surface:

```
T = distance from entry point to the farthest exit among all colliders of that body part
```

If the back-ray misses — a tangential clip — the chord falls back to a minimum of
two calibers rather than to a table of thicknesses: a graze should be a graze.

## Overpenetration and fragments

Whether a projectile exits is decided by physics, not by a penetration rating:

```
exit ⟺ L > T and the projectile was not stopped by bone
```

The bone roll is shared with the fracture roll, so a bullet stopped by a femur and
a femur fracture are the same event rather than two independent dice.

A projectile that exits leaves with the velocity the drag law gives it:

```
v_out = v · exp(−T / λ),   λ = L / ln(v / v_stop)
```

and whatever it hits next is evaluated from that state. Nothing is zeroed by
game-logic special cases.

Fragmentation splits the parent's mass rather than inventing damage: each fragment
carries `share/n` of the mass, its diameter follows from the cube root of that
mass, and it is then an ordinary projectile with its own state. Fragments too
small to matter deposit their energy locally instead of spawning. Total damage
therefore stays inside the parent bullet's energy budget.

## Penetration

Penetration is rescaled from the template value by the ratio of energy densities
between the actual impact and the cartridge's own muzzle conditions:

```
pen = pen_template · (m·v²/d²)_impact / (m·v²/d²)_muzzle
```

This keeps the relative ordering of cartridges that BSG established while making
the number respond to range, barrel length and anything the projectile has been
through. It is stateless — computed per forward hit, never accumulated.

## Armor

Armor is an obstacle with material properties, and the projectile has to defeat it
with specific energy.

### Threshold

```
A_hit   = A · CoreAreaFrac · (1 + ExpansionOnArmor · X)
U_hit   = E / A_hit
U_limit = ClassULimit(class) · ULimitMult(material) · durability / max(cos θ, AngleMinCos)
```

Two things move the area the energy lands on, and they pull opposite ways.

The plate meets the hard core, not the calibre: a 5.5 mm tungsten-carbide core in a
7.85 mm bullet arrives at twice the energy density that the same energy spread over
the full jacket would. That is where armor piercing comes from, and it is read from
the round's published construction rather than from a multiplier keyed off how soft
the bullet is.

And a bullet that can deform flattens against the face of the panel before it has
finished loading it, so the same energy lands on more of the plate. This is why a
hollow point is poor against armor whatever energy it carries, and it is the reason
the two terms are separate: a soft bullet with a hard core — an M855A1 — does both.

Class thresholds are anchored to the GOST protection classes: each class is rated
against a specific test cartridge, so the threshold is that cartridge's specific
energy rather than an invented number.

An oblique hit presents more material along the path, hence the `1/cos θ` term,
floored so that extreme angles hand over to ricochet mechanics instead of
producing infinities.

Fibrous armor gets an extra reduction against sharp-nosed cores, which slip
between fibres rather than loading them:

```
U_limit ← U_limit · (1 − SharpVulnMult · clamp01((0.5 − X) · 2))
```

### The band

Real panels are not uniform, and a hard threshold produces a step nobody believes.
Penetration is probabilistic in a band around the threshold:

```
ratio  = U_hit / U_limit
P(pierce) = clamp01( (ratio − (1 − band)) / (2·band) )
```

Outside the band the outcome is deterministic.

### The price of the hole

A projectile that defeats the panel pays for it:

```
E_cost = ECostMult · U_limit · A_hit              work ∝ strength × hole × thickness
v_out  = √(2·(E − E_cost) / m)                   the whole bullet decelerated as one
m_out  = m · CoreMassFrac · (1 − KFrag)          jacket stripped, then eroded
d_out  = d · √CoreAreaFrac                       what carries on is the core
X_base = clamp01((X − (1 − CoreMassFrac)) / CoreMassFrac)
X_out  = min(1, X_base · (1 + KDef))             flattening of what is left
```

What enters the body is slower, narrower and lighter, and the flesh model works
from that state. There is no separate "mitigation percentage" anywhere.

Three things follow from the core being what carries on. The hole is core-sized, so
it costs less to make. The jacket stops in that hole, and the energy it was still
carrying stays in the panel rather than disappearing. And the deformable material
goes with the jacket first, so a round that sheds one comes out **harder** than it
went in — an M855 that arrives as 0.65 g of steel penetrator is not a soft bullet
any more. `KDef` and `KFrag` are properties of the barrier: ceramic grinds a core
down and aramid does not.

### Local damage and wear

Armor remembers where it has been hit. Around each impact, within a
material-specific radius `DAreaMm`, the local threshold is multiplied by
`DegradeMult` per prior hit, with a floor:

```
U_limit ← U_limit · max(DegradeMult^n, DegradeFloor)
```

This is what separates materials in play. Ceramic has the highest threshold, the
smallest `DegradeMult` and the widest damage radius — it cracks in tiles, and the
second hit into the same tile meets rubble. Armor steel has a narrow radius and a
high multiplier: the gong takes dozens of hits. UHMWPE and aramid sit between,
with the sharp-core vulnerability above.

Durability loss is driven by absorbed energy rather than by hit count:

```
Δdurability = E_absorbed / JPerDurability
```

where a blocked hit absorbs the full energy and a penetration absorbs only
`E_cost`. Brittle materials wear out in a few stops, steel lasts.

## Behind-armor blunt trauma

A panel that stops a bullet still delivers momentum. The severity predictor is the
Blunt Criterion:

```
BC = ln( E_bfd / ( W^(1/3) · T_wall · D ) )
```

with `E_bfd` the energy reaching the body, `W` body mass, `T_wall` chest wall
thickness and `D` the effective distribution diameter — small for soft armor,
large for a steel plate, which is why the same energy behind steel is a bruise and
behind a soft panel is a broken rib.

The response is piecewise: a plateau at low `BC` (the plate held, it hurt, nothing
more), a rising branch where the probability of internal bleeding follows a
logistic in `BC`, and a severe branch with lung or heart contusion, guaranteed
internal bleeding and a long stamina penalty. Vanilla blunt damage is disabled
when this is active — otherwise the same hit is paid for twice.

## Blood and trauma

### Volume and thresholds

Blood volume is a separate resource, not a health pool. Total volume follows the
standard estimate per body mass (~70 ml/kg, default 5000 ml), and the stages are
the four **ATLS** classes of hemorrhagic shock, expressed as remaining fraction:

| Remaining | ATLS class | In game |
|---|---|---|
| ≤ 85% | II | tachycardia, stamina penalty |
| ≤ 70% | III | tremor, tunnel vision, heavy stamina penalty, damage vulnerability |
| ≤ 60% | IV | pre-coma: cyclic stun, desaturation, no sprint or jump |
| ≤ 50% | — | death |

The health tab's blood pressure bar is this scale, normalised so that 0% is the
death threshold.

Bleedings do not damage HP at all. They move blood.

### Flow

Rates are per body part and per bleeding type, and flow is not constant — falling
pressure and vasoconstriction limit it:

```
Q(t) = Q₀ · (V / V_max)^β        β ≈ 1.5
```

Total loss per frame is capped by cardiac output (~5 l/min): no number of wounds
can drain blood faster than the heart moves it.

Vanilla bleeding effects are reused rather than replaced, which keeps icons,
medicine, bot reactions and kill attribution working. The one custom state is
internal bleeding, which by design has no icon and no field treatment. It can be
switched off per faction.

**Internal means into a cavity, not merely untreatable.** The distinction decides
which wounds qualify, and it is the anatomy that decides, not the severity:

| wound | where the blood goes | in game |
|---|---|---|
| destroyed abdomen | peritoneal cavity, aorta / vena cava / iliacs | internal, permanent |
| BABT under a plate or helmet | thoracic cavity, cranium | internal, permanent |
| blast barotrauma | lungs, gut | internal, permanent |
| destroyed limb | out of the hole, femoral or brachial | heavy external, treatable |

A destroyed leg is the textbook indication for a tourniquet, and a junctional or
awkward one is what a hemostatic is carried for — both are in the game, so that
wound is a heavy external bleed. Making it permanent instead put a limb wound
beyond the reach of the exact items that exist to treat it, and double-counted
the femoral artery, which the rate table already covers on its own.

Internal bleeds are stored one entry per causing hit, with the zone that opened
them, rather than summed into a single figure — the journal has to be able to
answer where the blood is going, which a running total cannot.

**Open: intracranial pressure.** Behind-helmet trauma really does cause bleeding
inside the skull, so the head belongs on the list. Modelling it as a volume drain
is nonetheless wrong in kind: an intracranial bleed kills by rising pressure in a
closed box at 100–150 ml, not by hypovolemia over litres. Left as a drain for
now; a proper ICP state with its own timeline and blackout is a TODO.

### Wounds

Any penetrating wound above a damage threshold bleeds, bypassing the vanilla
probability roll — a hole in you is a hole blood comes out of. Heavy bleeding
keeps its own roll on top. Fractures come from actual bone hits with energy behind
them rather than from a damage-number lottery.

## Ammunition normalization

Every round in the database, including rounds added by other mods, is re-derived
from its physical data at server start.

### What the number on the card means

A hit has no single damage value: the same round does 110 or 235 depending on how
much of the body it crosses. The card therefore quotes one defined test rather
than an average — a **perpendicular hit into the centre of the chest of a gelatin
manikin at 5 m**: 250 mm of tissue, the anteroposterior chest depth of an adult
male, at muzzle velocity, with no oblique lengthening of the path. Everything
else in the model is the same code as in a raid, and 250 mm sits close to the
median chord a torso hit actually produces, so the card reads as a typical
centre-mass hit rather than a best case.

The depth is `BodyDepthMm` in the server config. It only ever affects the
displayed value and the fallback damage when the physics model is off; a real hit
deposits along the collider chord it actually crosses.

### Bullet construction

Three numbers describe what a projectile is made of, because one cannot describe
both a jacketed lead ball and a tungsten dart:

| | meaning | example |
|---|---|---|
| `X` | deformable fraction | M80 FMJ 0.25, soft point 0.70, hollow point 0.90 |
| `CoreAreaFrac` | core frontal area / bullet frontal area | M993: 5.5 mm in 7.85 → 0.49 |
| `CoreMassFrac` | core mass / bullet mass | M993: 91 gr of 128 → 0.71 |

They come from the reference book, keyed by the cartridge's own name — a bullet is
the same bullet in every pack it ships in, and a statistic taken over whatever
cohort an install happens to have gave clones of one cartridge different physics.

A core is only recorded when its mass or diameter is published, and an **area**
fraction only when the core is hard enough to keep its shape against a plate. That
line runs between the M855 and the M855A1: same case, same 62 grains, same calibre,
but 40 HRC of steel tip upsets on the face of the panel and 58 HRC does not.

For cartridges the book does not name — modded ammunition, mostly — `X` is inferred
as a percentile blend within the caliber cohort (specific damage positive, specific
penetration negative, fragmentation chance), and the core is read off how far the
round's penetration sits above what its energy density buys. At the cohort median
that comes out monolithic, which is the truth for most of them. Cohorts smaller
than a threshold fall back to a global regression.

### Buckshot

Pellet mass is derived from lead density and nominal pellet diameter, and pellet
count from charge mass divided by pellet mass. Vanilla puts eight pellets into
almost everything, which under-loads small buckshot by a factor of two to four.
Flechettes are steel and keep their shape, so they take a low `X`: deep, narrow,
poor at wounding.

### Grenades

Fragment mass and initial velocity come from open-source prototype specifications;
the diameter used by the wound model is that of the equivalent steel sphere,
`d = (6m / ρπ)^(1/3)`. Blast strength scales from the actual explosive charge by
the cube-root law:

```
Strength = Strength_anchor · (charge / charge_anchor)^(1/3)
```

Fragment count is left alone for performance reasons, so a grenade's lethality
comes from fragment physics rather than fragment quantity.

## Calibration

The free constants were fixed against a small number of anchors rather than tuned
to feel:

- channel depth: 9 mm FMJ reaching roughly half a metre of ordnance gelatin
- permanent cavity per HP: 9×19 PST landing near its vanilla damage
- temporary cavity per HP: 7.62×39 PS landing near its vanilla damage
- penetration scale: M61, M995 and PS mapping close to their vanilla ratings
- armor classes: the specific energy of each GOST class's test cartridge
- blast: one reference grenade's vanilla strength against its real charge

Anchoring to vanilla for two cartridges is deliberate. The model is meant to
change the *shape* of the damage curve — how it responds to distance, armor and
geometry — not to make every number unrecognisable.

## What is deliberately not modelled

- **Organs.** The game exposes only BSG's hitboxes, and there is no heart or lung
  among them. Zone multipliers over the colliders that do exist are as close as a
  mod can get.
- **Yaw and tumbling.** Real wound channels depend heavily on when a bullet yaws.
  There is no data per cartridge to drive it and no way to observe it in game, so
  expansiveness stands in for the whole family of "does it stay point-forward".
- **Bone geometry.** Bone is a probability per collider scaled by energy, not a
  skeleton.
- **Ricochet angles** beyond the floor on the cosine term; vanilla handles the
  bounce itself.

## Sources

- **ATLS (Advanced Trauma Life Support)**, American College of Surgeons — the four
  classes of hemorrhagic shock and their symptom progression.
- **Fackler**, wound ballistics — permanent versus temporary cavity, and the
  velocity boundary above which stretch becomes destructive.
- **Sturdivan, Viano & Champion**, *Journal of Trauma* (2004) — the Blunt Criterion
  and injury-risk curves for blunt ballistic chest impact; validated in blunt
  impact research at **Wayne State University** (Bir, Viano).
- **Clinical literature on behind-armor blunt trauma** in military medicine, with
  the backface-deformation limits used in armor certification.
- **GOST body-armor protection classes** and their certification test cartridges.
- **Ordnance gelatin test data** (10% tissue simulant) for penetration depth.
- **Open-source prototype specifications** for shell loads, pellet counts, grenade
  fragment mass and velocity, and explosive charge weights; plus the cube-root
  scaling law for blast.
