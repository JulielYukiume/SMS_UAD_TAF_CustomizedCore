using System;
using System.Reflection;
using HarmonyLib;
using Il2Cpp;

namespace TAFHotfixBatch1.Modules
{
    /// <summary>
    /// Module E（方案 B）—— **显示层修正**：改造中的船按"改造设计"计价。
    ///
    /// 背景（第 25–28 轮实测）：
    ///   · 1.5.1.6 的 `Ship.Cost()` 是**每次现算**（31 条指令）；
    ///   · 1.7 改成**缓存版**（62 指令，按 IsCampaign/IsWorld 分支），并新增
    ///     `CalcWeightAndCost` / `NeedRecalcStats`；**改造流程没有触发失效** ⇒ 缓存保留旧价。
    ///   · 在活船对象上无论怎么刷新都算不准：`RefreshHull` 会 NRE，强算会漏装甲（造价降 18 倍）。
    ///     造船厂之所以正确，是因为它用设计 store **新建**了一个干净的 Ship 对象。
    ///   · 实测：设计自身造价可直接读到（例：638,974,000），而失效标记后船算出 659,874,900（+3.27%）。
    ///
    /// 做法（用户选定方案 B，等价于"显示层用设计代表改造中的船"）：
    ///   Postfix `Ship::Cost()` 与 `Ship::MaintenanceCost()`：
    ///     当且仅当 **船处于 Refit(10)** 且 `designShipForRefit != null` 时，
    ///     直接返回该设计的对应数值。
    ///   · 不写任何缓存、不重建部件、不影响未改造的船与设计对象本身；
    ///   · 未改造的船、设计对象、AI 船一律原样返回。
    ///
    /// 开关：`refit_cost_display`（默认 1；0 = 回到原始的旧价显示）。
    /// </summary>
    internal static class RefitCostDisplay
    {
        private static bool _inCost;        // 防重入（rd.Cost() 会再次进入本 Postfix）
        private static int _logged;
        private static int _noDesignLog;
        private static readonly System.Collections.Generic.HashSet<string> _seen =
            new System.Collections.Generic.HashSet<string>();
        private static string SafeId(Ship s) { try { return s.id.ToString(); } catch { return "?"; } }

        internal static void Install(HarmonyLib.Harmony harmony)
        {
            if (!HotfixParams.RefitCostDisplay)
            {
                HotfixLog.Info("[SMS.1.E] RefitCostDisplay: disabled by config");
                return;
            }

            try
            {
                var cost = AccessTools.Method(typeof(Ship), "Cost");
                if (cost == null)
                {
                    HotfixLog.Warn("[SMS.1.E] Ship::Cost not found -- display fix inactive");
                    return;
                }
                harmony.Patch(cost, postfix: new HarmonyMethod(
                    AccessTools.Method(typeof(RefitCostDisplay), nameof(Postfix_Cost))));

                // 当前排水量：界面读 Ship::Weight(force, updateCache) —— 这是 11,472 的真正来源
                var wm = AccessTools.Method(typeof(Ship), "Weight");
                if (wm != null)
                    harmony.Patch(wm, postfix: new HarmonyMethod(
                        AccessTools.Method(typeof(RefitCostDisplay), nameof(Postfix_Weight))));
                else
                    HotfixLog.Warn("[SMS.1.E] Ship::Weight not found -- displacement display fix inactive");

                // 吨位：界面读的是 Ship::TonnageMin/TonnageMax（不是 store.tonnage）
                foreach (var nm in new string[] { "TonnageMin", "TonnageMax" })
                {
                    var tm = AccessTools.Method(typeof(Ship), nm);
                    if (tm == null) { HotfixLog.Warn("[SMS.1.E] Ship::" + nm + " not found"); continue; }
                    harmony.Patch(tm, postfix: new HarmonyMethod(
                        AccessTools.Method(typeof(RefitCostDisplay), nameof(Postfix_Tonnage))));
                }

                var maint = AccessTools.Method(typeof(Ship), "MaintenanceCost");
                if (maint != null)
                    harmony.Patch(maint, postfix: new HarmonyMethod(
                        AccessTools.Method(typeof(RefitCostDisplay), nameof(Postfix_MaintenanceCost))));

                HotfixLog.Info("[SMS.1.E] RefitCostDisplay: patched Ship::Cost"
                               + (maint != null ? " + Ship::MaintenanceCost" : "")
                               + "  (ships under Refit report the refit design's values)"
                               + "  mode=" + HotfixParams.Mode);
            }
            catch (Exception e)
            {
                HotfixLog.Error("[SMS.1.E] RefitCostDisplay install failed: " + e);
            }
        }

