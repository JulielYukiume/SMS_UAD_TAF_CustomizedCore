using System;
using System.Collections.Generic;
using HarmonyLib;
using Il2Cpp;
using TAFHotfixBatch1;
using UnityEngine;

namespace TweaksAndFixes
{
    /// <summary>
    /// [SMS.1.SECT] 舯段（middle sections）自定义顺序 —— 修"舯段色差"。
    ///
    /// 原理：3.21.x 里游戏按 `middles.Count % middlesBase.Count` 循环克隆基础段，
    /// 某些船体因此把"另一套涂装的段"接进来，形成色差。
    /// 3.22 用 `hullData.csv` 的 `sectioning` 列指定"每档段数该用哪些基础段索引"来解决。
    ///
    /// 本实现**只在数据里明确指定了顺序时**才接管；否则完全不干预（保持原版行为）。
    /// 触发时机：`Ship.RefreshHull(bool updateSections)` 之后（Postfix）。
    /// </summary>
    [HarmonyPatch(typeof(Ship))]
    internal class Patch_Ship_Sections
    {
        /// <summary>与 Beta6 相同的段数公式。</summary>
        private static int CalcMiddleCount(Ship ship, PartData data)
        {
            try
            {
                float beamBonus = ship.beamDraughtBonus;
                if (beamBonus == 0f) beamBonus = 1f;

                float from = data.sectionsMin - 0.499f;
                float to = data.sectionsMax + 0.499f;
                float t = Mathf.InverseLerp(ship.TonnageMin() / beamBonus,
                                            ship.TonnageMax() / beamBonus,
                                            ship.tonnage);
                return Mathf.RoundToInt(Mathf.Lerp(from, to, t));
            }
            catch
            {
                return -1;
            }
        }

        [HarmonyPostfix]
        [HarmonyPatch(nameof(Ship.RefreshHull))]
        internal static void Postfix_RefreshHull(Ship __instance, bool updateSections)
        {
            if (__instance == null || !updateSections) return;

            try
            {
                var hull = __instance.hull;
                if (hull == null) return;

                var data = hull.data;
                if (data == null) return;

                string model = data.model;
                if (!HullData.ModifiedSectionOrder(model)) return;   // 没配置：不动

                int middleCount = CalcMiddleCount(__instance, data);
                if (middleCount <= 0) return;

                var hd = HullData.Get(model);
                if (hd == null) return;

                if (middleCount >= hd.sectioningParsed.Count)
                {
                    // 没有对应档位：保持原版（可能本档位没写）
                    return;
                }

                var order = hd.sectioningParsed[middleCount];
                if (order == null || order.Count == 0) return;
                if (order.Count == 1 && order[0] == -1) return;      // 显式"用默认"

                ApplySections(__instance, hull, order);
            }
            catch (Exception e)
            {
                HotfixLog.Warn("[SMS.1.SECT] RefreshHull postfix: " + e.Message);
            }
        }

        private static void ApplySections(Ship ship, Part hull, List<int> order)
        {
            var baseList = hull.middlesBase;
            var middles = hull.middles;
            if (baseList == null || middles == null || baseList.Count == 0) return;

            // 清空现有段
            for (int i = middles.Count - 1; i >= 0; i--)
            {
                var old = middles[i];
                middles.RemoveAt(i);
                if (old != null) UnityEngine.Object.DestroyImmediate(old);
            }

            // 按指定索引克隆
            foreach (int idx in order)
            {
                if (idx < 0 || idx >= baseList.Count)
                {
                    HotfixLog.Warn("[SMS.1.SECT] section index " + idx + " out of range ("
                                   + baseList.Count + ") on '" + ship.vesselName + "'");
                    continue;
                }

                var src = baseList[idx];
                if (src == null) continue;

                var clone = UnityEngine.Object.Instantiate(src, src.transform.parent, false);
                clone.name = src.name;
                middles.Add(clone);
                Util.SetActiveX(clone, true);
            }

            HotfixLog.Info("[SMS.1.SECT] sections rebuilt: " + order.Count + " on '" + ship.vesselName + "'");
        }
    }
}
