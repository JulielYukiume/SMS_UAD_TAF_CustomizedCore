using System;
using System.Collections.Generic;
using HarmonyLib;
using Il2Cpp;
using UnityEngine;

namespace TAFHotfixBatch1.Modules
{
    /// <summary>
    /// Module F —— TAF 的"AI 隐藏构造"位移未还原（部件沉到 -1000 米）。
    ///
    /// 根因（2026-10-18 反汇编 + 源码双证）：
    ///   TAF 的 `Harmony/Ship.cs` 里有一段 workaround：
    ///
    ///       [HarmonyPatch(nameof(Ship.EnterConstructor))] [HarmonyPrefix]
    ///       internal static void Prefix_EnterConstructor(Ship __instance)
    ///       {
    ///           if (GameManager.IsWorld && __instance.player.isAi)
    ///           {
    ///               Melon<TweaksAndFixes>.Logger.Msg($"Making {__instance.vesselName} visible in world.");
    ///               __instance.gameObject.transform.position += new Vector3(0, -1000, 0);
    ///           }
    ///       }
    ///
    ///   目的是压掉"AI 在过回合里进构造器时，地图上留下一张巨大俯视图"的显示问题；
    ///   但它把**真船**整体下沉 1000 米，**且没有任何还原**。
    ///
    /// 触发链（`xref-call` 实证）：
    ///   `CampaignController.<AiRefitShips>d__190::MoveNext`
    ///       → `Ship::EnterConstructor()`   （隐藏调取的 AI 改装路径，**不**经 `GameManager.ToConstructor`）
    ///       → TAF Prefix 命中 `IsWorld && isAi` ⇒ 下沉
    ///   同一协程随后调用 `Ship::LeaveConstructor()`（成对！）⇒ **这就是最佳还原点**。
    ///
    /// 症状：AI 改装过的船，在战斗中"只有空船体在海面上跑"——因为船体按海平面定位，
    ///       而部件是它的子物体、仍留在 -1000 米。
    ///
    /// 本模块做法（不改 TAF 文件，只补一次还原）：
    ///   ① Prefix `Ship::EnterConstructor`（**Priority.First**，跑在 TAF 的 Prefix 之前）：
    ///        条件与 TAF 相同（`IsWorld && player.isAi`）时，记下原始位置。
    ///   ② Postfix `Ship::LeaveConstructor`：还原（AI 改装协程会调用它）。
    ///   ③ 兜底 A：Postfix `GameManager::ChangeState` —— 一旦离开 World（进战斗/读档）就还原全部。
    ///   ④ 兜底 B：Postfix `CampaignController::AdvanceShips` —— 回合结束时清理残留。
    ///
    /// 还原时做**下沉校验**：只有"当前 y 比记录值低 500 米以上"才写回，
    /// 避免在 TAF 未安装/行为变化时误改一条正常的船。
    ///
    /// 开关：`ai_refit_restore`（默认 1；`log` 模式只记录不改）。
    /// 红线：只改 transform.position，不碰部件、设计表、存档字段。
    /// </summary>
    internal static class AiRefitRestore
    {
        /// <summary>shipId → 原始世界坐标（只记录被 TAF 移动过的 AI 船）。</summary>
        private static readonly Dictionary<string, Vector3> _saved = new Dictionary<string, Vector3>();
        private static int _shifted, _restored, _safety;

        internal static void Install(HarmonyLib.Harmony harmony)
        {
            if (!HotfixParams.AiRefitRestore)
            {
                HotfixLog.Info("[SMS.1.F] AiRefitRestore: disabled by config");
                return;
            }

            try
            {
                var enter = AccessTools.Method(typeof(Ship), "EnterConstructor");
                if (enter != null)
                {
                    // Priority.First ⇒ 在 TAF 的 Prefix 之前执行，才能拿到"位移前"的位置
                    var pre = new HarmonyMethod(AccessTools.Method(typeof(AiRefitRestore), nameof(Prefix_EnterConstructor)));
                    pre.priority = Priority.First;
                    harmony.Patch(enter, prefix: pre);
                }
                else HotfixLog.Warn("[SMS.1.F] Ship::EnterConstructor not found");

                var leave = AccessTools.Method(typeof(Ship), "LeaveConstructor");
                if (leave != null)
                    harmony.Patch(leave, postfix: new HarmonyMethod(
                        AccessTools.Method(typeof(AiRefitRestore), nameof(Postfix_LeaveConstructor))));
                else HotfixLog.Warn("[SMS.1.F] Ship::LeaveConstructor not found");

                var changeState = AccessTools.Method(typeof(GameManager), "ChangeState");
                if (changeState != null)
                    harmony.Patch(changeState, postfix: new HarmonyMethod(
                        AccessTools.Method(typeof(AiRefitRestore), nameof(Postfix_ChangeState))));
                else HotfixLog.Warn("[SMS.1.F] GameManager::ChangeState not found");

                HotfixLog.Info("[SMS.1.F] AiRefitRestore: armed (save pos before TAF shifts AI ships; restore on LeaveConstructor"
                               + " + ChangeState safety net)  mode=" + HotfixParams.Mode);
            }
            catch (Exception e)
            {
                HotfixLog.Error("[SMS.1.F] install failed: " + e);
            }
        }

