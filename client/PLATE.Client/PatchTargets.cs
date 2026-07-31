using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;

namespace PLATE.Client
{
    /// <summary>
    /// Single registry of all patch targets: remapped names change between SPT
    /// versions, so they are fixed in one place. Every target resolves lazily and is
    /// logged by the startup self-test: name drift is visible right when the game
    /// loads, not on the first shot. Names come from reversing the 0.16.9 assemblies.
    /// </summary>
    public static class PatchTargets
    {
        // --- Ballistics ---
        public static Type BallisticsCalculator => FindType("EFT.Ballistics.BallisticsCalculator");
        public static Type EftBulletClass => FindType("EftBulletClass");
        public static Type BodyPartCollider => FindType("BodyPartCollider");
        public static Type ArmorComponent => FindType("EFT.InventoryLogic.ArmorComponent");
        public static Type ArmorResistanceStruct => FindType("ArmorResistanceStruct");
        public static Type DamageInfoStruct => FindType("DamageInfoStruct");
        public static Type AmmoItemClass => FindType("AmmoItemClass");

        /// <summary>Body overpenetration: spawns a "child" bullet with damage × k.</summary>
        public static MethodBase Bullet_Overpenetrate => Method(EftBulletClass, "method_24");
        /// <summary>Fragmentation inside the body.</summary>
        public static MethodBase Bullet_Fragment => Method(EftBulletClass, "method_22");
        /// <summary>The "should it fragment" roll.</summary>
        public static MethodBase Bullet_ShouldFragment => Method(EftBulletClass, "method_10");
        /// <summary>Damage/PenPower degradation from speed loss on a hit.</summary>
        public static MethodBase Bullet_DegradeOnHit => Method(EftBulletClass, "method_4");
        /// <summary>Deterministic body overpenetration check.</summary>
        public static MethodBase BodyPart_IsPenetrated => Method(BodyPartCollider, "IsPenetrated");
        /// <summary>Armor penetration roll.</summary>
        public static MethodBase Armor_SetPenetrationStatus => Method(ArmorComponent, "SetPenetrationStatus");
        /// <summary>Armor damage cut + blunt (behind-armor trauma hook).</summary>
        public static MethodBase Armor_ApplyDamage => Method(ArmorComponent, "ApplyDamage");
        /// <summary>Penetration chance curve.</summary>
        public static MethodBase Armor_GetPenetrationChance => Method(ArmorResistanceStruct, "GetPenetrationChance");

        /// <summary>DamageInfo constructor from a bullet — the energy-transfer hook for the body part.</summary>
        public static MethodBase DamageInfo_CtorFromShot =>
            DamageInfoStruct == null || EftBulletClass == null
                ? null
                : AccessTools.Constructor(DamageInfoStruct,
                    new[] { FindType("EFT.EDamageType"), EftBulletClass });

        // --- Grenades ---
        /// <summary>Static explosion helper: gathers targets with a sphere and creates fragments.</summary>
        public static Type GrenadeExplosionHelper => FindType("GClass2085");

        /// <summary>Explosion: MaxExplosionDistance is a hard cap on fragment spread (transpiler).
        /// Blast/concussion are computed in a separate method with its own radius read — left alone.</summary>
        public static MethodBase Grenade_Explosion => Method(GrenadeExplosionHelper, "Explosion");

        // --- Health ---
        public static Type ActiveHealthController => FindType("EFT.HealthSystem.ActiveHealthController");
        public static Type EffectBase => FindType("EFT.HealthSystem.ActiveHealthController+GClass3008");
        public static Type BleedingBase => FindType("EFT.HealthSystem.ActiveHealthController+Bleeding");
        public static Type LightBleeding => FindType("EFT.HealthSystem.ActiveHealthController+LightBleeding");
        public static Type HeavyBleeding => FindType("EFT.HealthSystem.ActiveHealthController+HeavyBleeding");
        public static Type WoundEffect => FindType("EFT.HealthSystem.ActiveHealthController+Wound");
        public static Type TremorEffect => FindType("EFT.HealthSystem.ActiveHealthController+Tremor");
        public static Type TunnelVisionEffect => FindType("EFT.HealthSystem.ActiveHealthController+TunnelVision");
        public static Type LowEdgeHealthEffect => FindType("EFT.HealthSystem.ActiveHealthController+LowEdgeHealth");
        public static Type PainEffect => FindType("EFT.HealthSystem.ActiveHealthController+Pain");
        public static Type FractureEffect => FindType("EFT.HealthSystem.ActiveHealthController+Fracture");

