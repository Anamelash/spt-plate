using System;
using PLATE.Server.Services; // BallisticLimit, compiled into both halves from one file

namespace PLATE.Client.Ballistics
{
    /// <summary>
    /// What a wall, a door or a sheet of tin does to a bullet.
    ///
    /// Vanilla answers that question with a gate: a per-collider threshold against the
    /// cartridge's template penetration number, then a coin flip, and a projectile that
    /// gets through pays nothing at all for the hole it made. Here it is a barrier with
    /// a material and a thickness, and the same four values the rest of the mod carries
    /// — mass, diameter, velocity, deformable fraction — decide whether the projectile
    /// comes out the far side and how fast. The wound model downstream needs no changes:
    /// it already computes everything from the velocity it is handed.
    ///
    /// No EFT types and no engine behaviour, so the whole thing is checkable without a
    /// game running, the way Anatomy and WindedModel are (MODEL.md, "Environment
    /// barriers"). It does reach for UnityEngine.Mathf through ArmorExit, which is pure
    /// static arithmetic and runs outside the player as happily as inside it.
    /// </summary>
    internal static class ObstacleModel
    {
        /// <summary>A steel sheet: the ballistic limit, the same law armour uses.</summary>
        public const string MechSteel = "steel";

        /// <summary>
        /// A bulk medium that resists by crushing and by inertia — wood, cardboard,
        /// rubber, gravel, snow. One law, parameterised by the material's own published
        /// crushing strength and density.
        /// </summary>
        public const string MechPoncelet = "poncelet";

        /// <summary>Nothing worth a threshold: glass, wire mesh, grass. A flat, symbolic
        /// energy price for making the hole, and through.</summary>
        public const string MechAlways = "always";

        /// <summary>Concrete, stone, soil: the engine gives us no thickness for these,
        /// so they are walls. Ricochet or stop.</summary>
        public const string MechNever = "never";

        /// <summary>Leave this material to the game.</summary>
        public const string MechVanilla = "vanilla";

        /// <summary>Constants shared by every barrier. All of them live in the reference
        /// book; none of them may appear as a literal in a formula.</summary>
        public struct Tuning
        {
            /// <summary>Smallest cosine an oblique hit is read at, so a graze is not an
            /// infinitely long path.</summary>
            public double AngleMinCos;

            /// <summary>
            /// How much more it costs to open a hole in a medium than to crush it in a
            /// uniaxial test. Cavity-expansion theory prices this at three to five times
            /// the material's own strength, and it is the same confinement factor the
            /// ductile hole-growth constant sits on in the armour model.
            /// </summary>
            public double ConfinementFactor;

            /// <summary>Poncelet's inertial coefficient: the drag term is
            /// ½·C_d·ρ·v² over the projectile's frontal area.</summary>
            public double DragCoefficient;

            /// <summary>A bullet that flattens digs less deeply: the path scales by
            /// (1 − this·X), as it does in tissue.</summary>
            public double ExpansionDepthFactor;

            /// <summary>Half-width of the probabilistic band around the critical
            /// ricochet angle, as a fraction of that angle.</summary>
            public double RicochetBand;

            /// <summary>Velocity the tabulated critical angles were observed at, m/s.</summary>
            public double RicochetVelocityRef;

            /// <summary>How fast the critical angle falls with velocity: α ∝ v^(−q).</summary>
            public double RicochetVelocityExp;

            /// <summary>How much of the retention is lost between a grazing bounce and
            /// one right at the critical angle.</summary>
            public double RicochetLoss;

            /// <summary>The normal component of the mirror reflection is multiplied by
            /// this: a ricochet leaves flatter than it arrived.</summary>
            public double RicochetFlatten;

            /// <summary>
            /// How far off line a barrier throws a projectile, per unit of the areal
            /// density it presents over the projectile's own sectional density. See
            /// DeviationScale.
            /// </summary>
            public double DeviationK;

            /// <summary>
            /// How much worse the deflection gets for a projectile the barrier deformed,
            /// at the point where the barrier took all of its speed. This is the whole
            /// velocity dependence of the deflection, and it is second-hand: a rigid
            /// projectile's deflection does not depend on how fast it was going.
            /// </summary>
            public double DeviationDeformMult;

            /// <summary>
            /// How blunt a barrier that killed the core leaves it, at the point where it
            /// took all of the projectile's speed. Scaled by how much of the speed it
            /// actually took (see Work), which is what keeps a sheet of tin from
            /// mushrooming a bullet the way ten millimetres of plate does.
            /// </summary>
            public double CoreBluntK;

            /// <summary>How much mass a dead core shaves off in the hole, same scaling.</summary>
            public double CoreErosionK;

            /// <summary>
            /// How much of its speed a barrier has to take before it has a rim solid
            /// enough to shear a jacket off. Below it a hard core carries its jacket
            /// through, which is what a bullet does through thin sheet.
            /// </summary>
            public double JacketStripWork;

            /// <summary>
            /// How deep a shell's collider has to be before a projectile crossing it is
            /// taken to meet TWO of its walls rather than one panel, mm.
            ///
            /// A barrel is six hundred millimetres of outline around air and a bullet
            /// pays for its wall twice, going in and coming out. A container side is one
            /// panel whose collider happens to be a few centimetres thick, and charging
            /// that twice would double the price of every sheet in the game. Nothing in
            /// the scene tells the two apart — this is a judgement about how map geometry
            /// is authored rather than a piece of physics, which is why it sits in the
            /// book where it can be retuned and is called out in MODEL.md as the weakest
            /// number in the module.
            /// </summary>
            public double ShellCavityMm;

            /// <summary>
            /// Uniform half-width of the per-encounter ballistic-limit draw, as a
            /// fraction of V50. The certification criteria the armour model is
            /// calibrated against price the shot-to-shot scatter of a measured V50 at
            /// CV = 0.04; this is the ±2σ of that, and it is what turns the limit from
            /// a cliff into the zone of mixed results the testing standards describe.
            /// </summary>
            public double SteelLimitScatter;

            /// <summary>
            /// How much yaw one crossing hands the projectile, per unit of Work and per
            /// unit of slenderness. See <see cref="YawGain"/>.
            /// </summary>
            public double YawGainK;

            /// <summary>
            /// How much an oblique crossing adds to that, per unit of tan θ: an angled
            /// face loads the nose on one side only, which is the systematic reason a
            /// projectile leaves a barrier turning.
            /// </summary>
            public double YawObliquityK;
        }

        /// <summary>The obstacle as the projectile meets it.</summary>
        public struct Barrier
        {
            /// <summary>One of the Mech* constants.</summary>
            public string Mechanism;

            /// <summary>Thickness along the surface normal, mm — of ONE wall.</summary>
            public double ThicknessMm;