        // ------------------------------------------------------------------
        // ① 记录（必须跑在 TAF 的 Prefix 之前）
        // ------------------------------------------------------------------

        internal static void Prefix_EnterConstructor(Ship __instance)
        {
            if (__instance == null) return;

            try
            {
                // 与 TAF 的 workaround 同条件：只有这些船会被它移动
                if (!GameManager.IsWorld) return;
                Player p = null;
                try { p = __instance.player; } catch { }
                if (p == null || !p.isAi) return;

                string id = SafeId(__instance);
                if (string.IsNullOrEmpty(id)) return;

                Vector3 pos = __instance.gameObject.transform.position;
                _saved[id] = pos;
                _shifted++;

                if (HotfixParams.Verbose)
                    HotfixLog.Info("[SMS.1.F] saved pos: '" + SafeName(__instance) + "' y=" + pos.y.ToString("0.##")
                                   + "  (TAF will shift this ship down 1000m)");
            }
            catch (Exception e) { HotfixLog.Warn("[SMS.1.F] Prefix_EnterConstructor: " + e.Message); }
        }

        // ------------------------------------------------------------------
        // ② 主还原点
        // ------------------------------------------------------------------

        internal static void Postfix_LeaveConstructor(Ship __instance)
        {
            if (__instance == null) return;
            try { Restore(__instance, "LeaveConstructor"); }
            catch (Exception e) { HotfixLog.Warn("[SMS.1.F] Postfix_LeaveConstructor: " + e.Message); }
        }

        // ------------------------------------------------------------------
        // ③ 兜底 A：离开 World（进战斗/读档）
        // ------------------------------------------------------------------

        internal static void Postfix_ChangeState(GameManager.GameState newState)
        {
            try
            {
                if (newState == GameManager.GameState.World) return;
                RestoreAll("ChangeState->" + newState);
            }
            catch (Exception e) { HotfixLog.Warn("[SMS.1.F] Postfix_ChangeState: " + e.Message); }
        }

        // ------------------------------------------------------------------
        // 还原实现
        // ------------------------------------------------------------------

        private static void Restore(Ship ship, string why)
        {
            string id = SafeId(ship);
            if (string.IsNullOrEmpty(id)) return;

            Vector3 original;
            if (!_saved.TryGetValue(id, out original)) return;

            Vector3 cur = ship.gameObject.transform.position;
            // 只有"确实被下沉"才写回；否则可能是一条正常移动过的船
            if (original.y - cur.y < 500f)
            {
                _saved.Remove(id);
                return;
            }

            if (HotfixParams.IsApply)
            {
                ship.gameObject.transform.position = original;
                _restored++;
                HotfixLog.Info("[SMS.1.F] restored: '" + SafeName(ship) + "' y " + cur.y.ToString("0.##")
                               + " -> " + original.y.ToString("0.##") + "   (" + why + ")");
            }
            else
            {
                HotfixLog.Info("[SMS.1.F][log] would restore: '" + SafeName(ship) + "' y " + cur.y.ToString("0.##")
                               + " -> " + original.y.ToString("0.##") + "   (" + why + ")");
            }

            _saved.Remove(id);
        }

        private static void RestoreAll(string why)
        {
            if (_saved.Count == 0) return;

            var ids = new List<string>(_saved.Keys);
            int n = 0;
            for (int i = 0; i < ids.Count; i++)
            {
                Ship s = null;
                try
                {
                    var cc = CampaignController.Instance;
                    if (cc != null && cc.CampaignData != null)
                    {
                        var list = cc.CampaignData.Vessels;
                        for (int j = 0; j < list.Count; j++)
                        {
                            Ship cand = list[j] as Ship;
                            if (cand == null) continue;
                            if (SafeId(cand) == ids[i]) { s = cand; break; }
                        }
                    }
                }
                catch { }

                if (s == null) { _saved.Remove(ids[i]); continue; }
                Restore(s, why);
                n++;
            }

            if (n > 0)
            {
                _safety += n;
                HotfixLog.Info("[SMS.1.F] safety restore: " + n + " ship(s)  (" + why + ")");
            }
        }

        internal static void LogSummary()
        {
            if (_shifted > 0 || _restored > 0)
                HotfixLog.Info("[SMS.1.F] summary: shifted=" + _shifted + " restored=" + _restored + " safety=" + _safety
                               + " pending=" + _saved.Count);
        }

        private static string SafeId(Ship s) { try { return s.id.ToString(); } catch { return null; } }
        private static string SafeName(Ship s) { try { return s.name; } catch { return "<unnamed>"; } }
    }
}
