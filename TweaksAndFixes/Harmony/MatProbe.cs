using System;
using System.Text;
using HarmonyLib;
using Il2Cpp;
using TAFHotfixBatch1;
using UnityEngine;
using UnityEngine.Rendering;

namespace TweaksAndFixes
{
    /// <summary>
    /// [SMS.1.MAT] 舯段渲染/材质只读探针。
    ///
    /// 背景（2026-10-26 用户分析）：
    ///   · 原版船体模型（如 `hood_hull_a` / `bismarck_hull_a`）**只有一种舯段定义**；
    ///   · 游戏按"可读次序循环/往返"复制出多余的舯段；
    ///   · 色差恰好出现在这些**复制出来的舯段**上 ⇒ 怀疑复制过程中
    ///     材质实例 / 法线方向 / 光照相关属性与原版段不一致。
    ///   · 原版几乎没有影子（平均光照掩盖差异）；duke 在 3.21.1+ 引入太阳与阴影后才暴露。
    ///
    /// 本探针只读，打印每个段的关键渲染信息，便于对比"复制段 vs 原始段"：
    ///   名称 / Renderer 数 / sharedMaterial 名与实例 ID / 是否接收阴影 / 投射阴影模式 / 包围盒中心
    ///
    /// 开关：`mat_probe`（默认 1）。触发：进入 Constructor 状态且 `PlayerController.Instance.Ship` 存在时，最多 3 次。
    /// </summary>
    [HarmonyPatch(typeof(GameManager))]
    internal static class Patch_MatProbe
    {
        private static int _runs;

        [HarmonyPostfix]
        [HarmonyPatch("ChangeState")]
        internal static void Postfix_ChangeState(GameManager.GameState newState)
        {
            if (!HotfixParams.MatProbe) return;
            if (_runs >= 3) return;

            string st = "";
            try { st = newState.ToString(); } catch { }
            if (st != "Constructor") return;

            // 延迟到下一帧再取（此刻 Ship 可能还没装载完）
            _runs++;
            try { MelonLoader.MelonCoroutines.Start(Delayed()); }
            catch (Exception e) { HotfixLog.Warn("[SMS.1.MAT] start: " + e.Message); }
        }

        private static System.Collections.IEnumerator Delayed()
        {
            yield return null;
            yield return null;

            try { Dump(); }
            catch (Exception e) { HotfixLog.Warn("[SMS.1.MAT] dump: " + e.Message); }
        }

        private static void Dump()
        {
            var pc = PlayerController.Instance;
            if (pc == null) return;
            var ship = pc.Ship;
            if (ship == null) return;

            var sb = new StringBuilder();
            sb.Append("[SMS.1.MAT] ship='").Append(SafeName(ship)).Append("'");

            var hull = ship.hull;

            if (hull == null)
            {
                sb.Append("  hull=null");
                // ★ 首/尾段：hullSections 是 IEnumerable<GameObject>（IL2CPP 绑定缺 MoveNext），
            //   改用 hull.bow / hull.stern 这两个直接引用。
            try
            {
                AppendSection(sb, "Bow", hull.bow);
                AppendSection(sb, "Stern", hull.stern);
            }
            catch (Exception e1) { sb.Append("\n  bow/stern err ").Append(e1.Message); }

            HotfixLog.Info(sb.ToString());
                return;
            }

            // 1) 舯段（玩家复制出来的那些）
            try
            {
                var middles = hull.middles;
                var bases = hull.middlesBase;
                sb.Append("\n  middles=").Append(middles != null ? middles.Count : -1)
                  .Append("  middlesBase=").Append(bases != null ? bases.Count : -1);

                if (middles != null)
                {
                    for (int i = 0; i < middles.Count && i < 12; i++)
                    {
                        GameObject go = null;
                        try { go = middles[i]; } catch { }
                        sb.Append("\n   M").Append(i).Append(": ").Append(Describe(go));
                    }
                }

                if (bases != null)
                {
                    for (int i = 0; i < bases.Count && i < 6; i++)
                    {
                        GameObject go = null;
                        try { go = bases[i]; } catch { }
                        sb.Append("\n   B").Append(i).Append(": ").Append(Describe(go));
                    }
                }
            }
            catch (Exception e) { sb.Append("  middles err ").Append(e.Message); }

            HotfixLog.Info(sb.ToString());
        }


        /// <summary>紧凑输出一个段的缩放/法线信息。</summary>
        private static void AppendSection(StringBuilder sb, string tag, GameObject g)
        {
            if (g == null) { sb.Append("\n   ").Append(tag).Append("=null"); return; }
            string sc = "?", lsc = "?";
            try
            {
                var ls = g.transform.localScale;
                var ly = g.transform.lossyScale;
                sc = "(" + ls.x.ToString("0.###") + "," + ls.y.ToString("0.###") + "," + ls.z.ToString("0.###") + ")";
                lsc = "(" + ly.x.ToString("0.###") + "," + ly.y.ToString("0.###") + "," + ly.z.ToString("0.###") + ")";
            }
            catch { }
            sb.Append("\n   ").Append(tag).Append(" ").Append(g.name)
              .Append(" local=").Append(sc).Append(" lossy=").Append(lsc);
        }

