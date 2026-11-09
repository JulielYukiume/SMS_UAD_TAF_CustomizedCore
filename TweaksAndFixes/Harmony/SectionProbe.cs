using System;
using System.Text;
using HarmonyLib;
using Il2Cpp;
using TAFHotfixBatch1;
using UnityEngine;

namespace TweaksAndFixes
{
    /// <summary>
    /// [SMS.2.SEC] 分段状态转储探针（**只读**，用于色差定位）。
    ///
    /// 目的：直接回答"有色差的段 与 正常的段，到底哪一项状态不同"。
    /// 对每个段（bow / middles / stern）打印：
    ///   · 名字、localPosition、localScale
    ///   · 每个 Renderer 的：material 名、shader 名、lightProbeUsage、reflectionProbeUsage、
    ///     lightmapIndex、shadowCastingMode、receiveShadows、是否 staticBatch
    ///   · Variation 组件数量与其 variations 列表（判断图案变体是否被初始化）
    ///
    /// 开关：`section_probe`（默认 0）
    /// 时机：`Ship.RefreshHull` 之后（Postfix），此时段已重建完成。
    /// </summary>
    [HarmonyPatch(typeof(Ship))]
    internal static class Patch_Ship_SectionProbe
    {
        private static int _expCount;
        internal static int _count;

        [HarmonyPostfix]
        [HarmonyPatch(nameof(Ship.RefreshHull))]
        internal static void Postfix_RefreshHull(Ship __instance)
        {
            try
            {
                if (__instance == null || __instance.hull == null) return;

                // ★ 实验开关（no_batch / mat_force / lod_off）**不再依赖 section_probe**，
                //   只有 Dump 受 section_probe 控制 —— 否则 section_probe=0 时实验根本不会执行。
                if (HotfixParams.SectionMatForce > 0 && _expCount++ < 200)
                    ApplyMatForce(__instance);

                if (HotfixParams.SectionNoBatch) ApplyNoBatch(__instance);
            if (HotfixParams.SectionReflectionOff || HotfixParams.SectionProbeAnchor)
                ApplyReflectionTest(__instance);

                if (HotfixParams.SectionLodOff > 0)
                    ApplyLodOff(__instance);

                if (!HotfixParams.SectionProbe) return;
                if (_count++ > 20) return;

                Dump(__instance);
            }
            catch (Exception e) { HotfixLog.Warn("[SMS.2.SEC] postfix: " + e.Message); }
        }


        /// <summary>
        /// [SMS.2.SEC] 材质实验（**会改材质**，仅在 section_mat_force>0 时执行）：
        ///   1 = 中段改用艏段的材质（验证"中段材质与艏艉不同"是否就是色差来源）
        ///   2 = 全船所有段统一使用 steel_4
        /// </summary>

        /// <summary>[SMS.2.SEC] 实验：关闭所有段上的 AutomaticLOD（强制全精度 mesh）。</summary>
        /// <summary>[SMS.2.SEC] H1 实验：把舯段的 isStatic 置 false（退出静态合批）。</summary>
        /// <summary>[SMS.2.SEC] 反射实验：关闭反射采样 / 统一 probeAnchor，并打印全局反射设置。</summary>
        private static void ApplyReflectionTest(Ship s)
        {
            try
            {
                var h = s.hull;
                var root = h != null && h.gameObject != null ? h.gameObject.transform : null;
                int off = 0, anc = 0;
                var all = new System.Collections.Generic.List<GameObject>();
                if (h.bow != null) all.Add(h.bow);
                if (h.middles != null) for (int i = 0; i < h.middles.Count; i++) if (h.middles[i] != null) all.Add(h.middles[i]);
                if (h.stern != null) all.Add(h.stern);

                for (int g = 0; g < all.Count; g++)
                {
                    var rs = all[g].GetComponentsInChildren<Renderer>(true);
                    if (rs == null) continue;
                    for (int k = 0; k < rs.Length; k++)
                    {
                        var r = rs[k];
                        if (r == null) continue;
                        if (HotfixParams.SectionReflectionOff)
                        {
                            r.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
                            off++;
                        }
                        if (HotfixParams.SectionProbeAnchor && root != null)
                        {
                            r.probeAnchor = root;
                            anc++;
                        }
                    }
                }

                HotfixLog.Info("[SMS.2.SEC] reflection test: probeOff=" + off + " anchors=" + anc
                               + " | RenderSettings.reflIntensity=" + RenderSettings.reflectionIntensity
                               + " ambientMode=" + RenderSettings.ambientMode
                               + " ambientIntensity=" + RenderSettings.ambientIntensity
                               + " defaultRefl=" + RenderSettings.defaultReflectionMode);
            }
            catch (Exception e) { HotfixLog.Warn("[SMS.2.SEC] reflection test: " + e.Message); }
        }

