using System.Collections.Generic;
using UnityEngine;

namespace CathedralOfFeedback
{
    /// <summary>
    /// The building, as maths. A 16-sided chapter house: 9 m radius, 7 m walls, a pointed
    /// (gothic) vault to ~19.7 m. Everything is described in "field coordinates" as seen from
    /// a point at eye height in the middle of the floor:
    ///   u = azimuth, 16 units per turn (one unit per kaleidoscope wedge, mirror-wrapped),
    ///   v = polar angle from straight up, normalised so the foot of the wall is 1.
    /// Meshes are generated at runtime; nothing here is an imported asset.
    /// </summary>
    public static class Nave
    {
        public const float Radius = 9f, WallHeight = 7f, ArchRadius = 13.5f, EyeHeight = 1.6f;
        public const int Wedges = 16;
        public const float WallFrac = 0.3f;          // share of the profile parameter spent on the wall
        public const float ZoneMid = 0.34f, ZoneTop = 0.14f;

        public static readonly float ArchSpan = Mathf.Acos((ArchRadius - Radius) / ArchRadius);
        public static readonly float ThetaMax = Mathf.PI * 0.5f + Mathf.Atan2(EyeHeight, Radius);
        public static readonly float ZoneSpring = VOf(Radius, WallHeight);
        public static float ApexHeight => WallHeight + ArchRadius * Mathf.Sin(ArchSpan);

        /// <summary>Profile of the shell. s: 0 foot of the wall, WallFrac springing line, 1 apex.</summary>
        public static void Profile(float s, out Vector2 p, out Vector2 inward, out Vector2 tangent)
        {
            if (s <= WallFrac)
            {
                p = new Vector2(Radius, WallHeight * s / WallFrac);
                inward = new Vector2(-1f, 0f); tangent = new Vector2(0f, 1f);
            }
            else
            {
                float t = (s - WallFrac) / (1f - WallFrac) * ArchSpan;
                float c = Mathf.Cos(t), sn = Mathf.Sin(t);
                p = new Vector2(Radius - ArchRadius + ArchRadius * c, WallHeight + ArchRadius * sn);
                inward = new Vector2(-c, -sn); tangent = new Vector2(-sn, c);
            }
        }

        public static float VOf(float radius, float y) => Mathf.Atan2(radius, y - EyeHeight) / ThetaMax;

        /// <summary>Profile parameter whose point is seen at field height v.</summary>
        public static float SForV(float v)
        {
            float lo = 0f, hi = 1f;
            for (int i = 0; i < 32; i++)
            {
                float mid = (lo + hi) * 0.5f;
                Profile(mid, out var p, out _, out _);
                if (VOf(p.x, p.y) > v) lo = mid; else hi = mid;
            }
            return (lo + hi) * 0.5f;
        }

        /// <summary>World direction (from the viewer) to field coordinates, u folded into one wedge.</summary>
        public static Vector2 DirToField(Vector3 d)
        {
            float theta = Mathf.Acos(Mathf.Clamp(d.y, -1f, 1f));
            float a = Mathf.Atan2(d.z, d.x) / (2f * Mathf.PI) * Wedges;
            float m = Mathf.Repeat(a, 2f);
            return new Vector2(m <= 1f ? m : 2f - m, Mathf.Clamp01(theta / ThetaMax));
        }

        static Vector3 Around(Vector2 rz, float phi) => new Vector3(rz.x * Mathf.Cos(phi), rz.y, rz.x * Mathf.Sin(phi));

