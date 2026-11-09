using System;
using System.Collections.Generic;
using HarmonyLib;
using Il2Cpp;
using TAFHotfixBatch1;
using UnityEngine;

namespace TweaksAndFixes
{
    /// <summary>
    /// [SMS.2.SEC] 舯段 UV 归一化（`section_uv_normalize`）。
    ///
    /// 依据（port194 实测，京畿 / hood_hull_a）：
    ///   bow    uv X 跨度 14.14, Y 跨度 7.47,  avg Y 2.425
    ///   middle uv X 跨度 15.36, Y 跨度 8.04,  avg Y 2.953
    ///   stern  uv X 跨度 19.68, Y 跨度 10.29, avg Y 3.498
    ///
    /// ⇒ 各段 UV 是"按自身长度连续展开"的，跨度与起点都不同；段被 `RefreshHull`
    ///   缩放适配后 UV 不会跟着缩放 ⇒ 纹素密度/贴图起点不一致 ⇒ 段间色带。
    ///
    /// 本模块只处理**克隆出来的舯段**（不动 bow/stern）：
    ///   把每段 UV 的跨度与中心，归一化到 bow 的跨度与中心，逐轴独立。
    /// 幂等：原始 UV 按 mesh InstanceID 缓存，重复调用不会累积。
    /// </summary>
    internal static class SectionUvFix
    {
        private static readonly Dictionary<int, Vector2[]> Originals = new Dictionary<int, Vector2[]>();
        private static readonly Dictionary<int, Mesh> Clones = new Dictionary<int, Mesh>();

        internal static void Apply(Ship ship)
        {
            if (!HotfixParams.SectionUvNormalize) return;
            try
            {
                var h = ship != null ? ship.hull : null;
                if (h == null) return;
                if (h.middles == null || h.middles.Count == 0) return;

                if (!TrySpan(h.bow, out float bowSpanX, out float bowSpanY,
                             out float bowCx, out float bowCy))
                    return;

                int changed = 0;
                for (int i = 0; i < h.middles.Count; i++)
                {
                    var go = h.middles[i];
                    if (go == null) continue;
                    var rs = go.GetComponentsInChildren<Renderer>(true);
                    if (rs == null) continue;
                    for (int k = 0; k < rs.Length; k++)
                    {
                        var r = rs[k];
                        if (r == null) continue;
                        if (r.name != null && r.name.StartsWith("LOD")) continue;   // 只改 LOD0 主网格
                        if (Normalize(r, bowSpanX, bowSpanY, bowCx, bowCy)) changed++;
                    }
                }

                if (changed > 0)
                    HotfixLog.Dbg("[SMS.2.SEC] uv normalize: ship='" + SafeName(ship)
                                  + "' meshes=" + changed
                                  + " target span=(" + bowSpanX.ToString("0.##") + ","
                                  + bowSpanY.ToString("0.##") + ")");
            }
            catch (Exception e) { HotfixLog.Warn("[SMS.2.SEC] uv normalize: " + e.Message); }
        }

        private static bool Normalize(Renderer r, float tSpanX, float tSpanY, float tCx, float tCy)
        {
            try
            {
                var mf = r.GetComponent<MeshFilter>();
                if (mf == null || mf.sharedMesh == null) return false;

                var mesh = mf.sharedMesh;
                int id = mesh.GetInstanceID();

                if (!Originals.TryGetValue(id, out var orig))
                {
                    var uv = mesh.uv;
                    if (uv == null || uv.Length == 0) return false;
                    orig = new Vector2[uv.Length];
                    Array.Copy(uv, orig, uv.Length);
                    Originals[id] = orig;
                }

                float minX = 9e9f, minY = 9e9f, maxX = -9e9f, maxY = -9e9f;
                for (int i = 0; i < orig.Length; i++)
                {
                    var v = orig[i];
                    if (v.x < minX) minX = v.x;
                    if (v.y < minY) minY = v.y;
                    if (v.x > maxX) maxX = v.x;
                    if (v.y > maxY) maxY = v.y;
                }
                float spanX = maxX - minX, spanY = maxY - minY;
                if (spanX <= 1e-4f || spanY <= 1e-4f) return false;

                float sx = tSpanX / spanX;
                float sy = tSpanY / spanY;
                float cx = (minX + maxX) * 0.5f;
                float cy = (minY + maxY) * 0.5f;

                var dst = new Vector2[orig.Length];
                for (int i = 0; i < orig.Length; i++)
                {
                    float ux = tCx + (orig[i].x - cx) * sx;
                    float uy = tCy + (orig[i].y - cy) * sy;
                    dst[i] = new Vector2(ux, uy);
                }

                // 需要可写网格：若不可写则用克隆副本
                Mesh target = mesh;
                if (!mesh.isReadable)
                {
                    if (!Clones.TryGetValue(id, out target) || target == null)
                    {
                        target = UnityEngine.Object.Instantiate(mesh);
                        Clones[id] = target;
                        mf.sharedMesh = target;
                    }
                }

                target.uv = dst;
                return true;
            }
            catch { return false; }
        }

        private static bool TrySpan(GameObject go, out float spanX, out float spanY, out float cx, out float cy)
        {
            spanX = spanY = cx = cy = 0f;
            if (go == null) return false;
            try
            {
                var rs = go.GetComponentsInChildren<Renderer>(true);
                if (rs == null) return false;
                for (int i = 0; i < rs.Length; i++)
                {
                    var r = rs[i];
                    if (r == null || r.name == null || r.name.StartsWith("LOD")) continue;
                    var mf = r.GetComponent<MeshFilter>();
                    if (mf == null || mf.sharedMesh == null) continue;
                    var uv = mf.sharedMesh.uv;
                    if (uv == null || uv.Length == 0) continue;

                    float minX = 9e9f, minY = 9e9f, maxX = -9e9f, maxY = -9e9f;
                    for (int k = 0; k < uv.Length; k++)
                    {
                        var v = uv[k];
                        if (v.x < minX) minX = v.x;
                        if (v.y < minY) minY = v.y;
                        if (v.x > maxX) maxX = v.x;
                        if (v.y > maxY) maxY = v.y;
                    }
                    spanX = maxX - minX;
                    spanY = maxY - minY;
                    cx = (minX + maxX) * 0.5f;
                    cy = (minY + maxY) * 0.5f;
                    return true;
                }
            }
            catch { }
            return false;
        }

        private static string SafeName(Ship s)
        {
            try { return s.vesselName; } catch { }
            try { return s.name; } catch { }
            return "?";
        }
    }

    [HarmonyPatch(typeof(Ship), nameof(Ship.RefreshHull))]
    internal static class Patch_Ship_SectionUvFix
    {
        [HarmonyPostfix]
        internal static void Postfix(Ship __instance)
        {
            SectionUvFix.Apply(__instance);
        }
    }
}
