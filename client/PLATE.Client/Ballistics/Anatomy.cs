using UnityEngine;

namespace PLATE.Client.Ballistics
{
    /// <summary>
    /// Which way round a hitbox is, and which of the boxes sharing a name you actually hit.
    ///
    /// The torso hitboxes are plain boxes, but nothing in the game says which of a box's
    /// three local axes runs across the body and which runs front to back, and it is not
    /// the same axis for every one of them. A convention written down here would put the
    /// liver on the wrong side of half the colliders and there would be no symptom to
    /// notice. So it is resolved from the transforms at the moment of the hit: the local
    /// axis pointing nearest the character's own up is height, the one pointing nearest
    /// their right is width, and what is left is depth. That survives a rig change at
    /// BSG's end without anyone having to shoot a test dummy to find out.
    ///
    /// No EFT types here on purpose — the arithmetic is checkable without a game running.
    /// </summary>
    internal static class Anatomy
    {
        /// <summary>Local axes of a hitbox, named by what they do on a body.</summary>
        internal struct Axes
        {
            /// <summary>Local axis index (0..2) running across the body, shoulder to shoulder.</summary>
            public int Width;

            /// <summary>Local axis index running along the body, hips to head.</summary>
            public int Height;

            /// <summary>Local axis index running through the body, back to chest.</summary>
            public int Depth;

            /// <summary>+1 when that local axis points along the character's own right, -1 when it points the other way.</summary>
            public float WidthSign;

            /// <summary>+1 when that local axis points up.</summary>
            public float HeightSign;

            /// <summary>+1 when that local axis points forward, out of the chest.</summary>
            public float DepthSign;
        }

        /// <summary>
        /// A hitbox measured in the terms the zone rules are written in.
        /// <see cref="SizeLocal"/> is what fractions are measured against (the entry
        /// point arrives in the same units); <see cref="SizeMm"/> is the same box in
        /// millimetres, which is what the thickness and depth rules quote.
        /// </summary>
        internal struct Box
        {
            public bool Valid;
            public Axes Axes;

            /// <summary>Box size in the collider's own units.</summary>
            public Vector3 SizeLocal;

            /// <summary>The same box scaled into millimetres.</summary>
            public Vector3 SizeMm;

            /// <summary>Box centre offset inside the collider's local space.</summary>
            public Vector3 CenterLocal;

            /// <summary>The collider's transform — kept so world points convert through the same one.</summary>
            public Transform Transform;

            public float WidthMm => Valid ? SizeMm[Axes.Width] : 0f;

            public float HeightMm => Valid ? SizeMm[Axes.Height] : 0f;

            public float DepthMm => Valid ? SizeMm[Axes.Depth] : 0f;

            /// <summary>Smallest side, mm — what tells a plate from a volume.</summary>
            public float ThinnestMm =>
                Valid ? Mathf.Min(SizeMm.x, Mathf.Min(SizeMm.y, SizeMm.z)) : 0f;
        }

        /// <summary>
        /// The same box in world space, on an orthonormal frame of its own axes. Zones
        /// are cut out of a box along its width, and a channel is a ray in the world, so
        /// the two have to meet somewhere: here, where a point becomes three millimetre
        /// coordinates and the intersection is a slab test with no scale left in it.
        /// </summary>
        internal struct Frame
        {
            public bool Valid;

            /// <summary>Box centre in world space.</summary>
            public Vector3 Center;

            /// <summary>Unit world direction of width, pointing at the character's own right.</summary>
            public Vector3 Width;

            /// <summary>Unit world direction of height, pointing up.</summary>
            public Vector3 Height;

            /// <summary>Unit world direction of depth, pointing out of the chest.</summary>
            public Vector3 Depth;

            /// <summary>Half sizes along width, height and depth, mm.</summary>
            public Vector3 HalfMm;

            public float WidthMm => HalfMm.x * 2f;

            public float HeightMm => HalfMm.y * 2f;

            public float DepthMm => HalfMm.z * 2f;

