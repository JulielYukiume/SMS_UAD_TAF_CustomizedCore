using System;
using System.Collections.Generic;
using System.Globalization;
using MelonLoader;
using Il2Cpp;

namespace TweaksAndFixes
{
    /// <summary>
    /// [SMS3-20] 把 CSV 里 **grade ≥ 6 的列**（`accuracy_6`、`model_6`、`scale_6` …）读进游戏对象的字典。
    ///
    /// 为什么需要：1.7.0.0 的 `GunData` / `PartModelData` 只有 `_1.._5` 的平铺字段（`dump.cs` 确认），
    /// 引擎加载器**不会**把 `_6` 列映射到 `accuracies` / `models` 等字典。
    /// 实测（rc39f 日志）：`models[6] already loaded: 0/228`、`accuracies[6]: 0/39`，
    /// 而 mod 的 CSV 里明明有完整的 grade-6 数据 ⇒ 必须由 TAF 自己补读。
    ///
    /// 合法性规则（用户 2026-11-15 口径）：
    /// - partModels：`model_N` 非空、`scale_N`/`max_scale_N` **不为 0**、`weight_modifier_N`/`caliber_length_modifier_N` 不为 0；
    ///   任一不满足则该列**不写入**（交给后续逐列回滚去补），避免用坏值污染字典；
    /// - guns：射表列 `> 0` 才写入；
    /// - 只处理 `grade >= 6`（1..5 由引擎负责）；`default` 行与 `#` 注释行跳过。
    /// </summary>
    public static class GradeColumns
    {
        private const int FirstExtraGrade = 6;

        public static void LoadAll()
        {
            try { LoadPartModels(); }
            catch (Exception e) { Melon<TweaksAndFixes>.Logger.Warning("[SMS.3.GRADE] LoadPartModels failed: " + e.Message); }

            try { LoadGuns(); }
            catch (Exception e) { Melon<TweaksAndFixes>.Logger.Warning("[SMS.3.GRADE] LoadGuns failed: " + e.Message); }
        }

        // ---------------------------------------------------------------- partModels

        private static void LoadPartModels()
        {
            string text = Serializer.CSV.GetTextFromFileOrAsset("partModels");
            if (string.IsNullOrEmpty(text)) return;

            var rows = Parse(text, out var header);
            if (header == null) return;

            int colModel = -1, colScale = -1, colMaxScale = -1, colWeight = -1, colCalLen = -1;

            int loadedModels = 0, loadedRows = 0;

            foreach (var row in rows)
            {
                string name = Get(row, 0);
                if (string.IsNullOrEmpty(name) || name == "default" || name.StartsWith("#") || name.StartsWith("###"))
                    continue;

                if (!G.GameData.partModels.TryGetValue(name, out var pm) || pm == null)
                    continue;

                loadedRows++;

                for (int i = 0; i < header.Count; i++)
                {
                    string col = header[i];
                    if (!TrySplitGradeColumn(col, out var field, out int grade) || grade < FirstExtraGrade)
                        continue;

                    string raw = Get(row, i);
                    if (string.IsNullOrWhiteSpace(raw))
                        continue;

                    switch (field)
                    {
                        case "model":
                            if (pm.models == null) pm.models = new();
                            pm.models[grade] = raw.Trim();
                            loadedModels++;
                            break;

                        case "scale":
                            if (!TryParseFloat(raw, out float sc) || sc == 0f) break;
                            if (pm.scales == null) pm.scales = new();
                            pm.scales[grade] = sc;
                            break;

                        case "max_scale":
                            if (!TryParseFloat(raw, out float msc) || msc == 0f) break;
                            if (pm.maxScales == null) pm.maxScales = new();
                            pm.maxScales[grade] = msc;
                            break;

                        case "weight_modifier":
                            if (!TryParseFloat(raw, out float wm) || wm == 0f) break;
                            if (pm.weightModifiers == null) pm.weightModifiers = new();
                            pm.weightModifiers[grade] = wm;
                            break;

                        case "caliber_length_modifier":
                            if (!TryParseFloat(raw, out float cl) || cl == 0f) break;
                            if (pm.caliberLengthModifiers == null) pm.caliberLengthModifiers = new();
                            pm.caliberLengthModifiers[grade] = cl;
                            break;
                    }
                }
            }

            Melon<TweaksAndFixes>.Logger.Msg("[SMS.3.GRADE] csv-column load (partModels): " + loadedRows
                + " rows scanned, " + loadedModels + " model entries written for grade>=" + FirstExtraGrade);
        }

        // ---------------------------------------------------------------- guns

