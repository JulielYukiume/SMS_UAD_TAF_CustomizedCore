using System;
using System.Collections.Generic;
using HarmonyLib;
using Il2Cpp;

namespace TAFHotfixBatch1.Modules
{
    /// <summary>
    /// Module B —— SHIPLOSS-1：过回合丢船。用户原则：**维修优先级恒高于补充船员**。
    ///
    /// 已定论的两个机制（taf/docs/21 §B.4.2 / §D.7）：
    ///   ① **状态劫持**：船员不足 ⇒ Player::TakeCrewFromPool 失败 ⇒ 原版把状态改成
    ///      Mothballed(8) 并把 repairingProgress 强写 100.0f，破坏维修记账。
    ///      （VesselEntity::SetStatus 本体只做赋值，**没有任何防冲突**。）
    ///   ② **静默擦除**：Ship::FromStore 返回 false 时，GameManager::LoadShips 会调用
    ///      VesselEntity::TryToEraseVessel 把船擦除 ⇒ 之后不再被写进存档（**无日志**）。
    ///
    /// 做法：
    ///   · Prefix VesselEntity::SetStatus —— 否决 "Repairing(4) → Mothballed(8)"（进度 &lt; 100 时），
    ///     并**快照该船对象与真实进度**；玩家手动封存（Ship/Submarine::Mothball）走逃生门放行。
    ///   · Postfix CampaignController::AdvanceShips / AdvanceMinorShips —— 直接对快照里的船
    ///     **回写被强改的进度**（不依赖任何注册表查询），使下一回合维修 tick（阈值 15/33/66）
    ///     继续把船维持在 Repairing。
    ///   · Prefix VesselEntity::TryToEraseVessel —— **只记录不拦截**，用于定位静默擦除（诊断）。
    ///
    /// ⚠ 红线：状态与**真实进度必须一起恢复**；只留 Repairing 而进度为 100 反而制造不一致组合。
    /// </summary>
    internal static class ShipLossGuard
    {
        private const int STATUS_REPAIRING = 4;
        private const int STATUS_MOTHBALLED = 8;
        private const float PROGRESS_FULL = 100f;

        private sealed class Snap
        {
            internal VesselEntity Ship;
            internal float Progress;
        }

        private static readonly Dictionary<string, Snap> _snapshot = new Dictionary<string, Snap>();
        private static bool _inVeto;

        internal static void Install(HarmonyLib.Harmony harmony)
        {
            if (!HotfixParams.ShipLossGuard)
            {
                HotfixLog.Info("[SMS.1.B] ShipLossGuard: disabled by config");
                return;
            }

            int ok = 0;
            ok += TryPatch(harmony, typeof(VesselEntity), "SetStatus", nameof(Prefix_SetStatus), true);
            ok += TryPatch(harmony, typeof(CampaignController), "AdvanceShips", nameof(Postfix_AdvanceShips), false);
            ok += TryPatch(harmony, typeof(CampaignController), "AdvanceMinorShips", nameof(Postfix_AdvanceMinorShips), false);
            ok += TryPatch(harmony, typeof(VesselEntity), "TryToEraseVessel", nameof(Prefix_TryToEraseVessel), true);

            HotfixLog.Info("[SMS.1.B] ShipLossGuard: " + ok + " patch(es) installed  (repair>crew="
                           + HotfixParams.PriorityRepairOverCrew + ", mode=" + HotfixParams.Mode + ")");
        }

        private static int TryPatch(HarmonyLib.Harmony harmony, Type type, string method,
                                    string patchSpec, bool isPrefix)
        {
            try
            {
                var target = AccessTools.Method(type, method);
                if (target == null)
                {
                    HotfixLog.Warn("[SMS.1.B] target not found: " + type.Name + "::" + method);
                    return 0;
                }

                string[] parts = patchSpec.Split('|');
                var prefix = isPrefix ? new HarmonyMethod(AccessTools.Method(typeof(ShipLossGuard), parts[0])) : null;
                var postfix = isPrefix ? null : new HarmonyMethod(AccessTools.Method(typeof(ShipLossGuard), parts[0]));
                if (parts.Length > 1 && isPrefix)
                    postfix = new HarmonyMethod(AccessTools.Method(typeof(ShipLossGuard), parts[1]));

                harmony.Patch(target, prefix: prefix, postfix: postfix);
                return 1;
            }
            catch (Exception e)
            {
                HotfixLog.Error("[SMS.1.B] patch " + type.Name + "::" + method + " failed: " + e.Message);
                return 0;
            }
        }

