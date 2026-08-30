using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;

namespace PLATE.Client
{
    /// <summary>
    /// Single registry of all patch targets: the names the game ships change between SPT
    /// versions, so they are fixed in one place. Every target resolves lazily and is
    /// logged by the startup self-test: name drift is visible right when the game
    /// loads, not on the first shot.
    ///
    /// SPT 4.1 deobfuscated the client — the GClass/method_N names of 4.0 are gone and
    /// every target below is a real one. That makes the names readable but no more
    /// stable: they are still whatever SPT's prepatcher decided this release, which is
    /// exactly why they live here and not inline at the patch sites.
    /// </summary>
    public static class PatchTargets
    {
        // --- Ballistics ---
        public static Type BallisticsCalculator => FindType("EFT.Ballistics.BallisticsCalculator");
        public static Type Shot => FindType("EFT.Ballistics.Shot");
        public static Type BodyPartCollider => FindType("BodyPartCollider");

        /// <summary>Everything shootable in a scene that is not a person: walls, doors,
        /// sheet metal, glass. BodyPartCollider derives from it and overrides both of
        /// the virtuals below, so a patch here never reaches a body.</summary>
        public static Type BallisticCollider => FindType("EFT.Ballistics.BallisticCollider");
        public static Type ArmorComponent => FindType("EFT.InventoryLogic.ArmorComponent");
        public static Type ArmoredEquipment => FindType("EFT.InventoryLogic.ArmoredEquipment");
        public static Type ArmoredEquipmentTemplate =>
            FindType("EFT.InventoryLogic.ArmoredEquipmentTemplate");
        public static Type ArmorResistanceData => FindType("ArmorResistanceData");
        public static Type DamageInfo => FindType("EFT.Ballistics.DamageInfo");
        public static Type Ammo => FindType("EFT.InventoryLogic.Ammo");

        /// <summary>Body overpenetration: spawns a "child" bullet with damage × k
        /// (a deviated fragment — the child carries EBulletState.DeviationHit).</summary>
        public static MethodBase Bullet_Overpenetrate => Method(Shot, "CreateDeviatedFragment");
        /// <summary>Fragmentation inside the body.</summary>
        public static MethodBase Bullet_Fragment => Method(Shot, "CreateBulletFragments");
        /// <summary>The "should it fragment" roll.</summary>
        public static MethodBase Bullet_ShouldFragment => Method(Shot, "IsBulletFragmented");
        /// <summary>Damage/PenPower degradation from speed loss on a hit.</summary>
        public static MethodBase Bullet_DegradeOnHit => Method(Shot, "HandleCollision");
        /// <summary>Deterministic body overpenetration check.</summary>
        public static MethodBase BodyPart_IsPenetrated => Method(BodyPartCollider, "IsPenetrated");
        /// <summary>A bullet that bounced: the ricochet "child" (heavy armor and environment).</summary>
        public static MethodBase Bullet_Ricochet => Method(Shot, "CreateRicochetedFragment");

        /// <summary>
        /// Where every projectile in the game is born — the muzzle, an overpenetration,
        /// a ricochet, a fragment — and the only place before its trajectory table is
        /// built.
        ///
        /// Two separate things need exactly this moment. The table is formed inside from
        /// the `direction` and `speed` ARGUMENTS and then overwrites the projectile's
        /// position and velocity every tick, so anything the mod writes to a child
        /// afterwards is discarded on its first tick: the exit state of a barrier has to
        /// arrive here or not at all. And the shot object itself comes out of a pool of
        /// two hundred, carrying whatever the mod recorded against it in a previous life,
        /// which is where its per-projectile tables have to be cleared.
        ///
        /// The parameter NAMES are part of the contract (PatchIntegrityTests pins them):
        /// Harmony binds a prefix's arguments by name, so a rename in a future SPT would
        /// silently stop both fixes rather than fail loudly.
        /// </summary>
        public static MethodBase Bullet_Create => Method(Shot, "Create");
        /// <summary>The environment's penetration gate (threshold + roll in vanilla).</summary>
        public static MethodBase Obstacle_IsPenetrated => Method(BallisticCollider, "IsPenetrated");
        /// <summary>The environment's ricochet gate (one angle window for every surface
        /// in vanilla).</summary>
        public static MethodBase Obstacle_Deflects => Method(BallisticCollider, "Deflects");
        /// <summary>Armor penetration roll.</summary>
        public static MethodBase Armor_SetPenetrationStatus => Method(ArmorComponent, "SetPenetrationStatus");
        /// <summary>Armor damage cut + blunt (behind-armor trauma hook).</summary>
        public static MethodBase Armor_ApplyDamage => Method(ArmorComponent, "ApplyDamage");
        /// <summary>Penetration chance curve.</summary>
        public static MethodBase Armor_GetPenetrationChance => Method(ArmorResistanceData, "GetPenetrationChance");

