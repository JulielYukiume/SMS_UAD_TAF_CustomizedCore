using System;
using HarmonyLib;
using Il2Cpp;

namespace TAFHotfixBatch1.Modules
{
    /// <summary>
    /// Module E —— 改造后船的状态/造价没有刷新（内存状态不一致，用户第 25 轮报告）。
    ///
    /// 用户现象：
    ///   1) 选定船开始改造后，造价缓存仍按旧（昂贵）值 ⇒ 需要"存档→主界面→重读档"才恢复；
    ///   2) 修了造价之后，**船的其它信息（排水量/航速/装甲/船员需求等）仍未刷新**；
    ///   3) 用户建议：被选中的船应当**整体重算**；或为省内存，**直接用改造设计的参数覆写**这些船
    ///      （"用设计代表所有这里被选中的船"）。
    ///
    /// 挂钩点（**已修正**：该方法属于 `PlayerController`，不是 CampaignController）：
    ///   `PlayerController::RefitShipsStart(List&lt;Ship&gt; shipList, Ship refitDesign, bool isPlayer)`
    ///
    /// 做法（两级）：
    ///   ① **覆写**（默认开启，`refit_state_overwrite=1`）：
    ///        own  = ship.ToStore()            // 先保存船自身的"身份/生命周期"字段
    ///        src  = refitDesign.ToStore()     // 改造设计的舰体/部件/装甲等
    ///        src.id = ship.id
    ///        ship.FromStore(src, ship.id, null, null, false)   // 用设计覆写
    ///        再把 own 里的船专属字段盖回去：status / 港口 / 位置 / 船员% / 改造进度 / 任务 / 参战回合
    ///        ship.design = refitDesign
    ///   ② **重算**：`NeedRecalcStats()` + `CalcWeightAndCost(force:true, updateCached:true)`
    ///
    /// 只处理本次被选中的船（不做全船重算，避免性能负担）。
    /// 红线：不删船、不改设计表、不推进回合；身份字段全部保留。
    /// </summary>
    internal static class RefitCostRefresh
    {
        /// <summary>等待"装甲/分舱结算完成"后再重算的船（延后两帧）。</summary>
        private static readonly System.Collections.Generic.List<Ship> _pendingRecalc =
            new System.Collections.Generic.List<Ship>();
        internal static void Install(HarmonyLib.Harmony harmony)
        {
            if (!HotfixParams.RefitCostRefresh)
            {
                HotfixLog.Info("[SMS.1.E] RefitCostRefresh: disabled by config");
                return;
            }

            try
            {
                var m = AccessTools.Method(typeof(PlayerController), "RefitShipsStart");
                if (m == null)
                {
                    HotfixLog.Warn("[SMS.1.E] PlayerController::RefitShipsStart not found -- module E inactive");
                    return;
                }

                harmony.Patch(m, postfix: new HarmonyMethod(
                    AccessTools.Method(typeof(RefitCostRefresh), nameof(Postfix_RefitShipsStart))));

                HotfixLog.Info("[SMS.1.E] RefitCostRefresh: patched PlayerController::RefitShipsStart"
                               + "  (overwrite state from refit design + recalc cost)"
                               + "  overwrite=" + (HotfixParams.RefitStateOverwrite ? 1 : 0)
                               + "  mode=" + HotfixParams.Mode);
            }
            catch (Exception e)
            {
                HotfixLog.Error("[SMS.1.E] install failed: " + e);
            }
        }

