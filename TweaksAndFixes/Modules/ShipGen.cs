using System;
using System.Text;
using Il2Cpp;
using MelonLoader;
using TAFHotfixBatch1;
using UnityEngine;

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


        /// <summary>
        /// [SMS.1.SHIPGEN] 生成后校验：检查 AI 刚生成的舰船是否违反 shipgen_limit。
        ///
        /// 说明：本方法**只校验并记录**，不修改舰船。原因：
        ///   · Beta6 的做法是**整体替换** `Ship._GenerateRandomShip_d__573.MoveNext`
        ///     （762 行，等于重写 AI 随机选件流程），风险极高；
        ///   · 我们改为"生成后审计"，先验证 `shipgen_limit` 数据与判定是否正确，
        ///     确认无误后再决定是否做"删除/重试"级别的强制。
        ///
        /// 开关：`shipgen_limits`（0=关闭，1=只审计，2=审计并尝试删除违规部件——未实现时等同 1）
        /// </summary>
        public static int ValidateGenerated(Ship ship)
        {
            if (ship == null || ship.hull == null || ship.hull.data == null) return 0;
            if (PartLimitData.Count == 0) return 0;

            string hkey = ship.hull.data.name;
            if (hkey == null || !PartLimitData.ContainsKey(hkey)) return 0;

            var lim = PartLimitData[hkey];
            if (!lim.hasLimits) return 0;

            int violations = 0;

            try
            {
                int mainCount = 0, secCount = 0, torpCount = 0, funnelCount = 0;
                int mainBarrels = 0;

                if (ship.parts != null)
                {
                    var e = ship.parts.GetEnumerator();
                    while (e.MoveNext())
                    {
                        var part = e.Current;
                        if (part == null || part.data == null) continue;
                        var d = part.data;

                        if (d.isGun)
                        {
                            float cal = d.GetCaliberInch(ship);
                            if (ship.IsMainCal(d))
                            {
                                mainCount++;
                                mainBarrels += d.barrels;

                                if (lim.max_main_gun_cal > 0f && cal > lim.max_main_gun_cal) violations++;
                                if (lim.min_main_gun_cal > 0f && cal < lim.min_main_gun_cal) violations++;
                            }
                            else if (!Ship.IsCasemateGun(d))
                            {
                                secCount++;
                                if (lim.max_sec_gun_cal > 0f && cal > lim.max_sec_gun_cal) violations++;
                                if (lim.min_sec_gun_cal > 0f && cal < lim.min_sec_gun_cal) violations++;
                            }
                        }
                        else if (d.isTorpedo) torpCount++;
                        else if (d.isFunnel) funnelCount++;
                    }
                }

                if (lim.max_main_gun_count > 0f && mainCount > lim.max_main_gun_count) violations++;
                if (lim.max_sec_gun_count > 0f && secCount > lim.max_sec_gun_count) violations++;
                if (lim.max_torp_count > 0f && torpCount > lim.max_torp_count) violations++;
                if (lim.max_torp_count == 0f && torpCount > 0) violations++;

                if (violations > 0)
                {
                    HotfixLog.Info("[SMS.1.SHIPGEN] generated ship violates limits: ship='"
                        + SafeName(ship) + "' hull='" + hkey + "'"
                        + " main=" + mainCount + "/" + lim.max_main_gun_count
                        + " sec=" + secCount + "/" + lim.max_sec_gun_count
                        + " torp=" + torpCount + "/" + lim.max_torp_count
                        + " funnel=" + funnelCount
                        + " violations=" + violations);
                }
            }
            catch (Exception e) { HotfixLog.Warn("[SMS.1.SHIPGEN] ValidateGenerated: " + e.Message); }

            return violations;
        }

        private static string SafeName(Ship s)
        {
            try { return s.vesselName; } catch { }
            try { return s.name; } catch { }
            return "?";
        }


        // ======================================================================
        //  生成后夹取（移植 Beta6 Patch_ShipGenRandom.ClampShipStats）
        //  数据来源：shipTypes.csv / parts.csv 的 `shipgen_clamp(stat:number; ...)`
        //  支持 stat：speed_min / speed_max / beam_min / beam_max / draught_min / draught_max
        // ======================================================================

        internal static float LastSpeed = -1f;
        internal static float LastBeam = -1f;
        internal static float LastDraught = -1f;

        private static void ParseClampTokens(Il2CppSystem.Collections.Generic.List<string> tokens,
                                             string sourceFile, string idName,
                                             float stMin, float stMax,
                                             float bMin, float bMax,
                                             float dMin, float dMax,
                                             ref float sMin, ref float sMax,
                                             ref float beamMin, ref float beamMax,
                                             ref float draughtMin, ref float draughtMax,
                                             ref bool any)
        {
            if (tokens == null) return;
            var e = tokens.GetEnumerator();
            while (e.MoveNext())
            {
                string token = e.Current;
                if (string.IsNullOrEmpty(token)) continue;

                var kv = token.Split(':');
                if (kv.Length != 2)
                {
                    HotfixLog.Warn("[SMS.1.SHIPGEN] Invalid '" + sourceFile + "' 'shipgen_clamp' param: '"
                                   + token + "' for ID '" + idName + "'. Must be 'shipgen_clamp(stat:number; ...)'.");
                    continue;
                }

                if (!ModUtils.TryParse(kv[1].Trim(), out float v))
                {
                    HotfixLog.Warn("[SMS.1.SHIPGEN] Invalid '" + sourceFile + "' 'shipgen_clamp' param: '"
                                   + token + "' for ID '" + idName + "'. Must be valid number.");
                    continue;
                }

                switch (kv[0].Trim())
                {
                    case "speed_min": sMin = Mathf.Clamp(v, stMin, stMax); break;
                    case "speed_max": sMax = Mathf.Clamp(v, stMin, stMax); break;
                    case "beam_min": beamMin = Mathf.Clamp(v, bMin, bMax); break;
                    case "beam_max": beamMax = Mathf.Clamp(v, bMin, bMax); break;
                    case "draught_min": draughtMin = Mathf.Clamp(v, dMin, dMax); break;
                    case "draught_max": draughtMax = Mathf.Clamp(v, dMin, dMax); break;
                    default:
                        HotfixLog.Warn("[SMS.1.SHIPGEN] Invalid '" + sourceFile + "' 'shipgen_clamp' param: '"
                                       + token + "' for ID '" + idName + "'. Unsupported stat.");
                        continue;
                }

                any = true;
            }
        }

        /// <summary>
        /// 把 AI 生成出来的速度/船宽/吃水夹回 `shipgen_clamp` 区间。
        ///
        /// `force=true` 时无条件重新夹取（用于"生成结束"这种一次性调用）；
        /// `force=false` 时只在数值与上次记录**不同**（即 AI 改过）时才夹取 —— 与 Beta6 语义一致。
        /// </summary>
        public static void ClampShipStats(Ship ship, bool force = false)
        {
            try
            {
                if (ship == null || ship.hull == null || ship.hull.data == null) return;

                var data = ship.hull.data;
                var st = ship.shipType;
                if (st == null) return;

                float stMin = st.speedMin, stMax = st.speedMax;
                float bMin = data.beamMin, bMax = data.beamMax;
                float dMin = data.draughtMin, dMax = data.draughtMax;

                float sMin = stMin, sMax = stMax;
                float beamMin = bMin, beamMax = bMax;
                float draughtMin = dMin, draughtMax = dMax;
                bool any = false;

                // ① 舰种级 shipgen_clamp
                try
                {
                    if (st.paramx != null && st.paramx.ContainsKey("shipgen_clamp"))
                        ParseClampTokens(st.paramx["shipgen_clamp"], "shipTypes.csv", st.name,
                                         stMin, stMax, bMin, bMax, dMin, dMax,
                                         ref sMin, ref sMax, ref beamMin, ref beamMax,
                                         ref draughtMin, ref draughtMax, ref any);
                }
                catch { }

                // ② 船体级 shipgen_clamp（优先级更高：后解析覆盖前者）
                try
                {
                    if (data.paramx != null && data.paramx.ContainsKey("shipgen_clamp"))
                        ParseClampTokens(data.paramx["shipgen_clamp"], "parts.csv", data.name,
                                         stMin, stMax, bMin, bMax, dMin, dMax,
                                         ref sMin, ref sMax, ref beamMin, ref beamMax,
                                         ref draughtMin, ref draughtMax, ref any);
                }
                catch { }

                if (!any) return;

                // 保证 min <= max
                if (sMin > sMax) { var tmp = sMin; sMin = sMax; sMax = tmp; }
                if (beamMin > beamMax) { var tmp = beamMin; beamMin = beamMax; beamMax = tmp; }
                if (draughtMin > draughtMax) { var tmp = draughtMin; draughtMin = draughtMax; draughtMax = tmp; }

                // ---- 速度（内部单位 m/s，配置里是节）----
                try
                {
                    float cur = ship.speedMax;                      // m/s
                    float clamped = Mathf.Clamp(cur, sMin * 0.5144444f, sMax * 0.5144444f);
                    if (force || Mathf.Abs(clamped - cur) > 0.0001f)
                    {
                        clamped = ModUtils.roundToInc(clamped, 0.1f);
                        ship.SetSpeedMax(clamped);
                        LastSpeed = ship.speedMax;
                    }
                }
                catch (Exception e) { HotfixLog.Warn("[SMS.1.SHIPGEN] clamp speed: " + e.Message); }

                // ---- 船宽（百分比偏移，按比例重映射）----
                try
                {
                    if (force || Mathf.Abs(LastBeam - ship.beam) > 0.0001f)
                    {
                        float v = Reratio(ship.beam, bMin, bMax, beamMin, beamMax);
                        ship.SetBeam(ModUtils.roundToInc(v, 0.1f), false);
                        LastBeam = ship.beam;
                    }
                }
                catch (Exception e) { HotfixLog.Warn("[SMS.1.SHIPGEN] clamp beam: " + e.Message); }

                // ---- 吃水 ----
                try
                {
                    if (force || Mathf.Abs(LastDraught - ship.draught) > 0.0001f)
                    {
                        float v = Reratio(ship.draught, dMin, dMax, draughtMin, draughtMax);
                        ship.SetDraught(ModUtils.roundToInc(v, 0.1f), false);
                        LastDraught = ship.draught;
                    }
                }
                catch (Exception e) { HotfixLog.Warn("[SMS.1.SHIPGEN] clamp draught: " + e.Message); }

                if (HotfixParams.ShipGenLimits >= 2)
                {
                    HotfixLog.Info("[SMS.1.SHIPGEN] clamp applied: '" + SafeName(ship) + "'"
                        + " speed=" + ship.speedMax.ToString("0.##")
                        + " beam=" + ship.beam.ToString("0.##")
                        + " draught=" + ship.draught.ToString("0.##")
                        + "  (ranges speed " + sMin.ToString("0.#") + "-" + sMax.ToString("0.#")
                        + ", beam " + beamMin.ToString("0.#") + "-" + beamMax.ToString("0.#")
                        + ", draught " + draughtMin.ToString("0.#") + "-" + draughtMax.ToString("0.#") + ")");
                }
            }
            catch (Exception e) { HotfixLog.Warn("[SMS.1.SHIPGEN] ClampShipStats: " + e.Message); }
        }

        /// <summary>线性重映射（Beta6 的 Reratio 同款）。</summary>
        public static float Reratio(float v, float a1, float a2, float b1, float b2)
        {
            if (b1 == b2) return b1;
            if (a1 == a2) return a1;
            return b1 + (b2 - b1) * ((v - a1) / (a2 - a1));
        }


        // ======================================================================
        //  生成后优化（移植 Beta6 Patch_ShipGenRandom.OptimizeComponents）
        //  ① engine/boilers/fuel 组合择优（取最轻组合）—— 直接关系"设计能否通过"
        //  ② 鱼雷推进器 fast → normal（省重量）
        //  ③ 炮弹 light → normal
        //  ④ 鱼雷尺寸偏好（读 shipgen_limit 的 pref_torp_diameter）
        // ======================================================================

        internal static ComponentData DesiredTorpSize;

        private static bool CanModCompType(Ship ship, string key)
        {
            try
            {
                var gd = G.GameData;
                if (gd.compTypes == null || !gd.compTypes.ContainsKey(key)) return false;
                var ct = gd.compTypes[key];
                return ship.components != null && ship.components.ContainsKey(ct);
            }
            catch { return false; }
        }

        private static bool CanModComp(Ship ship, string key)
        {
            try
            {
                var gd = G.GameData;
                if (gd.components == null || !gd.components.ContainsKey(key)) return false;
                var comp = gd.components[key];
                return ship.components != null && ship.components.ContainsKey(comp.typex);
            }
            catch { return false; }
        }

        /// <summary>生成后部件优化（含鱼雷尺寸偏好）。</summary>
        public static void OptimizeComponents(Ship ship)
        {
            if (ship == null) return;

            try { OptimizePowerplant(ship); }
            catch (Exception e) { HotfixLog.Warn("[SMS.1.SHIPGEN] optimize powerplant: " + e.Message); }

            try { OptimizeSimple(ship, "torpedo_prop", "torpedo_prop_fast", "torpedo_prop_normal"); }
            catch (Exception e) { HotfixLog.Warn("[SMS.1.SHIPGEN] optimize torpedo_prop: " + e.Message); }

            try { OptimizeSimple(ship, "shell", "shell_light", "shell_normal"); }
            catch (Exception e) { HotfixLog.Warn("[SMS.1.SHIPGEN] optimize shell: " + e.Message); }

            try { OptimizeTorpSize(ship); }
            catch (Exception e) { HotfixLog.Warn("[SMS.1.SHIPGEN] optimize torp size: " + e.Message); }
        }

        /// <summary>① 动力组合：在所有"可用"的 engine/boilers/fuel 三元组里挑选**总重最轻**的一组。</summary>
        private static void OptimizePowerplant(Ship ship)
        {
            if (!CanModCompType(ship, "boilers") || !CanModCompType(ship, "engine") || !CanModCompType(ship, "fuel"))
                return;

            var gd = G.GameData;
            var ctBoilers = gd.compTypes["boilers"];
            var ctEngine = gd.compTypes["engine"];
            var ctFuel = gd.compTypes["fuel"];

            float bestWeight = ship.Weight();
            var bestEngine = ship.components[ctEngine];
            var bestBoilers = ship.components[ctBoilers];
            var bestFuel = ship.components[ctFuel];

            var techs = gd.technologies;
            if (techs == null) return;

            var eEngine = techs.GetEnumerator();
            while (eEngine.MoveNext())
            {
                var engineComp = eEngine.Current.Value != null ? eEngine.Current.Value.componentx : null;
                if (engineComp == null || engineComp.type != "engine") continue;
                if (!ship.IsComponentAvailable(engineComp)) continue;

                var eBoilers = techs.GetEnumerator();
                while (eBoilers.MoveNext())
                {
                    var boilersComp = eBoilers.Current.Value != null ? eBoilers.Current.Value.componentx : null;
                    if (boilersComp == null || boilersComp.type != "boilers") continue;
                    if (!ship.IsComponentAvailable(boilersComp)) continue;

                    var eFuel = techs.GetEnumerator();
                    while (eFuel.MoveNext())
                    {
                        var fuelComp = eFuel.Current.Value != null ? eFuel.Current.Value.componentx : null;
                        if (fuelComp == null || fuelComp.type != "fuel") continue;
                        if (!ship.IsComponentAvailable(fuelComp)) continue;

                        try
                        {
                            ship.InstallComponent(engineComp, true);
                            ship.InstallComponent(boilersComp, true);
                            ship.InstallComponent(fuelComp, true);

                            float w = ship.Weight();
                            if (w < bestWeight)
                            {
                                bestWeight = w;
                                bestEngine = engineComp;
                                bestBoilers = boilersComp;
                                bestFuel = fuelComp;
                            }
                        }
                        catch { }
                    }
                }
            }

            try
            {
                ship.InstallComponent(bestEngine, true);
                ship.InstallComponent(bestBoilers, true);
                ship.InstallComponent(bestFuel, true);
            }
            catch { }
        }

        /// <summary>②③ 简单替换：若当前是 from 且 to 可用，则换成 to。</summary>
        private static void OptimizeSimple(Ship ship, string typeKey, string fromKey, string toKey)
        {
            if (!CanModCompType(ship, typeKey)) return;
            if (!CanModComp(ship, fromKey) || !CanModComp(ship, toKey)) return;

            var gd = G.GameData;
            var ct = gd.compTypes[typeKey];
            if (!ship.components.ContainsKey(ct)) return;

            if (ship.components[ct] == gd.components[fromKey])
                ship.InstallComponent(gd.components[toKey], true);
        }

        /// <summary>④ 鱼雷尺寸偏好：按 pref_torp_diameter（-1 未设 / 0..4 分档）挑选。</summary>
        private static void OptimizeTorpSize(Ship ship)
        {
            if (ship.hull == null || ship.hull.data == null) return;
            string hkey = ship.hull.data.name;
            if (hkey == null || !PartLimitData.ContainsKey(hkey)) return;

            var lim = PartLimitData[hkey];

            if (!CanModCompType(ship, "torpedo_size")) return;
            var ct = G.GameData.compTypes["torpedo_size"];

            // 已有偏好且当前不是它就换上
            if (DesiredTorpSize != null)
            {
                try
                {
                    if (ship.components[ct] != DesiredTorpSize)
                    {
                        if (HotfixParams.ShipGenLimits >= 2)
                            HotfixLog.Info("[SMS.1.SHIPGEN] torp size -> " + DesiredTorpSize.name);
                        ship.InstallComponent(DesiredTorpSize, true);
                    }
                }
                catch { }
                return;
            }

            if (lim.pref_torp_diameter == -1f) return;

            // 收集所有可用的 torpedo_size
            var list = new System.Collections.Generic.List<ComponentData>();
            var gd = G.GameData;
            var e = gd.components.GetEnumerator();
            while (e.MoveNext())
            {
                var comp = e.Current.Value;
                if (comp == null) continue;
                if (comp.type != "torpedo_size") continue;
                if (!ship.IsComponentAvailable(comp)) continue;
                list.Add(comp);
            }

            int count = list.Count;
            if (count == 0) return;

            int n33 = (int)Mathf.Round(list.Count * 0.33f);
            int n66 = (int)Mathf.Round(list.Count * 0.66f);

            if (count == 1) DesiredTorpSize = list[0];
            else
            {
                float pref = lim.pref_torp_diameter;
                if (pref == 0f) DesiredTorpSize = list[0];
                else if (pref == 1f)
                {
                    if (count == 2) DesiredTorpSize = list[0];
                    if (count > 2) DesiredTorpSize = list[UnityEngine.Random.Range(0, Mathf.Max(1, n33))];
                }
                else if (pref == 2f)
                {
                    if (count > 2) DesiredTorpSize = list[UnityEngine.Random.Range(Mathf.Min(n33, count - 1), Mathf.Max(n33 + 1, n66))];
                }
                else if (pref == 3f)
                {
                    if (count == 2) DesiredTorpSize = list[1];
                    if (count > 2) DesiredTorpSize = list[UnityEngine.Random.Range(Mathf.Min(n66, count - 1), count)];
                }
                else if (pref == 4f) DesiredTorpSize = list[list.Count - 1];

                if (DesiredTorpSize == null) DesiredTorpSize = list[list.Count - 1];
            }

            if (DesiredTorpSize != null)
            {
                if (HotfixParams.ShipGenLimits >= 2)
                    HotfixLog.Info("[SMS.1.SHIPGEN] torp size preference='" + lim.pref_torp_diameter
                                   + "' chose " + DesiredTorpSize.name);
                ship.InstallComponent(DesiredTorpSize, true);
            }
        }

        /// <summary>按 PartData.name 索引的限制表（ParsePartLimitData 时填充）。</summary>
        public static System.Collections.Generic.Dictionary<string, PartLimits> PartLimitData
            = new System.Collections.Generic.Dictionary<string, PartLimits>();


        // ======================================================================
        //  判定层（P-c）：由限制表判断"这个部件能不能再放"
        //  语义与 Beta6 反编译一致；所有异常一律返回 true（宽松），避免误拦。
        // ======================================================================

        /// <summary>检查 part 的父级链里是否出现 target，或是否有父级满足 pred。</summary>
        internal static bool HasParent(Part part, Part target, Func<Part, bool> pred)
        {
            try
            {
                int guard = 100;
                var cur = part;
                while (cur != null && guard-- > 0)
                {
                    var m = cur.mount;
                    if (m == null) break;
                    var pp = m.parentPart;
                    if (pp == null) break;
                    if (ReferenceEquals(pp, target)) return true;
                    if (pred != null && pp.data != null && pred(pp)) return true;
                    cur = pp;
                }
            }
            catch { }
            return false;
        }


        /// <summary>
        /// [SMS.1.SHIPGEN] 判断"这门炮算主炮还是副炮"——**优先用 shipgen_limit 的口径区间**，
        /// 而不是 `ship.IsMainCal(data)`。
        ///
        /// 为什么必须这样（2026-10-26 实测 + 用户 Beta 回测）：
        ///   · 旧 TAF 式（`ai_min_caliber_main(13)` 等）由**游戏本体**处理，一直有效；
        ///   · 新式 `shipgen_limit(...)`（3.21.0 起）**从未真正生效**：SMS 新船体永远"放不下主炮"；
        ///   · Beta6 同病 ⇒ 不是我们移植引入。
        /// 推断根因：AI 选炮时船还没有"主炮"，`ship.IsMainCal(data)` 对候选返回 false，
        ///   于是主炮候选走了**副炮分支**；而副炮限制（如 max_sec_gun_cal:5）会把 10~12 英寸炮
        ///   全部拒掉 ⇒ 表现为"永远放不下主炮"。
        /// 所以这里改为：先用 limit 里声明的口径区间归类，区间无法判断时才回落到 IsMainCal。
        /// </summary>
        private static bool IsMainByLimits(PartData d, Ship ship, PartLimits lim)
        {
            try
            {
                float cal = d.GetCaliberInch(ship);

                bool hasMainRange = lim.min_main_gun_cal > 0f || lim.max_main_gun_cal > 0f;
                if (hasMainRange)
                {
                    bool inMain = (lim.min_main_gun_cal <= 0f || cal >= lim.min_main_gun_cal)
                               && (lim.max_main_gun_cal <= 0f || cal <= lim.max_main_gun_cal);
                    if (inMain) return true;

                    bool hasSecRange = lim.min_sec_gun_cal > 0f || lim.max_sec_gun_cal > 0f;
                    if (hasSecRange)
                    {
                        bool inSec = (lim.min_sec_gun_cal <= 0f || cal >= lim.min_sec_gun_cal)
                                  && (lim.max_sec_gun_cal <= 0f || cal <= lim.max_sec_gun_cal);
                        if (inSec) return false;
                    }
                }

                return ship.IsMainCal(d);
            }
            catch { return true; }
        }

        /// <summary>能否再加一门炮（主炮/副炮分别按各自限制）。</summary>
        public static bool CanAddGun(Part part, PartData data, Ship ship, ref string denyReason)
        {
            try
            {
                if (data == null) return true;
                if (!PartLimitData.ContainsKey(data.name))
                {
                    HotfixLog.Warn("[SMS.1.SHIPGEN] no limit data for " + data.name);
                    return true;
                }
                if (part == null || ship == null) return true;

                var mount = part.mount;
                if (mount == null || mount.parentPart == null) return true;
                var parentData = mount.parentPart.data;
                if (parentData != null && parentData.isHull) return true;

                var parentPart = mount.parentPart;
                PartLimits lim;
                var pkey = parentPart.data != null ? parentPart.data.name : null;
                if (pkey == null || !PartLimitData.ContainsKey(pkey)) return true;
                lim = PartLimitData[pkey].BuildRestrictionsFor(part);
                if (!lim.hasLimits) return true;

                float cal = data.GetCaliberInch(ship);

                if (IsMainByLimits(data, ship, lim))
                {
                    if ((lim.max_main_gun_cal > 0f && cal > lim.max_main_gun_cal)
                        || (lim.min_main_gun_cal > 0f && cal < lim.min_main_gun_cal))
                    {
                        denyReason = "shipgen_limit: main gun cal range";
                        return false;
                    }

                    if (lim.max_main_gun_count > 0f && ship.mainGuns != null)
                    {
                        int n = 0;
                        var e = ship.mainGuns.GetEnumerator();
                        while (e.MoveNext())
                        {
                            var cur = e.Current;
                            if (cur == null) continue;
                            if (cur.mount == null || cur.mount.parentPart == null) continue;
                            if (cur.data != null && cur.data.isBarbette) continue;
                            if (HasParent(cur, parentPart, c => c.data != null && c.data.isBarbette)) continue;
                            n++;
                        }
                        if (n > lim.max_main_gun_count)
                        {
                            denyReason = "shipgen_limit: main gun count range";
                            return false;
                        }
                    }
                }
                else
                {
                    if ((lim.max_sec_gun_cal > 0f && cal > lim.max_sec_gun_cal)
                        || (lim.min_sec_gun_cal > 0f && cal < lim.min_sec_gun_cal))
                    {
                        denyReason = "shipgen_limit: sec gun cal range";
                        return false;
                    }

                    bool casemate = Ship.IsCasemateGun(data);
                    if (!casemate && lim.max_sec_gun_count == 0f)
                    {
                        denyReason = "shipgen_limit: sec guns disabled";
                        return false;
                    }

                    if (!casemate && lim.max_sec_gun_count > 0f && ship.parts != null)
                    {
                        int n = 0;
                        var e = ship.parts.GetEnumerator();
                        while (e.MoveNext())
                        {
                            var cur = e.Current;
                            if (cur == null || cur.data == null) continue;
                            if (ship.IsMainCal(cur.data)) continue;
                            if (Ship.IsCasemateGun(cur.data)) continue;
                            if (cur.data != data) continue;
                            if (cur.data.isBarbette) continue;
                            if (HasParent(cur, parentPart, c => c.data != null && c.data.isBarbette)) continue;
                            n++;
                        }
                        if (n > lim.max_sec_gun_count)
                        {
                            denyReason = "shipgen_limit: sec gun count range";
                            return false;
                        }
                    }
                }

                return true;
            }
            catch (Exception e)
            {
                HotfixLog.Warn("[SMS.1.SHIPGEN] CanAddGun: " + e.Message);
                return true;
            }
        }

        /// <summary>该炮是否仍"可选"（船体级限制；用于 AI 选炮列表过滤）。</summary>
        public static bool IsGunAvailible(PartData data, Ship ship, ref bool __result)
        {
            __result = true;
            try
            {
                if (data == null || ship == null || ship.hull == null || ship.hull.data == null) return true;
                if (!PartLimitData.ContainsKey(data.name)) return true;

                var hkey = ship.hull.data.name;
                if (hkey == null || !PartLimitData.ContainsKey(hkey)) return true;
                var lim = PartLimitData[hkey];
                if (!lim.hasLimits) return true;

                float cal = data.GetCaliberInch(ship);

                if (IsMainByLimits(data, ship, lim))
                {
                    if ((lim.max_main_gun_cal > 0f && cal > lim.max_main_gun_cal)
                        || (lim.min_main_gun_cal > 0f && cal < lim.min_main_gun_cal))
                    { __result = false; return false; }

                    if ((lim.max_main_gun_barrels > 0f && data.barrels > lim.max_main_gun_barrels)
                        || (lim.min_main_gun_barrels > 0f && data.barrels < lim.min_main_gun_barrels))
                    { __result = false; return false; }

                    if (lim.max_main_gun_count > 0f && ship.mainGuns != null
                        && ship.mainGuns.Count > lim.max_main_gun_count)
                    { __result = false; return false; }

                    if (lim.max_main_gun_cal_count > 0f && ship.mainGuns != null)
                    {
                        if (!CheckCalBuckets(ship.mainGuns, data, ship, cal, lim.max_main_gun_cal_count, true))
                        { __result = false; return false; }
                    }
                }
                else
                {
                    if ((lim.max_sec_gun_cal > 0f && cal > lim.max_sec_gun_cal)
                        || (lim.min_sec_gun_cal > 0f && cal < lim.min_sec_gun_cal))
                    { __result = false; return false; }

                    bool casemate = Ship.IsCasemateGun(data);
                    if (!casemate && ((lim.max_sec_gun_barrels > 0f && data.barrels > lim.max_sec_gun_barrels)
                        || (lim.min_sec_gun_barrels > 0f && data.barrels < lim.min_sec_gun_barrels)))
                    { __result = false; return false; }

                    if (!casemate && lim.max_sec_gun_count == 0f) { __result = false; return false; }

                    if (lim.max_sec_gun_cal_count > 0f && ship.parts != null)
                    {
                        if (!CheckSecCalBuckets(ship, data, cal, lim.max_sec_gun_cal_count))
                        { __result = false; return false; }
                    }
                }

                return true;
            }
            catch (Exception e)
            {
                HotfixLog.Warn("[SMS.1.SHIPGEN] IsGunAvailible: " + e.Message);
                __result = true;
                return true;
            }
        }

        /// <summary>主炮"口径分档"检查：不同口径档位数量不得超过限制，且相邻口径差 >= taf_shipgen_min_cal_gap。</summary>
        private static bool CheckCalBuckets(Il2CppSystem.Collections.Generic.List<Part> guns, PartData data, Ship ship,
                                            float cal, float maxBuckets, bool main)
        {
            try
            {
                int gap = 3;
                try { gap = Config.Param("taf_shipgen_min_cal_gap", 3); } catch { }

                int myCal = ModUtils.toInt(data.GetCaliberInch(null));
                bool alreadyHas = false;
                var buckets = new System.Collections.Generic.Dictionary<int, string>();

                var e = guns.GetEnumerator();
                while (e.MoveNext())
                {
                    var cur = e.Current;
                    if (cur == null || cur.data == null) continue;
                    int c = ModUtils.toInt(cur.data.GetCaliberInch(ship));

                    if (c == myCal && cur.data == data) { alreadyHas = true; break; }
                    if (Mathf.Abs(c - myCal) < gap) return false;
                    if (!buckets.ContainsKey(c)) buckets[c] = cur.data.name;
                }

                if (!alreadyHas && buckets.Count > maxBuckets) return false;
                return true;
            }
            catch { return true; }
        }

        /// <summary>副炮"口径分档"检查。</summary>
        private static bool CheckSecCalBuckets(Ship ship, PartData data, float cal, float maxBuckets)
        {
            try
            {
                int gap = 3;
                try { gap = Config.Param("taf_shipgen_min_cal_gap", 3); } catch { }

                int myCal = ModUtils.toInt(data.GetCaliberInch(null));
                bool alreadyHas = false, hasCasemate = false;
                var buckets = new System.Collections.Generic.Dictionary<int, string>();

                var e = ship.parts.GetEnumerator();
                while (e.MoveNext())
                {
                    var cur = e.Current;
                    if (cur == null || cur.data == null) continue;
                    if (!cur.data.isGun) continue;
                    if (ship.IsMainCal(cur.data)) continue;

                    bool cas = Ship.IsCasemateGun(cur.data);
                    if (cas) hasCasemate = true;

                    int c = ModUtils.toInt(cur.data.GetCaliberInch(ship));
                    if (c == myCal && cur.data == data) { alreadyHas = true; break; }
                    if (Mathf.Abs(c - myCal) < gap) return false;
                    if (!buckets.ContainsKey(c)) buckets[c] = cur.data.name;
                }

                if (!hasCasemate && Ship.IsCasemateGun(data)) alreadyHas = true;
                if (!alreadyHas && buckets.Count > maxBuckets) return false;
                return true;
            }
            catch { return true; }
        }

        /// <summary>能否再加鱼雷。</summary>
        public static bool CanAddTorpedo(Part part, PartData data, Ship ship, ref string denyReason)
        {
            try
            {
                if (data == null) return true;
                if (!PartLimitData.ContainsKey(data.name)) return true;
                if (part == null || ship == null) return true;

                var mount = part.mount;
                if (mount == null || mount.parentPart == null) return true;
                var parentData = mount.parentPart.data;
                if (parentData != null && parentData.isHull) return true;

                var parentPart = mount.parentPart;
                var pkey = parentPart.data != null ? parentPart.data.name : null;
                if (pkey == null || !PartLimitData.ContainsKey(pkey)) return true;

                var lim = PartLimitData[pkey].BuildRestrictionsFor(part);
                if (!lim.hasLimits) return true;

                if (lim.max_torp_count == 0f)
                {
                    denyReason = "shipgen_limit: torp count range";
                    return false;
                }

                if (lim.max_torp_count > 0f && ship.torpedoesAll != null)
                {
                    int n = 0;
                    var e = ship.torpedoesAll.GetEnumerator();
                    while (e.MoveNext())
                    {
                        var cur = e.Current;
                        if (cur == null) continue;
                        if (cur.mount == null || cur.mount.parentPart == null) continue;
                        if (cur.data != null && cur.data.isBarbette) continue;
                        if (HasParent(cur, parentPart, c => c.data != null && c.data.isBarbette)) continue;
                        n++;
                    }
                    if (n > lim.max_torp_count)
                    {
                        denyReason = "shipgen_limit: torp count range";
                        return false;
                    }
                }

                return true;
            }
            catch (Exception e) { HotfixLog.Warn("[SMS.1.SHIPGEN] CanAddTorpedo: " + e.Message); return true; }
        }

        /// <summary>该鱼雷是否仍可选。</summary>
        public static bool IsTorpAvailible(PartData data, Ship ship, ref bool __result)
        {
            __result = true;
            try
            {
                if (data == null || ship == null || ship.hull == null || ship.hull.data == null) return true;
                if (!PartLimitData.ContainsKey(data.name)) return true;

                var hkey = ship.hull.data.name;
                if (hkey == null || !PartLimitData.ContainsKey(hkey)) return true;
                var lim = PartLimitData[hkey];
                if (!lim.hasLimits) return true;

                if (lim.max_torp_count == 0f) { __result = false; return false; }

                if (lim.max_torp_count > 0f && ship.torpedoesAll != null
                    && ship.torpedoesAll.Count > lim.max_torp_count)
                { __result = false; return false; }

                bool isSub = data.paramx != null && data.paramx.ContainsKey("sub_torpedo");
                if (!isSub)
                {
                    if ((lim.max_torp_tube_count > 0f && data.barrels > lim.max_torp_tube_count)
                        || (lim.min_torp_tube_count > 0f && data.barrels < lim.min_torp_tube_count))
                    { __result = false; return false; }
                }

                // 只能混用同一种鱼雷
                if (ship.torpedoesAll != null && ship.torpedoesAll.Count > 0 && !isSub)
                {
                    Part first = null;
                    var e = ship.torpedoesAll.GetEnumerator();
                    while (e.MoveNext())
                    {
                        var cur = e.Current;
                        if (cur == null || cur.data == null) continue;
                        if (cur.data.paramx != null && cur.data.paramx.ContainsKey("sub_torpedo")) continue;
                        first = cur; break;
                    }
                    if (first != null && first.data != data) { __result = false; return false; }
                }

                return true;
            }
            catch (Exception e) { HotfixLog.Warn("[SMS.1.SHIPGEN] IsTorpAvailible: " + e.Message); __result = true; return true; }
        }

        /// <summary>能否再加烟囱（按"烟囱容量"饱和判断）。</summary>
        public static bool CanAddFunnel(Part part, PartData data, Ship ship, ref string denyReason)
        {
            try
            {
                if (ship == null || data == null) return true;

                if (!ship.statsValid) ship.CStats();

                float target = 150f;
                try { target = Config.Param("taf_shipgen_target_funnel_cap", 150f); } catch { }

                float smoke = 0f;
                try
                {
                    var gd = G.GameData;
                    if (gd.stats != null && gd.stats.ContainsKey("smoke_exhaust"))
                    {
                        var statData = gd.stats["smoke_exhaust"];
                        if (statData != null && ship.stats != null && ship.stats.ContainsKey(statData))
                            smoke = ship.stats[statData].total;
                    }
                }
                catch { return true; }

                if (smoke < target) return true;

                int n = 0;
                float fcapAll = 0f, fcapThis = 0f;
                if (ship.parts == null) return true;

                var e = ship.parts.GetEnumerator();
                while (e.MoveNext())
                {
                    var cur = e.Current;
                    if (cur == null || cur.data == null || !cur.data.isFunnel) continue;
                    n++;

                    float fcap = 0f;
                    try
                    {
                        var gd = G.GameData;
                        if (gd.stats != null && gd.stats.ContainsKey("fcap"))
                        {
                            var key = gd.stats["fcap"];
                            if (cur.data.statsx != null && cur.data.statsx.ContainsKey(key))
                                fcap = cur.data.statsx[key];
                        }
                    }
                    catch { }

                    fcapAll += fcap;
                    if (cur.data == data) fcapThis = fcap;
                }

                if (fcapThis == 0f) return true;
                if (n <= 1) return true;

                if (smoke * (1f - fcapThis / fcapAll) < target) return true;

                denyReason = "amount";
                return false;
            }
            catch (Exception e) { HotfixLog.Warn("[SMS.1.SHIPGEN] CanAddFunnel: " + e.Message); return true; }
        }

        /// <summary>统一入口：按部件类型分派。</summary>
        public static bool CanAdd(Part part, ref string denyReason)
        {
            try
            {
                if (part == null) return true;
                var data = part.data;
                if (data == null) return true;

                var ship = part.ship;

                if (data.isGun) return CanAddGun(part, data, ship, ref denyReason);
                if (data.isTorpedo) return CanAddTorpedo(part, data, ship, ref denyReason);
                if (data.isFunnel) return CanAddFunnel(part, data, ship, ref denyReason);
                return true;
            }
            catch { return true; }
        }

        /// <summary>统一入口：该部件是否可用（船体级）。</summary>
        public static bool PartAvailible(Ship ship, PartData data, ref bool __result)
        {
            try
            {
                if (data == null) return true;
                if (data.isGun) return IsGunAvailible(data, ship, ref __result);
                if (data.isTorpedo) return IsTorpAvailible(data, ship, ref __result);
                return true;
            }
            catch { return true; }
        }

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
