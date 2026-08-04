using EFT;
using UnityEngine;

namespace PLATE.Client.Ballistics
{
    internal enum OrganZone
    {
        None = 0,
        Heart,
        Liver,
        Spine,
    }

    /// <summary>
    /// Organs as thirds of the hitboxes the game already has.
    ///
    /// No new geometry and no ellipsoids: the middle third of RibcageUp is the heart and
    /// the mediastinum behind it, the right third of RibcageLow is the liver, and a thin
    /// SpineTop or SpineDown collider is the cord. Lungs get no zone of their own —
    /// nearly the whole ribcage is lung, so a lung multiplier would be a multiplier on
    /// everything. In the AIS table one lobe of lung IS the reference point the other
    /// zones are scored against, so it is already in the model at 1.0.
    ///
    /// What decides a direct hit is how far the channel runs INSIDE the zone against how
    /// deep the zone is. That is the distinction AAST draws: a tangential wound of the
    /// myocardium that does not breach the endocardium is survivable, a perforated
    /// ventricle is not. Clipping the corner of the zone gives a short path; going
    /// through gives the full depth.
    ///
    /// Pure geometry — no Unity objects, so the arithmetic is checkable without a game.
    /// </summary>
    internal static class OrganZones
    {
        /// <summary>A zone as a slice of the box it lives in.</summary>
        internal struct Zone
        {
            public OrganZone Kind;

            /// <summary>Centre offset along the width axis, as a fraction of the box width.</summary>
            public float WidthOffset;

            /// <summary>Share of the box width the zone takes up.</summary>
            public float WidthShare;

            /// <summary>How it was recognised — goes in the journal so a misplaced zone is visible.</summary>
            public string Where;
        }

        /// <summary>Constants of the cavity and the zone severities.</summary>
        internal struct Tuning
        {
            /// <summary>Radial strength of tissue, MPa — the one constant the cavity radius rests on.</summary>
            public float TissueStrengthMPa;

            /// <summary>Severity of each zone against one lobe of lung, from the AIS squares.</summary>
            public float KHeart;

            public float KLiver;
            public float KSpine;

            /// <summary>Chance ceiling of traumatic cardiac arrest from a cavity that missed the heart.</summary>
            public float ArrestChance;

            /// <summary>Chance ceiling of avulsing the liver.</summary>
            public float AvulsionChance;

            /// <summary>Half the liver's span, mm — how big a cavity has to be to tear it off.</summary>
            public float LiverRadiusMm;

            /// <summary>Centre of the high-velocity sigmoid, m/s (the wound model's own).</summary>
            public float VelocityCenter;

            /// <summary>Width of that sigmoid, m/s.</summary>
            public float VelocityWidth;
        }

        /// <summary>What the channel did to the zone.</summary>
        internal struct Result
        {
            public OrganZone Kind;
            public string Where;

            /// <summary>Channel length inside the zone, mm.</summary>
            public float PathMm;

            /// <summary>What that had to beat, mm.</summary>
            public float NeedMm;

            /// <summary>Distance from the entry point to where the zone starts, mm.</summary>
            public float ToZoneMm;

            /// <summary>The depth criterion is met.</summary>
            public bool Through;

            /// <summary>And this zone dies of it.</summary>
            public bool Lethal;

            /// <summary>Radius of the temporary cavity around the channel, mm.</summary>
            public float TcRadiusMm;

            /// <summary>How close the channel came to the zone, mm — zero when it went through.</summary>
            public float DistanceMm;

            /// <summary>Share of the zone the cavity reaches into, 0..1.</summary>
            public float Overlap;

            /// <summary>Damage multiplier this zone earns.</summary>
            public float Multiplier;

            /// <summary>
            /// How much of the organ this meeting involved, 0..1 — the larger of what the
            /// channel crossed of its depth and what the cavity reached into it. What
            /// bleeding is scaled by: half an organ opened bleeds like half an organ.
            /// </summary>
            public float Involvement;

            public string Name => Kind.ToString().ToLowerInvariant();
        }

