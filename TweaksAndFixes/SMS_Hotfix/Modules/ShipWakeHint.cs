using System;
using System.Collections;
using System.Collections.Generic;
using Il2Cpp;
using TAFHotfixBatch1;
using UnityEngine;

namespace TweaksAndFixes
{
    /// <summary>
    /// [SMS.2.WAKE] 船尾航迹（仅 oceanType=1 简化 / 2 空水）。
    ///
    /// 定位：Hull(Part) 的 transform（船体模型，不是判定框）；宽度/长度取 hullSize.size。
    /// 点位组合见 ship_wake_points（1..9）。
    /// 渲染：**每船 1 个 GameObject + 1 个 Mesh**（所有点位合并），顶点色控制颜色。
    /// 性能：材质共享；仅在有新点/裁剪时重建（另每 4 帧补一次）；超距船跳过。
    /// </summary>
    internal static class ShipWakeHint
    {
        private const int ROLE_WHITE = 0, ROLE_PORT = 1, ROLE_STBD = 2, ROLE_CENTER = 3, ROLE_BOW = 4;

        private class ShipRec
        {
            public WeakReference<Ship> Ship;
            public GameObject Go;
            public Mesh Mesh;
            public List<Vector3>[] Pts;
            public List<float>[] Times;
            public Color32[] Tints;
            public float Clock;
            public int Frame;
            public bool Dirty;
        }

        private static readonly List<ShipRec> Ships = new List<ShipRec>();
        private static readonly HashSet<int> _ids = new HashSet<int>();
        private static readonly Dictionary<int, Material> MaterialCache = new Dictionary<int, Material>();
        private static bool _running;

        internal static void Register(Ship s)
        {
            try
            {
                if (s == null) return;
                int id = 0;
                try { id = s.GetInstanceID(); } catch { }
                if (id != 0)
                {
                    if (!_ids.Add(id)) return;      // 已注册
                }
                Ships.Add(new ShipRec { Ship = new WeakReference<Ship>(s), Dirty = true });
                EnsureRunning();
            }
            catch { }
        }

        private static void EnsureRunning()
        {
            if (_running) return;
            _running = true;
            try { MelonLoader.MelonCoroutines.Start(Loop()); }
            catch (Exception e) { HotfixLog.Warn("[SMS.2.WAKE] loop: " + e.Message); }
        }

        private static bool ModeOn()
        {
            if (!HotfixParams.ShipWakeHint) return false;
            int ot = 0;
            try { ot = UiM.TAF_Settings.settings.oceanType; } catch { }
            int daW = Config.Param("taf_disable_all_water", 0) == 1 ? 1 : 0;
            return ((daW == 1) ? 2 : ot) != 0;
        }

        private static Material GetMaterial()
        {
            Material m;
            if (MaterialCache.TryGetValue(0, out m) && m != null) return m;
            var shader = Shader.Find("Sprites/Default");
            if (shader == null) shader = Shader.Find("Unlit/Transparent");
            if (shader == null) shader = Shader.Find("Standard");
            m = new Material(shader);
            try
            {
                m.renderQueue = 2600;
                if (m.HasProperty("_Color")) m.color = new Color(1f, 1f, 1f, 1f);
            }
            catch { }
            MaterialCache[0] = m;
            return m;
        }

