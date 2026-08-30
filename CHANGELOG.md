# P.L.A.T.E. — Changes vs vanilla

P.L.A.T.E. (Penetration, Lethality, Armor & Trauma Engine) replaces the abstract
damage numbers of the base game with an attempt to reproduce a real physical
model of terminal ballistics, armor, the cover in between, and blood loss. Below
is an exhaustive list of what behaves differently from vanilla and the reasoning
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
- **A blacked-out limb is wrecked, not missing.** The base game treats a limb at
  zero as gone: it will not put a fracture in one, whatever hits it. PLATE reads it
  as a limb that has stopped working while still being attached to you, so it keeps
  bleeding and keeps breaking — and breaks *more* easily than a sound one, because
  the bone has already been struck and the tissue that brace it is gone. A limb
  still above zero is handled by the game's own rules, unchanged.
- **A bot with a broken leg stays on the ground.** It used to stand up the instant
  anything asked it to — a route, a patrol, a turn, or simply being shot at — and go
  down again a step later, which read as slapstick rather than injury. Now it stays
  down and fights from there — lying still, because the game's prone command stops a
  bot where he is and its AI has no notion of moving while down. It is still let up
  for the one thing worth standing for, throwing a grenade, and a splint lifts the
  whole thing. Toggleable.
- **A surgical kit sets the bone it operates around.** Repairing a limb clears its
  bleedings (already the case) and now also its fracture — the base game leaves the
  break standing in a limb it has just rebuilt. A splint remains the cheap answer
  for a limb that is still standing. Toggleable.
- **Bones matter.** A limb hit can stop a bullet in the bone — with a fracture
  and full energy transfer — or punch through and continue into the torso.
