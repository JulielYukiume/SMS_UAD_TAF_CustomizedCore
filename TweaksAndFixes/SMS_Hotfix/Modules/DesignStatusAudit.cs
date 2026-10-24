using System;
using System.Collections.Generic;
using HarmonyLib;
using Il2Cpp;
using TAFHotfixBatch1;

namespace TAFHotfixBatch1.Modules
{
    /// <summary>
    /// [SMS.1.STA] 设计 status 稽核（只读 + 可选修复）。
    ///
    /// 背景：port17 实测发现——改造产出的设计在存档里 `status = 1`（应为 0），
    /// 说明 A 模块（RefitChainFix 的 `CloneDesign` Postfix）在这条路径上没生效或被后续步骤覆盖。
    /// 本模块用于**先取证**：
    ///   · 列出所有 `isDesign == true` 且 `status != 0` 的设计（名称/状态/designId/是否赔偿）；
    ///   · `design_status_fix=1` 时把它们统一置 0（与 A 模块的规则一致）。
    ///
    /// 只读取设计对象，不触碰实船。
    /// </summary>
    internal static class DesignStatusAudit
    {
        private static bool _done;

        internal static void Install(HarmonyLib.Harmony harmony)
        {
            if (!HotfixParams.DesignStatusAudit)
            {
                HotfixLog.Info("[SMS.1.STA] disabled");
                return;
            }

            try
            {
                var m = AccessTools.Method(typeof(GameManager), "ChangeState");
                if (m == null) { HotfixLog.Warn("[SMS.1.STA] ChangeState not found"); return; }

                harmony.Patch(m, postfix: new HarmonyMethod(
                    AccessTools.Method(typeof(DesignStatusAudit), nameof(Postfix_ChangeState))));
                HotfixLog.Info("[SMS.1.STA] armed (design status audit"
                               + (HotfixParams.DesignStatusFix ? ", fix=on" : ", fix=off") + ")");
            }
            catch (Exception e)
            {
                HotfixLog.Warn("[SMS.1.STA] install: " + e.Message);
            }
        }

        internal static void Postfix_ChangeState(GameManager.GameState newState)
        {
            if (_done) return;
            if (newState.ToString() != "World") return;
            _done = true;

            try { Run(); }
            catch (Exception e) { HotfixLog.Warn("[SMS.1.STA] run: " + e.Message); }
        }

        private static void Run()
        {
            var cc = CampaignController.Instance;
            var data = cc != null ? cc.CampaignData : null;
            var vessels = data != null ? data.Vessels : null;
            if (vessels == null) return;

            int designs = 0, bad = 0, fixedCount = 0;

            for (int i = 0; i < vessels.Count; i++)
            {
                VesselEntity ve = null;
                try { ve = vessels[i]; } catch { }
                var ship = ve as Ship;
                if (ship == null) continue;

                Ship.Store st = null;
                try { st = ship.ToStore(); } catch { }
                if (st == null) continue;

                bool isDesign = false;
                try { isDesign = ship.isDesign; } catch { }
                if (!isDesign) continue;

                designs++;

                int status = -1;
                try { status = (int)ship.status; } catch { }
                if (status == 0) continue;

                bad++;

                bool isRep = false;
                try { isRep = ship.isReparationDesign; } catch { }

                string name = "";
                try { name = ship.vesselName; } catch { }

                HotfixLog.Warn("[SMS.1.STA] design '" + name + "' status=" + status
                               + " reparation=" + isRep + " index=" + i);

                if (HotfixParams.DesignStatusFix)
                {
                    try
                    {
                        ship.status = (Ship.Status)0;
                        fixedCount++;
                        HotfixLog.Info("[SMS.1.STA] FIXED '" + name + "' status " + status + " -> 0");
                    }
                    catch (Exception e) { HotfixLog.Warn("[SMS.1.STA] fix failed: " + e.Message); }
                }
            }

            HotfixLog.Info("[SMS.1.STA] summary: designs=" + designs + " nonZeroStatus=" + bad
                           + " fixed=" + fixedCount);
        }
    }
}
