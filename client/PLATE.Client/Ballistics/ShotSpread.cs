using System.Runtime.CompilerServices;
using PLATE.Server.Services; // YawModel, compiled into both halves from one file
using UnityEngine;

namespace PLATE.Client.Ballistics
{
    /// <summary>
    /// Everything about a shot that is not the same twice, drawn once and kept.
    ///
    /// A multiplier of N(1, sigma) on the final damage would be noise, not a model. The
    /// spread has to come from where it comes from in reality, and then it correlates by
    /// itself: a projectile that turned early is terrible in a shallow wound and weaker
    /// in a through-and-through, late is the other way round, and one cartridge behaves
    /// differently in an arm and in a chest because the channel is a different length —
    /// not because a die was rolled.
    ///
    /// Drawn once per projectile, never per frame and never per body part. A shot that
    /// crosses two body parts that got two independent draws would have stopped being
    /// one shot. Overpenetration children and fragments inherit their parent's draw with
    /// the travelled distance taken off the neck, so a bullet that turned in an arm
    /// arrives at the chest already sideways.
    ///
    /// Armour is deliberately not in here. It has its own probability band around the
    /// ballistic limit, and mixing the two would leave the certification tests meaning
    /// nothing.
    /// </summary>
    internal class ShotSpread
    {
        /// <summary>Travel before the projectile goes broadside, mm.</summary>
        public float NeckMm;

        /// <summary>
        /// What the tissue along this channel is like against the calibrated average.
        /// Ribs, cartilage and diaphragm are not gelatin, and above 1 means a channel
        /// that runs further.
        /// </summary>
        public float TissueScale = 1f;

        /// <summary>Lateral shift of the organ zones for this shot, mm. One skeleton, many people.</summary>
        public float ZoneShiftMm;

        private System.Random _rng;

        /// <summary>
        /// What this shot has already done to each organ.
        ///
        /// One organ can be several colliders — RibcageUp is two boxes and RibcageLow is
        /// three — so a single projectile meets the same liver more than once, and each
        /// meeting is its own damage event. Everything that belongs to the ORGAN rather
        /// than to the event has to be remembered here, or a graze would count as a hit
        /// and then silence the run-through behind it.
        ///
        /// Shared by reference along a shot's whole chain, children of an
        /// overpenetration included: that is the same projectile in the same body.
        /// </summary>
        private struct ZoneMemory
        {
            public bool Touched;
            public bool Through;
            public bool Lethal;
            public bool Drawn;
            public float Roll;
            public float BleedMlSec;
        }

        private ZoneMemory[] _zones = new ZoneMemory[8];

        private bool Valid(int zone) => zone > 0 && zone < _zones.Length;

        /// <summary>First time this shot's channel entered this organ at all.</summary>
        public bool FirstTouch(int zone)
        {
            if (!Valid(zone) || _zones[zone].Touched)
            {
                return false;
            }

            _zones[zone].Touched = true;
            return true;
        }

        /// <summary>First time it went deep enough into it to matter.</summary>
        public bool FirstThrough(int zone)
        {
            if (!Valid(zone) || _zones[zone].Through)
            {
                return false;
            }

            _zones[zone].Through = true;
            return true;
        }

        /// <summary>First time that turned out to be fatal.</summary>
        public bool FirstLethal(int zone)
        {
            if (!Valid(zone) || _zones[zone].Lethal)
            {
                return false;
            }

            _zones[zone].Lethal = true;
            return true;
        }

        /// <summary>
        /// This shot's single draw for this organ: made on first need and returned
        /// unchanged afterwards.
        ///
        /// Keeping the number rather than the verdict is the whole point. Testing the
        /// same u against a, and later against b, gives P(u &lt; max(a,b)) — one roll, at
        /// the best chance the shot ever had. Remembering the verdict instead would let
        /// a glancing pass at the edge of the heart use up the roll at 1% and silence the
        /// crossing behind it at 13%.
        /// </summary>
        public float RollFor(int zone, out bool fresh)
        {
            fresh = false;
            if (!Valid(zone))
            {
                return 1f; // no memory to hang it on: never fires
            }

            if (!_zones[zone].Drawn)
            {
                _zones[zone].Drawn = true;
                _zones[zone].Roll = (float)(_rng ?? (_rng = NewRng())).NextDouble();
                fresh = true;
            }

            return _zones[zone].Roll;
        }

        /// <summary>
        /// How much bleeding this meeting opens beyond what the shot has already opened
        /// in the same organ, ml/s. One liver bleeds at one rate however many boxes the
        /// game cuts it into, and a graze followed by a run-through has to end at the
        /// run-through's rate rather than at their sum.
        /// </summary>
        public float BleedTopUp(int zone, float mlSec)
        {
            if (!Valid(zone) || mlSec <= _zones[zone].BleedMlSec)
            {
                return 0f;
            }

            var extra = mlSec - _zones[zone].BleedMlSec;
            _zones[zone].BleedMlSec = mlSec;
            return extra;
        }