- **Over-penetration is an energy balance.** A bullet exits a body part with
  the velocity physics leaves it, and whatever it hits next receives damage
  computed from that remaining velocity. Nothing is zeroed out by game-logic
  quirks (vanilla's occasional "no damage" pass-through cases are fixed).
- **What a body took out of a bullet stays taken out, all the way to the next
  target.** The game plans a projectile's entire flight the instant it is born, so
  a speed written onto it afterwards lasted until its first tick and no longer. At
  contact range the loss showed anyway; across a room it did not, and a round that
  had crossed one man reached the one behind him having paid nothing for the first.
  The exit speed is now part of how the projectile is created. A through-and-through
  is weak at the far end the way it always should have been, and the same applies to
  anything that crosses cover. Fragments are born with their own mass and calibre
  too, so the game's own drag finally slows a fragment like a fragment rather than
  like the bullet it came off.
- **Every bullet is its own bullet.** The engine recycles the objects it fires, and
  a recycled one used to arrive still carrying the last round's draw: where it
  turns, what tissue it crosses, and which organs it had already been through. A hit
  on a heart could therefore be read as the same shot reaching the same organ twice
  and quietly deliver nothing. Cleared at birth now.
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
profiles, and bullet length — which is what the channel widens to once the bullet
turns — is taken from a published drawing wherever somebody has measured one and
otherwise derived from mass and calibre, an inference that reproduces the measured
lengths of 7.62×51 M80 and 5.56×45 M855 to a tenth of a millimetre.

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
- **A class is what a construction earns, not a label it wears.** Game class
  `N` now means GOST `BrN`, with class 0 reserved for sub-Br1 fragment protection.
  Sewn aramid and pressed fibre shells both top out at Br2: forming the same fibres
  into a helmet does not conjure another ballistic rung. Published certificates
  and passports win over model names and vanilla labels in either direction, so a
  Level IIIA Crye AirFrame is no longer classed with construction helmets while a
  Zhuk carrier's built-in package remains Br1 regardless of which plates or skin
  are fitted to it.
- **Protection classes are anchored to the real GOST protection standard,** and
  are the fallback for armor nothing is known about. Class 6 is deliberately left
  empty for the standard's 12.7 mm AP threat. Ordinary hats, caps and bandanas have
  no protected zones and remain clothing rather than acquiring class 0 and a
  fictitious UHMWPE liner.
- **Hardness decides who wins — by deciding what happens to the core.** A
  quenched core arrives intact and the contest is between it and the plate's
  strength. A softer core is crushed on the face when the impact is fast enough —
  the same mild steel that punches titanium at pistol speed dies on it out of a
  rifle — and a crushed core loads the plate as a spread slug, not a punch. A
  carbide core is the third case: it does not flatten, it *cracks*, but only on a
  face hard enough to crack it — the 6B23's ultra-hard steel panel turns back the
  7N24's tungsten carbide exactly as its maker's certificate says, while the same
  round goes through titanium, which is too soft to break it. This is the
  difference between the two 7.62×39 loads and between the two 62-grain 5.56
  loads, and it is why 25 mm of polyethylene shrugs off a mild-steel-cored rifle
  round and is beaten by a small hard core carrying half the energy. Every branch
  of it is pinned by a published vest passport: the 6B3TM holds the mild PS its
  papers claim and is pierced by the SVD its papers admit, and the 6B23 stops all
  six rounds on its maker's schedule.
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
  the energy the armor absorbed — but whether a hit is allowed to charge that
  price is the material's decision, because the published multi-hit record
  splits three ways. A steel or titanium plate does not wear at all unless the
  hit bites past half its thickness — and a bullet that flattened on the face
  bites far shallower than a hard core of the same speed, so soft lead never
  eats a steel plate from below the limit: real armour steel has taken repeated
  AP hits *on the same spot* without losing resistance, and the "gong" that
  shrugs off hundreds of rounds is now true in game. A fibre plate pays
  durability only when actually penetrated — polyethylene panels in published
  tests *gain* ballistic limit over an eight-shot series rather than losing
  it. Ceramic keeps paying for every stop with the energy it ate, which is
  exactly the certified budget: a ceramic plate is good for roughly 2–4
  full-power rifle stops, 5–10 intermediate, 10–20 pistol — the same windows
  ESAPI's three-shots-per-threat protocol and destructive tests put real
  plates in.
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

## Environment: cover is a barrier, not a threshold

**Vanilla:** whether a bullet crosses a wall, a door or a sheet of tin is one
comparison — the round's penetration number against a number the level designer
put on the collider — followed by a coin flip weighted per material. A round that
wins pays *nothing*: the projectile spawned on the far side carries the parent's
full speed, its full damage and its full penetration. Cover is therefore
transparent to the rest of the game, and the same cartridge is as lethal through
a plank as it is in the open.

**PLATE:** an obstacle is a material and a thickness, settled by the same four
values that settle a plate or a body — mass, calibre, impact velocity,
construction. Whether it goes through, what it costs, what comes out the far
side and whether a shallow hit skips off are all computed rather than rolled.
What each material is, and how thick a thing made of it usually is, lives in
`obstacle-reference.jsonc` next to the plugin, in plain text with the reasoning
beside every number. It is its own module — on by default, switchable off to
vanilla on its own — and it needs nothing from the server half.

- **Crossing something costs speed, and the cost is still there at the far end
  of the room.** A round through a door arrives at whatever is behind it having
  paid for the door — slower, and therefore doing less when it lands, because
  everything else in the mod reads velocity at impact. Where the barrier stops
  it, it stops.
- **The thickness is the object you actually shot, not an average of its kind.**
  It is measured off the collider along the real line of flight, so an oblique hit
  is already paying for the longer path, and an electric motor is an electric motor
  where a locker door is a locker door — both are "thick metal" as far as the game
  is concerned. The book's per-material figure is the fallback for a graze or a
  shape the probe cannot resolve; the journal and the marker label say which of the
  two produced the number.
- **A drum is not six hundred millimetres of steel.** Sheet materials are read as
  a skin around air: a barrel, a plastic canister, a shipping container panel or a
  cabinet costs its wall going in and, where there was genuinely room inside for
  two walls, its wall coming out. Measuring those honestly and believing the
  measurement is what would make a fuel drum bulletproof. Timber, logs and closed
  crates go the other way and are taken at what they measure.
- **A door costs a door.** A leaf is recognised where the map says it is one, and
  what a leaf is depends on what it is made of: thin steel and plastic laminate into
  two skins over a frame, a wooden door is a fixed 50 mm of wood rather than the
  100–200 mm box its collider draws around the whole assembly, and a steel door is
  one 5 mm plate. Before that split, 1,716 ordinary entrance, interior and garage
  doors were being priced as the heaviest steel in the game. Blast doors in the
  bunkers are the exception and stay shut — nothing a rifle carries opens one.
- **What a collider is made of is read from what the map says, not only from the
  preset.** Level designers write the material into the object's own name, and
  about six thousand colliders in the shipped maps carry a name that disagrees with
  the material stamped on them — an entire shower block tagged as sheet metal, a
  thousand door frames tagged as thin wood. The map's own word wins, the scene's
  grouping (a vehicle node, a door node) is applied on top of it, and a short list
  of named objects settles the cases neither can. Every one of those layers can
  only ever *add* information: an object nobody named anything keeps the preset it
  always had, and so does any map or mod that names things its own way.
- **Cars are cover for exactly as long as a car is.** A vehicle flank is 3 mm —
  outer panel, inner panel and the window mechanism between them — and crossing
  the body pays both sides. A 9x19 meets roughly even odds on the near door across
  a pistol's useful range, arrives nearly spent if it gets through, and does not
  make the far side; a .45 does not make the near one; rifle ball crosses the whole
  car with two thirds of its speed. What is *not* modelled: every part of a vehicle
  is that same flank. The engine block, the wheels, the seats and the B-pillar are
  not there, because the colliders offer no way to find them — usually a car is one
  or two boxes spanning the whole vehicle. Cover behind a car is therefore uniformly
  weaker than cover behind a real one, in precisely the spots where a real one is
  strong.
- **Masonry behaves like masonry.** A single 115 mm course of brick stops a pistol
  round, is crossed by a hard-cored 5.45 and by a 7.62, and takes two courses to
  stop a .50. The same course in concrete keeps the 5.45 out; a 300 mm structural
  wall keeps everything out. Brick is lighter and weaker than concrete and is
  treated as its own material — the game has no material for it, so it is picked
  out by the level author's own naming, which means a brick wall somebody named
  something else is priced as concrete.
- **Wood is checked against Hatcher's pine.** Seasoned white pine takes a .22 LR
  four to six inches deep and a .30-06 twenty-seven to thirty, and four cartridges
  spanning a factor of six in energy land where his tables put them — off pine's
  own published strength and density, not off a curve bent to fit.
- **A pallet of boxes is a lottery.** Palletised cargo is a stack of near-empty
  cardboard with packed goods drawn along the path, so clipping a corner costs
  what a cardboard box costs, crossing the pallet is survivable but expensive, and
  a shot down its length usually is not survivable at all — and two rounds on the
  same line can genuinely disagree, one threading the voids and one finding three
  boxes of goods. The stack does not know which way it was stacked; a shot along
  the layers meets the same statistics as one across them.
- **The second wall costs more than the first.** A bullet leaving a barrier is
  tumbling, and a tumbling bullet presents several times its own frontal area to
  whatever it meets next: more drag, less depth, a much higher bar to clear. A
  5.45 that used to sail through twelve sheets of tin with 291 m/s still on the
  clock now dies on the sixth — three oil drums. The anchor is the keyholing
  forensic reconstructions find on targets shot through car doors.
- **A bullet through a barrier does not come out on the line it went in on.** A
  9x19 through a 45 mm pine door leaves about two degrees off, which is where the
  reconstruction literature puts it, and everything else is that number scaled by
  sectional density: heavy and slow goes straighter than light and fast. Speed by
  itself does not make a bullet wander — the physics says so unanimously — it makes
  it wander by killing the bullet, and a barrier that flattened a core throws what
  is left of it much further off line.
- **What comes out the far side is a changed projectile,** through the same code a
  plate uses, scaled by how hard the barrier had to work. Wood does not deform
  bullets at any speed a gun produces, which is why wood and water are the classic
  recovery media and the model reproduces it without being told. Steel does, and a
  hard core takes its jacket through a tin wall and leaves it in a steel one.
- **Ricochet is a property of the surface, not of the ammunition card.** Vanilla
  uses one angle window for everything in the game and then rolls the cartridge's
  chance; here each surface has its own critical grazing angle, falling as impact
  speed rises — around 17° for concrete, stone and steel, 25° for soil and gravel,
  15° for wood, and 7° for water, which is one of the best-measured numbers in the
  ricochet literature. What leaves is slower and flatter than what arrived. One
  gate sits in front of it all: **a barrier can only throw off what it could have
  refused.** A round that would punch through a roof does that instead of bouncing,
  and a 20 mm table top stops mirroring P90 fire at a standing shooter, which is
  the raid observation the rule came from.
- **A bullet does not disintegrate on tin.** Vanilla rolls the cartridge's
  fragmentation chance against the collider's, and a round that loses is replaced
  by fragments carrying 23% of its speed — 95% of its energy gone. One measured
  raid caught sixty-three of those, the worst being a 5.45 steel core "destroyed"
  by 0.7 mm of sheet metal; one scene prop carried nine times its own preset's
  chance, so two bullets in three came apart on that single object. Break-up is
  now decided by what actually happened to the core. Said plainly, because it cuts
  both ways: with the shipped book that is nearly never, and the real case at the
  other end — a soft rifle bullet coming apart against thick steel — is currently
  expressed as heavy deformation and lost mass rather than as pieces.
- **Glass costs what a pane costs.** A window is fractured out rather than crushed
  through, so it charges a flat energy price: a 9x19 loses about 5 m/s, a birdshot
  pellet loses most of what it had, and one already down to 200 m/s does not get
  through at all. Wire mesh and low grass are the same idea at no cost.
- **What is deliberately left to the game.** Water's penetration (a medium of
  unknown depth), gratings, tall grass, and the default collider an unconfigured
  object gets. Ground and road surfaces — stone, asphalt, soil, gravel — stay
  barriers nothing gets through, because they have no far face to measure a
  thickness against; the shot that skips off a kerb and out the other side is the
  price of that. Every one of these is named in the book as vanilla on purpose, so
  a material left out by accident and one left out by decision can be told apart.