            /// <summary>
            /// How many of those walls the entry face charges. One for everything the
            /// geometry can speak for; two for a door leaf, which is two skins over a
            /// frame and whose collider is far too shallow for the shell rule
            /// (<see cref="Tuning.ShellCavityMm"/>) to notice the cavity between them.
            ///
            /// Nothing in the geometry separates a two-skin leaf from a single profiled
            /// sheet inside a deep collider, so this is not measured: it is the scene's
            /// own word, the `DOORS` grouping node BSG park their door leaves under,
            /// carried here from the resolution. 0 reads as 1, so a barrier built
            /// without one behaves as it always did.
            /// </summary>
            public double Walls;

            /// <summary>Poncelet: the medium's crushing strength, MPa.</summary>
            public double StrengthMPa;

            /// <summary>
            /// Bulk density, g/cm³. The Poncelet law's inertial term is built on it, and
            /// every mechanism uses it twice more: the areal density that decides the
            /// deflection, and the stagnation pressure that decides whether the
            /// projectile survives the meeting intact.
            /// </summary>
            public double DensityGCm3;

            /// <summary>
            /// Vickers hardness of the barrier. Only the core's fate reads it (and the
            /// steel law's hardness argument, which mild steel's flow mode switches off
            /// anyway); a material soft enough for it not to matter can leave it at 0,
            /// which reads as "this cannot deform anything".
            /// </summary>
            public double HardnessHv;

            /// <summary>
            /// Whether a thing made of this material is solid through.
            ///
            /// This is what tells a measured collider from a measured object. A bullet
            /// into a log crosses as much wood as the log is deep, so the collider IS
            /// the path. A bullet into a barrel crosses a millimetre of steel, air, and
            /// a millimetre of steel again — the collider is six hundred millimetres of
            /// outline and none of it is material. Measuring the second kind and
            /// believing the number makes barrels, canisters and container sides
            /// bulletproof, which is exactly what it did.
            ///
            /// There is no hollow flag anywhere in Unity to read; what the game does
            /// carry is the MaterialType the level designer put on the collider, and
            /// `MetalThin` on a barrel is that statement in as many words. So the answer
            /// lives per material in the book, and for a shell the book's thickness —
            /// the wall — is the honest number rather than the measurement.
            /// </summary>
            public bool Solid;

            /// <summary>Always: flat energy price of the hole, J.</summary>
            public double CostJ;

            /// <summary>
            /// How much more of a brittle medium is perforated than is penetrated, as a
            /// ratio of thicknesses. 1 — or 0, read as 1 — means the medium fails only
            /// where the projectile actually reaches.
            ///
            /// A semi-infinite block of concrete stops a bullet at some depth. A SLAB of
            /// that same depth does not: the compression wave reaches the free rear face
            /// and throws a cone of material off it, and the projectile follows through
            /// the hole. The NDRC relation puts the perforation limit around 1.3 times
            /// the semi-infinite penetration for concrete, and the last third of a slab
            /// is therefore not material the projectile has to cross. Ductile media have
            /// no equivalent — a steel sheet petals rather than scabs, and wood splits —
            /// so this is left at 1 everywhere except the brittle entries.
            /// </summary>
            public double SpallFactor;

            /// <summary>Steel: the sheet's strength, by whichever of the two ductile
            /// mechanisms FailureMode names.</summary>
            public double YieldMPa;
            public double ShearMPa;
            public string FailureMode;

            /// <summary>
            /// What is packed INSIDE this medium, if anything. Null — the usual case —
            /// means the barrier is homogeneous and is crossed in one go, exactly as it
            /// always was. See <see cref="StackFill"/>.
            /// </summary>
            public StackFill Fill;
        }

        /// <summary>
        /// A carrier medium with discrete things packed in it, met one at a time.
        ///
        /// Palletised cargo is the case this exists for. A pallet of boxes is two
        /// materials at once and neither of them alone is honest: the stack itself is
        /// mostly air in cardboard, so clipping a corner of it must cost about what a
        /// cardboard box costs, while what is IN the boxes is packed goods, and a bullet
        /// sent down the length of a loaded pallet meets a lot of it. Averaging the two
        /// into one homogeneous solid — which is what the book did before — gets both
        /// ends wrong at once: a corner clip stops rifle rounds and a lengthwise shot is
        /// no worse than a crossing.
        ///
        /// So the carrier is crossed continuously and the contents are met discretely:
        /// every <see cref="SpacingMm"/> of path the projectile either runs into a
        /// package or does not. Two consequences fall out of that and neither is
        /// available to a homogeneous medium. The first is that the cost grows with the
        /// path in packages rather than in millimetres, which is the difference between
        /// a corner and a long axis. The second is that it is a LOTTERY: two identical
        /// rounds on the same line get different answers, one threading the voids and
        /// one hitting three boxes of goods, which is what shooting into stacked cargo
        /// actually looks like.
        ///
        /// The inclusion's thickness is tied to the spacing
        /// (<see cref="ContentFraction"/> of it), so the EXPECTED amount of cargo per
        /// metre of path — frac·chance — does not depend on the spacing at all. The
        /// spacing is therefore a grain size rather than a strength knob: coarser means
        /// fewer, thicker packages and a wider spread of outcomes. It is not perfectly
        /// neutral, and MODEL.md says why — every layer boundary asks the yaw question
        /// again, so finer slicing costs slightly more — but it is a weak lever rather
        /// than a free parameter.
        /// </summary>
        public class StackFill
        {
            /// <summary>How much path there is per draw, mm.</summary>
            public double SpacingMm;

            /// <summary>How much of that path a package occupies when one is drawn.</summary>
            public double ContentFraction;

            /// <summary>Odds that a given layer holds a package at all, 0..1.</summary>
            public double Chance;

            /// <summary>The package: an ordinary barrier, crossed in the ordinary way.
            /// Its own thickness is set by the loop and its own Fill is never read —
            /// cargo does not contain cargo.</summary>
            public Barrier Content;
        }

        /// <summary>The projectile, in the four values the whole mod carries.</summary>
        public struct Projectile
        {
            public double MassG;
            public double DiaMm;
            public double V;
            public double X;

            /// <summary>Hard core frontal area / bullet frontal area; 1 = monolithic.</summary>
            public double CoreAreaFrac;

            /// <summary>Hard core mass / bullet mass; 1 = monolithic.</summary>
            public double CoreMassFrac;

            /// <summary>Vickers hardness of the core.</summary>
            public double HardnessHv;

            /// <summary>
            /// How far off nose-on this projectile arrives, 0..1: 0 is point first,
            /// 1 is fully broadside. What a previous barrier left it turning by — never
            /// what this one does to it, which is <see cref="Outcome.ExitYaw"/>.
            /// </summary>
            public double YawFrac;

            /// <summary>
            /// Length of the projectile, mm. Yaw needs it twice: a slender thing is
            /// destabilised by a barrier and a ball is not, and a slender thing presents
            /// far more of itself once it has turned.
            ///
            /// The core does not compute it — length comes out of the broadside geometry
            /// the wound model already carries (YawModel), and reaching for that from
            /// here would drag the ammunition cache into a file that must stay pure. The
            /// caller fills it in; ZERO means "this caller does not model yaw", and then
            /// nothing here does anything it did not do before.
            /// </summary>
            public double LengthMm;