        internal static void Postfix_RefitShipsStart(PlayerController __instance,
                                                     Il2CppSystem.Collections.Generic.List<Ship> shipList,
                                                     Ship refitDesign, bool isPlayer)
        {
            if (!HotfixParams.IsApply || !HotfixParams.RefitCostRefresh) return;
            if (shipList == null) return;
            if (!isPlayer && !HotfixParams.RefitAiToo)
            {
                // 默认只处理玩家自己的改造；AI 的改造不动，避免引入无法验证的副作用。
                // 置 refit_ai_too=1 可一并处理 AI（用于验证，见 docs/44）。
                HotfixLog.Info("[SMS.1.E] skip AI refit (" + shipList.Count + " ship(s)) -- player-only"
                               + " (set refit_ai_too=1 to include AI)");
                return;
            }
            bool aiRefit = !isPlayer;
            if (aiRefit) HotfixLog.Info("[SMS.1.E] AI refit enabled: " + shipList.Count + " ship(s)");

            int n = 0, ok = 0, okInvalidate = 0;
            string dn = SafeName(refitDesign);

            for (int i = 0; i < shipList.Count; i++)
            {
                Ship s = shipList[i];
                if (s == null) continue;
                n++;

                float before = -1f, after = -1f;
                try { before = s.Cost(); } catch { }
                var sw = System.Diagnostics.Stopwatch.StartNew();

                // ★★★ 第 28 轮：版本对比得出的正确修法 —— 只置"失效标记"，让引擎自己惰性重算。
                //   依据：1.5.1.6 的 Ship::Cost() 是"每次现算"（31 指令）；
                //         1.7 改成缓存版（62 指令，按 IsCampaign/IsWorld 分支读缓存），
                //         并新增 CalcWeightAndCost / NeedRecalcStats（后者仅 10 指令 = 纯置失效标记）。
                //   ⇒ 1.7 的优化漏掉了"改造时失效"，我们只需补这一次标记，
                //     引擎会在下次读取（界面刷新）时用**当时正确的部件/装甲状态**重算。
                // ---- 仅用于性能测试：模拟"整船重读一遍"（ToStore→FromStore→重算）----
                //   ⚠ 会重建该船的部件对象，**只应在测试存档上开启**（refit_bench=1）。
                if (HotfixParams.RefitBench)
                {
                    var bsw = System.Diagnostics.Stopwatch.StartNew();
                    try
                    {
                        Ship.Store own = s.ToStore();
                        Player p = null;
                        try { if (own != null && !string.IsNullOrEmpty(own.playerName)) p = LoadSave.GetPlayer(own.playerName, false); } catch { }
                        var mid = new Il2CppSystem.Nullable<Il2CppSystem.Guid>(s.id);
                        bool okB = s.FromStore(own, mid, null, p, false);
                        s.NeedRecalcStats();
                        float c = -1f; try { c = s.Cost(); } catch { }
                        bsw.Stop();
                        HotfixLog.Warn("[SMS.1.E][bench] '" + SafeName(s) + "': ToStore+FromStore+recalc = "
                                       + bsw.ElapsedMilliseconds + " ms  (ok=" + okB + ", cost=" + c.ToString("0") + ")");
                    }
                    catch (Exception ex)
                    {
                        bsw.Stop();
                        HotfixLog.Warn("[SMS.1.E][bench] '" + SafeName(s) + "': failed after " + bsw.ElapsedMilliseconds + " ms : " + ex.Message);
                    }
                }

                // ---- 诊断：设计与船各自的 造价/吨位（用于与"造船厂里的正确值"三方对照）----
                if (HotfixParams.RefitDiag)
                {
                    try
                    {
                        float dc = -1f, dt = -1f;
                        try { dc = refitDesign != null ? refitDesign.Cost() : -1f; } catch { }
                        try { if (refitDesign != null) { var ds = refitDesign.ToStore(); if (ds != null) dt = ds.tonnage; } } catch { }
                        float st2 = -1f;
                        try { var ss = s.ToStore(); if (ss != null) st2 = ss.tonnage; } catch { }
                        float sc = -1f;
                        try { sc = s.Cost(); } catch { }
                        int spp = -1, dpp = -1;
                        try { var z1 = s.ToStore(); if (z1 != null) spp = z1.parts != null ? z1.parts.Count : 0; } catch { }
                        try { var z2 = refitDesign != null ? refitDesign.ToStore() : null; if (z2 != null) dpp = z2.parts != null ? z2.parts.Count : 0; } catch { }
                        HotfixLog.Info("[SMS.1.E][diag] parts: ship=" + spp + " design=" + dpp
                                       + (spp > 0 && dpp > 0 && spp > dpp * 3 / 2 ? "  <-- PARTS LOOK DOUBLED" : ""));
                        HotfixLog.Info("[SMS.1.E][diag] design '" + dn + "': cost=" + dc.ToString("0")
                                       + " tonnage=" + dt.ToString("0.##")
                                       + "  ||  ship '" + SafeName(s) + "': cost=" + sc.ToString("0")
                                       + " tonnage=" + st2.ToString("0.##"));
                    }
                    catch (Exception ex) { HotfixLog.Warn("[SMS.1.E][diag] failed: " + ex.Message); }
                }

                if (HotfixParams.RefitInvalidate)
                {
                    try { s.NeedRecalcStats(); okInvalidate++; }
                    catch (Exception ex) { HotfixLog.Warn("[SMS.1.E] '" + SafeName(s) + "': NeedRecalcStats failed: " + ex.Message); }
                }

                if (HotfixParams.RefitDeferredRecalc) _pendingRecalc.Add(s);

                if (HotfixParams.RefitStateOverwrite && refitDesign != null)
                {
                    if (OverwriteFromDesign(s, refitDesign)) ok++;
                }
                else
                {
                    try { s.NeedRecalcStats(); } catch { }
                    try { s.CalcWeightAndCost(true, true); ok++; } catch { }
                }

                sw.Stop();
                try { after = s.Cost(); } catch { }

                if (n <= 20)
                    HotfixLog.Info("[SMS.1.E] refit refresh: '" + SafeName(s) + "' -> design '" + dn + "'"
                                   + "  cost " + before.ToString("0.#") + " -> " + after.ToString("0.#")
                                       + "  (" + sw.ElapsedMilliseconds + " ms)");
            }

            if (HotfixParams.RefitDeferredRecalc && _pendingRecalc.Count > 0)
            {
                try { MelonLoader.MelonCoroutines.Start(DeferredRecalc()); }
                catch (Exception ex) { HotfixLog.Warn("[SMS.1.E] deferred recalc coroutine failed: " + ex.Message); }
            }

            HotfixLog.Info("[SMS.1.E] refit start: " + n + " ship(s), " + ok + " refreshed to design '" + dn + "'"
                           + "  (isPlayer=" + isPlayer + ", aiRefit=" + aiRefit + ", invalidate=" + okInvalidate + ", overwrite=" + (HotfixParams.RefitStateOverwrite ? 1 : 0) + ")");
        }