- **Walls do not remember being shot.** A wall that has taken a hundred rounds is
  exactly as strong as a new one, unlike a plate, which wears; concrete spall and
  secondary debris are not modelled at all. And the model inherits whatever the
  level designer drew — map geometry was never authored to be shot through, so a
  door whose collider is twice the door is now a door whose collider is twice the
  door, instead of being averaged away.

*What this is based on:* one law for everything with substance — Poncelet's
two-term resistance, the material's own crushing strength plus the inertial cost
of throwing it aside — with the confinement factor cavity-expansion theory puts
on the static term, checked against Hatcher's white-pine penetration tables.
Steel sheet goes through the same ballistic-limit model the armour section
describes, against structural mild steel's published properties, which is
literally the same material the armour model's one non-armour ladder is measured
on. Concrete's resistance follows Forrestal's cavity-expansion fit, validated
across concrete strengths from 14 to 97 MPa and striking velocities from 250 to
800 m/s, with the NDRC perforation relation for the cone a free rear face throws
off; the check is a published test the fit was not made against, where 120 mm of
ultra-high-performance concrete takes 55 mm from a 7.62 ball and the law says 57.
Critical ricochet angles, velocity retention and the departure angle being
shallower than the arrival come from shooting-incident reconstruction, and the
water figure from Kneubuehl's ricochet work.

## Ammunition and grenade data: normalized against real prototypes

- **Bullet construction comes from a table of real cartridges,** keyed by the
  round's own name: how much of it can deform, and the frontal area and mass of
  its hard core where one is published. A cartridge is the same cartridge in every
  pack it ships in, which was not true before — the character of a round used to
  be inferred from where it sat among whatever other ammunition an install
  happened to have, so clones of one bullet could come out with different physics
  and a plain M80 could be read as an expanding round. Ammunition the table does
  not name still falls back to that inference.
- **Five cartridges carry a measured bullet length now.** No item in the game holds
  one, so the model works length out from how much mass sits behind the calibre —
  right to a fraction of a millimetre for a lead-cored bullet, and short for a
  steel-cored one, because steel is lighter than lead for the same volume. The
  5.45x39 PS came out at 20.4 mm against a measured 24.8; the 9x19 7N31, steel under
  an aluminium jacket, came out at 9.4 mm against a measured 13 — shorter than its
  own calibre, which is a model calling a pistol bullet a ball. That matters twice
  over: length is how wide the wound channel gets once the bullet turns, and it is
  the lever arm a barrier tips a bullet over with. The 5.45x39 PS, the 7.62x39 PS,
  the 9x19 7N31, the 7.62x51 M80 and the 5.56x45 M855 now use the published figure;
  the first three hit harder for it, on the card and in a raid alike, since the
  server bakes the displayed number through the same length the client uses at
  impact. Everything else stays inferred on purpose — a length invented per
  cartridge would be worse than an openly approximate one. `ammo-reference.jsonc`
  moves to version 17 and is rewritten once, with your previous copy kept beside it
  as `.v16.bak`.
