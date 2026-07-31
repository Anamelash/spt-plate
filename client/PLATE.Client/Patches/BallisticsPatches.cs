using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using EFT;
using EFT.InventoryLogic;
using HarmonyLib;
using PLATE.Client.Ballistics;
using UnityEngine;

namespace PLATE.Client.Patches
{
    /// <summary>
    /// Energy-based damage transfer model plus fixes for vanilla damage zeroing.
    ///
    /// 1. DamageInfo built from a bullet: the body part receives a (1-F) share of the
    ///    damage on overpenetration (F is retention derived from expansiveness X), and
    ///    full damage when the bullet stops. Also cancels the vanilla AVOID zeroing.
    /// 2. ArmorComponent.ApplyDamage: on penetration damage = input * m(pen, class, wear)
    ///    instead of the vanilla "no mitigation or zero".
    /// 3. method_24: the overpenetration "child" carries the F share instead of the vanilla k.
    /// </summary>
    internal static class BallisticsPatches
    {
        public static void Apply(Harmony harmony)
        {
            PatchSafe(harmony, PatchTargets.DamageInfo_CtorFromShot, nameof(DamageInfoCtorPostfix));
            PatchSafe(harmony, PatchTargets.Armor_ApplyDamage, nameof(ArmorMitigationPostfix),
                prefixName: nameof(ArmorMitigationPrefix));
            PatchSafe(harmony, PatchTargets.Bullet_Overpenetrate, nameof(OverpenChildPostfix));
            PatchSafe(harmony, PatchTargets.Bullet_Fragment, nameof(FragmentBudgetPostfix));

            // absolute penetration derived from impact energy density
            PatchSafe(harmony, PatchTargets.Bullet_DegradeOnHit, nameof(AbsolutePenPostfix));

            // overpenetration is decided by physics (L > chord, stopped-by-bone), not PenetrationLevel
            if (PatchTargets.BodyPart_IsPenetrated != null)
            {
                harmony.Patch(PatchTargets.BodyPart_IsPenetrated,
                    prefix: new HarmonyMethod(typeof(BallisticsPatches),
                        nameof(IsPenetratedPrefix)));
            }
            else
            {
                Plugin.Log.LogError("[PLATE] Ballistics: IsPenetrated not resolved, " +
                                    "vanilla overpen rule stays");
            }

            // physical armor model (U threshold + projectile mutation);
            // fallback: GOST fragment gate + vanilla roll
            if (PatchTargets.Armor_SetPenetrationStatus != null)
            {
                harmony.Patch(PatchTargets.Armor_SetPenetrationStatus,
                    prefix: new HarmonyMethod(typeof(BallisticsPatches),
                        nameof(ArmorPenetrationPrefix)));
            }
            else
            {
                Plugin.Log.LogError("[PLATE] Ballistics: SetPenetrationStatus not resolved, " +
                                    "physical armor / fragment block skipped");
            }
        }

        /// <summary>
        /// Context of the current shot (energy/diameter/victim) — DamageInfo knows it,
        /// but ArmorComponent.ApplyDamage (same frame, same Player.ApplyShot stack) does not.
        /// </summary>
        private struct ShotContext
        {
            public float EnergyJ;
            public float DiameterMm;
            public Player Victim;
            public int Frame;
        }

        private static ShotContext _shotCtx;

        /// <summary>Bullet energy of the current frame (for the fracture roll in BloodPatches), -1 if none.</summary>
        internal static float ShotEnergyThisFrame =>
            _shotCtx.Frame == Time.frameCount ? _shotCtx.EnergyJ : -1f;

        private static void PatchSafe(Harmony harmony, MethodBase target, string postfixName,
            string prefixName = null)
        {
            if (target == null)
            {
                Plugin.Log.LogError($"[PLATE] Ballistics: target for {postfixName} not resolved, skipped");
                return;
            }

            try
            {
                harmony.Patch(target,
                    prefix: prefixName == null
                        ? null
                        : new HarmonyMethod(typeof(BallisticsPatches), prefixName),
                    postfix: new HarmonyMethod(typeof(BallisticsPatches), postfixName));
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"[PLATE] Ballistics: failed to patch {target.Name}: {ex.Message}");
            }
        }

        private static bool Off => !PlateClientConfig.BallisticsEnabled.Value;

        // one-time dump of hitbox geometry (checking whether they are plates or volumes)
        private static bool _collidersDumped;

        private static void DumpCollidersOnce(Player victim)
        {
            if (_collidersDumped || victim == null)
            {
                return;
            }

            _collidersDumped = true;
            try
            {
                var parts = victim.gameObject.GetComponentsInChildren<BodyPartCollider>();
                Plugin.Log.LogInfo($"[PLATE] Victim hitboxes ({parts.Length} total), " +
                                   "local sizes and world AABB:");
                foreach (var p in parts)
                {
                    var c = p.Collider;
                    string geom;
                    switch (c)
                    {
                        case BoxCollider b:
                            geom = $"Box {b.size.x:0.000}x{b.size.y:0.000}x{b.size.z:0.000}";
                            break;
                        case CapsuleCollider cap:
                            geom = $"Capsule r={cap.radius:0.000} h={cap.height:0.000} dir={cap.direction}";
                            break;
                        case SphereCollider s:
                            geom = $"Sphere r={s.radius:0.000}";
                            break;
                        default:
                            geom = c == null ? "NULL" : c.GetType().Name;
                            break;
                    }

                    var world = c != null ? c.bounds.size : Vector3.zero;
                    Plugin.Log.LogInfo(
                        $"  {p.BodyPartColliderType,-22} {geom,-40} " +
                        $"AABB {world.x:0.00}x{world.y:0.00}x{world.z:0.00} m");
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"[PLATE] Collider dump failed: {ex.Message}");
            }
        }

        /// <summary>Fragments lighter than this are not projectiles (their energy stays in the body part).</summary>
        private const float MinFragMassG = 0.3f;

        /// <summary>Share of damage the bullet keeps when passing through a body part.</summary>
        private static float Retention(EftBulletClass shot)
        {
            var x = (float)AmmoDataCache.GetX(shot.Ammo?.TemplateId);
            return Mathf.Lerp(PlateClientConfig.FleshRetentionAp.Value,
                PlateClientConfig.FleshRetentionHp.Value, x);
        }

