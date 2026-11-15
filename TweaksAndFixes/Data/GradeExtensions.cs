//#define LOGHULLSTATS
//#define LOGHULLSCALES
//#define LOGPARTSTATS
//#define LOGGUNSTATS

using MelonLoader;
using Il2Cpp;

#pragma warning disable CS0649
#pragma warning disable CS8600
#pragma warning disable CS8602
#pragma warning disable CS8603
#pragma warning disable CS8604
#pragma warning disable CS8618
#pragma warning disable CS8625

namespace TweaksAndFixes
{
    public static class GradeExtensions
    {
        public class GunDataExtension : Serializer.IPostProcess
        {
            [Serializer.Field] string name;
            [Serializer.Field] string param;

            public void PostProcess()
            {
                if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(param))
                    return;

                if (!G.GameData.guns.TryGetValue(name, out var gd))
                    return;

                // To make life easier for modders, let them use the column names
                // in the csv rather than the names of the fields.
                param = param.Replace("firerate(", "firerates(");
                param = param.Replace("barrelW(", "barrelWeights(");
                param = param.Replace("firerate(", "firerates(");
                param = param.Replace("shellW(", "shellWeights(");
                param = param.Replace("shellV(", "shellVelocities(");
                param = param.Replace("range(", "ranges(");
                param = param.Replace("accuracy(", "accuracies(");
                param = param.Replace("penetration(", "penetrations(");

                if (!Serializer.Human.FillIndexedDicts(gd, param, true))
                    Melon<TweaksAndFixes>.Logger.Msg($"Failed to parse params for gun data {gd.name}!");

                int max = GetMaxGrade(gd);
                Config.MaxGunGrade = Math.Max(Config.MaxGunGrade, max);
            }

            public static int GetMaxGrade(GunData gd)
            {
                int max = 6;
                for (; ; ++max)
                {
                    if (gd.accuracies.ContainsKey(max)
                        || gd.barrelWeights.ContainsKey(max)
                        || gd.firerates.ContainsKey(max)
                        || gd.penetrations.ContainsKey(max)
                        || gd.ranges.ContainsKey(max)
                        || gd.shellVelocities.ContainsKey(max)
                        || gd.shellWeights.ContainsKey(max)
                        || gd.accuracies.ContainsKey(max))
                        continue;

                    break;
                }
                return max - 1;
            }

            public static void LoadData()
            {
                var text = Serializer.CSV.GetTextFromFileOrAsset("guns");
                if (text != null)
                {
                    List<GunDataExtension> list = new List<GunDataExtension>();
                    Serializer.CSV.Read<List<GunDataExtension>, GunDataExtension>(text, list, true, true);
                }

                // We need to ensure that everything goes up to the max grade.
                foreach (var gd in G.GameData.guns.Values)
                {
                    ModUtils.FillGradeData(gd.accuracies, Config.MaxGunGrade);
                    ModUtils.FillGradeData(gd.barrelWeights, Config.MaxGunGrade);
                    ModUtils.FillGradeData(gd.firerates, Config.MaxGunGrade);
                    ModUtils.FillGradeData(gd.penetrations, Config.MaxGunGrade);
                    ModUtils.FillGradeData(gd.ranges, Config.MaxGunGrade);
                    ModUtils.FillGradeData(gd.shellVelocities, Config.MaxGunGrade);
                    ModUtils.FillGradeData(gd.shellWeights, Config.MaxGunGrade);
                    ModUtils.FillGradeData(gd.accuracies, Config.MaxGunGrade);
                }
                if (Config.MaxGunGrade > 5)
                {
                    GunData.gradesTotal = Config.MaxGunGrade;
                    Melon<TweaksAndFixes>.Logger.Msg("Max gun grade is now " + Config.MaxGunGrade);
                }
            }
        }

        public class PartModelExtension : Serializer.IPostProcess
        {
            [Serializer.Field] string name;
            [Serializer.Field] string param;

            public void PostProcess()
            {
                if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(param))
                    return;

                if (!G.GameData.partModels.TryGetValue(name, out var pm))
                    return;

                // To make life easier for modders, let them use the column names
                // in the csv rather than the names of the fields.
                param = param.Replace("model(", "models(");
                param = param.Replace("scale(", "scales(");
                param = param.Replace("max_scale(", "maxScales(");
                param = param.Replace("weight_modifier(", "weightModifiers(");
                param = param.Replace("caliber_length_modifier(", "caliberLengthModifiers(");

                Serializer.Human.FillIndexedDicts(pm, param, true);
            }

