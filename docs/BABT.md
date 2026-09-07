# BABT mechanical model: implementation and validation

Select **BABT model** in F12's regular Ballistics section. **Extended** is the
default and enables the transfer path described below. **Simple** keeps the
established stopped-round injury model as the compatibility choice. The separate
**BABT enabled** switch controls either mode. The selection applies to subsequent
hits without a restart.

Body hit markers show `F` (wound HP), `B` (blunt HP) and `A` (armor durability
points lost across the hit's layers). The health method's return value is the
actual total health loss, including overflow; zero remains zero. Extended's
wound/BABT contributions are allocated proportionally to that final total for
display, including health multipliers and remaining-health limits. This split
is display attribution, not two separately measured health losses. Armor loss
is measured after PLATE's durability correction and is not added to health loss.
The armor accumulator belongs to the synchronous shot scope, including nested
shots, and is consumed once by the matching victim/collider's marker.

Marker follow-up, 2026-09-05: Release client tests passed 653/653 and the solution
build succeeded. Client and server were deployed to `D:\Games\SPT_4.1.3`; both
installed DLL SHA-256 hashes match the build outputs. Previous DLLs and relevant
configuration were backed up under `dist/deploy-backups/20260905-200214-babt-markers`.
This verifies installation, not live marker appearance or Unity hook execution.

The active transfer path extends the existing penetration model with a reduced
armor/body response. Both stopped and penetrating hits can produce blunt injury.
The separate projectile-contact research solver remains available for explicit
reference scenarios. Neither entry point establishes experimentally validated
back-face deformation for an individual armor product. A numerical conservation
test is not a ballistic test of that product.

## Resolved impact and penetrating hits

`PhysicalArmorDecision` remains responsible for penetration. BABT consumes its
actual incoming and outgoing mass, diameter, velocity and expansion state, armor
construction, angle, local wear, core fate and penetration-work audit values.
It does not run another ballistic-limit decision or subtract the penetration
work a second time.

For each actually visited armor component, in SI units,

```
E_in = m_in*v_in^2/2
E_out = m_out*v_out^2/2 + sum(m_secondary*v_secondary^2/2)
E_deposited = E_in - E_out
J = m_in*v_in_normal - m_out*v_out_normal
  - sum(m_secondary*v_secondary_normal)
```

The secondary inventory is explicit. Material retained in the armor belongs to
deposited energy; material leaving the system carries energy away. In particular,
`ArmorExit.JacketEnergyJ` is an audit quantity for retained projectile material,
not another debit on top of the actual input/output energy difference.

Successive primary states must match: `out[n] == in[n+1]`. The chain therefore
telescopes from its first incoming projectile to its final residual projectile.
One aggregate impulse excites the surviving armor assembly, followed by one body
response. Adding independent body responses for every layer would spend the
shared body response repeatedly.

## Impulse-to-motion reduction

The transfer path does not invent a second projectile contact force history. It
uses an instantaneous impulse approximation and two coupled participating panel
masses. For local mass `m_l`, remaining mass `m_r`, and spatial mode factor `phi`,

```
M = m_l + m_r
mu = m_l*m_r/M
Q = (m_r/M)*phi*J
V = J/M
q_dot = Q/mu
v_l = V + (m_r/M)*q_dot
v_r = V - (m_l/M)*q_dot
E_seed = J^2/(2*M) + Q^2/(2*mu)
E_unresolved = E_deposited - E_seed
```

At a centered local impulse (`phi=1`), `v_l=J/m_l` and `v_r=0`; a translation-only
impulse (`phi=0`) moves both masses at `J/M`. Negative unresolved energy is an
inconsistent reduction, not permission to create energy or tune a damage cap.
Unresolved armor/projectile dissipation includes mechanisms the existing model
does not individually identify. It is not measured heat and is not body work.

The model then integrates panel motion, elastic/plastic deformation and contact
with the body, without reintroducing the incoming projectile energy. The full
ledger counts each store and dissipation once:

```
E_in = E_out + E_unresolved + K_all + U_all
     + W_plastic + W_brittle_fracture + W_dissipation
W_body_net = integral(F_panel_on_body * v_body, dt)
           = K_body + U_body_foundation + W_body_foundation_damping
```

`W_body_net` is a subset of that ledger, not an extra term to add to it. Returned
elastic energy reduces net body work. Interface/pad dissipation is separate from
work at the body surface. A stiff, massive plate can retain the large majority of
projectile energy in armor/projectile mechanisms while passing relatively little
mechanical energy to the body; a rifle or pistol caliber is not assigned a fixed
damage tier.

## Game injury adapter

Only nonnegative **net work on the body** reaches `BabtInjuryModel`. It reuses the
existing low-velocity contact-bruise branch of `ClientWoundModel`:

```
BABT_HP = W_body_net / Wound.EnergyCapPerHp
```

The shipped contact scale is 7 J/HP. This is a game-health conversion already used
by the wound model, not a medical conversion from joules to injury. There is no
2 HP plateau or fixed 40 HP maximum in this adapter. The computed contact area
enters the existing Blunt Criterion effects mapping; it is not applied again as
an energy multiplier. Zero work causes no BABT damage or effects.

For penetration, the wound model receives the residual projectile state and its
damage is preserved. The adapter adds blunt HP once at the final health call,
after vanilla absorbed-damage accounting. Existing winded, trauma and blood
endpoints receive the body response, rather than the projectile's entire lost
energy. The underlying effects thresholds remain game approximations requiring
BABT-specific calibration.

## Active construction and body estimates

The active resolver reads the established armor `Plates` and `Materials` payload.
Separate research profiles are optional. Product thickness, intrinsic backing,
material grade and soft-package packing retain their original provenance.
Effective flat spans, unmeasured boundary conditions and mechanical-property
substitutions are explicitly estimates. A nominal inventory footprint is not a
measurement of an armor plate's outline or curvature.

For retained structural-thickness fraction `f` from the existing local armor
wear model and net-section fraction `g = 1 - A_hole/A_panel`, the reduced laws are:

| Form | Undamaged law | Damage reduction |
|---|---|---|
| Isotropic metal or intact brittle face | `A=E*h/(1-nu^2)`, `D=E*h^3/[12*(1-nu^2)]` | Extensional `g*f`, bending `g*f^3`; isolated metal yield moment scales as `g*f^2` |
| Bonded fiber laminate | Effective laminate `A,D`, with bonded neutral-axis terms | Separate extensional and bending reductions; authored layer centers retained |
| Sewn or soft UD package | `A_s=(sigma_f/epsilon_f)*packing*orientation_share*h` and cubic membrane response | `A_s` scales as `g*f`; balanced fiber directions and secant stiffness are estimates |
| Failed ceramic face with backing | Failed face loses coherent elastic resistance; backing responds according to its own form and state | Retained fragments still contribute mass; no fictitious pristine ceramic spring |

Using penetration wear as structural degradation is itself a provisional
connection between models. It is not a measurement of remaining thickness, a
fracture mesh, a full laminate damage law or a dynamic ceramic constitutive model.
Mass therefore remains based on the original construction. Only explicitly
modeled outgoing armor mass is removed; retained projectile mass is added.
Supporting components use their own local state. A carrier's soft insert and a
plate's intrinsic backing are distinct components.

An initially coherent brittle face can also fail during a stopped-hit response.
For the supplied strain limit and the retained trial curvature,

```
z_effective = abs(z_layer_center - z_neutral) + f*h/2
q_failure = epsilon_failure / (curvature_factor*z_effective)
W_brittle_fracture = U_before(q_failure) - U_after(q_failure) >= 0
```

The time step is subdivided at this event. The surviving backing stiffness is
rebuilt at the current displacement and velocity; mass, momentum, plastic offsets
and prior body work remain in the same trajectory. The energy released by the
stiffness loss enters the fracture reservoir. This is an ideal-brittle reduction
using a supplied strain limit, not a prediction of crushing or fragment motion.

The runtime body reductions cover the thorax and abdomen:

- Thorax: an estimated anterior mass of 0.45 kg, foundation stiffness 26.3 kN/m
  and compression damping 525 Ns/m. These adapt a low-speed Lobdell-style chest
  model to a fixed-foundation reduction; they are not BABT measurements. The
  underlying lumped chest representation is discussed in the
  [NHTSA thoracic biofidelity study](https://www-nrd.nhtsa.dot.gov/departments/esv/23rd/files/23ESV-000327.PDF)
  and the [DOT biomechanical impact-response report](https://www.autosafetyresearch.org/Thor/AATD%20Phase%201%20Task%20B%20Report%20%2C%20Melvin%2C%20Biomechanical%20Impact%20Response%20%26%20Injury%20in%20the%20Auto%20Envronment%2C%20Mar%201985.pdf).
- Abdomen: stiffness 12.9 kN/m and damping 765 Ns/m are adapted from
  [Trosseille et al.'s seatbelt-loading experiments](https://pubmed.ncbi.nlm.nih.gov/17096219/).
  The moving mass is a separate geometric tissue-slab estimate,
  `rho_tissue*A_contact*h_participating`, not a fitted mass from that experiment.
  Its depth comes from the caller's body-wall setting, not measured anatomy.

Direct panel/body contact uses a unilateral numerical penalty spring with
`K_contact=100*K_foundation` and zero contact dashpot. The approximately 1% added
series compliance is a numerical approximation, not a measured trauma pad.
The construction footprint is an estimated effective contact area; the model
does not resolve an evolving pressure footprint. Head, neck and limb injury
responses retain the legacy path pending appropriate regional models.

When a resolved torso/abdomen impact cannot use the full mechanical reduction,
the active path reports `TRANSFER_ESTIMATE_FALLBACK`. It uses the existing
material throughput/spread approximation against each contact's own energy
loss and a bounded aggregate budget. This estimate can accompany a penetrating
wound, but supplies no measured or predicted BFD. Missing or uncertain outgoing
material and unknown support state are reported rather than silently called a
complete mechanical result. Simple retains the old injury scale.

Numerical time steps are selected from an upper bound on tangent stiffness,
including membrane stiffening at the available seed energy, and the damping
stability bound. The configured step is a maximum, not an instruction to skip
resolution checks. Convergence of body work is distinguished from the plate
coming to rest: free recoil can retain kinetic energy after contact has ended.

## Projectile-contact research solver

The sections below describe the earlier, explicitly authored reference solver.
Its incomplete product/contact profiles do not define the active transfer path's
construction database or decide whether an existing armor item has geometry.

### Physical contract

All mechanical quantities use SI units. Rear-face displacement, permanent
deformation, body compression and the impression in test clay are separate
outputs, not interchangeable names for one depth.

The normal force applied by the projectile must result from a contact law or a
measured, applicable force history. Projectile energy alone does not determine
the force history. In particular, neither the ballistic limit nor the inferred
depth of a crater determines rear-face deflection.

The target mechanical system is

```
M q'' + C q' + f_internal(q, state) + f_contact(q, q') = f_projectile
w_rear(x,y,t) = z(t) + theta_x(t)*y - theta_y(t)*x + w_flex(x,y,t)
```

The rigid-body and deformation components must not each receive the full
projectile energy. The same applies to successive layers. For an initially
unloaded system, the energy ledger is

```
E_in = E_out + K + U_elastic + W_plastic + W_fracture
     + W_friction_and_heat + W_body
```

The work on the body is measured at the body surface, not at the plate side of
a compressible pad. Rebound can return elastic energy; dissipative work cannot
be negative. Momentum accounting includes the body/support reactions and any
outgoing fragments.

### Implemented reduction

`BabtModel` is shared source between the server test runtime and the net471
client. It uses four translating coordinates: projectile `x_p`, local panel
mass `x_l`, remaining participating panel mass `x_r`, and body mass `x_b`.
The local and remaining masses are explicit externally resolved inputs, not
automatically the whole item's mass or a fixed fraction of it.

```
m_p*x_p'' = -F_p
m_l*x_l'' = F_p - F_contact - F_flex
m_r*x_r'' = F_flex
m_b*x_b'' = F_contact - k_b*x_b - c_b*x_b'
q = x_l - x_r
```

Projectile/panel and panel/body contact use a unilateral elastic spring with
a compression-only dashpot. If compression `s <= 0`, force is zero. Otherwise
`F = k*s + c*max(s', 0)`. The spring stores `k*s^2/2`; the dashpot dissipates
`c*max(s',0)^2`. These are declared reduced contact laws, not measured generic
properties of every bullet or pad.

For the flat simply-supported elastic plate reduction, the retained flexural
shape is `sin(pi*x/a)*sin(pi*y/b)` with its amplitude at the center. Its
generalized stiffness is

```
lambda = pi^2/a^2 + pi^2/b^2
K = D*a*b*lambda^2/4
```

This truncation does not resolve an arbitrary impact location, curved geometry,
early stress-wave propagation or edge contact. Effective masses and contact
parameters must be appropriate to that reduction. A complete numerical result
is always at most `Provisional`, including when all supplied inputs are marked
measured. It is never a claim that the full planned shell model has run.

Bonded elastic groups use an extensional-stiffness-weighted neutral axis and
the parallel-axis contribution to bending stiffness. Unbonded groups contribute
their own bending stiffness without that bonded-thickness term, but still share
the reduction's imposed deformation coordinate. Independent interlayer motion,
friction, separation and changing contact area are not resolved.

Metal yielding is an elastic-perfectly-plastic generalized spring, not a full
Johnson-Cook constitutive integration. Permanent offset is accumulated by return
mapping. The reported unloaded residual is an equilibrium estimate from those
offsets; it is not the panel displacement at the simulation's final time.

Soft fabric uses an explicitly constrained parabolic circular trial shape with
equivalent radius `R = sqrt(a*b/pi)`. With radial geometric strain from that
shape and an externally supplied package stiffness `A_s`,

```
U_membrane = 2*pi*A_s*q^4/(3*R^2)
F_membrane = 8*pi*A_s*q^3/(3*R^2)
max_trial_strain = 2*q^2/R^2
```

This is a reduced energy law, not the full membrane-wave equations above. It
does not model yarn take-up, wrinkling details or propagation of the deformation
cone. Exceeding the supplied strain domain invalidates the result. Brittle
layers are supported only within a supplied intact elastic domain; crushing,
fiber rupture, delamination and perforation are unsupported.

Contact area is an explicit input to this reduction, not an output reconstructed
from a resolved pressure field. Numerical energy/momentum errors, force, impulse,
body work and distinct body/contact displacements are diagnostic outputs. No
mechanical output is converted to HP.

The live integration labels any evaluated result `scope=REFERENCE_ONLY`. It
uses the observed incoming projectile state to evaluate a hypothetical pristine,
center-loaded flat assembly with the authored pad and normal velocity component.
It does **not** identify the actual plate-local hit location, tangential impact
conditions, local damage state, or worn carrier/pad stack. A declared `PadKey`
matches authored profiles; it does not inspect equipment worn behind the plate.
These four missing observations prevent treating the result as predicted BFD of
the actual hit. Merely completing the input JSON does not close these gaps.

The shipped projectile-contact impact profile dictionary is empty. Consequently,
this separate research entry point cannot evaluate an ordinary product without
an explicitly authored contact history/profile, even though the active transfer
path has construction and body estimates. Positive projectile-contact solver
tests use synthetic inputs only.

`IncompleteTimeHorizon` preserves the partial mechanical response with its
status. Peak values then cover only the supplied observation window, and the
unloaded residual estimate can change if further plastic flow occurs after that
window. A completed numerical integration and a settled physical event are
different conditions.

## Construction, not material labels

| Construction | Required mechanical description |
|---|---|
| Steel, titanium, aluminum plate | Density, shape, elastic stiffness, alloy-specific plasticity, support and contact |
| Bonded aramid or UHMWPE laminate | Oriented ply stiffness, transverse shear, interfaces and failure |
| Sewn or unbonded soft insert | Areal density, membrane response, yarn take-up, slip, seams and failure |
| Ceramic with backing | Brittle face, retained fragment mass, interfaces and actual backing |
| Combined | An explicit stack of identified layers |
| Transparent armor | Brittle glass layers and actual polymer interlayers/backing |

An integrated insert is not automatically soft. Conversely, the elastic modulus
of an individual fiber cannot be used as the bulk modulus of an entire sewn
package. A hard aramid shell and a soft aramid vest require different models.

Thin isotropic plate bending provides a limiting-case check:

```
D = E*h^3 / (12*(1 - nu^2))
rho*h*w'' + D*laplacian(laplacian(w)) = p_front - p_back
```

This is not a universal ballistic deflection formula. Plasticity, large
deflections, curvature, support compliance and transverse shear alter the
response. See [COMSOL's shell and plate theory](https://doc.comsol.com/6.3/doc/com.comsol.help.sme/sme_ug_theory.06.130.html).

For bonded laminates the corresponding elastic quantities are the laminate
`A`, `B`, `D` matrices, calculated by integrating each transformed ply stiffness
through its actual thickness. Delamination invalidates treating separated plies
as one bonded thickness. See [NASA RP 1351](https://ntrs.nasa.gov/archive/nasa/casi.ntrs.nasa.gov/19950009349.pdf).

For soft packages, the transverse response is driven by membrane tension and
its evolving geometry. Neither the whole package area nor an arbitrary fixed
spread diameter participates instantaneously. See [Phoenix and Porwal's
membrane impact model](https://www.sciencedirect.com/science/article/pii/S0020768303003299).

Pressure-dependent brittle damage requires a separate constitutive model and
calibration. A full JH-2 simulation is not interchangeable with reducing an
elastic modulus by remaining game durability. See [Ansys JH-2 documentation](https://ansyshelp.ansys.com/public/Views/Secured/corp/v242/en/exd_ag/ds_ex_mat_johnholm.html).

## Evidence and validation limits

Every construction must distinguish measured data, estimates and absent data.
A schema version or a positive thickness is not a validation certificate. Do
not synthesize missing contact, backing or injury parameters from armor class,
`BluntThroughput`, the ballistic limit or a desired damage number.

Mechanical verification must cover zero input, passive contacts, energy and
momentum accounting, time-step convergence, finite outputs, support conditions,
layer coupling and the limits of the chosen spatial reduction. Synthetic test
fixtures are identified as such and must not be shipped as measured products.

Empirical validation separately requires applicable impact tests: projectile
state, product construction, support/pad setup, impact location and angle,
rear-face displacement history where available, residual deformation and
penetration outcome. A clay impression alone does not calibrate body response.

Clinical injury prediction must distinguish torso, limbs and head and be
calibrated separately from armor deflection. The contact-bruise adapter above
defines a game-health conversion; it does not establish a medical conversion of
mechanical joules or millimeters to injury severity. Regional BABT
experiments measure velocity, force, impulse, deflection and injury outcomes;
see [Yoganandan et al., 2024](https://academic.oup.com/milmed/article/189/Supplement_3/659/7735961).

Claims of real-product accuracy still require product coverage with applicable
data, mechanical validation, injury calibration and live-raid validation of the
complete event path. The active implementation is an explicitly estimated
reduction whose numerical budget and runtime integration can be tested
independently of those claims.

## Release preparation verification: 2026-09-07

The 1.5.0 release package was built from the versioned working tree. Both client
and server Release builds completed with zero warnings and zero errors. Client
tests passed **653/653**. Server tests passed **467/472**; the five failures are
byte-for-byte the same assertions and values reproduced from the clean 1.4.1 tag:
the two AramidUD thickness anchors, the MildSteel and AramidUD error-shape checks,
and the M193/M80 fibre ordering check. The focused BABT server suite passed
**31/31**.

The resulting `dist/PLATE-1.5.0.zip` has SHA-256
`3AB4BC4D297E80D3CE33887418E28EEF7DD31C7C79C9296DBAE4F5635A55E5F9`.
Packaging and build verification do not establish Unity hook attachment, marker
appearance or the new injury path in a live raid. No deployment or new live-raid
acceptance was performed during release preparation.

## Active transfer verification: 2026-09-05

The final Release solution build succeeded. Client tests passed **647/647**;
server tests passed **467/472**, with the same five pre-existing penetration-model
failures listed below. No package, game deployment or live-raid test was performed.
Test reports are in the respective test projects' `bin/TestResults` directories.

The client count includes an explicit host limitation. The desktop .NET Framework
host rejects the game's `DeltaTimeDelegate` metadata while preparing each of
`Player.ApplyShot`, `Player.ProceedDamageThroughArmor` and `Player.ApplyDamageInfo`,
before Harmony compiles a PLATE hook. The fixture probes each unpatched original
independently and still attempts the full production patch set. It accepts only
the corresponding exact, evidenced host failure; other patch failures remain
failures. Original and patch parameter contracts are checked separately. These
checks do **not** confirm attachment or health delivery in Unity/Mono. The existing
three `ActiveHealthController.ApplyDamage` hooks also retain their previously
documented desktop-host limitation.

Transferred-response tests cover stop and pierce, the aggregate energy/impulse
budget, embedded and outgoing mass, uncertain secondary fate, material/thickness
scaling, zero load, repeated and pooled shots, and delivery ownership. Production
resolver scenarios include steel, UHMWPE, soft aramid and ceramic/backing on the
thorax and abdomen. Prescribed projectile outcomes in these scenarios are test
inputs, not new claims about an armor product's ability to stop that projectile.

Actual time-step halving produced the following convergence check:

| Scenario | Coarse / fine step | Coarse / fine net body work |
|---|---|---|
| 8 g at 300 m/s, stopped by 6.35 mm steel | 10.729 / 5.364 microseconds | 0.898147 / 0.898520 J |
| 48 g at 780 m/s, stopped by ceramic/backing, thorax | 3.016 / 1.508 microseconds | 228.684 / 228.580 J |

Both work differences were below 0.05%. The ceramic runs each retained one
brittle dropout and approximately 39.7118 J in the fracture reservoir. A separate
100-hit mixed steel/ceramic smoke run used 1,045,750 integration steps and about
74 ms on the test host; this is not an in-raid frame-time measurement.

The 0.898 J steel scenario maps to about 0.13 HP on the existing 7 J/HP contact
scale. Removing the old floor/cap therefore does not imply every rifle hit must
exceed 2 HP or every .50 hit must exceed 40 HP: the result follows estimated work
on the body, with experimental calibration still outstanding.

Full mechanical response remains unavailable for unknown stacks, curved regional
geometry, and unknown intermediate-secondary fate. Resolved torso/abdomen chains
can use the labeled, energy-bounded estimate described above. A missing exact
shot ledger retains the established game armor result. In addition, the late
health-call update does not rewrite the outer `ApplyShot` value copy used by
vanilla reaction/notification code; health receives the combined damage and the
PLATE overlay has its own wound/BABT breakdown. These event paths require raid
validation.

## Earlier diagnostic verification: 2026-09-05

The pre-change baseline was 587 passing client tests and 435 passing server
tests with 5 server failures. After implementation and review, Release client
tests passed 606/606; Release server tests passed 455/460 with the same 5
baseline failures. Solution Release build succeeded. No game deployment or
live-raid validation was performed.

The unchanged server failures are the two AramidUD thickness anchors at 5.7 and
6.8 mm, the MildSteel and AramidUD thickness-error-shape checks in
`BallisticLadderTests`, and the M193/M80 ordering check in `NijPenetrationTests`.
These results do not certify the existing penetration model.

Review corrected the distinction between contact and body compression, stale
pooled-shot/delivery state, exact blocker matching, same-item component grouping,
nonfinite/unknown schema inputs and diagnostic exception isolation. Independent
mechanical checks verified elastic velocity/work scaling, zero body work before
contact, incomplete observation-window status, and refusal outside constitutive
domains. The four live applicability gaps described above remain unresolved and
are explicitly marked in reference-scenario output.

That earlier revision was a diagnostic/research prototype with legacy gameplay
damage, including the 2/40 HP plateaus. The active resolved-transfer path and
its subsequent verification are separate from that historical test count.
