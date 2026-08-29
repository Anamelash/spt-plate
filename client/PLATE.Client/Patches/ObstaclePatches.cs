using System;
using System.Reflection;
using EFT.Ballistics;
using HarmonyLib;
using PLATE.Client.Ballistics;
using PLATE.Client.Overlay;
using UnityEngine;

namespace PLATE.Client.Patches
{
    /// <summary>
    /// Walls, doors and sheet metal as barriers rather than as gates.
    ///
    /// Vanilla decides an obstacle with a threshold on the cartridge's template
    /// penetration number and a coin flip, and charges a projectile that gets through
    /// exactly nothing: the child spawned on the far side of a door carries the parent's
    /// full speed, damage and penetration. That makes the environment transparent to the
    /// wound model — the same round is as lethal through a plank as in the open — and it
    /// leaves the obstacle gate as the last consumer of a template number the rest of
    /// the mod has already replaced with physics.
    ///
    /// Four hooks here, all of them on the environment side only:
    ///   IsPenetrated          — the barrier decides, deterministically
    ///   Deflects              — the bounce decides, by grazing angle
    ///   IsBulletFragmented    — whether the projectile stopped existing as one
    ///   CreateDeviatedFragment — what the barrier left of what came through
    ///
    /// and a fifth outside it: the MOTION of whatever the collision spawns — the speed
    /// and the direction, for a pass-through and a bounce alike — is laid into the
    /// arguments of EftBulletClass.Create by ShotLifecyclePatches, because the engine builds the
    /// projectile's whole trajectory table there and ignores anything written after.
    ///
    /// The hooks read a decision the matching prefix left behind: the gate is called
    /// from inside HandleCollision, one shot at a time on the ballistics thread, so a
    /// single slot is enough (the pattern SurvivabilityPatches uses for the damage
    /// spill). The two gate prefixes share one barrier resolution per collision the same
    /// way — vanilla asks Deflects first, and the bounce is gated on the verdict
    /// IsPenetrated is about to hand out, because a sheet can only throw off what it
    /// could refuse.
    ///
    /// Bodies are not ours here — BodyPartCollider overrides both virtuals, so a patch
    /// on the base never reaches them, and the guards below say so out loud anyway.
    /// </summary>
    internal static class ObstaclePatches
    {
        public static void Apply(Harmony harmony)
        {
            PatchSafe(harmony, PatchTargets.Obstacle_IsPenetrated,
                nameof(IsPenetratedPrefix), prefix: true);
            PatchSafe(harmony, PatchTargets.Obstacle_Deflects,
                nameof(DeflectsPrefix), prefix: true);
            PatchSafe(harmony, PatchTargets.Bullet_Overpenetrate,
                nameof(DeviatedChildPostfix), prefix: false);
            PatchSafe(harmony, PatchTargets.Bullet_ShouldFragment,
                nameof(FragmentationPrefix), prefix: true);

            // the book is read here rather than on the first shot: a malformed file
            // should be in the log at startup, next to the self-test, and not in the
            // middle of a firefight
            var book = ObstacleReference.Current;
            Plugin.Log.LogInfo(book == null
                ? "[PLATE] Obstacle physics: reference book unavailable, every wall stays vanilla"
                : $"[PLATE] Obstacle physics enabled (reference v{book.Version}, " +
                  $"{book.Materials?.Count ?? 0} materials)");
        }

        private static void PatchSafe(Harmony harmony, MethodBase target, string name, bool prefix)
        {
            if (target == null)
            {
                PatchStats.MarkFailed(null, Label(name), "target not resolved");
                Plugin.Log.LogError($"[PLATE] Obstacles: target for {name} not resolved, skipped");
                return;
            }

            try
            {
                var patch = new HarmonyMethod(typeof(ObstaclePatches), name);
                harmony.Patch(target, prefix: prefix ? patch : null,
                    postfix: prefix ? null : patch);
                PatchStats.Track(harmony, target, Label(name));
            }
            catch (Exception ex)
            {
                PatchStats.MarkFailed(target, Label(name), ex.Message);
                Plugin.Log.LogError($"[PLATE] Obstacles: failed to patch {target.Name}: {ex.Message}");
            }
        }

        private static bool Off => !PlateClientConfig.ObstacleEnabled.Value;

        /// <summary>
        /// Telemetry label for a hook here. Prefixed because PatchStats keys its rows by
        /// LABEL, and this class and BallisticsPatches both call their gate
        /// `IsPenetratedPrefix` — one on the body's collider, one on the environment's.
        /// Sharing a name merged them into a single row and a single counter, so neither
        /// could be read on its own, which is the exact indistinguishability the hook
        /// report exists to prevent. Same convention OverlayPatches already uses.
        /// </summary>
        private static string Label(string name)
        {
            return "obstacle:" + name;
        }

        // --- The decision left for the child ---

        private static EftBulletClass _pierced;
        private static ObstacleModel.Outcome _exit;
        private static int _piercedFrame;

        private static EftBulletClass _bounced;
        private static float _bounceRetention;
        private static Vector3 _bounceNormal;
        private static int _bouncedFrame;

        // one barrier resolution per collision, shared by the two gate prefixes:
        // vanilla asks Deflects and then IsPenetrated about the same collider inside
        // one HandleCollision, and the ricochet gate must agree with the penetration
        // verdict it is gating on
        private static EftBulletClass _resolvedShot;
        private static int _resolvedFrame;
        private static BallisticCollider _resolvedFor;
        private static ObstacleModel.Barrier _resolvedBarrier;
        private static ObstacleModel.Projectile _resolvedProjectile;
        private static ObstacleModel.Outcome _resolvedOutcome;

        // whether this collision resolved its thickness off the scene or out of the book —
        // the single most useful thing to know when a wall behaves unexpectedly
        private static bool _resolvedMeasured;

        // the full chord through this collider along the trajectory, mm — the object's
        // extent, which for a shell is its outline rather than its wall
        private static double _resolvedChordMm;

        // The verdict, kept for whoever asks after the fact. Separate from _pierced on
        // purpose: that one is consumed and cleared by the child postfix, which runs
        // inside CreateFragments and therefore BEFORE the HandleCollision postfixes that
        // report the collision. This slot is only ever overwritten, never cleared.
        private static EftBulletClass _verdictShot;
        private static int _verdictFrame;
        private static bool _verdictPenetrated;
        private static PLATE.Server.Services.BallisticLimit.CoreFate _verdictFate;