        // ---------------------------------------------------------------- shell
        public static Mesh BuildShell(int segs = 96, int wallRings = 10, int archRings = 54)
        {
            var ss = new List<float>();
            for (int i = 0; i <= wallRings; i++) ss.Add(WallFrac * i / wallRings);
            for (int j = 1; j <= archRings; j++) ss.Add(WallFrac + (1f - WallFrac) * j / archRings);

            int rings = ss.Count, cols = segs + 1;
            var v = new Vector3[rings * cols]; var n = new Vector3[rings * cols]; var uv = new Vector2[rings * cols];
            for (int r = 0; r < rings; r++)
            {
                Profile(ss[r], out var p, out var inw, out _);
                float fv = VOf(p.x, p.y);
                for (int c = 0; c < cols; c++)
                {
                    float phi = 2f * Mathf.PI * c / segs;
                    int k = r * cols + c;
                    v[k] = Around(p, phi); n[k] = Around(inw, phi);
                    uv[k] = new Vector2((float)Wedges * c / segs, fv);
                }
            }
            var t = new int[(rings - 1) * segs * 6];
            int q = 0;
            for (int r = 0; r < rings - 1; r++)
                for (int c = 0; c < segs; c++)
                {
                    int a = r * cols + c, b = a + 1, d = a + cols, e = d + 1;
                    t[q++] = a; t[q++] = d; t[q++] = b; t[q++] = b; t[q++] = d; t[q++] = e;
                }
            return Finish("NaveShell", v, n, uv, null, t);
        }

        // ---------------------------------------------------------------- floor
        public static Mesh BuildFloor(int segs = 64)
        {
            float r = Radius + 0.3f;
            var v = new Vector3[segs + 1]; var n = new Vector3[segs + 1]; var uv = new Vector2[segs + 1];
            v[0] = Vector3.zero; n[0] = Vector3.up;
            for (int i = 0; i < segs; i++)
            {
                float a = 2f * Mathf.PI * i / segs;
                v[i + 1] = new Vector3(Mathf.Cos(a) * r, 0f, Mathf.Sin(a) * r); n[i + 1] = Vector3.up;
            }
            var t = new int[segs * 3];
            for (int i = 0; i < segs; i++) { t[i * 3] = 0; t[i * 3 + 1] = 1 + (i + 1) % segs; t[i * 3 + 2] = 1 + i; }
            return Finish("NaveFloor", v, n, uv, null, t);
        }

        // ---------------------------------------------------------------- stone
        sealed class Buf
        {
            public readonly List<Vector3> v = new List<Vector3>(), n = new List<Vector3>();
            public readonly List<Vector2> uv = new List<Vector2>(), uv2 = new List<Vector2>();
            public readonly List<int> t = new List<int>();

            /// <summary>Flat-shaded quad a-b-c-d. sX = "along" (0 floor .. 1 apex) for the beat pulse.</summary>
            public void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, float sa, float sb, float sc, float sd, float emissive)
            {
                Vector3 nn = Vector3.Cross(b - a, d - a);
                if (nn.sqrMagnitude < 1e-12f) nn = Vector3.Cross(c - b, a - b);
                nn.Normalize();
                Vector3 ctr = (a + b + c + d) * 0.25f;
                float refA = Mathf.Atan2(ctr.z, ctr.x) / (2f * Mathf.PI) * Wedges;
                int i0 = v.Count;
                Add(a, nn, sa, emissive, refA); Add(b, nn, sb, emissive, refA);
                Add(c, nn, sc, emissive, refA); Add(d, nn, sd, emissive, refA);
                t.Add(i0); t.Add(i0 + 1); t.Add(i0 + 2); t.Add(i0); t.Add(i0 + 2); t.Add(i0 + 3);
            }

            void Add(Vector3 p, Vector3 nn, float s, float e, float refA)
            {
                v.Add(p); n.Add(nn);
                float a = Mathf.Atan2(p.z, p.x) / (2f * Mathf.PI) * Wedges;
                a += Wedges * Mathf.Round((refA - a) / Wedges);        // keep the quad on one side of the atan2 seam
                uv.Add(new Vector2(a, VOf(new Vector2(p.x, p.z).magnitude, p.y)));
                uv2.Add(new Vector2(s, e));
            }
        }

