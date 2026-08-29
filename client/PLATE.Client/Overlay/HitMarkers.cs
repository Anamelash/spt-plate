using System.Collections.Generic;
using UnityEngine;

namespace PLATE.Client.Overlay
{
    /// <summary>
    /// How a marker's world position is turned into a place on the screen for its label.
    ///
    /// There are several plausible answers because EFT does not render through a plain
    /// full-screen camera, and the label went to the wrong place with the obvious one
    /// while the diagnostic insisted everything was drawn on screen. Rather than guess a
    /// fourth time these are all four candidates, switchable in F12 so one raid can say
    /// which is right.
    /// </summary>
    public enum LabelProjection
    {
        /// <summary>WorldToScreenPoint against the full screen height. The obvious one.</summary>
        Screen,

        /// <summary>Same, but measured against the CAMERA's pixel rect rather than the
        /// screen — they differ whenever the camera does not own the whole window.</summary>
        CameraPixels,

        /// <summary>WorldToViewportPoint scaled by the screen. Immune to the camera's
        /// pixel rect entirely, which makes it the safest bet if the rect is the
        /// problem.</summary>
        Viewport,

        /// <summary>Camera.main instead of EFT's own camera, in case the one the mod
        /// picks is not the one drawing the world.</summary>
        MainCamera,
    }

    /// <summary>
    /// Where the bullets actually went, drawn in the world.
    ///
    /// A journal line says a door was penetrated; it does not say which door, or at what
    /// angle, or whether the shot that produced the line is the one you thought you
    /// fired. A cross at the impact point with a ray through it along the line of
    /// arrival says all three at a glance, which is the whole reason this exists: the
    /// obstacle model's raid smoke is a list of questions of the form "did that actually
    /// happen where I think it did".
    ///
    /// Geometry is a shared line mesh instanced through transforms, NOT
    /// GameObject.CreatePrimitive: a primitive comes with a collider, and a collider in
    /// front of the muzzle would catch the very bullets this is here to observe. The
    /// trap is closed by construction rather than by remembering to disable something.
    ///
    /// A marker on a person is stuck to the bone that was hit and carries its position
    /// AND its orientation, so it travels and turns with the body — including into the
    /// ragdoll — rather than hanging in the air where the target used to be.
    ///
    /// Text is drawn by OverlayHud in OnGUI, projected to the screen and pinned to the
    /// point — unlike the floating labels, which drift upward and belong to a victim
    /// rather than to a place.
    /// </summary>
    internal static class HitMarkers
    {
        /// <summary>Half-span of the impact cross before the config scale, m.</summary>
        private const float BaseCrossM = 0.03f;

        /// <summary>
        /// How far the trajectory ray reaches on EACH side of the impact point before
        /// the config scale, m. The line runs through the point rather than only back
        /// from it: a marker on the far face of a wall, or on the far side of a body, is
        /// otherwise invisible from where you are standing, which is exactly where you
        /// are standing when you want to read it.
        /// </summary>
        private const float BaseRayM = 0.75f;

        /// <summary>Point size of the label before the config scale.</summary>
        public const int BaseFontSize = 13;

        internal class Marker
        {
            /// <summary>
            /// What the marker hangs on: the bone that was hit, or null for a hit on
            /// something that does not move. Followed rather than parented to — a bone
            /// can carry a non-unit scale, which would distort the cross, and the rig
            /// being destroyed would take a pooled object with it.
            /// </summary>
            public Transform Anchor;

            /// <summary>Impact point in the anchor's space; meaningless without one.</summary>
            public Vector3 LocalPos;

            /// <summary>
            /// Orientation in the anchor's space. Kept so the marker TURNS with the limb
            /// it is stuck in and not merely travels with it: a cross that holds its
            /// original world orientation while the body it marks rolls over ends up
            /// pointing through the model instead of along the wound.
            /// </summary>
            public Quaternion LocalRot;

            /// <summary>Last known world orientation, for when the anchor goes away.</summary>
            public Quaternion LastRot;

