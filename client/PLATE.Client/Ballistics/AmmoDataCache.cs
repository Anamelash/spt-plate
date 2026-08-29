using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using PLATE.Server.Services;
using SPT.Common.Http;

namespace PLATE.Client.Ballistics
{
    /// <summary>
    /// Normalizer data from the server (/plate/ammo-data): expansiveness index X,
    /// muzzle energy E0 and fragmentation chance for every ammo template + the wound
    /// channel model constants ("__wound"). The client cannot recompute X itself:
    /// it only sees the already-normalized templates (sd equalized by energy).
    /// </summary>
    internal static class AmmoDataCache
    {
        internal class Entry
        {
            public double X { get; set; }
            public double E0 { get; set; }
            public double? Pdm { get; set; }

            /// <summary>Hard core frontal area as a fraction of the bullet's; 1 = monolithic.</summary>
            public double Ca { get; set; } = 1;

            /// <summary>Hard core mass as a fraction of the bullet's; 1 = monolithic.</summary>
            public double Cm { get; set; } = 1;

            /// <summary>Vickers hardness of the core — which of it and the plate gives way first.</summary>
            public double Hv { get; set; } = 60;

            /// <summary>Share of large fragments (1/grenade FragmentsCount); shrapnel only.</summary>
            public double? LargeShare { get; set; }

            /// <summary>
            /// Measured bullet length, mm, where the reference book publishes one.
            /// 0 — nobody did, and the geometry infers it from mass over calibre. An
            /// older server does not send the field at all, which reads as the same 0
            /// and therefore as the behaviour that was there before.
            /// </summary>
            public double L { get; set; }
        }

        /// <summary>Wound channel model constants (server-side AmmoNormalizer config).</summary>
        internal class WoundParams
        {
            public bool Enabled { get; set; }
            public double GelDepthK { get; set; }
            public double GelStopVelocity { get; set; }
            public double ExpansionDepthFactor { get; set; }
            public double ExpansionAreaFactor { get; set; }

            // Broadside geometry. The defaults matter: an older server does not send
            // these, and reading them as zero would give every bullet no length and so
            // no cavity at all.
            public double YawNeckCalibres { get; set; } = 20;
            public double YawBroadsideFraction { get; set; } = 0.75;
            public double BulletDensityGPerCm3 { get; set; } = 10.5;
            public double BulletFormFactor { get; set; } = 0.65;

            public double BodyDepthMm { get; set; }
            public double WoundVolumePerHp { get; set; }
            public double TcVelocityCenter { get; set; }
            public double TcVelocityWidth { get; set; }
            public double TcEnergyPerHp { get; set; }
            public double TcFragBonus { get; set; }

            /// <summary>Velocity at the tumble point above which a jacket lets go, m/s.
            /// An older server does not send it; 600 is the shipped default.</summary>
            public double FragVelocityThreshold { get; set; } = 600;

            public double EnergyCapPerHp { get; set; }
        }

        /// <summary>Armor material profile (server-side Armor config).</summary>
        internal class ArmorMatProfile
        {
            public double ULimitMult { get; set; } = 1.0;
            public double ECostMult { get; set; } = 0.6;
            public double KDef { get; set; }
            public double KFrag { get; set; }

            /// <summary>Local degradation radius around a hit, mm. Capped at the 51 mm
            /// the certification standards space their scored shots by.</summary>
            public double DAreaMm { get; set; } = 30;

            /// <summary>Damage one hit does to the spot it lands on, 0..1 (3.4).</summary>
            public double SpotDamageQ { get; set; } = 0.4;

            /// <summary>How local the damage stays: thickness at a damaged spot is 1 − x^k.</summary>
            public double WearExponentK { get; set; } = 2;

            /// <summary>Fiber vulnerability to sharp-nosed bullets (X below 0.5).</summary>
            public double SharpVulnMult { get; set; }

            /// <summary>Joules of absorbed energy per 1 durability point.</summary>
            public double JPerDurability { get; set; } = 400;
        }

        /// <summary>Physical armor constants (the "__armor" block).</summary>
        internal class ArmorParams
        {
            public bool Enabled { get; set; }
            public double ThresholdBand { get; set; } = 0.12;
            public double AngleMinCos { get; set; } = 0.34;
            public double[] ClassULimitJmm2 { get; set; }

