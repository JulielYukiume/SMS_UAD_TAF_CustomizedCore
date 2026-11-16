using System;
using System.Collections.Generic;
using Il2Cpp;
using TAFHotfixBatch1;
using UnityEngine;

namespace TweaksAndFixes
{
    /// <summary>
    /// [SMS.2.OCEAN] 空水模式的"远景水底" —— **原版对象** `FakePlaneToFixWiresGlitch`
    /// （材质 `WaterBottom`，位于 y=-100，尺寸 100000×100000）。
    ///
    /// 之前我们以为那层灰雾是自己加的平面（`NoneBackdrop`），实测确认它来自这个**原版对象**：
    ///   port133 之前空水模式用"关键字扫描"会把它误关，所以那时看不到；
    ///   改成白名单后它恢复显示 ⇒ 出现"灰色雾层"。
    ///
    /// 处理方式（`none_waterbottom_mode`）：
    ///   0 = 不动（原版灰）
    ///   1 = 改色（默认；`none_waterbottom_r/g/b`）
    ///   2 = 关闭该对象
    ///   3 = **下移 + 改色**（`none_waterbottom_height` 为位移量）
    ///   （早期版本 mode=3 只下移不改色，实测"加深后露出的浅蓝"仍然是原版色，故合并）
    /// </summary>
    internal static class NoneWaterBottom
    {
        private const string ObjectName = "FakePlaneToFixWiresGlitch";

        private static GameObject _go;
        private static Material _mat;
        private static Color _origColor;
        private static Vector3 _origPos;
        private static bool _captured;

        internal static void Apply(int oceanMode)
        {
            try
            {
                if (oceanMode != 2)
                {
                    Restore();
                    return;
                }

                if (_go == null) Find();

                int mode = (int)HotfixParams.NoneWaterBottomMode;
                if (_go == null || mode == 0) return;

                if (!_captured) Capture();

                switch (mode)
                {
                    case 2:
                        _go.SetActive(false);
                        HotfixLog.Info("[SMS.2.OCEAN] waterbottom: disabled");
                        break;

                    case 3:
                        _go.transform.position = new Vector3(_origPos.x,
                            _origPos.y + HotfixParams.NoneWaterBottomHeight, _origPos.z);
                        ApplyColor();
                        HotfixLog.Info("[SMS.2.OCEAN] waterbottom: moved to y=" + _go.transform.position.y
                                       + " + recolored");
                        break;

                    default: // 1 = recolor
                        ApplyColor();
                        break;
                }
            }
            catch (Exception e) { HotfixLog.Warn("[SMS.2.OCEAN] waterbottom: " + e.Message); }
        }


        private static void ApplyColor()
        {
            try
            {
                if (_mat == null) return;
                var c = new Color(HotfixParams.NoneWaterBottomR,
                                  HotfixParams.NoneWaterBottomG,
                                  HotfixParams.NoneWaterBottomB, 1f);
                _mat.color = c;
                try { _mat.SetColor("_Color", c); } catch { }
                HotfixLog.Info("[SMS.2.OCEAN] waterbottom: recolored to (" + c.r + "," + c.g + "," + c.b + ")");
            }
            catch { }
        }

        private static void Find()
        {
            try
            {
                foreach (var mr in UnityEngine.Object.FindObjectsOfType<MeshRenderer>())
                {
                    if (mr == null) continue;
                    string n = "";
                    try { n = mr.gameObject.name; } catch { }
                    if (n != ObjectName) continue;

                    _go = mr.gameObject;
                    _mat = mr.sharedMaterial;
                    HotfixLog.Info("[SMS.2.OCEAN] waterbottom: found '" + n + "' pos=" + _go.transform.position);
                    return;
                }
            }
            catch { }
        }

        private static void Capture()
        {
            _captured = true;
            try { _origPos = _go.transform.position; } catch { }
            try
            {
                if (_mat != null)
                {
                    _origColor = _mat.color;
                    HotfixLog.Info("[SMS.2.OCEAN] waterbottom: original color=("
                                   + _origColor.r + "," + _origColor.g + "," + _origColor.b + ","
                                   + _origColor.a + ")  // 原版默认色（参考）");
                }
            }
            catch { }
        }

        private static void Restore()
        {
            if (!_captured || _go == null) return;
            try
            {
                if (_go != null)
                {
                    if (!_go.activeSelf) _go.SetActive(true);
                    _go.transform.position = _origPos;
                }
                if (_mat != null && HotfixParams.NoneWaterBottomMode >= 1)
                {
                    _mat.color = _origColor;
                    try { _mat.SetColor("_Color", _origColor); } catch { }
                }
            }
            catch { }
            _captured = false;
        }
    }
}
