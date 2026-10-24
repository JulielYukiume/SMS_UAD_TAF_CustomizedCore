using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Il2Cpp;

namespace TAFHotfixBatch1.Modules
{
    /// <summary>
    /// dirtycheater「立即完成当前科技」修正（SMS 非原版科技树）。
    ///
    /// 原实现（TAF `Modified/CheatMenuM.cs::ResearchFocuses`）用
    ///   Regex("^" + priorityName + "(_\\d+)?$")
    /// 去匹配 `techData.name`，并额外硬编码了几条 "gun_*" 特例。
    /// SMS 改过科技树后，`techPriorities` 里的名字与实际 `techData.name` 不再同名 ⇒ 点按钮没反应。
    ///
    /// 本模块用 Prefix **整体接管** 该方法（Priority.First，return false 跳过原体）：
    ///   ① 先按"名字精确/前缀"匹配（保持原语义）；
    ///   ② 再按 techData 的 priority / category / group 字段匹配（若存在）；
    ///   ③ 仍无 ⇒ 回退"当前进度最高且未完成"的那一项（即"当前科技"）。
    /// </summary>
    internal static class CheatTechFix
    {
        internal static void Install(HarmonyLib.Harmony harmony)
        {
            if (!HotfixParams.CheatTechFix) { HotfixLog.Info("[SMS.1.T] CheatTechFix: disabled"); return; }
            try
            {
                var t = AccessTools.TypeByName("TweaksAndFixes.CheatMenu");
                if (t == null) { HotfixLog.Warn("[SMS.1.T] CheatMenu type not found"); return; }
                var m = AccessTools.Method(t, "ResearchFocuses", new Type[] { typeof(Player) });
                if (m == null) { HotfixLog.Warn("[SMS.1.T] ResearchFocuses not found"); return; }
                var hm = new HarmonyMethod(AccessTools.Method(typeof(CheatTechFix), nameof(Prefix)));
                hm.priority = Priority.First;
                harmony.Patch(m, prefix: hm);
                HotfixLog.Info("[SMS.1.T] CheatTechFix: armed on CheatMenu.ResearchFocuses");
            }
            catch (Exception e) { HotfixLog.Error("[SMS.1.T] install failed: " + e); }
        }

        internal static bool Prefix(Player player)
        {
            if (!HotfixParams.IsApply) return true;
            try
            {
                if (player == null) return true;
                int done = 0;

                List<string> priorities = new List<string>();
                try { foreach (var p in player.techPriorities) priorities.Add(p); } catch { }

                foreach (var tech in player.technologies)
                {
                    if (tech == null) continue;
                    try
                    {
                        if (tech.progress >= 100f || tech.IsEndTechResearched) continue;
                    }
                    catch { continue; }

                    string name = null;
                    object data = null;
                    try { data = tech.data; } catch { }
                    try { name = data != null ? (string)data.GetType().GetField("name")?.GetValue(data) : null; } catch { }
                    if (name == null) { try { name = tech.data.name; } catch { } }

                    if (!Matches(name, data, priorities)) continue;

                    float cur = 0f;
                    try { cur = tech.progress; } catch { }
                    try { tech.progress = 99.9f; } catch { }
                    done++;
                    HotfixLog.Info("[SMS.1.T] tech '" + name + "' progress " + cur.ToString("F1") + " -> 99.9");
                }

                if (done == 0) done += FallbackTopTech(player);

                HotfixLog.Info("[SMS.1.T] ResearchFocuses: completed " + done + " tech(s) for " + Name(player)
                               + " (priorities=" + priorities.Count + ")");
                return false;   // 跳过原实现（它只认原版命名）
            }
            catch (Exception e) { HotfixLog.Warn("[SMS.1.T] prefix: " + e.Message); return true; }
        }

        private static bool Matches(string name, object data, List<string> priorities)
        {
            if (priorities.Count == 0) return true;

            foreach (var pri in priorities)
            {
                if (string.IsNullOrEmpty(pri)) continue;
                if (name != null && (name == pri || name.StartsWith(pri + "_", StringComparison.OrdinalIgnoreCase)))
                    return true;
            }

            // techData 上的 priority / category / group 字段（若存在）
            if (data != null)
            {
                var t = data.GetType();
                foreach (var f in t.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
                {
                    if (f.Name == null || f.FieldType != typeof(string)) continue;
                    string ln = f.Name.ToLowerInvariant();
                    if (ln != "priority" && ln != "category" && ln != "group" && ln != "techpriority") continue;
                    string v = null;
                    try { v = (string)f.GetValue(data); } catch { }
                    if (string.IsNullOrEmpty(v)) continue;
                    foreach (var pri in priorities)
                        if (pri != null && (pri == v || pri.StartsWith(v, StringComparison.OrdinalIgnoreCase)
                                            || v.StartsWith(pri, StringComparison.OrdinalIgnoreCase)))
                            return true;
                }
            }
            return false;
        }

        /// <summary>没有匹配项时：完成"当前进度最高且未完成"的一项（即玩家正在研究的科技）。</summary>
        private static int FallbackTopTech(Player player)
        {
            Technology best = null; float bestProg = -1f;
            try
            {
                foreach (var tech in player.technologies)
                {
                    if (tech == null) continue;
                    try { if (tech.progress >= 100f || tech.IsEndTechResearched) continue; } catch { continue; }
                    float pr = 0f; try { pr = tech.progress; } catch { }
                    if (pr > bestProg) { bestProg = pr; best = tech; }
                }
                if (best != null)
                {
                    string nm = "?";
                    try { nm = best.data.name; } catch { }
                    try { best.progress = 99.9f; } catch { }
                    HotfixLog.Info("[SMS.1.T] fallback: highest-progress tech '" + nm + "' " + bestProg.ToString("F1") + " -> 99.9");
                    return 1;
                }
            }
            catch { }
            return 0;
        }

        private static string Name(Player p)
        {
            try { var pd = p.data; if (pd != null) return pd.name; } catch { }
            return "?";
        }
    }
}