        /// <summary>A rib following the shell from s0 to s1 at azimuth phi.</summary>
        static void Rib(Buf m, float phi, float width, float depth, float s0, float s1, int steps)
        {
            Vector3 T = new Vector3(-Mathf.Sin(phi), 0f, Mathf.Cos(phi));
            Vector3 pa = default, pb = default, pc = default, pd = default; float ps = 0f;
            for (int i = 0; i <= steps; i++)
            {
                float s = Mathf.Lerp(s0, s1, i / (float)steps);
                Profile(s, out var p, out var inw, out _);
                Vector3 P = Around(p, phi), N = Around(inw, phi);
                float taper = Mathf.Lerp(1f, 0.45f, Mathf.InverseLerp(WallFrac, 1f, s));
                float w = width * taper, dp = depth * taper;
                Vector3 a = P - T * (w * 0.5f), b = P + T * (w * 0.5f);
                Vector3 c = P + T * (w * 0.28f) + N * dp, d = P - T * (w * 0.28f) + N * dp;
                if (i > 0)
                {
                    m.Quad(pa, a, d, pd, ps, s, s, ps, 0f);
                    m.Quad(pd, d, c, pc, ps, s, s, ps, 0f);
                    m.Quad(pc, c, b, pb, ps, s, s, ps, 0f);
                }
                pa = a; pb = b; pc = c; pd = d; ps = s;
            }
        }

        /// <summary>A horizontal moulding running all the way round at profile parameter s.</summary>
        static void Ring(Buf m, float s, float height, float depth, int segs, float emissive)
        {
            Profile(s, out var p, out var inw, out var tan);
            Vector3 pa = default, pb = default, pc = default, pd = default;
            for (int j = 0; j <= segs; j++)
            {
                float phi = 2f * Mathf.PI * j / segs;
                Vector3 P = Around(p, phi), N = Around(inw, phi), U = Around(tan, phi);
                Vector3 a = P - U * (height * 0.5f), b = P + U * (height * 0.5f);
                Vector3 c = P + U * (height * 0.3f) + N * depth, d = P - U * (height * 0.3f) + N * depth;
                if (j > 0)
                {
                    m.Quad(pa, a, d, pd, s, s, s, s, emissive);
                    m.Quad(pd, d, c, pc, s, s, s, s, emissive);
                    m.Quad(pc, c, b, pb, s, s, s, s, emissive);
                }
                pa = a; pb = b; pc = c; pd = d;
            }
        }

        /// <summary>Swept n-gon tube along a planar path. side = normal of the path's plane.</summary>
        static void Tube(Buf m, IList<Vector3> path, IList<float> radius, IList<float> along, Vector3 side, int sides, float emissive, bool closed)
        {
            int n = path.Count;
            var prev = new Vector3[sides + 1]; var cur = new Vector3[sides + 1];
            int count = closed ? n + 1 : n;
            for (int i = 0; i < count; i++)
            {
                int k = i % n;
                Vector3 a = path[closed ? (k - 1 + n) % n : Mathf.Max(k - 1, 0)];
                Vector3 b = path[closed ? (k + 1) % n : Mathf.Min(k + 1, n - 1)];
                Vector3 tan = (b - a).normalized;
                Vector3 nrm = Vector3.Cross(tan, side).normalized;
                for (int j = 0; j <= sides; j++)
                {
                    float ang = 2f * Mathf.PI * j / sides;
                    cur[j] = path[k] + (nrm * Mathf.Cos(ang) + side * Mathf.Sin(ang)) * radius[k];
                }
                if (i > 0)
                {
                    int pk = (i - 1) % n;
                    for (int j = 0; j < sides; j++)
                        m.Quad(prev[j], cur[j], cur[j + 1], prev[j + 1], along[pk], along[k], along[k], along[pk], emissive);
                }
                var tmp = prev; prev = cur; cur = tmp;
            }
        }

        /// <summary>A limb that leaves <paramref name="from"/> going up, then bends over to <paramref name="to"/>.</summary>
        static void Branch(Buf m, Vector3 from, Vector3 to, float r0, float r1, float along0, float along1)
        {
            Vector3 span = to - from;
            Vector3 side = Vector3.Cross(Vector3.up, span).normalized;
            Vector3 ctrl = from + Vector3.up * (span.y * 0.85f) + new Vector3(span.x, 0f, span.z) * 0.12f;
            var path = new List<Vector3>(); var rad = new List<float>(); var al = new List<float>();
            const int steps = 12;
            for (int i = 0; i <= steps; i++)
            {
                float u = i / (float)steps;
                path.Add((1 - u) * (1 - u) * from + 2 * (1 - u) * u * ctrl + u * u * to);
                rad.Add(Mathf.Lerp(r0, r1, u)); al.Add(Mathf.Lerp(along0, along1, u));
            }
            Tube(m, path, rad, al, side, 6, 0f, false);
        }

