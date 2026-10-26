using System;
using System.Text;
using Il2Cpp;
using MelonLoader;
using TAFHotfixBatch1;

namespace TweaksAndFixes.Modules
{
    /// <summary>
    /// [SMS.1.SHIPGEN] 3.22 的 AI 造船限制模块（移植 Beta6 `TweaksAndFixes.Modules.ShipGen`）。
    ///
    /// 数据来源：parts.csv / shipTypes 的 `paramx` 里的 `shipgen_limit(...)` 语法（TAF 自定义、
    /// 原版表里没有）。SMS 的 parts_override.csv 有 18 行 hull 使用它。
    ///
    /// 结构：
    ///   · `PartLimits`：一组"限制字段"（全部默认 -1 = 未设置，Merge 时"未设置才被覆盖"）
    ///   · `ShipGen`：静态表 `PartLimitData`（按 PartData.name 索引）+ CanAdd*/IsXAvailible 判定
    ///
    /// 说明：本文件为**分步移植**。第一批（P-a）包含：
    ///   · PartLimits 全部字段、两个构造、BuildRestrictionsFor、两个 Merge
    ///   · PartLimitData 表与 ParsePartLimitData 骨架
    ///   · Parse / ParseShipType / ParsePartData 的入口（第二批实现）
    /// </summary>
    internal class ShipGen
    {
        public class PartLimits
        {
            public static int totalLimits;

            public PartData data;
            public string key = string.Empty;
            public bool hasLimits;

            public float max_main_gun_cal = -1f;
            public float min_main_gun_cal = -1f;
            public float max_main_gun_barrels = -1f;
            public float min_main_gun_barrels = -1f;
            public float max_main_gun_count = -1f;
            public float max_main_gun_cal_count = -1f;
            public float max_sec_gun_cal = -1f;
            public float min_sec_gun_cal = -1f;
            public float max_sec_gun_barrels = -1f;
            public float min_sec_gun_barrels = -1f;
            public float max_sec_gun_count = -1f;
            public float max_sec_gun_cal_count = -1f;
            public float max_torp_tube_count = -1f;
            public float min_torp_tube_count = -1f;
            public float max_torp_count = -1f;
            public float pref_torp_diameter = -1f;

            /// <summary>复制构造（用于 Merge/派生，不改原对象）。</summary>
            public PartLimits(PartLimits from)
            {
                if (from == null) return;
                data = from.data;
                key = from.data != null ? from.data.name : string.Empty;
                hasLimits = from.hasLimits;
                max_main_gun_cal = from.max_main_gun_cal;
                min_main_gun_cal = from.min_main_gun_cal;
                max_main_gun_barrels = from.max_main_gun_barrels;
                min_main_gun_barrels = from.min_main_gun_barrels;
                max_main_gun_count = from.max_main_gun_count;
                max_main_gun_cal_count = from.max_main_gun_cal_count;
                max_sec_gun_cal = from.max_sec_gun_cal;
                min_sec_gun_cal = from.min_sec_gun_cal;
                max_sec_gun_barrels = from.max_sec_gun_barrels;
                min_sec_gun_barrels = from.min_sec_gun_barrels;
                max_sec_gun_count = from.max_sec_gun_count;
                max_sec_gun_cal_count = from.max_sec_gun_cal_count;
                max_torp_tube_count = from.max_torp_tube_count;
                min_torp_tube_count = from.min_torp_tube_count;
                max_torp_count = from.max_torp_count;
                pref_torp_diameter = from.pref_torp_diameter;
            }

            public PartLimits(PartData from)
            {
                data = from;
                key = from != null ? from.name : string.Empty;
                if (from != null) Parse(from);
            }

            /// <summary>由部件推导限制：把自身与所有父级（炮塔座等）的限制合并起来。</summary>
            public PartLimits BuildRestrictionsFor(Part part)
            {
                var result = new PartLimits(this);
                if (part == null || part.data == null) return result;

                try
                {
                    if (part.mount == null || part.mount.parentPart == null)
                    {
                        // 顶层部件：把船体（hull）的限制合并进来（hull 自身除外）
                        if (!part.data.isHull)
                        {
                            var ship = part.ship;
                            var hull = ship != null ? ship.hull : null;
                            var hd = hull != null ? hull.data : null;
                            if (hd != null && PartLimitData.ContainsKey(hd.name))
                                result.Merge(PartLimitData[hd.name]);
                        }
                        return result;
                    }

                    int guard = 100;
                    var parent = part.mount.parentPart;
                    while (guard-- > 0 && parent != null)
                    {
                        var pd = parent.data;
                        if (pd != null && PartLimitData.ContainsKey(pd.name))
                            result.Merge(PartLimitData[pd.name]);

                        if (parent.mount == null || parent.mount.parentPart == null) break;
                        parent = parent.mount.parentPart;
                    }
                }
                catch (Exception e) { HotfixLog.Warn("[SMS.1.SHIPGEN] BuildRestrictionsFor: " + e.Message); }

                return result;
            }