        private static void ApplyNoBatch(Ship s)
        {
            try
            {
                var h = s.hull;
                int n = 0;
                if (h.middles != null)
                {
                    for (int i = 0; i < h.middles.Count; i++)
                    {
                        var go = h.middles[i];
                        if (go == null) continue;
                        foreach (var t in go.GetComponentsInChildren<Transform>(true))
                        {
                            if (t == null) continue;
                            t.gameObject.isStatic = false;
                            n++;
                        }
                    }
                }
                HotfixLog.Info("[SMS.2.SEC] no-batch: cleared isStatic on " + n + " objects");
            }
            catch (Exception e) { HotfixLog.Warn("[SMS.2.SEC] no-batch: " + e.Message); }
        }

        private static void ApplyLodOff(Ship s)
        {
            try
            {
                var h = s.hull;
                int n = 0;
                System.Action<GameObject> off = (go) =>
                {
                    if (go == null) return;
                    try
                    {
                        var lods = go.GetComponentsInChildren<AutomaticLOD>();
                        if (lods == null) return;
                        for (int i = 0; i < lods.Length; i++)
                        {
                            if (lods[i] == null) continue;
                            lods[i].enabled = false;
                            n++;
                        }
                    }
                    catch { }
                };

                if (HotfixParams.SectionLodOff >= 2)
                {
                    // ★ 实验 mode 2：让 LOD 生效并强制停在 LOD0，同时隐藏 LOD1/LOD2 子物体，
                    //   消除"全精度 r[0] 与 LOD1/LOD2 同时渲染"的重叠（材质不同 ⇒ 色差/斑马纹）。
                    System.Action<GameObject> fix = (go) =>
                    {
                        if (go == null) return;
                        try
                        {
                            var lods = go.GetComponentsInChildren<AutomaticLOD>();
                            if (lods != null)
                            {
                                for (int i = 0; i < lods.Length; i++)
                                {
                                    var al = lods[i];
                                    if (al == null) continue;
                                    al.enabled = true;
                                    al.ComputeAllLODMeshes(false);
                                    al.SwitchToLOD(0, false);
                                    n++;
                                }
                            }

                            var tr = go.transform;
                            for (int i = 0; i < tr.childCount; i++)
                            {
                                var c = tr.GetChild(i);
                                if (c == null) continue;
                                if (c.name == "LOD1" || c.name == "LOD2")
                                {
                                    c.gameObject.SetActive(false);
                                    n++;
                                }
                            }
                        }
                        catch { }
                    };

                    fix(h.bow);
                    if (h.middles != null)
                        for (int i = 0; i < h.middles.Count; i++) fix(h.middles[i]);
                    fix(h.stern);

                    HotfixLog.Info("[SMS.2.SEC] lod fix(mode2): touched " + n);
                    return;
                }

                off(h.bow);
                if (h.middles != null)
                    for (int i = 0; i < h.middles.Count; i++) off(h.middles[i]);
                off(h.stern);

                HotfixLog.Info("[SMS.2.SEC] lod off: disabled " + n + " AutomaticLOD");
            }
            catch (Exception e) { HotfixLog.Warn("[SMS.2.SEC] lod off: " + e.Message); }
        }