        private static string Describe(GameObject go)
        {
            if (go == null) return "<null>";

            var sb = new StringBuilder();
            sb.Append(go.name).Append(" active=").Append(go.activeSelf);
            try { sb.Append(" z=").Append(go.transform.localPosition.z.ToString("0.00")); } catch { }

            // ★ [SMS.1.MAT] 朝向/法线信息：负缩放会让网格法线翻转 ⇒ 光照下呈暗块
            try
            {
                var tr0 = go.transform;
                var ls = tr0.localScale;
                var lossy = tr0.lossyScale;
                sb.Append(" localScale=(")
                  .Append(ls.x.ToString("0.###")).Append(",")
                  .Append(ls.y.ToString("0.###")).Append(",")
                  .Append(ls.z.ToString("0.###")).Append(")")
                  .Append(" lossy=(")
                  .Append(lossy.x.ToString("0.###")).Append(",")
                  .Append(lossy.y.ToString("0.###")).Append(",")
                  .Append(lossy.z.ToString("0.###")).Append(")");
                if (ls.x < 0 || ls.y < 0 || ls.z < 0 || lossy.x < 0 || lossy.y < 0 || lossy.z < 0)
                    sb.Append("  ⚠NEGATIVE_SCALE");
            }
            catch { }

            // 法线抽样：取第一个有 MeshFilter 的渲染器，累加法线（世界空间 Y/Z 分量）
            try
            {
                MeshFilter mf = null;
                try { mf = go.GetComponentInChildren<MeshFilter>(); } catch { }
                var mesh = mf != null ? mf.sharedMesh : null;
                if (mesh != null)
                {
                    var norms = mesh.normals;
                    if (norms != null && norms.Length > 0)
                    {
                        float sumY = 0f, sumZ = 0f, sumX = 0f;
                        int step = Math.Max(1, norms.Length / 200);
                        int n = 0;
                        for (int i = 0; i < norms.Length; i += step)
                        {
                            var v = norms[i];
                            sumX += v.x; sumY += v.y; sumZ += v.z; n++;
                        }
                        if (n > 0)
                            sb.Append("  normals(n=").Append(n).Append(") avg=(")
                              .Append((sumX / n).ToString("0.###")).Append(",")
                              .Append((sumY / n).ToString("0.###")).Append(",")
                              .Append((sumZ / n).ToString("0.###")).Append(")");
                    }
                    else sb.Append("  normals=none");
                }
            }
            catch (Exception e) { sb.Append("  normals err ").Append(e.Message); }

            // ★ 子对象名单（用于对比"复制段 vs 原始段"缺了哪些）
            try
            {
                var tr = go.transform;
                int n = tr.childCount;
                sb.Append(" children(").Append(n).Append(")=");
                for (int i = 0; i < n && i < 14; i++)
                {
                    var c = tr.GetChild(i);
                    if (c == null) continue;
                    sb.Append(c.name);
                    try { if (!c.gameObject.activeSelf) sb.Append("*"); } catch { }
                    if (i < n - 1 && i < 13) sb.Append(",");
                }

                // 渲染器挂在哪些子节点上（只列前 10 个，便于对齐）
                sb.Append(" rend@=");
                int shown = 0;
                for (int i = 0; i < n && shown < 10; i++)
                {
                    var c = tr.GetChild(i);
                    if (c == null) continue;
                    Renderer r0 = null;
                    try { r0 = c.GetComponent<Renderer>(); } catch { }
                    if (r0 == null) continue;
                    string mn = "?";
                    try { if (r0.sharedMaterial != null) mn = r0.sharedMaterial.name; } catch { }
                    sb.Append(c.name).Append(":").Append(mn).Append(" ");
                    shown++;
                }
            }
            catch (Exception e) { sb.Append(" children err ").Append(e.Message); }

            try
            {
                var rends = go.GetComponentsInChildren<Renderer>();
                sb.Append(" renderers=").Append(rends != null ? rends.Length : -1);

                if (rends != null)
                {
                    for (int i = 0; i < rends.Length && i < 12; i++)
                    {
                        var r = rends[i];
                        if (r == null) continue;

                        string mat = "?";
                        int matId = 0;
                        try
                        {
                            var sm = r.sharedMaterial;
                            if (sm != null) { mat = sm.name; matId = sm.GetInstanceID(); }
                        }
                        catch { }

                        string goName = "?";
                        try { goName = r.gameObject.name; } catch { }
                        bool en = false;
                        try { en = r.enabled; } catch { }
                        string lodInfo = "";
                        try
                        {
                            var lg = r.GetComponentInParent<LODGroup>();
                            if (lg != null) lodInfo = " lodGroup=" + lg.name;
                        }
                        catch { }
                        sb.Append("  [r").Append(i)
                          .Append(" go=").Append(goName)
                          .Append(" enabled=").Append(en)
                          .Append(" mat=").Append(mat)
                          .Append("#").Append(matId)
                          .Append(" cast=").Append(r.shadowCastingMode)
                          .Append(lodInfo)
                          .Append("]");
                    }
                }
            }
            catch (Exception e) { sb.Append(" rend err ").Append(e.Message); }

            return sb.ToString();
        }

        private static string SafeName(Ship s)
        {
            try { return s.vesselName; } catch { }
            try { return s.name; } catch { }
            return "?";
        }
    }
}