        /// <summary>
        /// Base constructor for every armour item. EFT only creates Armor and Repairable
        /// components when armorClass is greater than zero, although class zero is a real
        /// anti-fragment rung in PLATE.
        /// </summary>
        public static MethodBase ArmoredEquipment_Ctor =>
            ArmoredEquipment == null || ArmoredEquipmentTemplate == null
                ? null
                : AccessTools.Constructor(ArmoredEquipment,
                    new[] { typeof(string), ArmoredEquipmentTemplate });

        /// <summary>DamageInfo constructor from a bullet — the energy-transfer hook for the body part.</summary>
        public static MethodBase DamageInfo_CtorFromShot =>
            DamageInfo == null || Shot == null
                ? null
                : AccessTools.Constructor(DamageInfo,
                    new[] { FindType("EFT.EDamageType"), Shot });

        // --- Grenades ---
        /// <summary>Static explosion helper: gathers targets with a sphere and creates fragments.</summary>
        public static Type GrenadeExplosionHelper => FindType("EFT.ExplosionSharedMethods");

        /// <summary>Explosion: MaxExplosionDistance is a hard cap on fragment spread (transpiler).
        /// Blast/concussion are computed in a separate method with its own radius read — left alone.</summary>
        public static MethodBase Grenade_Explosion => Method(GrenadeExplosionHelper, "Explosion");

        // --- Debug field tools (ghost mode, speed) ---
        public static Type BotsGroup => FindType("BotsGroup");
        public static Type BotHearingSensor => FindType("BotHearingSensor");
        public static Type MovementContext => FindType("EFT.MovementContext");

        /// <summary>The one funnel through which anyone becomes a bot group's enemy.</summary>
        public static MethodBase Bots_AddEnemy => Method(BotsGroup, "AddEnemy");
        /// <summary>Each bot's ear; vanilla's own mute flag is read-only, so this is
        /// where a ghosted player's sounds are dropped.</summary>
        public static MethodBase Bots_HearSound => Method(BotHearingSensor, "OnSoundPlayed");
        /// <summary>Where displacement is actually handed to the CharacterController.
        /// Virtual but overridden nowhere, so the base implementation is the one that
        /// runs (ClientPlayerMovementContext overrides only ApplyMotion above it).</summary>
        public static MethodBase Player_DirectApplyMotion => Method(MovementContext, "DirectApplyMotion");

        // --- Health ---
        public static Type ActiveHealthController => FindType("EFT.HealthSystem.ActiveHealthController");
        public static Type EffectBase => FindType("EFT.HealthSystem.ActiveHealthController+Effect");
        public static Type BleedingBase => FindType("EFT.HealthSystem.ActiveHealthController+Bleeding");
        public static Type LightBleeding => FindType("EFT.HealthSystem.ActiveHealthController+LightBleeding");
        public static Type HeavyBleeding => FindType("EFT.HealthSystem.ActiveHealthController+HeavyBleeding");
        public static Type WoundEffect => FindType("EFT.HealthSystem.ActiveHealthController+Wound");
        public static Type TremorEffect => FindType("EFT.HealthSystem.ActiveHealthController+Tremor");
        public static Type TunnelVisionEffect => FindType("EFT.HealthSystem.ActiveHealthController+TunnelVision");
        public static Type LowEdgeHealthEffect => FindType("EFT.HealthSystem.ActiveHealthController+LowEdgeHealth");
        public static Type PainEffect => FindType("EFT.HealthSystem.ActiveHealthController+Pain");
        public static Type FractureEffect => FindType("EFT.HealthSystem.ActiveHealthController+Fracture");

        /// <summary>Finds an active effect by type and body part (generic, declared on the BaseHealthController base).
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

        /// <summary>Med applicability gate (inherited from the BaseHealthController generic base).
        /// Patched so the blood bag (MedKit class) is applicable without lost HP.</summary>
        public static MethodBase Health_CanApplyItem => Method(ActiveHealthController, "CanApplyItem");

        /// <summary>Out-of-raid health controller (stash/character menu) — has ITS OWN
        /// ApplyItem override, a base-class patch does not catch it.</summary>
        public static Type OutOfRaidHealthController => FindType("EFT.HealthSystem.OfflineHealthController");

        /// <summary>Item application by the LOCAL player (all UI paths: inventory,
        /// hotbar, dragging onto the health bar). DoMedEffect is called only by observed
        /// controllers (bots), so the local player needs these hooks.
        ///
        /// ApplyItem is declared abstract on the generic base the health controllers
        /// derive from, and ActiveHealthController itself is abstract too: neither has
        /// a body, so neither can be patched. The bodies live in the concrete
        /// subclasses (in-raid own/observed controllers, out-of-raid controller), and
        /// those are what gets patched here — found by walking the type tree rather
        /// than by hardcoding remapped class names.</summary>
        public static List<MethodBase> Health_ApplyItemOverloads
        {
            get
            {
                var list = new List<MethodBase>();
                foreach (var type in ConcreteHealthControllers())
                {
                    list.AddRange(type
                        .GetMethods(AccessTools.all | BindingFlags.DeclaredOnly)
                        .Where(m => m.Name == "ApplyItem" && !m.IsAbstract)
                        .Cast<MethodBase>());
                }

                return list.Distinct().ToList();
            }
        }