        private static IEnumerator Loop()
        {
            while (true)
            {
                yield return null;
                try
                {
                    if (!ModeOn())
                    {
                        for (int i = 0; i < Ships.Count; i++) DestroyRec(Ships[i]);
                        continue;
                    }

                    int maxShips = Mathf.Max(0, (int)HotfixParams.ShipWakeMaxShips);
                    int processed = 0;
                    float far = Mathf.Max(1000f, HotfixParams.ShipWakeMaxDistance);

                    for (int i = Ships.Count - 1; i >= 0; i--)
                    {
                        Ship s;
                        if (!Ships[i].Ship.TryGetTarget(out s) || s == null)
                        {
                            DestroyRec(Ships[i]);
                            Ships.RemoveAt(i);
                            continue;
                        }

                        if (maxShips > 0 && processed >= maxShips) { HideRec(Ships[i]); continue; }

                        bool near = true;
                        try
                        {
                            var cam = G.cam;
                            if (cam != null) near = (s.transform.position - cam.transform.position).sqrMagnitude < far * far;
                        }
                        catch { }
                        if (!near) { HideRec(Ships[i]); continue; }

                        // ★ 只为我方可见的舰船绘制航迹（敌方未发现时不该有航迹）
                        bool visible = true;
                        try { visible = WakeVisibility.ShipVisible(s); } catch { }
                        if (!visible) { HideRec(Ships[i]); continue; }

                        processed++;
                        try { UpdateShip(s, Ships[i]); }
                        catch (Exception ex) { LogOnce("update: " + ex.GetType().Name + " " + ex.Message); }
                    }
                }
                catch { }
            }
        }

        /// <summary>把已注册的船只（存活）收集到给定列表，供可见性判定使用。</summary>
        internal static void CollectShips(List<Ship> into)
        {
            if (into == null) return;
            for (int i = 0; i < Ships.Count; i++)
            {
                Ship s;
                if (Ships[i].Ship.TryGetTarget(out s) && s != null) into.Add(s);
            }
        }

        private static void HideRec(ShipRec rec)
        {
            try { if (rec.Go != null && rec.Go.activeSelf) rec.Go.SetActive(false); } catch { }
        }

        private static void DestroyRec(ShipRec rec)
        {
            try { if (rec.Go != null) UnityEngine.Object.Destroy(rec.Go); } catch { }
            rec.Go = null; rec.Mesh = null;
        }

        private static void PointsFor(Ship s, int n, out Vector3[] pts, out int[] roles)
        {
            Transform tf = s.transform;
            try { if (s.hull != null && s.hull.transform != null) tf = s.hull.transform; } catch { }

            float L = 100f, B = 10f;
            try { L = Mathf.Max(10f, s.hullSize.size.z); } catch { }
            try { B = Mathf.Max(2f, s.hullSize.size.x); } catch { }

            Vector3 fwd = tf.forward; fwd.y = 0f;
            if (fwd.sqrMagnitude < 1e-4f) fwd = Vector3.forward;
            fwd.Normalize();
            Vector3 right = Vector3.Cross(Vector3.up, fwd).normalized;

            Vector3 C = tf.position;
            float a = L * 0.5f;
            float b = B * 0.5f + Mathf.Max(0f, HotfixParams.ShipWakeExtraWidth) * 0.5f;

            Vector3 P = C - right * b, S = C + right * b;
            Vector3 T = C - fwd * a, N = C + fwd * a;

            float c1 = Mathf.Cos(Mathf.PI * 0.25f), s1 = Mathf.Sin(Mathf.PI * 0.25f);
            float c2 = Mathf.Cos(Mathf.PI * 0.125f), s2 = Mathf.Sin(Mathf.PI * 0.125f);

            Vector3 a1 = C - fwd * (a * c1) + right * (b * s1);
            Vector3 a2 = C - fwd * (a * c1) - right * (b * s1);
            Vector3 a3 = C - fwd * (a * c2) + right * (b * s2);
            Vector3 a4 = C - fwd * (a * c2) - right * (b * s2);

            switch (n)
            {
                case 1: pts = new[] { C }; roles = new[] { ROLE_CENTER }; break;
                case 2: pts = new[] { P, S }; roles = new[] { ROLE_PORT, ROLE_STBD }; break;
                case 3: pts = new[] { P, S, T }; roles = new[] { ROLE_PORT, ROLE_STBD, ROLE_WHITE }; break;
                case 5:
                    pts = new[] { P, S, N, a1, a2 };
                    roles = new[] { ROLE_PORT, ROLE_STBD, ROLE_BOW, ROLE_WHITE, ROLE_WHITE }; break;
                case 6:
                    pts = new[] { P, S, T, N, a1, a2 };
                    roles = new[] { ROLE_PORT, ROLE_STBD, ROLE_WHITE, ROLE_BOW, ROLE_WHITE, ROLE_WHITE }; break;
                case 7:
                    pts = new[] { P, S, N, a1, a2, a3, a4 };
                    roles = new[] { ROLE_PORT, ROLE_STBD, ROLE_BOW, ROLE_WHITE, ROLE_WHITE, ROLE_WHITE, ROLE_WHITE }; break;
                case 8:
                    pts = new[] { P, S, T, N, a1, a2, a3, a4 };
                    roles = new[] { ROLE_PORT, ROLE_STBD, ROLE_WHITE, ROLE_BOW, ROLE_WHITE, ROLE_WHITE, ROLE_WHITE, ROLE_WHITE }; break;
                case 9:
                    pts = new[] { C, P, S, T, N, a1, a2, a3, a4 };
                    roles = new[] { ROLE_CENTER, ROLE_PORT, ROLE_STBD, ROLE_WHITE, ROLE_BOW, ROLE_WHITE, ROLE_WHITE, ROLE_WHITE, ROLE_WHITE };
                    break;
                default:
                    pts = new[] { P, S, T, N };
                    roles = new[] { ROLE_PORT, ROLE_STBD, ROLE_WHITE, ROLE_BOW };
                    break;
            }
        }

