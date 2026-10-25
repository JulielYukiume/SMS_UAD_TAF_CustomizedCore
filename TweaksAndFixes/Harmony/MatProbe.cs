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

        private static string Describe(GameObject go)
        {
            if (go == null) return "<null>";

            var sb = new StringBuilder();
            sb.Append(go.name).Append(" active=").Append(go.activeSelf);
            try { sb.Append(" z=").Append(go.transform.localPosition.z.ToString("0.00")); } catch { }

            try
            {
                var rends = go.GetComponentsInChildren<Renderer>();
                sb.Append(" renderers=").Append(rends != null ? rends.Length : -1);

                if (rends != null)
                {
                    for (int i = 0; i < rends.Length && i < 3; i++)
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

                        sb.Append("  [r").Append(i)
                          .Append(" mat=").Append(mat)
                          .Append("#").Append(matId)
                          .Append(" recvShadow=").Append(r.receiveShadows)
                          .Append(" cast=").Append(r.shadowCastingMode)
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