        /// <summary>
        /// Which zone a collider carries, if any. The spine colliders are asked their
        /// thickness first: SpineTop is also the whole upper back, half a metre across,
        /// and that box is not a spinal cord.
        /// </summary>
        public static bool TryFind(EBodyPartColliderType collider, in Anatomy.Frame frame,
            out Zone zone)
        {
            zone = default;
            if (!frame.Valid)
            {
                return false;
            }

            switch (collider)
            {
                case EBodyPartColliderType.RibcageUp:
                    zone = new Zone
                    {
                        Kind = OrganZone.Heart,
                        WidthOffset = 0f,
                        WidthShare = 1f / 3f,
                        Where = "RibcageUp mid third",
                    };
                    return true;

                case EBodyPartColliderType.RibcageLow:
                    zone = new Zone
                    {
                        Kind = OrganZone.Liver,
                        WidthOffset = 1f / 3f, // the body's own right
                        WidthShare = 1f / 3f,
                        Where = "RibcageLow right third",
                    };
                    return true;

                case EBodyPartColliderType.SpineTop:
                case EBodyPartColliderType.SpineDown:
                    if (!Anatomy.IsSpinePlate(frame))
                    {
                        return false; // the upper back, not the cord
                    }

                    zone = new Zone
                    {
                        Kind = OrganZone.Spine,
                        WidthOffset = 0f,
                        WidthShare = 1f,
                        Where = $"{collider} {frame.ThinnestMm:0} mm plate",
                    };
                    return true;

                default:
                    return false;
            }
        }

        /// <summary>
        /// Runs the channel through the zone. Returns false when the collider carries no
        /// zone at all; a channel that carries one but misses it comes back true with
        /// Through false — "the zone was not touched" and "there is no zone here" are
        /// different answers and the journal has to be able to tell them apart.
        /// </summary>
        /// <param name="entryWorld">Where the projectile entered this collider.</param>
        /// <param name="dirWorld">Direction of travel.</param>
        /// <param name="channelMm">How far the projectile gets in tissue from that entry.</param>
        /// <param name="dEdxJPerMm">Energy the projectile leaves per mm of that path.</param>
        /// <param name="zoneShiftMm">Where this body's organs sit against the rig's, mm
        /// along the width — one skeleton, many people.</param>
        public static bool TryHit(EBodyPartColliderType collider, in Anatomy.Frame frame,
            Vector3 entryWorld, Vector3 dirWorld, float channelMm, float dEdxJPerMm,
            in Tuning tuning, out Result result, float zoneShiftMm = 0f)
        {
            result = default;
            if (!TryFind(collider, frame, out var zone))
            {
                return false;
            }

            result.Kind = zone.Kind;
            result.Where = zone.Where;
            result.Multiplier = 1f;
            result.TcRadiusMm = CavityRadiusMm(dEdxJPerMm, tuning.TissueStrengthMPa);

            var origin = Anatomy.ToFrameMm(frame, entryWorld);
            var dir = Anatomy.DirInFrame(frame, dirWorld);

            // the zone is the same box narrowed along width and slid sideways; moving the
            // origin the other way is the same thing and keeps the box centred. The
            // per-shot shift rides along with it, so a hit at the edge of a zone becomes
            // a probability instead of a step at a box boundary.
            origin.x -= zone.WidthOffset * frame.WidthMm + zoneShiftMm;
            var half = new Vector3(frame.HalfMm.x * zone.WidthShare, frame.HalfMm.y,
                frame.HalfMm.z);

            var travelled = Mathf.Max(Mathf.Min(channelMm, Reach(origin, dir, half)), 0f);
            result.DistanceMm = Anatomy.SegmentToBox(origin, dir, travelled, half);

            // How far the cavity has to reach to have the whole zone inside it: the
            // narrowest way across it. Not the width — the spinal cord is 226 mm wide and
            // 17 mm thick, and it is the 17 that a cavity has to sweep to engulf it.
            var halfSpanMm = Mathf.Max(Mathf.Min(half.x, Mathf.Min(half.y, half.z)), 1f);
            result.Overlap = Mathf.Clamp01(
                (result.TcRadiusMm - result.DistanceMm) / halfSpanMm);

            if (!Anatomy.RayBox(origin, dir, half, out var tEnter, out var tExit))
            {
                result.NeedMm = Criterion(zone.Kind, frame, 0f);
                result.Multiplier = Reached(zone.Kind, tuning, result.Overlap);

                // the channel went past, so whatever of the organ was involved was
                // reached by the cavity and by nothing else
                result.Involvement = result.Overlap;
                return true;
            }

            var enter = Mathf.Max(tEnter, 0f);
            var span = Mathf.Max(tExit - enter, 0f); // what the geometry offers
            var path = Mathf.Max(Mathf.Min(tExit, channelMm) - enter, 0f); // what the bullet takes

            result.ToZoneMm = enter;
            result.PathMm = path;
            result.NeedMm = Criterion(zone.Kind, frame, span);

            // The spine is the exception the plate thickness buys us. There is no depth
            // to be halfway through: it is 13-17 mm of collider and anything that comes
            // out the far side of it has gone through the cord. A projectile that stops
            // inside has not.
            result.Through = zone.Kind == OrganZone.Spine
                ? span > 0f && path >= span - 0.01f
                : path > result.NeedMm;

            // The liver's outcome is not death by itself — avulsion is what the autopsy
            // series calls unsurvivable, and that is a roll, not a geometry test.
            result.Lethal = result.Through &&
                            (zone.Kind == OrganZone.Heart || zone.Kind == OrganZone.Spine);

            // A channel that went through the organ has already done the worst the
            // multiplier describes; one that only stretched it gets the share it reached.
            result.Multiplier = result.Lethal ? 1f
                : result.Through ? K(zone.Kind, tuning)
                : Reached(zone.Kind, tuning, result.Overlap);

            // The cord has no depth to be part-way through, so only the cavity says how
            // much of it was involved. For the others, a channel across half the organ's
            // depth involves half of it, and going through involves all of it.
            result.Involvement = zone.Kind == OrganZone.Spine
                ? result.Overlap
                : result.Through
                    ? 1f
                    : Mathf.Max(result.Overlap,
                        Mathf.Clamp01(result.PathMm / Mathf.Max(2f * result.NeedMm, 1f)));
            return true;
        }