            /// <summary>
            /// Last known world position. Kept up to date while the marker is following
            /// something, so that when the thing it was following finally goes away the
            /// marker stays where the body fell rather than jumping.
            /// </summary>
            public Vector3 LastWorld;

            public Vector3 DirIn;
            public string Text;
            public Color Color;
            public float BornAt;
            public bool Live;

            public GameObject Root;
            public Transform Cross;
            public Transform Ray;

            /// <summary>
            /// Where the marker is right now. The label reads this and not a stored
            /// point, so the text can never drift away from the cross it belongs to.
            /// Falls back to where it happened rather than to the bone-local offset,
            /// which as a world coordinate would be a metre from the map's origin.
            /// </summary>
            public Vector3 WorldPos =>
                Anchor != null ? Anchor.TransformPoint(LocalPos) : LastWorld;

            /// <summary>Which way the marker faces right now.</summary>
            public Quaternion WorldRot =>
                Anchor != null ? Anchor.rotation * LocalRot : LastRot;

            /// <summary>The rig this was hanging on is gone (body despawned).</summary>
            public bool Orphaned => Anchored && Anchor == null;

            /// <summary>Was it ever attached to something — distinguishes "hit a wall"
            /// from "hit a body that has since been destroyed".</summary>
            public bool Anchored;
        }

        private static readonly List<Marker> Ring = new List<Marker>();
        private static int _next;
        private static Transform _parent;

        private static Mesh _crossMesh;
        private static Mesh _rayMesh;
        private static Material _material;
        private static bool _geometryFailed;

        /// <summary>Everything currently alive, for the label pass.</summary>
        public static IEnumerable<Marker> Live
        {
            get
            {
                for (var i = 0; i < Ring.Count; i++)
                {
                    if (Ring[i].Live)
                    {
                        yield return Ring[i];
                    }
                }
            }
        }

        // --- Colors. A debug palette, not a physical constant: the existing overlay
        // sets the precedent that these are code constants rather than config. ---

        public static readonly Color BodyPenetrated = Color.white;
        public static readonly Color BodyBlocked = new Color(0.75f, 0.75f, 0.75f);
        public static readonly Color WallPenetrated = new Color(0.35f, 0.95f, 0.35f);
        public static readonly Color WallStopped = new Color(0.95f, 0.3f, 0.3f);
        public static readonly Color WallRicochet = new Color(0.98f, 0.85f, 0.25f);

        /// <summary>
        /// The far face of a crossing already paid for on the way in. A colour of its
        /// own rather than a dimmed green: the collision is real and worth seeing — it is
        /// where the projectile left the object — but it cost nothing, and drawn in the
        /// penetration green next to its own entry marker it made one shot through a door
        /// look like two charges six centimetres apart. That reading is what sent an
        /// investigation after a bug the doors did not have, so this one has to be
        /// unmistakable at a glance and not merely darker. The label carries an `F` for
        /// the same reason — colour alone is a poor thing to read a number against.
        /// </summary>
        public static readonly Color WallFreeExit = new Color(0.35f, 0.8f, 1f);

        /// <summary>
        /// Records a hit. Cheap enough for a shotgun pattern — eight or nine of these
        /// land in one frame and the ring buffer reuses the oldest rather than growing.
        /// </summary>
        /// <param name="anchor">The bone to hang on, for a hit on something that moves.
        /// Null pins the marker to the world, which is right for a wall and wrong for a
        /// person.</param>
        public static void Add(Vector3 pos, Vector3 dirIn, string text, Color color,
            Transform anchor = null)
        {
            if (!PlateClientConfig.MarkersEnabled.Value)
            {
                return;
            }

            // The world origin is under the floor of every map, and a hit reported there
            // is a hit with no geometry behind it — a blast, or damage the mod applied
            // itself. Marking it would put a label at the bottom of the level.
            if (pos.sqrMagnitude < 1e-6f)
            {
                return;
            }

            var t = PerfTrace.Begin();
            try
            {
                var capacity = Mathf.Max(1, PlateClientConfig.MarkerBuffer.Value);
                Resize(capacity);

                var m = Ring[_next % Ring.Count];
                _next = (_next + 1) % Ring.Count;

                var dir = dirIn.sqrMagnitude > 1e-8f ? dirIn.normalized : Vector3.forward;

                // the ray runs back along the line of arrival, so it points at where the
                // shot came from; the mesh spans both ways, so the sign is cosmetic
                var rot = Quaternion.LookRotation(-dir);

                m.Anchor = anchor;
                m.Anchored = anchor != null;
                m.LastWorld = pos;
                m.LastRot = rot;
                m.LocalPos = anchor != null ? anchor.InverseTransformPoint(pos) : pos;
                m.LocalRot = anchor != null ? Quaternion.Inverse(anchor.rotation) * rot : rot;
                m.DirIn = dir;
                m.Text = text;
                m.Color = color;
                m.BornAt = Time.time;
                m.Live = true;

                Place(m);
            }
            finally
            {
                PerfTrace.End("overlay.markers", t);
            }
        }

