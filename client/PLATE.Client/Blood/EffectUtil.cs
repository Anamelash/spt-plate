using System;
using System.Collections.Generic;
using System.Reflection;
using EFT.HealthSystem;

namespace PLATE.Client.Blood
{
    /// <summary>
    /// AddEffect for protected effects via generic reflection, with a cache of closed methods.
    /// </summary>
    internal static class EffectUtil
    {
        private static readonly Dictionary<Type, MethodInfo> Cache = new Dictionary<Type, MethodInfo>();

        public static void Add(ActiveHealthController ahc, Type effectType,
            EBodyPart bodyPart, float workTime, float strength)
        {
            Add(ahc, effectType, bodyPart, null, workTime, null, strength);
        }

        /// <summary>
        /// The full argument list, for effects that want the game's own defaults rather
        /// than a time and a strength of ours. A null is not zero here: it is "ask the
        /// effect class", which for a fracture is what decides that it lasts until it is
        /// splinted instead of expiring on a timer.
        /// </summary>
        public static void Add(ActiveHealthController ahc, Type effectType,
            EBodyPart bodyPart, float? delay, float? workTime, float? residue, float? strength)
        {
            if (ahc == null || effectType == null || PatchTargets.Health_AddEffect == null)
            {
                return;
            }

            if (!Cache.TryGetValue(effectType, out var closed))
            {
                closed = PatchTargets.Health_AddEffect.MakeGenericMethod(effectType);
                Cache[effectType] = closed;
            }

            closed.Invoke(ahc, new object[]
            {
                bodyPart, delay, workTime, residue, strength, null,
            });
        }
    }
}