            /// <summary>Spread of a deformable bullet on the panel face: area × (1 + this·X).</summary>
            public double ExpansionOnArmor { get; set; } = 0.6;

            /// <summary>
            /// The fraction of its thickness a hit must bite into before a ductile
            /// plate wears at all (ArmorDamageCalculator). The initializer is the
            /// shipped default, kept for a payload from an older server.
            /// </summary>
            public double WearDepthFraction { get; set; } = 0.5;

            /// <summary>The share of the energy price a fibre pack pays for a hit it
            /// stopped; 0 = the published multi-hit evidence.</summary>
            public double FibreBlockWearFraction { get; set; }

            public Dictionary<string, ArmorMatProfile> Materials { get; set; }

            private static readonly ArmorMatProfile Default = new()
                { ULimitMult = 1.0, ECostMult = 0.6, KDef = 0.2, KFrag = 0.05 };

            public ArmorMatProfile Profile(string material)
            {
                return material != null && Materials != null &&
                       Materials.TryGetValue(material, out var p) && p != null
                    ? p
                    : Default;
            }

            /// <summary>
            /// Class U_limit, J/mm². The index IS the class — 0 is the anti-fragment
            /// tier, 1..6 are Br1..Br6 since the realignment. A class beyond the table
            /// gets the last entry, which also keeps a stale six-element array sane.
            /// </summary>
            public double ClassULimit(int armorClass)
            {
                if (ClassULimitJmm2 == null || ClassULimitJmm2.Length == 0)
                {
                    return double.MaxValue; // no data — impenetrable (obvious in tests)
                }

                var idx = armorClass;
                if (idx < 0)
                {
                    idx = 0;
                }
                else if (idx >= ClassULimitJmm2.Length)
                {
                    idx = ClassULimitJmm2.Length - 1;
                }

                return ClassULimitJmm2[idx];
            }
        }

        /// <summary>An armour item's construction: how thick it is and what of.</summary>
        internal class PlateGeometry
        {
            /// <summary>Thickness of the hard element, mm.</summary>
            public double T { get; set; }

            /// <summary>Material key into the materials table.</summary>
            public string M { get; set; }

            /// <summary>Fraction of the entry that is actually the material; 1 = solid.</summary>
            public double P { get; set; } = 1;

            /// <summary>Fibre backing behind the face, mm; 0 = single layer.</summary>
            public double B { get; set; }

            /// <summary>Backing material key; empty = aramid, the dominant case.</summary>
            public string BM { get; set; }

            /// <summary>
            /// The alloy's own shear, yield and hardness, where the product names a grade
            /// the game's eight materials cannot express — a Russian 44S panel and an
            /// American AR500 both arrive as "ArmoredSteel" and are not the same steel.
            /// 0 = whatever the material table says.
            /// </summary>
            public double S { get; set; }

            public double Y { get; set; }

            public double H { get; set; }
        }

        /// <summary>How a material fails and how strongly, from the reference book.</summary>
        internal class MaterialPhysics
        {
            public string Class { get; set; }

            /// <summary>Ductile only: ShearPlugging | HoleExpansion; empty = plugging.</summary>
            public string FailureMode { get; set; }
            public double DensityGCm3 { get; set; }
            public double ShearMPa { get; set; }
            public double YieldMPa { get; set; }
            public double CompressiveMPa { get; set; }
            public double FibreTensileMPa { get; set; }
            public double FailureStrain { get; set; }
            public double HardnessHv { get; set; }
        }

        /// <summary>The /plate/armor-data payload.</summary>
        internal class ArmorGeometry
        {
            public Dictionary<string, PlateGeometry> Plates { get; set; }
            public Dictionary<string, MaterialPhysics> Materials { get; set; }
        }

        private static Dictionary<string, Entry> _data;
        private static WoundParams _wound;
        private static ArmorParams _armor;
        private static ArmorGeometry _geometry;
        private static bool _fetchFailed;
        private static bool _geometryFailed;