            /// <summary>
            /// The area it would present lying fully broadside, mm² (YawModel again).
            /// Zero means the same as a zero <see cref="LengthMm"/>: no yaw at all.
            /// </summary>
            public double SideAreaMm2;
        }

        /// <summary>What came of the meeting.</summary>
        public struct Outcome
        {
            public bool Penetrates;

            /// <summary>Velocity on the far side, m/s. 0 when the barrier held.</summary>
            public double ExitV;

            /// <summary>Path through the material along the trajectory, mm.</summary>
            public double PathMm;

            /// <summary>Poncelet: how deep the projectile could have gone, mm.
            /// Steel: 0.</summary>
            public double DepthMm;

            /// <summary>Steel: the ballistic limit, m/s, including this encounter's
            /// draw within the limit scatter. Otherwise 0.</summary>
            public double V50;

            /// <summary>
            /// Steel: the limit along the true line of arrival, with no obliquity
            /// floor — what the ricochet gate compares against. Same law and same
            /// per-encounter draw as <see cref="V50"/>; never below it. Otherwise 0.
            /// </summary>
            public double RefusalV50;

            /// <summary>
            /// The share of its speed the barrier took, 0..1. How hard the barrier
            /// worked on the projectile, and therefore how much of everything else it
            /// gets to do to it: a sheet of tin that costs a bullet a tenth of its speed
            /// does not deform it the way a plate that costs it half does.
            /// </summary>
            public double Work;

            /// <summary>What became of the core on the way through.</summary>
            public BallisticLimit.CoreFate Fate;

            /// <summary>Mass carrying on, g. The mass that arrived, when nothing
            /// happened to the projectile.</summary>
            public double ExitMassG;

            /// <summary>Diameter of what carries on, mm.</summary>
            public double ExitDiaMm;

            /// <summary>Deformable fraction of what carries on.</summary>
            public double ExitX;

            /// <summary>
            /// Characteristic tangent of the angle the barrier throws the projectile
            /// off by. Not an angle in degrees: it is used the way vanilla uses its own
            /// deviation, as the length of a random vector added to a unit direction.
            /// </summary>
            public double Deviation;

            /// <summary>
            /// How far off nose-on the projectile leaves, 0..1 — what it arrived with
            /// plus what this crossing added (<see cref="YawGain"/>). The barrier it is
            /// about to meet is the one that pays for it; this one has already been
            /// crossed at the yaw the projectile arrived with.
            /// </summary>
            public double ExitYaw;
        }

        /// <summary>
        /// How much material the entry face is, mm: the book's wall times however many
        /// of them this object is made of (<see cref="Barrier.Walls"/>). Every reader of
        /// the thickness goes through here — the path, the ballistic limit and the
        /// refusal gate all have to argue about the same barrier.
        /// </summary>
        public static double WallMm(Barrier b)
        {
            return b.ThicknessMm * (b.Walls > 1 ? b.Walls : 1);
        }

        // --- Yaw: what the previous barrier left, and what this one adds ---

        /// <summary>
        /// The area the projectile actually presents, mm²: its calibre when it is still
        /// nose-on, its broadside area when it is fully turned, and the interpolation in
        /// between.
        ///
        /// This is the whole of what yaw DOES. A projectile arriving sideways drags
        /// against more medium, digs less deeply, needs a higher limit to punch a sheet
        /// and is thrown further off line — and every one of those reads an area, so
        /// there is one place to change it and no separate per-effect constant. The
        /// broadside area is never taken as less than the calibre: a fully expanded
        /// hollow point is short and blunt and has nothing wider to turn into, and a
        /// round ball presents the same disc whichever way it faces, so shot does not
        /// need a rule of its own.
        ///
        /// A caller that leaves the geometry blank gets the calibre back untouched, bit
        /// for bit — that is the safe default the whole feature rests on.
        /// </summary>
        public static double EffectiveAreaMm2(Projectile p)
        {
            var cal = Area(p.DiaMm);
            if (p.DiaMm <= 0 || p.LengthMm <= 0 || p.SideAreaMm2 <= 0 || p.YawFrac <= 0)
            {
                return cal;
            }

            var side = Math.Max(p.SideAreaMm2, cal);
            return cal + Clamp01(p.YawFrac) * (side - cal);
        }

        /// <summary>
        /// The diameter of a disc of that area, mm — for the ballistic limit, which
        /// argues in diameters rather than in areas.
        ///
        /// This is NOT the exit calibre and must never become it: the projectile did not
        /// get fatter, it is lying over. <see cref="Outcome.ExitDiaMm"/> is what carries
        /// on, and yaw is deliberately kept out of it.
        /// </summary>
        public static double EffectiveDiaMm(Projectile p)
        {
            var area = EffectiveAreaMm2(p);
            return area > Area(p.DiaMm) ? Math.Sqrt(4.0 * area / Math.PI) : p.DiaMm;
        }

        /// <summary>
        /// How much yaw this crossing adds:
        ///
        ///     Δyaw = YawGainK · Work · (L/d − 1) · (1 + YawObliquityK · tan θ)
        ///
        /// A barrier destabilises a projectile by loading its nose off-axis, and the
        /// three things that decide how much are already here. **Work** — the share of
        /// the speed the barrier took — is the same measure of "how hard did this barrier
        /// work" that scales the core's deformation, and it is what keeps a sheet of tin
        /// from doing what ten millimetres of plate does without a second set of
        /// constants. **Slenderness**, L/d − 1, is the lever arm: a long thin projectile
        /// has a large overturning moment about its own centre and almost no polar
        /// inertia to resist it, a sphere has neither and cannot yaw at all (the geometry
        /// says so by itself — a ball comes out at L/d ≈ 1 and gets nothing). **Obliquity**
        /// is the systematic asymmetry: a face met at an angle loads one side of the nose
        /// before the other, which is why an angled plate keyholes what a square one lets
        /// through straight.
        ///
        /// Nothing is subtracted. A bullet does re-stabilise in air over tens of metres,
        /// but the case this exists for — a row of barrels, a car, a stud wall — is
        /// metres, and a decay term would need a time of flight this model does not see.
        /// Called out in MODEL.md as such.
        /// </summary>
        public static double YawGain(Projectile p, Tuning t, Outcome outcome, double cos)
        {
            if (!outcome.Penetrates || p.LengthMm <= 0 || p.SideAreaMm2 <= 0 || p.DiaMm <= 0)
            {
                return 0;
            }

            var slenderness = p.LengthMm / p.DiaMm - 1.0;
            if (slenderness <= 0)
            {
                return 0;
            }

            var floor = t.AngleMinCos > 0 ? t.AngleMinCos : 0.2;
            var c = Math.Min(Math.Abs(cos), 1.0);
            var tan = Math.Sqrt(Math.Max(1.0 - c * c, 0)) / Math.Max(c, floor);

            return t.YawGainK * Clamp01(outcome.Work) * slenderness
                   * (1.0 + t.YawObliquityK * tan);
        }