        /// <summary>
        /// 用改造设计覆写船：**在 store 层合并** ——
        /// 设计的舰体/部件/装甲等 + 船自身的身份与生命周期字段，最后一次性 FromStore。
        /// 这样避免逐个访问运行时属性（名称/类型都与 store 不同，易错）。
        /// </summary>
        private static bool OverwriteFromDesign(Ship ship, Ship design)
        {
            try
            {
                Ship.Store own = null, src = null;
                try { own = ship.ToStore(); } catch { }
                try { src = design.ToStore(); } catch { }
                if (src == null)
                {
                    HotfixLog.Warn("[SMS.1.E] '" + SafeName(ship) + "': design ToStore() failed -- fallback to recalc");
                    try { ship.NeedRecalcStats(); ship.CalcWeightAndCost(true, true); return true; } catch { return false; }
                }

                // ---- 把"船专属"字段并入设计 store ----
                if (own != null)
                {
                    try { src.id = own.id; } catch { }
                    // ★ 舰名/身份：绝不能跟着设计走（第 26 轮 bug：把舰名覆盖成了设计的名字）
                    try { src.vesselName = own.vesselName; } catch { }
                    try { src.seaGoupId = own.seaGoupId; } catch { }
                    try { src.YearCreated = own.YearCreated; } catch { }
                    try { src.Fuel = own.Fuel; } catch { }
                    try { src.ShipFuelReplenised = own.ShipFuelReplenised; } catch { }
                    try { src.isBuildingPaused = own.isBuildingPaused; } catch { }
                    try { src.isRepairingPaused = own.isRepairingPaused; } catch { }
                    try { src.isRefitPaused = own.isRefitPaused; } catch { }
                    try { src.isRefitSimple = own.isRefitSimple; } catch { }
                    try { src.shipBuildingPortLocation = own.shipBuildingPortLocation; } catch { }
                    try { src.returnAfterRepairToGroup = own.returnAfterRepairToGroup; } catch { }
                    try { src.DelayedForBattle = own.DelayedForBattle; } catch { }
                    try { src.ReinforcementBattleId = own.ReinforcementBattleId; } catch { }
                    try { src.lowCrewWasNotified = own.lowCrewWasNotified; } catch { }
                    try { src.portLocation = own.portLocation; } catch { }
                    try { src.location = own.location; } catch { }
                    try { src.sailingTo = own.sailingTo; } catch { }
                    try { src.prevPortLocation = own.prevPortLocation; } catch { }
                    try { src.status = own.status; } catch { }
                    try { src.CrewAmountPercents = own.CrewAmountPercents; } catch { }
                    try { src.refitProgress = own.refitProgress; } catch { }
                    try { src.buildingProgress = own.buildingProgress; } catch { }
                    try { src.repairingProgress = own.repairingProgress; } catch { }
                    try { src.mission = own.mission; } catch { }
                    try { src.LastBattleParticipating = own.LastBattleParticipating; } catch { }
                    try { src.dateCreated = own.dateCreated; } catch { }
                    try { src.dateFinished = own.dateFinished; } catch { }
                    try { src.playerName = own.playerName; } catch { }
                    try { src.playerOriginal = own.playerOriginal; } catch { }
                    try { src.manualMothballed = own.manualMothballed; } catch { }
                    try { src.refitDesignListID = own.refitDesignListID; } catch { }
                    try { src.isReparationDesign = own.isReparationDesign; } catch { }
                    try { src.isSharedDesign = own.isSharedDesign; } catch { }
                }

                bool okFrom = false;
                try
                {
                    var mid = new Il2CppSystem.Nullable<Il2CppSystem.Guid>(src.id);
                    Player p = null;
                    try { if (!string.IsNullOrEmpty(src.playerName)) p = LoadSave.GetPlayer(src.playerName, false); } catch { }
                    okFrom = ship.FromStore(src, mid, null, p, false);
                }
                catch (Exception e)
                {
                    HotfixLog.Warn("[SMS.1.E] '" + SafeName(ship) + "': FromStore failed: " + e.Message);
                }

                try { ship.design = design; } catch { }
                // ★ 不再强制重算：FromStore 已按装载流程建立正确的重量/造价缓存。
                //   在"装甲分区/分舱权重尚未结算"的时刻调用 CalcWeightAndCost(force:true)
                //   会漏掉装甲重量与材料成本 ⇒ 吨位/造价偏低（第 26 轮实测：造价降约 5 倍）。
                bool okCost = false;
                // ★★ 第 27 轮：补上"分区/装甲结算"这一步（缺它就永远算不准）。
                //   原版"查看该舰" = ToConstructor(...)，会把整船重新载入造船厂（含建模）。
                //   这里只调用**公开的轻量接口**：
                //     RefreshHull(updateSections:true) → RefreshMounts() → CalcWeightAndCost()
                //   不调用 RefreshDecor() / ConstructorM.ReloadModels()（那才是 3D/模型部分）。
                if (HotfixParams.RefitFullRecalc)
                {
                    try { ship.NeedRecalcStats(); } catch { }
                    try { ship.RefreshHull(true); }
                    catch (Exception e) { HotfixLog.Warn("[SMS.1.E] '" + SafeName(ship) + "': RefreshHull failed: " + e.Message); }
                    try { ship.RefreshMounts(); }
                    catch (Exception e) { HotfixLog.Warn("[SMS.1.E] '" + SafeName(ship) + "': RefreshMounts failed: " + e.Message); }
                    try { ship.CalcWeightAndCost(true, true); }
                    catch (Exception e) { HotfixLog.Warn("[SMS.1.E] '" + SafeName(ship) + "': CalcWeightAndCost failed: " + e.Message); }
                }

                if (HotfixParams.RefitForceRecalc)
                {
                    try { ship.NeedRecalcStats(); } catch { }
                    try { ship.CalcWeightAndCost(true, true); okCost = true; }
                    catch (Exception e) { HotfixLog.Warn("[SMS.1.E] '" + SafeName(ship) + "': CalcWeightAndCost failed: " + e.Message); }
                }
                else
                {
                    okCost = true;   // 信任 FromStore 建好的缓存
                }

                if (!okFrom)
                    HotfixLog.Warn("[SMS.1.E] '" + SafeName(ship) + "': FromStore returned false (state may be partial)");

                return okFrom || okCost;
            }
            catch (Exception e)
            {
                HotfixLog.Warn("[SMS.1.E] overwrite failed for '" + SafeName(ship) + "': " + e.Message);
                return false;
            }
        }
        /// <summary>
        /// 延后两帧再重算：`RefitShipsStart` 当下装甲分区/分舱权重尚未结算，
        /// 此刻 CalcWeightAndCost 会漏掉装甲重量（造价偏低）；
        /// 而完全不重算又会保留旧缓存（造价偏高）。折中 = 延迟到结算之后再算。
        /// </summary>
        private static System.Collections.IEnumerator DeferredRecalc()
        {
            var ships = new System.Collections.Generic.List<Ship>(_pendingRecalc);
            _pendingRecalc.Clear();

            yield return null;
            yield return null;

            int ok = 0;
            foreach (Ship s in ships)
            {
                if (s == null) continue;
                try { if (s.WasCollected) continue; } catch { }

                float before = -1f, after = -1f;
                try { before = s.Cost(); } catch { }

                try { s.NeedRecalcStats(); } catch { }
                try { s.CalcWeightAndCost(true, true); ok++; }
                catch (Exception e) { HotfixLog.Warn("[SMS.1.E] deferred CalcWeightAndCost failed: " + e.Message); }

                try { after = s.Cost(); } catch { }
                HotfixLog.Info("[SMS.1.E] deferred recalc: \x27" + SafeName(s) + "\x27  cost "
                               + before.ToString("0.#") + " -> " + after.ToString("0.#"));
            }
            HotfixLog.Info("[SMS.1.E] deferred recalc done: " + ok + "/" + ships.Count + " ship(s)");
        }

        private static string SafeName(Ship s) { try { return s == null ? "<null>" : s.name; } catch { return "<unnamed>"; } }
    }
}