        /// <summary>
        /// Every instantiable health controller: the subclasses of the abstract in-raid
        /// controller plus the out-of-raid one. Scanned once — the results are cached by
        /// the callers via the resolved target list.
        /// </summary>
        private static IEnumerable<Type> ConcreteHealthControllers()
        {
            var baseType = ActiveHealthController;
            if (baseType != null)
            {
                Type[] types;
                try
                {
                    types = baseType.Assembly.GetTypes();
                }
                catch (ReflectionTypeLoadException ex)
                {
                    types = ex.Types.Where(t => t != null).ToArray();
                }

                foreach (var t in types)
                {
                    if (t != null && !t.IsAbstract && baseType.IsAssignableFrom(t))
                    {
                        yield return t;
                    }
                }
            }

            if (OutOfRaidHealthController != null && !OutOfRaidHealthController.IsAbstract)
            {
                yield return OutOfRaidHealthController;
            }
        }

        // --- UI (blood bar on the Health tab) ---
        public static Type HealthParametersPanel => FindType("EFT.UI.Health.HealthParametersPanel");
        public static MethodBase HealthPanel_Show => Method(HealthParametersPanel, "Show");

        /// <summary>Final body-part damage (the guaranteed-LightBleeding and overlay hook).</summary>
        public static MethodBase Health_ApplyDamage => Method(ActiveHealthController, "ApplyDamage");

        /// <summary>
        /// Every HP change of a body part goes through here, and inside ApplyDamage it is
        /// called twice for two different things: once for the part that was hit, and once
        /// per surviving part when a destroyed part spills its excess damage over the rest.
        /// That makes it the one place the survivability overrides can hold a floor under
        /// a part or drop the spill (see SurvivabilityPatches).
        /// </summary>
        public static MethodBase Health_ChangeHealth => Method(ActiveHealthController, "ChangeHealth");
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
            { nameof(Shot), () => Shot },
            { nameof(BodyPartCollider), () => BodyPartCollider },
            { nameof(BallisticCollider), () => BallisticCollider },
            { nameof(ArmorComponent), () => ArmorComponent },
            { nameof(ArmoredEquipment), () => ArmoredEquipment },
            { nameof(ArmoredEquipmentTemplate), () => ArmoredEquipmentTemplate },
            { nameof(ArmorResistanceData), () => ArmorResistanceData },
            { nameof(DamageInfo), () => DamageInfo },
            { nameof(Ammo), () => Ammo },
            { nameof(Bullet_Overpenetrate), () => Bullet_Overpenetrate },
            { nameof(Bullet_Fragment), () => Bullet_Fragment },
            { nameof(Bullet_ShouldFragment), () => Bullet_ShouldFragment },
            { nameof(Bullet_DegradeOnHit), () => Bullet_DegradeOnHit },
            { nameof(BodyPart_IsPenetrated), () => BodyPart_IsPenetrated },
            { nameof(Bullet_Ricochet), () => Bullet_Ricochet },
            { nameof(Bullet_Create), () => Bullet_Create },
            { nameof(Obstacle_IsPenetrated), () => Obstacle_IsPenetrated },
            { nameof(Obstacle_Deflects), () => Obstacle_Deflects },
            { nameof(Armor_SetPenetrationStatus), () => Armor_SetPenetrationStatus },
            { nameof(Armor_ApplyDamage), () => Armor_ApplyDamage },
            { nameof(Armor_GetPenetrationChance), () => Armor_GetPenetrationChance },
            { nameof(ArmoredEquipment_Ctor), () => ArmoredEquipment_Ctor },
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
            { nameof(Health_ChangeHealth), () => Health_ChangeHealth },
            { nameof(Bleeding_RegularUpdate), () => Bleeding_RegularUpdate },
            { nameof(Health_DoBleed), () => Health_DoBleed },
            { nameof(Health_Kill), () => Health_Kill },
            { nameof(Health_DestroyBodyPart), () => Health_DestroyBodyPart },
            { nameof(Health_DoFracture), () => Health_DoFracture },
            { nameof(GrenadeExplosionHelper), () => GrenadeExplosionHelper },
            { nameof(Grenade_Explosion), () => Grenade_Explosion },
            { nameof(BotsGroup), () => BotsGroup },
            { nameof(BotHearingSensor), () => BotHearingSensor },
            { nameof(MovementContext), () => MovementContext },
            { nameof(Bots_AddEnemy), () => Bots_AddEnemy },
            { nameof(Bots_HearSound), () => Bots_HearSound },
            { nameof(Player_DirectApplyMotion), () => Player_DirectApplyMotion },
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