        /// <summary>
        /// The path through the sheet, mm. An oblique hit presents more material, and
        /// the cosine is floored so a graze does not read as an infinite wall — the same
        /// clamp the armour model uses.
        /// </summary>
        public static double PathMm(double thicknessMm, double cos, Tuning t)
        {
            var c = Math.Abs(cos);
            var floor = t.AngleMinCos > 0 ? t.AngleMinCos : 0.2;
            return thicknessMm / Math.Max(c, floor);
        }

        /// <summary>
        /// The velocity at which the medium's inertial resistance equals its strength.
        /// Below it the projectile is being pushed through a solid, above it it is
        /// throwing the medium aside — and it is computed from the material rather than
        /// tuned: v_stop = √(2000·S·σ / (C_d·ρ)) for σ in MPa and ρ in g/cm³.
        ///
        /// The wound channel carries a fitted 50 m/s for gelatin. Read backwards through
        /// this expression at ρ = 1.0 that is a strength of 0.25 MPa, which is where 10%
        /// ordnance gelatin's quasi-static crush strength actually sits — one law, two
        /// media, no separate fit.
        /// </summary>
        public static double StopVelocity(Barrier b, Tuning t)
        {
            var rho = b.DensityGCm3;
            var cd = t.DragCoefficient;
            if (rho <= 0 || cd <= 0)
            {
                return 0;
            }

            return Math.Sqrt(2000.0 * t.ConfinementFactor * Math.Max(b.StrengthMPa, 0) / (cd * rho));
        }

        /// <summary>
        /// The e-folding length of the Poncelet decay, mm: λ = 1000·(m/A)/(C_d·ρ).
        ///
        /// Sectional density again — the reason a heavy narrow bullet outreaches a light
        /// wide one — and nothing else. A bullet that flattens shortens it, and so does
        /// one arriving sideways: the area is what it presents, not what its calibre is
        /// (see <see cref="EffectiveAreaMm2"/>).
        /// </summary>
        public static double LambdaMm(Projectile p, Barrier b, Tuning t)
        {
            var area = EffectiveAreaMm2(p);
            if (area <= 0 || b.DensityGCm3 <= 0 || t.DragCoefficient <= 0)
            {
                return 0;
            }

            var spread = 1.0 - t.ExpansionDepthFactor * Clamp01(p.X);
            return 1000.0 * (p.MassG / area) / (t.DragCoefficient * b.DensityGCm3)
                   * Math.Max(spread, 0.05);
        }

        /// <summary>
        /// How deep this projectile could go into this medium, mm:
        /// D = λ · ln(1 + (v/v_stop)²).
        ///
        /// The same drag law the wound channel is built on, with the medium's static
        /// strength kept instead of dropped. Gelatin has almost none, so there the term
        /// vanishes and the depth collapses to λ·2·ln(v/v_stop); wood has a great deal,
        /// and dropping it makes a rifle round and a pistol round differ by a factor of
        /// two in pine when the published tables put them a factor of four apart.
        /// </summary>
        /// <summary>
        /// How much of the path actually resists, mm. The whole of it for everything
        /// that fails by crushing, and less for a brittle slab, whose rear face scabs
        /// off ahead of the projectile instead of being crossed — see
        /// <see cref="Barrier.SpallFactor"/>. A slab thicker than the penetration depth
        /// can therefore still be perforated, which is the difference between a block of
        /// concrete and a concrete wall.
        /// </summary>
        public static double ResistingPathMm(double pathMm, Barrier b)
        {
            var spall = b.SpallFactor > 0 ? b.SpallFactor : 1.0;
            return Math.Max(pathMm, 0) / spall;
        }

        public static double DepthMm(Projectile p, Barrier b, Tuning t)
        {
            var vStop = StopVelocity(b, t);
            var lambda = LambdaMm(p, b, t);
            if (vStop <= 0 || lambda <= 0 || p.V <= 0)
            {
                return 0;
            }

            var u = p.V / vStop;
            return lambda * Math.Log(1.0 + u * u);
        }

        /// <summary>
        /// What is left after <paramref name="pathMm"/> of the medium, m/s — the same
        /// law read backwards: (1 + u_res) = (1 + u)·exp(−path/λ). Zero when the
        /// projectile runs out inside.
        /// </summary>
        public static double PonceletResidual(Projectile p, Barrier b, Tuning t, double pathMm)
        {
            var vStop = StopVelocity(b, t);
            var lambda = LambdaMm(p, b, t);
            if (vStop <= 0 || lambda <= 0 || p.V <= 0)
            {
                return 0;
            }

            var u = p.V / vStop;
            var left = (1.0 + u * u) * Math.Exp(-Math.Max(pathMm, 0) / lambda) - 1.0;
            return left <= 0 ? 0 : vStop * Math.Sqrt(left);
        }

        /// <summary>
        /// Does it get through, and with what left. The one entry point the patch calls;
        /// everything above it is public so the arithmetic can be checked a piece at a
        /// time.
        ///
        /// <paramref name="limitDraw"/> is this encounter's draw within the steel
        /// limit's scatter, 0..1: 0.5 is the mean sheet (and the default, so tests are
        /// deterministic), the patch hands in a value from vanilla's own random stream
        /// so a replayed shot replays.
        ///
        /// <paramref name="stackSeed"/> is the seed for a barrier that is packed rather
        /// than homogeneous (<see cref="StackFill"/>) — every layer of it draws whether
        /// it holds a package. Zero is a perfectly good seed; the patch hands in one from
        /// vanilla's own stream for the same reason it does for the limit draw.
        /// </summary>
        public static Outcome Resolve(Projectile p, Barrier b, Tuning t, double cos,
            double limitDraw = 0.5, int stackSeed = 0)
        {
            var fill = b.Fill;
            if (fill != null && fill.SpacingMm > 0 && fill.Content.Mechanism != null)
            {
                return ResolveStack(p, b, t, cos, limitDraw, stackSeed);
            }

            return ResolveOnce(p, b, t, cos, limitDraw);
        }

