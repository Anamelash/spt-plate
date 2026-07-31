using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
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

            /// <summary>Fragmentation chance (vanilla field) — temporary cavity bonus in the channel model.</summary>
            public double Frag { get; set; }

            /// <summary>Share of large fragments (1/grenade FragmentsCount); shrapnel only.</summary>
            public double? LargeShare { get; set; }
        }

        /// <summary>Wound channel model constants (server-side AmmoNormalizer config).</summary>
        internal class WoundParams
        {
            public bool Enabled { get; set; }
            public double GelDepthK { get; set; }
            public double GelStopVelocity { get; set; }
            public double ExpansionDepthFactor { get; set; }
            public double ExpansionAreaFactor { get; set; }
            public double BodyDepthMm { get; set; }
            public double WoundVolumePerHp { get; set; }
            public double TcVelocityCenter { get; set; }
            public double TcVelocityWidth { get; set; }
            public double TcEnergyPerHp { get; set; }
            public double TcFragBonus { get; set; }
            public double EnergyCapPerHp { get; set; }
        }

        /// <summary>Armor material profile (server-side Armor config).</summary>
        internal class ArmorMatProfile
        {
            public double ULimitMult { get; set; } = 1.0;
            public double ECostMult { get; set; } = 0.6;
            public double KDef { get; set; }
            public double KFrag { get; set; }

            /// <summary>Local degradation radius around a hit, mm.</summary>
            public double DAreaMm { get; set; } = 30;

            /// <summary>Share of U_limit remaining in the zone after each hit.</summary>
            public double DegradeMult { get; set; } = 0.8;

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
            public double DurabilityFloor { get; set; } = 0.4;
            public double DegradeFloor { get; set; } = 0.15;
            public double PenConstructionFactor { get; set; } = 0.6;
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

            /// <summary>Class U_limit, J/mm² (a class beyond the table gets the last entry).</summary>
            public double ClassULimit(int armorClass)
            {
                if (ClassULimitJmm2 == null || ClassULimitJmm2.Length == 0)
                {
                    return double.MaxValue; // no data — impenetrable (obvious in tests)
                }

                var idx = armorClass - 1;
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

        private static Dictionary<string, Entry> _data;
        private static WoundParams _wound;
        private static ArmorParams _armor;
        private static bool _fetchFailed;

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

        /// <summary>Cartridge fragmentation chance; 0 when there is no data.</summary>
        public static double GetFrag(string ammoTemplateId)
        {
            EnsureLoaded();
            if (ammoTemplateId != null && _data != null &&
                _data.TryGetValue(ammoTemplateId, out var e))
            {
                return e.Frag;
            }

            return 0;
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
