using System;
using System.Collections;
using System.Collections.Generic;
using Il2Cpp;
using TAFHotfixBatch1;
using UnityEngine;

namespace TweaksAndFixes
{
    /// <summary>
    /// [SMS.2.OCEAN] 鱼雷雷迹补偿（仅 `oceanType=1` 简化 / `2` 空水；原版不介入）。
    ///
    /// 分段版：
    ///   · 每 0.2 s 采样 `Torpedo.AllTorpedo` 位置，写入该雷的点列；
    ///   · 用点列重建三角带（宽 1.2 m，贴水面 y=0.3），顶点 alpha 随年龄衰减；
    ///   · 支持鱼雷转向（自导 / 原版随机偏差）——条带随点列自然转弯；
    ///   · 点列超过 MAX_POINTS 自动裁剪；鱼雷消失即销毁条带。
    ///
    /// 颜色取 Suimono `color_foam (0.734, 0.898, 0.941)`；开关 `torp_wake_hint`（默认 1）。
    /// </summary>
    internal static class TorpedoWakeHint
    {
        private const int MAX_POINTS = 4096;     // 够 25 战斗秒的点量
        private const float SPACING = 1.2f;      // 点间距（米）

        private class Trail
        {
            public GameObject Go;
            public Mesh Mesh;
            public Material Mat;
            public readonly List<Vector3> Pts = new List<Vector3>();
            public readonly List<float> Times = new List<float>();
            public float Clock;
            public float LastSeenWall;
        }

        private static readonly Dictionary<int, Trail> Trails = new Dictionary<int, Trail>();
        private static bool _running;

        internal static void EnsureRunning()
        {
            if (_running) return;
            _running = true;
            try { MelonLoader.MelonCoroutines.Start(Loop()); }
            catch (Exception e) { HotfixLog.Warn("[SMS.2.OCEAN] wake loop: " + e.Message); }
        }

        private static IEnumerator Loop()
        {
            while (true)
            {
                yield return null;   // 每帧采样（避免 0.2s 造成的"幻灯片"跳变）

                try
                {
                    if (!HotfixParams.TorWakeHint) { DestroyAll(); continue; }

                    int ot = 0;
                    try { ot = UiM.TAF_Settings.settings.oceanType; } catch { }
                    int daW = Config.Param("taf_disable_all_water", 0) == 1 ? 1 : 0;
                    int mode = (daW == 1) ? 2 : ot;
                    if (mode == 0) { DestroyAll(); continue; }

                    var list = Torpedo.AllTorpedo;
                    if (list == null || list.Count == 0) { DestroyAll(); continue; }

                    var alive = new HashSet<int>();
                    for (int k = 0; k < list.Count; k++)
                    {
                        var t = list[k];
                        if (t == null) continue;

                        int id = 0; Vector3 pos = Vector3.zero;
                        try { id = t.GetInstanceID(); pos = PosOf(t); } catch { continue; }

                        // ★ 只为我方可见的鱼雷绘制雷迹（避免"未卜先知"）
                        bool tVisible = true;
                        try { tVisible = WakeVisibility.TorpedoVisible(t); } catch { }
                        if (!tVisible) continue;

                        // ★ 模型可见性 + 位置一致性：未发现的鱼雷模型不渲染（或被打包到极远处），
                        //   据此避免"模型还没出现就画雷迹"的剧透。
                        // [SMS.2.WAKE] 鱼雷模型取证（verbose=1，每颗雷约 2 秒一条）
                        if (HotfixParams.TorWakeProbe) ProbeTorp(t, pos);

                        if (HotfixParams.TorWakeRequireModel && !ModelVisible(t, pos)) continue;
                        alive.Add(id);      // ★ 关键：漏了这行会导致每帧把所有条带当"已消失"销毁

                        Trail tr;
                        if (!Trails.TryGetValue(id, out tr)) tr = Trails[id] = Create();

                        // 战斗内时间（随游戏倍速流逝）
                        // Unity 的 Time.deltaTime **本身已被 timeScale 缩放**，无需再乘一次
                        tr.Clock += Time.deltaTime;
                        float now = tr.Clock;
                        if (tr.Pts.Count == 0 || (tr.Pts[tr.Pts.Count - 1] - pos).sqrMagnitude > SPACING * SPACING)
                        {
                            tr.Pts.Add(pos);
                            tr.Times.Add(now);
                            while (tr.Pts.Count > MAX_POINTS) { tr.Pts.RemoveAt(0); tr.Times.RemoveAt(0); }
                        }

                        // 按时间裁剪（保留最近 RETENTION 秒）
                        float retention = Mathf.Max(0.5f, HotfixParams.TorWakeSeconds);
                        while (tr.Times.Count > 0 && now - tr.Times[0] > retention)
                        {
                            tr.Pts.RemoveAt(0);
                            tr.Times.RemoveAt(0);
                        }

                        tr.LastSeenWall = Time.time;
                        Rebuild(tr);
                    }

                    // 鱼雷消失后延迟 torp_wake_linger 秒再清理（命中/失效后航迹不立刻消失）
                    float linger = Mathf.Max(0f, HotfixParams.TorWakeLinger);
                    var dead = new List<int>();
                    foreach (var kv in Trails)
                    {
                        if (alive.Contains(kv.Key)) continue;
                        if (Time.time - kv.Value.LastSeenWall < linger) continue;
                        dead.Add(kv.Key);
                    }
                    for (int i = 0; i < dead.Count; i++) DestroyOne(dead[i]);
                }
                catch { }
            }
        }