        /// <summary>One crossing of one homogeneous barrier — the whole of the model
        /// before packed media existed, and still the whole of it for every barrier that
        /// is not packed.</summary>
        private static Outcome ResolveOnce(Projectile p, Barrier b, Tuning t, double cos,
            double limitDraw)
        {
            var outcome = new Outcome
            {
                PathMm = PathMm(WallMm(b), cos, t),
                ExitMassG = p.MassG,
                ExitDiaMm = p.DiaMm,
                ExitX = p.X,
                ExitYaw = Clamp01(p.YawFrac),
                Fate = BallisticLimit.CoreFate.Rigid,
            };

            if (p.MassG <= 0 || p.DiaMm <= 0 || p.V <= 0)
            {
                return outcome; // nothing to compute with — the barrier holds
            }

            switch (b.Mechanism)
            {
                case MechNever:
                    return outcome;

                case MechAlways:
                {
                    // a flat price for making the hole, paid out of the projectile's
                    // energy: a pane of glass is fractured out rather than crushed
                    // through, so what it costs barely depends on what is doing it
                    var cost = b.CostJ * ObliquityFactor(cos, t);
                    var vSq = p.V * p.V - 2000.0 * Math.Max(cost, 0) / p.MassG;
                    if (vSq <= 0)
                    {
                        return outcome;
                    }

                    outcome.Penetrates = true;
                    outcome.ExitV = Math.Sqrt(vSq);
                    break;
                }

                case MechPoncelet:
                {
                    outcome.DepthMm = DepthMm(p, b, t);
                    var vRes = PonceletResidual(p, b, t, ResistingPathMm(outcome.PathMm, b));
                    outcome.Penetrates = vRes > 0;
                    outcome.ExitV = vRes;
                    break;
                }

                case MechSteel:
                {
                    var tuning = BallisticLimit.Tuning.Default;
                    var core = Driving(p, tuning);
                    var barrier = LimitBarrier(b);

                    // the limit is a distribution, not a number: one draw serves the
                    // whole resolution, because the verdict, the residual and the
                    // refusal gate all describe the same square inch of sheet
                    var scatter = Math.Max(
                        1.0 + t.SteelLimitScatter * (2.0 * Clamp01(limitDraw) - 1.0), 0.01);

                    var v50 = BallisticLimit.V50(barrier, core, cos, p.V, tuning) * scatter;
                    outcome.V50 = v50;
                    outcome.RefusalV50 = RefusalLimit(p, b, cos) * scatter;
                    if (v50 <= 0 || p.V <= v50)
                    {
                        return outcome;
                    }

                    var plug = BallisticLimit.PlugMassG(barrier, core, cos, tuning);
                    outcome.ExitV = BallisticLimit.ResidualVelocity(p.V, v50,
                        BallisticLimit.MassAgainst(barrier, core), plug);
                    outcome.Penetrates = outcome.ExitV > 0;
                    break;
                }

                default:
                    return outcome; // vanilla and anything unknown: the caller bails out
            }

            if (!outcome.Penetrates)
            {
                outcome.ExitV = 0;
                return outcome;
            }

            // How hard the barrier had to work for it. Everything a barrier does to a
            // projectile beyond slowing it is scaled by this, which is what tells a
            // sheet of tin apart from ten millimetres of plate without a second set of
            // per-material constants.
            outcome.Work = 1.0 - outcome.ExitV / p.V;
            outcome.Fate = FateOf(p, b, t);
            ApplyExitState(ref outcome, p, b, t);
            outcome.Deviation = DeviationScale(p, b, t, outcome);

            // What it leaves turning by. Charged to the NEXT barrier and never to this
            // one: this crossing has already happened, at the yaw the projectile brought.
            outcome.ExitYaw = Math.Min(1.0, Clamp01(p.YawFrac) + YawGain(p, t, outcome, cos));
            return outcome;
        }

        // --- Packed media: a carrier crossed continuously, contents met one at a time ---

        /// <summary>
        /// A safety rail, not a model parameter. A book edited down to a spacing of
        /// microns must not hang the game on a building-sized chord, so the layer count
        /// is capped and the last layer absorbs whatever path is left. The medium is
        /// still crossed in full either way; only the graining coarsens.
        /// </summary>
        private const int MaxStackLayers = 1024;

        /// <summary>
        /// Below this much path there is nothing left to cross, mm. Floating point
        /// leaves slivers behind when a chord divides evenly by the spacing, and a
        /// sliver must not buy a whole extra draw.
        /// </summary>
        private const double StackTailMm = 1e-6;

        /// <summary>
        /// Whether the i-th layer of this stack holds a package, as a number in [0,1)
        /// to compare against the chance.
        ///
        /// An integer mixer rather than a Random, because the core has to stay pure and
        /// reproducible: the same shot resolved twice — and a shot IS resolved more than
        /// once, the ricochet gate asks before the penetration verdict does — has to give
        /// the same answer, and a replayed seed has to replay. The constants are a hash
        /// function's own (Knuth's multiplicative and the two xxHash finalisers); they
        /// describe no physics and tune nothing.
        /// </summary>
        public static double StackDraw(int seed, int index)
        {
            unchecked
            {
                var h = (uint)seed * 2654435761u + (uint)index * 2246822519u;
                h ^= h >> 15;
                h *= 2246822519u;
                h ^= h >> 13;
                h *= 3266489917u;
                h ^= h >> 16;
                return h / 4294967296.0;
            }
        }