            /// <summary>把 b 里"本对象尚未设置（<0）"的字段补进来。</summary>
            public PartLimits Merge(PartLimits b)
            {
                if (b == null) return this;
                if (max_main_gun_cal < 0f) max_main_gun_cal = b.max_main_gun_cal;
                if (min_main_gun_cal < 0f) min_main_gun_cal = b.min_main_gun_cal;
                if (max_main_gun_barrels < 0f) max_main_gun_barrels = b.max_main_gun_barrels;
                if (min_main_gun_barrels < 0f) min_main_gun_barrels = b.min_main_gun_barrels;
                if (max_main_gun_count < 0f) max_main_gun_count = b.max_main_gun_count;
                if (max_main_gun_cal_count < 0f) max_main_gun_cal_count = b.max_main_gun_cal_count;
                if (max_sec_gun_cal < 0f) max_sec_gun_cal = b.max_sec_gun_cal;
                if (min_sec_gun_cal < 0f) min_sec_gun_cal = b.min_sec_gun_cal;
                if (max_sec_gun_barrels < 0f) max_sec_gun_barrels = b.max_sec_gun_barrels;
                if (min_sec_gun_barrels < 0f) min_sec_gun_barrels = b.min_sec_gun_barrels;
                if (max_sec_gun_count < 0f) max_sec_gun_count = b.max_sec_gun_count;
                if (max_sec_gun_cal_count < 0f) max_sec_gun_cal_count = b.max_sec_gun_cal_count;
                if (max_torp_tube_count < 0f) max_torp_tube_count = b.max_torp_tube_count;
                if (min_torp_tube_count < 0f) min_torp_tube_count = b.min_torp_tube_count;
                if (max_torp_count < 0f) max_torp_count = b.max_torp_count;
                if (pref_torp_diameter < 0f) pref_torp_diameter = b.pref_torp_diameter;
                hasLimits = hasLimits || b.hasLimits;
                return this;
            }

            /// <summary>静态版 Merge：返回 a 的副本（不改 a）。</summary>
            public static PartLimits Merge(PartLimits a, PartLimits b)
            {
                var r = new PartLimits(a);
                if (b == null) return r;
                if (r.max_main_gun_cal < 0f) r.max_main_gun_cal = b.max_main_gun_cal;
                if (r.min_main_gun_cal < 0f) r.min_main_gun_cal = b.min_main_gun_cal;
                if (r.max_main_gun_barrels < 0f) r.max_main_gun_barrels = b.max_main_gun_barrels;
                if (r.min_main_gun_barrels < 0f) r.min_main_gun_barrels = b.min_main_gun_barrels;
                if (r.max_main_gun_count < 0f) r.max_main_gun_count = b.max_main_gun_count;
                if (r.max_main_gun_cal_count < 0f) r.max_main_gun_cal_count = b.max_main_gun_cal_count;
                if (r.max_sec_gun_cal < 0f) r.max_sec_gun_cal = b.max_sec_gun_cal;
                if (r.min_sec_gun_cal < 0f) r.min_sec_gun_cal = b.min_sec_gun_cal;
                if (r.max_sec_gun_barrels < 0f) r.max_sec_gun_barrels = b.max_sec_gun_barrels;
                if (r.min_sec_gun_barrels < 0f) r.min_sec_gun_barrels = b.min_sec_gun_barrels;
                if (r.max_sec_gun_count < 0f) r.max_sec_gun_count = b.max_sec_gun_count;
                if (r.max_sec_gun_cal_count < 0f) r.max_sec_gun_cal_count = b.max_sec_gun_cal_count;
                if (r.max_torp_tube_count < 0f) r.max_torp_tube_count = b.max_torp_tube_count;
                if (r.min_torp_tube_count < 0f) r.min_torp_tube_count = b.min_torp_tube_count;
                if (r.max_torp_count < 0f) r.max_torp_count = b.max_torp_count;
                if (r.pref_torp_diameter < 0f) r.pref_torp_diameter = b.pref_torp_diameter;
                r.hasLimits = r.hasLimits || b.hasLimits;
                return r;
            }

