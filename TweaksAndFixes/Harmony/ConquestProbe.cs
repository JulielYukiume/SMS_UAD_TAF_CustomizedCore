using System;
using HarmonyLib;
using Il2Cpp;
using MelonLoader;
using UnityEngine;
using TAFHotfixBatch1;

namespace TweaksAndFixes
{
    /// <summary>
    /// [SMS.2.CQ] 殖民征服 / 叛乱事件（`CampaignConquestEvent`）成功率取证探针。
    ///
    /// 背景：该事件的 tooltip 显示的是"**防御方**吨位 / 所需吨位"，但 `Chance to succeed`
    /// 实际取的是**攻击方（起义方）**的成功率 ⇒ 玩家作为防御方看到 100% 反而必丢省份。
    ///
    /// dump.cs（TypeDefIndex 7646）关键成员：
    ///   CurrentTonnage / RequiredTonnage / AdditionalRequiredTonnage / DurationCurrent / DurationTotal
    ///   private float invasionForce; private float cachedConquestChance;
    ///   private float GetConquestChance(bool force = false)
    ///   ValueTuple&lt;List&lt;TaskForce&gt;, List&lt;TaskForce&gt;&gt; UpdateTonnage()
    ///   override GetDescription()  ← 生成 tooltip 文本（含 {8}%）
    ///   BaseCampaignSpecialEvent: PlayerData Attacker / Defender / SpecialEventType EventType
    ///
    /// 开关：`conquest_probe`（默认 0，仅 DEBUG 配置使用）。
    /// </summary>
    internal static class ConquestProbe
    {
        private static float _next;

        internal static void OnInit(CampaignConquestEvent e)
        {
            if (!HotfixParams.ConquestProbe || e == null) return;
            HotfixLog.Info("[SMS.2.CQ] init event='" + Safe(e.Name)
                           + "' type=" + e.EventType
                           + " attacker=" + SafePlayer(e.Attacker)
                           + " defender=" + SafePlayer(e.Defender));
        }

        internal static void OnChance(CampaignConquestEvent e, float result)
        {
            if (!HotfixParams.ConquestProbe || e == null) return;
            float now = UnityEngine.Time.realtimeSinceStartup;
            if (now < _next) return;
            _next = now + 3f;

            int cur = 0, req = 0, add = 0;
            try { cur = e.CurrentTonnage; } catch { }
            try { req = e.RequiredTonnage; } catch { }
            try { add = e.AdditionalRequiredTonnage; } catch { }

            HotfixLog.Info("[SMS.2.CQ] chance=" + result.ToString("0.####")
                           + " current(defender)=" + cur
                           + " required=" + req + "+" + add
                           + " dur=" + e.DurationCurrent + "/" + e.DurationTotal
                           + " attacker=" + SafePlayer(e.Attacker)
                           + " defender=" + SafePlayer(e.Defender));
        }

        private static string Safe(string s) { return string.IsNullOrEmpty(s) ? "?" : s; }

        private static string SafePlayer(PlayerData p)
        {
            try
            {
                if (p == null) return "null";
                return Safe(p.name);
            }
            catch { return "?"; }
        }
    }