- **The 7.62x39 MAI AP is the round its own description says it is.** The game calls
  it a sabot carrying a tungsten carbide penetrator; the mod was reading it as lead,
  because no core was on file for it and lead is what the absence of one means. It
  now carries the hardness of that material, which is what decides everything it does
  against a metal plate. Its mass is also corrected: the card lists the whole
  projectile at a velocity only the penetrator reaches, and the energy that produces
  is half again what a 7.62x39 case can deliver. A sabot round flies as its
  penetrator, so it weighs what the calibre's own energy allows at that speed. Expect
  it to go through steel and titanium far better than before, and through ceramic
  and fabric worse — it was living on borrowed energy.
- **The 5.45x39 PS is the cartridge that is actually issued, and its card says so.**
  The game describes it as GRAU 7N6, and that index covers two different rounds: the
  1974 original, whose core is untreated mild steel, and the 1987 modernisation, whose
  core is a harder steel heat-treated to 60 HRC. The modernisation changed nothing
  visible — not the bullet, not the marking, not the index — which is why one name
  still covers both, and the original has not been produced since. PLATE models the
  round in service: the PS meets armor on its core's own 4.0 mm instead of on the
  whole 5.6 mm bullet, which moves it from well under the PP to just under it. The
  index on the card is corrected to 7N6M to match.
- **Barrels get their muzzle velocity from their length,** by the Le Duc curve
  fitted to published barrel-length ladders, instead of the numbers a pack happened
  to type: an 11-inch .308 shipping at −31% is worth about −16%, and .300 Blackout —
  a round designed to burn its powder in a short barrel — barely cares about length
  at all. Which items this applies to is now read from the item database — the
  item's class, the slots it fits, the properties only barrels carry — rather than
  from its name. Weapon packs register their content as clones named after their
  own locale text, so a name test recognizes no modded barrel at all: in one
  install, 89 pack barrels were being treated as muzzle brakes and clamped to −2%,
  which quietly gave an 8.5-inch .300 Blackout the ballistics of a 16-inch one.
  Lengths and calibers are read in whichever units and trade names the pack wrote
  them in (millimetres, inches, ".300 Blackout", ".338 Lapua"). A weapon with a
  fixed barrel is measured by what it is built as before what it is called: a
  rebrand that ships no model of its own is the weapon it is drawn as, so the
  pack rifle sold as a Century Arms Draco is the vanilla AKS-74U it wears —
  206 mm — rather than the 311 mm of the Romanian rifle it borrowed the name
  from. Failing that, the prototype in the item's name is used, so a rechambered
  AKS-74U is still an AKS-74U even though its internal name says otherwise. The
  reference book carries the weapons the popular
  packs add — AK-15, AK-12K, AK-308, PPK-20, VPO-185, the Draco, the SAK-21, the
  Beretta Storms, the Thompson and others — each with the published figure it
  comes from. A barrel sold under a rifle's name rather than a length takes that
  rifle's, so an "MK-12 Mod 0 Barrel" is the SPR's 18 inches without having to
  say so. A weapon nobody has entered keeps whatever modifier it shipped with
  rather than borrowing from a similar name: an AK-12K is not an AK-12, and it has
  its own 290 mm entry rather than the AK-12's 415.
- **A weapon whose barrel is built into another part is handled as such.** The
  MP5SD's ported 146 mm barrel exists as no item — it is part of the upper receiver
  — and it was being clamped like a muzzle device, which handed the SD supersonic
  ammunition. It now carries a measured figure instead: 285 m/s, the velocity H&K
  quote for it, against roughly 370 m/s for the same ball from a pistol barrel.
  Parts that the database does not identify as a barrel, a device or anything else
  keep whatever modifier they came with and are listed in the mod's report, because
  a barrel mistaken for a brake is invisible in play while an unrecognized handguard
  is one line in a file.
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
- **A heavy blow to the torso knocks the wind out — armour or not.** Vanilla has
  no such notion: a plate either holds or it does not, and either way you keep
  sprinting. Under PLATE the energy that reaches the torso — behind-armour energy
  on a blocked hit, the stretch cavity's deposit on a penetrating one — drains
  leg and arm stamina in proportion and holds their recovery for up to ten
  seconds. A blocked 12ga slug empties the bar outright; a spent bullet dying in
  the back of your vest does nothing; a volley of buckshot counts as the one blow
  it is. Real thoracic-impact research is where the thresholds come from, and
  every one of them is a config knob, switchable per side with your own half in
  the survivability section. Bots can additionally be *disoriented* by a
  full-severity blow — fall back and fire blind around where the shooter stood —
  but that one ships **off by default**.
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

- **Blood volume is readable on screen.** Vanilla has no indication of blood loss
  at all, because vanilla has no blood: the panel is a drop, the volume, and the
  ATLS tier you are currently in, with the loss rate underneath it while
  something is actually draining. It is built out of the game's own interface
  font and sits where you put it. You choose whether the volume reads in
  millilitres or as a percentage, and what the zero of the scale means — the
  whole body, where a full 5000 ml is full and death arrives around 2500, or
  only the blood that can actually be lost, where zero *is* the death point and
  a healthy body reads 2500 ml or 100%. The first is the more honest number, the
  second the more honest gauge of how much trouble you are in.
  - The panel can also count down the seconds to the next hypovolemia tier and
    to bleeding out. That one is **off by default**, on purpose: it is an
    affordance no person has. Blood loss is not a quantity a casualty perceives,
    and a clinician grades shock from pulse, blood pressure and mental state
    rather than predicting a clock — badly, at that. Showing a volume at all is
    the ordinary licence of a status indicator; a countdown is a prediction the
    character could not make, so the mod does not hand it out unasked.