        public const float CanopyRadius = 4.6f, LanternRadius = 1.25f, LanternHeight = 10.6f;

        public static Mesh BuildStone()
        {
            var m = new Buf();

            // 8 primary ribs on the kaleidoscope's mirror lines, 8 lighter ones between them.
            for (int k = 0; k < Wedges; k++)
            {
                float phi = 2f * Mathf.PI * k / Wedges;
                bool primary = (k & 1) == 0;
                Rib(m, phi, primary ? 0.42f : 0.22f, primary ? 0.40f : 0.20f, 0f, 0.985f, 72);
            }

            // Mouldings where the pane density changes, plus a plinth and the apex boss.
            Ring(m, 0.006f, 0.35f, 0.18f, 96, 0f);
            Ring(m, WallFrac, 0.34f, 0.30f, 96, 0f);
            Ring(m, SForV(ZoneMid), 0.26f, 0.24f, 96, 0f);
            Ring(m, SForV(ZoneTop), 0.20f, 0.20f, 64, 0f);
            Ring(m, 0.985f, 0.16f, 0.16f, 32, 0.6f);

            // Canopy: eight columns that lean in to carry a glowing lantern ring above the player.
            for (int k = 0; k < 8; k++)
            {
                float phi = 2f * Mathf.PI * (k + 0.5f) / 8f;
                Vector3 T = new Vector3(-Mathf.Sin(phi), 0f, Mathf.Cos(phi));
                var path = new List<Vector3>(); var rad = new List<float>(); var al = new List<float>();
                void P(float r, float y, float rr) { path.Add(Around(new Vector2(r, y), phi)); rad.Add(rr); al.Add(Mathf.Clamp01(y / LanternHeight) * 0.6f); }
                P(CanopyRadius, 0f, 0.36f); P(CanopyRadius, 0.30f, 0.36f); P(CanopyRadius, 0.36f, 0.19f);
                for (int i = 1; i <= 8; i++) P(CanopyRadius, 0.36f + (5.9f - 0.36f) * i / 8f, 0.19f - 0.02f * i / 8f);
                P(CanopyRadius, 5.96f, 0.30f); P(CanopyRadius, 6.22f, 0.32f); P(CanopyRadius, 6.28f, 0.15f);
                Vector2 b0 = new Vector2(CanopyRadius, 6.28f), b1 = new Vector2(CanopyRadius, 9.9f), b2 = new Vector2(LanternRadius, LanternHeight);
                for (int i = 1; i <= 14; i++)
                {
                    float u = i / 14f;
                    Vector2 q = (1 - u) * (1 - u) * b0 + 2 * (1 - u) * u * b1 + u * u * b2;
                    P(q.x, q.y, Mathf.Lerp(0.15f, 0.09f, u));
                }
                Tube(m, path, rad, al, T, 8, 0f, false);

                // Like Gaudí's trees, each column also branches outward: one limb to the vault
                // behind it and one to each neighbouring primary rib, where it meets the next
                // column's limb in a pointed arch.
                Vector3 fork = Around(new Vector2(CanopyRadius, 6.28f), phi);
                for (int b = -1; b <= 1; b++)
                {
                    float s = b == 0 ? 0.36f : 0.44f;
                    Profile(s, out var sp, out var inw, out _);
                    float tphi = phi + b * Mathf.PI / 8f;
                    Vector3 target = Around(sp, tphi) + Around(inw, tphi) * 0.15f;
                    Branch(m, fork, target, 0.13f, 0.07f, 0.36f, s);
                }
            }
            {
                var path = new List<Vector3>(); var rad = new List<float>(); var al = new List<float>();
                for (int i = 0; i < 48; i++)
                {
                    path.Add(Around(new Vector2(LanternRadius, LanternHeight), 2f * Mathf.PI * i / 48f));
                    rad.Add(0.11f); al.Add(0.6f);
                }
                Tube(m, path, rad, al, Vector3.up, 8, 1f, true);
            }

            return Finish("NaveStone", m.v.ToArray(), m.n.ToArray(), m.uv.ToArray(), m.uv2.ToArray(), m.t.ToArray());
        }

