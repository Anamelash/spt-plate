using Comfort.Common;
using EFT;
using UnityEngine;

namespace PLATE.Client.Overlay
{
    /// <summary>
    /// Who the person at the keyboard is, for the debug tooling that is specified as
    /// "the player's own shots" and nothing else.
    ///
    /// Read straight off the world rather than through OverlayHud's fight filter: that
    /// one only knows who you are while the overlay COMPONENT is running, and the
    /// overlay is off by default — so a journal or a marker keyed on it would be
    /// silently dead for everyone who did not switch on a debug visualisation they had
    /// no reason to switch on. Same lesson the event journal itself already learned.
    ///
    /// Cached per frame: a burst of automatic fire asks this once per collision and
    /// Singleton lookups are not free.
    /// </summary>
    internal static class LocalPlayerRef
    {
        private static int _frame = -1;
        private static string _profileId;

        /// <summary>The local player's profile id, or null outside a raid.</summary>
        public static string ProfileId
        {
            get
            {
                if (_frame != Time.frameCount)
                {
                    _frame = Time.frameCount;
                    try
                    {
                        _profileId = Singleton<GameWorld>.Instance?.MainPlayer?.ProfileId;
                    }
                    catch
                    {
                        _profileId = null; // world tearing down mid-frame
                    }
                }

                return _profileId;
            }
        }

        /// <summary>Was this shot fired by the local player. False outside a raid.</summary>
        public static bool IsShooter(string profileId)
        {
            var me = ProfileId;
            return me != null && profileId == me;
        }
    }
}