        /// <summary>X for a cartridge; 0.5 (neutral) when there is no data.</summary>
        public static double GetX(string ammoTemplateId)
        {
            EnsureLoaded();
            if (ammoTemplateId != null && _data != null &&
                _data.TryGetValue(ammoTemplateId, out var e))
            {
                return e.X;
            }

            return 0.5;
        }

        /// <summary>
        /// Hard core geometry: frontal area fraction and mass fraction. (1, 1) — a
        /// monolithic bullet, which is what an unknown cartridge is assumed to be.
        /// </summary>
        public static void GetCore(string ammoTemplateId, out float areaFrac, out float massFrac)
        {
            EnsureLoaded();
            if (ammoTemplateId != null && _data != null &&
                _data.TryGetValue(ammoTemplateId, out var e))
            {
                areaFrac = Clamp01Core(e.Ca);
                massFrac = Clamp01Core(e.Cm);
                return;
            }

            areaFrac = 1f;
            massFrac = 1f;
        }

        /// <summary>
        /// Measured bullet length for a cartridge, mm; 0 when the book does not publish
        /// one, when the server is not there at all, or when it is an older one. Zero is
        /// what every caller passes on to YawModel, which then infers the length from
        /// mass over calibre — never a crash and never a zero-length bullet.
        /// </summary>
        public static double GetLengthMm(string ammoTemplateId)
        {
            EnsureLoaded();
            if (ammoTemplateId != null && _data != null &&
                _data.TryGetValue(ammoTemplateId, out var e) && e.L > 0)
            {
                return e.L;
            }

            return 0;
        }

        /// <summary>Vickers hardness of the core; 60 (lead and copper) when unknown.</summary>
        public static double GetCoreHardness(string ammoTemplateId)
        {
            EnsureLoaded();
            if (ammoTemplateId != null && _data != null &&
                _data.TryGetValue(ammoTemplateId, out var e) && e.Hv > 0)
            {
                return e.Hv;
            }

            return 60;
        }

        /// <summary>A core fraction is a fraction; 0 would mean a bullet with no bullet in it.</summary>
        private static float Clamp01Core(double v)
        {
            return v <= 0.05 ? 0.05f : v >= 1 ? 1f : (float)v;
        }

        public static bool IsLoaded => _data != null;

        /// <summary>Wound channel model constants; null if the server did not provide
        /// them (old server or the module is disabled).</summary>
        public static WoundParams Wound
        {
            get
            {
                EnsureLoaded();
                return _wound;
            }
        }

        /// <summary>Physical armor constants; null — the server did not provide them.</summary>
        public static ArmorParams Armor
        {
            get
            {
                EnsureLoaded();
                return _armor;
            }
        }

        /// <summary>Large fragment share for shrapnel; -1 if the server did not report it.</summary>
        public static double GetLargeShare(string ammoTemplateId)
        {
            EnsureLoaded();
            if (ammoTemplateId != null && _data != null &&
                _data.TryGetValue(ammoTemplateId, out var e) && e.LargeShare.HasValue)
            {
                return e.LargeShare.Value;
            }

            return -1;
        }