        // --- 1. Energy transfer to the body part + cancelling the AVOID zeroing ---

        /// <summary>
        /// Source of truth for body-part damage: the absolute wound model
        /// W(m, d, v_impact, X, T_chord) — the template Damage value plays no part
        /// in the calculation. Priority.Last makes this the last writer of Damage
        /// relative to other mods. Fallback (model disabled / server without it):
        /// the legacy branches on top of the baked-in Damage.
        /// </summary>
        [HarmonyPriority(Priority.Last)]
        private static void DamageInfoCtorPostfix(ref DamageInfoStruct __instance,
            EDamageType damageType, EftBulletClass shot)
        {
            var isFragment = damageType == EDamageType.GrenadeFragment ||
                             damageType == EDamageType.Landmine;
            if (Off || (damageType != EDamageType.Bullet && !isFragment))
            {
                return;
            }

            try
            {
                if (!(shot.HittedBallisticCollider is BodyPartCollider bpc))
                {
                    return;
                }

                // context for BABT in ArmorComponent.ApplyDamage (same call stack)
                var v = shot.Vector3_1.magnitude;
                _shotCtx = new ShotContext
                {
                    EnergyJ = 0.5f * (shot.BulletMassGram / 1000f) * v * v,
                    DiameterMm = shot.BulletDiameterMilimeters,
                    Victim = bpc.Player as Player,
                    Frame = Time.frameCount,
                };

                DumpCollidersOnce(_shotCtx.Victim);

                var wound = AmmoDataCache.Wound;
                if (PlateClientConfig.PhysDamageModel.Value && wound is { Enabled: true })
                {
                    ApplyAbsoluteWound(ref __instance, shot, bpc, v, wound);
                    return;
                }

                // --- fallback: baked-in Damage + legacy correction branches ---

                if (isFragment)
                {
                    return; // fragments only need the context (BABT/fractures) — bullet branches don't apply
                }

                var overpen = shot.BulletState == EftBulletClass.EBulletState.DeviationHit &&
                              shot.IsForwardHit;
                var fragmented = shot.BulletState == EftBulletClass.EBulletState.FragmentationHit;

                if (overpen)
                {
                    // overpenetration: the part receives (1-F), the "child" carries the rest
                    __instance.Damage = shot.Damage * (1f - Retention(shot));
                }
                else if (fragmented && PlateClientConfig.FragRescale.Value)
                {
                    // fragmentation: fragments carry a share of the energy deeper,
                    // the part receives the remainder (vanilla gave full damage + fragment bonus)
                    __instance.Damage = shot.Damage * (1f - PlateClientConfig.FragEnergyShare.Value);
                }
                else if (shot.AvoidAdditionalDamage)
                {
                    __instance.Damage = shot.Damage; // cancel the vanilla AVOID zeroing
                }
            }
            catch (Exception ex)
            {
                LogError(nameof(DamageInfoCtorPostfix), ex);
            }
        }

        /// <summary>
        /// Absolute energy-deposition calculation: on a through-and-through hit
        /// (BulletState is already set by our IsPenetrated) deposit along the chord;
        /// stopped by bone / lodged / fragmented — the full channel.
        /// Also runs for armor-blocked hits: W becomes the "incoming" pre-armor damage
        /// (then either BABT or the penetration mitigation from the __state snapshot).
        /// </summary>
        private static void ApplyAbsoluteWound(ref DamageInfoStruct __instance,
            EftBulletClass shot, BodyPartCollider bpc, float v,
            AmmoDataCache.WoundParams wound)
        {
            var mass = shot.BulletMassGram;
            var dia = shot.BulletDiameterMilimeters;
            if (mass <= 0f || dia <= 0f)
            {
                return; // malformed modded template — leave it alone
            }

            var x = EffectiveX(shot); // accounts for armor-induced deformation in this hit
            var frag = (float)AmmoDataCache.GetFrag(shot.Ammo?.TemplateId);

            // overpenetration OR fragmentation: in both cases the energy keeps moving
            // beyond the chord (as a swarm of fragments) — the part receives the
            // deposition along the chord. Full deposition only when the bullet stops
            // (bone / lodged).
            var exits = shot.IsForwardHit &&
                        (shot.BulletState == EftBulletClass.EBulletState.DeviationHit ||
                         shot.BulletState == EftBulletClass.EBulletState.FragmentationHit);
            var chordMm = exits
                ? ChordMm(bpc, __instance.HitPoint, __instance.Direction, dia)
                : -1f; // stopped inside the part: full deposition over L

            var d = ClientWoundModel.Compute(mass, dia, v, x, frag, chordMm, wound);
            var vital = VitalMult(bpc.BodyPartColliderType);
            __instance.Damage = d.DamageHp * vital * PlateClientConfig.DamageScale.Value;

            Overlay.HitFeed.PushPanel(d.Contact
                ? $"  W {bpc.BodyPartType}: contact {v:0} m/s -> {__instance.Damage:0.#}"
                : $"  W {bpc.BodyPartColliderType}: {v:0} m/s, L {d.ChannelMm:0}" +
                  (exits ? $"/T {chordMm:0}" : "") +
                  $" mm, PC {d.Pc:0.#}+TC {d.Tc:0.#}" +
                  (vital > 1f ? $" x{vital:0.#}" : "") +
                  $" -> {__instance.Damage:0.#}" + (exits ? " (through)" : ""));
        }

        // --- Absolute penetration from impact energy density ---

