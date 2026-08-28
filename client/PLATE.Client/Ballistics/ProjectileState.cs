using System.Runtime.CompilerServices;

namespace PLATE.Client.Ballistics
{
    /// <summary>
    /// The part of a projectile's state the engine has nowhere to put.
    ///
    /// Mass and calibre live on the shot itself and survive being written to. Two things
    /// do not. The deformable fraction X: the game has never heard of it, and the mod
    /// reads it per ammunition TEMPLATE out of the server's table, which is fine right up
    /// to the moment something deforms the bullet — a plate, or a wall — after which the
    /// template's answer is the wrong one for the rest of that projectile's flight. And
    /// the YAW a barrier left it turning by, which the game has no notion of at all: a
    /// projectile that came out of a wall sideways meets the next one broadside, and
    /// nothing on the shot could say so.
    ///
    /// One record per projectile, both fields in it. Two tables would be two lookups and
    /// two chances for the pair to disagree about which projectile they describe; each
    /// field is written by whoever knows it and read as absent until then.
    ///
    /// The armour model got away with a single frame-scoped slot because the projectile
    /// it hands on is consumed in the same frame: the post-plate child spawns inside the
    /// torso and lands on the next collider immediately. A bullet through a door flies
    /// for as long as it likes, so its state has to outlive the frame — and be found
    /// again by whatever that bullet spawns, which is what the parent walk in
    /// BallisticsPatches.EffectiveX is for.
    ///
    /// Keyed on the projectile object, weakly, exactly as ShotSpread keys its draw: the
    /// engine pools shots and this must not be what keeps one alive.
    /// </summary>
    internal static class ProjectileState
    {
        /// <summary>
        /// Nullable rather than a value plus a flag: "nobody has written this" is a real
        /// state and has to be distinguishable from a written zero, or a projectile whose
        /// yaw was recorded would read as having a deformable fraction of nought.
        /// </summary>
        private class Record
        {
            public float? X;
            public float? Yaw;

            /// <summary>Our own serial for this projectile — see <see cref="Serial"/>.</summary>
            public int? Serial;
        }

        private static int _nextSerial;

        private static readonly ConditionalWeakTable<object, Record> Table =
            new ConditionalWeakTable<object, Record>();

        private static readonly ConditionalWeakTable<object, Record>.CreateValueCallback
            NewRecord = _ => new Record();

        /// <summary>Records what a barrier left of this projectile's deformable fraction.</summary>
        public static void SetX(object projectile, float x)
        {
            if (projectile == null)
            {
                return;
            }

            Table.GetValue(projectile, NewRecord).X = x;
        }

        /// <summary>The recorded X, if a barrier has had this projectile.</summary>
        public static bool TryGetX(object projectile, out float x)
        {
            if (projectile != null && Table.TryGetValue(projectile, out var d) && d.X.HasValue)
            {
                x = d.X.Value;
                return true;
            }

            x = 0f;
            return false;
        }

        /// <summary>Records how far off nose-on a barrier left this projectile, 0..1.</summary>
        public static void SetYaw(object projectile, float yaw)
        {
            if (projectile == null)
            {
                return;
            }

            Table.GetValue(projectile, NewRecord).Yaw = yaw < 0f ? 0f : yaw > 1f ? 1f : yaw;
        }

        /// <summary>The recorded yaw, if a barrier has had this projectile.</summary>
        public static bool TryGetYaw(object projectile, out float yaw)
        {
            if (projectile != null && Table.TryGetValue(projectile, out var d) && d.Yaw.HasValue)
            {
                yaw = d.Yaw.Value;
                return true;
            }

            yaw = 0f;
            return false;
        }

        /// <summary>
        /// Drops everything recorded against this projectile object.
        ///
        /// The weak key protects against a leak; it does not protect against REUSE, and
        /// the engine reuses. Shots come out of a pool of two hundred, and what the pool
        /// returns to the world is the same managed object with new numbers on it — so a
        /// record keyed on that object is inherited by an unrelated bullet fired minutes
        /// later. Measured: once any projectile in a raid had been turned fully broadside
        /// by a couple of barriers, nine in ten of the shots logged afterwards entered
        /// their FIRST barrier already sideways, muzzle-fresh rounds at 950 m/s included.
        ///
        /// Stamping the records with an identity was the other candidate and is worse:
        /// the engine's own primary seed space is 512 values, so one shot in five hundred
        /// would validate a stranger's record — and it would have left ShotSpread, which
        /// has no such field, broken. Clearing at birth is exact.
        /// </summary>
        public static void Forget(object projectile)
        {
            if (projectile != null)
            {
                Table.Remove(projectile);
            }
        }

        /// <summary>
        /// A number that tells this projectile apart from every other one in the raid,
        /// for the journal to name it by.
        ///
        /// The engine has nothing usable. `RandomSeed` looks like an identity and is not
        /// one: a primary shot draws it from a range of 512, so the pellets of a single
        /// shotgun volley share it routinely — the journal showed one id three times in
        /// one shell, which quietly stitched one pellet's exit onto another pellet's
        /// entry and made a verification read as if speed had been lost. Widening the
        /// mask cannot help, because the narrowness is the engine's, not ours.
        ///
        /// Ours is assigned once, when the projectile is created — which is exactly when
        /// the pooled record is dropped, so it can never be the previous tenant's.
        /// Assigned to the CHAIN's root only: fragments and post-barrier children are
        /// meant to read the same number as the bullet they came from, and the journal
        /// walks up to the root to find it.
        /// </summary>
        public static int Serial(object projectile)
        {
            if (projectile == null)
            {
                return 0;
            }

            var record = Table.GetValue(projectile, NewRecord);
            if (!record.Serial.HasValue)
            {
                record.Serial = ++_nextSerial;
            }

            return record.Serial.Value;
        }
    }
}