        private static void DestroyAll()
        {
            var keys = new List<int>(Trails.Keys);
            for (int i = 0; i < keys.Count; i++) DestroyOne(keys[i]);
        }

        private static void DestroyOne(int id)
        {
            Trail tr;
            if (!Trails.TryGetValue(id, out tr)) return;
            try { if (tr.Go != null) UnityEngine.Object.Destroy(tr.Go); } catch { }
            try { if (tr.Mat != null) UnityEngine.Object.Destroy(tr.Mat); } catch { }
            Trails.Remove(id);
        }


        /// <summary>
        /// [SMS.2.WAKE] 取鱼雷的"实际可见中心"：
        ///   优先用其 MeshRenderer 的 world bounds 中心（能反映模型真实深度），
        ///   失败则回落 transform.position。
        /// </summary>

        /// <summary>
        /// 模型可见性判定：
        ///   ① 至少有一个**激活且启用**的 Renderer（否则说明模型未加载/未显示）；
        ///   ② 模型中心与逻辑位置的距离 ≤ torp_wake_model_max_dist（防止"模型被挪到远处"的情况）。
        /// </summary>

        private static readonly System.Collections.Generic.Dictionary<int, float> _probeAt =
            new System.Collections.Generic.Dictionary<int, float>();

        /// <summary>[SMS.2.WAKE] torp probe: 模型是否存在/可见，以及模型中心与逻辑位置的偏差。</summary>
        private static void ProbeTorp(Torpedo t, Vector3 logicPos)
        {
            try
            {
                int id = t.GetInstanceID();
                float now = Time.realtimeSinceStartup;
                float last;
                if (_probeAt.TryGetValue(id, out last) && now - last < 2f) return;
                _probeAt[id] = now;

                bool goActive = t.gameObject != null && t.gameObject.activeInHierarchy;
                int all = 0, act = 0, en = 0;
                Vector3 sum = Vector3.zero;
                var rs = t.GetComponentsInChildren<Renderer>(true);
                if (rs != null)
                {
                    for (int i = 0; i < rs.Length; i++)
                    {
                        var r = rs[i];
                        if (r == null) continue;
                        all++;
                        if (r.gameObject.activeInHierarchy) act++;
                        if (r.enabled) en++;
                        if (r.gameObject.activeInHierarchy && r.enabled) sum += r.bounds.center;
                    }
                }
                Vector3 center = (act > 0 && en > 0) ? sum / Mathf.Max(1, en) : Vector3.zero;
                float dist = (act > 0) ? (center - logicPos).magnitude : -1f;

                HotfixLog.Dbg("[SMS.2.WAKE] torp '" + SafeName(t) + "' goActive=" + goActive
                              + " renderers=" + all + " active=" + act + " enabled=" + en
                              + " dist=" + dist.ToString("0.#")
                              + " logicPos=" + logicPos.ToString("0.#"));
            }
            catch { }
        }

        private static string SafeName(Torpedo t)
        {
            try { return t.gameObject != null ? t.gameObject.name : "?"; } catch { return "?"; }
        }

        private static bool ModelVisible(Torpedo t, Vector3 logicPos)
        {
            try
            {
                var rs = t.GetComponentsInChildren<Renderer>(true);
                if (rs == null || rs.Length == 0) return true;   // 没有 Renderer 的鱼雷：不做限制（放行）

                int active = 0;
                Vector3 sum = Vector3.zero;
                for (int i = 0; i < rs.Length; i++)
                {
                    var r = rs[i];
                    if (r == null) continue;
                    if (!r.gameObject.activeInHierarchy) continue;
                    if (!r.enabled) continue;
                    sum += r.bounds.center;
                    active++;
                }
                if (active < HotfixParams.TorWakeMinRenderers) return false;                    // 模型未显示 ⇒ 不画

                Vector3 center = sum / active;
                float dist = (center - logicPos).magnitude;
                // 不再用 dist 判定（拖尾会持续拉大平均中心，阈值不可靠，详见 docs/74）
                return true;
            }
            catch { return true; }
        }