        // Whether the exit state has already been laid into a child this collision. The
        // engine spawns exactly one child for an overpenetration and one for a bounce,
        // but it spawns N for a shattered core from the same parent, and the slot is what
        // makes "apply once" a property of the code rather than of that arithmetic.
        private static bool _exitApplied;

        // A far face that cost nothing, for the instruments. The marker and the survey
        // fire on every COLLISION, and a free exit is a collision the model deliberately
        // did not charge: without this they show a second marker six centimetres behind
        // the first, both labelled with the full thickness, and count a four-shot door as
        // eight hits. Frame- and collider-scoped like every other slot here.
        private static EftBulletClass _freeExitShot;
        private static int _freeExitFrame;
        private static BallisticCollider _freeExitFor;

        /// <summary>
        /// What material this collider counts as. The game's own MaterialType, unless the
        /// scene knows better: a name rule claims the object outright, the designer's own
        /// `_BALLISTIC_` word contradicts the preset, or the prop hangs under a grouping
        /// node that says what it is part of. See
        /// <see cref="ObstacleReference.EffectiveMaterial"/> for the three layers.
        ///
        /// One place for it deliberately: the physics, the journal line and the marker
        /// label all have to name the same material, or the next person reads a brick
        /// wall priced as brick under a line that says Concrete and goes looking for a
        /// bug in the arithmetic.
        /// </summary>
        internal static string MaterialOf(BallisticCollider collider)
        {
            if (collider == null)
            {
                return null;
            }

            Identify(collider, out var material, out _, out _);
            return material;
        }

        // what the last collider asked about resolved to. The gate prefixes, the journal
        // and the marker all ask about the same collider inside one HandleCollision, and
        // the answer costs four native `name` reads and three passes of the layers —
        // worth doing once. Frame-scoped like every other slot here, so a collider that
        // is reparented between frames is read afresh.
        private static BallisticCollider _identifiedFor;
        private static int _identifiedFrame;
        private static string _identifiedMaterial;
        private static double _identifiedWalls;
        private static double _identifiedLeafMm;
        private static string[] _identifiedNames;

        /// <summary>
        /// Everything the resolution reads out of the scene: what this collider is made
        /// of, how many of the book's walls its entry face is
        /// (<see cref="ObstacleReference.WallsCrossed"/>), and whether it is a door
        /// leaf of a fixed thickness
        /// (<see cref="ObstacleReference.DoorLeafThicknessMm"/>). One walk of the
        /// transform chain answers all three, and they must be answered together — the
        /// leaf semantics come from the same ancestors the material does, and from the
        /// material they resolve to.
        /// </summary>
        private static void Identify(BallisticCollider collider, out string material,
            out double walls, out double leafMm)
        {
            if (ReferenceEquals(_identifiedFor, collider) && _identifiedFrame == Time.frameCount)
            {
                material = _identifiedMaterial;
                walls = _identifiedWalls;
                leafMm = _identifiedLeafMm;
                return;
            }

            var book = ObstacleReference.Current;
            var names = NamesOf(collider);
            material = ObstacleReference.EffectiveMaterial(book,
                collider.TypeOfMaterial.ToString(), names);
            walls = ObstacleReference.WallsCrossed(book, material, names);
            leafMm = ObstacleReference.DoorLeafThicknessMm(book, material, names);

            _identifiedFor = collider;
            _identifiedFrame = Time.frameCount;
            _identifiedMaterial = material;
            _identifiedWalls = walls;
            _identifiedLeafMm = leafMm;
            _identifiedNames = names;
        }

        /// <summary>
        /// What this collider and its ancestry are called, `obj` and `par` as the survey
        /// spells them: the collider's own name, then two levels up joined by a slash,
        /// spaces turned into underscores so each stays one awk column.
        ///
        /// Free, because <see cref="Identify"/> already reads those names to resolve the
        /// material and caches them per collider per frame. Worth saying out loud that
        /// the per-hit journal went without them for a whole campaign while the
        /// aggregated survey carried both: a raid's per-hit lines could not be attributed
        /// to a prop at all, and had to be matched against neighbouring survey rows by
        /// timestamp, which is unreliable and sometimes impossible.
        /// </summary>
        internal static void NamesFor(BallisticCollider collider, out string obj, out string par)
        {
            Identify(collider, out _, out _, out _);
            var names = _identifiedNames;
            obj = Column(names != null && names.Length > 0 ? names[0] : null);

            var chain = names != null && names.Length > 1 ? Column(names[1]) : "-";
            if (names != null && names.Length > 2 && !string.IsNullOrEmpty(names[2]))
            {
                chain += "/" + Column(names[2]);
            }

            par = chain;
        }

        private static string Column(string name)
        {
            return string.IsNullOrEmpty(name) ? "-" : name.Replace(' ', '_');
        }

        /// <summary>
        /// The names the resolution reads: the collider's own first, then three
        /// ancestors, nearest first.
        ///
        /// The ancestors come along because half the scene names its colliders nothing
        /// at all — a BTR is three boxes called "MetalThick" under `balistic/BTR_82`, a
        /// fridge door is `Fridge (1)/Door_D/Ballistic 1/Metal 1` — and the prop's
        /// identity is the only place a rule can hook. They also carry the grouping
        /// nodes (`VEHICLES`, `DOORS`) the taxonomy layer reads.
        ///
        /// Collected unconditionally. The early version skipped the whole walk when the
        /// material had no name rules of its own, to save a native `gameObject.name`
        /// call per hit; the suffix and taxonomy layers made that a silent hole —
        /// `MetalNoDecal` has no rules and 209 of its colliders carry a `metalthin`
        /// suffix, and every one of them would have been left on a preset the designer
        /// disagreed with.
        /// </summary>
        private static string[] NamesOf(BallisticCollider collider)
        {
            string p1 = null, p2 = null, p3 = null;
            try
            {
                var t = collider.transform != null ? collider.transform.parent : null;
                if (t != null)
                {
                    p1 = t.name;
                    t = t.parent;
                    if (t != null)
                    {
                        p2 = t.name;
                        t = t.parent;
                        if (t != null)
                        {
                            p3 = t.name;
                        }
                    }
                }
            }
            catch
            {
                // a dying rig mid-frame: match on whatever names were reached
            }

            return new[] { NameOfObject(collider), p1, p2, p3 };
        }