- F12 menu holds the gameplay-level settings, including a **damage scale** and a
  **bleed rate** multiplier (from bullet-sponge to instant-kill for the curious),
  set separately for you, PMC bots and Savage-side NPCs — so "I want a fair fight
  but scavs should not bleed out in the woods" is two sliders. Fine-tuning —
  material profiles, model constants — lives in the config files next to the
  mod, server side and client side.
- **Each half of the mod is a switch.** Ballistics, the blood system, obstacle
  physics and the debug overlay turn on and off independently in F12, and off means
  vanilla for that half rather than a softened version of it.
- An event journal records every hit with its full physical breakdown — who fired,
  from what weapon, into whom, and what the round did on the way. It is written to
  two files, not one: `events.log` takes everything unfiltered, because that is what
  a bug report is built out of and it is the version you cannot reconstruct
  afterwards, and `events-player.log` takes only what your own shots produced,
  because finding your three doorways in four thousand lines of other people's
  firefights is not reading. The names in them are the templates' internal ones,
  because the display fields BSG leaves on cloned items lie wholesale: by those,
  every .50 AE round "is" a 9x19 PSO and the Desert Eagle "is" an M1911. Please
  attach the journal to bug reports.
- **Obstacle traffic has a file of its own** (`events-obstacles-hits.log`, off by
  default), because a firefight in the open generates a wall hit for every round
  that misses and it used to be the bulk of the journal. Two modes: one line per
  wall per bullet, with the model's decision and the game's outcome side by side, or
  the hits collapsed into one line per object per fifteen seconds for walking a map
  and seeing what is made of what.
- **Optional 3D markers show where your bullets actually went** (off by default): a
  cross at each impact with a ray back along the line of arrival and a label —
  damage and behind-armour trauma on a body, the material and the thickness it was
  charged for on an obstacle, with the verdict in the colour: through, stopped, or
  bounced. Markers on a body hang off the bone, so they follow the
  target and stay put where he falls, which is the whole point of walking up
  afterwards to see where you hit him.
- **Two field aids sit in the debug section for anyone auditing the model**
  (both off, both neutral at their defaults): a ghost mode where bots neither see
  nor hear you and groups already hunting you forget you within a couple of seconds,
  and a movement-speed multiplier for you alone. They exist for surveying a map's
  cover without a firefight, not for taking into a real raid.

## Compatibility note

PLATE derives behavior from physical data: masses, velocities, calibers,
materials. "Fun" mods that ship deliberately unrealistic ammunition or armor
stats (thousand-damage bullets, weightless pellets, paper plates with high
class numbers) will produce unpredictable — sometimes hilarious, sometimes
broken — results in combination with PLATE. Mods that overhaul the same systems
(ballistics/armor/medical overhauls) are incompatible by definition. Co-op
(Fika) is untested.

The obstacle module reads the maps as they were built: the material the level
designer stamped on a collider, the word he wrote into its name, and the shape he
drew. A custom map or a map edit is handled by the same rules and falls back to
the material's own figures wherever it names things differently — it will not
break, but an object nobody has looked at is priced as the average of its kind
rather than as itself.

## Release history

### 1.4.1

Armor cards now say what their paperwork says, and class 0 finally works as an
actual anti-fragment rung instead of a UI trap. This release also corrects the
soft-armor ladder that was one step too low during the Br conversion.

- **The number on the card is the Br number.** Class 1 is Br1, class 2 is Br2,
  and so on; class 0 is the sub-Br1 anti-fragment tier. Certificates and passports
  override Tarkov's labels in both directions. That fixes entries such as the
  Level IIIA Crye AirFrame and keeps both Zhuk carrier skins at the same Br1 soft
  package even though their fitted plates differ. Class 6 remains intentionally
  empty: that rung belongs to the 12.7 mm AP threat in the amended standard.
- **Soft armor has its missing rung back.** Sewn aramid and UHMWPE packages now
  use 3.0/5.5/7.6 mm and 2.7/5.0/7.0 mm for classes 0/1/2 respectively. Br2 is the
  ceiling for both sewn packages and pressed fibre shells; pressing a helmet shell
  does not buy a fictional extra class. Zhuk-3 and Zhuk-6 built-in packages use
  their passport value of Br1 and 5.5 mm aramid, while the plates still distinguish
  the variants.
- **Class-zero armor can be inspected and can stop fragments.** EFT normally
  skips the repair, buff and armor components when an item's class is zero. PLATE
  restores those components only for durable items that actually declare protected
  zones. Galvion Caiman Hybrid, Tac-Kek, PSh-97 and other class-zero helmets no
  longer open a blank `blablabla` inspection window, and their armor enters the hit
  pipeline normally.
- **Your cap is not secretly Dyneema.** Hats, caps and bandanas share EFT's armor
  template type but declare no protected zones. They are now left alone instead of
  receiving class 0, UHMWPE and armor components. The SSh-68's anti-fragment shell
  is likewise pinned to its actual 1.8 mm steel construction rather than borrowing
  a nonexistent generic class-zero steel rung.
- **Small data fixes:** Fence now accepts plates below class 3, and the shower
  locker is treated as thin sheet metal rather than concrete.

### 1.3.2

Barrel release. If you run weapon packs, most of their barrels have been firing
with the ballistics of something else entirely, and two vanilla guns were paying
for figures that were simply wrong. No wound, armour or blood formula moved.

- **Weapon-pack barrels are barrels again.** PLATE recognised a barrel by its
  internal name starting with `barrel_`, which is true of every vanilla item and
  of nothing a weapon pack registers: packs clone through WTT's item service,
  which renames the clone after whatever the locale calls it. So every modded
  barrel in the game failed the test, fell into the muzzle-device branch and was
  clamped to 2%, the value meant for a brake. One install had 89 of them like
  that. An 8.5 inch .300 Blackout was firing with the numbers of a 16 inch one and
  nothing on screen said so. A barrel is now identified from the item database
  instead: its class, the slots that list it, and the two properties only barrels
  carry. Lengths and calibres are read in whatever form the pack wrote them,
  millimetres or inches, ".300 Blackout" or ".338 Lapua", including the Cyrillic
  x one Russian pack puts in an otherwise Latin name.
