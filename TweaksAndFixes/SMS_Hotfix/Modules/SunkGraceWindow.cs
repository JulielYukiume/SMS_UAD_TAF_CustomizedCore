using System;
using System.Collections.Generic;
using HarmonyLib;
using Il2Cpp;

namespace TAFHotfixBatch1.Modules
{
    /// <summary>
    /// Module D —— **沉船 / 已擦除条目的删除缓冲期**（用户提案：改"触发条件"而非"改状态规避"）。
    ///
    /// 删除链（已实证）：
    ///   ① <c>Ui::DeleteSunkedShips</c>（战斗结算）→ <c>RemoveFromVessels</c> + <c>TryToEraseVessel</c>
    ///   ② **TAF 的 `Harmony/CampaignController.cs::Postfix_GetStore`** 在**存档写入**时把
    ///      `status ∈ {Erased(7), Sunk(5), Scrapped(6)}` 且无设计引用的条目 `RemoveAt`
    ///      ⇒ 实测：Duke 档 231 条 Erased 中 **164 条**就是这样被删掉的（第一大丢失来源）。
    ///
    /// 本模块：
    ///   · Prefix `VesselEntity::TryToEraseVessel` / `CampaignController::RemoveFromVessels` ——
    ///     缓冲期内的 **Sunk / Erased / Scrapped** 一律否决，并登记到 `_protected`；
    ///   · **Prefix `CampaignController::GetStore`** —— 在存档构造**之前**按 id 抓住
    ///     `Data.GetShips` 里那些被我们登记的 store 引用（TAF 之后只是"从列表移除"，对象仍在）；
    ///   · **Postfix `CampaignController::GetStore`（Priority.Low ⇒ 排在 TAF 之后）** ——
    ///     把被 TAF 剔掉的条目**原样塞回** `__result.Ships`。
    ///     因为没有 `Ship::ToStore()`，**只能靠持有原引用**，这也正是 Prefix 抓引用的原因。
    ///   · Postfix `CampaignController::AdvanceShips` —— 每回合清查：超期即解除保护。
    ///
    /// 参数：`sunk_grace_window`、`sunk_grace_turns`（默认 6）、`mode=log|apply`。
    /// ⚠ 只做"保留"，不改任何状态/进度；`mode=log` 只打印。
    /// </summary>
    internal static class SunkGraceWindow
    {
        private const int STATUS_SUNK = 5;
        private const int STATUS_SCRAPPED = 6;
        private const int STATUS_ERASED = 7;

        private sealed class Protected
        {
            internal VesselEntity Ship;
            internal int SunkTurn;
        }

        private static readonly Dictionary<string, Protected> _protected = new Dictionary<string, Protected>();
        private static readonly List<Ship.Store> _keepAtSave = new List<Ship.Store>();

        internal static void Install(HarmonyLib.Harmony harmony)
        {
            if (!HotfixParams.SunkGraceWindow)
            {
                HotfixLog.Info("[SMS.1.D] SunkGraceWindow: disabled by config");
                return;
            }

            int ok = 0;
            ok += TryPatch(harmony, typeof(VesselEntity), "TryToEraseVessel", nameof(Prefix_TryToEraseVessel), true);
            ok += TryPatch(harmony, typeof(CampaignController), "RemoveFromVessels", nameof(Prefix_RemoveFromVessels), true);
            ok += TryPatch(harmony, typeof(CampaignController), "AdvanceShips", nameof(Postfix_AdvanceShips), false);

            // ★ 存档侧：双 Postfix —— 高优先级先抓引用，低优先级在 TAF 之后塞回
            try
            {
                var getStore = AccessTools.Method(typeof(CampaignController), "GetStore");
                if (getStore != null)
                {
                    harmony.Patch(getStore, postfix: new HarmonyMethod(
                        AccessTools.Method(typeof(SunkGraceWindow), nameof(Postfix_GetStore_Snapshot)))
                    { priority = Priority.High });

                    harmony.Patch(getStore, postfix: new HarmonyMethod(
                        AccessTools.Method(typeof(SunkGraceWindow), nameof(Postfix_GetStore_ReAdd)))
                    { priority = Priority.Low });

                    ok += 2;
                    HotfixLog.Info("[SMS.1.D] patched CampaignController::GetStore"
                                   + " (snapshot@High, re-add@Low => runs after TAF's filter)");
                }
                else HotfixLog.Warn("[SMS.1.D] CampaignController::GetStore not found -- save-side buffer unavailable");
            }
            catch (Exception e) { HotfixLog.Error("[SMS.1.D] GetStore patch failed: " + e.Message); }

            HotfixLog.Info("[SMS.1.D] SunkGraceWindow: " + ok + " patch(es) installed  grace="
                           + HotfixParams.SunkGraceTurns + " turns  mode=" + HotfixParams.Mode);
        }