        private static void ApplyMatForce(Ship s)
        {
            try
            {
                var h = s.hull;
                Material bowMat = null;
                try
                {
                    var rs = h.bow != null ? h.bow.GetComponentsInChildren<Renderer>() : null;
                    if (rs != null)
                        for (int i = 0; i < rs.Length; i++)
                            if (rs[i] != null && rs[i].sharedMaterial != null) { bowMat = rs[i].sharedMaterial; break; }
                }
                catch { }

                // mode 2：取自中段的 LOD1 渲染器（实测其材质为 steel_4）
                Material steel4 = null;
                try
                {
                    if (h.middles != null && h.middles.Count > 0 && h.middles[0] != null)
                    {
                        var rs = h.middles[0].GetComponentsInChildren<Renderer>();
                        if (rs != null)
                        {
                            for (int i = 0; i < rs.Length; i++)
                            {
                                if (rs[i] == null || rs[i].sharedMaterial == null) continue;
                                if (rs[i].sharedMaterial.name == "steel_4") { steel4 = rs[i].sharedMaterial; break; }
                            }
                        }
                    }
                }
                catch { }

                // mode=3：直接用**舰艏首个船体 Renderer 的材质**（用于验证"舯段材质分配错误"假设）
                Material bowBody = null;
                try
                {
                    var b = s.hull.bow;
                    if (b != null)
                    {
                        var brs = b.GetComponentsInChildren<Renderer>();
                        if (brs != null)
                        {
                            for (int i = 0; i < brs.Length; i++)
                            {
                                var r = brs[i];
                                if (r == null || r.sharedMaterial == null) continue;
                                string mn = r.sharedMaterial.name;
                                if (mn.StartsWith("DeckBorder")) continue;
                                if (r.name.StartsWith("LOD")) continue;
                                bowBody = r.sharedMaterial;
                                break;
                            }
                        }
                    }
                }
                catch { }

                // mode=4：把**整条船体层级下所有 Renderer** 换成同一块纯灰 Unlit/Color。
                //   目的：排除"材质/贴图/关键字"一切因素，看剩余差异是否只剩几何（段重叠/z-fighting）。
                if (HotfixParams.SectionMatForce == 4)
                {
                    Material flat = null;
                    try
                    {
                        var sh = Shader.Find("Unlit/Color");
                        if (sh == null) sh = Shader.Find("Sprites/Default");
                        flat = new Material(sh);
                        flat.color = new Color(0.5f, 0.5f, 0.5f, 1f);
                    }
                    catch { }

                    int ch = 0;
                    var names = new System.Collections.Generic.HashSet<string>();
                    try
                    {
                        var rs = s.hull.gameObject.GetComponentsInChildren<Renderer>(true);
                        if (rs != null)
                        {
                            for (int k = 0; k < rs.Length; k++)
                            {
                                var r = rs[k];
                                if (r == null) continue;
                                try { if (r.sharedMaterial != null) names.Add(r.sharedMaterial.name.Replace(" (Instance)", "")); } catch { }
                                if (flat == null) continue;
                                r.sharedMaterial = flat;
                                ch++;
                            }
                        }
                    }
                    catch { }

                    HotfixLog.Info("[SMS.2.SEC] mat force mode=4 (ALL renderers under hull) changed=" + ch
                                   + " mats=[" + string.Join(",", names) + "]");
                    return;
                }

                Material target = HotfixParams.SectionMatForce == 1 ? bowMat
                                : HotfixParams.SectionMatForce == 3 ? bowBody
                                : steel4;
                if (target == null)
                {
                    HotfixLog.Warn("[SMS.2.SEC] mat force: target material not found (mode=" + HotfixParams.SectionMatForce + ")");
                    return;
                }

                int changed = 0;
                try
                {
                    if (h.middles != null)
                    {
                        for (int i = 0; i < h.middles.Count; i++)
                        {
                            var go = h.middles[i];
                            if (go == null) continue;
                            var rs = go.GetComponentsInChildren<Renderer>();
                            if (rs == null) continue;
                            for (int k = 0; k < rs.Length; k++)
                            {
                                var r = rs[k];
                                if (r == null || r.sharedMaterial == null) continue;
                                // 只改船体本体（跳过 DeckBorder / 救生艇等）
                                string mn = r.sharedMaterial.name;
                                if (mn == "DeckBorder") continue;
                                if (mn.StartsWith("Cr_")) continue;
                                if (r.name.StartsWith("LOD")) continue;
                                r.sharedMaterial = target;
                                changed++;
                            }
                        }
                    }
                }
                catch { }

                HotfixLog.Info("[SMS.2.SEC] mat force mode=" + HotfixParams.SectionMatForce
                               + " target='" + target.name + "' changed=" + changed);
            }
            catch (Exception e) { HotfixLog.Warn("[SMS.2.SEC] mat force: " + e.Message); }
        }