        /// <summary>
        /// The stack, layer by layer: <see cref="StackFill.SpacingMm"/> of carrier, then
        /// a draw, then — if it came up — the package it holds.
        ///
        /// The layers are cut off the PATH and each is crossed square on, at cos = 1. The
        /// path already carries the obliquity, so counting it again per layer would charge
        /// an angled shot twice; and a stack of boxes has no grain, so there is no
        /// direction for a layer to be oblique TO. That is a modelling choice and MODEL.md
        /// names it: a real pallet is layered, and a shot along its layers meets different
        /// geometry from one across them.
        ///
        /// Everything the projectile carries accumulates between sub-crossings in the
        /// ordinary way — speed, mass, calibre, deformable fraction and, above all, YAW:
        /// each package leaves the round turning a little further, and the next one meets
        /// more of it. That is the whole reason the loop hands the state forward rather
        /// than summing costs. Deflections do NOT sum: each layer throws the round off in
        /// its own direction, so they add in quadrature, as a random walk does.
        /// </summary>
        private static Outcome ResolveStack(Projectile p, Barrier b, Tuning t, double cos,
            double limitDraw, int stackSeed)
        {
            var fill = b.Fill;
            var totalPath = PathMm(WallMm(b), cos, t);

            // one layer of the carrier: the same medium, a slice of the thickness
            var layer = b;
            layer.Fill = null;
            layer.Walls = 1;

            // and the package, whose own thickness the loop sets; cargo holds no cargo
            var content = fill.Content;
            content.Fill = null;
            content.Walls = 1;

            var held = new Outcome
            {
                PathMm = totalPath,
                // zero until the loop says otherwise: nothing has been crossed yet, and
                // the homogeneous depth that used to sit here described a medium the
                // projectile never met
                DepthMm = 0,
                ExitMassG = p.MassG,
                ExitDiaMm = p.DiaMm,
                ExitX = p.X,
                ExitYaw = Clamp01(p.YawFrac),
                Fate = BallisticLimit.CoreFate.Rigid,
            };

            if (p.MassG <= 0 || p.DiaMm <= 0 || p.V <= 0 || totalPath <= 0)
            {
                return held;
            }

            var state = p;
            var fate = BallisticLimit.CoreFate.Rigid;
            var devSq = 0.0;
            var remaining = totalPath;

            // How far into the stack it actually got, as opposed to how far a fresh
            // projectile would get in an infinite slab of the carrier — which is what
            // `held.DepthMm` was built from and what made the journal print things like
            // "reached 15913 of 2194 mm". A stack is not one medium, so the homogeneous
            // depth answers a question nobody asked here: the honest number is the sum
            // of the layers it crossed before one of them stopped it.
            var travelled = 0.0;

            for (var index = 0; remaining > StackTailMm; index++)
            {
                var step = index + 1 >= MaxStackLayers
                    ? remaining
                    : Math.Min(fill.SpacingMm, remaining);
                remaining -= step;

                layer.ThicknessMm = step;
                if (!Cross(ref state, ref fate, ref devSq, layer, t, limitDraw))
                {
                    held.DepthMm = travelled;
                    return held;
                }

                travelled += step;

                if (fill.ContentFraction <= 0 ||
                    StackDraw(stackSeed, index) >= Clamp01(fill.Chance))
                {
                    continue;
                }

                // the package is as thick as its share of the layer it sits in, so the
                // cargo the projectile expects to meet per metre of path is
                // fraction*chance whatever the spacing is
                content.ThicknessMm = fill.ContentFraction * step;
                if (!Cross(ref state, ref fate, ref devSq, content, t, limitDraw))
                {
                    // the package sits inside the layer just crossed, so the distance
                    // stands: what stopped it was the cargo, not more carrier
                    held.DepthMm = travelled;
                    return held;
                }
            }

            return new Outcome
            {
                Penetrates = true,
                ExitV = state.V,
                PathMm = totalPath,
                DepthMm = held.DepthMm,
                Work = 1.0 - state.V / p.V,
                Fate = fate,
                ExitMassG = state.MassG,
                ExitDiaMm = state.DiaMm,
                ExitX = state.X,
                ExitYaw = state.YawFrac,
                Deviation = Math.Sqrt(devSq),
            };
        }

        /// <summary>
        /// One sub-crossing inside a stack: resolve it, and if the projectile lived, hand
        /// its new state to the next one. False means the stack held.
        /// </summary>
        private static bool Cross(ref Projectile state, ref BallisticLimit.CoreFate fate,
            ref double devSq, Barrier layer, Tuning t, double limitDraw)
        {
            var o = ResolveOnce(state, layer, t, 1.0, limitDraw);
            if (!o.Penetrates)
            {
                return false;
            }

            devSq += o.Deviation * o.Deviation;
            if (o.Fate > fate)
            {
                fate = o.Fate;
            }

            state.V = o.ExitV;
            state.MassG = o.ExitMassG;
            state.DiaMm = o.ExitDiaMm;
            state.X = o.ExitX;
            state.YawFrac = o.ExitYaw;
            return true;
        }

        /// <summary>
        /// The core as the barrier meets it — the armour model's own reading, at the
        /// diameter the projectile actually presents. A projectile lying over has to make
        /// a wider hole in the sheet, so its limit is higher and the face is more likely
        /// to win the argument about which of the two gives way.
        /// </summary>
        private static BallisticLimit.Core Driving(Projectile p, BallisticLimit.Tuning t)
        {
            return BallisticLimit.Driving(p.MassG, EffectiveDiaMm(p), p.CoreAreaFrac,
                p.CoreMassFrac, p.HardnessHv, t);
        }

        /// <summary>
        /// The barrier in the armour model's own terms. Only the steel mechanism uses it
        /// as a strength law; every mechanism uses it for the core's fate, which reads
        /// nothing but the density and the hardness.
        /// </summary>
        private static BallisticLimit.Barrier LimitBarrier(Barrier b)
        {
            return new BallisticLimit.Barrier
            {
                Class = BallisticLimit.Ductile,
                FailureMode = string.IsNullOrEmpty(b.FailureMode)
                    ? BallisticLimit.HoleExpansion
                    : b.FailureMode,
                ThicknessMm = WallMm(b),
                ShearMPa = b.ShearMPa,
                YieldMPa = b.YieldMPa,
                HardnessHv = b.HardnessHv,
                DensityGCm3 = b.DensityGCm3,
            };
        }

        /// <summary>
        /// Whether the projectile survives the meeting as a projectile: the same Taylor
        /// rigidity criterion the armour model decides a plate by, against the same
        /// constants. The barrier's stagnation pressure plus the share of its own
        /// strength that supports it, against the core's dynamic yield.
        ///
        /// A barrier with no hardness on file cannot deform anything, which is the right
        /// answer for cloth and paper and the reason those entries can leave it out.
        /// </summary>
        public static BallisticLimit.CoreFate FateOf(Projectile p, Barrier b, Tuning t)
        {
            var tuning = BallisticLimit.Tuning.Default;
            return BallisticLimit.FateOf(LimitBarrier(b), Driving(p, tuning), p.V, tuning);
        }

        /// <summary>
        /// What comes out the far side, through the same ArmorExit a plate hands its
        /// survivors to. A rigid core is untouched; one that died on the face comes out
        /// blunter and lighter, in proportion to how much of the projectile's speed the
        /// barrier took.
        ///
        /// The scaling by Work is the one thing that is not in the plate's version, and
        /// it is there because a plate is always a serious barrier while an obstacle
        /// ranges from a sheet of paper to a log. Pinned so that a barrier taking a third
        /// of the speed does exactly what the plate constants do.
        /// </summary>
        private static void ApplyExitState(ref Outcome outcome, Projectile p, Barrier b, Tuning t)
        {
            var dead = outcome.Fate != BallisticLimit.CoreFate.Rigid;
            var kDef = dead ? t.CoreBluntK * outcome.Work : 0;
            var kFrag = dead ? t.CoreErosionK * outcome.Work : 0;

            // Only a barrier with a rim solid enough to shear against strips a jacket,
            // and thin sheet has none: a hard core takes its jacket through a tin wall
            // and leaves it in a steel one.
            var strips = b.Mechanism == MechSteel && outcome.Work >= t.JacketStripWork;

            if (!dead && !strips)
            {
                return; // nothing happened to it; leave the state exactly as it arrived
            }

            var energyOutJ = 0.5 * (p.MassG / 1000.0) * outcome.ExitV * outcome.ExitV;
            var exit = ArmorExit.Compute((float)p.MassG, (float)p.DiaMm, (float)p.X,
                (float)energyOutJ, (float)p.CoreAreaFrac, (float)p.CoreMassFrac,
                (float)kFrag, (float)kDef, strips);

            outcome.ExitMassG = exit.MassG;
            outcome.ExitDiaMm = exit.DiaMm;
            outcome.ExitX = exit.X;
        }