        private static int TryPatch(HarmonyLib.Harmony harmony, Type type, string method,
                                    string patchName, bool isPrefix)
        {
            try
            {
                var target = AccessTools.Method(type, method);
                if (target == null)
                {
                    HotfixLog.Warn("[SMS.1.D] target not found: " + type.Name + "::" + method);
                    return 0;
                }
                var patch = new HarmonyMethod(AccessTools.Method(typeof(SunkGraceWindow), patchName));
                harmony.Patch(target, prefix: isPrefix ? patch : null, postfix: isPrefix ? null : patch);
                return 1;
            }
            catch (Exception e)
            {
                HotfixLog.Error("[SMS.1.D] patch " + type.Name + "::" + method + " failed: " + e.Message);
                return 0;
            }
        }

        // -------- 工具 --------
        private static int CurrentTurn()
        {
            try { return CampaignController.Instance.CurrentDate.turn; }
            catch { return -1; }
        }

        /// <summary>0..grace ⇒ 仍在缓冲期内；&gt;grace ⇒ 超期；-1 ⇒ 无法判定。</summary>
        private static int Elapsed(VesselEntity v)
        {
            if (HotfixParams.SunkGraceTurns <= 0) return -1;
            int now = CurrentTurn();
            if (now < 0) return -1;
            int t;
            try { t = v.LastBattleParticipating; } catch { return -1; }
            if (t <= 0) return -1;
            return now - t;
        }

        private static bool IsBufferable(int status)
        {
            return status == STATUS_SUNK || status == STATUS_ERASED || status == STATUS_SCRAPPED;
        }

        // -------- 挂钩 1/2：否决两个销毁原语 --------
        internal static bool Prefix_TryToEraseVessel(VesselEntity __instance)
        {
            try
            {
                if (__instance == null) return true;
                if (!IsBufferable((int)__instance.status)) return true;

                int elapsed = Elapsed(__instance);
                if (elapsed >= 0 && elapsed > HotfixParams.SunkGraceTurns) return true;   // 已超期：允许删除

                string key = __instance.id.ToString();
                if (!_protected.ContainsKey(key))
                {
                    _protected[key] = new Protected { Ship = __instance, SunkTurn = SafeLBP(__instance) };
                    HotfixLog.Warn("[SMS.1.D] protect '" + Name(__instance) + "' status=" + __instance.status
                                   + " id=" + key + "  turn=" + SafeLBP(__instance) + " now=" + CurrentTurn());
                }

                if (HotfixParams.IsApply) { HotfixLog.Info("[SMS.1.D] veto TryToEraseVessel (in grace) id=" + key); return false; }
                HotfixLog.Info("[SMS.1.D][log] would veto TryToEraseVessel (in grace) id=" + key);
                return true;
            }
            catch (Exception e) { HotfixLog.Error("[SMS.1.D] Prefix_TryToEraseVessel: " + e.Message); return true; }
        }

        internal static bool Prefix_RemoveFromVessels(VesselEntity vessel)
        {
            try
            {
                if (vessel == null) return true;
                if (!IsBufferable((int)vessel.status)) return true;

                int elapsed = Elapsed(vessel);
                if (elapsed >= 0 && elapsed > HotfixParams.SunkGraceTurns) return true;

                string key = vessel.id.ToString();
                if (!_protected.ContainsKey(key))
                    _protected[key] = new Protected { Ship = vessel, SunkTurn = SafeLBP(vessel) };

                if (HotfixParams.IsApply) { HotfixLog.Info("[SMS.1.D] veto RemoveFromVessels (in grace) id=" + key); return false; }
                HotfixLog.Info("[SMS.1.D][log] would veto RemoveFromVessels (in grace) id=" + key);
                return true;
            }
            catch (Exception e) { HotfixLog.Error("[SMS.1.D] Prefix_RemoveFromVessels: " + e.Message); return true; }
        }

