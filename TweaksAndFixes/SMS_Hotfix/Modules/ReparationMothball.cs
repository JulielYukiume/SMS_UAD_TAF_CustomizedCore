using System;
using HarmonyLib;
using Il2Cpp;

namespace TAFHotfixBatch1.Modules
{
    /// <summary>
    /// Module A2 —— 赔偿舰到手时的状态规范化（用户要求，2026-10-17 第 25 轮）。
    ///
    /// 问题（用户实测，1940 中华档）：
    ///   · 赔偿舰移交过来是 `Normal(1)` + **在港** + **船员 0** —— 原版一直如此；
    ///   · 该状态下这些船**无法被选中做改进设计**（低/无船员的可用性限制），
    ///     必须先手动"添加船员"才能选 ⇒ 体验很差，且容易被误判为"改造链没修好"。
    ///
    /// 处理（用户认可的语义）：
    ///   把赔偿舰设为 **非手动标记的封存**：`status = Mothballed(8)` + `manualMothballed = false`。
    ///   这样系统会按正常流程自动补员 → 海试 → `Commissioning(11)` → 服役，
    ///   既消掉"在役却没船员"的矛盾状态，也不会像手动封存那样挡住自动解封。
    ///
    /// 挂钩点（用户要求"闭合在赔偿流程内部"）：
    ///   `CampaignController::TransferShipToNewOwner(Player newOwner, Guid shipId, List&lt;Ship&gt; takenShips,
    ///                                              PortElement port, bool isReparation = false)`
    ///   —— 该签名自带 `isReparation` 标记，正是"赔偿移交"逐船调用的入口。
    ///   已在日志中确认：`[ProvinceNewOwner] … Reparation` 与 `[A] chain rebuilt` 同一秒发生，
    ///   说明赔偿流程确实走这条链。
    ///
    /// 红线：只调整**这条船自己的状态字段**，不删除、不改设计表、不推进回合。
    /// 开关：`reparation_mothball`（默认 1；0 = 回到原版行为）。
    /// </summary>
    internal static class ReparationMothball
    {
        internal static void Install(HarmonyLib.Harmony harmony)
        {
            if (!HotfixParams.ReparationMothball)
            {
                HotfixLog.Info("[SMS.1.A2] ReparationMothball: disabled by config");
                return;
            }

            try
            {
                var m = AccessTools.Method(typeof(CampaignController), "TransferShipToNewOwner");
                if (m == null)
                {
                    HotfixLog.Warn("[SMS.1.A2] CampaignController::TransferShipToNewOwner not found -- A2 inactive");
                    return;
                }

                harmony.Patch(m, postfix: new HarmonyMethod(
                    AccessTools.Method(typeof(ReparationMothball), nameof(Postfix_TransferShipToNewOwner))));

                HotfixLog.Info("[SMS.1.A2] ReparationMothball: patched CampaignController::TransferShipToNewOwner"
                               + "  (reparation ships -> non-manual mothballed)  mode=" + HotfixParams.Mode);
            }
            catch (Exception e)
            {
                HotfixLog.Error("[SMS.1.A2] install failed: " + e);
            }
        }

        /// <summary>
        /// 参数名必须与 vanilla 一致（Harmony 按名字绑定）：
        /// `newOwner` / `shipId` / `takenShips` / `port` / `isReparation`。
        /// 这里只需要 shipId 与 isReparation。
        /// </summary>
        internal static void Postfix_TransferShipToNewOwner(Il2CppSystem.Guid shipId, bool isReparation)
        {
            if (!isReparation) return;
            if (!HotfixParams.IsApply || !HotfixParams.ReparationMothball) return;

            try
            {
                Ship sh = FindShip(shipId);
                if (sh == null)
                {
                    HotfixLog.Warn("[SMS.1.A2] reparation transfer: ship " + SafeId(shipId) + " not found in Vessels");
                    return;
                }

                int st = -1;
                try { st = (int)sh.status; } catch { }

                // 已经在封存/舾装/改装流程里的船不重复处理
                if (st == (int)VesselEntity.Status.Mothballed
                    || st == (int)VesselEntity.Status.Commissioning
                    || st == (int)VesselEntity.Status.Refit)
                {
                    HotfixLog.Info("[SMS.1.A2] reparation ship '" + SafeName(sh) + "' already in status " + st + " -- skipped");
                    return;
                }

                // ★ 只在"没有船员"时才处理（用户修正）：否则会把正在使用的船错误封存。
                float crewPct = -1f;
                try
                {
                    var snap = sh.ToStore();
                    if (snap != null && snap.CrewAmountPercents.HasValue)
                        crewPct = snap.CrewAmountPercents.Value;
                }
                catch { }

                if (crewPct < 0f)
                {
                    HotfixLog.Info("[SMS.1.A2] reparation ship '" + SafeName(sh) + "': crew% unknown -> skipped (conservative)");
                    return;
                }
                if (crewPct > 0.01f)
                {
                    HotfixLog.Info("[SMS.1.A2] reparation ship '" + SafeName(sh) + "' has crew " + crewPct.ToString("0.##")
                                   + " -> left as-is (only zero-crew ships are mothballed)");
                    return;
                }

                try { sh.SetStatus(VesselEntity.Status.Mothballed); }
                catch { try { sh.status = VesselEntity.Status.Mothballed; } catch { } }

                try { sh.manualMothballed = false; } catch { }

                HotfixLog.Info("[SMS.1.A2] reparation ship '" + SafeName(sh) + "' (was status " + st
                               + ") -> Mothballed, manualMothballed=false"
                               + "  (will auto-crew + sea trials instead of 'in service with 0 crew')");
            }
            catch (Exception e)
            {
                HotfixLog.Warn("[SMS.1.A2] Postfix_TransferShipToNewOwner: " + e.Message);
            }
        }

        private static Ship FindShip(Il2CppSystem.Guid id)
        {
            try
            {
                var cc = CampaignController.Instance;
                if (cc == null) return null;
                var data = cc.CampaignData;
                if (data == null) return null;
                var list = data.Vessels;
                if (list == null) return null;

                for (int i = 0; i < list.Count; i++)
                {
                    var v = list[i];
                    if (v == null) continue;
                    Ship s = v as Ship;
                    if (s == null) continue;
                    try { if (s.id == id) return s; } catch { }
                }
            }
            catch { }
            return null;
        }

        private static string SafeId(Il2CppSystem.Guid g) { try { return g.ToString(); } catch { return "<err>"; } }
        private static string SafeName(Ship s) { try { return s.name; } catch { return "<unnamed>"; } }
    }
}