        /// <summary>
        /// Expires what has run out, and carries what is still alive along with the body
        /// it is stuck in. Driven from OverlayHud's frame.
        ///
        /// Following per frame rather than parenting: a bone can carry a non-unit scale,
        /// which would stretch the cross, and destroying the rig would take a pooled
        /// object down with it.
        /// </summary>
        public static void Tick(float now)
        {
            var ttl = PlateClientConfig.MarkerTtlSec.Value;

            for (var i = 0; i < Ring.Count; i++)
            {
                var m = Ring[i];
                if (!m.Live)
                {
                    continue;
                }

                if (ttl > 0f && now - m.BornAt > ttl) // 0 = keep for the whole raid
                {
                    m.Live = false;
                    m.Anchor = null;
                    if (m.Root != null)
                    {
                        m.Root.SetActive(false);
                    }

                    continue;
                }

                // Losing what it was stuck in does NOT retire the marker. Walking up to
                // a body afterwards to see where you hit it is the point of the thing,
                // and a corpse is a ragdoll on the same rig, so normally the marker just
                // keeps following. When the rig really does go — the body despawns — the
                // marker stays at the last place it was, which is where the body fell.
                if (m.Orphaned)
                {
                    m.Anchor = null;
                    continue;
                }

                if (m.Anchor != null)
                {
                    m.LastWorld = m.WorldPos;
                    m.LastRot = m.WorldRot;
                    if (m.Root != null)
                    {
                        m.Root.transform.position = m.LastWorld;
                        m.Root.transform.rotation = m.LastRot;
                    }
                }
            }
        }

        /// <summary>
        /// Re-applies the size settings to everything already on screen. Driven by
        /// ConfigFile.SettingChanged rather than by re-reading every frame — the same
        /// rule the blood HUD follows.
        /// </summary>
        public static void ApplyLayout()
        {
            for (var i = 0; i < Ring.Count; i++)
            {
                if (Ring[i].Live)
                {
                    Place(Ring[i]);
                }
            }
        }

        /// <summary>Raid teardown: hide everything and drop the pool.</summary>
        public static void Clear()
        {
            for (var i = 0; i < Ring.Count; i++)
            {
                if (Ring[i].Root != null)
                {
                    Object.Destroy(Ring[i].Root);
                }
            }

            Ring.Clear();
            _next = 0;
            if (_parent != null)
            {
                Object.Destroy(_parent.gameObject);
                _parent = null;
            }
        }

        // --- Pool ---

        private static void Resize(int capacity)
        {
            if (Ring.Count == capacity)
            {
                return;
            }

            // a changed buffer size rebuilds the pool rather than leaking the old one
            if (Ring.Count > capacity)
            {
                for (var i = capacity; i < Ring.Count; i++)
                {
                    if (Ring[i].Root != null)
                    {
                        Object.Destroy(Ring[i].Root);
                    }
                }

                Ring.RemoveRange(capacity, Ring.Count - capacity);
                _next = 0;
                return;
            }

            while (Ring.Count < capacity)
            {
                Ring.Add(new Marker());
            }
        }