            public float ThinnestMm =>
                2f * Mathf.Min(HalfMm.x, Mathf.Min(HalfMm.y, HalfMm.z));
        }

        /// <summary>
        /// Assigns the three local axes to width, height and depth by pointing them at
        /// the character. Height goes first because up is the least ambiguous direction
        /// on a body, then width, and depth takes what is left — so the three are always
        /// distinct even for a box whose axes sit at 45 degrees to everything.
        /// </summary>
        /// <param name="localX">World direction of the box's local X axis.</param>
        /// <param name="localY">World direction of the box's local Y axis.</param>
        /// <param name="localZ">World direction of the box's local Z axis.</param>
        /// <param name="bodyUp">The character's own up.</param>
        /// <param name="bodyRight">The character's own right — the side the liver is on.</param>
        public static Axes Resolve(Vector3 localX, Vector3 localY, Vector3 localZ,
            Vector3 bodyUp, Vector3 bodyRight)
        {
            var axes = new[] { Norm(localX), Norm(localY), Norm(localZ) };
            var taken = new bool[3];
            var up = Norm(bodyUp);
            var right = Norm(bodyRight);

            var a = default(Axes);
            a.Height = Nearest(axes, taken, up, out a.HeightSign);
            a.Width = Nearest(axes, taken, right, out a.WidthSign);
            // derived rather than taken from the transform so it is orthogonal to the
            // other two by construction: forward = right x up in Unity's handedness
            a.Depth = Nearest(axes, taken, Vector3.Cross(right, up), out a.DepthSign);
            return a;
        }

        private static int Nearest(Vector3[] axes, bool[] taken, Vector3 dir, out float sign)
        {
            var best = -1f;
            var idx = 0;
            sign = 1f;

            for (var i = 0; i < 3; i++)
            {
                if (taken[i])
                {
                    continue;
                }

                var dot = Vector3.Dot(axes[i], dir);
                if (Mathf.Abs(dot) <= best)
                {
                    continue;
                }

                best = Mathf.Abs(dot);
                idx = i;
                sign = dot < 0f ? -1f : 1f;
            }

            taken[idx] = true;
            return idx;
        }

        private static Vector3 Norm(Vector3 v)
        {
            return v.sqrMagnitude > 1e-12f ? v.normalized : Vector3.zero;
        }

        /// <summary>
        /// Measures a hitbox. Boxes only: the head is capsules and spheres, and no organ
        /// zone lives in one.
        /// </summary>
        /// <param name="collider">The collider that was hit — the specific box, not the body part.</param>
        /// <param name="body">The character's root transform, which says where up and right are.</param>
        public static bool TryDescribe(Collider collider, Transform body, out Box box)
        {
            box = default;
            if (!(collider is BoxCollider b) || body == null)
            {
                return false;
            }

            var t = b.transform;
            var scale = t.lossyScale;
            box = new Box
            {
                Valid = true,
                Axes = Resolve(t.right, t.up, t.forward, body.up, body.right),
                SizeLocal = b.size,
                SizeMm = new Vector3(
                    Mathf.Abs(b.size.x * scale.x) * 1000f,
                    Mathf.Abs(b.size.y * scale.y) * 1000f,
                    Mathf.Abs(b.size.z * scale.z) * 1000f),
                CenterLocal = b.center,
                Transform = t,
            };
            return true;
        }

        /// <summary>
        /// Where a point sits inside the box, as a signed fraction of each side: 0 at the
        /// centre, ±0.5 on a face. X is width and +X is the character's own right (the
        /// side the liver is on), Y is height with +Y up, Z is depth with +Z out of the
        /// chest. Not clamped — a point that reads past ±0.5 is a bug worth seeing.
        /// </summary>
        public static Vector3 Fractions(in Box box, Vector3 localPoint)
        {
            if (!box.Valid)
            {
                return Vector3.zero;
            }

            var rel = localPoint - box.CenterLocal;
            return new Vector3(
                Frac(rel[box.Axes.Width], box.SizeLocal[box.Axes.Width], box.Axes.WidthSign),
                Frac(rel[box.Axes.Height], box.SizeLocal[box.Axes.Height], box.Axes.HeightSign),
                Frac(rel[box.Axes.Depth], box.SizeLocal[box.Axes.Depth], box.Axes.DepthSign));
        }