- **The MP5SD stops firing supersonic.** Its ported 146 mm barrel exists as no
  item at all; it is part of the upper receiver, so the receiver was being treated
  as a muzzle device and clamped from -33% to -2%. The SD was quietly handed back
  the velocity its gas ports exist to remove. Parts with a barrel built into them
  are now recognised by where they sit in the weapon and take a measured figure:
  H&K quote 285 m/s, against roughly 370 for the same ball out of a pistol barrel.
- **A gun is what it is built as, not what the pack calls it.** The item sold as a
  Century Arms Draco has no model of its own and wears the vanilla AKS-74U, which
  makes it that carbine rebarreled for 7.62x39 rather than the 12.25 inch Romanian
  rifle it borrowed the name from. Where a pack does ship its own model, the gun
  is the one it claims to be and gets that gun's published barrel. Thirteen
  weapons the popular packs add are now in the reference book with the figure each
  one comes from: AK-15, AK-12K, AK-308, PPK-20, VPO-185, the Draco, the SAK-21,
  both Beretta Storms, the Thompson, the Saigas and the AS-1 and AS-2 bullpups.
  An AK-12K is not an AK-12, so it carries its own 290 mm rather than the longer
  rifle's 415.
- **Every Desert Eagle gets 13% of its muzzle velocity back.** The .50 AE was
  referenced against a 400 mm barrel. Nothing chambered for it has one. It is a
  Desert Eagle round, 5 and 6 inch, and the game's own 440 to 465 m/s is the 6
  inch figure, so the 6 inch guns sit at 0% where they belong and the 5 inch at
  -4%, which is the real difference between the two.
- **The Five-seveN is no longer punished for not being a P90.** Its velocity
  constant was derived from case capacity, a rule of thumb that breaks on a small
  bottlenecked case at high pressure: it put the pistol 24% below the P90 where FN
  publish 9%. Refitted to FN's own pair, the pistol reads -9.1% and the 16 inch
  barrel +3.0% instead of +10.2%.
- **Nothing unrecognised is clamped any more.** The clamp now needs positive
  evidence that a part is a muzzle device. Anything the database cannot place
  keeps the modifier it shipped with and is listed in
  `user/mods/PLATE/plate-barrel-report.md`, which also says how each barrel was
  identified. A barrel mistaken for a brake is invisible in a raid; an
  unrecognised handguard is one line in a file.
- `ammo-reference.jsonc` moves to version 16 and is rewritten once, with your
  previous copy kept beside it as `.v15.bak`. Two shipped figures were corrected
  rather than added, and the per-entry merge cannot deliver a correction.

### 1.3.1

Compatibility release for SPT 4.1.3. Nothing in the physical model changed —
no formula, constant or calibration anchor moved.

- **The server starts again on SPT 4.1.3.** 4.1.3 photographs the item database
  just before it loads profiles and refuses to start if anything was added
  afterwards: an item registered that late is an item the profiles already read
  cannot resolve. PLATE built its nine grenade-fragment templates at the very end
  of loading, so every 4.1.3 server carrying the mod died on startup with
  `DatabaseModifiedAfterCutoffException`. The fragments are now registered before
  the cutoff, in the same early pass that registers the blood bag, while the pass
  that gives them their prototype numbers still runs last and still has the final
  word over other mods. Nothing about the fragments themselves changed. If a
  grenade is added by another mod after that point it keeps its vanilla shrapnel
  and says so in the log, rather than taking the server down; and should a future
  SPT move the cutoff again, PLATE loses its own items with an explanation instead
  of killing the process.
- **Your edits to `ammo-reference.jsonc` survive a restart.** The version the mod
  ships was written down twice — once in code, once in the book itself — and the
  two had drifted apart, so every single server start decided the file was out of
  date, copied it to a `.bak` and rewrote it. Hand-corrected figures lasted until
  the next launch, and the backup holding them was overwritten by the launch after
  that. There is one number now, the one in the book. Existing installs get one
  final rewrite to bring the file's label up to date; the contents are already the
  shipped ones, since that is what the bug had been doing all along.

### 1.3.0

- **The wound now survives the vanilla armour call.** The game zeroes the body
  damage of any hit that pierced an armor-plate collider — by vanilla design the
  flesh colliders behind the plate were supposed to deal it, and by vanilla
  geometry those hits arrive at their back faces and are discarded wholesale. In
  PLATE the full wound rides on that very hit, so a bot in a plate carrier could
  soak ten .338 rounds through the chest while his heart was being declared
  destroyed in the log, over and over, to no effect. The wound is now restored
  after the vanilla armour call — a pierced plate kills the way the model always
  said it did. The same restore removes vanilla's hidden damage cut on soft-armor
  penetrations. This is the headline fix of the release: hard-plate targets
  stopped being bullet sponges.
- **The breath can be knocked out of you now — see the new entry under Blood and
  trauma.** Winded: a heavy torso impact, blocked or not, drains stamina and
  locks its recovery in proportion to the blow. On for everyone by default, with
  per-side switches and your own in section 7. The optional bot *disorientation*
  on a full-severity blow (fall back + blind fire around the shooter's last seen
  spot) ships off by default.
- **A blood bag in the stash no longer marks the profile invalid.** SPT 4.1.2
  started validating every profile against the item database at server startup,
  before mods finish loading; PLATE registered its transfusion kit late, so a bag
  brought home from a raid tripped the check on the next server start. The item
  now registers before profiles are read. The invalid flag was never written to
  disk — profiles heal on their own once the fixed server runs.
