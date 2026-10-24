using System;
using System.Collections.Generic;
using HarmonyLib;
using Il2Cpp;
using TAFHotfixBatch1;

namespace TAFHotfixBatch1.Modules
{
    /// <summary>
    /// [SMS.1.TON] 实存船"原始吨位"校验（只读）。
    ///
    /// 背景：1940 中华档的「海鹄」曾出现 `tonnage` 从 14700 掉到 13000 的坏档，
    /// 原因是 3.22 的 RefitShipsStart Postfix 在"装甲/分区未结算"时立刻
    /// CalcWeightAndCost(true,true) 并把偏低的吨位写回 ship（已在该分支禁用）。
    ///
    /// 本模块用于**离线核查**：对每条实存船（designId != empty），
    /// 用"船宽吃水之前的原始吨位"与当前设计的吨位对比：
    ///
    ///     原始吨位 ≈ ship.tonnage / beamDraughtBonus     （beamDraughtBonus <= 0 时按 1 处理）
    ///
    /// 判定：|原始 - 设计| / 设计 > 阈值（默认 2%）⇒ 记一条 WARN。
    ///
    /// 只读，不改任何数据；开关 `tonnage_check`（默认 1）。
    /// 修复（把 ship.tonnage 写回设计值）留待确认后再做，避免二次踩坑。
    /// </summary>
    internal static class TonnageCheck
    {
        private static bool _done;
        private static int _n;

        internal static void Install(HarmonyLib.Harmony harmony)
        {
            if (!HotfixParams.TonnageCheck)
            {
                HotfixLog.Info("[SMS.1.TON] disabled");
                return;
            }

            try
            {
                var m = AccessTools.Method(typeof(GameManager), "ChangeState");
                if (m == null) { HotfixLog.Warn("[SMS.1.TON] ChangeState not found"); return; }

                harmony.Patch(m, postfix: new HarmonyMethod(
                    AccessTools.Method(typeof(TonnageCheck), nameof(Postfix_ChangeState))));
                HotfixLog.Info("[SMS.1.TON] armed (read-only tonnage check)");
            }
            catch (Exception e)
            {
                HotfixLog.Warn("[SMS.1.TON] install: " + e.Message);
            }
        }

        internal static void Postfix_ChangeState(GameManager.GameState newState)
        {
            if (_done) return;
            if (newState.ToString() != "World") return;
            _done = true;

            try { Run(); }
            catch (Exception e) { HotfixLog.Warn("[SMS.1.TON] run: " + e.Message); }
        }

        private static void Run()
        {
            var cc = CampaignController.Instance;
            if (cc == null) return;
            var data = cc.CampaignData;
            if (data == null) return;

            var vessels = data.Vessels;
            if (vessels == null) return;

            int ships = 0, bad = 0;

            for (int i = 0; i < vessels.Count; i++)
            {
                VesselEntity ve = null;
                try { ve = vessels[i]; } catch { }
                if (ve == null) continue;

                Ship ship = ve as Ship;
                if (ship == null) continue;

                // 只要实存船（designId 非空）
                Ship.Store store = null;
                try { store = ship.ToStore(); } catch { }
                if (store == null) continue;
                try { if (store.designId == Il2CppSystem.Guid.Empty) continue; } catch { }

                ships++;

                float designTonnage = -1f;
                try
                {
                    if (ship.design != null) designTonnage = ship.design.tonnage;
                }
                catch { }

                if (designTonnage <= 0f) continue;

                float current = ship.tonnage;
                float bonus = 1f;
                try { bonus = ship.beamDraughtBonus; } catch { }
                if (bonus <= 0.0001f) bonus = 1f;

                float directDiff = Math.Abs(current - designTonnage);
                float directRatio = directDiff / designTonnage;

                // ① 严格判据（默认）：tonnage 必须与 design 完全一致（浮点容差 0.1 吨）
                //    这是"五项指纹"里对吨位的要求，故意不放宽，以便区分"当时就错"与"后来改坏"。
                if (HotfixParams.TonnageCheckStrict)
                {
                    if (directDiff <= 0.1f) continue;
                }
                else
                {
                    if (directRatio <= 0.02f) continue;
                }

                // ② 直接不一致时，才用"原始吨位（船宽吃水之前）"筛查
                float original = current / bonus;
                float origDiff = Math.Abs(original - designTonnage);
                float origRatio = origDiff / designTonnage;

                if (!HotfixParams.TonnageCheckStrict && origRatio <= 0.02f)
                {
                    // 宽松模式：可由船宽/吃水修正解释 ⇒ 仅提示，不算坏档
                    if (_n++ < 30)
                        HotfixLog.Info("[SMS.1.TON] explained '" + SafeName(ship) + "': tonnage="
                                       + current.ToString("0.##") + " ≈ design×bonus("
                                       + designTonnage.ToString("0.##") + "×" + bonus.ToString("0.####") + ")");
                }
                else
                {
                    bad++;
                    HotfixLog.Warn("[SMS.1.TON] MISMATCH '" + SafeName(ship) + "': tonnage="
                                   + current.ToString("0.##")
                                   + " design=" + designTonnage.ToString("0.##")
                                   + " (直接差 " + (directRatio * 100f).ToString("0.##") + "%"
                                   + ", 原始≈" + original.ToString("0.##") + " 差 "
                                   + (origRatio * 100f).ToString("0.##") + "%, bonus="
                                   + bonus.ToString("0.####") + ")");
                }
            }

            HotfixLog.Info("[SMS.1.TON] summary: ships=" + ships + " mismatched=" + bad
                           + " (threshold 2%)");
        }

        private static string SafeName(Ship s)
        {
            try { return s.vesselName; } catch { }
            try { return s.name; } catch { }
            return "?";
        }
    }
}