        private static Vector3 PosOf(Torpedo t)
        {
            Vector3 fallback = Vector3.zero;
            try { fallback = t.transform.position; } catch { }
            if (!HotfixParams.TorWakeUseRenderBounds) return fallback;

            try
            {
                var mrs = t.GetComponentsInChildren<MeshRenderer>();
                if (mrs == null || mrs.Length == 0) return fallback;

                Vector3 sum = Vector3.zero;
                int n = 0;
                for (int i = 0; i < mrs.Length; i++)
                {
                    var mr = mrs[i];
                    if (mr == null) continue;
                    if (!mr.gameObject.activeInHierarchy) continue;
                    sum += mr.bounds.center;
                    n++;
                }
                if (n == 0) return fallback;
                return sum / n;
            }
            catch { return fallback; }
        }

        private static Trail Create()
        {
            var go = new GameObject("SMS_TorpWake");
            try { UnityEngine.Object.DontDestroyOnLoad(go); } catch { }

            var mesh = new Mesh();
            var mf = go.AddComponent<MeshFilter>();
            mf.sharedMesh = mesh;

            var mr = go.AddComponent<MeshRenderer>();
            try
            {
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                mr.receiveShadows = false;
            }
            catch { }

            // 顶点色 + 透明：Sprites/Default 支持顶点色且不受光照
            var shader = Shader.Find("Sprites/Default");
            if (shader == null) shader = Shader.Find("Unlit/Transparent");
            if (shader == null) shader = Shader.Find("Standard");

            var mat = new Material(shader);
            try
            {
                mat.renderQueue = 2600;
                // ★ 材质 alpha 必须为 1：最终透明度完全交给顶点 alpha 控制（否则两者相乘会让渐隐被放大 0.55 倍）
                if (mat.HasProperty("_Color")) mat.color = new Color(0.734f, 0.898f, 0.941f, 1f);
            }
            catch { }
            mr.sharedMaterial = mat;

            return new Trail { Go = go, Mesh = mesh, Mat = mat };
        }

        private static void Rebuild(Trail tr)
        {
            try
            {
                var pts = tr.Pts;
                int n = pts.Count;
                if (n < 2)
                {
                    if (tr.Go != null && tr.Go.activeSelf) tr.Go.SetActive(false);
                    return;
                }
                if (tr.Go != null && !tr.Go.activeSelf) tr.Go.SetActive(true);

                var verts = new Vector3[n * 2];
                var cols = new Color32[n * 2];
                var tris = new int[(n - 1) * 6];

                for (int i = 0; i < n; i++)
                {
                    Vector3 dir = (i < n - 1) ? (pts[i + 1] - pts[i]) : (pts[i] - pts[i - 1]);
                    dir.y = 0f;
                    if (dir.sqrMagnitude < 1e-4f) dir = Vector3.forward;
                    dir.Normalize();
                    Vector3 side = Vector3.Cross(Vector3.up, dir) * (Mathf.Max(0.05f, HotfixParams.TorWakeWidth) * 0.5f);

                    float t = (float)i / (float)(n - 1);
                    float pow = Mathf.Max(0.1f, HotfixParams.TorWakeFadePow);
                    byte a = (byte)Mathf.Clamp(Mathf.Pow(t, pow) * HotfixParams.TorWakeAlpha, 0f, 255f);
                    var c = new Color32(187, 229, 240, a);

                    verts[i * 2] = new Vector3(pts[i].x - side.x, pts[i].y, pts[i].z - side.z);
                    verts[i * 2 + 1] = new Vector3(pts[i].x + side.x, pts[i].y, pts[i].z + side.z);
                    cols[i * 2] = c;
                    cols[i * 2 + 1] = c;
                }

                for (int i = 0; i < n - 1; i++)
                {
                    int b = i * 6, v = i * 2;
                    tris[b] = v; tris[b + 1] = v + 1; tris[b + 2] = v + 2;
                    tris[b + 3] = v + 1; tris[b + 4] = v + 3; tris[b + 5] = v + 2;
                }

                var m = tr.Mesh;
                m.Clear();
                m.vertices = verts;
                m.colors32 = cols;
                m.triangles = tris;
                m.RecalculateBounds();
            }
            catch { }
        }
    }
}
