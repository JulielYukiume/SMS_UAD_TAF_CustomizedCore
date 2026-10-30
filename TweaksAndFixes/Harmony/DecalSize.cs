using System;
using HarmonyLib;
using Il2Cpp;
using TAFHotfixBatch1;
using UnityEngine;

namespace TweaksAndFixes
{
    /// <summary>
    /// [SMS.1.DECAL] 命中贴花按**炮弹口径**缩放（移植 Beta6 的 `Patch_Effect` + `Patch_DecalsManager`）。
    ///
    /// 为什么需要：
    ///   原版所有弹孔尺寸固定 ⇒ 打穿装甲的穿甲弹与表面爆炸一样大，
    ///   敌舰侧面会被打成"月球表面"却还没沉（视觉与伤害不匹配）。
    ///   duke 的做法：先用 `Effect.Hit` 记下当前命中口径，再在 `DecalsManager.CreateHitDecal2`
    ///   里按 `GunData.shellSize` 缩放贴花（鱼雷 ×0.25）。
    ///
    /// 配置：
    ///   · `decal_size`（0/1，默认 1）—— 总开关
    ///   · `taf_shell_hit_decal_size_multiplier`（默认 10）—— 与 duke 同名，兼容其配置习惯
    ///
    /// 注意（1.7 与 Beta6 的签名差异）：
    ///   · 1.7：`Effect.Hit(Ship, string, Vector3, Quaternion, Part, Ship.HitPrecalc, float, Projectile)`
    ///   · 1.7：`DecalsManager.CreateHitDecal2(DecalParameters, Transform, int)`
    ///   因此这里只用 **参数子集**（Harmony 允许），并显式给参数类型。
    /// </summary>
    [HarmonyPatch(typeof(Effect))]
    internal static class Patch_Effect_Hit
    {
        internal static string Caliber = "";

        [HarmonyPrefix]
        [HarmonyPatch("Hit")]
        internal static void Prefix_Hit(Ship ship, Part fromWeapon)
        {
            try
            {
                if (!HotfixParams.DecalSize) return;
                if (fromWeapon == null || fromWeapon.data == null) return;

                if (fromWeapon.data.isTorpedo) Caliber = "torp";
                else Caliber = ModUtils.toInt(fromWeapon.data.GetCaliberInch(ship)).ToString();
            }
            catch (Exception e) { HotfixLog.Warn("[SMS.1.DECAL] Hit prefix: " + e.Message); }
        }

        [HarmonyPostfix]
        [HarmonyPatch("Hit")]
        internal static void Postfix_Hit()
        {
            if (!HotfixParams.DecalSize) return;
            Caliber = "";
        }
    }

    /// <summary>按口径缩放命中贴花（duke 同款逻辑）。</summary>
    [HarmonyPatch(typeof(DecalsManager))]
    internal static class Patch_DecalsManager_CreateHitDecal2
    {
        [HarmonyPrefix]
        [HarmonyPatch("CreateHitDecal2")]
        internal static void Prefix_CreateHitDecal2(DecalParameters dp)
        {
            try
            {
                if (!HotfixParams.DecalSize) return;
                if (dp == null) return;

                var guns = G.GameData != null ? G.GameData.guns : null;
                if (guns == null) return;

                // 没有命中上下文（例如碰撞/环境弹坑）时，随机取一个口径，避免尺寸为 0
                if (Patch_Effect_Hit.Caliber.Length == 0)
                {
                    int n = guns.Count;
                    if (n <= 0) return;

                    var e = guns.GetEnumerator();
                    while (e.MoveNext())
                    {
                        Patch_Effect_Hit.Caliber = e.Current.Key;
                        if (UnityEngine.Random.value < 1f / n) break;
                    }
                }

                if (guns.ContainsKey(Patch_Effect_Hit.Caliber))
                {
                    var gun = guns[Patch_Effect_Hit.Caliber];
                    float mult = Config.Param("taf_shell_hit_decal_size_multiplier", 10f);
                    dp.scale = Vector3.one * gun.shellSize * mult;
                }
                else if (Patch_Effect_Hit.Caliber == "torp")
                {
                    dp.scale = dp.scale * 0.25f;
                }
                else
                {
                    HotfixLog.Warn("[SMS.1.DECAL] gun caliber '" + Patch_Effect_Hit.Caliber + "' not found");
                }

                Patch_Effect_Hit.Caliber = "";
            }
            catch (Exception e) { HotfixLog.Warn("[SMS.1.DECAL] CreateHitDecal2: " + e.Message); }
        }
    }
}
