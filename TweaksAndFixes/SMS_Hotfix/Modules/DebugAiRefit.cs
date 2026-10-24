using System;
using HarmonyLib;
using Il2Cpp;

namespace TAFHotfixBatch1.Modules
{
    /// <summary>
    /// **调试用**：提高 AI 的改装意愿（概率）。
    ///
    /// 背景：验证 Module F（AI 隐藏构造位移）需要 AI 真的去尝试改装；
    /// 但快速模式（fast）下 AI 基本不会改装，靠推回合"碰运气"太慢。
    ///
    /// 可用的杠杆（`dump.cs`）：
    ///   `AiPersonalities.aiRefit` —— `public float`（0x70），每个 AI 性格独立的改装意愿；
    ///   集合入口 `GameData.aiPersonalities`（`Dictionary&lt;string, AiPersonalities&gt;`）。
    ///
    /// 做法：载入战役后（等 `G.GameData` 就绪），把所有 AI 性格的 `aiRefit` 设为配置值，
    /// 并打印"原值 → 新值"，便于确认到底是哪个性格在起作用。
    ///
    /// ⚠ 注意：如果"快速模式不改装"的门槛不在 `aiRefit` 而在其它地方（例如回合处理里按
    ///   `aiRefitShip` 或游戏速度分支），本开关不会生效 —— 届时看日志里是否有
    ///   TAF 的 `Making &lt;ship&gt; visible in world.` 即可判断。
    ///
    /// 开关：`debug_ai_refit`（0 = 关闭；>0 = 把所有 AI 性格的 aiRefit 设为该值，建议 1）
    /// </summary>
    internal static class DebugAiRefit
    {
        private static bool _applied;
        private static bool _scheduled;

        internal static void Install(HarmonyLib.Harmony harmony)
        {
            if (HotfixParams.DebugAiRefit <= 0f)
            {
                HotfixLog.Info("[SMS.1.DBG] DebugAiRefit: disabled (debug_ai_refit=0)");
                return;
            }

            try
            {
                var m = AccessTools.Method(typeof(GameManager), "ChangeState");
                if (m != null)
                    harmony.Patch(m, postfix: new HarmonyMethod(
                        AccessTools.Method(typeof(DebugAiRefit), nameof(Postfix_ChangeState))));

                HotfixLog.Info("[SMS.1.DBG] DebugAiRefit: armed  aiRefit -> " + HotfixParams.DebugAiRefit.ToString("0.###")
                               + "  (will apply once the campaign is loaded)");
            }
            catch (Exception e) { HotfixLog.Error("[SMS.1.DBG] DebugAiRefit install failed: " + e); }
        }

        internal static void Postfix_ChangeState(GameManager.GameState newState)
        {
            if (_applied || _scheduled) return;
            if (newState == GameManager.GameState.MainMenu) return;
            _scheduled = true;
            try { MelonLoader.MelonCoroutines.Start(ApplyDeferred()); }
            catch (Exception e) { _scheduled = false; HotfixLog.Warn("[SMS.1.DBG] coroutine start failed: " + e.Message); }
        }

        private static System.Collections.IEnumerator ApplyDeferred()
        {
            try
            {
                for (int i = 0; i < 600; i++)     // 最多等 10 秒
                {
                    yield return null;
                    if (_applied) yield break;
                    if (TryApply()) { _applied = true; yield break; }
                }
                HotfixLog.Warn("[SMS.1.DBG] DebugAiRefit: gave up (GameData.aiPersonalities never became ready)");
            }
            finally { _scheduled = false; }
        }

        private static bool TryApply()
        {
            try
            {
                var gd = G.GameData;
                if (gd == null) return false;

                var dict = gd.aiPersonalities;
                if (dict == null || dict.Count == 0) return false;

                float target = HotfixParams.DebugAiRefit;
                int n = 0;
                foreach (var ap in dict.Values)
                {
                    if (ap == null) continue;
                    float old = ap.aiRefit;
                    // ★ 只升不降：实测各性格本来就是 0.02 ~ 1.76 的**权重**（不是 0..1 概率），
                    //   直接写死会把 japan_ai(1.65) / spain_ai(1.76) 反而调低。
                    float nv = Math.Max(old, target);
                    try
                    {
                        if (HotfixParams.IsApply) ap.aiRefit = nv;
                    }
                    catch (Exception e)
                    {
                        HotfixLog.Warn("[SMS.1.DBG] set aiRefit failed for '" + SafeName(ap) + "': " + e.Message);
                        continue;
                    }
                    n++;
                    if (n <= 30)
                        HotfixLog.Info("[SMS.1.DBG] aiRefit: '" + SafeName(ap) + "'  " + old.ToString("0.###")
                                       + " -> " + (HotfixParams.IsApply ? nv.ToString("0.###") : "<unchanged, log mode>"));
                }

                HotfixLog.Warn("[SMS.1.DBG] aiRefit boosted for " + n + " AI personalit(ies)  (mode=" + HotfixParams.Mode + ")");
                return n > 0;
            }
            catch (Exception e)
            {
                HotfixLog.Warn("[SMS.1.DBG] TryApply: " + e.Message);
                return false;
            }
        }

        private static string SafeName(AiPersonalities ap)
        {
            try { return ap.name; } catch { }
            try { return ap.aiTextName; } catch { }
            return "<unnamed>";
        }
    }
}