        private static Color32 TintFor(int role)
        {
            if (HotfixParams.ShipWakeColorMode <= 0) return new Color32(220, 235, 245, 255);
            switch (role)
            {
                case ROLE_PORT: return new Color32(255, 170, 170, 255);
                case ROLE_STBD: return new Color32(170, 255, 180, 255);
                case ROLE_CENTER: return new Color32(255, 245, 170, 255);
                case ROLE_BOW: return new Color32(170, 210, 255, 255);
                default: return new Color32(220, 235, 245, 255);
            }
        }

        private static void UpdateShip(Ship s, ShipRec rec)
        {
            if (s.transform == null) return;

            int n = Mathf.Clamp((int)HotfixParams.ShipWakePoints, 1, 9);
            Vector3[] pts = null; int[] roles = null;
            try { PointsFor(s, n, out pts, out roles); }
            catch (Exception ex) { LogOnce("points: " + ex.GetType().Name + " " + ex.Message); }
            if (pts == null || pts.Length == 0) { LogOnce("points empty"); return; }

            if (rec.Pts == null || rec.Pts.Length != pts.Length)
            {
                rec.Pts = new List<Vector3>[pts.Length];
                rec.Times = new List<float>[pts.Length];
                rec.Tints = new Color32[pts.Length];
                rec.Dirty = true;
            }

            rec.Clock += Time.deltaTime;
            float now = rec.Clock;
            float retention = Mathf.Max(0.5f, HotfixParams.ShipWakeSeconds);

            for (int k = 0; k < pts.Length; k++)
            {
                if (rec.Pts[k] == null) { rec.Pts[k] = new List<Vector3>(); rec.Times[k] = new List<float>(); }
                rec.Tints[k] = TintFor(roles[k]);

                var list = rec.Pts[k]; var times = rec.Times[k];

                // ★ 瞬移过滤：脱离/恢复接触的瞬间，船体模型可能被挪到极远处（判定框留在原地），
                //   这会在远点留下"飞出去"的航迹。超过阈值就**清空该点列**重新开始。
                if (list.Count > 0)
                {
                    float jump = Mathf.Max(50f, HotfixParams.ShipWakeMaxJump);
                    if ((list[list.Count - 1] - pts[k]).sqrMagnitude > jump * jump)
                    {
                        list.Clear(); times.Clear();
                        rec.Dirty = true;
                    }
                }

                if (list.Count == 0 || (list[list.Count - 1] - pts[k]).sqrMagnitude > 4f)
                {
                    list.Add(pts[k]); times.Add(now);
                    while (list.Count > 4096) { list.RemoveAt(0); times.RemoveAt(0); }
                    rec.Dirty = true;
                }
                int removed = 0;
                while (times.Count > 0 && now - times[0] > retention)
                {
                    list.RemoveAt(0); times.RemoveAt(0); removed++;
                }
                if (removed > 0) rec.Dirty = true;
            }

            rec.Frame++;
            if (!rec.Dirty && (rec.Frame % 4) != 0) return;
            rec.Dirty = false;
            Rebuild(rec);
        }