- **The journal names the shooter and stopped repeating BSG's leftover labels.**
  Every hit line now opens with who fired and from what. Item names come from the
  templates' internal `_name` instead of the display field cloned items inherit
  from their donors — 108 of 199 vanilla cartridges carried someone else's name
  there (every 12x70 slug read "buckshot", the whole 5.7x28 line read "M855",
  every .50 AE read "9x19 PSO"), which had made more than one field report
  unsolvable. Wounds the engine throws away (back-face hits) are called out with
  a `!` marker instead of vanishing silently.
- **Deaths are counted once.** The game calls the kill routine twice per death;
  the journal used to print two DEAD lines and the wounds-versus-blood-loss
  statistics counted both. Deduplicated.
- **F12 descriptions say what the knob does** — derivations, calibration stories
  and vanilla internals moved out to MODEL.md where they belong.

- **The hardness term now decides what happened to the core, not just who is
  harder.** One clamped curve used to price every core-versus-plate meeting, and
  three published vest passports refuted it with errors in both directions at
  once. The term now branches on the core's fate: a quenched core arrives rigid
  and keeps the old physics untouched; a soft core is crushed on the face when
  the impact is fast enough (Taylor's rigidity criterion, two-material form —
  the same mild steel that punches titanium at pistol speed dies on it out of a
  rifle); a carbide core cracks, but only against a face hard enough to crack
  it. What you will meet in raids:
  - the 6B23 stops the 7N24 BS its certificate lists — tungsten carbide
    shatters on the panel's ultra-hard 44S steel — while the same round still
    goes through titanium, which is too soft to break it;
  - the 6B3TM behaves like its own passport: holds the old mild-core PS,
    pierced by the SVD through the chest;
  - deformable-tipped rounds (M855 and kin) fare a little better against very
    hard thin steel than before — a 40 HRC tip is not a punch, but it is not
    worth the old flat 2.08x credit to the plate either;
  - lead against titanium is credited at 1.79x instead of the 2.08x ceiling.
  Every constant of the new branch is pinned by a window the test corpus closes
  from both sides, and an independent measurement arrived after the fact: TNO's
  PASS 2024 paper shows a 30 HRC fragment simulator deforming above ~550 m/s
  and penetrating 86 m/s *worse* than the same projectile hardened to 60 HRC —
  a projectile that dies on the face is easier to stop, which is this branch
  measured by someone else.
- **The reference book grew six versions of armour and ammunition data** (8
  through 13), and most of it is visible on a card or at a plate:
  - **both PS cartridges are the modernised loads their index has meant since
    1989.** The 5.45 PS and the 7.62x39 PS have carried heat-treated cores
    under an unchanged GRAU index for decades, and the book now reads them as
    the cartridge actually in service — which moves both their card numbers
    and what they do to a plate.
  - **cards that name a GRAU index their round is not are corrected** in the
    item description, across every localisation — the index is written with a
    Latin N, a Cyrillic Н or a Latin H depending on the language, and all
    three spellings are handled.
  - **the 7.62x39 MAI AP is modelled as what its own description says it is**:
    a sabot round carrying a tungsten carbide penetrator — 2.0 g and 2.8 mm of
    carbide at 1300 HV, not 7.9 g of full-calibre bullet. The card weighs the
    sabot that never arrives at the target; the book weighs what flies.
  - **Russian steel panels carry their own alloy.** A 6B23 back panel is NII
    Stali's 44S at a published 2000+ MPa yield, not the generic AR500 figures
    the game's one steel material used to hand every plate. The panel now
    clears its certificate on its own steel.
  - **the ceramic lines sit on the backing they are built on**: alumina over
    UHMWPE instead of a defaulted aramid, and a stitched fabric screen counts
    only the fibre it actually holds (0.44 by volume) rather than being read
    as a solid laminate.
  - **the old Soviet vests got real constructions**: the 6B2 and the 6B3TM
    back at 1.25 mm of VT-14 titanium, the 6B3TM chest at 6.5 mm of VT-23,
    the 6B5-15 at 13 mm of boron carbide tiles, the 6B5-16 at 6.5 mm of
    titanium — each over its 30-layer fabric screen.
- **Ceramic plates read about 3% weaker in velocity.** The brittle constant
  outlived its own derivation when the backing data above improved, and is
  re-derived at 0.98 under the criterion the certificates are actually
  enforced at, with the Бр4 Granit binding. The recorded ceramic allowances
  were re-measured at the same time — up to 13% of stale headroom had
  accumulated inside them. The titanium Бр4 class rung re-solved from 11.2 to
  11.5 mm under the corrected PS.
- **Bone fractures actually roll now.** The limb segment a bullet struck was
  lost in transit to the health code, so over a measured raid of 1625 limb hits
  the fracture roll fired exactly zero times. The segment is now read off the
  collider the ballistics actually hit. With the roll alive, the rest of the
  fracture arc landed too: a blacked-out limb can break (vanilla refuses — it
  reads "blacked" as "gone"), a bot with a broken leg stays on the ground
  instead of standing up and falling over every other step (let up only to
  throw a grenade; a splint lifts the ban), and a surgical kit sets the bone in
  the limb it rebuilds. All toggleable, chances per segment in config.