        /// <summary>
        /// How far off line the barrier throws it.
        ///
        /// The resisting force is not exactly on the axis — the material is not uniform
        /// at the scale a bullet meets it, and the nose is never loaded symmetrically —
        /// so some fraction of the axial impulse arrives sideways. The sideways impulse
        /// turns the trajectory by J_lat/(m·v), and with an inertial resistance the axial
        /// impulse is itself ρ·v·A·h, so the velocity cancels and what is left is a ratio
        /// of two areal densities:
        ///
        ///     tan Δθ  ∝  (ρ_barrier · h_path) / (m/A)
        ///
        /// That is the whole of it for a projectile that comes through intact, and it is
        /// why a heavy bullet of a given calibre goes straighter than a light one: it is
        /// sectional density in the denominator, the same quantity that decides how deep
        /// it goes.
        ///
        /// **Velocity does not appear, and that is a result rather than an omission.**
        /// Every rigid-body route to it cancels: a purely inertial resistance gives the
        /// expression above, a purely static one deflects SLOW projectiles more, and the
        /// gyroscopic argument (overturning moment ∝ v², spin angular momentum ∝ v, time
        /// in the barrier ∝ 1/v) cancels twice over. What actually makes a fast bullet
        /// deflect more is that a fast bullet is the one that stops being a symmetric
        /// rigid body — so the velocity dependence enters here exactly once, through the
        /// core's fate, and a barrier that killed the core throws it much further off.
        ///
        /// Yaw gets no multiplier of its own here either, for the same reason: the
        /// sectional density in the denominator is read off the area the projectile
        /// actually presents, so a projectile arriving sideways is thrown further off by
        /// the expression as it already stands. One mechanism, no extra constant.
        /// </summary>
        public static double DeviationScale(Projectile p, Barrier b, Tuning t, Outcome outcome)
        {
            var area = EffectiveAreaMm2(p);
            if (area <= 0 || p.MassG <= 0 || b.DensityGCm3 <= 0)
            {
                return 0;
            }

            // g/mm² over g/mm²: the barrier's areal density along the path against the
            // projectile's sectional density
            var sectional = p.MassG / area;
            var areal = b.DensityGCm3 * 1e-3 * Math.Max(outcome.PathMm, 0);
            var scale = t.DeviationK * areal / sectional;

            if (outcome.Fate != BallisticLimit.CoreFate.Rigid)
            {
                scale *= 1.0 + t.DeviationDeformMult * Clamp01(outcome.Work);
            }

            return scale;
        }

        /// <summary>How much longer the path is at this angle, as a plain multiplier.</summary>
        public static double ObliquityFactor(double cos, Tuning t)
        {
            var floor = t.AngleMinCos > 0 ? t.AngleMinCos : 0.2;
            return 1.0 / Math.Max(Math.Abs(cos), floor);
        }

        // --- Ricochet ---

        /// <summary>
        /// The grazing angle, degrees: 0 is along the surface, 90 is square on. Takes the
        /// cosine between the trajectory and the surface normal, whichever sign it
        /// arrives with.
        /// </summary>
        public static double GrazeAngleDeg(double cosToNormal)
        {
            var c = Math.Abs(cosToNormal);
            if (c > 1)
            {
                c = 1;
            }

            return 90.0 - Math.Acos(c) * 180.0 / Math.PI;
        }

        /// <summary>
        /// The angle below which this surface throws the projectile off instead of
        /// taking it in, at this speed: α_crit = α₀·(v_ref/v)^q.
        ///
        /// Faster is not better for a ricochet. A bullet arriving quickly loads the
        /// surface hard enough to dig its own crater, or to come apart, before the
        /// surface can turn it — which is why the forensic critical angles are quoted
        /// per velocity band and fall as the band rises.
        /// </summary>
        public static double CriticalAngleDeg(double alpha0Deg, double v, Tuning t)
        {
            if (alpha0Deg <= 0)
            {
                return 0;
            }

            if (v <= 0 || t.RicochetVelocityRef <= 0 || t.RicochetVelocityExp <= 0)
            {
                return alpha0Deg;
            }

            var scaled = alpha0Deg * Math.Pow(t.RicochetVelocityRef / v, t.RicochetVelocityExp);
            return scaled > 89.0 ? 89.0 : scaled;
        }

        /// <summary>
        /// Chance of a bounce. Deterministic outside a band around the critical angle,
        /// linear inside it: the surface is not a plane at the scale a bullet meets it,
        /// and roughness is the one thing here that is honestly a die roll.
        /// </summary>
        public static double RicochetChance(double alphaDeg, double alphaCritDeg, Tuning t)
        {
            if (alphaCritDeg <= 0)
            {
                return 0;
            }

            var half = alphaCritDeg * Math.Max(t.RicochetBand, 0);
            if (half <= 0)
            {
                return alphaDeg < alphaCritDeg ? 1 : 0;
            }

            var lo = alphaCritDeg - half;
            var hi = alphaCritDeg + half;
            if (alphaDeg <= lo)
            {
                return 1;
            }

            return alphaDeg >= hi ? 0 : (hi - alphaDeg) / (hi - lo);
        }

        /// <summary>
        /// What fraction of its speed a bounced projectile keeps. Most at a grazing
        /// angle, least right at the critical one, where it has almost buried itself
        /// before coming out.
        /// </summary>
        public static double RicochetRetention(double alphaDeg, double alphaCritDeg,
            double retention, Tuning t)
        {
            if (retention <= 0)
            {
                return 0;
            }

            var frac = alphaCritDeg > 0 ? Clamp01(alphaDeg / alphaCritDeg) : 0;
            var k = retention * (1.0 - Clamp01(t.RicochetLoss) * frac);
            return k < 0 ? 0 : k > 1 ? 1 : k;
        }

        /// <summary>
        /// The angle a bounced projectile actually leaves at, degrees from the surface.
        ///
        /// Never the mirror angle. The surface yields under the impact and the
        /// projectile climbs out of a shallow trough it dug itself, so the departure is
        /// flatter than the arrival — one of the few things about ricochet the forensic
        /// literature is unanimous on. Scaling the normal component of the reflection by
        /// f is the same statement as tan α_out = f·tan α_in, which is what the patch
        /// does to the vector and what this reports as an angle.
        /// </summary>
        public static double ExitGrazeDeg(double alphaDeg, Tuning t)
        {
            var f = t.RicochetFlatten;
            if (f <= 0 || f >= 1 || alphaDeg <= 0)
            {
                return alphaDeg;
            }

            var rad = alphaDeg * Math.PI / 180.0;
            return Math.Atan(f * Math.Tan(rad)) * 180.0 / Math.PI;
        }