        internal static void Postfix_Cost(Ship __instance, ref float __result)
        {
            if (!HotfixParams.IsApply || !HotfixParams.RefitCostDisplay) return;
            if (_inCost) return;

            try
            {
                Ship rd = PendingRefitDesign(__instance);
                if (rd == null) return;

                _inCost = true;
                float v;
                try { v = rd.Cost(); }
                finally { _inCost = false; }

                if (v <= 0f) return;
                if (Math.Abs(v - __result) < 0.5f) return;

                // 吨位探针：一次运行即可确定"实际吨位"对应哪个 API（便于下一步修正）
                if (HotfixParams.Verbose && _logged < 300)
                {
                    try
                    {
                        float smin = -1f, smax = -1f, sst = -1f, dmin = -1f, dmax = -1f, dst = -1f;
                        try { smin = __instance.TonnageMin(); } catch { }
                        try { smax = __instance.TonnageMax(); } catch { }
                        try { var ss = __instance.ToStore(); if (ss != null) sst = ss.tonnage; } catch { }
                        try { dmin = rd.TonnageMin(); } catch { }
                        try { dmax = rd.TonnageMax(); } catch { }
                        try { var ds2 = rd.ToStore(); if (ds2 != null) dst = ds2.tonnage; } catch { }
                        HotfixLog.Debug("[SMS.1.E][fix] tonnage probe: ship '" + SafeName(__instance) + "' min=" + smin.ToString("0.##")
                                       + " max=" + smax.ToString("0.##") + " store=" + sst.ToString("0.##")
                                       + "  || design min=" + dmin.ToString("0.##") + " max=" + dmax.ToString("0.##")
                                       + " store=" + dst.ToString("0.##"));

                        // 重量探针：部件数 / 装甲条目 / 船宽吃水 / 超重缺重
                        //   若 ship.parts 明显多于 design.parts ⇒ 说明历史上 FromStore 覆写把部件**重复计入**了。
                        int sp = -1, dp = -1, sa = -1, da = -1;
                        float sbe = -1f, sdr = -1f, sow = -1f, suw = -1f;
                        float dbe = -1f, ddr = -1f, dow = -1f, duw = -1f;
                        try
                        {
                            var ss2 = __instance.ToStore();
                            if (ss2 != null)
                            {
                                sp = ss2.parts != null ? ss2.parts.Count : 0;
                                sa = ss2.armor != null ? ss2.armor.Count : 0;
                                sbe = ss2.beam; sdr = ss2.draught; sow = ss2.overweight; suw = ss2.underweight;
                            }
                        }
                        catch { }
                        try
                        {
                            var ds3 = rd.ToStore();
                            if (ds3 != null)
                            {
                                dp = ds3.parts != null ? ds3.parts.Count : 0;
                                da = ds3.armor != null ? ds3.armor.Count : 0;
                                dbe = ds3.beam; ddr = ds3.draught; dow = ds3.overweight; duw = ds3.underweight;
                            }
                        }
                        catch { }
                        HotfixLog.Debug("[SMS.1.E][fix] weight probe SHIP: '" + SafeName(__instance) + "'"
                                       + " parts=" + sp + " armor=" + sa
                                       + " beam=" + sbe.ToString("0.###") + " draught=" + sdr.ToString("0.###")
                                       + " overweight=" + sow.ToString("0.###") + " underweight=" + suw.ToString("0.###")
                                       + " tonMin=" + smin.ToString("0.##") + " tonMax=" + smax.ToString("0.##"));
                        HotfixLog.Debug("[SMS.1.E][fix] weight probe DESG: '" + SafeName(rd) + "'"
                                       + " parts=" + dp + " armor=" + da
                                       + " beam=" + dbe.ToString("0.###") + " draught=" + ddr.ToString("0.###")
                                       + " overweight=" + dow.ToString("0.###") + " underweight=" + duw.ToString("0.###")
                                       + " tonMin=" + dmin.ToString("0.##") + " tonMax=" + dmax.ToString("0.##"));
                    }
                    catch { }
                }

                if (HotfixParams.Verbose && _logged++ < 300)
                    HotfixLog.Debug("[SMS.1.E][fix] cost: '" + SafeName(__instance) + "' (Refit) : "
                                   + __result.ToString("0") + " -> " + v.ToString("0")
                                   + "   [design '" + SafeName(rd) + "']");
                __result = v;
            }
            catch { }
        }