        private static int _seeds = System.Environment.TickCount;

        /// <summary>
        /// Seeds from a managed source. UnityEngine.Random is a native call that does not
        /// exist outside the game, so a draw built on it could never be tested; and
        /// consecutive seeds hand System.Random near-identical opening draws, so the
        /// counter is scattered before it is used. A burst of shots must not walk its
        /// neck lengths steadily upwards.
        /// </summary>
        private static System.Random NewRng()
        {
            var n = (uint)System.Threading.Interlocked.Increment(ref _seeds);
            return new System.Random(unchecked((int)(n * 2654435761u ^ (n >> 15))));
        }

        // Keyed on the projectile itself rather than on a frame: the frame is not the
        // shot, and two people shot in the same frame are two shots.
        private static readonly ConditionalWeakTable<object, ShotSpread> Table =
            new ConditionalWeakTable<object, ShotSpread>();

        /// <summary>The draw for this projectile, made on first ask.</summary>
        public static ShotSpread For(object projectile, float diaMm,
            AmmoDataCache.WoundParams wound)
        {
            if (projectile == null)
            {
                return Fallback;
            }

            if (Table.TryGetValue(projectile, out var existing))
            {
                return existing;
            }

            var rng = NewRng();
            var fresh = new ShotSpread
            {
                _rng = rng,
                NeckMm = SampleNeck(diaMm, wound, rng),
                TissueScale = SampleTissue(rng),
                ZoneShiftMm = SampleZoneShift(rng),
            };
            Table.Add(projectile, fresh);
            return fresh;
        }

        /// <summary>
        /// The child of an overpenetration or a fragmentation carries the parent's draw
        /// forward, minus the tissue it has already crossed. Same shot, same body, and a
        /// projectile does not un-turn.
        /// </summary>
        public static void Inherit(object parent, object child, float consumedMm)
        {
            if (parent == null || child == null || !Table.TryGetValue(parent, out var from))
            {
                return;
            }

            var carried = new ShotSpread
            {
                _rng = from._rng,

                // by reference, not copied: what the shot has already done to an organ
                // travels with it. A child that got a fresh set of slots would roll the
                // same heart a second time, which is the one thing the draw is for.
                _zones = from._zones,

                NeckMm = Mathf.Max(from.NeckMm - Mathf.Max(consumedMm, 0f), 0f),
                TissueScale = from.TissueScale,
                ZoneShiftMm = from.ZoneShiftMm,
            };

            Table.Remove(child);
            Table.Add(child, carried);
        }

        /// <summary>A projectile with no identity to hang a draw on — the median shot.</summary>
        private static readonly ShotSpread Fallback = new ShotSpread
        {
            NeckMm = float.MaxValue,
            TissueScale = 1f,
            ZoneShiftMm = 0f,
        };

        private static float SampleNeck(float diaMm, AmmoDataCache.WoundParams wound,
            System.Random rng)
        {
            var median = (float)YawModel.MedianNeckMm(diaMm, wound?.YawNeckCalibres ?? 20);
            var sigma = PlateClientConfig.YawSpreadSigma.Value;
            return sigma <= 0f ? median : LogNormal(median, sigma, rng);
        }

        private static float SampleTissue(System.Random rng)
        {
            var sigma = PlateClientConfig.TissueSpread.Value;
            return sigma <= 0f ? 1f : Mathf.Clamp(1f + Normal(sigma, rng), 0.5f, 1.5f);
        }

        private static float SampleZoneShift(System.Random rng)
        {
            var sigma = PlateClientConfig.ZoneShiftMm.Value;
            return sigma <= 0f ? 0f : Normal(sigma, rng);
        }

        /// <summary>
        /// Where the turn happens, log-normal about the cartridge's median. Log-normal
        /// rather than normal because the neck cannot be negative and its published
        /// spread is multiplicative — one cartridge's neck varies twofold in gelatin.
        /// </summary>
        public static float LogNormal(float median, float sigma, System.Random rng)
        {
            return median * Mathf.Exp(Normal(sigma, rng));
        }

        /// <summary>Zero-mean normal draw of the given standard deviation, Box-Muller.</summary>
        public static float Normal(float sigma, System.Random rng)
        {
            var u1 = Mathf.Max((float)rng.NextDouble(), 1e-6f);
            var u2 = (float)rng.NextDouble();
            return sigma * Mathf.Sqrt(-2f * Mathf.Log(u1)) * Mathf.Cos(2f * Mathf.PI * u2);
        }
    }
}