        /// <summary>
        /// The limit along the true line of arrival, with no obliquity floor.
        ///
        /// The floor in the penetration verdict answers an exit question — a graze
        /// that does get through leaves by a chord its own calibre digs, not by an
        /// infinite slant — but refusal is not an exit question. Whether a sheet can
        /// turn a projectile away is decided by everything the trajectory would have
        /// to displace, and at a graze that is arbitrarily much. At normal incidence
        /// this is exactly the verdict's own limit.
        /// </summary>
        public static double RefusalLimit(Projectile p, Barrier b, double cos)
        {
            if (b.Mechanism != MechSteel)
            {
                return 0;
            }

            var c = Math.Abs(cos);
            if (c < 1e-4)
            {
                return double.MaxValue; // a pure graze displaces the whole sheet
            }

            var tuning = BallisticLimit.Tuning.Default;
            var slanted = LimitBarrier(b);
            slanted.ThicknessMm = WallMm(b) / c;
            return BallisticLimit.V50(slanted, Driving(p, tuning), 1.0, p.V, tuning);
        }

        /// <summary>
        /// A barrier can only throw off what it could refuse. For steel the gate is
        /// the ballistic limit along the true line of arrival (RefusalLimit, with this
        /// encounter's draw), so a round that would punch through a roof does exactly
        /// that instead of bouncing — and the same roof still skips the bullet that
        /// arrives spent, or at a few degrees of graze, where the slant it would have
        /// to displace is many times the sheet. The folk rule "thinner than the
        /// calibre never ricochets" falls out as a special case.
        ///
        /// Bulk media are now gated the same way, on whether the path at this
        /// obliquity would stop the projectile. The first edition left them ungated on
        /// the argument that a bounce off wood or soil is a surface phenomenon — the
        /// projectile digs a trough and climbs out — and for a semi-infinite medium
        /// that is true and the gate changes nothing: the medium stops the round, so
        /// it may bounce. What the argument missed is the THIN bulk member. A table
        /// top is 20 mm of pine; at fifteen degrees of graze a P90 crosses its whole
        /// slant with most of its speed to spare, and there is no trough to climb out
        /// of when the far face is nearer than the stopping depth. In play that read
        /// as tables mirroring rifle fire. Surfaces and logs keep their bounces;
        /// planks stop pretending to be armour.
        ///
        /// Everything else — walls, freebies, vanilla — refuses by definition: for a
        /// wall it is literally true, and for a zero-cost crossing the concept does
        /// not apply (water's bounce lives on its own class and must not die here).
        /// </summary>
        public static bool SheetCanRefuse(Projectile p, Barrier b, Outcome o)
        {
            switch (b.Mechanism)
            {
                case MechSteel:
                    return p.V <= o.RefusalV50;
                case MechPoncelet:
                    return !o.Penetrates;
                default:
                    return true;
            }
        }

        /// <summary>
        /// Does the FAR face of a collider the projectile is already inside cost a
        /// second wall.
        ///
        /// A SOLID collider was charged for its whole depth on the way in, so its exit
        /// is the same crossing seen from the other side and charging it again would
        /// count every wall twice. A SHELL was charged for one wall, and its far side is
        /// a second wall with air in between — a bullet through a barrel really does pay
        /// twice — but only where there was room in there for two walls.
        ///
        /// How that room is measured is the whole of this function. The chord of the
        /// collider is the obvious candidate and it is wrong for the commonest shape in
        /// the game: a trailer, a gantry crane, a stack of pipes and a truck body are
        /// each ONE non-convex mesh over the whole prop, so the chord is metres where
        /// the sheet is millimetres, and every solid region inside it hands out a free
        /// entry face (charged, correctly) and a free exit face (charged, wrongly).
        /// Crossing two real sheets of one trailer cost up to four.
        ///
        /// What actually says whether there was a cavity is how far the projectile has
        /// FLOWN inside this collider since it last struck it: two faces of one sheet
        /// are millimetres apart however big the mesh around them is, and the two skins
        /// of a barrel are the barrel's diameter apart. That distance is the caller's to
        /// find (walking the chain of parents for the last hit on this same collider);
        /// where there is none to find — a fragment born inside, a chain the engine
        /// released early — the chord is still the best guess there is and the old rule
        /// stands.
        ///
        /// <paramref name="cavityMm"/> is the book's <c>ShellCavityMm</c> and nothing
        /// else: the same threshold, asked of a better length.
        /// </summary>
        public static bool FarFaceCharges(bool solid, bool hasAnchor, double anchorDistMm,
            double chordMm, double cavityMm)
        {
            if (solid)
            {
                return false;
            }

            return (hasAnchor ? anchorDistMm : chordMm) >= cavityMm;
        }

        /// <summary>Which resolved decision, if any, the projectile about to be born
        /// carries away from the collision that spawned it.</summary>
        public enum LaunchSource
        {
            None,
            Penetration,
            Ricochet,
        }

        /// <summary>
        /// What the module wants the engine to build the child with. Speed always;
        /// direction only where the module has a deflection of its own, because "no
        /// model of ours" must not silently mean "no deflection at all" — a barrier with
        /// no body to it (wire mesh, grass) has to keep vanilla's own scatter.
        /// </summary>
        public struct ChildLaunch
        {
            public LaunchSource Source;
            public bool RebuildDirection;
            public double SpeedMs;
        }

        /// <summary>
        /// Which of the two decisions a collision left behind applies to the child it is
        /// spawning, and at what speed.
        ///
        /// Three things are deliberate here, and each of them is a bug that was reasoned
        /// out before it could be shipped:
        ///
        /// - a SHATTERED core launches nothing. The engine builds a shattered projectile
        ///   by calling the same spawn N times with the same parent, and handing every
        ///   one of those the whole exit speed would create energy out of nothing.
        /// - penetration is asked BEFORE the bounce. The ricochet gate is asked first by
        ///   vanilla and can be overruled by it afterwards (a projectile that has already
        ///   bounced twice is not allowed a third), which leaves a "this bounced" stamp
        ///   behind on a collision that then went through. The penetration verdict is
        ///   the later and truer one.
        /// - a bounce always rebuilds the direction, because the whole content of a
        ///   ricochet is where it went.
        /// </summary>
        public static ChildLaunch Launch(bool pierced, Outcome exit, bool bounced,
            double retention, double parentSpeedMs)
        {
            if (pierced)
            {
                if (exit.Fate == BallisticLimit.CoreFate.Shattered)
                {
                    return default;
                }

                return new ChildLaunch
                {
                    Source = LaunchSource.Penetration,
                    RebuildDirection = exit.Deviation > 0,
                    SpeedMs = exit.ExitV,
                };
            }

            if (bounced)
            {
                return new ChildLaunch
                {
                    Source = LaunchSource.Ricochet,
                    RebuildDirection = true,
                    SpeedMs = parentSpeedMs * (retention < 0 ? 0 : retention),
                };
            }

            return default;
        }

        private static double Area(double diaMm)
        {
            return Math.PI * diaMm * diaMm / 4.0;
        }

        private static double Clamp01(double v)
        {
            return v < 0 ? 0 : v > 1 ? 1 : v;
        }
    }
}