        internal static void Postfix_MaintenanceCost(Ship __instance, ref float __result)
        {
            if (!HotfixParams.IsApply || !HotfixParams.RefitCostDisplay) return;
            if (_inCost) return;

            try
            {
                Ship rd = PendingRefitDesign(__instance);
                if (rd == null) return;

                _inCost = true;
                float v;
                try { v = rd.MaintenanceCost(); }
                finally { _inCost = false; }

                if (v <= 0f) return;
                if (Math.Abs(v - __result) < 0.5f) return;

                if (HotfixParams.Verbose && _logged++ < 300)
                    HotfixLog.Debug("[SMS.1.E][fix] maintenance: '" + SafeName(__instance) + "' (Refit) : "
                                   + __result.ToString("0") + " -> " + v.ToString("0"));
                __result = v;
            }
            catch { }
        }

        /// <summary>当前排水量：改造中的船返回"设计"的 Weight（强制计算，不写缓存）。</summary>
        internal static void Postfix_Weight(Ship __instance, ref float __result)
        {
            if (!HotfixParams.IsApply || !HotfixParams.RefitCostDisplay) return;
            if (_inCost) return;

            try
            {
                Ship rd = PendingRefitDesign(__instance);
                if (rd == null) return;

                _inCost = true;
                float v;
                try { v = rd.Weight(true, false); }
                finally { _inCost = false; }

                if (v <= 0f) return;
                if (Math.Abs(v - __result) < 0.5f) return;

                string key = "W|" + SafeId(__instance) + "|" + __result.ToString("0") + "->" + v.ToString("0");
                if (_seen.Add(key))
                {
                    float tmax = -1f, wf = -1f;
                    try { tmax = rd.TonnageMax(); } catch { }
                    try { wf = rd.Weight(true, false); } catch { }
                    HotfixLog.Debug("[SMS.1.E][fix] Weight: '" + SafeName(__instance) + "' (Refit) : "
                                   + __result.ToString("0.##") + " -> " + v.ToString("0.##")
                                   + "   [design Weight=" + wf.ToString("0.##") + " TonnageMax=" + tmax.ToString("0.##")
                                   + "  usage=" + (tmax > 0f ? (wf / tmax * 100f).ToString("0.0") + "%" : "?") + "]");
                }
                __result = v;
            }
            catch { }
        }

        /// <summary>`TonnageMin()` / `TonnageMax()` 的共用 Postfix。</summary>
        internal static void Postfix_Tonnage(Ship __instance, ref float __result, MethodBase __originalMethod)
        {
            if (!HotfixParams.IsApply || !HotfixParams.RefitCostDisplay) return;
            if (_inCost) return;

            try
            {
                Ship rd = PendingRefitDesign(__instance);
                if (rd == null) return;

                string mn = __originalMethod != null ? __originalMethod.Name : "";
                _inCost = true;
                float v;
                try
                {
                    if (mn == "TonnageMax") v = rd.TonnageMax();
                    else if (mn == "TonnageMin") v = rd.TonnageMin();
                    else return;
                }
                finally { _inCost = false; }

                if (v <= 0f) return;
                if (Math.Abs(v - __result) < 0.01f) return;

                if (HotfixParams.Verbose && _logged++ < 300)
                    HotfixLog.Debug("[SMS.1.E][fix] " + mn + ": '" + SafeName(__instance) + "' (Refit) : "
                                   + __result.ToString("0.##") + " -> " + v.ToString("0.##"));
                __result = v;
            }
            catch { }
        }

        /// <summary>仅在"船正在改造"且"待应用设计存在"时返回该设计；其余一律 null。</summary>
        private static Ship PendingRefitDesign(Ship s)
        {
            if (s == null) return null;

            int st;
            try { st = (int)s.status; } catch { return null; }
            if (st != (int)VesselEntity.Status.Refit) return null;

            // ★ 第 29 轮修正：`designShipForRefit` 在实战中是 **null**；
            //   而存档实测显示：改造中的船 **designId 已经指向改造后的设计**
            //   （例：淮阳 designId=d6e6758a → '汾阳 Fenyang (Feb.1940)'）。
            //   ⇒ 直接取 `s.design`，它就是要按其计价的目标设计。
            Ship rd = null;
            try { rd = s.design; } catch { return null; }
            if (rd == null || ReferenceEquals(rd, s))
            {
                try { rd = s.designShipForRefit; } catch { }
                if (rd == null || ReferenceEquals(rd, s))
                {
                    if (_noDesignLog++ < 20)
                        HotfixLog.Warn("[SMS.1.E][fix] '" + SafeName(s) + "' is Refit but has no resolvable design"
                                       + " -- display fix cannot apply to this ship");
                    return null;
                }
            }

            return rd;
        }

        private static string SafeName(Ship s) { try { return s == null ? "<null>" : s.name; } catch { return "<unnamed>"; } }
    }
}