        // ---------------------------------------------------------------- light shafts
        /// <summary>
        /// One open prism per lancet window, slanting inward and down like sunlight. They stop
        /// short of the canopy columns, so a player near the middle is never inside one.
        /// </summary>
        public static Mesh BuildShafts(float length = 4.4f, float yLow = 0.95f, float yHigh = 5.0f)
        {
            var v = new List<Vector3>(); var n = new List<Vector3>(); var uv = new List<Vector2>(); var uv2 = new List<Vector2>(); var t = new List<int>();
            float halfAz = 0.66f * 0.5f * (2f * Mathf.PI / Wedges) * 0.86f;
            for (int k = 0; k < Wedges; k++)
            {
                float phi = 2f * Mathf.PI * (k + 0.5f) / Wedges;
                Vector3 radial = new Vector3(Mathf.Cos(phi), 0f, Mathf.Sin(phi));
                Vector3 dir = (-radial + Vector3.down * 0.5f).normalized;
                // Window corners, counter-clockwise, and the field coordinates each one starts from.
                var corner = new Vector3[4]; var f = new Vector2[4];
                for (int c = 0; c < 4; c++)
                {
                    float a = phi + ((c == 0 || c == 3) ? -halfAz : halfAz);
                    float y = c < 2 ? yLow : yHigh;
                    corner[c] = new Vector3(Mathf.Cos(a) * (Radius - 0.06f), y, Mathf.Sin(a) * (Radius - 0.06f));
                    f[c] = new Vector2(a / (2f * Mathf.PI) * Wedges, VOf(Radius, y));
                }
                Vector3 mid = (corner[0] + corner[1] + corner[2] + corner[3]) * 0.25f;
                for (int c = 0; c < 4; c++)
                {
                    int d = (c + 1) % 4;
                    Vector3 tc = mid + (corner[c] - mid) * 1.2f + dir * length, td = mid + (corner[d] - mid) * 1.2f + dir * length;
                    int i0 = v.Count;
                    v.Add(corner[c]); v.Add(corner[d]); v.Add(td); v.Add(tc);
                    uv.Add(new Vector2(0f, 0f)); uv.Add(new Vector2(0f, 1f)); uv.Add(new Vector2(1f, 1f)); uv.Add(new Vector2(1f, 0f));
                    uv2.Add(f[c]); uv2.Add(f[d]); uv2.Add(f[d]); uv2.Add(f[c]);
                    Vector3 face = Vector3.Cross(corner[d] - corner[c], tc - corner[c]).normalized;
                    for (int q = 0; q < 4; q++) n.Add(face);
                    t.Add(i0); t.Add(i0 + 1); t.Add(i0 + 2); t.Add(i0); t.Add(i0 + 2); t.Add(i0 + 3);
                }
            }
            return Finish("NaveShafts", v.ToArray(), n.ToArray(), uv.ToArray(), uv2.ToArray(), t.ToArray());
        }

        static Mesh Finish(string name, Vector3[] v, Vector3[] n, Vector2[] uv, Vector2[] uv2, int[] t)
        {
            var mesh = new Mesh { name = name, hideFlags = HideFlags.DontSave };
            mesh.indexFormat = v.Length > 65000 ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16;
            mesh.vertices = v; mesh.normals = n; mesh.uv = uv;
            if (uv2 != null) mesh.uv2 = uv2;
            mesh.triangles = t;
            mesh.RecalculateBounds();
            mesh.UploadMeshData(true);
            return mesh;
        }
    }
}