            public static void LoadData()
            {
                var text = Serializer.CSV.GetTextFromFileOrAsset("partModels");
                if (text != null)
                {
                    List<PartModelExtension> list = new List<PartModelExtension>();
                    Serializer.CSV.Read<List<PartModelExtension>, PartModelExtension>(text, list, true, true);
                }

                // We need to ensure that everything goes up to the max grade.
                foreach (var pm in G.GameData.partModels.Values)
                {
                    // Skip partmodels that aren't guns
                    if (pm.caliberLengthModifiers.count < 5)
                        continue;

                    ModUtils.FillGradeData(pm.caliberLengthModifiers, Config.MaxGunGrade);
                    ModUtils.FillGradeData(pm.maxScales, Config.MaxGunGrade);
                    ModUtils.FillGradeData(pm.models, Config.MaxGunGrade);
                    ModUtils.FillGradeData(pm.scales, Config.MaxGunGrade);
                    ModUtils.FillGradeData(pm.weightModifiers, Config.MaxGunGrade);
                }
            }
        }

        /// <summary>
        /// [SMS3-20d] 抬高 `Config.MaxGunGrade` 之后逐列补齐（正向判空 + 反向回滚）。
        ///
        /// **先明确一层引擎事实**（1.7.0.0 `dump.cs`）：`GunData` / `PartModelData` 只有 `_1.._5` 的**平铺字段**
        /// （`barrelW_1..5`、`model_1..5`、`scale_1..5` …），**没有 `_6`**。
        /// ⇒ CSV 里写的 `accuracy_6` / `model_6` 列**根本不会被引擎读入**；Mk6+ 只能通过 TAF 的 `param` 扩展列
        /// （`accuracy(6;…)` 等，填进 `accuracies` 等字典）或 `*_override.csv` 进入游戏。
        /// 本 mod 的 guns/partModels 没有 `param` 列 ⇒ Mk6 实际无数据，只能按"Mk6 ← Mk5"回滚。
        ///
        /// 规则（用户 2026-11-15 口径）：
        /// - partModels：`model_N` 非空、`scale_N`/`max_scale_N` 非 0 才算有数据；`weight_modifier_N`/`caliber_length_modifier_N`
        ///   为空时回落到 `default` 行（序列化器 useDefault 已做），再不行按列回滚；
        /// - guns：任何有效列的值不应为 0，0 视为缺列 → 该列单独回滚。
        /// - 统计"真实数据"时只算**默认回退行**（无 country / shipType 覆盖）的 partModels。
        /// </summary>
        public static void ReapplyGradeData()
        {
            if (Config.MaxGunGrade <= 5)
                return;

            int max = Config.MaxGunGrade;
            int pmFallback = 0, pmReal = 0, gunRows = 0, gunReal = 0;

            // ---- 1) partModels 先行（模型/缩放决定炮重与倍径修正） ----
            foreach (var pm in G.GameData.partModels.Values)
            {
                if (pm.caliberLengthModifiers.count < 5)
                    continue;   // 只处理火炮类 partModel

                bool isFallbackRow = string.IsNullOrEmpty(pm.countries) && string.IsNullOrEmpty(pm.shipTypes);
                if (isFallbackRow) pmFallback++;

                bool real = pm.models.ContainsKey(max) && ModUtils.IsValidGradeValue(pm.models[max], ModUtils.GradeValueRule.NonEmptyString)
                            && pm.scales.ContainsKey(max) && ModUtils.IsValidGradeValue(pm.scales[max], ModUtils.GradeValueRule.NonZeroNumber)
                            && pm.maxScales.ContainsKey(max) && ModUtils.IsValidGradeValue(pm.maxScales[max], ModUtils.GradeValueRule.NonZeroNumber);
                if (real)
                {
                    pmReal++;
                }
                else if (isFallbackRow && Config.Param("verbose", 0f) > 0f)
                {
                    Melon<TweaksAndFixes>.Logger.Msg("[SMS.3.GRADE] partModel " + pm.name
                        + ": grade " + max + " has no usable model/scale -> per-column rollback");
                }

                ModUtils.FillGradeData(pm.models, max, ModUtils.GradeValueRule.NonEmptyString);
                ModUtils.FillGradeData(pm.scales, max, ModUtils.GradeValueRule.NonZeroNumber);
                ModUtils.FillGradeData(pm.maxScales, max, ModUtils.GradeValueRule.NonZeroNumber);
                ModUtils.FillGradeData(pm.weightModifiers, max, ModUtils.GradeValueRule.NonZeroNumber);
                ModUtils.FillGradeData(pm.caliberLengthModifiers, max, ModUtils.GradeValueRule.NonZeroNumber);
            }

            // ---- 2) guns 射表 ----
            foreach (var gd in G.GameData.guns.Values)
            {
                gunRows++;

                bool real = gd.accuracies.ContainsKey(max) && ModUtils.IsValidGradeValue(gd.accuracies[max], ModUtils.GradeValueRule.PositiveNumber)
                            && gd.ranges.ContainsKey(max) && ModUtils.IsValidGradeValue(gd.ranges[max], ModUtils.GradeValueRule.PositiveNumber);
                if (real) gunReal++;

                ModUtils.FillGradeData(gd.accuracies, max, ModUtils.GradeValueRule.PositiveNumber);
                ModUtils.FillGradeData(gd.barrelWeights, max, ModUtils.GradeValueRule.PositiveNumber);
                ModUtils.FillGradeData(gd.firerates, max, ModUtils.GradeValueRule.PositiveNumber);
                ModUtils.FillGradeData(gd.penetrations, max, ModUtils.GradeValueRule.PositiveNumber);
                ModUtils.FillGradeData(gd.ranges, max, ModUtils.GradeValueRule.PositiveNumber);
                ModUtils.FillGradeData(gd.shellVelocities, max, ModUtils.GradeValueRule.PositiveNumber);
                ModUtils.FillGradeData(gd.shellWeights, max, ModUtils.GradeValueRule.PositiveNumber);
            }

            GunData.gradesTotal = Config.MaxGunGrade;

            Melon<TweaksAndFixes>.Logger.Msg("[SMS.3.GRADE] per-column grade fill up to " + max
                + ": fallback partModels " + pmFallback + " (real model at grade " + max + ": " + pmReal + "), "
                + "guns " + gunRows + " (real table at grade " + max + ": " + gunReal + ")");

            if (pmReal == 0 && gunReal == 0)
                Melon<TweaksAndFixes>.Logger.Warning("[SMS.3.GRADE] grade " + max
                    + " has no real data at all (base-game fields only go to grade 5; Mk6+ must be declared via TAF `param` columns). "
                    + "All columns were carried over from the nearest valid grade (expected for Mk6-on-Mk5 mods).");
        }

        public static void LoadData()
        {
            GunDataExtension.LoadData();
            PartModelExtension.LoadData();
        }
    }
}