        private static void Dump(Ship s)
        {
            var h = s.hull;
            HotfixLog.Info("[SMS.2.SEC] === ship='" + SafeName(s) + "' hull='" + (h.data != null ? h.data.name : "?")
                           + "' middles=" + (h.middles != null ? h.middles.Count : -1)
                           + " middlesBase=" + (h.middlesBase != null ? h.middlesBase.Count : -1) + " ===");

            PrintOne("bow", h.bow);

            // ★ 关键对照：把"基础中段模型"也打印出来。
            //   若 Clone 出来的 middle 的材质 与 middlesBase 不同，说明问题出在克隆/初始化阶段。
            try
            {
                if (h.middlesBase != null)
                {
                    for (int i = 0; i < h.middlesBase.Count; i++)
                        PrintOne("middleBase[" + i + "]", h.middlesBase[i]);
                }
            }
            catch (Exception e) { HotfixLog.Warn("[SMS.2.SEC] middlesBase: " + e.Message); }
            try
            {
                if (h.middles != null)
                {
                    for (int i = 0; i < h.middles.Count; i++)
                        PrintOne("middle[" + i + "]", h.middles[i]);
                }
            }
            catch (Exception e) { HotfixLog.Warn("[SMS.2.SEC] middles: " + e.Message); }
            PrintOne("stern", h.stern);
        }

        private static void PrintOne(string tag, GameObject go)
        {
            if (go == null) { HotfixLog.Info("[SMS.2.SEC]   " + tag + " = null"); return; }

            var sb = new StringBuilder();
            var t = go.transform;
            sb.Append("[SMS.2.SEC]   ").Append(tag)
              .Append(" '").Append(go.name).Append("'")
              .Append(" active=").Append(go.activeSelf)
              .Append(" localPos=").Append(t.localPosition.ToString("0.##"))
              .Append(" localScale=").Append(t.localScale.ToString("0.##"));

            try
            {
                int vars = 0;
                var vs = go.GetComponentsInChildren<Variation>();
                if (vs != null) vars = vs.Length;
                sb.Append(" variations=").Append(vars);

                try
                {
                    var lods = go.GetComponentsInChildren<AutomaticLOD>();
                    sb.Append(" autoLOD=").Append(lods != null ? lods.Length : 0);
                    if (lods != null)
                    {
                        for (int i = 0; i < lods.Length && i < 2; i++)
                        {
                            var al = lods[i];
                            if (al == null) continue;
                            sb.Append(" lod[").Append(i).Append("]en=").Append(al.enabled);
                        }
                    }
                }
                catch { }
            }
            catch { }

            HotfixLog.Info(sb.ToString());

            try
            {
                var renderers = go.GetComponentsInChildren<Renderer>();
                if (renderers == null) return;

                int dumpMax = Mathf.Clamp(HotfixParams.SectionProbeMax, 4, 64);
                for (int i = 0; i < renderers.Length && i < dumpMax; i++)
                {
                    var r = renderers[i];
                    if (r == null) continue;

                    string mat = "?", shader = "?";
                    try { if (r.sharedMaterial != null) { mat = r.sharedMaterial.name; if (r.sharedMaterial.shader != null) shader = r.sharedMaterial.shader.name; } } catch { }

                    HotfixLog.Info("[SMS.2.SEC]       r[" + i + "] '" + SafeName(r) + "'"
                                   + " mat='" + mat + "'"
                                   + " shader='" + shader + "'"
                                   + " lp=" + r.lightProbeUsage
                                   + " rp=" + r.reflectionProbeUsage
                                   + " lm=" + r.lightmapIndex
                                   + " lmScale=" + SafeLmScale(r)
                                   + " rlmi=" + SafeRlmi(r)
                                   + " static=" + SafeStatic(r)
                                   + " anchor=" + SafeAnchor(r)
                                   + " motion=" + SafeMotion(r)
                                   + " matInst=" + MatInst(r)
                                   + " cast=" + r.shadowCastingMode
                                   + " recv=" + r.receiveShadows
                                   + " batch=" + r.isPartOfStaticBatch
                                   + " matId=" + MatId(r)
                                   + " tex='" + MatTex(r) + "'"
                                   + " kw='" + MatKw(r) + "'"
                                   + " mesh[" + MeshInfo(r) + "]"
                                   + " uv[" + UvInfo(r) + "]"
                                   + " mpb=" + MatBlock(r));
                }
            }
            catch (Exception e) { HotfixLog.Warn("[SMS.2.SEC] renderers: " + e.Message); }
        }