            /// <summary>解析入口（P-b 实现）。</summary>
            public void Parse(PartData from)
            {
                hasLimits = false;
                if (from == null) return;
                try
                {
                    if (from.type == "hull" && from.shipType != null)
                        hasLimits = ParseShipType(from.shipType) || hasLimits;
                    hasLimits = ParsePartData(from) || hasLimits;
                }
                catch (Exception e) { HotfixLog.Warn("[SMS.1.SHIPGEN] Parse: " + e.Message); }
                if (hasLimits) totalLimits++;
            }

            /// <summary>
            /// 解析 `shipTypes.csv` 里的 `shipgen_limit(...)`。
            ///
            /// 语法（TAF 自定义，原版表里没有）：
            ///     shipgen_limit(max_main_gun_cal:14; min_main_gun_barrels:2; ...)
            /// 每个 token 形如 `stat:number`，用 `;` 分隔（由上层 CSV 参数解析完成）。
            /// </summary>
            private bool ParseShipType(ShipType st)
            {
                try
                {
                    if (st == null) return false;
                    var px = st.paramx;
                    if (px == null || !px.ContainsKey("shipgen_limit")) return false;

                    return ParseTokens(px["shipgen_limit"], "shipTypes.csv", st.name);
                }
                catch (Exception e) { HotfixLog.Warn("[SMS.1.SHIPGEN] ParseShipType: " + e.Message); return false; }
            }

            /// <summary>解析 `parts.csv` 里的 `shipgen_limit(...)`（语法同上）。</summary>
            private bool ParsePartData(PartData pd)
            {
                try
                {
                    if (pd == null) return false;
                    var px = pd.paramx;
                    if (px == null || !px.ContainsKey("shipgen_limit")) return false;

                    return ParseTokens(px["shipgen_limit"], "parts.csv", pd.name);
                }
                catch (Exception e) { HotfixLog.Warn("[SMS.1.SHIPGEN] ParsePartData: " + e.Message); return false; }
            }

            /// <summary>
            /// 公共 token 解析器：`stat:number` → 对应字段。
            ///
            /// 与 Beta6 反编译版本的语义一致（它用"长度 + 首字母 + 全串比较"的 switch 优化，
            /// 这里直接按名字 switch，结果相同）。
            /// </summary>
            private bool ParseTokens(Il2CppSystem.Collections.Generic.List<string> tokens,
                                     string sourceFile, string idName)
            {
                bool any = false;
                if (tokens == null) return false;

                var e = tokens.GetEnumerator();
                while (e.MoveNext())
                {
                    string token = e.Current;
                    if (string.IsNullOrEmpty(token)) continue;

                    var kv = token.Split(':');
                    if (kv.Length != 2)
                    {
                        HotfixLog.Warn("[SMS.1.SHIPGEN] Invalid '" + sourceFile + "' 'shipgen_limit' param: '"
                                       + token + "' for ID '" + idName
                                       + "'. Must be formatted 'shipgen_limit(stat:number; ...)'.");
                        continue;
                    }

                    string stat = kv[0].Trim();
                    if (!ModUtils.TryParse(kv[1].Trim(), out float v))
                    {
                        HotfixLog.Warn("[SMS.1.SHIPGEN] Invalid '" + sourceFile + "' 'shipgen_limit' param: '"
                                       + token + "' for ID '" + idName + "'. Must be valid number.");
                        continue;
                    }

                    switch (stat)
                    {
                        case "max_main_gun_cal": max_main_gun_cal = v; break;
                        case "min_main_gun_cal": min_main_gun_cal = v; break;
                        case "max_main_gun_barrels": max_main_gun_barrels = v; break;
                        case "min_main_gun_barrels": min_main_gun_barrels = v; break;
                        case "max_main_gun_count": max_main_gun_count = v; break;
                        case "max_main_gun_cal_count": max_main_gun_cal_count = v; break;

                        case "max_sec_gun_cal": max_sec_gun_cal = v; break;
                        case "min_sec_gun_cal": min_sec_gun_cal = v; break;
                        case "max_sec_gun_barrels": max_sec_gun_barrels = v; break;
                        case "min_sec_gun_barrels": min_sec_gun_barrels = v; break;
                        case "max_sec_gun_count": max_sec_gun_count = v; break;
                        case "max_sec_gun_cal_count": max_sec_gun_cal_count = v; break;

                        case "max_torp_tube_count": max_torp_tube_count = v; break;
                        case "min_torp_tube_count": min_torp_tube_count = v; break;
                        case "max_torp_count": max_torp_count = v; break;

                        case "pref_torp_diameter":
                            if (v < -1f || v > 4f)
                            {
                                HotfixLog.Warn("[SMS.1.SHIPGEN] Invalid '" + sourceFile + "' 'shipgen_limit' param: '"
                                               + token + "' for ID '" + idName
                                               + "'. pref_torp_diameter must be between 0 and 4!");
                                continue;
                            }
                            pref_torp_diameter = v; break;

                        default:
                            HotfixLog.Warn("[SMS.1.SHIPGEN] Unknown 'shipgen_limit' stat '" + stat
                                           + "' for ID '" + idName + "'");
                            continue;
                    }

                    any = true;
                }

                return any;
            }

