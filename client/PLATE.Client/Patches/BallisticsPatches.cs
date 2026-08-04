using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using EFT;
using EFT.HealthSystem;
using EFT.InventoryLogic;
using HarmonyLib;
using PLATE.Client.Ballistics;
using PLATE.Server.Services; // BallisticLimit, compiled into both halves from one file
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

            // second half of a lethal organ wound outside the chest and head pools
            PatchSafe(harmony, PatchTargets.Health_ApplyDamage, nameof(CentralWoundPostfix));

            // absolute penetration derived from impact energy density
            PatchSafe(harmony, PatchTargets.Bullet_DegradeOnHit, nameof(AbsolutePenPostfix));

            // overpenetration is decided by physics (L > chord, stopped-by-bone), not PenetrationLevel
            if (PatchTargets.BodyPart_IsPenetrated != null)
            {
                harmony.Patch(PatchTargets.BodyPart_IsPenetrated,
                    prefix: new HarmonyMethod(typeof(BallisticsPatches),
                        nameof(IsPenetratedPrefix)));
                PatchStats.Track(harmony, PatchTargets.BodyPart_IsPenetrated,
                    nameof(IsPenetratedPrefix));
            }
            else
            {
                PatchStats.MarkFailed(null, nameof(IsPenetratedPrefix), "target not resolved");
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
                PatchStats.Track(harmony, PatchTargets.Armor_SetPenetrationStatus,
                    nameof(ArmorPenetrationPrefix));
            }
            else
            {
                PatchStats.MarkFailed(null, nameof(ArmorPenetrationPrefix), "target not resolved");
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
                PatchStats.MarkFailed(null, postfixName, "target not resolved");
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
                PatchStats.Track(harmony, target, postfixName);
            }
            catch (Exception ex)
            {
                PatchStats.MarkFailed(target, postfixName, ex.Message);
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
                // to both logs: the organ zones are cut out of these boxes, so a raid
                // journal that does not say what the boxes were cannot be read afterwards
                Say($"Victim hitboxes ({parts.Length} total), " +
                    "local sizes, world AABB and resolved axes:");
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

                    // how the box reads once its axes are pointed at the character:
                    // the same list twice over is what makes a wrong axis obvious
                    var body = victim.gameObject.transform;
                    var anat = Anatomy.TryDescribe(c, body, out var box)
                        ? Anatomy.Describe(box) + (Anatomy.IsSpinePlate(box) ? " plate" : "")
                        : "";

                    var world = c != null ? c.bounds.size : Vector3.zero;
                    Say($"  {p.BodyPartColliderType,-22} {geom,-40} " +
                        $"AABB {world.x:0.00}x{world.y:0.00}x{world.z:0.00} m  {anat}");
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"[PLATE] Collider dump failed: {ex.Message}");
            }
        }

        private static void Say(string line)
        {
            Plugin.Log.LogInfo("[PLATE] " + line);
            Overlay.HitFeed.LogEvent(line);
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
            PatchStats.Hit(nameof(DamageInfoCtorPostfix));
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

            // fragmentation is no longer the vanilla FragmentationChance field: the
            // wound model derives it from what the bullet is made of and how fast it
            // is where it turns (3.6). All it needs from here is the core share.
            AmmoDataCache.GetCore(shot.Ammo?.TemplateId, out _, out var coreMassFrac);

            // the path through this part bounds everything: the cavity it can cut and
            // the energy it can leave behind. Whether the projectile carries on past it
            // is the drag law's answer, not the game's bullet state
            var t = PerfTrace.Begin();
            var chordMm = ChordMm(bpc, __instance.HitPoint, __instance.Direction, dia);
            PerfTrace.End("wound.chord", t);

            // what is not the same twice about this particular shot, drawn once
            var spread = ShotSpread.For(shot, dia, wound);

            var d = ClientWoundModel.Compute(mass, dia, v, x, coreMassFrac, chordMm, wound,
                spread.NeckMm, spread.TissueScale);
            var vital = VitalMult(bpc.BodyPartColliderType);
            __instance.Damage = d.DamageHp * vital * DamageScale(bpc.Player as Player);

            // what the projectile actually travelled in here: the chord bounds it, but so
            // does the channel, and a round that stopped short never reached its turn
            var alongMm = Mathf.Max(Mathf.Min(chordMm, d.ChannelMm), 1f);

            var ammo = AmmoLabel(shot);
            Overlay.HitFeed.PushHit(bpc.Player as Player, d.Contact
                ? $"W {ammo} {bpc.BodyPartType}: contact {v:0} m/s -> {__instance.Damage:0.#}"
                : $"W {ammo} {bpc.BodyPartColliderType}: {v:0}/{shot.InitialSpeed:0} m/s" +
                  $", L {d.ChannelMm:0}" +
                  $"/T {chordMm:0} mm, E {d.DepositFrac * 100f:0}%" +
                  $", PC {d.Pc:0.#}+TC {d.Tc:0.#}" +
                  (vital > 1f ? $" x{vital:0.#}" : "") +
                  $" -> {__instance.Damage:0.#}" +
                  (d.ChannelMm > chordMm ? " (through)" : "") +
                  // where it turned and what it went through: two identical-looking hits
                  // that differ have to say why they differ
                  (d.Contact
                      ? ""
                      : $" [yaw {(spread.NeckMm < alongMm ? $"{spread.NeckMm:0} mm" : "none")}" +
                        (d.Frag > 0f ? $", frag {d.Frag:0.00}" : "") +
                        $", tissue x{spread.TissueScale:0.00}]"));

            // the energy the tissue took, over the length it took it along: what the
            // radial cavity is driven by
            var deposited = 0.5f * (mass / 1000f) * v * v * d.DepositFrac;
            ApplyOrganZones(ref __instance, shot, bpc, d.ChannelMm, deposited / alongMm, v,
                spread);

            ApplyWoundBleeding(ref __instance, shot, bpc, d, alongMm, wound);
            LogAnatomy(bpc, __instance.HitPoint);
        }

        /// <summary>
        /// Whether this wound bleeds badly, from what the channel crossed rather than
        /// from what was fired. The ammo template's own bleed chance is overwritten:
        /// a projectile does not carry a bleeding rate around with it, it cuts whatever
        /// was in front of it.
        /// </summary>
        private static void ApplyWoundBleeding(ref DamageInfoStruct info, EftBulletClass shot,
            BodyPartCollider bpc, ClientWoundModel.Deposit d, float pathMm,
            AmmoDataCache.WoundParams wound)
        {
            if (!PlateClientConfig.OrganZones.Value || shot.BlockedBy.HasValue || d.Contact)
            {
                return; // armour took it, or there is no channel to sweep anything
            }

            var volume = d.Pc * (float)wound.WoundVolumePerHp;
            var swept = WoundBleeding.SweptMm2(volume, pathMm);
            var region = WoundBleeding.Region(bpc.BodyPartColliderType);

            var chance = WoundBleeding.HeavyChance(region, swept, BleedTuning());
            var before = info.HeavyBleedingDelta;
            info.HeavyBleedingDelta = chance;

            Overlay.HitFeed.PushHit(bpc.Player as Player,
                $"  bleed {region}: swept {swept:0} mm², " +
                $"heavy {before * 100f:0.#}% -> {chance * 100f:0.#}%");
        }

        private static WoundBleeding.Tuning BleedTuning()
        {
            return new WoundBleeding.Tuning
            {
                VesselsTorso = PlateClientConfig.BleedVesselsTorso.Value,
                VesselsJunction = PlateClientConfig.BleedVesselsJunction.Value,
                VesselsLimb = PlateClientConfig.BleedVesselsLimb.Value,
                VesselsHead = PlateClientConfig.BleedVesselsHead.Value,
                MaxChance = PlateClientConfig.BleedHeavyMaxChance.Value,
            };
        }

        // --- Organ zones ---

        private static OrganZones.Tuning ZoneTuning(AmmoDataCache.WoundParams wound)
        {
            return new OrganZones.Tuning
            {
                TissueStrengthMPa = PlateClientConfig.OrganTissueStrengthMPa.Value,
                KHeart = PlateClientConfig.OrganKHeart.Value,
                KLiver = PlateClientConfig.OrganKLiver.Value,
                KSpine = PlateClientConfig.OrganKSpine.Value,
                ArrestChance = PlateClientConfig.OrganArrestChance.Value,
                AvulsionChance = PlateClientConfig.OrganAvulsionChance.Value,
                LiverRadiusMm = PlateClientConfig.OrganLiverRadiusMm.Value,

                // the same boundary the wound model's own TC term uses, from the server:
                // two velocity sigmoids that disagreed would be two models
                VelocityCenter = (float)wound.TcVelocityCenter,
                VelocityWidth = (float)wound.TcVelocityWidth,
            };
        }

        /// <summary>
        /// Runs the channel against the organ zones of the collider it went into. Armour
        /// that stopped the shot ends it here: behind-armour trauma is a separate
        /// mechanism and nothing reached an organ.
        /// </summary>
        private static void ApplyOrganZones(ref DamageInfoStruct info, EftBulletClass shot,
            BodyPartCollider bpc, float channelMm, float dEdxJPerMm, float velocity,
            ShotSpread spread)
        {
            if (!PlateClientConfig.OrganZones.Value || shot.BlockedBy.HasValue)
            {
                return;
            }

            var wound = AmmoDataCache.Wound;
            if (wound == null)
            {
                return;
            }

            var victim = bpc.Player as Player;
            if (!Anatomy.TryDescribe(bpc.Collider, victim?.gameObject.transform, out var box) ||
                !Anatomy.TryFrame(box, out var frame))
            {
                return; // capsule or sphere — the head, which has its own multipliers
            }

            var tuning = ZoneTuning(wound);
            if (!OrganZones.TryHit(bpc.BodyPartColliderType, frame, info.HitPoint,
                    info.Direction, channelMm, dEdxJPerMm, tuning, out var hit,
                    spread.ZoneShiftMm))
            {
                return; // this collider carries no zone
            }

            // counted per organ rather than per collider box: RibcageUp is two boxes and
            // RibcageLow is three, and one bullet through one liver is one liver
            var zone = (int)hit.Kind;
            OrganZones.Tally(hit.Kind,
                touched: spread.FirstTouch(zone),
                through: hit.Through && spread.FirstThrough(zone),
                lethal: hit.Lethal && spread.FirstLethal(zone));

            // printed even when nothing fired: without it there is no telling a channel
            // that missed the heart from a heart that is missing from the code
            var head = $"  ZONE {hit.Name} ({hit.Where}): PC {hit.PathMm:0}/{hit.NeedMm:0} mm" +
                       (hit.ToZoneMm > 1f ? $" from {hit.ToZoneMm:0} mm in" : "") +
                       (hit.Through ? " deep" : "");

            if (hit.Lethal)
            {
                Overlay.HitFeed.PushHit(victim, head + " -> LETHAL");
                FloorDamageToLethal(ref info, bpc, victim, hit);
                return;
            }

            Overlay.HitFeed.PushHit(victim, head + (hit.Through ? "" : " -> no") +
                $", TC r={hit.TcRadiusMm:0} mm at {hit.DistanceMm:0} mm, " +
                $"overlap {hit.Overlap:0.00}");

            // --- the rolls, and either of them can still end it ---

            var arrest = OrganZones.ArrestChance(hit, velocity, tuning);
            if (arrest > 0f && Roll(spread, victim, hit.Kind, arrest, "arrest"))
            {
                Overlay.HitFeed.PushHit(victim, "    -> LETHAL (traumatic cardiac arrest)");
                OrganZones.Tally(hit.Kind, false, false, spread.FirstLethal(zone));
                FloorDamageToLethal(ref info, bpc, victim, hit);
                return;
            }

            var avulsion = OrganZones.AvulsionChance(hit, velocity, tuning);
            if (avulsion > 0f && Roll(spread, victim, hit.Kind, avulsion, "avulsion"))
            {
                Overlay.HitFeed.PushHit(victim, "    -> LETHAL (liver avulsed)");
                OrganZones.Tally(hit.Kind, false, false, spread.FirstLethal(zone));
                FloorDamageToLethal(ref info, bpc, victim, hit);
                return;
            }

            if (hit.Multiplier > 1.001f)
            {
                var before = info.Damage;
                info.Damage = before * hit.Multiplier;
                OrganZones.TallyMultiplier(hit.Kind);
                Overlay.HitFeed.PushHit(victim,
                    $"    x{hit.Multiplier:0.00} ({before:0.#} -> {info.Damage:0.#})");
            }

            OpenOrganBleed(victim, bpc, hit, spread);
        }

        /// <summary>
        /// Internal bleeding from an opened organ or great vessel. A channel through the
        /// liver opens the full rate — the retrohepatic vena cava runs through the organ,
        /// which is why these wounds kill and why nothing can be pressed on to stop them.
        /// The mediastinum is the same argument with the aorta and the vena cava in it,
        /// and a channel that went all the way through either of those is already dead by
        /// the time it gets here. The cord has no such vessel of its own.
        ///
        /// This is also where the balance the design asks for comes from: the liver stops
        /// being an instant death and becomes a death in half a minute, which moves it out
        /// of the 35% killed outright and into the 52% who die over the following minutes.
        /// </summary>
        private static void OpenOrganBleed(Player victim, BodyPartCollider bpc,
            OrganZones.Result hit, ShotSpread spread)
        {
            if (!PlateClientConfig.BloodEnabled.Value ||
                (hit.Kind != OrganZone.Liver && hit.Kind != OrganZone.Heart))
            {
                return;
            }

            var full = PlateClientConfig.OrganBleedMlSec.Value;
            var rate = full * hit.Involvement;

            // RibcageLow is three collider boxes and the liver behind them is one organ;
            // RibcageUp is two and the mediastinum behind them is one. Only what this
            // meeting opens beyond what the shot has already opened, so a graze followed
            // by a run-through ends at the run-through's rate instead of their sum.
            var extra = spread.BleedTopUp((int)hit.Kind, rate);
            if (extra <= 0.5f)
            {
                return;
            }

            Blood.PlateBloodManager.AddInternal(victim, extra,
                Blood.EInternalBleedSource.Organ, collider: bpc.BodyPartColliderType);

            var toEmpty = PlateClientConfig.BloodMaxMl.Value *
                          (1f - PlateClientConfig.DeathThreshold.Value);
            Overlay.HitFeed.PushHit(victim,
                $"    + internal bleed {extra:0.#} ml/s" +
                (extra < rate - 0.05f ? $" (up to {rate:0.#} for this organ)" : "") +
                $" ({toEmpty:0} ml in {toEmpty / rate:0} s)");
        }

        /// <summary>
        /// One draw per organ per shot, tested against every meeting with that organ.
        ///
        /// A bullet that walks through both RibcageUp boxes is still one bullet, so it
        /// does not get two rolls. But it must not be judged on the first meeting either:
        /// clipping the edge of the heart at 1% and then crossing it at 13% has to come
        /// out at 13. Keeping the number and re-testing it does exactly that —
        /// P(u &lt; a) then P(u &lt; b) on one u is P(u &lt; max(a, b)).
        /// </summary>
        private static bool Roll(ShotSpread spread, Player victim, OrganZone kind,
            float chance, string what)
        {
            var value = spread.RollFor((int)kind, out var fresh);
            var fired = value < chance;

            // printed whatever happens: a mechanic with a probability in it cannot be
            // debugged from the times it fired
            Overlay.HitFeed.PushHit(victim,
                $"    {what} {chance * 100f:0.0}% rolled {value:0.00}" +
                (fresh ? "" : " (this shot's draw)") +
                " -> " + (fired ? "yes" : "no"));
            OrganZones.TallyRoll(kind, fired);
            return fired;
        }

        /// <summary>
        /// Death is dealt as damage and never as a Kill call. The damage is raised to
        /// what the body part has left — a floor, not a replacement, so a category
        /// multiplier cannot save a pierced heart and the ordinary calculation still
        /// wins wherever it is larger. Everything downstream then works by itself:
        /// the kill is attributed to the shot, other mods hooking damage still see the
        /// event, and our own hooks fire without a private path around them.
        /// </summary>
        private static void FloorDamageToLethal(ref DamageInfoStruct info, BodyPartCollider bpc,
            Player victim, OrganZones.Result hit)
        {
            var ahc = victim?.ActiveHealthController;
            if (ahc == null)
            {
                Overlay.HitFeed.PushHit(victim, "    no health controller, damage left as is");
                return;
            }

            var part = bpc.BodyPartType;
            var remaining = ahc.GetBodyPartHealth(part).Current;
            var before = info.Damage;

            if (remaining > before)
            {
                info.Damage = remaining;
                Overlay.HitFeed.PushHit(victim,
                    $"    damage floored {before:0.#} -> {remaining:0.#} ({part} remaining)");
            }
            else
            {
                Overlay.HitFeed.PushHit(victim,
                    $"    damage {before:0.#} already above {part} remaining " +
                    $"{remaining:0.#}, left as is");
            }

            if (part == EBodyPart.Head || part == EBodyPart.Chest)
            {
                return; // zeroing one of these is death by itself
            }

            // The stomach pool does not kill — the game dies of Head or Thorax and
            // nothing else. But every mechanism on the autopsy list is central: a severed
            // cord or a torn vena cava kills the brain that stops being supplied, not the
            // abdomen. So the shot reaches the chest as well, as its own damage event,
            // once the primary one has landed.
            _central = new CentralWound
            {
                Victim = victim,
                Part = part,
                Zone = hit.Name,
                Frame = Time.frameCount,
            };
        }

        private struct CentralWound
        {
            public Player Victim;
            public EBodyPart Part;
            public string Zone;
            public int Frame;
        }

        private static CentralWound _central;
        private static bool _applyingCentral;

        /// <summary>
        /// The second half of a lethal wound in a body part whose pool does not kill.
        /// Runs after the primary damage has landed so the order of events reads the way
        /// it happened. One per shot however many pellets a volley put into the same
        /// frame, and guarded against re-entering itself.
        /// </summary>
        private static void CentralWoundPostfix(ActiveHealthController __instance,
            EBodyPart bodyPart, DamageInfoStruct damageInfo)
        {
            PatchStats.Hit(nameof(CentralWoundPostfix));
            if (Off || _applyingCentral || _central.Frame != Time.frameCount ||
                bodyPart != _central.Part ||
                !ReferenceEquals(__instance?.Player, _central.Victim))
            {
                return;
            }

            var pending = _central;
            _central = default;

            try
            {
                var chest = __instance.GetBodyPartHealth(EBodyPart.Chest).Current;
                if (chest <= 0f)
                {
                    return; // already gone
                }

                _applyingCentral = true;
                __instance.ApplyDamage(EBodyPart.Chest, chest, damageInfo);
                OrganZones.TallyCentral();
                Overlay.HitFeed.PushHit(pending.Victim,
                    $"    + central {chest:0.#} to Chest ({pending.Zone} sits in " +
                    $"{pending.Part}, whose pool does not kill)");
            }
            catch (Exception ex)
            {
                LogError(nameof(CentralWoundPostfix), ex);
            }
            finally
            {
                _applyingCentral = false;
            }
        }

        /// <summary>
        /// Where in the hitbox the bullet went in, once the box has been pointed at the
        /// character. The organ zones are thirds of these boxes, so a width axis resolved
        /// the wrong way round would move the liver to the left side of the body and
        /// nothing else would look wrong. Shooting a known side and reading the signs
        /// back is the check, and it is the only one there is.
        /// </summary>
        private static void LogAnatomy(BodyPartCollider bpc, Vector3 hitPoint)
        {
            if (!PlateClientConfig.AnatomyDebug.Value)
            {
                return;
            }

            var victim = bpc.Player as Player;
            if (!Anatomy.TryDescribe(bpc.Collider, victim?.gameObject.transform, out var box))
            {
                return; // capsule or sphere — the head, where no zone lives
            }

            var f = Anatomy.FractionsWorld(box, hitPoint);
            var third = Anatomy.Third(f.x);
            Overlay.HitFeed.PushHit(victim,
                // the body part as well as the collider: which HP pool a collider belongs
                // to is the whole reason the lethal zones need a second damage event
                $"BOX {bpc.BodyPartColliderType}/{bpc.BodyPartType} {Anatomy.Describe(box)}" +
                $": entry w{f.x:+0.00;-0.00} h{f.y:+0.00;-0.00} d{f.z:+0.00;-0.00}" +
                $", {(third > 0 ? "right" : third < 0 ? "left" : "middle")} third" +
                (Anatomy.IsSpinePlate(box) ? ", plate" : ""));
        }

        /// <summary>
        /// What was fired, for the journal. The template name carries both caliber and
        /// load ("762x51_M80"); the "patron_" prefix every one of them shares is
        /// dropped. Reading a hit without knowing the round tells you very little.
        /// </summary>
        private static string AmmoLabel(EftBulletClass shot)
        {
            var name = shot?.Ammo?.Template?.Name;
            if (string.IsNullOrEmpty(name))
            {
                return "?";
            }

            return name.StartsWith("patron_", StringComparison.OrdinalIgnoreCase)
                ? name.Substring("patron_".Length)
                : name;
        }

        /// <summary>
        /// Damage scale of the hit VICTIM's category — same split as the blood knobs,
        /// so "PMCs are made of paper, scavs are not" is one setting each.
        /// </summary>
        private static float DamageScale(Player victim)
        {
            return Blood.PlateBloodManager.CategoryValue(victim,
                PlateClientConfig.DamageScalePlayer.Value,
                PlateClientConfig.DamageScalePmc.Value,
                PlateClientConfig.DamageScaleScav.Value);
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
            PatchStats.Hit(nameof(AbsolutePenPostfix));
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
        /// Builds the collider list for a player before anyone shoots them. The scan
        /// walks a fully rigged character and is the one piece of per-victim work heavy
        /// enough to be felt as a hitch, so it must not happen on the frame of a hit.
        /// Called from the plugin's Update for one player at a time.
        /// </summary>
        internal static void WarmVictimColliders(Player p)
        {
            var t = PerfTrace.Begin();
            GetVictimColliders(p);
            PerfTrace.End("wound.warmColliders", t);
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
            PatchStats.Hit(nameof(IsPenetratedPrefix));
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

                // the same tissue this shot's damage will be computed against: the exit
                // decision and the wound have to be told the same story about the body
                var spread = ShotSpread.For(shot, dia, p);
                var l = ClientWoundModel.ChannelMm(mass, dia, v, x, p, spread.TissueScale);
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
        /// <summary>
        /// Recorded previous hits within the material's damage radius of this one.
        /// The count is the geometry's answer to "how damaged is this spot" — what to
        /// make of it is ArmorWear's business, per layer.
        /// </summary>
        private static int HitsNearby(ArmorComponent armor, BodyPartCollider bpc,
            Vector3 localPos, AmmoDataCache.ArmorMatProfile prof)
        {
            if (!PlateClientConfig.ArmorLocalDegradation.Value || prof.DAreaMm <= 0 ||
                !_armorHits.TryGetValue(armor, out var marks))
            {
                return 0;
            }

            var r2 = (float)(prof.DAreaMm / 1000.0 * (prof.DAreaMm / 1000.0));
            var n = 0;
            foreach (var m in marks)
            {
                if (m.Zone == bpc.BodyPartColliderType &&
                    (m.LocalPos - localPos).sqrMagnitude <= r2)
                {
                    n++;
                }
            }

            return n;
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
            PatchStats.Hit(nameof(ArmorPenetrationPrefix));
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
        /// Physical armor decision: U_hit = E/A_core versus U_limit = class·material·
        /// wear·(1/cos angle). Below the band — block (BABT); above — penetration with a
        /// price: E_cost, the jacket stripped off in the hole, and what is left eroded by
        /// the barrier (K_frag) and blunted by it (K_def).
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

            // The armour meets the hard core, not the calibre. A 5.6 mm carbide core in
            // a 7.85 mm bullet arrives at twice the energy density of the same energy
            // spread over the full jacket — that is where armour piercing comes from,
            // and it used to be imitated by a multiplier keyed off how soft the bullet was.
            AmmoDataCache.GetCore(shot.Ammo?.TemplateId, out var coreArea, out var coreMass);
            var hitArea = ArmorExit.ImpactArea(dia, coreArea, x, (float)cfg.ExpansionOnArmor);
            var uHit = eForU / hitArea;

            // threshold: class × material × slanted (oblique) thickness; wear joins
            // below, once this hit knows whether it found a damaged spot
            var prof = cfg.Profile(armor.Template.ArmorMaterial.ToString());
            var duraShare = armor.Repairable.TemplateDurability > 0
                ? Mathf.Clamp01(armor.Repairable.Durability /
                                armor.Repairable.TemplateDurability)
                : 1f;
            var dir = shot.Vector3_1.sqrMagnitude > 1e-6f
                ? shot.Vector3_1.normalized
                : Vector3.forward;

            // the raw one is kept for the log. Angle moves a limit harder than any
            // constant in the model — a 70° read costs a plate three times its
            // thickness — and reconstructing it afterwards from a v50 is guesswork, so
            // the hit line says what angle it was decided at and whether the floor,
            // rather than the geometry, is what set it
            var rawCos = Mathf.Abs(Vector3.Dot(dir, shot.HitNormal.normalized));
            var cos = Mathf.Max(rawCos, (float)cfg.AngleMinCos);
            var uLimit = (float)(cfg.ClassULimit(armor.ArmorClass) * prof.ULimitMult) / cos;

            // fibers (UHMWPE/aramid) get pushed apart by sharp-nosed projectiles — lower threshold for X<0.5
            if (prof.SharpVulnMult > 0)
            {
                uLimit *= 1f - (float)prof.SharpVulnMult * Mathf.Clamp01((0.5f - x) * 2f);
            }

            // Wear, probabilistic (3.4). Seen damage — this hit landed within the
            // damage radius of recorded previous hits — is answered by geometry.
            // Unseen damage (a worn item entering the raid, memory overflow) is
            // rolled: the chance of striking a damaged spot IS the missing
            // durability. The current hit is recorded below.
            var bpc = shot.HittedBallisticCollider as BodyPartCollider;
            var localPos = Vector3.zero;
            var hitsNearby = 0;
            if (bpc != null)
            {
                localPos = bpc.ColliderTransformCached != null
                    ? bpc.ColliderTransformCached.InverseTransformPoint(shot.RaycastHit_0.point)
                    : shot.RaycastHit_0.point;
                hitsNearby = HitsNearby(armor, bpc, localPos, prof);
            }

            // one draw per hit; both layers share it — it is one event, answered per
            // layer by that layer's own q and k
            var wearRoll = UnityEngine.Random.value;
            var wornFace = ArmorWear.WornFraction(hitsNearby, 1f - duraShare,
                (float)prof.SpotDamageQ, (float)prof.WearExponentK, wearRoll);
            uLimit *= wornFace;

            // --- Ballistic limit, where the item's construction is known ---
            //
            // A class threshold is a statement about a certificate; a ballistic limit is
            // a statement about a plate. Where the server resolved the item to a real
            // thickness and a real material, the question stops being "is this энергия
            // per mm² over the line" and becomes "is this faster than v_bl", and the
            // energy the plate takes stops being a tuned constant: it is whatever
            // ½m(v² − v_r²) comes to once Recht-Ipson has answered.
            var tuning = BallisticLimit.Tuning.Default;
            var limitCore = BallisticLimit.Driving(mass, dia, coreArea, coreMass,
                AmmoDataCache.GetCoreHardness(shot.Ammo?.TemplateId), tuning);

            // TemplateId, not Template: the latter is an ItemTemplate object whose
            // ToString is a type name, so it matched nothing and every plate in the game
            // fell back to its class threshold without saying so
            var haveGeometry = AmmoDataCache.TryBarrier(armor.Item.TemplateId.ToString(),
                out var barrier);
            float ratio;
            float eCost;
            float v50 = 0f;
            if (haveGeometry)
            {
                // wear thins the plate rather than lowering a number — per LAYER: the
                // tile of a ceramic composite is rubble after a hit, the fibre panel
                // behind it wears like the fibre it is
                barrier.ThicknessMm *= wornFace;
                if (barrier.BackingMm > 0)
                {
                    var backProf = cfg.Profile(
                        AmmoDataCache.BackingMaterialOf(armor.Item.TemplateId.ToString()));
                    barrier.BackingMm *= ArmorWear.WornFraction(hitsNearby, 1f - duraShare,
                        (float)backProf.SpotDamageQ, (float)backProf.WearExponentK, wearRoll);
                }

                v50 = (float)BallisticLimit.V50(barrier, limitCore, cos, tuning);
                ratio = v50 > 0f ? v / v50 : 999f;
            }
            else
            {
                ratio = uHit / Mathf.Max(uLimit, 1e-3f);
            }

            // probabilistic band around the limit (the material is not uniform)
            var band = Mathf.Max((float)cfg.ThresholdBand, 0.001f);
            var pierceChance = Mathf.Clamp01((ratio - (1f - band)) / (2f * band));
            var pierce = pierceChance > 0f && UnityEngine.Random.value < pierceChance;

            float eOut;
            if (haveGeometry && v50 > 0f)
            {
                // Recht-Ipson: what is left after the plate, plug and all
                var plug = (float)BallisticLimit.PlugMassG(barrier, limitCore, cos, tuning);
                // the same mass the limit was computed against — a tile and a fibre pack
                // meet the whole bullet, a metal plate meets the core
                var vr = (float)BallisticLimit.ResidualVelocity(v, v50,
                    (float)BallisticLimit.MassAgainst(barrier, limitCore), plug);
                eOut = 0.5f * (mass / 1000f) * vr * vr;
                eCost = Mathf.Max(e - eOut, 0f);
            }
            else
            {
                // energy price of penetration: work ∝ strength × hole area × thickness,
                // and the hole is the size of what makes it — a core punches a narrower
                // one and a bullet that flattened on the way in punches a wider one
                eCost = (float)prof.ECostMult * uLimit * hitArea;
                eOut = e - eCost;
            }

            // how the limit was arrived at, for the log: the angle it was read at, and
            // what the hardness argument between core and plate was worth. Those two
            // terms multiply, and between them they are most of the spread a raid shows
            // on one plate against one round
            var geometry = haveGeometry
                ? $"{barrier.ThicknessMm:0.0} mm, v {v:0}/{v50:0} m/s, " +
                  $"{Mathf.Acos(Mathf.Clamp01(rawCos)) * Mathf.Rad2Deg:0}°" +
                  (rawCos < cos ? " (at the floor)" : "") +
                  $", H x{BallisticLimit.HardnessFactor(barrier, limitCore, tuning):0.00}"
                : $"U {uHit:0.#}/{uLimit:0.#} J/mm², " +
                  $"{Mathf.Acos(Mathf.Clamp01(rawCos)) * Mathf.Rad2Deg:0}°" +
                  (rawCos < cos ? " (at the floor)" : "");

            if (!pierce || eOut < 1f)
            {
                shot.BlockedBy = armor.Item.Id; // block (or lodged in the soft pack) -> BABT
                if (bpc != null)
                {
                    RecordArmorHit(armor, bpc, localPos); // a blocked hit damages the zone too
                }

                RecordAbsorbedEnergy(armor, e); // all the energy goes into the armor
                // the victim comes from this shot's own collider rather than _shotCtx:
                // a stale frame there would put someone else's name on the line
                Overlay.HitFeed.PushHit(bpc?.Player as Player,
                    $"armor {armor.Template.ArmorMaterial} cl.{armor.ArmorClass}: " + geometry +
                    (wornFace < 1f ? $" (wear x{wornFace:0.00}, {hitsNearby} prior)" : "") + " -> block");
                return false;
            }

            // Only a barrier with a hole in it can strip a jacket off. Where the item's
            // construction is on file the barrier itself says whether it is a fibre pack;
            // where it is not, the material does.
            var stripsJacket = haveGeometry
                ? barrier.Class != BallisticLimit.Fibrous
                : !IsSoftPack(armor.Template.ArmorMaterial);

            var exit = ArmorExit.Compute(mass, dia, x, eOut, coreArea, coreMass,
                (float)prof.KFrag, (float)prof.KDef, stripsJacket);
            var mOut = exit.MassG;
            var dOut = exit.DiaMm;
            var vOut = exit.V;
            var xOut = exit.X;

            shot.BulletMassGram = mOut;
            shot.BulletDiameterMilimeters = dOut;
            shot.Vector3_1 = dir * vOut;
            _xOvrFrame = Time.frameCount;
            _xOvrShot = shot;
            _xOvrValue = xOut;

            if (bpc != null)
            {
                RecordArmorHit(armor, bpc, localPos); // a hole weakens the zone
            }

            // the penetration work plus whatever the shed jacket was still carrying
            RecordAbsorbedEnergy(armor, eCost + exit.JacketEnergyJ);

            Overlay.HitFeed.PushHit(bpc?.Player as Player,
                $"armor {armor.Template.ArmorMaterial} cl.{armor.ArmorClass}: " + geometry +
                (wornFace < 1f ? $" (wear x{wornFace:0.00}, {hitsNearby} prior)" : "") +
                $" -> pierce, -{eCost:0} J, v {v:0}->{vOut:0}, X {x:0.00}->{xOut:0.00}" +
                (mOut < mass * 0.995f
                    ? $", core {mass:0.0}->{mOut:0.0} g / {dia:0.0}->{dOut:0.0} mm"
                    : ""));
            return false; // vanilla does not roll
        }

        /// <summary>
        /// A pack of woven fibre rather than a rigid element — no hole, so nothing for a
        /// jacket to shear against. Used only where the item's construction is not on
        /// file; where it is, the barrier carries its own class.
        /// </summary>
        private static bool IsSoftPack(EArmorMaterial material)
        {
            return material == EArmorMaterial.Aramid || material == EArmorMaterial.UHMWPE;
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
            PatchStats.Hit(nameof(ArmorMitigationPostfix));
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

            ApplyBabtEffects(_shotCtx.Victim, damageInfo.BodyPartColliderType, bc, bc1, bc2);
            Overlay.HitFeed.PushHit(_shotCtx.Victim,
                $"BABT {armor.Template.ArmorMaterial} bc={bc:0.00} bfd={bfd:0}J " +
                $"D={dCm:0.#}cm bt={armor.BluntThroughput:0.###} -> dmg {dmg:0.#}");
        }

        private static int _babtFxFrame;
        private static Player _babtFxVictim;

        /// <summary>
        /// Limb hitboxes. Behind-armor trauma over one of these is soft armor on an arm
        /// or a leg: the backface deformation bruises muscle, it does not rupture
        /// anything into a cavity, so no internal bleed comes out of it. Anything not
        /// listed counts as core — an unrecognised hitbox is far likelier to be a new
        /// torso segment than a new limb.
        /// </summary>
        private static bool IsLimbZone(EBodyPartColliderType collider)
        {
            switch (collider)
            {
                case EBodyPartColliderType.LeftThigh:
                case EBodyPartColliderType.RightThigh:
                case EBodyPartColliderType.LeftCalf:
                case EBodyPartColliderType.RightCalf:
                case EBodyPartColliderType.LeftUpperArm:
                case EBodyPartColliderType.RightUpperArm:
                case EBodyPartColliderType.LeftForearm:
                case EBodyPartColliderType.RightForearm:
                    return true;
                default:
                    return false;
            }
        }

        private static void ApplyBabtEffects(Player victim, EBodyPartColliderType collider,
            float bc, float bc1, float bc2)
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

            // internal bleeding: probability grows toward BC2 (100% there).
            // Core zones only — a plate over the chest or a helmet transmits into a
            // cavity, an arm panel does not (TODO: an intracranial bleed is not a
            // volume problem at all — rising ICP, not hypovolemia; modelled as a drain
            // for now, see docs/MODEL.md)
            if (PlateClientConfig.BloodEnabled.Value &&
                !IsLimbZone(collider) &&
                UnityEngine.Random.value < t &&
                PlateClientConfig.BabtInternalBleedRate.Value > 0f)
            {
                Blood.PlateBloodManager.AddInternal(victim,
                    PlateClientConfig.BabtInternalBleedRate.Value,
                    Blood.EInternalBleedSource.Babt, collider: collider);
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
            PatchStats.Hit(nameof(FragmentBudgetPostfix));
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
                    var spread = ShotSpread.For(__instance, parentDia, wound);

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
                            var li = ClientWoundModel.ChannelMm(fragMass, fragDia, v, x, wound,
                                spread.TissueScale);
                            vOut = ExitSpeed(v, li, halfChord, (float)wound.GelStopVelocity);
                        }

                        var dir = frag.Vector3_1.sqrMagnitude > 1e-6f
                            ? frag.Vector3_1.normalized
                            : __instance.Vector3_1.normalized;
                        frag.Vector3_1 = dir * Mathf.Max(vOut, 0.1f);

                        // one body, one shot: the fragments carry the parent's draw on
                        ShotSpread.Inherit(__instance, frag, halfChord);
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
            PatchStats.Hit(nameof(OverpenChildPostfix));
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
                    var spread = ShotSpread.For(__instance, dia, wound);
                    var l = ClientWoundModel.ChannelMm(mass, dia, v, x, wound,
                        spread.TissueScale);
                    var t = ChordMm(bpc, __instance.RaycastHit_0.point,
                        __instance.Vector3_1, dia);
                    var vOut = ExitSpeed(v, l, t, (float)wound.GelStopVelocity);

                    var dir = child.Vector3_1.sqrMagnitude > 1e-6f
                        ? child.Vector3_1.normalized
                        : __instance.Vector3_1.normalized;
                    child.Vector3_1 = dir * Mathf.Max(vOut, 0.1f);

                    // same shot, same body, and a projectile does not un-turn: what it
                    // has already crossed comes off its neck
                    ShotSpread.Inherit(__instance, child, t);

                    Overlay.HitFeed.PushHit(bpc.Player as Player,
                        $"v_out {vOut:0} m/s after {bpc.BodyPartType}");
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