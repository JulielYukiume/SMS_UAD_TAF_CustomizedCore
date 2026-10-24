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

                float original = current / bonus;
                float diff = Math.Abs(original - designTonnage);
                float ratio = diff / designTonnage;

                if (ratio > 0.02f)
                {
                    bad++;
                    HotfixLog.Warn("[SMS.1.TON] '" + SafeName(ship) + "': tonnage=" + current.ToString("0.##")
                                   + " 原始≈" + original.ToString("0.##")
                                   + " design=" + designTonnage.ToString("0.##")
                                   + " 差=" + (ratio * 100f).ToString("0.##") + "%"
                                   + " (bonus=" + bonus.ToString("0.####") + ")");
                }
                else if (_n++ < 20)
                {
                    HotfixLog.Info("[SMS.1.TON] ok '" + SafeName(ship) + "': " + current.ToString("0.##")
                                   + " ≈ design " + designTonnage.ToString("0.##"));
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