            public void Print()
            {
                var sb = new StringBuilder();
                sb.Append("[SMS.1.SHIPGEN] ").Append(key).Append(" hasLimits=").Append(hasLimits);
                if (max_main_gun_cal >= 0f) sb.Append(" max_main_gun_cal=").Append(max_main_gun_cal.ToString("0.##"));
                if (min_main_gun_cal >= 0f) sb.Append(" min_main_gun_cal=").Append(min_main_gun_cal.ToString("0.##"));
                if (max_main_gun_barrels >= 0f) sb.Append(" max_main_gun_barrels=").Append(max_main_gun_barrels.ToString("0.##"));
                if (min_main_gun_barrels >= 0f) sb.Append(" min_main_gun_barrels=").Append(min_main_gun_barrels.ToString("0.##"));
                if (max_main_gun_count >= 0f) sb.Append(" max_main_gun_count=").Append(max_main_gun_count.ToString("0.##"));
                if (max_main_gun_cal_count >= 0f) sb.Append(" max_main_gun_cal_count=").Append(max_main_gun_cal_count.ToString("0.##"));
                if (max_sec_gun_cal >= 0f) sb.Append(" max_sec_gun_cal=").Append(max_sec_gun_cal.ToString("0.##"));
                if (min_sec_gun_cal >= 0f) sb.Append(" min_sec_gun_cal=").Append(min_sec_gun_cal.ToString("0.##"));
                if (max_sec_gun_barrels >= 0f) sb.Append(" max_sec_gun_barrels=").Append(max_sec_gun_barrels.ToString("0.##"));
                if (min_sec_gun_barrels >= 0f) sb.Append(" min_sec_gun_barrels=").Append(min_sec_gun_barrels.ToString("0.##"));
                if (max_sec_gun_count >= 0f) sb.Append(" max_sec_gun_count=").Append(max_sec_gun_count.ToString("0.##"));
                if (max_sec_gun_cal_count >= 0f) sb.Append(" max_sec_gun_cal_count=").Append(max_sec_gun_cal_count.ToString("0.##"));
                if (max_torp_tube_count >= 0f) sb.Append(" max_torp_tube_count=").Append(max_torp_tube_count.ToString("0.##"));
                if (min_torp_tube_count >= 0f) sb.Append(" min_torp_tube_count=").Append(min_torp_tube_count.ToString("0.##"));
                if (max_torp_count >= 0f) sb.Append(" max_torp_count=").Append(max_torp_count.ToString("0.##"));
                if (pref_torp_diameter >= 0f) sb.Append(" pref_torp_diameter=").Append(pref_torp_diameter.ToString("0.##"));
                HotfixLog.Info(sb.ToString());
            }
        }

        /// <summary>按 PartData.name 索引的限制表（ParsePartLimitData 时填充）。</summary>
        public static System.Collections.Generic.Dictionary<string, PartLimits> PartLimitData
            = new System.Collections.Generic.Dictionary<string, PartLimits>();

        /// <summary>启动时遍历所有部件，建立限制表（P-c 批次完成数据源接线）。</summary>
        public static void ParsePartLimitData()
        {
            try
            {
                PartLimitData.Clear();
                PartLimits.totalLimits = 0;

                var parts = G.GameData.parts;
                if (parts == null) return;

                var e = parts.GetEnumerator();
                while (e.MoveNext())
                {
                    var pd = e.Current.Value;
                    if (pd == null) continue;
                    var lim = new PartLimits(pd);
                    string nm = pd.name;
                    if (!string.IsNullOrEmpty(nm)) PartLimitData[nm] = lim;
                }

                HotfixLog.Info("[SMS.1.SHIPGEN] parsed " + PartLimitData.Count + " part limits, "
                               + "withLimits=" + PartLimits.totalLimits);
            }
            catch (Exception e) { HotfixLog.Warn("[SMS.1.SHIPGEN] ParsePartLimitData: " + e.Message); }
        }
    }
}
