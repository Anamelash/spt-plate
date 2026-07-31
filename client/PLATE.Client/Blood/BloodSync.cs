using System;
using Newtonsoft.Json;
using SPT.Common.Http;
using UnityEngine;

namespace PLATE.Client.Blood
{
    /// <summary>
    /// Blood volume exchange with the server (/plate/blood-get, /plate/blood-set).
    /// Persistence between raids + out-of-raid regeneration (computed by the server).
    /// </summary>
    internal static class BloodSync
    {
        internal class BloodRec
        {
            public double Cur { get; set; }
            public double Max { get; set; }
        }

        private static BloodRec _cached;
        private static float _cachedAt = -999f;

        /// <summary>Value from the profile (10 s cache). null = no record yet (full volume).</summary>
        public static BloodRec GetCached()
        {
            if (Time.realtimeSinceStartup - _cachedAt < 10f)
            {
                return _cached;
            }

            try
            {
                var json = RequestHandler.GetJson("/plate/blood-get");
                var rec = JsonConvert.DeserializeObject<BloodRec>(json);
                _cached = rec != null && rec.Max > 0 ? rec : null;
                _cachedAt = Time.realtimeSinceStartup;
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"[PLATE] blood-get failed: {ex.Message}");
                _cachedAt = Time.realtimeSinceStartup; // do not hammer the server every frame
            }

            return _cached;
        }

        /// <summary>Charge a blood pack use to the profile (after it has been applied).</summary>
        public static void PushItemUse(string itemId)
        {
            try
            {
                RequestHandler.PostJson("/plate/item-use",
                    JsonConvert.SerializeObject(new { id = itemId }));
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"[PLATE] item-use push failed: {ex.Message}");
            }
        }

        public static void Push(double cur, double max, bool died)
        {
            try
            {
                var body = JsonConvert.SerializeObject(new { cur, max, died });
                RequestHandler.PostJson("/plate/blood-set", body);
                _cached = new BloodRec { Cur = died ? max : cur, Max = max };
                _cachedAt = Time.realtimeSinceStartup;
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"[PLATE] blood-set failed: {ex.Message}");
            }
        }
    }
}