        /// <summary>
        /// [SMS.2.SEC] MaterialPropertyBlock 摘要。
        /// "同一 sharedMaterial 实例却显示不同颜色"的唯一常见机制：per-renderer MPB 覆盖。
        /// </summary>
        private static string MatBlock(Renderer r)
        {
            try
            {
                var mpb = new MaterialPropertyBlock();
                r.GetPropertyBlock(mpb);
                if (mpb == null || mpb.isEmpty) return "none";

                var sb = new System.Text.StringBuilder("has");

                try
                {
                    {
                        var c = mpb.GetColor("_Color");
                        sb.Append(" _Color=(").Append(c.r.ToString("0.###")).Append(",")
                          .Append(c.g.ToString("0.###")).Append(",")
                          .Append(c.b.ToString("0.###")).Append(")");
                    }
                }
                catch { }

                try
                {
                    {
                        var v = mpb.GetVector("_MainTex_ST");
                        sb.Append(" _MainTex_ST=(").Append(v.x.ToString("0.##")).Append(",")
                          .Append(v.y.ToString("0.##")).Append(",")
                          .Append(v.z.ToString("0.##")).Append(",")
                          .Append(v.w.ToString("0.##")).Append(")");
                    }
                }
                catch { }

                try
                {
                    {
                        var c2 = mpb.GetColor("_EmissionColor");
                        sb.Append(" _EmissionColor=(").Append(c2.r.ToString("0.###")).Append(",")
                          .Append(c2.g.ToString("0.###")).Append(",")
                          .Append(c2.b.ToString("0.###")).Append(")");
                    }
                }
                catch { }

                return sb.ToString();
            }
            catch (Exception e) { return "err:" + e.GetType().Name; }
        }

        private static int MatId(Renderer r)
        {
            try { return r.sharedMaterial != null ? r.sharedMaterial.GetInstanceID() : 0; } catch { return -1; }
        }

        private static string MatTex(Renderer r)
        {
            try
            {
                var m = r.sharedMaterial;
                if (m == null) return "?";
                if (m.HasProperty("_MainTex") && m.mainTexture != null) return m.mainTexture.name;
                return "-";
            }
            catch { return "?"; }
        }

        private static string MatKw(Renderer r)
        {
            try
            {
                var m = r.sharedMaterial;
                if (m == null) return "?";
                var kw = m.shaderKeywords;
                if (kw == null || kw.Length == 0) return "-";
                var s = string.Join(",", kw);
                return s.Length > 60 ? s.Substring(0, 60) : s;
            }
            catch { return "?"; }
        }


