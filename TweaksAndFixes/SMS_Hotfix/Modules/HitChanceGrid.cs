using System;
using HarmonyLib;
using UnityEngine;

namespace TAFHotfixBatch1.Modules
{
    /// <summary>
    /// [SMS.2.HC] "命定之死"：把射击概率下限从原版 1%（0.01）改成可调（默认 0.1% = 0.001）。
    ///
    /// 定位：`Ship::HitChance/10` 末尾 `chance = Mathf.Clamp(chance, 0.01f, min(1, Lerp(...)))`
    /// （1.7 @ 0x181f63a80；docs/05-design-mechanics/20a-minhit-1pct-floor.md）。
    ///
    /// ⚠ rc11 的教训：Prefix 里**不要**访问 `HotfixParams`（触发 cctor / 日志），
    /// 该方法是全局热路径，早期初始化期间会被大量调用 ⇒ rc11 启动即挂。
    /// 本版把判定条件在 **install 时**快照成普通静态字段，热路径只做浮点比较。
    /// </summary>
    internal static class HitChanceGrid
    {
        private const float VanillaFloor = 0.01f;
        private static float _floor = 0.001f;
        private static bool _on = true;
        private static int _hits;

        internal static void Install(HarmonyLib.Harmony harmony)
        {
            try
            {
                // 安装前把参数快照成普通字段（热路径不再触碰 HotfixParams）
                _on = HotfixParams.IsApply && HotfixParams.HitChanceGrid > 0f
                      && HotfixParams.HitChanceGrid < VanillaFloor;
                _floor = HotfixParams.HitChanceGrid;

                var m = AccessTools.Method(typeof(Mathf), "Clamp", new Type[] { typeof(float), typeof(float), typeof(float) });
                if (m == null) { HotfixLog.Warn("[SMS.2.HC] Mathf.Clamp not found"); return; }
                harmony.Patch(m, prefix: new HarmonyMethod(AccessTools.Method(typeof(HitChanceGrid), nameof(Prefix))));
                HotfixLog.Info("[SMS.2.HC] HitChanceFloor armed: floor=" + _floor.ToString("F6")
                    + " on=" + _on + " (vanilla 0.01)  mode=" + HotfixParams.Mode);
            }
            catch (Exception e) { HotfixLog.Error("[SMS.2.HC] install failed: " + e); }
        }

        internal static void Prefix(ref float min, float max)
        {
            if (!_on) return;
            if (min != VanillaFloor) return;
            if (max > 1.01f || max < 0.5f) return;
            min = _floor;
            if (_hits++ == 0) HotfixLog.Info("[SMS.2.HC] floor applied (first hit)");
        }
    }
}