        // -------- 存档侧 A（Priority.High ⇒ 在 TAF 之前）：抓住即将被剔除的 store 引用 --------
        internal static void Postfix_GetStore_Snapshot(CampaignController.Store __result)
        {
            _keepAtSave.Clear();
            if (!HotfixParams.IsApply) return;
            if (__result == null || __result.Ships == null) return;

            try
            {
                int now = CurrentTurn();
                int designsKept = 0, shipsKept = 0;

                for (int i = 0; i < __result.Ships.Count; i++)
                {
                    Ship.Store s = __result.Ships[i];
                    if (s == null) continue;

                    int st = (int)s.status;
                    if (st != STATUS_SUNK && st != STATUS_ERASED && st != STATUS_SCRAPPED) continue;

                    // ★ 用户规则（2026-10-17）：
                    //   · Erased/Sunk/Scrapped **属于设计** ⇒ 一律保留（接档可导出全寿命设计）
                    //   · **实存船**只应按缓冲期保留，超期才允许删除
                    bool isDesign = s.designId == Il2CppSystem.Guid.Empty;
                    if (isDesign)
                    {
                        _keepAtSave.Add(s);
                        designsKept++;
                        continue;
                    }

                    int lbp = 0;
                    try { lbp = s.LastBattleParticipating; } catch { }
                    if (lbp > 0 && now > 0 && now - lbp > HotfixParams.SunkGraceTurns) continue;   // 超期

                    _keepAtSave.Add(s);
                    shipsKept++;
                }

                if (_keepAtSave.Count > 0)
                    HotfixLog.Info("[SMS.1.D] save: captured " + _keepAtSave.Count
                                   + " protected store(s) before TAF's filter  (designs=" + designsKept
                                   + ", ships=" + shipsKept + ")");
            }
            catch (Exception e) { HotfixLog.Error("[SMS.1.D] Postfix_GetStore_Snapshot: " + e.Message); }
        }

        // -------- 存档侧 B（Priority.Low ⇒ 在 TAF 之后）：塞回被剔除的条目 --------
        internal static void Postfix_GetStore_ReAdd(CampaignController.Store __result)
        {
            if (_keepAtSave.Count == 0 || __result == null || __result.Ships == null) return;

            try
            {
                var present = new HashSet<string>();
                for (int i = 0; i < __result.Ships.Count; i++)
                {
                    var s = __result.Ships[i];
                    if (s != null) present.Add(s.id.ToString());
                }

                int added = 0;
                foreach (Ship.Store s in _keepAtSave)
                {
                    if (s == null) continue;
                    string id = s.id.ToString();
                    if (present.Contains(id)) continue;
                    __result.Ships.Add(s);
                    added++;
                    if (added <= 20)
                        HotfixLog.Debug("[SMS.1.D] save: re-added '" + StoreName(s) + "' status=" + s.status + " id=" + id);
                }

                if (added > 0)
                    HotfixLog.Warn("[SMS.1.D] save: re-added " + added + " entr(ies) that TAF's GetStore filter would drop");
            }
            catch (Exception e) { HotfixLog.Error("[SMS.1.D] Postfix_GetStore: " + e.Message); }
        }

        // -------- 挂钩 3：每回合清查 --------
        internal static void Postfix_AdvanceShips()
        {
            if (_protected.Count == 0) return;
            try
            {
                var expired = new List<string>();
                int now = CurrentTurn();
                foreach (KeyValuePair<string, Protected> kv in _protected)
                {
                    Protected p = kv.Value;
                    if (p == null || p.Ship == null) { expired.Add(kv.Key); continue; }
                    int elapsed = now >= 0 ? now - p.SunkTurn : -1;
                    if (elapsed < 0 || elapsed <= HotfixParams.SunkGraceTurns) continue;
                    expired.Add(kv.Key);
                    HotfixLog.Info("[SMS.1.D] grace expired for '" + Name(p.Ship) + "' id=" + kv.Key + "  elapsed=" + elapsed);
                }
                foreach (string k in expired) _protected.Remove(k);
            }
            catch (Exception e) { HotfixLog.Error("[SMS.1.D] sweep: " + e.Message); }
        }

        // -------- helpers --------
        private static int SafeLBP(VesselEntity v)
        {
            try { return v.LastBattleParticipating; } catch { return 0; }
        }

        private static string Name(VesselEntity v)
        {
            try { return v.vesselName; } catch { }
            try { return v.name; } catch { }
            return "<unnamed>";
        }

        private static string StoreName(Ship.Store s)
        {
            try { return s.vesselName; } catch { return "<unnamed>"; }
        }
    }
}