    [HarmonyPatch(typeof(CampaignConquestEvent), "GetConquestChance")]
    internal static class Patch_CQ_Chance
    {
        [HarmonyPostfix]
        internal static void Postfix(CampaignConquestEvent __instance, ref float __result)
        {
            try
            {
                float raw = __result;

                // ★ [SMS.2.CQ] 叛乱事件（EventType=2）的"谁占优"口径与原版相反：
                //   原版对 ConquestEvent(0) 用"current(进攻方)/required"算进攻方成功率；
                //   叛乱事件复用同一公式，但 current 实际是**防御方**吨位 ⇒ 防御方兵力越多，
                //   叛乱成功率越高（显示与结算都反）。这里对指定类型做 1-x 翻转。
                if (HotfixParams.ConquestChanceMode == 2 && NeedFlip(__instance))
                {
                    // 自定义公式：ratio 以"防御方吨位 / 所需吨位"为准，
                    //   成功（=叛乱得手）概率 = 1 - clamp((ratio-1)/(ratioFull-1)) × 时长系数
                    int cur = 0, req = 0, dc = 0, dt = 0;
                    try { cur = __instance.CurrentTonnage; } catch { }
                    try { req = __instance.RequiredTonnage; } catch { }
                    try { req += __instance.AdditionalRequiredTonnage; } catch { }
                    try { dc = __instance.DurationCurrent; dt = __instance.DurationTotal; } catch { }

                    // 语义（对齐 1.7 征服）：防御方达到 required（ratio>=ratioFull）⇒ 起义必败（概率 0）；
                    //   ratio 越低，起义成功率越高；时长只作为"起义方随时间推进"的系数。
                    float ratio = req > 0 ? (float)cur / req : 0f;
                    float full = Mathf.Max(0.01f, HotfixParams.ConquestRatioFull);
                    float prog = Mathf.Clamp01(ratio / full);          // 压制程度
                    float durFrac = dt > 0 ? Mathf.Clamp01((float)dc / dt) : 0f;
                    float mult = Mathf.Lerp(HotfixParams.ConquestMultStart, HotfixParams.ConquestMultFull, durFrac);
                    __result = Mathf.Clamp01((1f - prog) * mult);

                    if (HotfixParams.ConquestProbe)
                        HotfixLog.Info("[SMS.2.CQ] CUSTOM raw=" + raw.ToString("0.###")
                                       + " ratio=" + ratio.ToString("0.##")
                                       + " prog=" + prog.ToString("0.##")
                                       + " mult=" + mult.ToString("0.###")
                                       + " -> " + __result.ToString("0.###"));
                }
                else if (HotfixParams.ConquestChanceMode == 1 && NeedFlip(__instance))
                {
                    __result = 1f - raw;
                    if (HotfixParams.ConquestProbe)
                        HotfixLog.Info("[SMS.2.CQ] FLIP raw=" + raw.ToString("0.####")
                                       + " -> " + __result.ToString("0.####")
                                       + " type=" + __instance.EventType);
                }

                ConquestProbe.OnChance(__instance, raw);
            }
            catch { }
        }

        private static bool NeedFlip(CampaignConquestEvent e)
        {
            try
            {
                int t = (int)e.EventType;
                string list = HotfixParams.ConquestFixTypes;
                if (string.IsNullOrEmpty(list)) return false;
                foreach (var part in list.Split(','))
                {
                    int v;
                    if (int.TryParse(part.Trim(), out v) && v == t) return true;
                }
            }
            catch { }
            return false;
        }
    }

    /// <summary>结算判定：事件是赢/输/继续 —— 这是"最终判定是否反了"的决定性取证点。</summary>
    [HarmonyPatch(typeof(CampaignConquestEvent), "CheckEventProgress")]
    internal static class Patch_CQ_Progress
    {
        [HarmonyPostfix]
        internal static void Postfix(CampaignConquestEvent __instance, BaseCampaignSpecialEvent.EventProgress __result)
        {
            try
            {
                if (!HotfixParams.ConquestProbe || __instance == null) return;
                int cur = 0, req = 0;
                try { cur = __instance.CurrentTonnage; } catch { }
                try { req = __instance.RequiredTonnage; } catch { }
                HotfixLog.Info("[SMS.2.CQ] progress=" + __result
                               + " current=" + cur + " required=" + req
                               + " dur=" + __instance.DurationCurrent + "/" + __instance.DurationTotal
                               + " attacker=" + (__instance.Attacker != null ? __instance.Attacker.name : "?")
                               + " defender=" + (__instance.Defender != null ? __instance.Defender.name : "?"));
            }
            catch { }
        }
    }

    [HarmonyPatch(typeof(CampaignConquestEvent), "Init")]
    internal static class Patch_CQ_Init
    {
        [HarmonyPostfix]
        internal static void Postfix(CampaignConquestEvent __instance)
        {
            try { ConquestProbe.OnInit(__instance); } catch { }
        }
    }
}