        /// <summary>
        /// Radius of the temporary cavity, mm. The channel drives tissue radially until
        /// the work spent equals the energy given up over that length:
        /// dE/dx = pi r^2 sigma. One constant, tied to published cavity diameters —
        /// 7.62x51 comes out near 60 mm of radius, 9x19 near 30.
        /// </summary>
        public static float CavityRadiusMm(float dEdxJPerMm, float sigmaMPa)
        {
            if (dEdxJPerMm <= 0f || sigmaMPa <= 0f)
            {
                return 0f;
            }

            return Mathf.Sqrt(1000f * dEdxJPerMm / (Mathf.PI * sigmaMPa));
        }

        /// <summary>
        /// Traumatic cardiac arrest: a cavity that passed beside the heart rather than
        /// through it can still stop it. Needs rifle velocities — a pistol's cavity is
        /// small and slow, and the heart rides it out.
        /// </summary>
        public static float ArrestChance(in Result r, float velocity, in Tuning t)
        {
            if (r.Kind != OrganZone.Heart || r.Through || r.Overlap <= 0f)
            {
                return 0f;
            }

            return t.ArrestChance * r.Overlap * HighVelocity(velocity, t);
        }

        /// <summary>
        /// Avulsion: the ligaments and hepatic veins tearing, which is what the autopsy
        /// series lists as unsurvivable — not a through-and-through. That is damage by
        /// stretching, and the liver has the worst tolerance of it of any organ: dense,
        /// friable, and it does not give. So the chance comes from the cavity, not the
        /// channel.
        /// </summary>
        public static float AvulsionChance(in Result r, float velocity, in Tuning t)
        {
            if (r.Kind != OrganZone.Liver || !r.Through || t.LiverRadiusMm <= 0f)
            {
                return 0f;
            }

            return t.AvulsionChance * HighVelocity(velocity, t) *
                   Mathf.Min(1f, r.TcRadiusMm / t.LiverRadiusMm);
        }

        /// <summary>Fackler's high-velocity boundary: below it tissue absorbs the stretch elastically.</summary>
        public static float HighVelocity(float velocity, in Tuning t)
        {
            var width = Mathf.Max(t.VelocityWidth, 1f);
            return 1f / (1f + Mathf.Exp(-(velocity - t.VelocityCenter) / width));
        }