        private static string SafeLmScale(Renderer r)
        {
            try { var v = r.lightmapScaleOffset; return "(" + v.x.ToString("0.##") + "," + v.y.ToString("0.##") + "," + v.z.ToString("0.##") + "," + v.w.ToString("0.##") + ")"; }
            catch { return "?"; }
        }

        private static string SafeRlmi(Renderer r)
        {
            try { return r.realtimeLightmapIndex.ToString(); } catch { return "?"; }
        }

        private static string SafeStatic(Renderer r)
        {
            try { return r.gameObject.isStatic ? "1" : "0"; } catch { return "?"; }
        }

        private static string SafeAnchor(Renderer r)
        {
            try { return r.probeAnchor != null ? r.probeAnchor.name : "-"; } catch { return "?"; }
        }

        private static string SafeMotion(Renderer r)
        {
            try { return r.motionVectorGenerationMode.ToString(); } catch { return "?"; }
        }

        private static string MatInst(Renderer r)
        {
            try { return r.sharedMaterial == r.material ? "shared" : "INST"; } catch { return "?"; }
        }

        /// <summary>网格通道摘要：vertexCount / uv / uv2 / colors / tangents / readable。</summary>
        private static string MeshInfo(Renderer r)
        {
            try
            {
                Mesh m = null;
                var mf = r.GetComponent<MeshFilter>();
                if (mf != null) m = mf.sharedMesh;
                if (m == null)
                {
                    var smr = r.GetComponent<SkinnedMeshRenderer>();
                    if (smr != null) m = smr.sharedMesh;
                }
                if (m == null) return "none";

                int uv = 0, uv2 = 0, col = 0, tan = 0;
                try { uv = m.uv != null ? m.uv.Length : 0; } catch { }
                try { uv2 = m.uv2 != null ? m.uv2.Length : 0; } catch { }
                try { col = m.colors != null ? m.colors.Length : 0; } catch { }
                try { tan = m.tangents != null ? m.tangents.Length : 0; } catch { }

                return "v=" + m.vertexCount + " uv=" + uv + " uv2=" + uv2
                       + " col=" + col + " tan=" + tan + " rd=" + (m.isReadable ? 1 : 0);
            }
            catch { return "err"; }
        }


        /// <summary>UV 范围/均值摘要（用于验证"不同段采样到贴图不同区域 ⇒ 色调差"）。</summary>
        private static string UvInfo(Renderer r)
        {
            try
            {
                Mesh m = null;
                var mf = r.GetComponent<MeshFilter>();
                if (mf != null) m = mf.sharedMesh;
                if (m == null) { var smr = r.GetComponent<SkinnedMeshRenderer>(); if (smr != null) m = smr.sharedMesh; }
                if (m == null) return "none";

                var uvs = m.uv;
                if (uvs == null || uvs.Length == 0) return "none";

                float minX = 9e9f, minY = 9e9f, maxX = -9e9f, maxY = -9e9f, sx = 0f, sy = 0f;
                int n = uvs.Length;
                for (int i = 0; i < n; i++)
                {
                    var uv = uvs[i];
                    if (uv.x < minX) minX = uv.x; if (uv.y < minY) minY = uv.y;
                    if (uv.x > maxX) maxX = uv.x; if (uv.y > maxY) maxY = uv.y;
                    sx += uv.x; sy += uv.y;
                }
                return "min=(" + minX.ToString("0.##") + "," + minY.ToString("0.##") + ")"
                       + " max=(" + maxX.ToString("0.##") + "," + maxY.ToString("0.##") + ")"
                       + " avg=(" + (sx / n).ToString("0.###") + "," + (sy / n).ToString("0.###") + ")";
            }
            catch { return "err"; }
        }

        private static string SafeName(UnityEngine.Object o)
        {
            try { return o.name; } catch { return "?"; }
        }

        private static string SafeName(Ship s)
        {
            try { return s.vesselName; } catch { }
            try { return s.name; } catch { }
            return "?";
        }
    }
}