        private static void Place(Marker m)
        {
            EnsureBody(m);
            if (m.Root == null)
            {
                return; // no shader in this build — text and journal carry on alone
            }

            var cross = BaseCrossM * PlateClientConfig.MarkerPointScale.Value;
            var ray = BaseRayM * PlateClientConfig.MarkerRayScale.Value;

            m.Root.SetActive(true);
            m.Root.transform.position = m.WorldPos;

            // the whole marker carries the orientation, so the cross turns with the
            // limb too — it is the thing that shows which way the surface was facing
            m.Root.transform.rotation = m.WorldRot;

            m.Cross.localRotation = Quaternion.identity;
            m.Cross.localScale = new Vector3(cross, cross, cross);

            m.Ray.localRotation = Quaternion.identity;
            m.Ray.localScale = new Vector3(1f, 1f, ray);

            SetColor(m);
        }

        private static void SetColor(Marker m)
        {
            var cr = m.Cross.GetComponent<MeshRenderer>();
            var rr = m.Ray.GetComponent<MeshRenderer>();
            if (cr != null)
            {
                cr.material.color = m.Color;
            }

            if (rr != null)
            {
                // the ray is the same colour, dimmed: it is context, the point is the datum
                rr.material.color = new Color(m.Color.r, m.Color.g, m.Color.b, 0.5f);
            }
        }

        private static void EnsureBody(Marker m)
        {
            if (m.Root != null || _geometryFailed)
            {
                return;
            }

            if (!EnsureShared())
            {
                return;
            }

            m.Root = new GameObject("PLATE.HitMarker");
            m.Root.transform.SetParent(_parent, worldPositionStays: false);

            m.Cross = NewPiece("cross", _crossMesh, m.Root.transform);
            m.Ray = NewPiece("ray", _rayMesh, m.Root.transform);
        }

        private static Transform NewPiece(string name, Mesh mesh, Transform parent)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, worldPositionStays: false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.material = new Material(_material);
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            r.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            return go.transform;
        }

        private static bool EnsureShared()
        {
            if (_material != null)
            {
                // the meshes and the material survive a raid; the grouping object does
                // not, because Clear destroys it — rebuild it rather than parenting the
                // next raid's markers to nothing
                if (_parent == null)
                {
                    _parent = new GameObject("PLATE.HitMarkers").transform;
                    Object.DontDestroyOnLoad(_parent.gameObject);
                }

                return true;
            }

            var shader = Shader.Find("Sprites/Default");
            if (shader == null)
            {
                _geometryFailed = true;
                Plugin.Log.LogWarning(
                    "[PLATE] Hit markers: no 'Sprites/Default' shader in this build — " +
                    "marker geometry is disabled. The labels and the journal still work.");
                return false;
            }

            _material = new Material(shader);
            _parent = new GameObject("PLATE.HitMarkers").transform;
            Object.DontDestroyOnLoad(_parent.gameObject);

            // A cross of three orthogonal lines: readable from any angle, which a
            // billboard or a flat quad is not, and with no volume to intersect anything.
            _crossMesh = new Mesh { name = "PLATE.MarkerCross" };
            _crossMesh.SetVertices(new List<Vector3>
            {
                new Vector3(-1f, 0f, 0f), new Vector3(1f, 0f, 0f),
                new Vector3(0f, -1f, 0f), new Vector3(0f, 1f, 0f),
                new Vector3(0f, 0f, -1f), new Vector3(0f, 0f, 1f),
            });
            _crossMesh.SetIndices(new[] { 0, 1, 2, 3, 4, 5 }, MeshTopology.Lines, 0);

            // through the point, not out of it — see BaseRayM
            _rayMesh = new Mesh { name = "PLATE.MarkerRay" };
            _rayMesh.SetVertices(new List<Vector3>
                { new Vector3(0f, 0f, -1f), new Vector3(0f, 0f, 1f) });
            _rayMesh.SetIndices(new[] { 0, 1 }, MeshTopology.Lines, 0);

            return true;
        }
    }
}
