using MelonLoader;
using HarmonyLib;
using UnityEngine;
using Il2Cpp;
using System.Collections.Generic;

namespace TweaksAndFixes
{
    [HarmonyPatch(typeof(CampaignConquestEvent))]
    internal class Patch_CampaignConquestEvent
    {
        [HarmonyPatch(nameof(CampaignConquestEvent.GetConquestChance))]
        [HarmonyPrefix]
        internal static bool Prefix_GetConquestChance(CampaignConquestEvent __instance, bool force, ref float __result)
        {
            __result = GetConquestChance(__instance, force);
            return false;
        }

        private static float GetConquestChance(CampaignConquestEvent _this, bool force)
        {
            if (_this.cachedConquestChance != -1f && !force)
                return _this.cachedConquestChance;

            float totalReq = _this.requiredTonnage + _this.AdditionalRequiredTonnage;
            if (_this.CurrentTonnage < totalReq)
            {
                _this.cachedConquestChance = 0f;
                return 0f;
            }

            var ratioLerped = Mathf.Lerp(Config.Param("taf_conquest_event_chance_mult_starting_duration", 0.01f), Config.Param("taf_conquest_event_chance_mult_full_duration", 0.5f), _this.DurationTotal / (float)_this.maxDuration)
                * _this.CurrentTonnage / totalReq;

            float killFactor;
            // stock tests evt type - 2 <= 1. (a) we reverse for cleanliness,
            // and (b) there _aren't_ any higher-number types! Probably meant to
            // just be evt type <= 1, since that's the test used in CheckProgress
            if (_this.EventType > BaseCampaignSpecialEvent.SpecialEventType.RebellionLand)
            {
                killFactor = Mathf.Clamp01((_this.AttackerKillsTotal + 1f) / (_this.DefenderKillsTotal + 1f)) * 0.75f;
                var attacker = _this.Attacker.Player();
                var defender = _this.Defender.Player();
                if (attacker != null && attacker.isMajor && defender != null && !defender.isMajor)
                    killFactor *= 1.25f;
            }
            else
            {
                // ⚠ [SMS.2.RBL][TODO SMS.3] 这段是 Rebellion / RebellionLand 的分支，**吨位项是反的**：
                //     叛乱事件里 CurrentTonnage 是**防御方**兵力，而 chance 在 CheckEventProgress
                //     （1.7: 0x2C56E2 comiss xmm7,xmm6 / jae）里被当作**起义成功率**使用。
                //     ⇒ 防御方越强，chance 越高（越强越容易丢省）；空城时 chance≈0 ⇒ "空城反而防守成功"。
                //     duke 只修了击杀比（下方 killRatio 的注释），没修吨位项，且这里再加 0.66 会进一步放大。
                //   计划的正确修法（SMS.3，需单独立项评审）：
                //     ratioLerped = Lerp(start, full, dur) * (1 - clamp01(CurrentTonnage / totalReq));
                //     并去掉 +0.66 的常数补偿（或改为只在 duration 推进时加）。
                //   现状：SMS.2 用我们自己的 `rebellion_chance_mode=2` 覆盖本函数结果（默认生效）。
                ratioLerped += Config.Param("taf_conquest_event_add_chance", 0.66f);
                // Game is bugged and has this ratio flipped
                var killRatio = (_this.AttackerKillsTotal + 1f) / (_this.DefenderKillsTotal + 1f);
                if (totalReq != -1f)
                    killRatio *= Mathf.Lerp(1f, 2f, totalReq / 500000f);
                killFactor = Mathf.Clamp01(killRatio * 0.5f);
            }

            _this.cachedConquestChance = Mathf.Clamp01(Mathf.LerpUnclamped(1f, killFactor, Config.Param("taf_conquest_event_kill_factor", 1f)) * ratioLerped);
            return _this.cachedConquestChance;
        }
    }
}