- **Plates wear by what the hit did to them, not by what they ate.** The
  published multi-hit record splits by material, and the mod now follows it:
  - steel and titanium do not wear at all unless the hit bites past half the
    plate's thickness — real armour steel has taken repeated AP hits on the
    same spot without losing resistance, and the "gong" is now true in game;
    your plate carrier stops being a consumable against pistol fire;
  - a bullet that flattened on the face digs far shallower than a hard core at
    the same speed, so a magazine of soft-point rifle ball point-blank dents a
    steel panel instead of eating it — while ball arriving near the plate's
    limit still pays almost full price;
  - UHMWPE and aramid pay durability only when actually penetrated — published
    panels *gain* ballistic limit over an eight-shot test rather than losing
    it;
  - ceramic keeps paying for every stop with the energy it absorbed, which
    lands exactly in the certified budgets: roughly 2–4 full-power rifle stops,
    5–10 intermediate, 10–20 pistol.

- **Nothing about the physics changed.** No formula, constant or calibration anchor
  moved in this release. It is the blood readout and the settings around it.
- **The blood indicator is a panel instead of a text label.** Up to 1.0.0 the volume
  was a line of text pinned to the bottom-left corner — `BP 100%` — in a font that
  was not the game's, at a size that did not follow your resolution, ignoring
  anything you might have wanted to do about where it sat. It is now a proper
  interface element: a drop, the volume, the ATLS tier, and the loss rate on a
  second line while blood is actually leaving. It uses the game's own font and
  scales with the screen, so the same position lands in the same place at any
  resolution.
- **New F12 section, "8. HUD".** *Show HUD* moved here from "3. Blood & trauma",
  where it was called *Blood HUD*; if you had switched it off, it stays off. New
  next to it:
  - *Horizontal offset* / *Vertical offset* / *Scale* — where the panel sits and how
    big it is, applied live while you drag them.
  - *Units* — millilitres (`4500/5000 ml`) or percent (`90%`).
  - *Range* — what zero means. *FullVolume* counts the whole body, so the reading
    never approaches zero because death arrives around half of it. *UsableVolume*
    counts only the blood that can be lost: zero is the death point and a healthy
    body reads `2500 ml` or `100%`.
  - *Blood loss rate* (on) — the second line, in ml/s.
  - *Time estimate* (**off**) — seconds remaining to the next tier, or to bleeding
    out at tier 3. Deliberately not on by default; the reasoning is under
    [Quality of life](#quality-of-life) and in the description in F12. If you had it
    on from the day it appeared, it switches off on first launch — it never shipped
    in a release before this one.
  - In the config file next to the mod, for people who want them: the hue and
    saturation of the drop, the canvas sorting order, the gap between the lines, how
    often the second line refreshes, and a font override.
- **The panel now arrives with the vanilla HUD**, not several seconds earlier on the
  deploy screen while you are still waiting for your character.

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
- **New F12 section, "7. Player Survivability".** Everything that decides how long
  you last, in one place. Three of the switches below are new and deliberately
  override the model for players who want to survive a raid the physics would have
  ended; the fourth applies to bots too and lives with the rest of the trauma rules
  in section 3. None is on by default, and at their defaults nothing behaves
  differently from before. Five knobs that already existed moved into the new section:
  *Death from bleeding: Player*, *Internal bleeding: Player*, *Bleed rate: Player* and
  *Fracture collapse: Player* from "3. Blood & trauma", and *Damage scale: Player*
  from "2. Ballistics". Their PMC and Scav halves stayed behind. If you had tuned any
  of them, your value is carried across on first launch, not reset.
  - *Prevent death* (off) — hits can no longer kill you: your head and thorax never
    black out and each keeps at least 1 HP, including damage spilling into them from
    a destroyed limb. Parts still take damage, limbs still black out, bleedings and
    fractures still happen. Death from blood loss is not covered — that has its own
    switch, "Death from bleeding: Player", so turn both off to be fully unkillable.
  - *Limb hits can kill* (on, in section 3 — it applies to bots too) — vanilla spills
    the excess damage from a destroyed arm or leg over the surviving parts, which is
    how shot-off legs kill. Turn it off and limbs stop being a route to death for
    anyone: they still take damage, black out, bleed and cripple, but nothing carries
    over into the torso or head.
  - *Bleeding chance on hits to you* (1.0) — scales the chance a hit on you starts a
    bleeding, heavy, light and internal alike. At 0 you never bleed from being shot.
    Bots are unaffected.
  - *Critical organ hits on you* (on) — off, hits to your heart, liver, spinal cord,
    brain, jaw and neck deposit ordinary flesh damage with no lethality and no vital
    multiplier. An opened organ still bleeds; that is the chance above, not this.

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
- **Hatcher**, *Hatcher's Notebook* — penetration of small-arms bullets in
  seasoned white pine, the ladder the bulk-material law for the environment is
  checked against.
- **Poncelet**, and the cavity-expansion literature after him — resistance as a
  static strength term plus an inertial one, and the confinement factor on the
  static term. Basis of how anything with substance is crossed.
- **Forrestal & Altman**; **Forrestal, Frew, Hanchak & Brar**, *International
  Journal of Impact Engineering* (1994-96) — concrete's resistance to a
  penetrating projectile, fitted across concrete strengths from 14 to 97 MPa and
  striking velocities from 250 to 800 m/s. With the **NDRC penetration formulae**
  for the perforation limit standing above it, because a wall's free rear face
  scabs off ahead of the bullet. Basis of concrete and brick.
- **Haag & Haag**, *Shooting Incident Reconstruction* — critical ricochet angles
  and velocity retention off concrete, asphalt and sheet metal, the departure
  angle being shallower than the arrival, and the deflection of a handgun bullet
  through a wooden door. With **Kneubuehl** on ricochet off water. Basis of the
  ricochet and deflection models.
- **Open-source prototype specifications** — service manuals and public
  reference works for shell loads, pellet counts, grenade fragment mass and
  velocity, and explosive charge weights; plus the cube-root scaling law for
  blast effect.

Where reality is documented, PLATE follows it. Where a value had to be chosen to
fit the game (health pools, time scale), it is a config entry rather than a
hidden constant.