        // -------- 挂钩 1：否决"维修中 → 封存"并快照真实进度 --------
        internal static bool Prefix_SetStatus(VesselEntity __instance, VesselEntity.Status statusNew)
        {
            try
            {
                if (!HotfixParams.PriorityRepairOverCrew || __instance == null) return true;
                if ((int)statusNew != STATUS_MOTHBALLED) return true;
                if ((int)__instance.status != STATUS_REPAIRING) return true;   // 只拦"维修中"被劫持
                try { if (__instance.manualMothballed) return true; } catch { }   // 玩家手动封存：放行

                float prog = __instance.repairingProgress;
                if (prog >= PROGRESS_FULL) return true;                        // 已修好：不拦

                string key = __instance.id.ToString();
                _snapshot[key] = new Snap { Ship = __instance, Progress = prog };

                HotfixLog.Info("[SMS.1.B] veto Repairing->Mothballed on '" + Name(__instance)
                               + "' (progress " + prog.ToString("0.##") + " saved)");
                return false;   // ★ 不执行原方法：状态保持 Repairing
            }
            catch (Exception e)
            {
                HotfixLog.Error("[SMS.1.B] Prefix_SetStatus: " + e.Message);
                return true;
            }
        }

        // -------- 挂钩 2：回合结束后回写被强改的维修进度 --------
        internal static void Postfix_AdvanceShips() { RestoreSnapshots("AdvanceShips"); }
        internal static void Postfix_AdvanceMinorShips() { RestoreSnapshots("AdvanceMinorShips"); }

        private static void RestoreSnapshots(string who)
        {
            if (_snapshot.Count == 0) return;

            List<string> done = new List<string>();
            foreach (KeyValuePair<string, Snap> kv in _snapshot)
            {
                try
                {
                    Snap s = kv.Value;
                    if (s == null || s.Ship == null) { done.Add(kv.Key); continue; }

                    float now = s.Ship.repairingProgress;
                    if (now >= PROGRESS_FULL && s.Progress < PROGRESS_FULL)
                    {
                        if (HotfixParams.IsApply)
                        {
                            s.Ship.repairingProgress = s.Progress;
                            HotfixLog.Info("[SMS.1.B] restored repairingProgress on '" + Name(s.Ship) + "': "
                                           + s.Progress.ToString("0.##") + " (was " + now.ToString("0.##")
                                           + ") [" + who + "]");
                        }
                        else
                        {
                            HotfixLog.Info("[SMS.1.B][log] would restore progress on '" + Name(s.Ship) + "': "
                                           + s.Progress.ToString("0.##") + " (was " + now.ToString("0.##") + ")");
                        }
                    }
                    done.Add(kv.Key);
                }
                catch (Exception e)
                {
                    HotfixLog.Error("[SMS.1.B] restore: " + e.Message);
                    done.Add(kv.Key);
                }
            }

            foreach (string k in done) _snapshot.Remove(k);
        }

        // -------- 逃生门：玩家主动封存 --------

        // -------- 诊断：记录每一次擦除（含读档时的静默擦除）--------
        private static int _nullEraseCount;

        internal static void Prefix_TryToEraseVessel(VesselEntity __instance)
        {
            try
            {
                if (__instance == null)
                {
                    // 原版会大量以 null 调用（实测开局 916 次）—— 这属正常流程，
                    // 只做计数、不逐条记录，避免把真正有价值的擦除事件淹没。
                    _nullEraseCount++;
                    if (HotfixParams.Verbose && _nullEraseCount % 100 == 1)
                        HotfixLog.Info("[SMS.1.B][erase] null calls so far: " + _nullEraseCount);
                    return;
                }

                // NOTE: designId lives on Ship.Store, not on the runtime VesselEntity/Ship,
                // so it is not available here; id/name/status/progress are enough to locate the loss.
                HotfixLog.Warn("[SMS.1.B][erase] TryToEraseVessel '" + Name(__instance)
                               + "' id=" + __instance.id
                               + " status=" + __instance.status
                               + " progress=" + __instance.repairingProgress.ToString("0.##"));
            }
            catch (Exception e)
            {
                HotfixLog.Error("[SMS.1.B] Prefix_TryToEraseVessel: " + e.Message);
            }
        }

        private static string Name(VesselEntity v)
        {
            try { return v.vesselName; } catch { }
            try { return v.name; } catch { }
            return "<unnamed>";
        }
    }
}