        private static float K(OrganZone kind, in Tuning t)
        {
            switch (kind)
            {
                case OrganZone.Heart: return t.KHeart;
                case OrganZone.Liver: return t.KLiver;
                case OrganZone.Spine: return t.KSpine;
                default: return 1f;
            }
        }

        private static float Reached(OrganZone kind, in Tuning t, float overlap)
        {
            return 1f + (K(kind, t) - 1f) * Mathf.Clamp01(overlap);
        }

        /// <summary>
        /// How far along the ray it is still worth measuring: past the far side of the
        /// zone the distance only grows, and a channel a metre long would otherwise drag
        /// the search out into the open.
        /// </summary>
        private static float Reach(Vector3 origin, Vector3 dir, Vector3 half)
        {
            var span = new Vector3(Mathf.Abs(origin.x) + half.x, Mathf.Abs(origin.y) + half.y,
                Mathf.Abs(origin.z) + half.z);
            return span.magnitude;
        }

        private static float Criterion(OrganZone kind, in Anatomy.Frame frame, float spanMm)
        {
            return kind == OrganZone.Spine ? spanMm : frame.DepthMm * 0.5f;
        }

        // --- Raid tally ---
        //
        // A mechanic that sometimes kills outright cannot be judged by feel, and the
        // per-hit lines are too many to count by hand. The design has a target for these
        // numbers — 35% of deaths at the moment of the hit against 52% over the minutes
        // that follow — and this is where it gets checked.

        private static readonly int[] Touched = new int[4];
        private static readonly int[] Through = new int[4];
        private static readonly int[] Lethal = new int[4];
        private static readonly int[] Multiplied = new int[4];
        private static readonly int[] Rolled = new int[4];
        private static readonly int[] RollsFired = new int[4];
        private static int _central;

        /// <summary>
        /// Counts organs, not collider boxes. One organ is several boxes — RibcageUp is
        /// two and RibcageLow is three — so the caller says which of these is the first
        /// time this shot did it, and a projectile crossing the same liver twice counts
        /// once.
        /// </summary>
        public static void Tally(OrganZone kind, bool touched, bool through, bool lethal)
        {
            var i = (int)kind;
            if (i <= 0 || i >= Touched.Length)
            {
                return;
            }

            if (touched)
            {
                Touched[i]++;
            }

            if (through)
            {
                Through[i]++;
            }

            if (lethal)
            {
                Lethal[i]++;
            }
        }

        /// <summary>A zone that raised the damage of a hit.</summary>
        public static void TallyMultiplier(OrganZone kind)
        {
            Bump(Multiplied, kind);
        }

        /// <summary>
        /// A roll made and how it came out. Both, always: a mechanic with a probability
        /// in it cannot be judged from the times it fired, and a rate is the only way to
        /// tell a chance that is too high from one that simply got asked a lot.
        /// </summary>
        public static void TallyRoll(OrganZone kind, bool fired)
        {
            Bump(Rolled, kind);
            if (fired)
            {
                Bump(RollsFired, kind);
            }
        }

        /// <summary>A lethal zone outside the chest and head pools that had to reach the chest.</summary>
        public static void TallyCentral()
        {
            _central++;
        }

        private static void Bump(int[] counter, OrganZone kind)
        {
            var i = (int)kind;
            if (i > 0 && i < counter.Length)
            {
                counter[i]++;
            }
        }

        public static System.Collections.Generic.IEnumerable<string> Report()
        {
            yield return "-- organ zones      entered   deep  fatal   xN   rolls (fired)";
            for (var i = 1; i < Touched.Length; i++)
            {
                yield return $"  {((OrganZone)i).ToString().ToLowerInvariant(),-16}" +
                             $"{Touched[i],5} {Through[i],6} {Lethal[i],6} {Multiplied[i],4} " +
                             $"{Rolled[i],7} ({RollsFired[i]})";
            }

            yield return $"  central damage dealt to the chest: {_central}";
        }

        public static void ResetTally()
        {
            for (var i = 0; i < Touched.Length; i++)
            {
                Touched[i] = 0;
                Through[i] = 0;
                Lethal[i] = 0;
                Multiplied[i] = 0;
                Rolled[i] = 0;
                RollsFired[i] = 0;
            }

            _central = 0;
        }
    }
}