        private static void EnsureGo(ShipRec rec)
        {
            if (rec.Go != null) return;
            rec.Go = new GameObject("SMS_ShipWake");
            try { UnityEngine.Object.DontDestroyOnLoad(rec.Go); } catch { }
            rec.Mesh = new Mesh();
            rec.Go.AddComponent<MeshFilter>().sharedMesh = rec.Mesh;
            var mr = rec.Go.AddComponent<MeshRenderer>();
            try
            {
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                mr.receiveShadows = false;
            }
            catch { }
            mr.sharedMaterial = GetMaterial();
        }

        private static void Rebuild(ShipRec rec)
        {
            try
            {
                EnsureGo(rec);
                if (rec.Go == null) return;

                var verts = new List<Vector3>();
                var cols = new List<Color32>();
                var tris = new List<int>();

                float baseW = Mathf.Max(0.1f, HotfixParams.ShipWakeWidth);
                bool dash = HotfixParams.ShipWakeDash > 0;

                for (int k = 0; k < rec.Pts.Length; k++)
                {
                    var list = rec.Pts[k];
                    if (list == null || list.Count < 2) continue;
                    var tint = rec.Tints[k];
                    int cnt = list.Count;

                    for (int i = 0; i < cnt - 1; i++)
                    {
                        if (dash && (i % 2 == 1)) continue;

                        Vector3 dir = list[i + 1] - list[i];
                        dir.y = 0f;
                        if (dir.sqrMagnitude < 1e-4f) continue;
                        dir.Normalize();
                        Vector3 side = Vector3.Cross(Vector3.up, dir) * (baseW * 0.5f);

                        float t = (float)i / (float)(cnt - 1);
                        float pw = Mathf.Max(0.1f, HotfixParams.ShipWakeFadePow);
                        byte al = (byte)Mathf.Clamp(Mathf.Pow(t, pw) * HotfixParams.ShipWakeAlpha, 0f, 255f);
                        var c = new Color32(tint.r, tint.g, tint.b, al);

                        int v = verts.Count;
                        verts.Add(list[i] - side); verts.Add(list[i] + side);
                        verts.Add(list[i + 1] - side); verts.Add(list[i + 1] + side);
                        cols.Add(c); cols.Add(c); cols.Add(c); cols.Add(c);
                        tris.Add(v); tris.Add(v + 1); tris.Add(v + 2);
                        tris.Add(v + 1); tris.Add(v + 3); tris.Add(v + 2);
                    }
                }

                if (!rec.Go.activeSelf) rec.Go.SetActive(true);
                var m = rec.Mesh;
                m.Clear();
                if (verts.Count == 0) { HideRec(rec); return; }
                m.vertices = verts.ToArray();
                m.colors32 = cols.ToArray();
                m.triangles = tris.ToArray();
                m.RecalculateBounds();
            }
            catch { }
        }

        private static float _lastDiag;
        private static readonly HashSet<string> _seen = new HashSet<string>();

        private static void LogOnce(string msg)
        {
            if (_seen.Contains(msg)) return;
            _seen.Add(msg);
            HotfixLog.Warn("[SMS.2.WAKE] " + msg);
        }
    }
}
