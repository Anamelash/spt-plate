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
- [Organs](#organs)
- [Spread](#spread)
- [Path through the body](#path-through-the-body)
- [Overpenetration and fragments](#overpenetration-and-fragments)
- [Penetration](#penetration)
- [Armor](#armor)
- [Behind-armor blunt trauma](#behind-armor-blunt-trauma)
- [Environment barriers](#environment-barriers)
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

Crushed tissue along the channel, proportional to the volume actually swept. The
channel is not one width from end to end: a projectile enters point-forward and,
if it is long enough and travels far enough, turns and goes on broadside.

```
A_nose = A · (1 + ExpansionAreaFactor · X)          expansion, from the moment of entry
A_side = max(A_nose, YawBroadsideFraction · L_b · d)  after the turn
PC     = (A_nose · min(P, N) + A_side · max(P − N, 0)) / WoundVolumePerHp
```

`P = min(L, T)` is the path actually travelled in this body part, and it holds
whether the projectile exits or stops inside: a bullet caught by bone still only
wounds the tissue it passed through.

`N` is the travel before the turn — `YawNeckCalibres · d` as a median, drawn
log-normally per shot in a raid (see [Spread](#spread)). `L_b` is the
projectile's length. See [Bullet length](#bullet-length) for where it comes from.

Two things fall out of the geometry rather than being written down. A round ball
comes out the same area whichever way it faces — the square around a circle is
1.27 times its area and a tumbling projectile averages three quarters of its
widest face — so buckshot has no broadside to turn into. And a fully expanded
bullet is short and blunt, so `A_side` never exceeds `A_nose` for it either.

### Bullet length

`L_b` is read twice — as the width of the channel past the turn, above, and as the
lever arm a barrier tips a projectile over with
([Yaw](#yaw-and-why-the-second-wall-costs-more-than-the-first)) — and no game
template carries it. Two sources, in this order.

**Published, when there is one.** The reference book may carry `LengthMm` per
cartridge, the same idiom as its `MassG` override and for the same class of
reason: what somebody measured outranks what the model works out. The server
reads it while normalizing, bakes the card's Damage through it, and publishes it
to the client per cartridge in `/plate/ammo-data` (`L`), so both halves compute
the same channel from the same number.

**Inferred, otherwise** — from the one thing always known about a bullet, how
much mass sits behind its calibre:

```
L_b = m / (A · ρ · f)
```

with `ρ` the mean density of a jacketed bullet and `f` how much of its bounding
cylinder it fills once the ogive and boat tail are taken out. That puts 7.62×51
M80 at 28.8 mm against a measured 28.9 and 5.56×45 M855 at 23.0 against 23.0.

The inference's limit is one density for every bullet on earth. That density is a
lead one, and a steel core is lighter for the same volume, so a steel-cored round
reads short: 5.45×39 7N6 at 20.4 mm against a measured 24.8, and 9×19 7N31 —
steel under an aluminium jacket, the lightest construction in the book — at 9.4 mm
against 13, which is **shorter than its own calibre**. At `L/d ≈ 1.05` the yaw
term calls that bullet a sphere and no barrier can ever turn it, while the raid
journal shows it keyholing. That is what the published field is for.

The rejected alternative was a mean density derived from the core fractions. Only
two dozen cartridges publish a core geometry; for everything else those fractions
are themselves inferred, and a density built on them would be a guess resting on a
guess. A measured length is a measured length.

Coverage is therefore **deliberately partial**, and what is not covered stays
inferred at the accuracy above — right to a fraction of a millimetre for a
lead-cored bullet, up to ~20% short for a steel-cored rifle round and ~30% short
for the extreme pistol case. Published entries at the time of writing: 7.62×51
M80 (28.9), 5.56×45 M855 (23.0), 5.45×39 7N6 (24.8), 7.62×39 PS (26.8), 9×19
7N31 (13.0).

One consequence is worth stating: a published length does not shrink when a
barrier strips a bullet's jacket, where the inference would have shrunk it with
the mass. That is the better of the two errors — a jacket-stripped core keeps
roughly the length of the bullet it came out of, and it is the mass-scaled
inference that would wrongly make it stubby — but neither is a measurement of a
deformed projectile, and nothing here models one.

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
the 25% its share of the channel would suggest.

`frag` converts stretching into tearing — fragments puncture the stretched wall
of the cavity so the tissue no longer springs back — and it is **derived, not
read from the vanilla FragmentationChance field**. A bullet breaks up where it
turns broadside, because that is where the envelope takes the full load; it only
breaks if it is still fast enough there; and only the deformable share breaks —
a hard core never does:

```
v_neck = v · exp(−neck / λ)
frag   = X · (1 − CoreMassFrac)     if the turn comes inside the body
                                    and v_neck > FragVelocityThreshold
       = 0                          otherwise
```

The threshold sits at the bottom of the published 600–700 m/s band for
thin-jacketed ball, read at the tumble point rather than at impact. That one
rule reproduces the gelatin literature without a per-cartridge opinion: M193 and
M855 arrive at their turn above 700 m/s and shed their lead; the 7.62×39 PS has
already slowed to under 500 by its turn and comes through whole; a monolith has
nothing to shed; no pistol round is ever fast enough.

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

## Organs

There is no heart among BSG's hitboxes, but there are boxes in the right places,
and an organ is a share of one. The middle third of `RibcageUp` is the heart and
the mediastinum behind it; the right third of `RibcageLow` is the liver; a thin
`SpineTop` or `SpineDown` collider is the cord. Lungs get no zone of their own,
because nearly the whole ribcage is lung and a multiplier on everything is a
multiplier on nothing — in the AIS table one lobe of lung *is* the reference the
other zones are scored against, so it is already there at 1.0.

Which local axis of a box runs across the body is not a constant, so it is
resolved at the moment of a hit: the axis nearest the character's own up is
height, the one nearest their right is width, the remainder is depth. The same
comparison gives the sign, which is what keeps the liver on the body's own right
when the target turns around. `SpineTop` is two boxes under one name — a 17 mm
plate and half a metre of upper back — and the thickness tells them apart.

**Direct hit.** What decides it is how far the channel ran *inside* the zone
against how deep the zone is:

```
path_in_zone > depth_zone / 2
```

That is the distinction the trauma scales draw: a tangential wound of the
myocardium that does not breach the endocardium is survivable, a perforated
ventricle is not. The cord is the exception — 13–17 mm of collider has no
half-depth to be short of, so anything that comes out the far side has been
through it.

**Cavity reach.** The channel can miss an organ and the stretch still reach it.
The cavity radius comes from the energy given up per unit length:

```
dE/dx = π · r² · σ
```

One constant, tied to published gelatin diameters: 7.62×51 lands near 60 mm of
radius and 9×19 near 30. Overlap is how far that reaches into the zone,
normalised on the narrowest way across it — the cord is 226 mm wide and 17 mm
thick, and it is the 17 that a cavity has to sweep to engulf it.

**Severity** comes from the ratio of AIS squares against one lobe of lung: heart
and mediastinum 2.8, cord 2.3, liver 1.8. Squares, because AIS is an ordinal
scale and two moderate wounds must stay lighter than one severe.

**Death is dealt as damage**, never as a scripted kill. A fatal zone raises the
hit to what the body part had left — a floor, not a replacement, so a category
multiplier cannot save a pierced heart and the ordinary calculation still wins
where it is larger. Everything downstream then works by itself: the kill is
attributed to the shot, and the kill feed, the statistics and other mods all see
an ordinary hit. The game only dies of head or thorax, so a fatal wound in the
abdominal pool reaches the chest as a second damage event of its own — which is
anatomically the honest reading anyway, since a severed cord or a torn vena cava
kills the brain that stops being supplied, not the abdomen.

**Rolls.** A cavity that passed beside the heart can still stop it, and the liver
can be torn off its ligaments; neither is certain, and both need rifle velocities
— they are scaled by the same high-velocity sigmoid the temporary cavity uses, so
a pistol practically never does either. One organ is several collider boxes, so
the draw is made once per shot per organ and re-tested against each meeting: that
comes out at the best chance the shot ever had, rather than at two rolls or at
whichever box came first.

## Spread

Two identical-looking hits differ, and the difference has to come from where it
comes from in reality rather than from a multiplier of `N(1, σ)` on the result.
Everything here is drawn once per projectile and carried through overpenetration
children with the travelled distance taken off, so a bullet that turned in an arm
arrives at the chest already sideways.

- **Where it turns** — log-normal about the cartridge's median. Log-normal
  because a neck cannot be negative and its published spread is multiplicative:
  one cartridge's neck length varies twofold in gelatin. This is the single most
  variable quantity in wound ballistics, and it is why the same round behaves
  differently in an arm and in a chest — not because a die was rolled, but
  because the channel is a different length.
- **Tissue** — ribs, cartilage and diaphragm are not gelatin, so the channel
  length and the energy left behind move together by ±15%.
- **Where the organs are** — the game has one skeleton and people do not, so the
  zones shift sideways per shot. That turns "hit the heart" from a step at the
  edge of a box into a gradient.

Armour is deliberately outside all of this. It has its own probability band
around the ballistic limit, and mixing the two would leave the certification
tests meaning nothing.

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
therefore stays inside the parent bullet's energy budget. Where in the part the
bullet broke up is not knowable, so a fragment is priced against half the remaining
chord — the midpoint is the only answer that does not invent one.

**Both of these are handed over at the moment the child is created**, for the reason
set out under "Environment barriers": the engine builds a projectile's entire
predicted trajectory when it is born, from the arguments it is born with, and
overwrites its velocity out of that table on every tick afterwards. A speed written
into a child after the fact survives until that child's first tick. Inside a person
this hid well — the next collider is usually met within that same tick, and the
impact interpolation returns almost all of it — and at the far end of a
through-and-through it was total: a bullet that had crossed a torso arrived at the
next thing it hit having paid nothing for the first. So the exit speed goes into the
spawn arguments, and for a fragment so do its mass and calibre, which additionally
gives the engine's own drag the fragment's sectional density instead of the parent
bullet's. A projectile that does not get out, and a fragment under the mass floor,
are launched at a speed that drops them where they were born rather than at zero: a
spawn with no speed has no direction either, and the whole trajectory is built from
direction × speed.

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

### The ballistic limit

Where the item resolves to a real thickness and a real material — which is every
piece of armor the reference book covers — the question is not whether a specific
energy clears a threshold. It is whether the projectile is faster than the speed
at which that plate stops it:

```
W    = k(failure mode) · S · geometry · packing · (H_plate / H_core)
v_bl = √(2W / m_core)
```

`S` is the strength that resists this projectile, and which one that is depends on
how the barrier fails: a ductile metal shears, a ceramic crushes, a fiber panel
stretches (`σ_fibre · ε_fibre`). The geometry follows the same split — punching a
hole through a solid means shearing its perimeter through its own thickness, so
`π·d·T²`, while a fiber pack has no hole to punch and each layer takes its share
over the core's face, so `π·d²/4·T`. Only the fiber in a pack works, so a sewn
package is scaled by how much of it is fiber rather than air.

A ductile metal has two ways to give way, and **which one applies is a property
of the alloy, carried as data (`FailureMode`), never an outcome of arithmetic**:

```
ShearPlugging:  W = K_d · S_shear · π·d·T²        v_bl ∝ T
HoleExpansion:  W = K_h · σ_y · π·d²/4 · T        v_bl ∝ √T
```

What decides the mode is the alloy's **strain-hardening reserve**, read as UTS
over yield. Plugging is adiabatic shear: it needs the deformation to localise
into a band, and an alloy whose hardening is exhausted — quenched, aged,
cold-worked — lets it. RHA (1000/900 = 1.11), Ti-6Al-4V (1.08), AR500 (1.32) and
6082-T651 (1.12) all plug, and the 6082 obliquity trial confirms the plug law's
`sec θ` scaling to 1.5%. An alloy with reserve left hardens wherever a band
tries to start, so no band forms and the material flows aside instead:
structural mild steel at 450/250 = 1.8 is the corpus's one flowing metal. That
is also why 6 mm of mild steel stops "7.62 AP" nearly as well as 6 mm of RHA in
the papers (304.5 against 320 m/s): the flow path it forces on the core is the
expensive one, and no cost-based rule could ever say so — an earlier version of
this model chose the mechanism by `min()` over the two works, and the mild
ladder refuted it from its thinnest point.

The split pays twice. `K_h` is derived from the mild ladder alone (all seven
points, geometric mean; no hardness factor in the branch — flow resistance is
the plate's yield stress, already in the term) and `K_d` from the RHA ladder
alone, and the two calibrations cannot reach each other. Every mild point then
lands inside its own band, and the RHA-over-mild ratios come out at 0.90–0.99 of
the published ones with no shared constant behind them. `K_h = 6.6` also has a
physical address: cavity-expansion theory prices opening a hole at 3–5 times the
yield stress over its volume.

**What remains open, recorded rather than fitted away.** The per-row solutions
for `K_h` hold at 5.6–6.4 from 4.7 to 16 mm and rise to 8.4–9.0 at 20–25 mm:
past `T/d ≈ 2.6` the flow is *confined* — deep cavity expansion costs more than
thin-plate flow — and one constant cannot carry both regimes. That is the mild
shape test's remaining red, deeper than any wearable plate, closable by a
confinement term with data behind it.

Measured against the two ladders, model over published:

| `T/d` | 0.62 | 0.79 | 1.05 | 1.31 | 1.57 | 1.84 | 2.10 | 2.62 | 3.28 |
|---|---|---|---|---|---|---|---|---|---|
| plug (RHA) | | 0.98 | 0.98 | 1.01 | 1.01 | 1.01 | 1.01 | | |
| flow (mild) | 1.07 | 1.09 | | 1.07 | 1.05 | | 1.02 | **0.89** | **0.86** |

Two things follow, and the second bounds the first. The flow law drifts steadily
from +9% to −14% and breaks between 2.10 and 2.62, which is the confinement
above. But the plug law holds to 3.8% across its whole ladder — so a plate that
plugs is not affected by any of this, and no wearable steel or titanium plate is
in the flow regime at all. The open shape problem is real and it is not what
decides whether armour in this game stops a bullet.

**The hardness term is a decision before it is a curve: what happened to the
core.** One clamped power of the plate-over-core hardness ratio used to do every
job at once, and three vest passports refuted it with a sign pattern no ratio
curve can produce. The same titanium vest (6B3TM) was under-credited against the
mild PS core — its passport holds that round, and the model needed a factor above
1 at a ratio below 1, which a power of the ratio cannot say — and over-credited
against lead, whose SVD round the same passport sends through the chest. The 6B23
was under-credited against tungsten carbide: a factor of 0.41 demanded at a ratio
where the RHA ladder pins 0.32, so the term cannot even be monotone in the ratio.
Three fates, each with its own physics:

- **Rigid** — a quenched core (above `DeformCoreMaxHv`, ≈45 HRC) or any core the
  plate cannot stress to its yield. The contest is whether the plate's shear band
  or the core gives way first: `clamp((HV_p/HV_c)^1.96, 0.30, 2.08)`, exponent,
  floor, ceiling and the RHA-ladder derivation unchanged.
- **Deformed** — a softer core dies on the face when the contact stress reaches
  its own strength: Taylor's rigidity criterion in two-material form,
  `½ρ_plate·v² + q·Y_plate ≥ Y_core`, with `Y = 3.27·HV` (Tabor's constraint
  factor, pinned by 44S's published pair: 613 HV against 2000–2100 MPa yield) and
  `q = DeformPlateSupport` the plate's supported share. Velocity is in the
  criterion and the corpus shows it belongs there: the same construction-steel
  family is rigid at 335 m/s out of a PM and dead at 720 out of an AKM. A dead
  core loads the plate as spread mass over more than its own calibre:
  `clamp((HV_p/HV_c)^0.33, 1.05, 2.08)` — never below `DeformFloor`, growing
  shallowly, capped by the same ceiling the AR500 certificate pins.
- **Shattered** — a brittle core (above `BrittleCoreMinHv`; carbides and
  ceramics, no steel) cracks on a face hard enough, `HV_p/HV_c ≥ ShatterRatio`,
  and rubble is spread mass exactly as a mushroomed slug is: the dead-core factor
  covers both deaths. The 6B23 certificate is the anchor — 613 HV of 44S turning
  back the 7N24's 1300 HV VK-8 — and titanium at a ratio of 0.27 stays under it,
  which keeps the 7N24 the titanium-killer nothing in the corpus contradicts.

What the rework bought, on the evidence that demanded it: all three vest-passport
gates hold (the 6B23 stops the 7N24 it is certified against with no recorded
shortfall left; the 6B3TM holds the mild PS and is pierced by the SVD, both per
its passport), and nothing else in the corpus moved — the RHA ladder, the pistol
rungs, the AR500 anchor and every certified product read exactly as before.

**The fibre mode, and the two datasets that disagree about it.** Fibre used to be
the one failure mode with no published ladder at all — its constant came off two
certificates, and nothing measured whether the *law* was right. It has one now:
ten para-aramid points, two constructions of one Twaron fibre, shot to STANAG 2920
with the 1.10 g .22 FSP (Kośla et al., *Materials* 2022, 15(6), 2314). Each point
publishes an areal density as well as a thickness, so the packing fraction the
model needs is measured rather than assumed — the sewn packs come out at 0.48 fibre
by volume and the pressed laminate at 0.61.

What the ladder says:

- The woven ladder's **shape holds**: the model tracks it to a spread of 1.09
  across 3.6–10 mm, which is the first evidence that `π·d²/4·T` is the right form
  for a pack at all.
- The laminate ladder's shape **does not**: its error climbs from 1.06 at 2.2 mm to
  1.25 at 6.8 mm. Part of that may be packing rather than thickness — its last
  point is 17% denser than the rest of its own ladder — and a laminate ladder at
  constant packing would separate the two.
- The constant it derives is **23.1 against the 27.5 the certificates demand**.
  That direction is the finding. A certificate is one-sided — the plate stopped the
  round, so the limit is *at least* the test velocity — so 27.5 is a floor, and a
  floor sitting above a two-sided measurement means the model is short of work
  somewhere. Moving `FibrousK` onto the ladder was tried: it puts a dozen certified
  plates below their own test velocity, which is not a recalibration but a model
  claiming real armour does not work.

So the constant stays where the certificates put it, the ladder rows carry the miss
in the open (two of the ten are red), and what closes it is a thickness law that
fits both — thin fragments and 21–33 mm bullet-rated plates — not a number chosen
between them. The other half of the same evidence is that fibre still reads the
small fast bullet and the big slow one in the wrong order (M193 against M80), which
is about the `d²` area law rather than the thickness law, and which this ladder
cannot settle because it is one projectile.

**Obliquity, measured.** The `1/cos θ` path length is not a small assumption: for a
plate that fails by plugging it says `v_bl` rises *exactly* as `sec θ`, that the
gain is the same for steel, aluminium and titanium alike (the material cancels),
and that a fibre pack gains only `√sec θ`. It was the last input in the armour model
with nothing published behind it, and it moves outcomes harder than any constant
here — a raid log had one vest reading `v_bl` from 767 to 1528 m/s on neighbouring
hits, all of it angle.

Two datasets now test it, both from the REL ballistic-limit database (Ryan et al.,
*Defence Technology* 2023; Mendeley `10.17632/4f92y6jzzh.2`, CC BY 4.0):

| angle | measured `v_bl`/`v_bl`(0°) | `sec θ` |
|---|---|---|
| 15° | 1.030 | 1.035 |
| 30° | 1.158 | 1.155 |
| 45° | 1.433 | 1.414 |

— 20 mm of 6082-T651 against the 7.62 APM2, all four angles in one trial
(Forrestal, Børvik, Warren & Chen, *Experimental Mechanics* 54: 471–481, 2014), and
the bare cores of the same bullets give 1.041 / 1.161 / 1.407. The secant law is
right to within 1.5% over the whole range.

The material-cancels claim gets its own test from twelve other pairs — ten
aluminium rows across seven alloys, plus one ultra-high-hardness steel at two
thicknesses, from four studies, each plate shot at 0° and 30°. They scatter from
1.06 to 1.16 about a
mean of 1.11, with the model's 1.155 at the top of that range: an angled plate in
the game is a few percent stronger than the average trial found. That is recorded
rather than fitted away, because one number falling out of the geometry is worth
more than an exponent fitted to the mean of twelve alloys nobody wears.

**Where it stops.** The published series ends at 45°. The cosine floor is 0.34,
about 70°, so everything between 45° and the floor is extrapolation on a law
verified over half that range, and nothing measures the fibre mode's `√sec θ` at
any angle at all.

### What mass the limit is computed against

The same trial settled a question the model had been answering wrong. Forrestal
fired complete APM2 bullets and, separately, their stripped 5.3 g cores into the
same plate: the limits came out within a few percent of each other — 501 against
514 at normal incidence, 718 against 723 at 45° — so half the bullet's mass makes
no difference to whether it gets through a metal plate. The jacket stays at the
face.

```
m_ductile = m_core + JacketCarry · (m_bullet − m_core),  JacketCarry = 0.05
m_brittle = m_fibrous = m_bullet
```

`JacketCarry` is `(514/501)² − 1` spread over the mass that is not core: five
percent. The split by failure mode is not a convenience — the trial is a **metal
plate**, and a tile does not get punched, it shatters and takes the whole
projectile with it, while a fibre pack catches what arrives. Reading those two at
the core's mass alone produces answers the standards themselves refute: a Level IV
tile stopping .30-06 AP more easily than M80 ball, and the ceramic class rungs
falling under their own certificates. What would extend the measurement: Forrestal's
experiment against a tile and against a pack.

The rule it replaced — *the whole bullet, always* — was defended on the grounds
that describing a 7N10 as its 1.7 g core made the round harder to stop than the
same round with no core described. That is still true, and it is no longer a
defence: the undescribed reading over-credits a bullet with mass its core never
delivers, and the measurement says so. Every mode constant was fitted against the
old rule and every one of them was derived again.

Below `v_bl` the plate holds. Above it, Recht–Ipson gives what carries on, and the
plug punched out of the plate leaves with it:

```
v_r = m/(m + m_plug) · √(v² − v_bl²)
```

The energy the armor took is no longer a constant to tune. It is `½m(v² − v_r²)`,
and it falls out of the limit.

### The class an item can hold

A class is a certificate a construction earned, and the game hands out ratings its
materials cannot reach: 125 of the aramid packages sewn into vests are stamped
class 3, which with aramid alone would take on the order of 200 mm of it. That is
why carriers are sold as Br1 or Br2 and the rifle protection lives in the plates.

The ceiling is a property of the form, not of the item:

| Form | aramid, UHMWPE | polycarbonate | metal, ceramic |
|---|---|---|---|
| sewn package | 2 | — | uncapped |
| pressed shell, visor, mask | 3 | 2 | uncapped |
| plate | uncapped | — | uncapped |

Pressing the same fibre into a resin-bonded shell buys one rung and no more: past
that a helmet stops getting thicker and starts getting a metal or ceramic element,
and that element is a product in its own right. Metal and ceramic are not capped —
a heavier helmet really is a thicker shell — and neither is a plate, which is where
rifle protection lives and whose thickness answers for it in the ballistic limit
directly.

The construction has always been read at the ceiling. What the item carries is now
read there too, because everything that reads a class rather than a thickness — the
fragment gate, the fallback threshold below, the item card, other mods — believed
the label. An aramid package rated 3 was being modelled as the class 2 package it is
and gated fragments as though it were a class 3 plate.

The one exemption is a hard element the game files under some other slot, and it is
data rather than a rule: the Velocity SLAAP is 18 mm of polyethylene against the
7.3 mm of the thickest shell anyone fields — a rifle-rated applique that bolts onto
a helmet — so the reference book marks it a plate and its rating stands.

### The class threshold, for armor with no construction on file

An item the reference book cannot resolve — an invented plate, a mod's own — still
falls back to a specific energy against its class:

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
Penetration is probabilistic in a band around whichever limit applies — `v/v_bl`
where the construction is known, `U_hit/U_limit` where it is not:

```
P(pierce) = clamp01( (ratio − (1 − band)) / (2·band) )
```

Outside the band the outcome is deterministic.

### The price of the hole

A projectile that defeats the panel pays for it:

```
E_cost = E − ½m·v_r²                              a consequence, not a constant
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

A worn plate is not uniformly thinner — it is intact where nothing hit it and
broken where something did. The old model multiplied the whole plate by a smooth
durability factor, which made a plate at 50% strength half a plate everywhere;
wear is now **probabilistic with one curve and two inputs**.

The curve: a spot carrying damage `x` (0..1) presents `1 − x^k` of its
thickness. `k` is how local the material keeps its damage — aramid at 4 loses
the cut fibres and nothing beside them, soft ductile metal and UHMWPE sit at 3,
hard steel and titanium at 2, ceramic at 1.5 because the crack web spreads and a
struck tile is rubble.

The inputs:

- **Seen damage.** Armor remembers where it has been hit; a new hit within
  `DAreaMm` of recorded ones is answered by geometry, nothing to roll:
  `x = 1 − (1−q)^n` over the `n` hits recorded there, `q` being the damage one
  hit does to a spot (ceramic 0.90, hard steel/titanium 0.50, soft
  ductile/UHMWPE 0.40, aramid 0.30). After one recorded hit a ceramic tile
  presents 15% of itself, hard steel 75%, UHMWPE 94%, aramid 99%.
- **Unseen damage.** The item entered the raid worn, or the hit memory (64
  records) overflowed. The chance of striking a damaged spot **is** the missing
  durability; a struck spot reads `x = max(missing, q)` — max, because a roll
  that said "you found damage" means at least one hit landed there, and with it
  the two paths meet at the boundary instead of telling two stories about one
  plate. A clean roll meets the full plate.

On a two-layer barrier `q` and `k` belong to the **layer**: one draw decides the
event, and the ceramic face answers it as ceramic while the fibre panel behind
answers as fibre — which is how a composite keeps stopping rifle rounds on a
cracked tile's backing, degraded but not gone.

The damage radius has an anchor the rest of the numbers lack: both certification
standards space their scored shots so that damage zones do not interact — NIJ
0101.06 demands 51 mm between hits — so no material's `DAreaMm` exceeds 51. The
`q` values are assumptions pending manufacturers' multi-hit data (which exists,
but for *spaced* hits, not one spot), and are marked as such in the config, on a
par with the certification criterion's `P_pass`.

Durability loss is driven by absorbed energy rather than by hit count —

```
Δdurability = E_absorbed / JPerDurability
```

— where a blocked hit absorbs the full energy and a penetration absorbs only
`E_cost`. But whether a hit is *allowed* to charge that price, and to record a
wear spot at all, is the material's decision (`ArmorDamageCalculator`), because
the multi-hit evidence splits three ways:

- **Ductile metal wears only past `WearDepthFraction` (0.5) of its thickness.**
  Partial-penetration depth follows from the failure law's own work integral —
  plugging work grows as `T²` so `p/T = v/v50`, hole-expansion flow grows as `T`
  so `p/T = (v/v50)²`. Below the fraction a stopped bullet leaves a dent and
  nothing else: no durability, no spot. Above it the price ramps linearly to the
  full energy price at the limit (a step would make `0.49·v50` free and
  `0.51·v50` full). The anchors: Armox 600T took repeated 7.62 M61 AP **on the
  same spot** without losing resistance — the craters do not deepen
  cumulatively, the dent floor work-hardens (Göde et al., *Eng. Sci. Tech.* 38,
  2023, `10.1016/j.jestch.2023.101337`) — and MIL-STD-662F reads a metal plate
  as pristine two projectile diameters from a crater, which is also what pins
  the metal `DAreaMm` at 15–20 mm.

  A projectile that **died on the face** (CoreFate other than rigid) reads its
  depth at `(v/v50)²` whatever the plate's own law: the linear reading belongs
  to a rigid punch boring its own calibre, and a mushroomed slug spreads over
  several calibres and shoves metal aside — the flow reading. This is the steel
  gong made honest: a magazine of soft-point 7.62x39 point-blank at 0.7 of the
  limit dents a Бр3 panel and costs it nothing, while ball arriving near the
  limit still pays most of the full price — which keeps the AR500 Level III
  certificate's six M80 a real test. Surfaced by a raid, not a paper: the first
  build of this rule read a lead slug's depth linearly and a Бр3 panel ate a
  magazine it should have shrugged off.
- **Fibre pays durability only for penetration.** Dyneema HB26 panels *gain*
  V50 during their own eight-shot test, and a hybrid soft pack shot at 75 mm
  spacing outperforms the same pack at 150 (van Es; van der Jagt-Deutekom &
  Broos, PASS 2024, `10.52202/080042-0031`). A blocked hit costs
  `FibreBlockWearFraction` (0) of the energy price — but still records its
  spot: the caught bullet has cut the fibres of its own channel, and the
  clearing evidence is spaced shots, not repeats into one crater.
- **Ceramic pays the full energy price both ways, unchanged.** The
  certification budgets land where that price already puts a ~45-durability
  plate — ~2 full-power rifle stops, ~3.4 intermediate, ~13 pistol — against
  ESAPI's three shots per threat (Rev G: three each of six threats), NIJ
  0101.06's one .30-06 AP for Level IV and six M80 for Level III, GOST's five
  shots at five-calibre spacing, and the observed 2–4 / 5–10 / 10–20 from
  destructive tests. Two hits into a ceramic/UHMWPE assembly leave a third of
  the one-hit residual strength, and a tiled face keeps double the residual of
  a monolithic one (Guo et al., *Materials* 15(3):901, 2022) — the tile-versus-
  monolith distinction is deliberately not modelled yet, one datapoint being
  too few to calibrate on.

An item the server has no geometry for (`v50 = 0`) pays the old full price —
there is no thickness to take half of.

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

## Environment barriers

A wall, a door or a sheet of tin is a barrier with a material and a thickness, and
the same four values decide it as decide everything else. Vanilla decides it with
a gate instead: `PenetrationPower > collider.PenetrationLevel` and a coin flip
weighted by two per-material chances, after which a projectile that got through
pays **nothing** — the child spawned on the far side of a door carries the
parent's full speed, damage and penetration. That made the environment
transparent to the wound model (the same round is as lethal through a plank as in
the open) and left the obstacle gate as the last consumer of a template number the
rest of the mod had already replaced.

The engine hands over two facts about what was hit: a `MaterialType` and a
`PenetrationLevel` read **off the collider**, not off the preset — a map's
`_MedPen` and `_HiPen` variants are the same material with different numbers, and
a level designer may put any value on any object. The reference book
(`obstacle-reference.jsonc`, next to the plugin) turns that pair into a barrier by
naming a mechanism and the material's own published properties. It also carries a
thickness per level, interpolated piecewise-linearly between anchors and flat outside
them, but that is the fallback: where the collider can be measured, the scene wins
(see below). The level does nothing else: it used to be read as the designer saying
"this is a wall", overriding the material, and a raid census killed that reading —
level 100 carries concrete walls alongside an IBC tote's plastic cage, a polythene
box, a cistern, a boiler, a run of pipes and a patch of gravel floor. It is a blanket
"not meant to be shot through" applied by hand, and treating it as geometry made a
plastic tote bulletproof.

### Thickness, and what a collider means

How much of the barrier the projectile has to cross is **measured off the scene**, not
looked up. At the moment of the hit the engine hands over the collider that was struck;
a ray cast backwards from beyond it, tested against that collider alone, finds the far
face, and the distance from the entry point to it is the thickness along the actual line
of flight. Obliquity is therefore already in the number and the secant law above applies
only to what the measurement cannot see.

The measurement is taken as it comes. There is no sanity check against the book, and
that is the point rather than an oversight: a hollow shell measures as the whole shell
and an electric motor measures as an electric motor, which are exactly the objects a
per-material anchor gets wrong. The book's anchor is what a thing of that kind *usually*
is; the collider is the thing. An electric motor and a locker door are both `MetalThick`
and only one of them should stop a 5.45.

The anchors stay as the fallback, for the cases where the probe comes back with nothing:
a graze along the surface with no depth to cross, or a collider the ray cannot resolve.
The journal says which of the two produced the number, `h=2.03mm(measured)` against
`h=0.7mm(book)`, because when a wall behaves unexpectedly that is the first thing worth
knowing.

**A collider is not always a path.** Measuring it and believing the number made
barrels, plastic canisters and the corrugated sides of shipping containers
bulletproof, and rightly so on its own terms: a barrel is six hundred millimetres of
collider around one millimetre of steel, and the measurement reports the outline. What
separates the two cases is not in the geometry — Unity has no hollow flag, and
`MeshCollider.convex` is about physics representation rather than about the object.
What the game does carry is the `MaterialType` the level designer put on the collider,
and `MetalThin` on a barrel is that statement in as many words.

So the book classifies each material as solid through or a shell around air. For a
solid one the measurement is the path and is used. For a shell the measurement is only
the outline, and the wall is the book's thickness — which is what the anchor was always
describing. A shell is also charged **twice** where there was room inside it for two
walls with air between them: a bullet through a barrel really does cross steel going in
and coming out. How much room that has to be (`ShellCavityMm`, 150 mm) is the one
number here that is a judgement about how maps are authored rather than a piece of
physics, because a container panel is a single sheet whose collider is a few
centimetres thick and must not be charged as if it were a drum. It is the first thing
to reach for if sheet metal starts feeling expensive.

**What is measured against that threshold is the projectile's own path inside the
object, not the chord of the collider.** The distinction does not exist for a drum and
is everything for the commonest shape in the maps. A trailer, a gantry crane, a stack
of pipes, a truck body: each is one non-convex mesh drawn around the whole prop, and the
engine raises a collision at every face of every solid region inside it. The chord of
such a mesh is metres where its sheet is millimetres, so a chord-based rule read "there
is room for two walls in here" and charged the exit of each sheet as a second skin —
crossing two real sheets of one trailer cost up to four. What actually says whether
there was a cavity is how far the projectile FLEW inside the object since it last
struck it: two faces of one sheet are millimetres apart however big the mesh around them
is, and the two skins of a barrel are the barrel's diameter apart. That distance is read
off the projectile's own ancestry — every crossing spawns a child whose parent remembers
the collider it hit and where — so the nearest ancestor that hit this same collider is
the last time the chain was at one of its faces. Whether that ancestor met a front face
or a back one is deliberately not asked: a projectile crossing the second sheet of a
trailer meets its front face, and requiring otherwise would break the chain at exactly
the case this exists for.

Where there is no such ancestor to be had — a fragment born inside the object, a chain
the engine released early — the chord is still the best guess there is and the old rule
stands. The cost of the change is that a genuine cavity under 150 mm (a small pipe, a
box section) drops from two walls to one, which is an undercharge bounded by a single
wall of the book; if a crane leg should cost two skins, that is a question for its
material and not for the geometry.

There is one thing that rule cannot see, and it is the commonest object in the game
that has it: a door leaf. Geometry cannot tell a leaf from a single profiled sheet
lying inside a deep collider — but the scene can, because BSG park their door leaves
under a `DOORS` node, and the resolution layers below read it. Where they do not, a
name can say it instead (`DoorNames`, matched like the vehicle families over the
collider's own name and its ancestors'): Factory's entrance gate hangs off
`Enterance_Gate_01` with no such node in the chain at all, and on the maps that do
have one the gate's wicket door sits four levels below it, out of the ancestor walk's
reach. The same words carry the rest of the family — swing gates, the PTOR
checkpoint, garage gates, transfer gateways — which the census found on that same
plain-69 anchor. Roller shutters are deliberately left out of it and given a material
instead: a shutter is a curtain of 0.8–1.2 mm slats, one layer and not a leaf of two.
Either way the answer only says "this is a leaf"; **what a leaf is, the material
says** (`DoorLeaf` in the book), because nobody builds every door the same way:

- *Sheet that cannot carry itself* — thin steel, plastic — laminates: **a leaf is two
  skins over a frame** (`DoorLeaf: skins`), and its 46 mm collider is far under the
  cavity threshold, so without the rule it was charged one sheet where the bullet
  crosses two. The **entry** face is charged `DoorWalls` (2) of the book's wall and
  the exit face is left exactly as it was: under the cavity threshold it is free, so
  a leaf costs two sheets in total and no more.
- *A wooden door is ~50 mm of wood* (`DoorLeafMm: 50`) — a **fixed** thickness that
  replaces the shell's one-board anchor. Not the measured chord: a leaf's collider is
  the door assembly, 100–200 mm deep, and reading that as timber made every wooden
  door a safe. The collider's depth is the box's, not the wood's.
- *A thick-steel door is 5 mm* (`DoorLeafMm: 5`) and **one plate**, never two —
  nobody welds a door out of two slabs. Five is the armoured end of a real steel
  entrance door (1.5–3 mm of sheet over a frame); the plain-69 anchor it replaces
  was handing 1 716 ordinary colliders — entrance doors, interior metal doors,
  garage shutters, the PTOR gates — 10 mm of the heaviest steel in the game, the
  tier vanilla reserved for .50. A hull off a door still gets the anchor. The
  bunkers' blast doors and gates go the other way and are `Machinery` by identity
  outright — a hermetic door is a machine, and nothing a rifle carries opens one
  (the bunkers' interior shells wear the same material and stay in the
  building-shell class: they are not doors).

The count rides in the barrier itself, so everything that reads a thickness — the
ballistic limit, the path, the refusal gate, the journal line and the marker label —
argues about the same object. Vehicles are deliberately **not** given the flag: their
3 mm flank already contains the inner panel, and crossing the body is a chord over
the cavity threshold, which the ordinary shell rule prices at two flanks by itself.

Which materials are shells was settled empirically — first object by object off raid
journals, then by a survey campaign across four maps (one aggregated measurement line
per prop; 3 206 props under fire) checked against a census of all 18 430 collider
names in the shipped scenes. `MetalThin`, `Plastic`, `Fabric`, `Cardboard`,
`GarbagePaper` and `Glass` are sheets. `WoodThin` joins them because "thin" names a
board and what is built out of boards — cabinets, crates, pallets, doors, window frames
— is hollow; its colliders measured 50–96 mm where the board is twenty. `Rubber` joins
them because every object carrying it was a loader's wheel, whose collider spans the
whole tyre: read as solid it is unshootable, read as a shell the bullet pays for tread
going in and tread coming out, which is what a tyre is. `MetalThick` joined with the
campaign: its census carriers are barrels, cisterns, pipes, gates, trucks and
dumpsters — outlines around air that measured as metres of "steel" — and the genuinely
dense ~2.6% (machinery, rails, columns, hatches) is carved out by name instead (see
the reference below). `WoodThick` stays solid on the opposite evidence — logs, piles,
stumps and live trees measure honestly, and a closed crate reads as a box full of
something rather than as a bare plank.

One consequence worth stating. Map geometry is not authored to be shot through, so what
the model reads is whatever the level designer drew — a door whose collider is twice the
door, a housing that is solid where it looks hollow — and the mod now inherits those
decisions instead of averaging over them.

### The path through it

```
h_path = h / max(|cos θ|, AngleMinCos)
```

The same secant law and the same clamp as the armor model, for the same reason: a
graze presents more material, and without a floor it presents infinitely more.

### Steel

A steel sheet is the armor model's problem with a different plate. It goes through
`BallisticLimit` unchanged — ballistic limit, Recht-Ipson residual, plug mass and
all — against structural mild steel: 250 MPa yield, 158 HV, 7.85 g/cm³, failing by
hole expansion rather than by shear plugging, because an alloy with
strain-hardening reserve left cannot localise a shear band at any price. Those are
the same figures the published mild-steel ladder in `ArmorStandardTests` is
measured on, so the environment's sheet metal and the armor model's one non-armor
alloy are literally the same material.

The thicknesses are what the objects plausibly are: 1.0 mm for the common sheet of
the environment (`MetalThin` — the survey census put 95% of its instances on one
level, and the typical carrier is a car body or a cabinet at 0.8–1.0 mm, not the
0.5–0.7 fence profile the first edition was anchored to), 1.5 mm for scrap lying over
itself (`GarbageMetal`, 7), 3 mm for a vehicle's flank (`VehicleChassis` — an outer
panel of 0.8–1.0, an inner panel of 0.7–1.0 and the window mechanism, intrusion beam or
seat frame between them; a shell, so crossing a whole car pays both flanks), and
a 2 / 4 / 6 / 10 mm ladder for `MetalThick` at its four levels (7, 18, 32, 69).
That ladder reproduces vanilla's hierarchy for a pistol round by geometry rather
than by a threshold — 9x19 ball crosses 2 mm and 4 mm and stops at 6 — and lets a
rifle round through all of it, which is what a rifle round does to mild steel.

The vehicle flank is anchored the same way, on what shooting cars is known to do: its
limit for 9x19 ball comes out at 281 m/s, so across a pistol's useful range the near
door is roughly even odds, what gets through arrives nearly spent, and the far flank
stops it outright — while rifle ball crosses the whole car with two thirds of its
speed and a slow heavy pistol round (.45 ACP ball) does not cross even one side.

The limit itself is a distribution, not a number. The certification criteria the
armor model is calibrated against price the shot-to-shot scatter of a measured V50
at a coefficient of variation of 0.04, and each encounter with a sheet draws its
own limit uniformly within the ±2σ of that — one draw per encounter, shared by the
verdict, the residual and the ricochet gate, because all three describe the same
square inch of material. Near the limit this makes what the testing standards call
a zone of mixed results: some rounds dribble through and some stop, instead of
every round splitting the same way at a single figure.

### Bulk media

Everything with substance — wood, cardboard, rubber, gravel, snow — obeys one law,
parameterised by the material's own crushing strength and density. A projectile in
a resisting medium meets a static term (the material's strength) and an inertial
term (throwing the material aside), which is Poncelet:

```
F = A · (S·σ + ½·C_d·ρ·v²)
```

Integrating it gives the depth and the residual directly:

```
λ      = 1000 · (m/A) / (C_d · ρ) · (1 − ExpansionDepthFactor · X)     mm
v_stop = √( 2000 · S · σ / (C_d · ρ) )                                 m/s
D      = λ · ln( 1 + (v/v_stop)² )                                     mm
v_res  = v_stop · √( max(0, (1 + (v/v_stop)²)·exp(−h_path/λ) − 1) )    m/s
```

with `σ` in MPa and `ρ` in g/cm³. It gets through when `v_res > 0`, which is the
same statement as `D > h_path`.

This is the wound channel's law with the static term kept instead of dropped.
Gelatin has almost no strength, so there the term vanishes and the depth collapses
to the familiar `λ·2·ln(v/v_stop)`; wood has a great deal of it, and dropping it
puts a rifle round and a pistol round a factor of two apart in pine when the
published tables put them a factor of four apart. The consistency check runs the
other way too: read the wound channel's fitted 50 m/s backwards through the
`v_stop` expression at ρ = 1.0 and it asks for a strength of 0.25 MPa, which is
where 10% ordnance gelatin's quasi-static crush strength actually sits.

One difference in reading. Here `D` is where the projectile comes to **rest**,
because the medium's static term brings it to rest. The wound channel's `L` is
where tissue stops being **cut**, which happens long before the bullet stops. The
two are not the same quantity and must not be compared as if they were.

**The two constants come from theory, not from a fit.** `S` is the confinement
factor cavity-expansion theory puts at three to five times a material's uniaxial
strength — the same scale the armor model's ductile hole-growth constant sits on —
and is taken at 5. `C_d` is Poncelet's inertial coefficient, taken at 1, the
classic value for a blunt cavity. What justifies them is that with pine's own
published properties (6 MPa across the grain, 0.50 g/cm³) four cartridges spanning
a factor of six in energy land where Hatcher's white-pine tables put them:

| Cartridge | m, d, v | Model | Published |
|---|---|---|---|
| .22 LR | 2.6 g, 5.7 mm, 330 m/s | 132 mm | ~4-6 in |
| .45 ACP ball | 14.9 g, 11.5 mm, 250 m/s | 120 mm | ~5-6 in |
| 9x19 ball | 8.0 g, 9.0 mm, 380 m/s | 199 mm | ~6-8 in |
| .30-06 M2 ball | 9.7 g, 7.82 mm, 838 m/s | 778 mm | ~27-30 in |

Pine's stop velocity comes out at 346 m/s, which is why a pistol bullet spends most
of its path in wood pushing through it and a rifle bullet spends most of its path
throwing it aside.

Everything else with bulk carries its own two properties on the same law:
corrugated board at 0.3 MPa and 0.10 g/cm³, cloth at 0.05 and 0.15, rigid sheet
plastic at 45 and 1.20, rubber at 15 and 1.20, fired clay tile at 40 and 1.90,
loose aggregate at 1 and 1.60, compacted snow at 0.3 and 0.35. The thicknesses sit
beside them in the book.

### Packed media: a carrier and its contents

One material is not enough for palletised cargo, and averaging two into one gets
both ends wrong at the same time.

A pallet of boxes is two things at once. The **stack** is cardboard around air: clip
a corner of it and it has to cost about what a cardboard box costs. What is **in**
the boxes is packed goods, and a round sent down the long axis of a loaded pallet
meets a great deal of it. The book used to answer both with `GenericSoft`, one
homogeneous solid at 0.40 g/cm³ over the whole measured chord. That made a corner
clip stop rifle fire, made the long axis no worse than the short one — the chord is
the only thing a homogeneous medium reads, and it does not know how much of the
object is behind it — and made every shot into the same pallet come out identical,
which is the one thing shooting into stacked cargo demonstrably is not.

So the carrier is crossed **continuously** and the contents are met **discretely**:

```
layers  = ceil(h_path / SpacingMm)
step_i  = min(SpacingMm, remaining)                 mm of carrier
package = ContentFraction · step_i  with probability Chance    mm of contents
```

Each sub-crossing is the ordinary bulk-media law above; the projectile's whole state
— speed, mass, calibre, deformable fraction and **yaw** — is handed forward from one
to the next, so a package met late in the stack is paid for at the yaw the earlier
ones left. Deflections do not add: each layer throws the round off in its own
direction, so they combine in quadrature, as a random walk does. `Penetrates` means
it survived every sub-crossing.

Two properties fall out of this that a homogeneous medium cannot have. The first is
that the cost grows with the path **in packages** rather than in millimetres, which
is the difference between a corner and a long axis. The second is that it is a
**lottery**: two rounds on the same line get different answers, one threading the
voids and one meeting three boxes of goods.

The shipped numbers: the carrier `BoxCargo` at 0.1 MPa and 0.03 g/cm³ (corrugated
board at the volume fraction a stack of boxes actually has, i.e. near enough to air)
and the contents `BoxContent` at 1 MPa and 0.40 g/cm³ — the density the whole pallet
used to be given, now carried by the fraction of it that earns it. Spacing 300 mm,
`ContentFraction` 0.3, `Chance` 0.5.

**The spacing is a grain size, not a strength knob.** A package is a *fraction of
the layer it sits in*, so the expected cargo per metre of path is
`ContentFraction · Chance` — 15% — whatever the spacing is. It is not perfectly
neutral, and the reason is worth stating because it was the first thing the model
got wrong: **once yaw exists, slicing a homogeneous medium is no longer free.** Each
slice asks the destabilisation question again, and the sum of `Work` over slices
always exceeds the `Work` of one crossing, so a finer stack is a slightly stronger
one. Measured over 4 000 seeds, a 5.45 BS across a 1200 mm pallet (mean exit
velocity, a stop counting as zero):

| Spacing | 600 | 300 | 150 | 100 |
|---|---|---|---|---|
| Mean exit, m/s | 405 | 325 | 281 | 265 |

It converges, and halving the shipped spacing costs 13% of the answer. That is the
honest residue of the mechanism; it is pinned as a band under a fifth rather than
claimed to be nothing, and it is why the spacing is a weak lever rather than a free
parameter.

**Measured behaviour.** 5.45 BS (3.68 g, 5.6 mm, 850 m/s) and 9x19 ball (8.0 g,
9.0 mm, 380 m/s), rigid, square on, exit velocity by how many packages the draw
actually produced:

| Packages in 1200 mm | 0 | 1 | 2 (expected) | 3 | 4 |
|---|---|---|---|---|---|
| 5.45 BS out, m/s | 704 | 510 | **301** | 149 | stopped |
| 9x19 out, m/s | 312 | 248 | **178** | 96 | stopped |

and the three geometries over 4 000 seeds each:

| Geometry | Through | Mean exit (5.45) |
|---|---|---|
| Corner clip, 300 mm | 100% | 763 m/s (702-824) |
| Across a pallet, 1200 mm | 94% | 347 m/s (149-704) |
| Down the length, 2400 mm | 15% | 171 m/s |

A corner is a cardboard box, a crossing is survivable and expensive, and the long
axis usually is not survivable — with a real chance of the round threading it,
which is the point.

Two things are deliberately left out and are listed again under *What is
deliberately not modelled*: the layers are **isotropic along the path** (a real
pallet is layered and knows where its top is, and a shot along its layers meets
different geometry from one across them), and an IBC tote is assumed empty rather
than remembering whether the first round found liquid in it.

### Concrete, brick, and the free rear face

Concrete obeys the same law, with two things said about it that the softer media do
not need.

**The strength is not the cube strength.** A projectile opening a cavity in concrete
meets far more than the unconfined compressive strength `f'c`, and how much more is
itself a function of `f'c`. Forrestal's cavity-expansion fit, validated against
penetration data at 14, 35 and 97 MPa for striking velocities from 250 to 800 m/s,
puts the resistance at

```
R = S · f'c        with        S = 82.6 · f'c^(−0.544)      (f'c in MPa)
```

so ordinary structural concrete at `f'c` = 30 MPa resists at 389 MPa. That is the
same product `S·σ` the Poncelet law above already asks for, so nothing new enters the
model: the book carries `R` divided by the global confinement factor of 5 and the
depth law multiplies them back together. The check is a published test the fit was
not made against — 120 mm of ultra-high-performance concrete takes 55 mm from a
7.62 ball, and the same law at that material's strength says 57.

**A slab is not a block.** The depth law answers for a semi-infinite medium. A wall
has a free rear face, the compression wave reaches it and throws a cone of material
off it, and the projectile follows through the gap without ever having to cross that
last part. The NDRC relation puts the perforation limit for concrete at about 1.3
times the semi-infinite penetration, so the model divides the path by a per-material
`SpallFactor` before comparing:

```
h_resist = h_path / SpallFactor
```

This is a property of brittle failure, not a fudge for concrete: steel petals, wood
splits, and both are left at 1. What it changes is exactly the case it should — a
wall between one and 1.3 depths thick is perforated with very little left, which is
what shooting through masonry looks like.

**One preset, two materials — split by name.** The game puts `Concrete` on brick walls
as well, and fired clay brick is the weaker and lighter of the two: 15-25 MPa against
concrete's 30, 1.9 g/cm³ against 2.35, and fired clay's hardness rather than crushed
stone's. What separates them in the scene is that the level author said so in the
object's name — `Area_01_inside_wall_C_bricks_01_BALLISTIC_concrete` — so the book
carries a `Brick` material of its own and a name rule that selects it.

The strength follows the same route as concrete's: `R = S·f'c` at `f'c` = 20 MPa gives
324 MPa. Applying a fit made on concrete to brick is an extrapolation and is named as
one here; what it is not is a free parameter.

What comes out of it, at one course of 115 mm: a pistol round is stopped by both, a
hard-cored 5.45 crosses brick and not concrete, a 7.62 crosses both, and a .50 crosses
two courses of brick. A structural concrete wall at 300 mm stops everything.

### Resolution layers

The brick rule was the first admission that **the `MaterialType` is not the last word on
what a collider is made of**, and the census settled how far that goes. Of 567 504
colliders in the shipped scenes, 346 080 carry a `_BALLISTIC_<word>` suffix — the level
designer's own word for the material — and about six thousand of those words contradict
the material on the same object. Whole classes were affected and no hand-written rule
had reached them: 1 099 `WoodThin` colliders whose name says metal (door frames, sling
loops, the capped lids of equipment boxes), 454 `MetalThin` whose name says concrete (an
entire shower block), the Labs holding cells tagged as sheet while their `Chainfence`
material gives them away for free, 209 colliders of `MetalNoDecal`, a preset with no
rules of its own at all. Ten thousand more colliders hang under the scene's own
`VEHICLE(S)` nodes, where a sheet is a car's flank rather than a road sign.

So a material is resolved in **three layers**, in this order, and each of them can only
ever *add* a material the book already defines:

```
identity (NameOverrides)  →  suffix  →  taxonomy  →  the preset
```

- **Identity** — the book's own name rules, below. It settles the question outright:
  an object claimed by name is that object, and no later layer may take it back. That
  finality is not a convenience, it is what protects the exceptions — armour parked
  under a `VEHICLES` node is still armour, a shipping container on a truck bed is still
  a container, a loader's counterweight whose own name says `metalthic` is still cast
  iron. Names are tried in order, the collider's own first and then its ancestors,
  nearest first, three transforms up, because half the scene names its colliders
  nothing at all: a BTR is three boxes called `MetalThick` under `balistic/BTR_82`, a
  fridge door is `Fridge (1)/Door_D/Ballistic 1/Metal 1`. A named part settles the
  question itself and never consults its parents.
- **Suffix** — the word after `_BALLISTIC_` in the collider's **own** name, through the
  book's `SuffixAliases`. Only the collider's own name: an ancestor may say what the
  prop *is*, but not what this particular box is made of, or the panel and the frame of
  a metal-framed wooden door would be priced alike. The word is normalised (the density
  flags `_LowPen`/`_MedPen`/`_HiPen`, a baked-in `_PL100` and trailing numbering are not
  material) and looked up whole first, then with up to two trailing segments dropped, so
  `metalthin_top` finds the sheet while `wood_thin` resolves as itself long before it
  could decay into the ambiguous `wood`. BSG's misspellings are in the table on purpose
  — they are in shipped scenes, and it is the engine's own suffix parser failing on them
  that dumps their colliders onto `None`. The ambiguous half of the vocabulary is
  deliberately absent: bare `metal` and bare `wood` sit on thin and thick carriers
  alike, so they name nothing.
- **Taxonomy** — what the scene graph says the collider is part of, applied to whatever
  the layer above left, which is a deliberate chaining: a `WoodThin` collider whose own
  name says `metalthin`, parked under `VEHICLES`, is read as sheet and then as vehicle
  skin. Grouping nodes are matched as **whole ancestor names**, never as substrings —
  `vehicle` as a substring swallows the `vechicle_BMP2` prop and everything parked in a
  named car park, `door` would catch a fridge's `Door_D`. Nodes alone are not enough
  either: 5 101 vehicle-named colliders live outside any `VEHICLES` node, the same
  Chevrolet Cruze sitting under the node on one map and under `OFF` on another, so the
  book also carries a census-built list of model words matched anywhere in the
  collider's line. The tempting short ones were refused: bare `man_` catches `woman_`,
  bare `paz_` catches props that are not the bus. `MetalThin` under a vehicle becomes
  `VehicleChassis`, `MetalThick` becomes structural plate; a `DOORS` node changes no
  material at all and instead marks the collider as a leaf, whose construction the
  material's own `DoorLeaf` word decides — skins, solid, or one plate, as described
  above.

The failure mode of every layer is the same and it is the old answer: an object whose
name says nothing keeps the preset, a map or a mod that names things differently keeps
the preset, a word the tables do not know keeps the preset, and a rule pointing at a
material the book does not define keeps the preset. A suffix also loses outright to the
substances in `SuffixFinal` — concrete, stone, ground, water, a body — where the word
describes the skin and the material is what the projectile has to cross: 297 `Concrete`
colliders say `tile` and 55 say `stone`, and a tiled wall is still a wall.

**The name-override reference.** What began as the one brick rule grew, with the survey
campaign, into the book's reference of name rules — every keyword validated against the
full census of collider names, and the words that looked obvious but caught the wrong
things (`rail` catches handrails, `transformer` the substation shed, `engine` a fire
engine, `motor` a motorbike, `table` the Warehouse*Vege*table) recorded as rejected.
Fifteen of those rules are gone again with the layers, because the suffix reproduces
them for every material at once (the `metalthin` rescue on thick metal, `chainfence` on
plastic, `woodthin` on thick wood, and most of the `None` typo block). What is left is
what a layer cannot do: words that are not suffixes at all, shields that must beat the
suffix, and dead branches kept as insurance for maps nobody has surveyed. The families:

- *Steel*: `gunsafe` → a 4 mm steel box; `chainfence` → the free-pass mesh the
  designer's own suffix names, kept as an identity rule purely for the ordering (the
  suffix layer would say the same thing, but `metal_stairs` below is identity and
  would claim the mesh treads of stairs 02/07/08 before the layer ever ran);
  `container` → 1.6 mm corrugated Corten; stairs and
  loader chassis → structural plate (8 mm — the 6–10 mm class between a car body and
  armour). On `MetalThick`, the dense carve-outs → `Machinery` (solid, measured):
  pump machinery, turbines, transformers, switchgear, generators, robot arms,
  columns, rail track, ATMs, cast hatches, a turret, loader counterweights (cast
  iron, confirmed by eye in a raid), heavy plant (an excavator, a Kirovets), and
  the armoured fleet — BTR, BMP-2, T-90 (both spellings: the drivable one calls
  itself T_90A and slipped the first rule), Tigr, Typhoon, Stryker; a BTR hull had
  carried its material at a level the ladder read as 2 mm, and a 9x19 crossed it
  in a raid, and the BMP's turret sat on the plain anchor until an AK crossed it. Steel drums go the other way — 1.2-1.5 mm of sheet, not plate.
  Soft-skinned trucks and appliances go the other way: a fridge is sheet, not the
  10 mm the plain-69 anchor hands every shell, and every Kamaz variant's cab doors
  are `VehicleChassis` — a truck cab door is built like a car's, two panels with the
  window mechanism between them — while the chassis rails, body and drum stay plate.
  (The `metalthin` rescue on the thick material, ~1100 colliders and the largest
  single rule the book ever had, is now the suffix layer's work and no longer a rule
  at all.) The Terrakot mall's steel-clad exterior faces go to Concrete — read
  as a 10 mm shell they made a shoot-through building. Heavy plant (a JCB backhoe,
  an asphalt paver, a road roller) is structural plate — not Machinery, because one
  collider spans the whole machine, cab included; the roller's thin-tagged cab stays
  sheet like a Kamaz door. Cast-iron heating radiators go to the GunSafe shell: ~4 mm
  of iron plus a water column is exactly what that 4 mm shell prices, while the bare
  word `radiator` was refused — the Heating_Radiator_Set parent hangs over a family
  of pipes the ancestor climb would have turned into safes.
- *Wood*: crates that are boards around air → the board shell; firewood billets
  (`poleno`) the other way, boards → solid timber; a closed ammunition crate names
  its own material back at itself (`ammobox` → `WoodThick`), which is a shield rather
  than a change — the crate's name says `metalthick` and the suffix layer would
  otherwise turn a box full of shells into a shell full of air.
- *Cloth that only wears cloth*: sandbags and rubble sacks → `Sand`; mattresses and
  upholstered furniture → `Upholstery` (low-density bulk: a pistol round dies inside a
  couch, a rifle round crosses it slowed).
- *Containers that are their contents*: palletised cargo (`polythene_box`,
  `box_carton`, `pallet_cardboard`, `pallet_weapon_box`) → `BoxCargo`, a carrier with
  packages drawn along the path rather than either empty boxes or one averaged solid
  (see "Packed media: a carrier and its contents"); cable drums → `Cable` (wound
  copper — a rifle ball dies inside a full drum); the construction-debris dumpster →
  its fill, so only its corners, where the chord is short, give.
- *Props stranded on ground materials*: sandbag walls on `Soil` → `Sand`; curbs on
  `Stone` → concrete, stone planters and garden fences → masonry; a crushed-concrete
  barricade on `Gravel` → concrete. The ground materials themselves stay impassable —
  broad words like `rock` were rejected exactly because they reach terrain cliffs.
- *Masonry by eye*: Factory's `inside_wall` family is visibly brick under the
  plaster and joins the `_bricks_` names on the `Brick` material.
- *Glass*: `glass_block` → a hollow block, ~10 mm of glass per face; `armored_glass` →
  a BR4-class laminate that stops handguns and not rifles; debris (`chunk`, `broken`)
  is claimed by identity rules first and stays plain glass.
- *The `None` bucket*: the census showed it is mostly BSG's own typos — `metaltin`,
  `chainfance`, `fabrick`, `concete` fail the engine's suffix parse and land lamp
  posts and a wood stove on "impenetrable". The misspellings themselves now live in
  the suffix table, where every material benefits from them; what stays here as rules
  is what the layer cannot see — words that are not suffixes at all (`post`, `rubble`,
  `metall`, and the `concrete` whose carriers say it in the middle of the name) and
  the shields that must beat the suffix (the loader counterweight's own name says
  `metalthic` and it is cast iron). The true default collider matches none of it and
  stays impassable.

Order within one material's rules is meaningful — first match wins — and two
collisions are deliberate: `gunsafe` precedes `container` (a safe's name contains
both), debris identity rules precede `glass_block`.

### Through for a price, and walls

Glass is neither mechanism: a pane is fractured out rather than crushed through, so
what it costs hardly depends on what is doing it. It carries a flat energy price
instead (15 J for a pane, 8 for one already shattered), paid out of the
projectile's energy and scaled by the same secant. A 9x19 loses 5 m/s crossing a
window; a birdshot pellet loses most of what it had, and one that has slowed to
200 m/s does not get through at all. Wire mesh and low grass are the same mechanism
at zero cost.

Stone, asphalt, soil and gravel are walls, and stay walls for a reason the measurement
cannot fix: ground and road surfaces have no far face for the probe to find, so there
is no thickness to compare a depth against. Concrete used to be in that list and is not
any more — a concrete wall has two faces, and once the near one is struck the far one
is measurable like any other object's.

`PenetrationLevel` no longer overrides anything. It was read as the designer saying
"this is a wall" whenever it reached 100, and a census of one raid's journal retired
that reading: the colliders carrying 100 are concrete walls and floors, and also an IBC
tote's plastic cage, a polythene box, a water cistern, a boiler, a reactor housing, a
run of pipes, thin metal on a pillar, and a patch of gravel floor. It is a blanket "not
meant to be shot through" applied by hand, not a statement about geometry, and reading
it as one made a plastic tote bulletproof. The level now does one job: selecting a
thickness from the book's anchors, for the cases where the scene cannot be measured.

### How hard the barrier worked

One quantity ties the rest of this section together:

```
Work = 1 − v_res / v
```

the share of its speed the barrier took. A plate is always a serious barrier, so the
armor model can apply its constants flat; an obstacle ranges from a sheet of paper to a
log, and the difference between a bullet that lost a tenth of its speed and one that
lost half is the whole difference between a scratch and a mushroom. Everything a
barrier does to a projectile beyond slowing it is scaled by `Work`, which is why one
set of constants covers the whole table.

### Yaw, and why the second wall costs more than the first

Everything above prices one crossing. A row of them was priced wrong, and obviously so:
a rigid core crossed a line of oil drums in a straight line, losing the same six percent
of its speed at the fifth wall as at the first, because nothing in the model made the
projectile any different on the far side. Deformation is scaled by `Work`, and `Work` on
a millimetre of sheet is a few percent — so the bullet came out of the first barrel
virgin, and out of the fifth virgin as well.

Deflection is not the missing piece and must not be stretched into it. It is pinned on a
measured anchor (a 9x19 two degrees off through a pine door) and on 1 mm of steel it
honestly gives about half a degree; a row of barrels is not stopped by a bullet
wandering. What stops it is that the bullet **arrives at the next wall sideways**. A
projectile leaving a barrier is turning, and a turning projectile presents several times
its own frontal area to whatever it meets next — so it drags more, digs less deep, needs
a much higher ballistic limit and is thrown further off line, all at once.

**The state.** One number per projectile, `yaw ∈ [0,1]`: 0 nose-on, 1 fully broadside. It
is durable and it is inherited — recorded against the projectile object and found again
by walking up the chain of parents, exactly as the deformable fraction is (see the end of
"What the barrier leaves of the bullet"). A bullet through a wall spawns a child on the
far side, that child spawns another through the next wall, and each finds the nearest
ancestor a barrier had its way with.

**What a crossing adds:**

```
Δyaw = YawGainK · Work · (L/d − 1) · (1 + YawObliquityK · tan θ)
yaw' = min(1, yaw + Δyaw)
```

Three factors, none of them new to the model:

- **`Work`** — the share of the speed this barrier took. The same measure of "how hard
  did this barrier have to work" that scales the core's deformation, and it is what keeps
  a sheet of tin from doing what ten millimetres of plate does without a second table of
  constants.
- **`L/d − 1`, slenderness** — the lever arm. A long thin projectile has a large
  overturning moment about its own centre and very little polar inertia to resist it; a
  sphere has neither. `L` is the same length the broadside geometry uses — published per
  cartridge where a measurement exists, inferred from mass and calibre otherwise (see
  [Bullet length](#bullet-length)) — so the two halves of the mod agree about how long a
  bullet is. The consequences fall out rather than being written: 9x19 ball 1.05, 7.62x51
  M80 2.7, a buckshot pellet 0.11 — a ball has no orientation to lose — and a flechette
  about 14, which is why darts are notorious for losing the plot against the first thing
  they touch. This is the term the published length was introduced for: 9x19 7N31 read
  1.05 off its mass, i.e. a sphere that no barrier could ever turn, against 1.44 off its
  measured 13 mm.
- **`tan θ`, obliquity** — an angled face loads one side of the nose before the other,
  which is the systematic reason a projectile leaves a barrier turning at all. θ is
  measured from the normal, with the same `AngleMinCos` floor the path uses.

Nothing is subtracted. A real bullet does re-stabilise in air over tens of metres, but
the case this exists for — a row of barrels, a car, a stud wall — is metres, and a decay
term would need a time of flight the model never sees.

**What yaw then does, and only this:** the area the projectile presents.

```
A_eff = A_cal + yaw · (A_side − A_cal)          A_side ≥ A_cal always
d_eff = √(4·A_eff/π)
```

`A_side` is the broadside area from the same geometry as `L`, and the floor at `A_cal` is
geometry rather than a rule about shot: a fully expanded hollow point is short and blunt
and has nothing wider to turn into, and a round ball presents the same disc whichever way
it faces. Three readers, and no others: the Poncelet decay length `λ` (which is `m/A`),
the deflection (`m/A` again), and the steel branch's ballistic limit and core-fate test
through `d_eff`. Because the deflection already carries sectional density in its
denominator, a yawing projectile is thrown further off line by the expression as it
stands — no extra term, no extra constant.

**What yaw deliberately does not do.** It never touches the exit calibre. The projectile
did not get fatter, it is lying over, and writing `d_eff` into the exit state would hand
the wound model a bullet that had grown. It also does not shorten the wound channel's
neck: a bullet that arrives at flesh already sideways really would turn earlier, but the
client's channel formulas are required to match the server's baked ones, and the server
cannot know what the bullet crossed on the way. Both are listed under what is not
modelled.

**Calibration.** One anchor: a 9x19 ball through a vehicle flank — 3 mm of steel, `Work`
0.43, slenderness 1.05 — comes out **about half sideways**, which is the keyholing on the
target that forensic reconstructions of shots through car doors are recognised by. That
gives `YawGainK` = 1.1. `YawObliquityK` = 1.0 says the destabilising impulse roughly
doubles at 45°; it is a judgement and not a measurement, and the book says so where it is
written down.

What that produces for a 5.45 BS (3.68 g, 5.6 mm, 850 m/s) crossing 1 mm walls, which is
the case the whole subsection exists for:

| Wall | yaw in | A_eff, mm² | v in → out, m/s | Without yaw |
|---|---|---|---|---|
| 1 | 0.00 | 24.6 | 850 → 798 | 850 → 798 |
| 2 | 0.20 | 37.8 | 798 → 723 | 798 → 748 |
| 3 | 0.50 | 58.2 | 723 → 615 | 748 → 699 |
| 4 | 0.97 | 90.1 | 615 → 465 | 699 → 651 |
| 5 | 1.00 | 66.1* | 465 → 260 | 651 → 605 |
| 6 | 1.00 | 47.5* | stopped | 605 → 560 |

\* the fourth wall took a quarter of the speed, which is over `JacketStripWork`, so from
there on the calibre and the broadside area are the bare core's.

Six walls is three oil drums. The old model put the same round through twelve walls with
291 m/s still on the clock.

### Deflection

A projectile through a barrier does not come out on the line it went in on. The
resisting force is not exactly on the axis — the material is not uniform at the scale a
bullet meets it, and the nose is never loaded symmetrically — so some fraction of the
axial impulse arrives sideways and turns the trajectory by `J_lat / (m·v)`. With an
inertial resistance the axial impulse is itself `ρ·v·A·h`, the velocity cancels, and
what is left is a ratio of two areal densities:

```
tan Δθ  =  DeviationK · (ρ_barrier · h_path) / (m/A)   · (1 + DeviationDeformMult · Work)
                                                          ← only if the core died
```

Sectional density in the denominator again — the same quantity that decides how deep it
goes decides how straight it comes out. `DeviationK` is pinned so that a 9x19 through a
45 mm pine door leaves about two degrees off line, which is where the forensic
reconstruction literature puts a handgun bullet through a wooden door. Everything else
in the table is that number times a ratio:

| Through a 45 mm pine door | Deflection |
|---|---|
| .45 ACP, 14.9 g at 250 m/s | ~1.8° |
| 9x19 ball, 8.0 g at 380 m/s | ~2.0° |
| 5.7x28, 2.0 g at 716 m/s | ~3.3° |
| 7.62x51 M80, 9.5 g at 800 m/s | ~1.3° |

**Velocity does not appear, and that is a result rather than an omission.** Every
rigid-body route to a velocity term cancels. A purely inertial resistance gives the
expression above. A purely static one — the barrier's strength, acting for a transit
time `h/v` — deflects *slow* projectiles more, not fast ones. The gyroscopic argument
cancels twice: the overturning moment goes as `v²`, the spin's angular momentum as `v`,
and the time in the barrier as `1/v`. Physics is unanimous that a rigid projectile's
deflection is velocity-independent.

What actually makes a fast bullet worse through a barrier is that a fast bullet is the
one that stops being a symmetric rigid body. So velocity enters the deflection exactly
once, through the core's fate below, and it enters hard: a barrier that killed the core
throws what is left of it much further off. That also settles the practical ordering —
heavy and slow goes straighter than light and fast — twice over, once through sectional
density and once through deformation, and never the other way round.

Vanilla's own deviation (a per-material chance of a per-material random kick, the same
for a fragment and a .50) is replaced wherever the book claims a material. Where it does
not — a material with no body to it, like wire mesh — the game's draw is left exactly as
it was, because "no model of ours" must not silently become "no deflection at all".

### What the barrier leaves of the bullet

A plate strips a jacket, blunts a core and hands the flesh model a changed projectile.
A wall now does the same thing through the same code, because the difference between
them was never physical — it was that one of them had been written.

Whether the projectile survives the meeting intact is the **Taylor rigidity criterion**,
two-material form, exactly as the armor model asks it: the barrier's stagnation pressure
plus the share of its own strength that supports it, against the core's dynamic yield.

```
½·ρ_barrier·v²  +  DeformPlateSupport · HvToYield · Hv_barrier   ≥   HvToYield · Hv_core
```

Three consequences worth stating, because all three are checkable:

- **Wood does not deform bullets.** At 0.5 g/cm³ the stagnation pressure never reaches
  lead's dynamic yield at any speed a gun produces. This is why wood and water are the
  classic bullet-recovery media, and the model reproduces it without being told.
- **Steel does.** A lead-cored bullet on a steel sheet meets three times its own yield
  and mushrooms; a hardened core at the same speed does not. The separation is the same
  one the armor model is calibrated on.
- **Speed is what moves a core across the line.** A mild steel core is rigid against
  sheet metal at pistol velocity and dead against it at rifle velocity — which is the
  velocity dependence the deflection borrows.

What then happens to it goes through the same `ArmorExit` a plate uses, with the two
coefficients scaled by `Work`:

```
kDef  = CoreBluntK  · Work      how much blunter the dead core comes out
kFrag = CoreErosionK · Work     how much mass it shaves off in the hole
```

Both are pinned so that a barrier taking a third of the projectile's speed does exactly
what the plate constants do (`KDef` 0.2, `KFrag` 0.05). A rigid core is untouched: it
comes out with the mass, calibre and deformable fraction it went in with.

The jacket is the one place the two barriers genuinely differ. A plate always has a rim
for a jacket to shear against; thin sheet does not, and a hard core takes its jacket
through a tin wall and leaves it in a steel one. `JacketStripWork` is where that line
sits, and it is drawn on `Work` for the same reason everything else here is.

The changed state travels with the projectile for the rest of its flight, and to
anything it spawns. Mass and calibre live on the shot itself; the deformable fraction
has nowhere in the engine to live, so it is recorded per projectile and found again by
walking up the chain of parents — which also fixed the armor case, where a post-plate
projectile that outlived its frame used to revert to its cartridge's figure.

**And the exit speed has to be handed over at the moment the projectile is created.**
The engine builds a projectile's entire predicted trajectory when it is born, from the
direction and speed it is created with, and then overwrites its position and velocity
out of that table on every tick. A speed written into a freshly spawned child after the
fact therefore survives exactly until that child's first tick. At contact range this is
invisible — an impact inside the first tick is interpolated back towards the written
value, so most of the exit speed is returned — and at range it is total: a bullet
through a door arrived at a body thirty metres away with very nearly its muzzle speed,
and the barrier's deflection never bent the real flight path at all. Vanilla's own
deviation and ricochet work for precisely the reason ours did not: they are *arguments*
to the spawn, computed before it. So the exit speed and the rebuilt direction are laid
into those arguments instead, together with the spawn point, which vanilla places along
its own direction and which would otherwise put the child sideways of the hole it came
out of. Where the model has no deflection of its own — a barrier with no body to it,
like wire mesh — the direction is left to vanilla, because "no model of ours" must not
silently mean "no deflection at all".

Two consequences of that are worth stating because they are what a raid will notice.
Distance now costs what the barrier said it costs, so a round through a wall is weak at
range in a way it never was. And a shattered core is exempt: the engine builds one by
spawning N children from the same parent, and handing each of them the whole exit speed
would create energy out of nothing.

### Ricochet

Vanilla has one angle window for every surface in the game — between 42.5° and 80°
from the normal — and then rolls the material's chance against the cartridge's.
That says the same thing about concrete, water and a sheet of tin, and it makes the
bounce a property of the ammunition card.

Here it is a critical grazing angle per surface. With `α` measured from the surface
(0 = along it, 90 = square on):

```
α_crit(v) = α₀ · (V_ref / v) ^ q
```

Faster is not better for a ricochet: a bullet arriving quickly loads the surface
hard enough to dig its own crater, or to come apart, before the surface can turn
it, which is why the forensic critical angles are quoted per velocity band and fall
as the band rises. `V_ref` is 400 m/s and `q` is 0.35.

Below `α_crit` it bounces, above it does not, and around it there is a band of ±25%
where the chance is linear — the surface is not a plane at the scale a bullet meets
it, and roughness is the one thing in this module that is honestly a die roll.
Vanilla's own limit of two ricochets per shot is left alone.

One gate sits in front of all of this for sheet metal: **a sheet can only throw
off what it could refuse.** Ricochet off steel is allowed only when the projectile
is at or under the sheet's ballistic limit along the **true line of arrival** —
same law and same per-encounter draw as the penetration verdict, but with no
obliquity floor. The floor answers an exit question: a graze that does get through
leaves by a chord its own calibre digs, not by an infinite slant. Refusal is not
an exit question — whether a sheet can turn a projectile away is decided by
everything the trajectory would have to displace, and at a graze that is
arbitrarily much. So a round that would punch through a roof does exactly that
instead of bouncing, and the same roof still skips the bullet that arrives spent —
or arrives at a few degrees of graze. The folk rule "thinner than the calibre
never ricochets" falls out as the special case it is, with the speed and the mass
in it.

For sheet metal the skip angle therefore emerges from the ballistic limit itself:
1.0 mm of tin refuses a 9x19 at muzzle speed once the slant passes about four
millimetres of steel, which happens below roughly ten degrees of graze — where the
forensic sheet-metal tables put it. The tabulated critical angles are left doing
their real work on the massive surfaces, where the gate is trivially open.

Bulk media are gated the same way, on whether the path at this obliquity would stop
the projectile. The first edition left them ungated, on the argument that a bounce
off wood or soil is a surface phenomenon — a trough dug and climbed out of — and for
a semi-infinite medium that is true and the gate changes nothing: the medium stops
the round, so it may bounce. What the argument missed is the thin bulk member. A
table top is 20 mm of pine, a standing shooter meets it at 10–16 degrees of graze,
and a P90 crosses that whole slant with most of its speed to spare — there is no
trough to climb out of when the far face is nearer than the stopping depth. In play
that read as tables mirroring rifle fire, and it is the observation the gate's
extension came from. Surfaces, logs and walls keep their bounces; planks stop
pretending to be armour. Zero-cost crossings (water, mud, mesh) are not gated at
all — refusal does not apply to something that never resists, and their bounces
live on their own classes.

Wood also left the shared "Soft" ricochet class over the same raid observation. The
25–30° critical angles in the forensic tables belong to yielding granular ground —
soil and sand roll a bullet out of the trough — while wood's fibres cut instead of
yielding and its tabulated angles sit at 12–17° for handgun rounds, less at rifle
speed. The book now carries a `Wood` class at 15°; at P90 velocity that scales below
a standing shooter's table-top graze, which together with the refusal gate is what
retired the mirror.

What leaves is slower and flatter:

```
k_r        = Retention · (1 − RicochetLoss · α/α_crit)
tan α_out  = RicochetFlatten · tan α_in
```

Retention is highest at a grazing bounce and lowest right at the critical angle,
where the projectile has nearly buried itself before coming out. The shipped values
put a hard surface between 80% and 40% of the impact speed, which brackets the
50-80% the forensic literature reports for shallow ricochets off concrete, and a
soft one at half that. The departure being flatter than the arrival is one of the
few things that literature is unanimous on, and scaling the normal component of the
mirror reflection by 0.5 is the same statement as the tangent relation above.

The shipped critical angles are 17° for hard surfaces (concrete, stone, asphalt,
steel, tile), 25° for soft ones (soil, gravel, wood) and 7° for water — the last
being one of the best-measured numbers in the whole ricochet literature. Materials
that bounce nothing (glass, cloth, cardboard, wire mesh) say so explicitly rather
than by omission.

### What stays vanilla

Water's penetration (a medium of unknown depth, and vanilla's huge deflection is a
fair reading of that; only the bounce is ours), gratings (whether the bullet went
between the bars is honestly stochastic geometry), tall grass (vanilla gives it no
settings at all), tyres, the default collider an unconfigured object gets, and
every material that belongs to the body and armor models. Each of them is named in
the book with a mechanism of `vanilla`, so a material left out by accident and one
left out on purpose can be told apart.

A `vanilla` preset is a statement about the preset, not about the object: the
resolution layers run first, so a collider on one of these whose own name says what it
is made of — `MetalNoDecal` is 209 colliders saying `metalthin` — is that material and
never reaches the vanilla branch. What stays vanilla is what nothing said anything
about.

Fragmentation against an obstacle does **not** stay vanilla, and it is the last
thing here that stopped being a roll. Vanilla rolls the cartridge's chance against
the collider's, and a projectile that loses replaces itself with fragments carrying
`0.7 / MaxFragments` of its speed — a flat 77% velocity loss, which is 95% of the
energy. A measured raid put that at sixty-three events, every one landing on exactly
23.3% of the arrival speed, the worst of them a 5.45 steel core "destroyed" by
0.7 mm of sheet metal the model had priced at a 6% loss. It is decoration rather
than material, too: one scene object carried a 0.65 fragmentation chance where its
own preset says 0.07, so two bullets in three came apart on that one prop.

What happens to a projectile in a barrier is already computed, so it decides this
as well: a core that merely mushroomed is modelled as mushroomed — blunter and
lighter through `ArmorExit` — and only a core that **shattered** has stopped
existing as a projectile.

The consequence is worth stating plainly rather than discovering later: with the
shipped book that is nearly never. Shattering needs a brittle core — tungsten
carbide, above 1000 HV — meeting a face hard enough to crack it, and of every
material in the book only loose aggregate clears that ratio. Environment
fragmentation therefore all but stops happening. That is the correct reading of "a
bullet does not disintegrate on tin"; what it costs is the real case at the other
end — a soft rifle bullet coming apart against thick steel — which the model
currently expresses as heavy deformation and mass loss rather than as pieces.
Vanilla still decides for any material the book does not claim.

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
probability roll — a hole in you is a hole blood comes out of. Fractures come from
actual bone hits with energy behind them rather than from a damage-number lottery:

```
P(fracture) = clamp01( P(bone | limb segment) · ramp · multipliers )
ramp        = clamp01((E − E_min)/(E_full − E_min))
```

The bone term is per segment, because a femur and a tibia are not equally exposed,
and it is the same roll that decides whether the bullet stopped in the limb — a
round arrested by a femur and a broken femur are one event, not two dice. That
sharing is why the term is not free to be tuned for fractures alone: halve it and
limbs overpenetrate more often by exactly the same factor. It is one number
answering one physical question, and both consequences follow from it.

It is also the least anchored quantity in this section. It stands on the cross
section a long bone takes up in a limb rather than on any published series, and it
spent the mod's whole history unexercised — the fracture roll never reached it, so
nothing it produced was ever seen. The figures shipped today are half what they
were when they were first written down, on the evidence of the first raids in which
they did anything at all. What would replace the estimate: a gelatin or cadaveric
series reporting bone involvement per limb segment.

The outer clamp is not decoration. The ramp saturates at a few hundred joules, so
above that every multiplier on top of it — the one for a wrecked limb, the ones per
side — is pushing against a probability that is already 1. Rifle energies therefore
break the bone they find whatever those are set to, and the multipliers only bite in
the ramp's own range, which in practice means handgun and fragment hits. Where they
should bite is a question about `E_full`, not about them.

**A blacked-out limb is wrecked, not absent.** It has stopped doing its job; it has
not left the body, and nobody carries it to the surgeon separately. So it goes on
bleeding, and it goes on breaking — with a *higher* chance than a sound one, since
the bone in it has already been struck and the tissue that was bracing it is gone.
The game takes the other reading and refuses to fracture such a limb at all, so
this is a deliberate departure: the multiplier is a config parameter, and the
effect is added through the same call the game uses, only without that one guard.
A limb still above zero goes through the game's own path unchanged, so the
departure is confined to the case the game would have refused.

Whether it bleeds *badly* is decided by what the channel crossed, not by what was
fired. A round does not carry a bleeding rate around with it: it cuts whatever was
in front of it. A channel of diameter `d` over a length `L` sweeps a plane of
`d·L`, and the vessels it opens are the ones that crossed that plane — a Poisson
process in the length of vessel per unit volume:

```
swept  = sqrt(4 · V_cavity · P / π)
P(art) = 1 − exp(−density_region · swept)
```

The cartridge is still in it, but through the channel it actually cut. Only the
density knows any anatomy, and it is kept in the regions the combat mortality data
is kept in: general torso, junctional (neck, groin, shoulder — where a vessel runs
and a tourniquet has nothing to squeeze against), limb, head. Calibrated so a
rifle round across a chest lands about where the old per-cartridge chance was.

The great vessels are deliberately absent from that term. The aorta and the vena
cava are in the mediastinum, and the retrohepatic vena cava runs *through* the
liver — which is why those wounds kill and why there is nothing to press on. They
open an internal bleed instead, scaled by how much of the organ was involved, and
no bandage, tourniquet or hemostatic reaches it. Counting them in both places
would have been the same vessels twice, once in a form a field kit closes.

### Winded

A heavy blow to the torso knocks the breath out — a transient diaphragm spasm and
vagal response, not an injury. It happens well below the energies BABT reads as
trauma, and it happens whether or not the armour held, which is why it is its own
term rather than a side effect of damage.

The insult is the energy the torso actually received, in joules: the behind-armour
energy `E·BluntThroughput` for a blocked hit — the same quantity BABT is computed
from — and the temporary-cavity deposit for a penetrating one. Reading the stretch
component as the blunt insult gives the penetrating case its known physiology for
free: a pistol through-and-through sits below the Fackler sigmoid, deposits almost
no cavity and barely disturbs breathing; a rifle wound winds hard. Pellets landing
in the same frame sum into one blow before the ramp is read — eight buckshot
impacts on a vest are one strike to the chest, not eight small ones. Hits on
limbs, junctions and the head do not reach the diaphragm and are excluded.

Severity is a linear ramp between two configured energies. The onset (60 J) sits
where thoracic less-lethal impact studies (Bir) begin recording transient
respiratory disruption in volunteers, of the order of the historic 79 J (58 ft·lbf)
casualty criterion. Saturation (300 J) is calibrated at the edges: a blocked 12ga
slug on a steel plate (~430 J behind it) must saturate, and a blocked 9x19 on an
aramid vest (~220 J) winds about two thirds. A spent bullet dying in the far panel
of a vest (tens of joules) does nothing.

What severity `t` does: both stamina pools (legs and arms) are multiplied by
`1 − t` and their restoration is held for `t` times a configured maximum (10 s)
through the game's own downtime deadline — an empty pool with no restoration is a
player who cannot sprint, and the heavy breathing and sway follow from the vanilla
exhaustion machinery untouched. A bot does not run on stamina, so it receives the
mover's sprint-pause deadline for the same duration, and a blow that saturates the
ramp can disorient it outright for a configured few seconds (3 s; its own switch,
off by default): the bot falls back
away from the shooter and — only if its own vision had the shooter at the moment
of the blow — mag-dumps at where it believes the shooter is: a point drawn once on
a one-metre circle around the shooter's torso, sprayed around between trigger
pulls, fired through the bot's own trigger path so reloading and readiness stay
its own logic. Hit from a direction it never saw, it only falls back. The effect is deliberately
custom rather than the vanilla flashbang: the flash machinery wipes the bot's
enemy memory and posts a search point for its whole group, and raid testing had
entire groups standing dazed at walls, deaf to a visible player. Per-side
switches; the player's own is a section-7 choice.

Deliberately simplified: the ramp is linear because the volunteer data gives a
band, not a curve; the criterion is energy rather than chest-wall velocity (the
viscous criterion needs a deformation history no game hit carries); and the blind
effect borrows the flashbang's machinery wholesale rather than inventing a
concussion model for AI.

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

The number is therefore **a ranking, not a contract**. It exists so two cartridges
can be compared under identical conditions; it is not a promise about any
particular raid hit, and a hit that lands above or below it is the model working,
not a bug. How far off it can be, and why:

- **The chord.** The card assumes 250 mm of tissue. A grazing hit crosses a few
  centimetres and deposits almost nothing; an oblique shot through a torso can
  cross 400 mm and deposit more than the card. This alone spans roughly 0.1× to
  1.5× the card value.
- **Distance.** The card is at muzzle velocity from 5 m. At 200 m a rifle round
  has lost enough velocity that both the crush and the stretch terms are down —
  tens of percent, more for light fast bullets.
- **Anatomy.** Organ zones multiply what the chord deposits: the same chest chord
  reads differently through lung, liver or heart. Limb hits stop in bone or exit
  early.
- **What armour left of the bullet.** After a plate, the round arrives slower,
  deformed and sometimes lighter; the card knows nothing about armour.

So "the card says 110 and the raid log says 95" is not a defect report — it is a
hit at range, or off-centre, or through less body than the reference chord. A
defect report is a raid hit whose damage cannot be reproduced from its own logged
physical state (`events.log` carries the full layout per hit).

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

The book can also carry a measured **length**, `LengthMm`, which nothing in the
game database holds at all. It is optional and partial by design; where it is
absent the geometry infers a length from mass over calibre. The server bakes the
card's damage through whichever of the two applies and ships the published figure
to the client with the rest of the per-cartridge data, so both halves compute one
channel. See [Bullet length](#bullet-length).

The book can also override the **mass**, which it otherwise takes from the card.
That exists for one shape of cartridge: a sabot round leaves the barrel as its
penetrator alone, so a card listing the whole projectile at the velocity only the
penetrator reaches describes something that never arrives — and it shows up as an
energy the case cannot deliver. The mass that belongs there follows from the two
figures that do hold: the calibre's own service energy and the stated velocity.

For cartridges the book does not name — modded ammunition, mostly — `X` is inferred
as a percentile blend within the caliber cohort (specific damage positive, specific
penetration negative; the vanilla fragmentation chance used to be a third component
and is gone — the model derives fragmentation itself, and feeding the game's
opinion of it back into `X` would be circular), and the core is read off how far
the round's penetration sits above what its energy density buys. At the cohort median
that comes out monolithic, which is the truth for most of them. Cohorts smaller
than a threshold fall back to a global regression.

### Buckshot

Pellet mass is derived from lead density and nominal pellet diameter, and pellet
count from charge mass divided by pellet mass. Vanilla puts eight pellets into
almost everything, which under-loads small buckshot by a factor of two to four.
Flechettes are steel and keep their shape, so they take a low `X`: deep, narrow,
poor at wounding.

### Muzzle velocity by barrel length

A cartridge has one `InitialSpeed` in the database, and the barrel it is fired
from moves it. Velocity against barrel length follows Le Duc,

```
v(L) = v∞ · L / (L + C)
```

so the modifier a barrel of length `L` carries is relative to the reference barrel
`L_ref` of its caliber — the service weapon the cartridge's `InitialSpeed` is
quoted for, so a barrel that length changes nothing:

```
percent = 100 · [ (L / (L + C)) / (L_ref / (L_ref + C)) − 1 ]
```

`C` is fitted to a published barrel-length ladder where one exists, and derived
from case capacity otherwise (`1.67·V_case / A_bore`, worth about ±35%). Both,
along with `L_ref`, are per-caliber entries in the reference book.

Two things about those entries are worth stating because both have been wrong in
practice. `L_ref` has to be a barrel something chambered for the cartridge
actually has: the .50 AE was entered against 400 mm, a length no Desert Eagle —
the only gun that fires it — comes near, and every Desert Eagle in the game paid
13% of its muzzle velocity for that one number. And the case rule is a rule of
thumb that a small bottlenecked case at high pressure breaks: it derived 94 for
the 5.7x28, which puts the Five-seveN 24% below the P90 where FN publishes 9%,
so that caliber carries a measured 24 fitted to the maker's own pair instead.
Where a case-derived constant produces a modifier that measurement contradicts,
the constant is what is wrong.

**Which part the length model applies to** is decided from the item database, not
from what the item is called. Vanilla names every barrel `barrel_*`, but weapon
packs register clones through WTT's item service, which names them
`[Pack]_(whatever the locale says)`, so a naming test misses every modded barrel
in the game. A part is a barrel when its class is Barrel, or some weapon lists it
in a `mod_barrel` slot, or it carries `CenterOfImpact` and `ShotgunDispersion` —
the two properties that in the whole vanilla database belong to barrels and whole
weapons and to nothing else. Length and caliber are then read off the item's name
and locale text, in millimetres or inches, and a caliber is matched either by its
dimensions or by the trade names in the reference book (".300 Blackout",
".338 Lapua"); a name claiming two calibers decides nothing and the slot graph
votes instead. A barrel sold under a rifle's name rather than a length — "MK-12
Mod 0 Barrel" — takes that rifle's barrel, by the same prototype match the
weapons use.

**A weapon whose barrel does not come off** has no barrel item to read, and its
length exists only in the prototype it is modelled on: the reference book carries
one per such weapon, keyed by the template's name. A pack that rechambers a
vanilla weapon rewrites that key — an AKS-74U reappears as
`[Pack]_(Kalashnikov AKS-74U .300 Blackout Assault Rifle)` — so two further
witnesses are asked, in this order:

- **The model it is drawn with.** A rebrand that ships no model of its own is
  the weapon it is drawn as: same geometry, same barrel, and a rechambering does
  not move the muzzle. Where a pack item and a weapon the book knows share a
  prefab, they share a length. This outranks the name, because what a weapon is
  built as is a fact and what a pack writes on it is a claim: the item called a
  Century Arms Draco wears the vanilla AKS-74U and is 206.5 mm, where the real
  Draco — a Romanian PM md. 90 derivative sharing nothing with it but a name —
  is 311 mm.
- **The prototype's own name**, matched against what the item is *called* and
  never against what is written about it. Whole names only, longest first: an
  AK-12K is not an AK-12 and its barrel is shorter, so a prefix does not count.
  The description is excluded because it is prose and prose names other guns —
  the AS-1 is a bullpup built on an AK-74M whose card recounts the trials the
  AK-12 went on to win, and reading that as "this is an AK-12" would be luck
  rather than reasoning. A barrel's description describes that barrel and is
  still read for a length; a weapon's is a history lesson.

A weapon that is neither drawn as something known nor named after anything known
keeps whatever modifier it shipped with rather than borrowing a neighbour's.

**A part with the barrel built into it** — an MP5SD upper receiver, whose ported
146 mm barrel exists as no item — has no length to read and no model to apply: its
gas ports, not its length, are what put the round below the speed of sound. Such a
part is recognized structurally: it owns the muzzle slot, and no weapon that can
mount it has a barrel item anywhere in its tree. Its figure comes from the
reference book as the total the weapon should end up at, and because the game adds
the weapon's own modifier to the part's, the part is given the difference:

```
part = TotalPercent − percent(host weapon)
```

**Muzzle devices** — brakes, flash hiders, suppressors — are clamped to
`DeviceClampPercent` (2%), which is about what a can or a brake is worth. The
clamp requires positive evidence that the part is one: its class, or the muzzle
slot it sits in. Anything the database does not identify keeps its modifier and is
listed in the report instead, because the two mistakes do not cost the same. A
barrel mistaken for a brake gives an 8.5 inch .300 BLK the ballistics of a
16 inch one with nothing on screen to say so; a handguard mistaken for something
unrecognized costs a line in a report.

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
- permanent and temporary cavity per HP: the combat-mortality research figure of
  about **2.3 rifle hits to the torso (85 HP) to incapacitation** — roughly 37 HP
  a hit. The two constants (`WoundVolumePerHp` 381 mm³, `TcEnergyPerHp` 74 J) are
  one calibration and move together. They used to be anchored to the vanilla
  damage of two cartridges (9×19 PST ≈ 54, 7.62×39 PS ≈ 57), which calibrated the
  model to the game's own invented numbers; against the research anchor the
  permanent-cavity share roughly doubles and the stretch share falls to about a
  third, so pistol rounds (almost all PC) gain relative to rifle rounds (much of
  whose damage is TC). This is a whole-balance change, and its verification is a
  raid, not an argument.
- penetration scale: M61, M995 and PS mapping close to their vanilla ratings
- armor classes: the specific energy of each GOST class's test cartridge
- blast: one reference grenade's vanilla strength against its real charge

The armor constants follow a stricter rule — **strengths are published, free
constants come one per failure mechanism**, derived in a fixed order so that no
constant absorbs another's data (the derivation is repeated by CalibrationTests
against the shipped values):

0. **The projectile first.** Every constant below is fitted through a core, so the
   core has to be right before any of them mean anything: 5.3 g at 6.2484 mm and
   **570 HV**, measured, where the ladders used to assume 730 HV — which is what
   the same database measures for *tungsten carbide*. Hardened steel AP cores come
   in at 570 (.30 M2 AP), 595 (14.5 B-32) and 630 (.50 M2 AP). Re-reading it, plus
   the jacket-carry rule above, moved `DuctileK` from 4.69 to **2.64** and the
   hardness exponent from 1.32 to **1.96** without either changing what an AP round
   does to a plate — the two errors had been cancelling.
1. `DuctileK` from the RHA V50 ladder alone; the steel-certified Russian plates and
   titanium's own ladder point are then checks, not inputs.
2. `HoleGrowthK` from the mild-steel ladder — which identified it only as a
   bound; see "The ballistic limit" for what that ladder's shape actually says.
3. The hardness term, in two halves along the core's fate (see "The ballistic
   limit"). The **rigid** half keeps its whole derivation: the exponent from what
   separates hard plate from mild against the same core, the floor (0.30,
   anchored on a 7N21 against aluminium and bounded above by the RHA ladder's own
   factor, past which a softer plate would out-earn a harder one), the ceiling.

   The ceiling was 4.5 and its anchor was **circular**: the two steel pistol rungs
   it stood on are *computed*, solved from their own class's cartridge at this very
   clamp, so any ceiling produces a thickness that clears the certificate and the
   certificate confirms nothing. Sweeping the clamp from 4.5 to 1.0 moves not one
   certified product in the corpus — they are ceramic, fibre, or met by an AP core
   the clamp never reaches. What pins it is the single certificate where a soft core
   meets a hard plate: a 0.25-inch AR500 plate against six shots of M80 ball, 9.5 g
   of lead alloy at 847 m/s into 580 HV steel. That plate holds down to 2.077 and
   fails below, so 2.08 is the floor it demands and the value sits on it, as
   `FibrousK` sits on its own. At 4.5 the same plate read 47% over its certificate,
   and 6.5 mm of titanium stopped a .50 BMG. The two computed pistol rungs were
   re-solved at the new clamp: 1.3 → 1.9 mm and 1.7 → 2.5 mm.

   The **dead-core** half is new, and every one of its constants is a window the
   corpus pins from two sides, with the value sitting inside and CalibrationTests
   probing both walls:

   - `DeformCoreMaxHv` **480**, window (390, 570): the pre-1989 PS core deforms
     on titanium (6B3TM passport) and the 570 HV M2 AP is rigid through the
     whole RHA ladder, top rows included — which is also what keeps the ladder
     deriving one constant, since a mid-ladder branch flip would tear the
     per-row solutions apart.
   - `DeformPlateSupport` **0.14**, window [0.096, 0.199): below it titanium's
     stagnation pressure alone is 109 MPa short of crushing the PS core at
     720 m/s; at the top the 9x18's 250 HV core would die on a Бр1 panel at
     335 m/s and the Бр1/Бр2 rungs stop being distinguishable.
   - `DeformSpreadExponent` **0.33**, window [0.323, 0.405]: the smallest value
     at which M80 still drives the AR500 certificate up to the ceiling it pins,
     bounded above where the 6B3TM would start stopping the SVD its passport
     sends through. Sits just over its own floor, as the ceiling sits on the
     AR500's.
   - `DeformFloor` **1.05**: a core that dies on the face loads the plate over
     more than its calibre, never less — 1.0 is the physical floor, and the
     6B3TM passport demands 1.035; it sits just above.
   - `BrittleCoreMinHv` **1000**: a material bound, not a fit — hardened tool
     steels end near 800 HV and the space above belongs to carbides and
     ceramics, which fail by fracture.
   - `ShatterRatio` **0.47**: a hair inside the one documented shatter, the 6B23
     certificate's 613/1300. One-sided — it says this ratio suffices and nothing
     about softer plates.

   What would close the windows properly is the experiment that varies core
   hardness against one plate: Anderson, Hohler, Walker & Stilp, "The influence
   of projectile hardness on ballistic performance", *Int. J. Impact Eng.* 22(6)
   1999 — 17 penetrator materials, one target, V50 each. No accessible copy
   exists; its published conclusion (effectiveness rises monotonically with
   hardness over 200–750 HV) is consistent with a threshold in the window but
   does not place it.

   One measurement did arrive after the fact and confirms the branch from
   outside the corpus. TNO's "deformation gap" (van der Jagt-Deutekom & Broos,
   PASS 2024, `10.52202/080042-0031`): the standard 30 HRC FSP starts to
   deform on UHMWPE helmet shells around 550 m/s, and the same FSP hardened to
   60 HRC never deforms — and penetrates **86 m/s more easily**. A projectile
   that dies on the face is easier to arrest than one that arrives intact,
   which is `DeformFloor > 1` measured by someone else, and the hardness
   boundary between the two behaviours sits between 30 and 60 HRC — bracketing
   the `DeformCoreMaxHv` window from both sides on an independent projectile.
4. `BrittleK` from the bare-tile DOP point read one-sided ("the limit is at or
   above the velocity fired"), with the ceramic certificates pinning where in
   the tile's band the constant sits — **0.98**, the smallest value at which
   every certified ceramic product without a recorded shortfall holds its class
   at the criterion the tests enforce (zero-of-five), the Бр4 Granit against
   the 7N10 binding. Since the backing became its own layer, the tile and the
   certificates agree — the 2.5× they used to disagree by was the backing's
   work hiding inside the constant.

   Two rules the value's history taught. The requirement is read at the
   *enforced* criterion, not the bare test velocity: the old 1.04 was derived
   at bare velocity, agreed with enforcement by coincidence, and outlived its
   own justification the day the backing data improved. And only products
   without recorded shortfalls may demand anything — a recorded miss is a
   documented gap (the erosion term, 3.2), and letting it bid would push the
   constant up to hide the very physics its entry documents. Re-deriving also
   re-measured every ceramic allowance: the backing fixes had left up to 13%
   of hidden head-room inside them, room a regression could have crossed
   unseen.
5. `FibrousK` on the certificates it has to satisfy, read as a floor rather than
   a fit — 28.8, with the sewn Бр2 package binding. It is no longer the mode with
   no ladder: ten para-aramid points now sit under it and derive 23.1, and the
   ladder cannot become the anchor until the thickness law is fixed, because on its
   own value a dozen certified plates stop holding their class. See "The ballistic
   limit"; this is now the model's best-documented weakness rather than its
   least-documented one.
6. The **packing exponent**, fitted against all of the fibre evidence at once —
   ladders at 0.48–0.72 packing, pressed plates at 1.0, sewn packages at 0.44 —
   and landing on **1.0**, which is to say the linear law the model already had.
   The two aramid ladders on their own solve 0.38 and agree with each other
   exactly at it; the same value moves every pressed plate from 12% away to 32%
   away, and the difference between a woven cloth and a unidirectional laminate is
   not only packing. What would settle it: one construction at three packings.

**What the fixture has never seen: a heavy large-calibre projectile.** Every point
behind every constant above is a 3.5–10.7 g core of 5.6–7.8 mm. A .50 BMG is 42 g
at 12.7 mm — four times the mass and one and a half times the calibre of anything
the model was fitted through — and against it the model reads plates far stronger
than they are. Two measurements say so, in the same direction:

- The model's own `v50` ratio between .50 M2 AP and .30-06 M2 AP against one plate
  is **0.65–0.74**. Published penetration says a .50 AP defeats about twice the RHA
  a .30-06 AP does at the same range, which for a plugging plate (`v_bl ∝ T`) makes
  the ratio **0.50**.
- Nothing worn on a body stops a .50 BMG, and the model has 13 mm of 6B5-15's
  ceramic holding one at 985 m/s head-on — above the 847 it arrives at, at every
  angle — and an ESAPI at 835, a coin flip. It also puts the 1980s 6B5-15 above a
  Level IV ESAPI, which is its own tell.

The gap is a factor of about 1.5 in `v50`, and it is not the hardness clamp: the
brittle branch has no hardness term at all and misses by the same margin. It is the
extrapolation itself. What closes it is `v50` rows at .50 calibre — the REL database
that supplied the obliquity trials above has them — read into the ladder alongside
the 7.62 rows, so that the `d` and `m` dependence is measured across a range instead
of assumed from one end of it.

**The obliquity the model is fed, as opposed to the law it feeds.** The secant law
is verified to 45°; what reaches it is the surface normal of whatever hitbox the
game's own raycast struck (`HitNormal` is `RaycastHit_0.normal` and nothing else).
Over 330 armour hits in one raid that angle never once came in under 14°, and its
median was 33°. No surface that ever faces the shooter squarely can produce that —
a cylinder hit uniformly across its width puts about 17% of hits inside 10°. Since
`v50 ∝ 1/cos θ` in every branch, a 33° median is a flat **+19% on every limit in the
game**, and the 60° tail doubles them. Whether the game's hitboxes are genuinely
that sloped or the plate zones are being struck on faces that are not the plate's is
open, and it is measured by the angle now printed on every armour line in the event
log rather than argued about.

The wound constants used to be anchored to vanilla for two cartridges, on the
argument that the model should change the shape of the damage curve rather than
every number. That argument lost: a model whose scale is calibrated to invented
numbers inherits their invention, and the research anchor above replaced it. The
sketch this came from carried a `TissueElasticity` term for the stretch damage;
in the model that role is split between two things that already exist — the
Fackler velocity sigmoid (whether stretch tears at all) and the organ-zone
severities from the AIS tables (how much a given tissue minds being stretched:
`KHeart`, `KLiver`, `KSpine` against the lung baseline). It is deliberately not a
third, separate knob.

## What is deliberately not modelled

- **The survivability overrides are not part of the model at all.** Four switches
  let a player be harder to kill than the physics says: a floor of 1 HP under their
  own head and thorax, a multiplier on the chance a hit on them starts a bleeding,
  a multiplier on the chance a bullet breaks one of their bones, and their own
  critical organ and vital-zone damage turned off (config section 7, "Player
  Survivability"), plus arms and legs removed as a route to death for everyone
  (section 3, since it changes bots too). Nothing here is derived from
  anything, and none of it is on by default — the defaults leave every formula
  below untouched. They exist because wanting to survive a raid is a
  legitimate thing to want, and the honest way to give it is a labelled switch
  rather than wound constants quietly tuned until the numbers feel kind. Everything
  else in this document describes what happens with all four at their defaults.

- **What gas ports do to a barrel.** A ported barrel bleeds propellant gas to
  bring the round below the speed of sound, which is not a length effect and is
  not derived here: the two such items in the game carry one measured figure each
  from the reference book, and a ported barrel nobody has written an entry for
  keeps whatever modifier it shipped with. The same goes for a part carrying a
  velocity modifier that the item database does not identify as a barrel, a
  device, or anything else — it is left alone and reported rather than clamped on
  a guess.
- **Organ shape.** The zones are thirds of BSG's boxes, not anatomy: no
  ellipsoids, no per-organ geometry, and the same organ comes out a different size
  depending on which of a body part's boxes the bullet went into. The one thing
  that is resolved properly is which way round a box is.
- **A measured bullet length for most cartridges.** The reference book carries one
  where somebody published it, and it wins wherever it exists; everything else
  falls back on mass over calibre at a single lead-ish density, which is right to
  a fraction of a millimetre for a lead-cored bullet and up to a third short for a
  light steel-cored one. Coverage is partial on purpose: a density derived from
  the core fractions would be a guess resting on a guess, since for most rounds
  those fractions are themselves inferred. See [Bullet length](#bullet-length).
- **The length of a deformed projectile.** A published length is the length of the
  intact bullet and does not change when a barrier strips its jacket or upsets it.
- **A neck length per cartridge.** Where a bullet turns is modelled, but the
  median it is drawn about is one constant in calibres for every round. Published
  gelatin necks run from about twelve calibres to over thirty, and closing that
  needs a measured neck per cartridge that no template carries.
- **Bone geometry.** Bone is a probability per collider scaled by energy, not a
  skeleton.
- **A separate deposit for the flesh behind a pierced plate.** The engine spawns
  the post-plate projectile inside the torso box the plate collider is embedded
  in, so its hits on that box arrive at the back face and are discarded wholesale
  (`ApplyHit` forwards forward hits only — no damage, no armor roll). The wound
  model therefore prices the whole chord through the body part into the plate
  hit's own damage — which vanilla zeroes for a pierced plate collider and the
  mod restores after the vanilla armor call — and the discarded flesh hits stay
  discarded: applying them too would count the same tissue twice. Every such
  discard above 1 HP on a live, non-blacked part is called out in the event log
  with a `!` line, so the bookkeeping is visible rather than assumed.
- **How many fragments a bullet breaks into.** The engine draws that count, and the
  mass split needs it one call before the count becomes visible — the list of
  fragments is still growing while they are being created. The draw is therefore
  asked a second time, of the engine's own function with the engine's own arguments,
  which is exact as long as the engine keeps drawing it that way and silently wrong
  if it ever stops. Rather than assume, the prediction is compared against the true
  count where that count finally exists, and a disagreement is written to the log:
  the assumption is made falsifiable instead of being made safe.
- **Ricochet angles** beyond the floor on the cosine term; vanilla handles the
  bounce itself. The secant law the floor sits on is verified to 45° and
  extrapolated from there — see "The ballistic limit" — and no published series
  we could find reaches 60°, where a hard core stops perforating and starts
  glancing off.
- **Obliquity for a fibre pack.** The path lengthens by `1/cos θ` there too, so
  the pack gains `√sec θ`, and nothing measures it. Textile armour is reported to
  behave differently in kind from plate at angle — the projectile can push fibres
  aside instead of loading them — so this is an assumption inherited from the
  plate case rather than a result. What would close it: one soft package shot at
  0°, 30° and 45° with the same fragment.
- **Ceramic telling a lead core from a hardened one.** The phenomenon is real —
  alumina shatters a lead bullet and loses to a 60 HRC core — but at 1500 HV a
  ceramic outranks every core in the game, so the hardness ratio pinned to its
  ceiling for all of them and had become a flat 2.5× on the strength: a constant
  wearing physics' clothes, unable to distinguish the cores it existed to
  distinguish. It is removed rather than kept decorative. What would bring it
  back: one ceramic tile shot against cores of two hardnesses (a lead-cored ball
  and an AP of the same calibre), giving the two points the exponent needs.
- **The shatter gap.** A brittle core's fracture is a shock phenomenon and real
  carbide cores survive slow impacts that would shatter them at speed — so a
  correctly modelled 7N24 would have a band of velocities where it pierces a
  plate that stops it when it arrives faster. The shatter decision here is
  velocity-blind on purpose: below the rigid-branch limit both readings block,
  so the gap cannot show inside the game's velocity range, and a threshold
  velocity would be a number with no measurement behind it. What would close
  it: a V50 series for a WC-cored round against one hard steel plate, fired
  from below the shatter threshold to above it.
- **The dead-core factor's shape.** Above the deform threshold the factor grows
  as one shallow power of the hardness ratio with one floor, because four
  passport statements are all the corpus says about it — enough to pin a window,
  not a form. The experiment that would draw the curve exists and is unread:
  Anderson, Hohler, Walker & Stilp 1999, seventeen penetrator hardnesses against
  one target.
- **A cartridge with no prototype.** The 7.62x39 MAI AP is not in the Russian
  ammunition literature, on the forums, or in the patent record: it appears to be the
  game's own invention, so the usual anchor — measure the real thing — does not
  exist. What it is modelled on instead is the game's own description of its
  construction, a sabot carrying a tungsten carbide penetrator, which fixes the
  hardness against material this book already carries and the mass against the
  calibre's energy. Its **core geometry stays inferred** from where its penetration
  sits above its energy density, because nothing anywhere states the penetrator's
  diameter, and a number invented for it would decide how it fights every plate in
  the game. What would close it: a published section drawing.
- **Sewn versus consolidated backing.** The fibre panel behind a plate's face is
  modelled at laminate packing; a stitched package behind a Russian plate is
  looser than that. The book's backing thicknesses were derived from areal
  density at laminate density, so the arithmetic is self-consistent, but a
  measured package density per product would replace an assumption with a fact.
- **Ground, still.** Stone, asphalt, soil and gravel remain barriers nothing gets
  through. Concrete no longer is (see "Concrete, brick, and the free rear face"),
  but these four are surfaces rather than objects: a road, a bank, a floor of
  rubble. The probe finds no far face on them, so there is no thickness for a depth
  law to be compared against, and inventing one per map would be a worse answer than
  none. What is lost is the shot that skips off a kerb and out the other side.
- **Brick that is not called brick.** Brick is modelled as its own material, but the
  game has no material for it: it is selected off the scene object's name, so a
  brick wall a level author named something else is priced as concrete. That is the
  designed failure — the rule only ever adds a material and never takes the preset
  away — but it does mean the split is as complete as BSG's naming is, and no more.
  What would close it: a real second `MaterialType`.
- **A vehicle as one thickness.** Everything the taxonomy calls a vehicle skin is the
  same 3 mm flank, which is a car door averaged over a car: the engine block, the
  wheels, the seats, the B-pillar and the transmission tunnel are all missing, and a
  round crossing the bonnet meets exactly what a round crossing the rear door does.
  The colliders offer no way to tell those apart — a car is usually one or two boxes
  spanning the whole vehicle — so the alternative is not a better model but a
  per-prop table of guesses. Cover behind a car is therefore uniformly weaker than
  cover behind a real one, in the places where real ones are strong.
- **A door leaf that the map calls nothing at all.** A leaf is recognised by BSG's
  `DOORS` grouping node or by a name the book knows, so a leaf that has neither is
  charged as plain material. It is the same designed failure as the brick rule — the
  scene may add information and never take it away — and the same limit: as complete
  as the naming is, and no more.
- **Colliders that describe the same panel twice.** A wicket door cut into a gate is
  a hole in one sheet, but the scene carries it as a second collider nested inside
  the first, and a shot through it is charged for both. Nothing in the geometry says
  which of two overlapping boxes is the hole and which is the plate; the model
  therefore prices the panels so that the doubled crossing lands where a single one
  should, rather than trying to detect the nesting. That leaves the arithmetic right
  where it was measured and wrong wherever a map nests three.
- **What a ricochet does to the projectile.** A bullet that bounces off concrete
  is badly deformed, and the model does not say so: the bounce changes speed and
  direction and nothing else. Penetration has an energy budget to price the
  deformation against — how much of its speed the barrier took — and a bounce
  does not, so crediting one would mean inventing a third set of constants for
  the occasion. What would close it: recovered-projectile mass and deformation
  against incidence angle, which the ricochet literature reports qualitatively
  and rarely tabulates.
- **A deformed bullet getting wider.** `ArmorExit` shrinks a calibre when a
  barrier strips a jacket and never grows one when a core mushrooms, so a
  flattened bullet carries a larger deformable fraction at its original
  diameter. The wound model reads that fraction and widens the channel from it,
  so the effect is not lost — but the projectile's stated calibre is now an
  understatement, and it is the same understatement the plate path has always
  had.
- **The price of a hole not scaling with calibre.** Glass and the other
  "through for a price" materials charge one flat energy figure, so a fragment
  and a .50 pay the same for the same pane. For a sheet that fractures out rather
  than being crushed through that is close to right, and it is why the mechanism
  is only used where the price is small or symbolic.
- **A soft bullet coming apart against thick steel.** Fragmentation against an
  obstacle is now decided by the core's fate rather than by a roll, and the fate
  has two deaths where reality has three: rigid, mushroomed, shattered. A lead
  rifle bullet against 6 mm of plate really does leave as pieces, and the model
  says "badly deformed and lighter" instead — the energy bookkeeping is right, the
  number of objects leaving is not. Closing it needs a break-up criterion for
  ductile cores, which is a different measurement from the Taylor one already used.
- **Which way a pallet is stacked.** Packed cargo (see "Packed media: a carrier and
  its contents") draws its packages along the path and nothing else: the layers are
  isotropic, so the stack does not know where its top is and a shot along the boxes
  meets the same statistics as a shot across them. A real pallet is banded and
  layered, and the two directions are genuinely different geometry. Closing it needs
  the impact direction against the prop's own axes, which the collider does offer —
  but every layer of resolution above this one has cost a table of per-prop guesses,
  and a stack orientation would be another. What is kept instead is the part that is
  geometry rather than authoring: a longer path meets more cargo.
- **An IBC tote is assumed empty.** The census puts `eurocube*` on `Plastic` for the
  tank and `MetalThin` for the cage, which is already the empty reading, and a full
  one is a metre of water. Deciding empty-or-full on the first hit and remembering it
  for the raid is the right answer and is **future work**: it needs per-collider
  memory (a `ConditionalWeakTable`, as the armour hit record uses), which is a
  mechanism rather than a constant, and it would be the first thing in this module
  that is not a pure function of the shot.
- **Secondary debris a wall throws.** Concrete spall is not modelled at all, and
  neither is progressive destruction — a wall that has been shot a hundred times is
  exactly as strong as a new one, unlike a plate, which wears.
- **What a bullet's construction does to a ricochet.** The bounce is decided by
  angle, speed and surface; a hardened core and a lead round nose leave the same
  way. The real difference is that the hard one is more likely to break up on a
  hard surface instead of coming off it, and nothing in the forensic tables we
  could find puts a number on that per core material.
- **Yaw inside a single bulk barrier.** Serial barriers now destabilise a
  projectile (see "Yaw, and why the second wall costs more than the first"), but a
  crossing is still resolved at the yaw the projectile arrived with: a flechette in
  20 mm of pine is priced nose-on for the whole 20 mm, when in reality it starts
  turning inside. Closing it needs a neck length in the barrier, the way the wound
  channel has one — a distance before the turn rather than a per-crossing
  increment — and there is no published neck for wood or sheet steel to pin it on.
- **Yaw does not shorten the wound channel's neck.** A bullet that comes out of a
  wall half sideways would, in flesh, turn much earlier than one that arrives
  point-first, and the mod carries exactly the number that would say so. The border
  is drawn deliberately: the client's channel formulas must match the server's baked
  ones, and the server prices a cartridge with no idea what the bullet crossed on
  the way. Barrier yaw therefore stops at the flesh — it costs the projectile speed
  and mass on the way in, and the wound it then makes is the wound that state
  deserves.
- **The `_MedPen`/`_HiPen` suffix convention** on cloth is now read as the density
  flag it plainly is (bare `Fabric_MedPen`/`_HiPen` colliders route to the padding
  material; named sandbags route to sand first). On the metal and wood materials
  the same suffixes mirror the PenetrationLevel ladder the book already prices, so
  nothing further hangs on them.
- **The `Tyre` preset** turned out to sit on two dirty pickup hulls and a baggage
  cart's wheel — the name rules now send the hull to sheet metal and the wheel to
  rubber. Any new carrier that ever appears stays vanilla until identified.

## Sources

- **ATLS (Advanced Trauma Life Support)**, American College of Surgeons — the four
  classes of hemorrhagic shock and their symptom progression.
- **Fackler**, wound ballistics — permanent versus temporary cavity, and the
  velocity boundary above which stretch becomes destructive.
- **Sturdivan, Viano & Champion**, *Journal of Trauma* (2004) — the Blunt Criterion
  and injury-risk curves for blunt ballistic chest impact; validated in blunt
  impact research at **Wayne State University** (Bir, Viano).
- **Bir**, Wayne State University — thoracic response to less-lethal kinetic
  impacts: the energy band where transient respiratory disruption begins in
  volunteers, the anchor of the winded onset.
- **Clinical literature on behind-armor blunt trauma** in military medicine, with
  the backface-deformation limits used in armor certification.
- **GOST body-armor protection classes** and their certification test cartridges.
- **Kośla, Kubiak, Łandwijt, Urbaniak & Kucharska-Jastrzabek**, *Materials* 15(6)
  2314 (2022) — V50 against the STANAG 2920 .22 FSP for ten para-aramid packages,
  by areal density and thickness: the fibre mode's ladder.
- **Forrestal, Børvik, Warren & Chen**, *Experimental Mechanics* 54: 471–481
  (2014) — 6082-T651 against the 7.62 APM2 at 0°, 15°, 30° and 45°, bullets and
  bare cores: the obliquity law and the jacket's contribution to it.
- **Ryan, Nguyen, Gallardy, Cimpoeru et al.**, REL ballistic-limit database
  (*Defence Technology* 2023; Mendeley `10.17632/4f92y6jzzh.2`, CC BY 4.0) — 1084
  measured V50s in aluminium, titanium and steel, including the 0°/30° pairs the
  material-independence of the obliquity term is checked against.
- **Hatcher**, *Hatcher's Notebook* — penetration of small-arms bullets in
  seasoned white pine, the ladder the bulk-medium law is checked against.
- **Poncelet**, and the cavity-expansion literature after it — the two-term
  resistance law (static strength plus inertial drag) and the three-to-five-times
  confinement factor on the static term.
- **Forrestal & Altman**, *An empirical equation for penetration depth of
  ogive-nose projectiles into concrete targets*, and **Forrestal, Frew, Hanchak &
  Brar**, *Penetration of grout and concrete targets with ogive-nose steel
  projectiles*, *Int. J. Impact Engineering* (1994-96) — concrete's resistance as
  `S·f'c` with `S = 82.6·f'c^(−0.544)`, fitted across 14, 35 and 97 MPa targets and
  250-800 m/s.
- **NDRC penetration formulae** for concrete — the perforation limit standing above
  the semi-infinite penetration depth because the free rear face scabs.
- Published small-arms tests against ultra-high-performance concrete — the 55 mm a
  7.62 ball leaves in a 120 mm slab, the check the concrete strength is validated
  against rather than fitted to.
- **Haag & Haag**, *Shooting Incident Reconstruction* — critical ricochet angles
  and velocity retention off concrete, asphalt and sheet metal, and the departure
  angle being smaller than the angle of incidence.
- **Kneubuehl**, ricochet and terminal ballistics — the critical angle off water
  and its velocity dependence, and barrier penetration tables.
- **Ordnance gelatin test data** (10% tissue simulant) for penetration depth.
- **Open-source prototype specifications** for shell loads, pellet counts, grenade
  fragment mass and velocity, and explosive charge weights; plus the cube-root
  scaling law for blast.
