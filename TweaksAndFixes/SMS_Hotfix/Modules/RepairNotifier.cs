using System;
using HarmonyLib;
using Il2Cpp;

namespace TAFHotfixBatch1.Modules
{
    /// <summary>
    /// 读档期修复的**玩家提示**：若本次加载中发生了数据修复，
    /// 在**加载完成后**（下一次 `CampaignController::AdvanceShips`）弹出一次提示，
    /// 告诉玩家"另存为新档 → 重新读取该新档"，修复才会在游戏中完全生效。
    ///
    /// 理由（用户 2026-10-17 定）：
    ///   · 我们的修复作用在**存档数据层**（store）；内存中"设计 vs 船"的分类可能已按旧数据完成；
    ///   · 因此**硬目标 = 落盘数据正确**，再靠一次"另存 + 重读"让游戏用正确数据重建内存。
    ///
    /// 实现细节：`Ui.ShowMessage(string text, Action ok)`（Ui 实测签名），
    /// 实例取自静态属性 `Ui.ui`；全程 try/catch，失败只记日志、绝不影响游戏。
    /// </summary>
    internal static class RepairNotifier
    {
        private static bool _pending;
        private static bool _shown;
        private static string _text = "";

        internal static int ChainFixes, DupIds, SharedFlags;

        internal static void ResetCounters() { ChainFixes = 0; DupIds = 0; SharedFlags = 0; }

        /// <summary>B2 + S 完成之后调用：此时预览模型已构建完毕，提示"另存并重读"最合适。</summary>
        internal static void NotifyOnLoadComplete()
        {
            int made = SharedBackfillProbe.MadeCount;
            int total = ChainFixes + DupIds + SharedFlags + made;
            if (total <= 0) return;
            Notify("TAF 热修：读取本存档时已修复 " + total + " 处数据（改造链 " + ChainFixes
                   + " / 重复 id " + DupIds + " / 共享设计标记 " + SharedFlags
                   + " / 反建设计 " + made + "）。请【另存为一个新档】，然后重新读取该新档 —— 修复才会在游戏中完全生效。");
        }
        internal static void Notify(string text)
        {
            _pending = true;
            _text = text;
            HotfixLog.Warn("[SMS.1.NOTIFY] queued: " + text);
        }

        internal static void Install(HarmonyLib.Harmony harmony)
        {
            // 三个触发点，任一先到即显示（TryShow 幂等）：
            //   · OnEnterState  —— 进入战役状态的瞬间（读档完成后最先到）
            //   · AdvanceShips  —— 玩家过回合时（兜底）
            int ok = 0;
            ok += TryPatch(harmony, typeof(GameManager), "OnEnterState");
            ok += TryPatch(harmony, typeof(CampaignController), "AdvanceShips");
            HotfixLog.Info("[SMS.1.NOTIFY] installed on " + ok + " trigger(s) (OnEnterState / AdvanceShips)");
        }

        private static int TryPatch(HarmonyLib.Harmony harmony, Type type, string method)
        {
            try
            {
                var m = AccessTools.Method(type, method);
                if (m == null) { HotfixLog.Warn("[SMS.1.NOTIFY] " + type.Name + "::" + method + " not found"); return 0; }
                harmony.Patch(m, postfix: new HarmonyMethod(
                    AccessTools.Method(typeof(RepairNotifier), nameof(Postfix_Trigger))));
                return 1;
            }
            catch (Exception e)
            {
                HotfixLog.Warn("[SMS.1.NOTIFY] patch " + type.Name + "::" + method + " failed: " + e.Message);
                return 0;
            }
        }

        /// <summary>幂等：只在有待显示消息时动作一次。</summary>
        internal static void Postfix_Trigger()
        {
            if (!_pending || _shown) return;
            _shown = true;
            _pending = false;

            try
            {
                Ui ui = null;
                try { ui = G.ui; } catch { }

                if (ui != null)
                {
                    ui.ShowMessage(_text, null);
                    HotfixLog.Info("[SMS.1.NOTIFY] shown to player");
                }
                else
                {
                    HotfixLog.Warn("[SMS.1.NOTIFY] Ui.ui is null -- player notice skipped (see log above)");
                }
            }
            catch (Exception e)
            {
                HotfixLog.Warn("[SMS.1.NOTIFY] ShowMessage failed: " + e.Message);
            }
        }
    }
}
