using System;
using System.Reflection;
using System.Text;
using Il2Cpp;
using TAFHotfixBatch1;
using UnityEngine;

namespace TweaksAndFixes
{
    /// <summary>
    /// [SMS.1.DUMP] 舰船内存状态转储（等价于 Beta6 的 `Ui.PrintShipDataExhaustive`，但实现方式不同）。
    ///
    /// 为什么不直接移植 Beta6 的实现：
    ///   · 它 3,067 IL / 889 反编译行 / 179 个插值块，机械转换后仍有 50+ 编译错误（`ref` 表达式、
    ///     Il2Cpp 集合遍历等），继续手工修意义不大；
    ///   · 它输出的就是"Ship 的字段值列表"，用**反射**遍历 `Ship` 的公开属性/字段同样能达到目的，
    ///     而且自动跟随游戏版本变化（新增字段自动出现）。
    ///
    /// 用途：排查"内存状态 vs 落盘 JSON 不一致"（例如 tonnage、status、designId 这类）。
    /// 开关：`ship_dump`（默认 0，避免误触发刷屏）。
    /// </summary>
    internal static class ShipDataDump
    {
        /// <summary>把一艘船的关键内存字段 dump 到日志。</summary>
        public static void Dump(Ship s)
        {
            if (s == null) return;

            var sb = new StringBuilder();
            sb.Append("[SMS.1.DUMP] ship='").Append(SafeName(s)).Append("'");

            try { sb.Append(" type=").Append(s.shipType != null ? s.shipType.name : "?"); } catch { }
            try { sb.Append(" isDesign=").Append(s.isDesign); } catch { }
            try { sb.Append(" isRefitDesign=").Append(s.isRefitDesign); } catch { }
            try { sb.Append(" status=").Append((int)s.status); } catch { }
            try { sb.Append(" tonnage=").Append(s.tonnage.ToString("0.##")); } catch { }
            try { sb.Append(" beam=").Append(s.beam.ToString("0.##")); } catch { }
            try { sb.Append(" draught=").Append(s.draught.ToString("0.##")); } catch { }
            try { sb.Append(" hull=").Append(s.hull != null && s.hull.data != null ? s.hull.data.name : "?"); } catch { }

            try
            {
                sb.Append(" parts=").Append(s.parts != null ? s.parts.Count : -1);
                sb.Append(" mainGuns=").Append(s.mainGuns != null ? s.mainGuns.Count : -1);
                sb.Append(" torpedoes=").Append(s.torpedoesAll != null ? s.torpedoesAll.Count : -1);
            }
            catch { }

            HotfixLog.Info(sb.ToString());

            // ---- 反射遍历：属性 ----
            int n = 0;
            foreach (var p in typeof(Ship).GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                try
                {
                    if (p.GetIndexParameters().Length > 0) continue;
                    if (!p.CanRead) continue;

                    object v;
                    try { v = p.GetValue(s); }
                    catch (Exception e) { v = "<err " + e.GetType().Name + ">"; }

                    string txt = v == null ? "null" : v.ToString();
                    if (txt != null && txt.Length > 120) txt = txt.Substring(0, 120) + "…";

                    HotfixLog.Info("[SMS.1.DUMP]   prop " + p.Name + " = " + txt);

                    if (++n >= 120) break;
                }
                catch { }
            }

            // ---- 反射遍历：字段 ----
            int m = 0;
            foreach (var f in typeof(Ship).GetFields(BindingFlags.Public | BindingFlags.Instance))
            {
                try
                {
                    object v = f.GetValue(s);
                    string txt = v == null ? "null" : v.ToString();
                    if (txt != null && txt.Length > 120) txt = txt.Substring(0, 120) + "…";

                    HotfixLog.Info("[SMS.1.DUMP]   field " + f.Name + " = " + txt);
                    if (++m >= 120) break;
                }
                catch { }
            }

            HotfixLog.Info("[SMS.1.DUMP] done: props=" + n + " fields=" + m);
        }

        private static string SafeName(Ship s)
        {
            try { return s.vesselName; } catch { }
            try { return s.name; } catch { }
            return "?";
        }
    }
}
