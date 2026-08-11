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
projectile's length, which is not in any template, so it comes from the one thing
always known about a bullet — how much mass sits behind its calibre:

```
L_b = m / (A · ρ · f)
```

with `ρ` the mean density of a jacketed bullet and `f` how much of its bounding
cylinder it fills once the ogive and boat tail are taken out. That puts 7.62×51
M80 at 28.8 mm against a measured 28.9 and 5.56×45 M855 at 23.0 against 23.0.

Two things fall out of the geometry rather than being written down. A round ball
comes out the same area whichever way it faces — the square around a circle is
1.27 times its area and a tumbling projectile averages three quarters of its
widest face — so buckshot has no broadside to turn into. And a fully expanded
bullet is short and blunt, so `A_side` never exceeds `A_nose` for it either.

Known limit: one density for every bullet, so a mild-steel core reads short —
5.45×39 7N6 comes out at 20.4 mm against a measured 24.8.

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

- **Organ shape.** The zones are thirds of BSG's boxes, not anatomy: no
  ellipsoids, no per-organ geometry, and the same organ comes out a different size
  depending on which of a body part's boxes the bullet went into. The one thing
  that is resolved properly is which way round a box is.
- **A neck length per cartridge.** Where a bullet turns is modelled, but the
  median it is drawn about is one constant in calibres for every round. Published
  gelatin necks run from about twelve calibres to over thirty, and closing that
  needs a measured neck per cartridge that no template carries.
- **Bone geometry.** Bone is a probability per collider scaled by energy, not a
  skeleton.
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
- **Ordnance gelatin test data** (10% tissue simulant) for penetration depth.
- **Open-source prototype specifications** for shell loads, pellet counts, grenade
  fragment mass and velocity, and explosive charge weights; plus the cube-root
  scaling law for blast.