        /// <summary>
        /// Postfix on method_4: after the vanilla degradation, PenPower is overwritten
        /// with an absolute value — template pen × the ratio of energy densities
        /// (E/A at impact vs the template's E0/A0). At the muzzle it equals the template
        /// value (the ammo card stays honest, the server's blend calibration is kept),
        /// with distance it falls as v²; fragments/children get their own value
        /// automatically (their mass and cross-section have already been split by the
        /// overpenetration/fragmentation code). Stateless: recomputed on every hit,
        /// multipliers do not accumulate along the chain. A slingshot-speed bullet
        /// penetrates nothing.
        /// </summary>
        private static void AbsolutePenPostfix(EftBulletClass __instance)
        {
            if (Off || !PlateClientConfig.PhysDamageModel.Value)
            {
                return;
            }

            var wound = AmmoDataCache.Wound;
            if (wound == null || !wound.Enabled)
            {
                return;
            }

            try
            {
                if (!__instance.IsForwardHit)
                {
                    return; // like vanilla: pen changes only on forward hits
                }

                if (!(__instance.Ammo?.Template is AmmoTemplate tpl))
                {
                    return;
                }

                var m0 = tpl.BulletMassGram;
                var d0 = tpl.BulletDiameterMilimeters;
                var v0 = tpl.InitialSpeed;
                float pen0 = tpl.PenetrationPower;
                var m = __instance.BulletMassGram;
                var d = __instance.BulletDiameterMilimeters;
                if (m0 <= 0f || d0 <= 0f || v0 <= 0f || pen0 <= 0f || m <= 0f || d <= 0f)
                {
                    return; // malformed template — keep vanilla degradation
                }

                var v = __instance.Vector3_1.magnitude;
                // energy density ∝ m·v²/d² (shared constants cancel in the ratio)
                var ratio = (m * v * v / (d * d)) / (m0 * v0 * v0 / (d0 * d0));
                __instance.PenetrationPower = pen0 * Mathf.Clamp(ratio, 0f, 1.2f);
            }
            catch (Exception ex)
            {
                LogError(nameof(AbsolutePenPostfix), ex);
            }
        }

        // --- Chord through the collider and the physical overpenetration decision ---

        // cache of the victim's hitboxes (29 per body, lives as long as the Player)
        private static readonly ConditionalWeakTable<Player, BodyPartCollider[]>
            _victimColliders = new ConditionalWeakTable<Player, BodyPartCollider[]>();

        private static BodyPartCollider[] GetVictimColliders(Player p)
        {
            return p == null
                ? null
                : _victimColliders.GetValue(p,
                    pl => pl.gameObject.GetComponentsInChildren<BodyPartCollider>());
        }

        /// <summary>
        /// Projectile path length inside the body part, mm. Some EFT hitboxes are thin
        /// surface plates (SpineTop 1.7 cm, SideChestUp 1.1 cm — measured in raid), so
        /// the chord of a single collider underestimates the path. We treat the body as
        /// solid between boundaries: from the actual entry point to the FARTHEST exit
        /// surface among all colliders of the same body part (entry through the lower
        /// chest plate → exit through the back plate = an honest ~24 cm). A tangential
        /// graze stays a graze: its exits are near the entry. If every raycast misses,
        /// it is a degenerate tangent — minimal chord (2 calibers). The table of typical
        /// thicknesses is used only when there is no collider at all.
        /// </summary>
        private static float ChordMm(BodyPartCollider bpc, Vector3 entry, Vector3 direction,
            float diaMm)
        {
            var minChord = diaMm * 2f;
            if (bpc.Collider == null)
            {
                return FallbackThicknessMm(bpc.BodyPartType);
            }

            var dir = direction.sqrMagnitude > 1e-6f ? direction.normalized : Vector3.forward;
            var all = GetVictimColliders(bpc.Player as Player);
            var tExitMax = -1f;

            if (all != null)
            {
                foreach (var part in all)
                {
                    if (part == null || part.BodyPartType != bpc.BodyPartType ||
                        part.Collider == null)
                    {
                        continue;
                    }

                    var col = part.Collider;
                    var dFar = (col.bounds.center - entry).magnitude +
                               col.bounds.extents.magnitude + 0.1f;
                    var back = new Ray(entry + dir * dFar, -dir);
                    if (col.Raycast(back, out var exitHit, dFar + 0.05f))
                    {
                        var tExit = dFar - exitHit.distance; // exit along the ray, measured from the entry
                        if (tExit > tExitMax)
                        {
                            tExitMax = tExit;
                        }
                    }
                }
            }
            else
            {
                // no Player (fragment dummies etc.) — chord from a single collider
                var col = bpc.Collider;
                var dFar = col.bounds.size.magnitude + 0.05f;
                var back = new Ray(entry + dir * dFar, -dir);
                if (col.Raycast(back, out var exitHit, dFar + 0.05f))
                {
                    tExitMax = dFar - exitHit.distance;
                }
            }

            return tExitMax > 0f ? Mathf.Max(tExitMax * 1000f, minChord) : minChord;
        }

        /// <summary>
        /// Tissue sensitivity of the zone: the volumetric model is calibrated for torso
        /// muscle; the brain is an order of magnitude more sensitive per mm³ destroyed
        /// (15 ml = death), the neck carries major vessels, the jaw is severe but not
        /// brain-level.
        /// </summary>
        private static float VitalMult(EBodyPartColliderType collider)
        {
            switch (collider)
            {
                case EBodyPartColliderType.Eyes:
                case EBodyPartColliderType.HeadCommon:
                case EBodyPartColliderType.ParietalHead:
                case EBodyPartColliderType.BackHead:
                case EBodyPartColliderType.Ears:
                    return PlateClientConfig.VitalBrainMult.Value;
                case EBodyPartColliderType.Jaw:
                    return PlateClientConfig.VitalJawMult.Value;
                case EBodyPartColliderType.NeckFront:
                case EBodyPartColliderType.NeckBack:
                    return PlateClientConfig.VitalNeckMult.Value;
                default:
                    return 1f;
            }
        }

        /// <summary>Typical part thicknesses (mm) — used only when no collider is present.</summary>
        private static float FallbackThicknessMm(EBodyPart part)
        {
            switch (part)
            {
                case EBodyPart.Head: return 140f;
                case EBodyPart.Chest:
                case EBodyPart.Stomach: return 350f;
                case EBodyPart.LeftArm:
                case EBodyPart.RightArm: return 90f;
                default: return 130f;
            }
        }

        // stopped-by-bone: one roll per hit, shared with the fracture roll
        // (BloodPatches.TryBoneFracture): bone -> the bullet stays in the part + fracture per the energy ramp
        private static int _boneFrame = -1;
        private static EBodyPartColliderType _boneCollider;
        private static bool _boneHit;