        private static void LoadGuns()
        {
            string text = Serializer.CSV.GetTextFromFileOrAsset("guns");
            if (string.IsNullOrEmpty(text)) return;

            var rows = Parse(text, out var header);
            if (header == null) return;

            int rowsScanned = 0, fieldsWritten = 0;

            foreach (var row in rows)
            {
                string name = Get(row, 0);
                if (string.IsNullOrEmpty(name) || name == "default" || name.StartsWith("#") || name.StartsWith("###"))
                    continue;

                if (!G.GameData.guns.TryGetValue(name, out var gd) || gd == null)
                    continue;

                rowsScanned++;

                for (int i = 0; i < header.Count; i++)
                {
                    string col = header[i];
                    if (!TrySplitGradeColumn(col, out var field, out int grade) || grade < FirstExtraGrade)
                        continue;

                    string raw = Get(row, i);
                    if (string.IsNullOrWhiteSpace(raw) || !TryParseFloat(raw, out float v) || v <= 0f)
                        continue;

                    switch (field)
                    {
                        case "accuracy":   (gd.accuracies ??= new())[grade] = v; break;
                        case "barrelW":    (gd.barrelWeights ??= new())[grade] = v; break;
                        case "firerate":   (gd.firerates ??= new())[grade] = v; break;
                        case "penetration":(gd.penetrations ??= new())[grade] = v; break;
                        case "range":      (gd.ranges ??= new())[grade] = v; break;
                        case "shellV":     (gd.shellVelocities ??= new())[grade] = v; break;
                        case "shellW":     (gd.shellWeights ??= new())[grade] = v; break;
                        case "accuracy_old":
                        case "accuracy_new":
                            break;
                        default:
                            continue;
                    }

                    fieldsWritten++;
                }
            }

            Melon<TweaksAndFixes>.Logger.Msg("[SMS.3.GRADE] csv-column load (guns): " + rowsScanned
                + " rows scanned, " + fieldsWritten + " table entries written for grade>=" + FirstExtraGrade);
        }

        // ---------------------------------------------------------------- helpers

        private static bool TrySplitGradeColumn(string col, out string field, out int grade)
        {
            field = null;
            grade = -1;
            if (string.IsNullOrEmpty(col)) return false;

            int us = col.LastIndexOf('_');
            if (us <= 0 || us == col.Length - 1) return false;

            if (!int.TryParse(col.Substring(us + 1), NumberStyles.Integer, CultureInfo.InvariantCulture, out grade))
                return false;

            field = col.Substring(0, us).Trim().ToLowerInvariant();
            return grade > 0;
        }

        private static bool TryParseFloat(string s, out float v)
        {
            v = 0f;
            if (string.IsNullOrWhiteSpace(s)) return false;
            return float.TryParse(s.Trim(), NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out v);
        }

        private static string Get(List<string> row, int i) => row != null && i >= 0 && i < row.Count ? row[i] : string.Empty;

        /// <summary>
        /// 极简 CSV 解析：支持双引号包裹与引号内的逗号；跳过 `#` 注释行。
        /// 只认**主表**表头（含 `_1` 列，如 `model_1` / `accuracy_1`）；
        /// 文件后面若还有 `@name,param` 之类的 TAF 扩展段，则跳过该段的行（避免列错位）。
        /// </summary>
        private static List<List<string>> Parse(string text, out List<string> header)
        {
            header = null;
            var rows = new List<List<string>>();
            bool inMainSection = false;

            foreach (var line in text.Split('\n'))
            {
                string l = line.TrimEnd('\r');
                if (l.Length == 0) continue;

                var cells = SplitCsvLine(l);
                if (cells.Count == 0) continue;

                string first = cells[0].Trim();

                if (first.StartsWith("@name"))
                {
                    bool isMain = false;
                    foreach (var c in cells)
                    {
                        if (c.Trim().EndsWith("_1")) { isMain = true; break; }
                    }

                    if (header == null && isMain)
                    {
                        header = cells;
                        inMainSection = true;
                    }
                    else
                    {
                        inMainSection = false;   // 后续扩展段（param 等）：忽略其行
                    }

                    continue;
                }

                if (first.StartsWith("#")) continue;
                if (!inMainSection) continue;

                rows.Add(cells);
            }

            return rows;
        }

        private static List<string> SplitCsvLine(string line)
        {
            var cells = new List<string>();
            var cur = new System.Text.StringBuilder();
            bool inQuotes = false;

            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];
                if (inQuotes)
                {
                    if (c == '"')
                    {
                        if (i + 1 < line.Length && line[i + 1] == '"') { cur.Append('"'); i++; }
                        else inQuotes = false;
                    }
                    else cur.Append(c);
                }
                else if (c == '"') inQuotes = true;
                else if (c == ',') { cells.Add(cur.ToString()); cur.Clear(); }
                else cur.Append(c);
            }

            cells.Add(cur.ToString());
            return cells;
        }
    }
}