        /// <summary>
        /// The barrier an armour item is, ready for the ballistic limit. Returns false
        /// when the server could not resolve a thickness for it, which is the caller's
        /// signal to fall back to reading the item's class.
        /// </summary>
        public static bool TryBarrier(string armorTemplateId, out BallisticLimit.Barrier barrier)
        {
            barrier = default;
            EnsureGeometry();
            if (armorTemplateId == null || _geometry?.Plates == null ||
                !_geometry.Plates.TryGetValue(armorTemplateId, out var plate) ||
                plate == null || plate.T <= 0)
            {
                return false;
            }

            if (_geometry.Materials == null || plate.M == null ||
                !_geometry.Materials.TryGetValue(plate.M, out var m) || m == null)
            {
                return false;
            }

            barrier = new BallisticLimit.Barrier
            {
                Class = m.Class,
                FailureMode = m.FailureMode,
                ThicknessMm = plate.T,

                // the product's own alloy wins over the material's, because the material
                // is the game's enum and one of its eight names covers every steel there is
                ShearMPa = plate.S > 0 ? plate.S : m.ShearMPa,
                YieldMPa = plate.Y > 0 ? plate.Y : m.YieldMPa,
                CompressiveMPa = m.CompressiveMPa,
                FibreTensileMPa = m.FibreTensileMPa,
                FailureStrain = m.FailureStrain,
                HardnessHv = plate.H > 0 ? plate.H : m.HardnessHv,
                DensityGCm3 = m.DensityGCm3,

                // a sewn package is mostly air and only its fibre does any work; the
                // server resolved that per item and sent it
                PackedFraction = plate.P > 0 ? plate.P : 1,
            };

            // the fibre panel behind the face works as its own layer; its properties
            // come from the same materials table the face's do
            if (plate.B > 0)
            {
                var backingKey = string.IsNullOrEmpty(plate.BM) ? "Aramid" : plate.BM;
                if (_geometry.Materials.TryGetValue(backingKey, out var bm) && bm != null)
                {
                    barrier.BackingMm = plate.B;
                    barrier.BackingTensileMPa = bm.FibreTensileMPa;
                    barrier.BackingStrain = bm.FailureStrain;
                    // a stitched fabric screen is mostly air; a pressed laminate is not
                    barrier.BackingPacked = backingKey == "Aramid"
                        ? BallisticLimit.SewnPacked
                        : 1;
                }
            }

            return true;
        }

        /// <summary>
        /// The backing layer's material key, or null for a single-layer item. Wear
        /// needs it: q and k are properties of a LAYER, and the fibre panel behind a
        /// ceramic face wears like the fibre it is, not like the face.
        /// </summary>
        public static string BackingMaterialOf(string armorTemplateId)
        {
            EnsureGeometry();
            if (armorTemplateId == null || _geometry?.Plates == null ||
                !_geometry.Plates.TryGetValue(armorTemplateId, out var plate) ||
                plate == null || plate.B <= 0)
            {
                return null;
            }

            return string.IsNullOrEmpty(plate.BM) ? "Aramid" : plate.BM;
        }

        private static void EnsureGeometry()
        {
            if (_geometry != null || _geometryFailed)
            {
                return;
            }

            try
            {
                _geometry = JsonConvert.DeserializeObject<ArmorGeometry>(
                    RequestHandler.GetJson("/plate/armor-data"));
                var status = "[PLATE] Armour geometry loaded from server: " +
                             $"{_geometry?.Plates?.Count ?? 0} items with a thickness, " +
                             $"{_geometry?.Materials?.Count ?? 0} materials";
                Plugin.Log.LogInfo(status);
                Overlay.HitFeed.LogEvent(status);
            }
            catch (Exception ex)
            {
                _geometryFailed = true;
                Plugin.Log.LogWarning(
                    $"[PLATE] Failed to fetch /plate/armor-data ({ex.Message}); every plate " +
                    "will be read at its class threshold instead of its construction.");
            }
        }

        private static void EnsureLoaded()
        {
            if (_data != null || _fetchFailed)
            {
                return;
            }

            try
            {
                var json = RequestHandler.GetJson("/plate/ammo-data");
                var root = JObject.Parse(json);
                if (root["__wound"] != null)
                {
                    _wound = root["__wound"].ToObject<WoundParams>();
                    root.Remove("__wound");
                }

                if (root["__armor"] != null)
                {
                    _armor = root["__armor"].ToObject<ArmorParams>();
                    root.Remove("__armor");
                }

                _data = root.ToObject<Dictionary<string, Entry>>();
                var status = $"[PLATE] Ammo data loaded from server: {_data?.Count ?? 0} entries, " +
                             $"wound model: {(_wound is { Enabled: true } ? "on" : "off")}, " +
                             $"armor model: {(_armor is { Enabled: true } ? "on" : "off")}";
                Plugin.Log.LogInfo(status);
                Overlay.HitFeed.LogEvent(status);
            }
            catch (Exception ex)
            {
                _fetchFailed = true; // do not hammer the server on every shot; X stays neutral
                Plugin.Log.LogWarning(
                    $"[PLATE] Failed to fetch /plate/ammo-data ({ex.Message}); using neutral X=0.5. " +
                    "Check that the PLATE server component is installed and AmmoNormalizer is enabled.");
            }
        }
    }
}