        /// <summary>Bone roll of this hit, if the overpenetration check has already made it.</summary>
        internal static bool TryGetBoneHit(EBodyPartColliderType collider, out bool boneHit)
        {
            if (_boneFrame == Time.frameCount && _boneCollider == collider)
            {
                boneHit = _boneHit;
                return true;
            }

            boneHit = false;
            return false;
        }

        /// <summary>
        /// Physical overpenetration decision instead of the vanilla
        /// penPower·CF > PenetrationLevel. Exit ⇔ L(v_impact) > T_chord and not
        /// stopped by bone. Armor block (BlockedBy) is kept as in vanilla.
        /// </summary>
        private static bool IsPenetratedPrefix(BodyPartCollider __instance,
            EftBulletClass shot, Vector3 hitPoint, ref bool __result)
        {
            if (Off || !PlateClientConfig.PhysDamageModel.Value)
            {
                return true;
            }

            var p = AmmoDataCache.Wound;
            if (p == null || !p.Enabled)
            {
                return true;
            }

            try
            {
                if (shot.BlockedBy.HasValue)
                {
                    __result = false; // stopped by armor — like vanilla
                    return false;
                }

                var mass = shot.BulletMassGram;
                var dia = shot.BulletDiameterMilimeters;
                if (mass <= 0f || dia <= 0f)
                {
                    return true; // malformed template — vanilla rule
                }

                var v = shot.Vector3_1.magnitude;
                var x = EffectiveX(shot);
                var l = ClientWoundModel.ChannelMm(mass, dia, v, x, p);
                var chord = ChordMm(__instance, hitPoint, shot.Vector3_1, dia);

                // bone: probability per collider (shared with fractures), stashed for BloodPatches
                _boneFrame = Time.frameCount;
                _boneCollider = __instance.BodyPartColliderType;
                _boneHit = UnityEngine.Random.value <
                           BloodPatches.BoneChance(__instance.BodyPartColliderType);

                __result = !_boneHit && l > chord;
                return false;
            }
            catch (Exception ex)
            {
                LogError(nameof(IsPenetratedPrefix), ex);
                return true;
            }
        }

        // --- Physical armor: projectile state modifier ---

        // X_out after armor-induced deformation: frame+projectile context (same hit
        // stack as ShotContext) — downstream wound/penetration code reads it via EffectiveX
        private static int _xOvrFrame = -1;
        private static EftBulletClass _xOvrShot;
        private static float _xOvrValue;

        /// <summary>Projectile X accounting for armor-induced deformation in this same hit.</summary>
        internal static float EffectiveX(EftBulletClass shot)
        {
            if (_xOvrFrame == Time.frameCount && ReferenceEquals(_xOvrShot, shot))
            {
                return _xOvrValue;
            }

            return (float)AmmoDataCache.GetX(shot?.Ammo?.TemplateId);
        }

        // --- Hit-location memory (local U_limit degradation) ---

        private struct ArmorHitMark
        {
            public EBodyPartColliderType Zone;
            public Vector3 LocalPos; // in the body-part bone's local space (the plate follows it)
        }

        private static readonly ConditionalWeakTable<ArmorComponent, List<ArmorHitMark>>
            _armorHits = new ConditionalWeakTable<ArmorComponent, List<ArmorHitMark>>();

        private const int MaxHitMemory = 64;

        /// <summary>Local U_limit multiplier from previous hits within the DArea radius.</summary>
        private static float LocalDegradation(ArmorComponent armor, BodyPartCollider bpc,
            Vector3 localPos, AmmoDataCache.ArmorMatProfile prof, float floor)
        {
            if (!PlateClientConfig.ArmorLocalDegradation.Value || prof.DAreaMm <= 0 ||
                !_armorHits.TryGetValue(armor, out var marks))
            {
                return 1f;
            }

            var r2 = (float)(prof.DAreaMm / 1000.0 * (prof.DAreaMm / 1000.0));
            var mult = 1f;
            foreach (var m in marks)
            {
                if (m.Zone == bpc.BodyPartColliderType &&
                    (m.LocalPos - localPos).sqrMagnitude <= r2)
                {
                    mult *= (float)prof.DegradeMult;
                }
            }

            return Mathf.Max(mult, floor);
        }

        private static void RecordArmorHit(ArmorComponent armor, BodyPartCollider bpc,
            Vector3 localPos)
        {
            if (!PlateClientConfig.ArmorLocalDegradation.Value)
            {
                return;
            }

            var marks = _armorHits.GetOrCreateValue(armor);
            if (marks.Count >= MaxHitMemory)
            {
                marks.RemoveAt(0);
            }

            marks.Add(new ArmorHitMark { Zone = bpc.BodyPartColliderType, LocalPos = localPos });
        }

        // --- Durability wear from absorbed energy (frame+armor context) ---

        private static int _absorbFrame = -1;
        private static readonly List<KeyValuePair<ArmorComponent, float>> _absorbed =
            new List<KeyValuePair<ArmorComponent, float>>(4);

        private static void RecordAbsorbedEnergy(ArmorComponent armor, float joules)
        {
            if (_absorbFrame != Time.frameCount)
            {
                _absorbed.Clear();
                _absorbFrame = Time.frameCount;
            }

            _absorbed.Add(new KeyValuePair<ArmorComponent, float>(armor, joules));
        }