        private static string NameOfObject(BallisticCollider collider)
        {
            try
            {
                return collider.gameObject != null ? collider.gameObject.name : null;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// How far this projectile's chain has flown INSIDE this collider since it last
        /// struck it, mm.
        ///
        /// No stored state and no table: every crossing of a barrier spawns a child whose
        /// `Parent` is the projectile that made the crossing, and each of those parents
        /// remembers the collider it hit and where. So the nearest ancestor that hit THIS
        /// collider is the last time the chain was at one of its faces, and the distance
        /// between the two hit points is how much of the object lies between them —
        /// millimetres for the two faces of one sheet, the diameter for the two skins of
        /// a barrel, whatever the size of the mesh drawn around either.
        ///
        /// The role of that ancestor's own face is deliberately not asked about. A
        /// projectile crossing the second sheet of a trailer meets its FRONT face, and
        /// requiring a forward hit there would break the chain at exactly the case this
        /// exists for.
        ///
        /// The chain is not trusted unconditionally. The engine releases a shot whose
        /// children are still flying when a tick throws, and a released shot goes back
        /// into the pool to be reused. Most of that is caught for free — pooling nulls
        /// both `Parent` and `HittedBallisticCollider`, so a released ancestor stops
        /// matching — and what is left, an object reissued and sent at the same collider
        /// again, is caught by requiring the distance to fit inside the object being
        /// crossed. Anything that fails falls back to the chord rule, which is what the
        /// module did everywhere before this.
        ///
        /// <paramref name="limitMm"/> is that ceiling, the widest separation two points
        /// of the object can have; 0 means it could not be read and the check is skipped.
        /// It is passed in rather than read here so the walk itself stays free of native
        /// calls and can be driven by a test against the real fields — which is also what
        /// makes a future SPT renaming `Parent` or `HittedBallisticCollider` fail loudly.
        /// </summary>
        internal static bool TryAnchorDistanceMm(EftBulletClass shot, BallisticCollider collider,
            double limitMm, out double distanceMm)
        {
            distanceMm = 0;

            // ReferenceEquals, not ==: BallisticCollider is a UnityEngine.Object and
            // Unity's operator== reports a "fake null" for anything whose native side is
            // gone — which is every collider a headless test can build, and would make
            // this walk untestable while looking perfectly correct.
            if (shot == null || ReferenceEquals(collider, null))
            {
                return false;
            }

            var here = shot.HitPoint;
            var depth = 0;
            for (var s = shot.Parent; s != null && depth < MaxChainWalk; s = s.Parent, depth++)
            {
                if (!ReferenceEquals(s.HittedBallisticCollider, collider))
                {
                    continue;
                }

                var mm = Vector3.Distance(here, s.HitPoint) * 1000.0;
                if (!(mm > 0) || (limitMm > 0 && mm > limitMm))
                {
                    return false;
                }

                distanceMm = mm;
                return true;
            }

            return false;
        }

        /// <summary>
        /// The widest separation two points of the object under the projectile can have,
        /// mm — the bounding diagonal of the Unity collider it is inside. 0 when it
        /// cannot be read, which reads as "no ceiling" downstream.
        /// </summary>
        private static double ColliderExtentMm(EftBulletClass shot)
        {
            try
            {
                var hit = shot.RaycastHit_0.collider;
                return hit == null ? 0 : hit.bounds.size.magnitude * 1000.0;
            }
            catch
            {
                // a dying rig mid-frame: no bound to check against, the rest still holds
                return 0;
            }
        }

        /// <summary>
        /// Ceiling on the walk up the chain. The engine's own chains end after about a
        /// dozen crossings (each one takes 0.08 off the deviation budget), so this is
        /// never reached in play; it is here because a shot released while its children
        /// fly can in principle be reissued as an ancestor of its own descendant, and a
        /// walk with no ceiling would then not terminate.
        /// </summary>
        private const int MaxChainWalk = 32;

        /// <summary>
        /// Was this collision a far face the model let through for free — the exit of a
        /// crossing already paid for on the way in.
        ///
        /// For the instruments, which count collisions rather than charges. False also
        /// when the module did not decide this collision at all, which is honest: with
        /// the module off there is no such thing as a free exit, every collision is
        /// vanilla's own.
        /// </summary>
        internal static bool FreeExitThisHit(EftBulletClass shot, BallisticCollider collider)
        {
            return Claims(_freeExitShot, _freeExitFrame, shot) &&
                   ReferenceEquals(_freeExitFor, collider);
        }

        /// <summary>
        /// The state the resolved collision hands the projectile the engine is about to
        /// build, as launch arguments rather than as a write after the fact. Called from
        /// the prefix on EftBulletClass.Create — see ShotLifecyclePatches for why it has to be
        /// there and nowhere later.
        ///
        /// <paramref name="parent"/> is what identifies the collision: it is the shot
        /// whose HandleCollision is still on the stack, and the slots below are stamped
        /// with it. Which slot wins, and whether either does, is
        /// <see cref="ObstacleModel.Launch"/> — pure, and tested against all three of the
        /// ways this can go wrong.
        /// </summary>
        internal static bool TryChildLaunch(EftBulletClass parent, Vector3 vanillaDirection,
            out float speedMs, out Vector3 direction, out bool rebuildDirection)
        {
            speedMs = 0f;
            direction = Vector3.zero;
            rebuildDirection = false;

            if (Off || parent == null)
            {
                return false;
            }

            try
            {
                var pierced = Claims(_pierced, _piercedFrame, parent) && !_exitApplied;
                var bounced = Claims(_bounced, _bouncedFrame, parent);
                var launch = ObstacleModel.Launch(pierced, _exit, bounced, _bounceRetention,
                    parent.Vector3_1.magnitude);

                if (launch.Source == ObstacleModel.LaunchSource.None)
                {
                    return false;
                }

                // one stamp, one child. The penetration slot is left standing because the
                // postfix on the spawner still reads it for the mass, calibre and yaw the
                // child only exists to receive; the bounce slot has no such reader and is
                // dropped, so a verdict vanilla declined cannot be collected later.
                _exitApplied = true;
                _bounced = null;

                speedMs = Mathf.Max((float)launch.SpeedMs, 0.1f);
                rebuildDirection = launch.RebuildDirection;
                if (rebuildDirection)
                {
                    direction = launch.Source == ObstacleModel.LaunchSource.Ricochet
                        ? FlattenedBounce(vanillaDirection, parent)
                        : DeviatedDirection(parent, (float)_exit.Deviation);
                }

                return true;
            }
            catch (Exception ex)
            {
                LogError(nameof(TryChildLaunch), ex);
                return false;
            }
        }

        /// <summary>
        /// Where a projectile that got through is pointing: the PARENT's direction with
        /// the barrier's own scatter on it.
        ///
        /// The parent's rather than the child's, because the child vanilla is about to
        /// build already carries vanilla's per-material draw, and keeping it would stack
        /// a second, unphysical scatter on top of ours. The scatter itself is vanilla's
        /// own idiom, drawn from vanilla's own random stream so that a replayed shot
        /// replays: a random unit vector of this length added to a unit direction.
        /// </summary>
        private static Vector3 DeviatedDirection(EftBulletClass parent, float spread)
        {
            var dir = parent.Vector3_1.sqrMagnitude > 1e-6f
                ? parent.Vector3_1.normalized
                : parent.Direction;

            var tilted = dir + parent.Randoms.GetRandomDirection(parent.RandomSeed) * spread;
            return tilted.sqrMagnitude > 1e-6f ? tilted.normalized : dir;
        }

        /// <summary>
        /// Where a projectile that bounced is pointing: vanilla's own reflection,
        /// flattened towards the surface. A ricochet leaves at a smaller angle than it
        /// arrived at, which is one of the few things the forensic literature is
        /// unanimous on.
        ///
        /// Vanilla's direction is taken as the starting point rather than recomputed,
        /// because it already carries the mirror AND the surface's own scatter draw, and
        /// rebuilding it would silently throw that scatter away.
        /// </summary>
        private static Vector3 FlattenedBounce(Vector3 vanillaDirection, EftBulletClass parent)
        {
            var dir = vanillaDirection.sqrMagnitude > 1e-6f
                ? vanillaDirection.normalized
                : parent.Vector3_1.sqrMagnitude > 1e-6f
                    ? parent.Vector3_1.normalized
                    : parent.Direction;

            var flatten = (float)ObstacleReference.TuningOf(ObstacleReference.Current)
                .RicochetFlatten;
            if (flatten > 0f && flatten < 1f && _bounceNormal.sqrMagnitude > 1e-6f)
            {
                var n = _bounceNormal.normalized;
                // scaling the normal component of the reflection is the same statement
                // as tan(a_out) = f·tan(a_in)
                var along = Vector3.Dot(dir, n);
                var flattened = dir - n * along * (1f - flatten);
                if (flattened.sqrMagnitude > 1e-6f)
                {
                    dir = flattened.normalized;
                }
            }

            return dir;
        }

        /// <summary>Did the module decide this collision at all.</summary>
        private static bool HasVerdict(EftBulletClass shot)
        {
            return Claims(_verdictShot, _verdictFrame, shot) && _verdictPenetrated;
        }

        /// <summary>
        /// Did this collision deform the projectile — for the journal, which otherwise
        /// has only the engine's word for it, and the engine has never heard of a wall
        /// changing a bullet. Read-only, and false whenever the module did not decide
        /// this collision at all.
        /// </summary>
        internal static bool DeformedThisHit(EftBulletClass shot)
        {
            return HasVerdict(shot) &&
                   _verdictFate != PLATE.Server.Services.BallisticLimit.CoreFate.Rigid;
        }

        /// <summary>
        /// The thickness the decision was actually taken against, mm, and whether it came
        /// off the scene rather than out of the book.
        ///
        /// Not the same number as the raw chord, and the difference is the point: for a
        /// solid material the collider IS the path and the two agree, but for a shell the
        /// chord is the outline of a barrel and the thickness is the millimetre of steel
        /// at its wall. Reporting the chord for a shell says a tyre is 900 mm thick right
        /// next to a hole the bullet plainly made.
        ///
        /// Falls back to the raw measurement when this module did not decide the
        /// collision — a vanilla material has no thickness of ours to report, and what
        /// the scene measures is still the useful thing to see.
        /// </summary>
        internal static bool TryThicknessUsedMm(EftBulletClass shot, BallisticCollider collider,
            out double thicknessMm, out bool measured)
        {
            if (Claims(_resolvedShot, _resolvedFrame, shot) &&
                ReferenceEquals(_resolvedFor, collider) && _resolvedBarrier.ThicknessMm > 0)
            {
                // what the verdict was actually taken against, both walls of a door leaf
                // included — a marker saying 1 mm over a hole the model priced at 2 is
                // the same lie as reporting a tyre's outline
                thicknessMm = ObstacleModel.WallMm(_resolvedBarrier);
                measured = _resolvedMeasured;
                return true;
            }

            measured = true;
            return TryMeasureThicknessMm(shot, out thicknessMm);
        }

        /// <summary>
        /// Is this the shot the matching prefix decided for, this frame.
        ///
        /// Reference equality alone is not enough: shots come out of a pool, so a
        /// decision nobody collected — the gate says "bounce" and vanilla then declines
        /// because the shot has already ricocheted twice — could be picked up much later
        /// by a recycled object wearing the same reference. The prefix and the postfix
        /// both run inside one HandleCollision, so requiring the frame as well bounds
        /// the staleness to nothing at all.
        /// </summary>
        private static bool Claims(EftBulletClass stash, int frame, EftBulletClass shot)
        {
            return ReferenceEquals(stash, shot) && frame == Time.frameCount;
        }

        /// <summary>
        /// The barrier, the projectile and the verdict for this collision, computed
        /// once and shared: Deflects (which vanilla asks first) gates the bounce on
        /// it, IsPenetrated (asked right after when nothing bounced) reads the same
        /// answer instead of recomputing it. False means the material is the game's
        /// business or the projectile has no state to compute with.
        /// </summary>
        private static bool TryResolve(BallisticCollider collider, EftBulletClass shot,
            out ObstacleModel.Barrier barrier, out ObstacleModel.Projectile projectile,
            out ObstacleModel.Outcome outcome)
        {
            if (Claims(_resolvedShot, _resolvedFrame, shot) &&
                ReferenceEquals(_resolvedFor, collider))
            {
                barrier = _resolvedBarrier;
                projectile = _resolvedProjectile;
                outcome = _resolvedOutcome;
                return true;
            }

            barrier = default;
            projectile = default;
            outcome = default;

            var book = ObstacleReference.Current;
            Identify(collider, out var material, out var walls, out var leafMm);

            if (!ObstacleReference.TryBarrier(book, material, collider.PenetrationLevel,
                    out barrier, walls, leafMm))
            {
                return false; // this material is the game's business
            }

            _resolvedMeasured = false;
            _resolvedChordMm = 0;
            projectile = Read(shot);
            if (projectile.MassG <= 0 || projectile.DiaMm <= 0)
            {
                return false; // nothing to compute with — leave it to vanilla
            }

            // The scene's own geometry outranks the book, but only where the collider is
            // the path. For a solid material it is: a bullet into a log crosses as much
            // wood as the log is deep, and an electric motor and a locker door are both
            // MetalThick with only one of them stopping a 5.45. For a shell it is not —
            // the collider is the outline of a barrel and the material is the millimetre
            // of steel at its wall, which the book already knows.
            _resolvedChordMm = TryMeasureThicknessMm(shot, out var chord) ? chord : 0;
            if (barrier.Solid && _resolvedChordMm > 0)
            {
                barrier.ThicknessMm = _resolvedChordMm;
                _resolvedMeasured = true;
            }

            // the sheet's own scatter: one draw per resolution, decorrelated from the
            // raw seed — GetRandomFloat is pure in its seed, and the raw one is what
            // vanilla's own gates and our ricochet band roll already read
            var mixed = GClass2608.RandomizeInt(shot.RandomSeed);
            var draw = shot.Randoms.GetRandomFloat(mixed);

            // and the seed a packed medium lays its cargo out with. Same stream, same
            // reason: the resolution is asked for more than once per collision (the
            // ricochet gate before the penetration verdict), and a replayed shot has to
            // meet the same boxes in the same places
            outcome = ObstacleModel.Resolve(projectile, barrier,
                ObstacleReference.TuningOf(book), shot.Float_3, draw,
                mixed);

            _resolvedShot = shot;
            _resolvedFrame = Time.frameCount;
            _resolvedFor = collider;
            _resolvedBarrier = barrier;
            _resolvedProjectile = projectile;
            _resolvedOutcome = outcome;
            return true;
        }

        /// <summary>
        /// Does this obstacle stop the projectile. Replaces the vanilla threshold and
        /// roll entirely: the answer is a ballistic limit for a steel sheet and a
        /// penetration depth for everything with bulk, both computed from the state the
        /// projectile actually arrives in.
        /// </summary>
        private static bool IsPenetratedPrefix(BallisticCollider __instance, EftBulletClass shot,
            ref bool __result)
        {
            PatchStats.Hit(Label(nameof(IsPenetratedPrefix)));
            if (Off || shot == null || __instance is BodyPartCollider)
            {
                return true;
            }

            var begin = PerfTrace.Begin();
            try
            {
                if (!TryResolve(__instance, shot, out var barrier, out var projectile,
                        out var outcome))
                {
                    NoteUnmapped(__instance, shot);
                    return true; // unmapped material or a stateless projectile — vanilla
                }

                // The far face of something the projectile is already inside — see
                // ObstacleModel.FarFaceCharges for what decides whether it costs a second
                // wall. The length it is decided on is the distance the chain has flown
                // inside THIS collider since it last struck it, and the chord of the
                // collider only where there is no such anchor to be had.
                if (!shot.IsForwardHit)
                {
                    var anchored = TryAnchorDistanceMm(shot, __instance, ColliderExtentMm(shot),
                        out var insideMm);
                    if (!ObstacleModel.FarFaceCharges(barrier.Solid, anchored, insideMm,
                            _resolvedChordMm,
                            ObstacleReference.TuningOf(ObstacleReference.Current).ShellCavityMm))
                    {
                        _freeExitShot = shot;
                        _freeExitFrame = Time.frameCount;
                        _freeExitFor = __instance;
                        __result = true;
                        return false;
                    }
                }

                var cos = shot.Float_3;

                _pierced = outcome.Penetrates ? shot : null;
                _piercedFrame = Time.frameCount;
                _exit = outcome;
                _exitApplied = false;
                __result = outcome.Penetrates;

                _verdictShot = shot;
                _verdictFrame = Time.frameCount;
                _verdictPenetrated = outcome.Penetrates;
                _verdictFate = outcome.Fate;

                Log(shot, __instance, barrier, projectile, outcome, cos);
                return false;
            }
            catch (Exception ex)
            {
                LogError(nameof(IsPenetratedPrefix), ex);
                return true;
            }
            finally
            {
                PerfTrace.End("obstacle.pen", begin);
            }
        }

        /// <summary>
        /// Does it bounce. Vanilla asks whether the angle is inside one window that is
        /// the same for concrete, water and tin, and then rolls two per-material
        /// chances; this asks whether the grazing angle is under the critical angle for
        /// this surface at this speed.
        /// </summary>
        private static bool DeflectsPrefix(BallisticCollider __instance, EftBulletClass shot,
            Vector3 shotNormal, ref bool __result)
        {
            PatchStats.Hit(Label(nameof(DeflectsPrefix)));
            if (Off || shot == null || __instance is BodyPartCollider)
            {
                return true;
            }

            var begin = PerfTrace.Begin();
            try
            {
                var book = ObstacleReference.Current;
                var material = MaterialOf(__instance);
                if (!ObstacleReference.TryRicochet(book, material, out var alpha0, out var retention))
                {
                    return true;
                }

                // a sheet can only throw off what it could refuse: the bounce is gated
                // on the ballistic limit along the true line of arrival, from the same
                // resolution (and the same scatter draw) the penetration verdict reads.
                // What punches through must punch through — and it will, when vanilla
                // asks IsPenetrated a moment after this says no.
                if (TryResolve(__instance, shot, out var gateBarrier, out var gateProjectile,
                        out var gateOutcome) &&
                    !ObstacleModel.SheetCanRefuse(gateProjectile, gateBarrier, gateOutcome))
                {
                    __result = false;
                    return false;
                }

                var tuning = ObstacleReference.TuningOf(book);
                var v = shot.Vector3_1.magnitude;
                var alpha = ObstacleModel.GrazeAngleDeg(shot.Float_3);
                var alphaCrit = ObstacleModel.CriticalAngleDeg(alpha0, v, tuning);
                var chance = ObstacleModel.RicochetChance(alpha, alphaCrit, tuning);

                // the same random stream vanilla draws from, so a replayed shot replays
                var bounces = chance > 0 &&
                              (chance >= 1 || chance > shot.Randoms.GetRandomFloat(shot.RandomSeed));
                __result = bounces;

                if (bounces)
                {
                    _bounced = shot;
                    _bouncedFrame = Time.frameCount;
                    _bounceRetention =
                        (float)ObstacleModel.RicochetRetention(alpha, alphaCrit, retention, tuning);
                    _bounceNormal = shotNormal;

                    if (WorthLogging(shot))
                    {
                        NamesFor(__instance, out var obj, out var par);
                        ObstacleSurvey.LogLine(
                            $"  wall {material} RICO a={alpha:0.0}deg (crit {alphaCrit:0.0}) " +
                            $"v {v:0}->{v * _bounceRetention:0} m/s" +
                            $" obj={obj} par={par} shot={HitFeed.ShotId(shot)}");
                    }
                }

                return false;
            }
            catch (Exception ex)
            {
                LogError(nameof(DeflectsPrefix), ex);
                return true;
            }
            finally
            {
                PerfTrace.End("obstacle.rico", begin);
            }
        }

        /// <summary>
        /// Does the projectile come apart against this obstacle.
        ///
        /// Vanilla rolls two chances, one on the cartridge and one on the collider, and
        /// on a hit the roll wins the projectile is replaced by fragments carrying
        /// 0.7/MaxFragments of its speed — a flat 77% velocity loss, which is 95% of the
        /// energy. A raid measured it: sixty-three times, every one landing on exactly
        /// 23.3% of the arrival speed, and the largest of them was a 5.45 steel core
        /// "destroyed" by 0.7 mm of sheet metal that the model priced at a 6% loss.
        ///
        /// It is also decoration rather than material: one scene object carried a 0.65
        /// fragmentation chance where its own preset says 0.07, so two bullets in three
        /// came apart on that one prop.
        ///
        /// So the same argument that replaced the penetration gate replaces this one.
        /// What happens to a projectile in a barrier is already computed — CoreFate — and
        /// a core that merely mushroomed is modelled as mushroomed: it comes out blunter
        /// and lighter through ArmorExit, not as three slow children. Only a core that
        /// SHATTERED is a projectile that stopped existing as one.
        ///
        /// Worth being plain about the consequence: with the shipped book that is nearly
        /// never. Shattering needs a brittle core (tungsten carbide, above 1000 HV) meeting
        /// a face hard enough to crack it, and of every material in the book only loose
        /// aggregate clears that ratio. Environment fragmentation therefore all but stops
        /// happening — which is the correct reading of "a bullet does not disintegrate on
        /// tin", not a switch quietly turning a feature off. MODEL.md says so too.
        /// </summary>
        private static bool FragmentationPrefix(EftBulletClass __instance, ref bool __result)
        {
            PatchStats.Hit(Label(nameof(FragmentationPrefix)));
            if (Off || __instance.HittedBallisticCollider is BodyPartCollider)
            {
                return true; // the body's fragmentation is the wound model's business
            }

            try
            {
                // no verdict means the module was never asked about this collider — an
                // unmapped material, or a projectile with no state. Vanilla decides, as
                // it does everything else about those.
                if (!HasVerdict(__instance))
                {
                    return true;
                }

                __result = _verdictFate ==
                           PLATE.Server.Services.BallisticLimit.CoreFate.Shattered;
                return false;
            }
            catch (Exception ex)
            {
                LogError(nameof(FragmentationPrefix), ex);
                return true;
            }
        }

        /// <summary>
        /// What is left of the projectile that came out the far side, other than its
        /// motion.
        ///
        /// Speed and direction are NOT here any more, and that is the point: the engine
        /// builds a child's whole trajectory table inside Create, from the arguments, and
        /// overwrites its velocity out of that table on every tick — so a velocity
        /// written afterwards survived only until the child's first tick and a bullet
        /// through a door arrived at a body thirty metres later with nearly its muzzle
        /// speed. They are laid into the arguments instead (ShotLifecyclePatches), and
        /// this hook keeps only what the child has to exist to receive.
        ///
        /// Damage and penetration are deliberately not touched. Both are derived from the
        /// state at the next impact (the wound model and AbsolutePenPostfix), so writing
        /// them now would only be a second, staler opinion.
        /// </summary>
        private static void DeviatedChildPostfix(EftBulletClass __instance)
        {
            PatchStats.Hit(Label(nameof(DeviatedChildPostfix)));
            if (Off || !Claims(_pierced, _piercedFrame, __instance))
            {
                return;
            }

            _pierced = null;
            try
            {
                if (__instance.HittedBallisticCollider is BodyPartCollider ||
                    __instance.Fragments.Count == 0)
                {
                    return; // the body's own overpenetration hook owns that case
                }

                var child = __instance.Fragments[__instance.Fragments.Count - 1];

                // What the barrier left of it. A rigid core comes through unchanged and
                // these are the values it went in with, so this costs nothing in the
                // common case and is the whole point in the others.
                child.BulletMassGram = (float)_exit.ExitMassG;
                child.BulletDiameterMilimeters = (float)_exit.ExitDiaMm;
                ProjectileState.SetX(child, (float)_exit.ExitX);

                // And how far over it is lying. Deliberately NOT written to the calibre:
                // the projectile did not get fatter, and the wound model downstream would
                // read a widened diameter as a wider bullet. Only the next barrier asks.
                ProjectileState.SetYaw(child, (float)_exit.ExitYaw);
            }
            catch (Exception ex)
            {
                LogError(nameof(DeviatedChildPostfix), ex);
            }
        }

        // --- Plumbing ---

        /// <summary>
        /// How much of this collider the projectile actually has to cross, mm, measured
        /// off the scene rather than looked up.
        ///
        /// Cast BACKWARDS from beyond the object: a ray that starts inside a collider
        /// registers nothing in Unity, so the far face can only be found by coming at it
        /// from outside. `Collider.Raycast` tests that one collider and no other, so
        /// nothing in front of or behind the object can be mistaken for it.
        ///
        /// The answer is taken as it comes, with no sanity clamp against the book. That
        /// is the point: a hollow shell measures as the whole shell, and an electric
        /// motor measures as an electric motor, which is exactly the case the book's
        /// per-material anchor gets wrong and the reason the anchor was only ever a
        /// stand-in for the geometry.
        ///
        /// False means the measurement did not come off — the probe missed, or the
        /// object has no depth along this line — and then the book's anchor stands.
        /// </summary>
        internal static bool TryMeasureThicknessMm(EftBulletClass shot, out double thicknessMm)
        {
            thicknessMm = 0;
            var begin = PerfTrace.Begin();
            try
            {
                var collider = shot.RaycastHit_0.collider;
                if (collider == null)
                {
                    return false;
                }

                var dir = shot.Vector3_1;
                if (dir.sqrMagnitude < 1e-8f)
                {
                    return false;
                }

                dir = dir.normalized;

                // far enough to be outside the object along any direction, whatever
                // shape it is: the diagonal of its own bounding box, plus a margin
                var probe = collider.bounds.size.magnitude + 1f;
                var here = shot.RaycastHit_0.point;

                // Both ends of the chord, because which end we are standing on depends
                // on which face was hit. A ray that starts inside a collider registers
                // nothing, so each surface can only be found by coming at it from
                // outside — forwards from before the object for the near face, backwards
                // from beyond it for the far one.
                var inbound = new Ray(here - dir * probe, dir);
                var outbound = new Ray(here + dir * probe, -dir);
                if (!collider.Raycast(inbound, out var near, probe * 2f) ||
                    !collider.Raycast(outbound, out var far, probe * 2f))
                {
                    return false;
                }

                var nearPoint = inbound.origin + dir * near.distance;
                var farPoint = outbound.origin - dir * far.distance;

                var mm = Vector3.Distance(nearPoint, farPoint) * 1000f;
                if (mm <= 0.01f)
                {
                    return false; // a graze along the surface, not a crossing
                }

                thicknessMm = mm;
                return true;
            }
            catch (Exception ex)
            {
                LogError(nameof(TryMeasureThicknessMm), ex);
                return false;
            }
            finally
            {
                PerfTrace.End("obstacle.measure", begin);
            }
        }

        /// <summary>The projectile as this module needs it: the four values, the core
        /// geometry the steel branch's ballistic limit asks for, and the broadside
        /// geometry yaw is computed against.</summary>
        private static ObstacleModel.Projectile Read(EftBulletClass shot)
        {
            AmmoDataCache.GetCore(shot.Ammo?.TemplateId, out var coreArea, out var coreMass);

            var massG = (double)shot.BulletMassGram;
            var diaMm = (double)shot.BulletDiameterMilimeters;
            var x = BallisticsPatches.EffectiveX(shot);

            // The same broadside geometry the wound channel is built on, from the same
            // server constants — a bullet has one length, and a second opinion about it
            // here would be a second set of numbers to keep in step. Those constants have
            // in-code defaults, so this works with no server at all, and the model core
            // reads none of it: it is handed the two numbers and stays pure.
            var yawGeometry = ClientWoundModel.Yaw(AmmoDataCache.Wound);

            // and the book's measured length where there is one. It matters more here
            // than in the channel: slenderness L/d − 1 is the lever arm the barrier tips
            // the bullet over with, and the mass-over-calibre inference reads a
            // steel-cored round short enough to call it a ball. 0 = nothing published, or
            // no server at all, and YawModel infers exactly as it always did.
            var lengthMm = AmmoDataCache.GetLengthMm(shot.Ammo?.TemplateId);

            return new ObstacleModel.Projectile
            {
                MassG = massG,
                DiaMm = diaMm,
                V = shot.Vector3_1.magnitude,
                X = x,
                CoreAreaFrac = coreArea,
                CoreMassFrac = coreMass,
                HardnessHv = AmmoDataCache.GetCoreHardness(shot.Ammo?.TemplateId),
                YawFrac = EffectiveYaw(shot),
                LengthMm = PLATE.Server.Services.YawModel.LengthMm(massG, diaMm, yawGeometry,
                    lengthMm),
                SideAreaMm2 = PLATE.Server.Services.YawModel.SideAreaMm2(massG, diaMm, x,
                    yawGeometry, lengthMm),
            };
        }

        /// <summary>
        /// How far off nose-on this projectile is already flying.
        ///
        /// The same walk up the chain BallisticsPatches.EffectiveX does for the
        /// deformable fraction, and for the same reason: a bullet through a wall spawns a
        /// child on the far side, that child spawns another through the next wall, and
        /// each one has to find the nearest ancestor a barrier had its way with. Nothing
        /// recorded means nose-on, which is how every bullet leaves a barrel.
        /// </summary>
        private static float EffectiveYaw(EftBulletClass shot)
        {
            for (var s = shot; s != null; s = s.Parent)
            {
                if (ProjectileState.TryGetYaw(s, out var recorded))
                {
                    return recorded;
                }
            }

            return 0f;
        }

        /// <summary>
        /// Is this worth a line in the journal.
        ///
        /// Every bullet a bot fires and misses with ends in soil, concrete or a tree, and
        /// every one of those is an obstacle interaction — so logging all of them buries
        /// the journal and the on-screen panel under other people's misses, which is
        /// exactly the file a bug report is read out of. Default is the local player's
        /// own shots; the switch is there for anyone studying what bots shoot through.
        ///
        /// The main player is read straight off the world rather than through the
        /// overlay's fight filter: that one only knows who you are while the overlay
        /// component is running, and the overlay is off by default.
        /// </summary>
        // one line per (material, level) pair per raid — see NoteUnmapped
        private static readonly System.Collections.Generic.HashSet<string> SeenUnmapped =
            new System.Collections.Generic.HashSet<string>();

        /// <summary>Per-raid state, cleared by the plugin when the world goes away.</summary>
        internal static void ResetRaidState()
        {
            SeenUnmapped.Clear();
        }

        /// <summary>
        /// Reports a material the book does not claim — once per (material, level) pair
        /// per raid, for the player's own shots.
        ///
        /// It exists because a material left to the game is otherwise INVISIBLE: the gate
        /// returns to vanilla before anything is written, so a raid teaches us nothing
        /// about it. That is the blind spot behind every open question in the book — what
        /// a tyre collider actually carries, what Fabric_HiPen is, whether some map puts a
        /// level on tall grass — and those cannot be answered by guessing at the anchors.
        ///
        /// The collider's own numbers go in the line, not the preset's: the preset is a
        /// designer's palette and the whole point is to find out what the scene really
        /// has. Deduplication keeps this to a few dozen lines a raid rather than one per
        /// bullet, and the pair is the key — the object name is only the first one seen,
        /// carried along because it says which door on which map to go and look at.
        /// </summary>
        private static void NoteUnmapped(BallisticCollider collider, EftBulletClass shot)
        {
            // in either mode, not only per-hit: discovery is one line per raid per
            // material and answers a question the aggregate rows only half-ask
            if (PlateClientConfig.ObstacleLog.Value == ObstacleLogMode.Off ||
                !LocalPlayerRef.IsShooter(shot.PlayerProfileID))
            {
                return;
            }

            var material = MaterialOf(collider);

            // Two different findings, and the line says which. A material the book
            // declares vanilla is a question we parked and can now answer; one the book
            // has never heard of is a modded material nobody has looked at. A material
            // the book DOES model reached here for the other reason TryResolve fails —
            // a projectile with no mass or calibre — and that is not a discovery.
            var entry = ObstacleReference.Material(ObstacleReference.Current, material);
            if (entry != null && entry.Mechanism != ObstacleModel.MechVanilla)
            {
                return;
            }

            var verdict = entry == null
                ? "not in the book at all (modded?)"
                : "left to the game on purpose";

            var level = collider.PenetrationLevel.ToString("0.#",
                System.Globalization.CultureInfo.InvariantCulture);
            if (!SeenUnmapped.Add(material + "/" + level))
            {
                return;
            }

            string objectName;
            try
            {
                objectName = collider.gameObject != null ? collider.gameObject.name : "?";
            }
            catch
            {
                objectName = "?";
            }

            ObstacleSurvey.LogLine(string.Format(
                System.Globalization.CultureInfo.InvariantCulture,
                "wall? {0}({1}) pl={2} pc={3:0.##} rc={4:0.##} fc={5:0.##} dev={6:0.###} — {7}",
                material, objectName, level, collider.PenetrationChance,
                collider.RicochetChance, collider.FragmentationChance,
                collider.TrajectoryDeviation, verdict));
        }

        private static bool WorthLogging(EftBulletClass shot)
        {
            if (PlateClientConfig.ObstacleLog.Value != ObstacleLogMode.EveryHit)
            {
                return false;
            }

            // no HitFeed attribution any more: wall lines live in the obstacle file
            // and never reach the event journals, so there is no second file to route
            return LocalPlayerRef.IsShooter(shot.PlayerProfileID) ||
                   !PlateClientConfig.ObstacleLogMineOnly.Value;
        }

        private static void Log(EftBulletClass shot, BallisticCollider collider,
            ObstacleModel.Barrier barrier, ObstacleModel.Projectile p,
            ObstacleModel.Outcome outcome, float cos)
        {
            if (!WorthLogging(shot))
            {
                return;
            }

            // grass and wire mesh are "through, for nothing", and every bullet that
            // misses crosses some. A line saying a projectile lost no speed is noise,
            // and the journal is what bug reports are read out of.
            if (barrier.Mechanism == ObstacleModel.MechAlways && barrier.CostJ <= 0)
            {
                return;
            }

            var head = $"  wall {MaterialOf(collider)} pl={collider.PenetrationLevel:0.#} " +
                       $"{barrier.Mechanism} h={ObstacleModel.WallMm(barrier):0.##}mm" +
                       (barrier.Walls > 1 ? $"({barrier.Walls:0.#} walls)" : "") +
                       (_resolvedMeasured ? "(measured) " : "(book) ") +
                       $"path={outcome.PathMm:0.##}mm a={ObstacleModel.GrazeAngleDeg(cos):0}deg";

            // Which prop, and which projectile. The aggregated survey carried both and
            // the per-hit line carried neither, which made the richer of the two files
            // the one that could not be read: a line said "MetalThick 4 mm" with no way
            // of telling which of a map's thousand metal things it was, and consecutive
            // lines of automatic fire down one wall are indistinguishable from the two
            // faces of one sheet without a projectile to hang them on.
            NamesFor(collider, out var obj, out var par);
            var tail = $" obj={obj} par={par} shot={HitFeed.ShotId(shot)}";

            if (outcome.Penetrates)
            {
                // what it did to the bullet, and only when it did something: a rigid
                // core through a plank is the common case and does not need a clause
                var toll = "";
                if (outcome.Fate != PLATE.Server.Services.BallisticLimit.CoreFate.Rigid)
                {
                    toll = $", {outcome.Fate.ToString().ToLowerInvariant()}" +
                           $" X {p.X:0.00}->{outcome.ExitX:0.00}";
                    if (outcome.ExitMassG < p.MassG * 0.995)
                    {
                        toll += $", {p.MassG:0.0}->{outcome.ExitMassG:0.0} g";
                    }
                }
                else if (outcome.ExitDiaMm < p.DiaMm * 0.995)
                {
                    toll = $", stripped to {outcome.ExitMassG:0.0} g / {outcome.ExitDiaMm:0.0} mm";
                }

                // How far over it is lying, in and out — only once there is any, because
                // nose-on is what almost every line in this file describes. This is what
                // a raid check reads to see a row of barrels getting dearer.
                if (outcome.ExitYaw > 0.005)
                {
                    toll += $", yaw={p.YawFrac:0.00}->{outcome.ExitYaw:0.00}";
                }

                ObstacleSurvey.LogLine($"{head} v {p.V:0}->{outcome.ExitV:0} m/s" +
                                  $", dev {outcome.Deviation:0.000}{toll}{tail}");
                return;
            }

            // how much it was short by, in whatever unit this mechanism argues in
            string deficit;
            if (barrier.Mechanism == ObstacleModel.MechSteel)
            {
                deficit = outcome.V50 > 0 ? $"needs {outcome.V50:0} m/s" : "no limit";
            }
            else if (barrier.Mechanism == ObstacleModel.MechPoncelet)
            {
                // against the RESISTING path, not the geometric one: a brittle slab
                // scabs its rear face off, so the two differ and the journal would
                // otherwise report a stop at a depth that looks like it got through
                deficit = "reached " +
                          $"{outcome.DepthMm:0.#} of " +
                          $"{ObstacleModel.ResistingPathMm(outcome.PathMm, barrier):0.#} mm";
            }
            else
            {
                deficit = "wall";
            }

            ObstacleSurvey.LogLine($"{head} v {p.V:0} STOP ({deficit}){tail}");
        }

        private static float _lastErrorLogged;

        private static void LogError(string where, Exception ex)
        {
            if (Time.unscaledTime - _lastErrorLogged < 5f)
            {
                return;
            }

            _lastErrorLogged = Time.unscaledTime;
            Plugin.Log.LogError($"[PLATE] Obstacles {where}: {ex}");
        }
    }
}