        private static float Frac(float rel, float size, float sign)
        {
            return Mathf.Abs(size) < 1e-6f ? 0f : rel * sign / size;
        }

        /// <summary>Same, for a point in world space — converted through the box's own transform.</summary>
        public static Vector3 FractionsWorld(in Box box, Vector3 worldPoint)
        {
            return box.Valid && box.Transform != null
                ? Fractions(box, box.Transform.InverseTransformPoint(worldPoint))
                : Vector3.zero;
        }

        /// <summary>Lifts a measured box into world space as an orthonormal frame.</summary>
        public static bool TryFrame(in Box box, out Frame frame)
        {
            frame = default;
            if (!box.Valid || box.Transform == null)
            {
                return false;
            }

            frame = new Frame
            {
                Valid = true,
                Center = box.Transform.TransformPoint(box.CenterLocal),
                Width = WorldAxis(box.Transform, box.Axes.Width, box.Axes.WidthSign),
                Height = WorldAxis(box.Transform, box.Axes.Height, box.Axes.HeightSign),
                Depth = WorldAxis(box.Transform, box.Axes.Depth, box.Axes.DepthSign),
                HalfMm = new Vector3(box.WidthMm, box.HeightMm, box.DepthMm) * 0.5f,
            };
            return true;
        }

        private static Vector3 WorldAxis(Transform t, int idx, float sign)
        {
            var v = idx == 0 ? t.right : idx == 1 ? t.up : t.forward;
            return v * sign;
        }

        /// <summary>A world point in the frame's own coordinates, mm from the box centre.</summary>
        public static Vector3 ToFrameMm(in Frame frame, Vector3 worldPoint)
        {
            var rel = (worldPoint - frame.Center) * 1000f;
            return new Vector3(Vector3.Dot(rel, frame.Width), Vector3.Dot(rel, frame.Height),
                Vector3.Dot(rel, frame.Depth));
        }

        /// <summary>A world direction in the frame's own axes — still unit length, the frame is orthonormal.</summary>
        public static Vector3 DirInFrame(in Frame frame, Vector3 worldDir)
        {
            var d = Norm(worldDir);
            return new Vector3(Vector3.Dot(d, frame.Width), Vector3.Dot(d, frame.Height),
                Vector3.Dot(d, frame.Depth));
        }

        /// <summary>
        /// Ray against an axis-aligned box, both already in the same frame, everything in
        /// millimetres. The standard slab test: the entry is the last face the ray gets
        /// past and the exit the first one it leaves by, and if those cross over, it
        /// missed. False also when the box is entirely behind the origin — a channel does
        /// not run backwards.
        /// </summary>
        public static bool RayBox(Vector3 origin, Vector3 dir, Vector3 halfMm,
            out float tEnter, out float tExit)
        {
            tEnter = float.NegativeInfinity;
            tExit = float.PositiveInfinity;

            for (var i = 0; i < 3; i++)
            {
                var o = origin[i];
                var d = dir[i];
                var h = halfMm[i];

                if (Mathf.Abs(d) < 1e-6f)
                {
                    if (o < -h || o > h)
                    {
                        return false; // parallel to this slab and outside it
                    }

                    continue;
                }

                var t0 = (-h - o) / d;
                var t1 = (h - o) / d;
                if (t0 > t1)
                {
                    var swap = t0;
                    t0 = t1;
                    t1 = swap;
                }

                if (t0 > tEnter)
                {
                    tEnter = t0;
                }

                if (t1 < tExit)
                {
                    tExit = t1;
                }

                if (tEnter > tExit)
                {
                    return false;
                }
            }

            return tExit > 0f;
        }