        private static bool TryConsumeAbsorbedEnergy(ArmorComponent armor, out float joules)
        {
            joules = 0f;
            if (_absorbFrame != Time.frameCount)
            {
                return false;
            }

            for (var i = 0; i < _absorbed.Count; i++)
            {
                if (ReferenceEquals(_absorbed[i].Key, armor))
                {
                    joules = _absorbed[i].Value;
                    _absorbed.RemoveAt(i);
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Prefix on SetPenetrationStatus. With the physical armor model enabled — the
        /// U decision and projectile mutation (vanilla is always skipped); otherwise —
        /// the GOST fragment gate + vanilla.
        /// </summary>
        private static bool ArmorPenetrationPrefix(ArmorComponent __instance, EftBulletClass shot)
        {
            if (Off)
            {
                return true;
            }

            var armor = AmmoDataCache.Armor;
            if (PlateClientConfig.PhysDamageModel.Value && PlateClientConfig.PhysArmorModel.Value &&
                armor is { Enabled: true } && AmmoDataCache.Wound is { Enabled: true })
            {
                try
                {
                    return PhysicalArmorDecision(__instance, shot, armor);
                }
                catch (Exception ex)
                {
                    LogError(nameof(PhysicalArmorDecision), ex);
                    return true;
                }
            }

            return FragmentArmorBlockPrefix(__instance, shot);
        }

        /// <summary>
        /// Physical armor decision: U_hit = (E/A)·shape versus U_limit = class·material·
        /// wear·(1/cos angle). Below the band — block (BABT); above — penetration with a
        /// price: E_cost, deformation K_def (X grows), breakup K_frag (mass melts away).
        /// A weakened projectile enters the body — the wound model takes it from there.
        /// </summary>
        private static bool PhysicalArmorDecision(ArmorComponent armor, EftBulletClass shot,
            AmmoDataCache.ArmorParams cfg)
        {
            if (armor.Repairable.Durability <= 0f)
            {
                return false; // broken armor does not protect (like vanilla: no block)
            }

            var mass = shot.BulletMassGram;
            var dia = shot.BulletDiameterMilimeters;
            if (mass <= 0f || dia <= 0f)
            {
                return true; // malformed template — vanilla roll
            }

            var area = Mathf.PI * dia * dia / 4f;
            var v = shot.Vector3_1.magnitude;
            var e = 0.5f * (mass / 1000f) * v * v;
            var x = EffectiveX(shot);

            // uneven grenade fragmentation: a large fragment (base/fuze) carries a
            // multiple of the energy with 1/N chance — the GOST-gate mechanic ported to the U threshold
            var eForU = e;
            var name = shot.Ammo?.Template?.Name ?? "";
            if (name.StartsWith("shrapnel", StringComparison.OrdinalIgnoreCase))
            {
                var share = (float)AmmoDataCache.GetLargeShare(shot.Ammo?.TemplateId);
                if (share < 0f)
                {
                    share = PlateClientConfig.LargeFragShare.Value;
                }

                if (UnityEngine.Random.value < share)
                {
                    eForU *= PlateClientConfig.LargeFragEnergyMult.Value;
                }
            }

            var uHit = (eForU / area) *
                       (1f + (float)cfg.PenConstructionFactor * (0.5f - x));

            // threshold: class × material × wear × slanted (oblique) thickness
            var prof = cfg.Profile(armor.Template.ArmorMaterial.ToString());
            var duraShare = armor.Repairable.TemplateDurability > 0
                ? Mathf.Clamp01(armor.Repairable.Durability /
                                armor.Repairable.TemplateDurability)
                : 1f;
            var duraFactor = (float)cfg.DurabilityFloor +
                             (1f - (float)cfg.DurabilityFloor) * duraShare;
            var dir = shot.Vector3_1.sqrMagnitude > 1e-6f
                ? shot.Vector3_1.normalized
                : Vector3.forward;
            var cos = Mathf.Max(Mathf.Abs(Vector3.Dot(dir, shot.HitNormal.normalized)),
                (float)cfg.AngleMinCos);
            var uLimit = (float)(cfg.ClassULimit(armor.ArmorClass) * prof.ULimitMult) *
                         duraFactor / cos;

            // fibers (UHMWPE/aramid) get pushed apart by sharp-nosed projectiles — lower threshold for X<0.5
            if (prof.SharpVulnMult > 0)
            {
                uLimit *= 1f - (float)prof.SharpVulnMult * Mathf.Clamp01((0.5f - x) * 2f);
            }

            // local degradation — a hit near previous holes (ceramics: a shattered
            // tile segment); the current hit is recorded below
            var bpc = shot.HittedBallisticCollider as BodyPartCollider;
            var localPos = Vector3.zero;
            var localMult = 1f;
            if (bpc != null)
            {
                localPos = bpc.ColliderTransformCached != null
                    ? bpc.ColliderTransformCached.InverseTransformPoint(shot.RaycastHit_0.point)
                    : shot.RaycastHit_0.point;
                localMult = LocalDegradation(armor, bpc, localPos, prof, (float)cfg.DegradeFloor);
                uLimit *= localMult;
            }

            // probabilistic band around the threshold (the material is not uniform)
            var band = Mathf.Max((float)cfg.ThresholdBand, 0.001f);
            var ratio = uHit / Mathf.Max(uLimit, 1e-3f);
            var pierceChance = Mathf.Clamp01((ratio - (1f - band)) / (2f * band));
            var pierce = pierceChance > 0f && UnityEngine.Random.value < pierceChance;

            // energy price of penetration: work ∝ strength × area × thickness
            var eCost = (float)prof.ECostMult * uLimit * area;
            var eOut = e - eCost;

            if (!pierce || eOut < 1f)
            {
                shot.BlockedBy = armor.Item.Id; // block (or lodged in the soft pack) -> BABT
                if (bpc != null)
                {
                    RecordArmorHit(armor, bpc, localPos); // a blocked hit damages the zone too
                }

                RecordAbsorbedEnergy(armor, e); // all the energy goes into the armor
                Overlay.HitFeed.PushPanel(
                    $"  armor {armor.Template.ArmorMaterial} cl.{armor.ArmorClass}: " +
                    $"U {uHit:0.#}/{uLimit:0.#} J/mm²" +
                    (localMult < 1f ? $" (segment x{localMult:0.00})" : "") + " -> block");
                return false;
            }

            var kDefEff = (float)prof.KDef * x;            // soft bullets deform, a hard core holds up
            var kFragEff = (float)prof.KFrag * (1f - 0.5f * x); // a hard core crumbles more
            var mOut = mass * (1f - kFragEff);
            var xOut = Mathf.Min(1f, x + kDefEff);
            var vOut = Mathf.Sqrt(2f * eOut / (mOut / 1000f));

            shot.BulletMassGram = mOut;
            shot.Vector3_1 = dir * vOut;
            _xOvrFrame = Time.frameCount;
            _xOvrShot = shot;
            _xOvrValue = xOut;

            if (bpc != null)
            {
                RecordArmorHit(armor, bpc, localPos); // a hole weakens the zone
            }

            RecordAbsorbedEnergy(armor, eCost); // the armor absorbed the penetration work

            Overlay.HitFeed.PushPanel(
                $"  armor {armor.Template.ArmorMaterial} cl.{armor.ArmorClass}: " +
                $"U {uHit:0.#}/{uLimit:0.#}" +
                (localMult < 1f ? $" (segment x{localMult:0.00})" : "") +
                $" -> pierce, -{eCost:0} J, v {v:0}->{vOut:0}, X {x:0.00}->{xOut:0.00}");
            return false; // vanilla does not roll
        }

        // --- Fragments do not penetrate class 1+ armor (IRL: soft armor is anti-fragment armor) ---

        /// <summary>
        /// Prefix on the penetration roll for fragments (shrapnel* templates, including
        /// clones from GrenadePhysics): calibrated against GOST armor classes. If the
        /// fragment's energy AT IMPACT is below the class threshold (BR1 = 400 J = the
        /// 5.9 g @ 335 m/s test bullet +20% for shape, each class above is x1.45) — a
        /// forced block (BlockedBy, as the game itself does). Above the threshold — an
        /// honest vanilla roll: a large fragment near the epicenter can pierce BR1.
        /// The shrapnel BC of 0.013 bleeds the energy below the threshold by ~5 m —
        /// matching the GOST tests.
        /// </summary>
        private static bool FragmentArmorBlockPrefix(ArmorComponent __instance, EftBulletClass shot)
        {
            if (Off || !PlateClientConfig.FragmentsStoppedByArmor.Value)
            {
                return true;
            }

            try
            {
                if (__instance.ArmorClass < 1 || __instance.Repairable.Durability <= 0f)
                {
                    return true; // broken armor does not stop a fragment
                }

                var name = shot?.Ammo?.Template?.Name ?? "";
                if (!name.StartsWith("shrapnel", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }

                // impact energy: template mass, current speed (BC has already eaten the distance)
                var energyJ = 0.5f * (shot.BulletMassGram / 1000f) * shot.Vector3_1.sqrMagnitude;

                // uneven fragmentation: one base/fuze per FragmentsCount grenade
                // fragments (LargeShare from the server, 1/N) — only a large piece with
                // a multiple of the energy can step over the GOST threshold
                var share = (float)Ballistics.AmmoDataCache.GetLargeShare(shot.Ammo?.TemplateId);
                if (share < 0f)
                {
                    share = PlateClientConfig.LargeFragShare.Value; // the server did not report the share
                }

                if (UnityEngine.Random.value < share)
                {
                    energyJ *= PlateClientConfig.LargeFragEnergyMult.Value;
                }

                var threshold = PlateClientConfig.FragBlockEnergyJ.Value *
                                Mathf.Pow(PlateClientConfig.FragBlockClassFactor.Value,
                                    __instance.ArmorClass - 1);
                if (energyJ >= threshold)
                {
                    return true; // large energetic fragment — honest roll
                }

                shot.BlockedBy = __instance.Item.Id;
                return false;
            }
            catch (Exception ex)
            {
                LogError(nameof(FragmentArmorBlockPrefix), ex);
                return true;
            }
        }

        // --- 2. Damage mitigation on armor penetration (and removing the plate+overpen zeroing) ---

        private struct ArmorCallState
        {
            public float Damage;
            public float Durability;
        }

        private static void ArmorMitigationPrefix(ArmorComponent __instance,
            ref DamageInfoStruct damageInfo, out ArmorCallState __state)
        {
            __state = new ArmorCallState
            {
                Damage = damageInfo.Damage,
                Durability = __instance.Repairable.Durability,
            };
        }

        private static void ArmorMitigationPostfix(ArmorComponent __instance,
            ref DamageInfoStruct damageInfo, ArmorCallState __state)
        {
            var dt = damageInfo.DamageType;
            if (Off || (dt != EDamageType.Bullet && dt != EDamageType.GrenadeFragment &&
                        dt != EDamageType.Landmine))
            {
                return;
            }

            try
            {
                // physical armor: wear from absorbed energy (J per durability point,
                // per material); fallback — vanilla loss with material multipliers
                var armorData = AmmoDataCache.Armor;
                if (PlateClientConfig.PhysDamageModel.Value &&
                    PlateClientConfig.PhysArmorModel.Value &&
                    armorData is { Enabled: true } &&
                    TryConsumeAbsorbedEnergy(__instance, out var absorbedJ))
                {
                    var jPerDura = armorData
                        .Profile(__instance.Template.ArmorMaterial.ToString()).JPerDurability;
                    if (jPerDura > 0)
                    {
                        __instance.Repairable.Durability = Mathf.Max(0f,
                            __state.Durability - absorbedJ / (float)jPerDura);
                    }
                }
                else
                {
                    // wear per material: "gong" steel is not worn down by non-penetrating
                    // bullets, ceramics crumble from any hit
                    AdjustDurability(__instance, __state.Durability, damageInfo.BlockedBy.HasValue);
                }

                if (damageInfo.BlockedBy.HasValue)
                {
                    // no penetration: behind-armor blunt trauma per Sturdivan instead of vanilla blunt
                    ApplyBabt(__instance, ref damageInfo);
                    return;
                }

                if (PlateClientConfig.PhysDamageModel.Value && PlateClientConfig.PhysArmorModel.Value &&
                    AmmoDataCache.Armor is { Enabled: true } && AmmoDataCache.Wound is { Enabled: true })
                {
                    // the armor already took its price in energy/mass/deformation on
                    // penetration — W in DamageInfo was computed from the weakened
                    // projectile, no multiplier needed
                    return;
                }

                var duraShare = __instance.Repairable.TemplateDurability > 0
                    ? Mathf.Clamp01(__instance.Repairable.Durability /
                                    __instance.Repairable.TemplateDurability)
                    : 1f;
                var resist = __instance.ArmorClass * PlateClientConfig.ArmorResistPerClass.Value *
                             (PlateClientConfig.ArmorDurabilityFloor.Value +
                              (1f - PlateClientConfig.ArmorDurabilityFloor.Value) * duraShare);
                var pen = Mathf.Max((float)damageInfo.PenetrationPower, 1f);
                var k = PlateClientConfig.ArmorMitigationK.Value;
                var m = k <= 0f
                    ? 1f
                    : Mathf.Clamp(pen / (pen + k * resist),
                        PlateClientConfig.ArmorMitigationMin.Value, 1f);

                // __state.Damage is the pre-armor damage: this overwrites both the
                // vanilla "no mitigation" and the buggy zeroing on overpenetration
                damageInfo.Damage = __state.Damage * m;
            }
            catch (Exception ex)
            {
                LogError(nameof(ArmorMitigationPostfix), ex);
            }
        }

        /// <summary>Durability wear recalculation per material: loss_new = loss_vanilla * mult(material, outcome).</summary>
        private static void AdjustDurability(ArmorComponent armor, float durabilityBefore, bool blocked)
        {
            if (!PlateClientConfig.Materials.TryGetValue(armor.Template.ArmorMaterial, out var profile))
            {
                return;
            }

            var mult = blocked ? profile.DuraBlockMult.Value : profile.DuraPenMult.Value;
            if (Mathf.Approximately(mult, 1f))
            {
                return;
            }

            var loss = durabilityBefore - armor.Repairable.Durability;
            if (loss <= 0f)
            {
                return;
            }

            armor.Repairable.Durability = Mathf.Clamp(
                durabilityBefore - loss * mult, 0f, armor.Repairable.MaxDurability);
        }

        // --- Behind-armor blunt trauma (Sturdivan's Blunt Criterion) ---

        private static void ApplyBabt(ArmorComponent armor, ref DamageInfoStruct damageInfo)
        {
            if (!PlateClientConfig.BabtEnabled.Value)
            {
                return; // vanilla blunt
            }

            if (_shotCtx.Frame != Time.frameCount || _shotCtx.EnergyJ <= 0f)
            {
                return; // no shot context (not a bullet path) — leave it alone
            }

            // energy that reached the body through the armor panel
            var bfd = _shotCtx.EnergyJ * (float)armor.BluntThroughput *
                      PlateClientConfig.BabtEnergyScale.Value;

            // effective diameter = the material's spread area (steel distributes the
            // load across the whole plate + trauma pad, aramid deflects at a point),
            // but never below the caliber
            var spreadCm = PlateClientConfig.Materials.TryGetValue(
                armor.Template.ArmorMaterial, out var profile)
                ? profile.SpreadCm.Value
                : 4f;
            var dCm = Mathf.Max(_shotCtx.DiameterMm / 10f, spreadCm);
            var denom = Mathf.Pow(PlateClientConfig.BabtBodyMassKg.Value, 1f / 3f) *
                        PlateClientConfig.BabtWallCm.Value * dCm;
            var bc = Mathf.Log(Mathf.Max(bfd, 1f) / denom);

            var bc1 = PlateClientConfig.BabtBc1.Value;
            var bc2 = PlateClientConfig.BabtBc2.Value;

            float dmg;
            if (bc < bc1)
            {
                dmg = PlateClientConfig.BabtPlateauDamage.Value; // plateau: a bruise under the plate
            }
            else
            {
                var t = Mathf.Clamp01((bc - bc1) / Mathf.Max(bc2 - bc1, 0.01f));
                dmg = Mathf.Lerp(PlateClientConfig.BabtPlateauDamage.Value,
                    PlateClientConfig.BabtMaxDamage.Value, t);
            }

            damageInfo.Damage = dmg;

            ApplyBabtEffects(_shotCtx.Victim, bc, bc1, bc2);
            Overlay.HitFeed.PushPanel(
                $"  BABT {armor.Template.ArmorMaterial} bc={bc:0.00} bfd={bfd:0}J " +
                $"D={dCm:0.#}cm bt={armor.BluntThroughput:0.###} -> dmg {dmg:0.#}");
        }

        private static int _babtFxFrame;
        private static Player _babtFxVictim;

        private static void ApplyBabtEffects(Player victim, float bc, float bc1, float bc2)
        {
            var ahc = victim?.ActiveHealthController;
            if (ahc == null)
            {
                return;
            }

            // per-volley dedup: 8 blocked pellets in one frame = a single effects bundle
            // (the "bruise" damage still applies per pellet — that is the total contusion)
            if (Time.frameCount == _babtFxFrame && ReferenceEquals(victim, _babtFxVictim))
            {
                return;
            }

            _babtFxFrame = Time.frameCount;
            _babtFxVictim = victim;

            // always: pain + a short concussion ("something slammed into the plate")
            Blood.EffectUtil.Add(ahc, PatchTargets.PainEffect, EBodyPart.Chest, 12f, 1f);
            ahc.DoContusion(1.5f, bc < bc1 ? 0.5f : 1f);

            if (bc < bc1)
            {
                return; // plateau: painful but not lethal — no internal injuries
            }

            var t = Mathf.Clamp01((bc - bc1) / Mathf.Max(bc2 - bc1, 0.01f));

            // upper half of the band: tremor
            if (t > 0.5f)
            {
                Blood.EffectUtil.Add(ahc, PatchTargets.TremorEffect, EBodyPart.Head, 8f, 1f);
            }

            // internal bleeding: probability grows toward BC2 (100% there)
            if (PlateClientConfig.BloodEnabled.Value &&
                UnityEngine.Random.value < t &&
                PlateClientConfig.BabtInternalBleedRate.Value > 0f)
            {
                Blood.PlateBloodManager.AddInternal(victim, PlateClientConfig.BabtInternalBleedRate.Value);
            }

            // severe BABT: lung contusion — winded for a long time
            if (bc >= bc2)
            {
                ahc.AddStaminaZeroffect(20f);
            }
        }

        // --- Fragment energy budget (instead of the vanilla 0.5/MaxFragments) ---

        private static void FragmentBudgetPostfix(EftBulletClass __instance)
        {
            if (Off || !PlateClientConfig.FragRescale.Value)
            {
                return;
            }

            try
            {
                if (!(__instance.HittedBallisticCollider is BodyPartCollider bpc) ||
                    __instance.Fragments.Count == 0)
                {
                    return;
                }

                var n = __instance.Fragments.Count;
                var share = PlateClientConfig.FragEnergyShare.Value;
                var perFragPen = __instance.PenetrationPower * share / n;

                var wound = AmmoDataCache.Wound;
                if (PlateClientConfig.PhysDamageModel.Value && wound is { Enabled: true })
                {
                    // fragments split the parent's MASS (diameter by cube root,
                    // preserving density); the damage of their hits is computed by the
                    // wound model from their own mass/speed. Re-fragmentation is
                    // forbidden by zeroing the instance chance (anti-recursion,
                    // stateless). Whether a fragment exits THIS part is decided by its
                    // own channel against the remaining chord (the fragmentation point
                    // is unknown — take half); one that does not exit or is lighter
                    // than m_min is inert, its energy has already been deposited in the
                    // part (the fragmentation TC bonus of the wound model).
                    var massShare = Mathf.Max(share / n, 1e-3f);
                    var parentMass = __instance.BulletMassGram;
                    var parentDia = __instance.BulletDiameterMilimeters;
                    var v = __instance.Vector3_1.magnitude;
                    var x = EffectiveX(__instance);
                    var halfChord = 0.5f * ChordMm(bpc, __instance.RaycastHit_0.point,
                        __instance.Vector3_1, parentDia);

                    foreach (var frag in __instance.Fragments)
                    {
                        var fragMass = parentMass * massShare;
                        var fragDia = parentDia * Mathf.Pow(massShare, 1f / 3f);
                        frag.BulletMassGram = fragMass;
                        frag.BulletDiameterMilimeters = fragDia;
                        frag.FragmentationChance = 0f;
                        // pen is not set: it is recomputed absolutely when the fragment hits

                        var vOut = 0f;
                        if (fragMass >= MinFragMassG)
                        {
                            var li = ClientWoundModel.ChannelMm(fragMass, fragDia, v, x, wound);
                            vOut = ExitSpeed(v, li, halfChord, (float)wound.GelStopVelocity);
                        }

                        var dir = frag.Vector3_1.sqrMagnitude > 1e-6f
                            ? frag.Vector3_1.normalized
                            : __instance.Vector3_1.normalized;
                        frag.Vector3_1 = dir * Mathf.Max(vOut, 0.1f);
                    }

                    return;
                }

                // fallback (model disabled): fragments split the damage budget share equally
                var perFrag = __instance.Damage * share / n;
                foreach (var frag in __instance.Fragments)
                {
                    frag.Damage = perFrag;
                    frag.PenetrationPower = perFragPen;
                }
            }
            catch (Exception ex)
            {
                LogError(nameof(FragmentBudgetPostfix), ex);
            }
        }

        // --- 3. Overpenetration child: speed from the log-drag model's energy balance ---

        /// <summary>
        /// Exit speed after T mm of tissue: v·exp(−T/λ), λ = L/ln(v/v_stop).
        /// If T ≥ L (or on a contact impact, L=0) the projectile does not exit — 0.
        /// </summary>
        private static float ExitSpeed(float v, float lMm, float tMm, float vStop)
        {
            if (lMm <= 0f || lMm <= tMm)
            {
                return 0f;
            }

            var lambda = lMm / Mathf.Log(v / Mathf.Max(vStop, 1f)); // L>0 ⇒ v>v_stop
            return v * Mathf.Exp(-tMm / lambda);
        }

        private static void OverpenChildPostfix(EftBulletClass __instance)
        {
            if (Off)
            {
                return;
            }

            try
            {
                if (!__instance.IsForwardHit ||
                    !(__instance.HittedBallisticCollider is BodyPartCollider bpc))
                {
                    return; // walls and body exits — vanilla
                }

                if (__instance.Fragments.Count == 0)
                {
                    return;
                }

                var child = __instance.Fragments[__instance.Fragments.Count - 1];
                var wound = AmmoDataCache.Wound;
                if (PlateClientConfig.PhysDamageModel.Value && wound is { Enabled: true })
                {
                    // the energy balance replaces the vanilla k damage/speed of the
                    // child. Damage and pen are left alone: on the next impact the
                    // wound model computes the damage and the penetration model the
                    // pen, both from the actual speed.
                    var mass = __instance.BulletMassGram;
                    var dia = __instance.BulletDiameterMilimeters;
                    if (mass <= 0f || dia <= 0f)
                    {
                        return;
                    }

                    var v = __instance.Vector3_1.magnitude;
                    var x = EffectiveX(__instance);
                    var l = ClientWoundModel.ChannelMm(mass, dia, v, x, wound);
                    var t = ChordMm(bpc, __instance.RaycastHit_0.point,
                        __instance.Vector3_1, dia);
                    var vOut = ExitSpeed(v, l, t, (float)wound.GelStopVelocity);

                    var dir = child.Vector3_1.sqrMagnitude > 1e-6f
                        ? child.Vector3_1.normalized
                        : __instance.Vector3_1.normalized;
                    child.Vector3_1 = dir * Mathf.Max(vOut, 0.1f);

                    Overlay.HitFeed.PushPanel(
                        $"  v_out {vOut:0} m/s after {bpc.BodyPartType}");
                    return;
                }

                // fallback: the child carries the F share instead of the vanilla k
                var f = Retention(__instance);
                child.Damage = __instance.Damage * f;
                child.PenetrationPower = __instance.PenetrationPower * f;
            }
            catch (Exception ex)
            {
                LogError(nameof(OverpenChildPostfix), ex);
            }
        }

        private static float _lastErrorLogged;

        private static void LogError(string where, Exception ex)
        {
            if (Time.unscaledTime - _lastErrorLogged < 5f)
            {
                return;
            }

            _lastErrorLogged = Time.unscaledTime;
            Plugin.Log.LogError($"[PLATE] Ballistics {where}: {ex}");
        }
    }
}