        /// <summary>Finds an active effect by type and body part (generic, declared on the GClass3009 base).
        /// Cached — called from runtime polling (fractures, once per second per bot).</summary>
        public static MethodInfo Health_FindActiveEffect =>
            _healthFindActiveEffect ??= ActiveHealthController == null
                ? null
                : AccessTools.Method(ActiveHealthController, "FindActiveEffect");

        private static MethodInfo _healthFindActiveEffect;

        /// <summary>LowEdgeHealth tick (self-removal by total HP — muted while the blood system holds it).</summary>
        public static MethodBase LowEdge_RegularUpdate => Method(LowEdgeHealthEffect, "RegularUpdate");

        /// <summary>Removal of any effect (push signal: a splint removed Fracture etc.).</summary>
        public static MethodBase EffectBase_Removed => Method(EffectBase, "Removed");

        /// <summary>Surgery: restores a destroyed part (push signal for cripple removal).</summary>
        public static MethodBase Health_RestoreBodyPart => Method(ActiveHealthController, "RestoreBodyPart");
        public static MethodBase Health_FullRestoreBodyPart => Method(ActiveHealthController, "FullRestoreBodyPart");

        /// <summary>Generic effect-adding method (protected effects go through MakeGenericMethod).</summary>
        public static MethodInfo Health_AddEffect =>
            ActiveHealthController == null ? null : AccessTools.Method(ActiveHealthController, "AddEffect");

        /// <summary>Medicine application (the transfusion item hook).</summary>
        public static MethodBase Health_DoMedEffect => Method(ActiveHealthController, "DoMedEffect");

        /// <summary>Med applicability gate (inherited from the GClass3009 generic base).
        /// Patched so the blood bag (MedKit class) is applicable without lost HP.</summary>
        public static MethodBase Health_CanApplyItem => Method(ActiveHealthController, "CanApplyItem");

        /// <summary>Out-of-raid health controller (stash/character menu) — has ITS OWN
        /// ApplyItem override, a base-class patch does not catch it.</summary>
        public static Type OutOfRaidHealthController => FindType("HealthControllerClass");

        /// <summary>Item application by the LOCAL player (all UI paths: inventory,
        /// hotbar, dragging onto the health bar). DoMedEffect is called only by observed
        /// controllers (bots) — the local player needs these hooks: the in-raid
        /// overloads (inherited from the generic base) + the declared-only override out of raid.</summary>
        public static List<MethodBase> Health_ApplyItemOverloads
        {
            get
            {
                var list = new List<MethodBase>();
                if (ActiveHealthController != null)
                {
                    list.AddRange(ActiveHealthController.GetMethods(AccessTools.all)
                        .Where(m => m.Name == "ApplyItem"));
                }

                if (OutOfRaidHealthController != null)
                {
                    list.AddRange(OutOfRaidHealthController
                        .GetMethods(AccessTools.all | BindingFlags.DeclaredOnly)
                        .Where(m => m.Name == "ApplyItem"));
                }

                return list.Distinct().ToList();
            }
        }

        // --- UI (blood bar on the Health tab) ---
        public static Type HealthParametersPanel => FindType("EFT.UI.Health.HealthParametersPanel");
        public static MethodBase HealthPanel_Show => Method(HealthParametersPanel, "Show");

        /// <summary>Final body-part damage (the guaranteed-LightBleeding and overlay hook).</summary>
        public static MethodBase Health_ApplyDamage => Method(ActiveHealthController, "ApplyDamage");
        /// <summary>Bleeding tick (redirects HP damage into blood drain).</summary>
        public static MethodBase Bleeding_RegularUpdate => Method(BleedingBase, "RegularUpdate");
        public static MethodBase Health_DoBleed => AccessTools.Method(ActiveHealthController, "DoBleed", new[] { typeof(bool), FindType("EBodyPart") });
        public static MethodBase Health_Kill => Method(ActiveHealthController, "Kill");
        public static MethodBase Health_DestroyBodyPart => Method(ActiveHealthController, "DestroyBodyPart");
        public static MethodBase Health_DoFracture => Method(ActiveHealthController, "DoFracture");

        // --- Self-test ---