        /// <summary>
        /// How close the travelled part of a channel comes to a box, mm — zero when it
        /// goes through it. This is what decides whether a temporary cavity reaches an
        /// organ the bullet itself missed.
        ///
        /// Distance to a convex set is a convex function along a line, so a ternary
        /// search over the segment finds the minimum without a case analysis of the
        /// twenty-six regions around a box, and stays right when the minimum is a flat
        /// stretch rather than a point.
        /// </summary>
        public static float SegmentToBox(Vector3 origin, Vector3 dir, float lengthMm,
            Vector3 halfMm)
        {
            var lo = 0f;
            var hi = Mathf.Max(lengthMm, 0f);

            for (var i = 0; i < 40; i++)
            {
                var third = (hi - lo) / 3f;
                var a = lo + third;
                var b = hi - third;
                if (PointToBox(origin + dir * a, halfMm) < PointToBox(origin + dir * b, halfMm))
                {
                    hi = b;
                }
                else
                {
                    lo = a;
                }
            }

            return PointToBox(origin + dir * (0.5f * (lo + hi)), halfMm);
        }

        /// <summary>Distance from a point to an axis-aligned box, mm. Zero inside it.</summary>
        public static float PointToBox(Vector3 p, Vector3 halfMm)
        {
            var dx = Mathf.Max(Mathf.Abs(p.x) - halfMm.x, 0f);
            var dy = Mathf.Max(Mathf.Abs(p.y) - halfMm.y, 0f);
            var dz = Mathf.Max(Mathf.Abs(p.z) - halfMm.z, 0f);
            return Mathf.Sqrt(dx * dx + dy * dy + dz * dz);
        }

        /// <summary>
        /// Which third of the width a fraction falls in: -1 the character's left, 0 the
        /// middle, +1 their right.
        /// </summary>
        public static int Third(float widthFraction)
        {
            const float edge = 1f / 6f;
            if (widthFraction > edge)
            {
                return 1;
            }

            return widthFraction < -edge ? -1 : 0;
        }

        /// <summary>
        /// A spine collider is only the spine when it is a thin plate.
        ///
        /// SpineTop is two boxes under one name: a 17 mm plate, which is the spine, and a
        /// 0.50 x 0.10 x 0.35 m box, which is the whole upper back with the shoulders.
        /// BodyPartColliderType does not tell them apart, so the thickness does. The
        /// measured plates are 17 and 13 mm and the back box is 100 mm, so the line sits
        /// between them with room on both sides.
        ///
        /// Only ever ask this of a SpineTop or SpineDown collider. It separates two boxes
        /// of the same name; it does not identify a box. There are thinner panels
        /// elsewhere on the torso — RibcageLow has a 28 mm one and SideChestUp an 11 mm
        /// one — and neither is a spine.
        /// </summary>
        public const float SpinePlateMaxMm = 30f;

        public static bool IsSpinePlate(in Box box)
        {
            return box.Valid && box.ThinnestMm < SpinePlateMaxMm;
        }

        public static bool IsSpinePlate(in Frame frame)
        {
            return frame.Valid && frame.ThinnestMm < SpinePlateMaxMm;
        }

        /// <summary>One line for the journal: the box in the resolved order, and which local axis went where.</summary>
        public static string Describe(in Box box)
        {
            if (!box.Valid)
            {
                return "not a box";
            }

            return $"w{box.WidthMm:0} h{box.HeightMm:0} d{box.DepthMm:0} mm " +
                   $"[{AxisName(box.Axes.Width, box.Axes.WidthSign)}" +
                   $"/{AxisName(box.Axes.Height, box.Axes.HeightSign)}" +
                   $"/{AxisName(box.Axes.Depth, box.Axes.DepthSign)}]";
        }

        private static string AxisName(int idx, float sign)
        {
            var axis = idx == 0 ? "x" : idx == 1 ? "y" : "z";
            return (sign < 0f ? "-" : "+") + axis;
        }
    }
}