        private static readonly Dictionary<string, Func<object>> All = new Dictionary<string, Func<object>>
        {
            { nameof(BallisticsCalculator), () => BallisticsCalculator },
            { nameof(EftBulletClass), () => EftBulletClass },
            { nameof(BodyPartCollider), () => BodyPartCollider },
            { nameof(ArmorComponent), () => ArmorComponent },
            { nameof(ArmorResistanceStruct), () => ArmorResistanceStruct },
            { nameof(DamageInfoStruct), () => DamageInfoStruct },
            { nameof(AmmoItemClass), () => AmmoItemClass },
            { nameof(Bullet_Overpenetrate), () => Bullet_Overpenetrate },
            { nameof(Bullet_Fragment), () => Bullet_Fragment },
            { nameof(Bullet_ShouldFragment), () => Bullet_ShouldFragment },
            { nameof(Bullet_DegradeOnHit), () => Bullet_DegradeOnHit },
            { nameof(BodyPart_IsPenetrated), () => BodyPart_IsPenetrated },
            { nameof(Armor_SetPenetrationStatus), () => Armor_SetPenetrationStatus },
            { nameof(Armor_ApplyDamage), () => Armor_ApplyDamage },
            { nameof(Armor_GetPenetrationChance), () => Armor_GetPenetrationChance },
            { nameof(DamageInfo_CtorFromShot), () => DamageInfo_CtorFromShot },
            { nameof(ActiveHealthController), () => ActiveHealthController },
            { nameof(EffectBase), () => EffectBase },
            { nameof(BleedingBase), () => BleedingBase },
            { nameof(LightBleeding), () => LightBleeding },
            { nameof(HeavyBleeding), () => HeavyBleeding },
            { nameof(WoundEffect), () => WoundEffect },
            { nameof(TremorEffect), () => TremorEffect },
            { nameof(TunnelVisionEffect), () => TunnelVisionEffect },
            { nameof(LowEdgeHealthEffect), () => LowEdgeHealthEffect },
            { nameof(LowEdge_RegularUpdate), () => LowEdge_RegularUpdate },
            { nameof(PainEffect), () => PainEffect },
            { nameof(FractureEffect), () => FractureEffect },
            { nameof(Health_FindActiveEffect), () => Health_FindActiveEffect },
            { nameof(EffectBase_Removed), () => EffectBase_Removed },
            { nameof(Health_RestoreBodyPart), () => Health_RestoreBodyPart },
            { nameof(Health_FullRestoreBodyPart), () => Health_FullRestoreBodyPart },
            { nameof(Health_AddEffect), () => Health_AddEffect },
            { nameof(Health_DoMedEffect), () => Health_DoMedEffect },
            { nameof(HealthParametersPanel), () => HealthParametersPanel },
            { nameof(HealthPanel_Show), () => HealthPanel_Show },
            { nameof(Health_ApplyDamage), () => Health_ApplyDamage },
            { nameof(Bleeding_RegularUpdate), () => Bleeding_RegularUpdate },
            { nameof(Health_DoBleed), () => Health_DoBleed },
            { nameof(Health_Kill), () => Health_Kill },
            { nameof(Health_DestroyBodyPart), () => Health_DestroyBodyPart },
            { nameof(Health_DoFracture), () => Health_DoFracture },
            { nameof(GrenadeExplosionHelper), () => GrenadeExplosionHelper },
            { nameof(Grenade_Explosion), () => Grenade_Explosion },
            { nameof(Health_CanApplyItem), () => Health_CanApplyItem },
            { nameof(Health_ApplyItemOverloads), () =>
                Health_ApplyItemOverloads.Count > 0 ? Health_ApplyItemOverloads : null },
        };

        /// <summary>Resolves all targets, returns the list of unresolved ones (empty = all good).</summary>
        public static List<string> SelfTest()
        {
            var failed = new List<string>();
            foreach (var kv in All)
            {
                try
                {
                    if (kv.Value() == null)
                    {
                        failed.Add(kv.Key);
                    }
                }
                catch (Exception)
                {
                    failed.Add(kv.Key);
                }
            }

            return failed;
        }

        /// <summary>
        /// CRITICAL: AccessTools.TypeByName scans ALL game types (~65 ms) on every
        /// call — caching is mandatory (misses included). Incident: fracture polling
        /// through uncached properties cost 130 ms per bot per second = a slideshow.
        /// </summary>
        private static readonly Dictionary<string, Type> TypeCache = new Dictionary<string, Type>();

        private static Type FindType(string fullName)
        {
            if (!TypeCache.TryGetValue(fullName, out var t))
            {
                t = AccessTools.TypeByName(fullName);
                TypeCache[fullName] = t;
            }

            return t;
        }

        private static MethodBase Method(Type type, string name)
        {
            return type == null ? null : AccessTools.Method(type, name);
        }
    }
}
