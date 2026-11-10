using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace TAFHotfixBatch1
{
    /// <summary>
    /// 配置读取：<c>&lt;游戏根&gt;/Mods/SMS_UAD_TAF_CustomizedCore_DEBUG.config.csv</c>，格式与 NavalHoliday 一致
    /// （三列 <c>name,value,comment</c>；兼容 ObjLoader 的 <c>@name,value</c> 表头写法）。
    ///
    /// 约定（用户 2026-10-16 定）：
    ///   · **独立 DLL / 合并进 TAF 之前**：配置为 <c>(DLL名).config.csv</c>，与 NavalHoliday 同模式；
    ///     **参数名不加前缀**（独立配置不会冲突）——即使最终不合并也没问题。
    ///   · **并入 TAF 之后**：改写为 params.csv / params_override.csv 中的参数；
    ///     其中**原版参数**用 <c>taf_</c> 前缀，而**由我们（而非 Duke）新增的**用复合前缀
    ///     <c>taf_sms_</c> 标记，以免混淆或冲突。
    ///
    /// 解析细节：容忍引号包裹的值（如 <c>"1;10000,0.8;20000"</c>）、<c>#</c>/<c>//</c> 注释行与空行。
    /// </summary>
    internal static class HotfixParams
    {
        private const string CfgName = "SMS_UAD_TAF_CustomizedCore_DEBUG.config.csv";

        // ---- 内建默认值（与随附的 config.csv 保持一致）----
        internal static string Mode = "log";                 // log = 只记录不改；apply = 真正写入
        internal static bool RefitChainFix = true;           // A  赔偿舰/旧存档 改造链修复
        internal static bool ShipLossGuard = true;           // B  丢船：维修优先于补充船员
        internal static bool DuplicateDesignGuard = true;    // C  重复设计 / 重复 id
        internal static bool SunkGraceWindow = true;         // D  沉船删除缓冲期
        internal static int SunkGraceTurns = 6;              // D  缓冲回合数（用户定：6）
        internal static bool PriorityRepairOverCrew = true;  // B  维修优先级恒高于补充船员
        internal static bool Verbose = false;
        /// <summary>
        /// A 方案 dry-run 探针。**默认关闭**：实测在 `Deserialize` Postfix 中访问 IL2CPP 对象
        /// 会在读档时崩溃（极可能是 MessagePack 反序列化不在主线程）。
        /// 仅用于复现/研究，正常游玩请保持 0。
        /// </summary>
        internal static bool DeserializeProbe = false;
        /// <summary>B2：把"被当成设计"的实存船重新挂回设计（主线程、一次性）。</summary>
        internal static bool ShipDesignRelink = true;
        /// <summary>
        /// 最后兜底：以"船自身的数据"反向生成一份设计。
        /// **默认关闭** —— 用户实证：全面启用会导致"存档膨胀 + 悬空引用丢船"，
        /// 仅对确无对应设计的个别船（约 17 条）才值得尝试。
        /// </summary>
        internal static bool ShipDesignRegen = false;
        /// <summary>
        /// A2：赔偿舰移交后设为"非手动标记的封存"，让系统自动补员→海试，
        /// 而不是停在"在役但 0 船员"的矛盾状态（默认开）。
        /// </summary>
        internal static bool ReparationMothball = true;
        /// <summary>[SMS.1.SCENE] 场景结构只读探针。</summary>
        internal static bool SceneProbe = false;
        /// <summary>[SMS.1.MAT] 舯段渲染/材质只读探针。</summary>
        internal static bool MatProbe = false;
        /// <summary>[SMS.1.DECOR] 是否用替代实现拦截空渲染器的 Decor.Awake（默认 0=不介入）。</summary>
        internal static bool DecorAwakeGuard = false;
        /// <summary>[SMS.2.RH] 吞掉 Decor.Awake 的原版异常（默认开）。</summary>
        internal static bool DecorAwakeSwallow = true;   // 默认不介入（开启会用空集合替代，可能抑制装饰初始化）
        /// <summary>[SMS.1.DECOR] 是否启用"被炮塔/炮管遮挡则隐藏装饰件"（默认 0=只生成不隐藏）。</summary>
        internal static bool DecorHideOccluded = true;    // 碰撞/遮挡隐藏（救生艇等应与原版一致）
        /// <summary>[SMS.1.SHADOW] 关闭舯段投影（A/B 色差测试用）。</summary>
        internal static bool MiddleShadowOff = false;
        /// <summary>[SMS.1.WL] 应用 hullData 的 waterline / stabilityForward（Beta4 引入）。</summary>
        internal static bool HullDataApply = true;
        /// <summary>[SMS.1.SHIPGEN] 造船限制模式：0=关，1=生成后审计，2=审计+强制（暂等同 1）。</summary>
        internal static int ShipGenLimits = 0;
        /// <summary>[SMS.1.OCEAN] 静态海面可行性探针（只读）。</summary>
        internal static bool StaticOceanProbe = false;
        /// <summary>[SMS.1.DECAL] 命中贴花按口径缩放（duke 的弹孔修复）。</summary>
        internal static bool DecalSize = true;
        /// <summary>[SMS.1.RH] 是否启用移植的 RefreshHull（0=走原版，用于色差二分）。</summary>
        internal static bool RefreshHull = true;
        /// <summary>[SMS.1.SEC] 分段状态转储探针（只读，色差定位用）。</summary>
        internal static bool SectionProbe = false;
        /// <summary>[SMS.1.SEC] 材质实验：0=不动 1=中段改用艏段材质 2=全船段统一为 steel_4</summary>
        internal static int SectionMatForce = 0;
        /// <summary>[SMS.1.SEC] 实验：1=关闭各段 AutomaticLOD（强制全精度模型）</summary>
        internal static int SectionLodOff = 0;
        /// <summary>[SMS.2.SEC] 把舯段 isStatic 置 false（验证静态合批假设）。</summary>
        internal static bool SectionNoBatch = false;
        /// <summary>[SMS.2.SEC] 每段最多 dump 几个 Renderer。</summary>
        internal static int SectionProbeMax = 32;
        /// <summary>[SMS.2.SEC] 舱段 UV 归一化（把舯段 UV 跨度/中心对齐到舰艏）。</summary>
        internal static bool SectionUvNormalize = false;
        /// <summary>[SMS.2.SEC] 关闭船体所有 Renderer 的反射探针采样（排除"环境反射"假设）。</summary>
        internal static bool SectionReflectionOff = false;
        /// <summary>[SMS.2.SEC] 把所有船体段的 probeAnchor 统一到船体根（排除"锚点不同"假设）。</summary>
        internal static bool SectionProbeAnchor = false;

        // ---- [SMS.2.SEC] 构造器反射探针（色差根因）----
        /// <summary>Probes 容器开关（0=按 mode 处理）。</summary>
        internal static bool ConstructorProbes = false;
        /// <summary>0=整组关(duke) 1=只关局部(默认) 2=局部intensity=0 3=全开 4=只留船中 6=1+提Global 7=局部下移</summary>
        internal static int ConstructorProbeMode = 1;
        /// <summary>mode=6 时 Global 探针 intensity。</summary>
        internal static float ConstructorProbeGlobalIntensity = 1.4f;
        /// <summary>mode=7 时局部探针的 Y。</summary>
        internal static float ConstructorProbeY = -30f;

        /// <summary>[SMS.2.CQ] 殖民征服/叛乱事件成功率取证探针（DEBUG 用）。</summary>
        internal static bool ConquestProbe = false;
        /// <summary>[SMS.2.UI] dump 选项窗口 General 页每行的结构（用于按钮组改造）。</summary>
        internal static bool UiDumpGeneral = false;
        /// <summary>[SMS.2.LOG] 不额外写独立日志文件（只走 MelonLoader 主日志）。</summary>
        internal static bool LogNoFile = true;
        /// <summary>[SMS.2.CQ] 需要翻转的事件类型（逗号分隔，默认 2=Rebellion）。</summary>
        internal static string ConquestFixTypes = "2";
        /// <summary>[SMS.2.CQ] 0=原版 1=翻转 2=自定义公式</summary>
        internal static int ConquestChanceMode = 2;   // 默认启用自定义公式（叛乱成功率修复）
        /// <summary>[SMS.2.CQ] mode2: 防御方吨位达到 required 的几倍时成功率 100%</summary>
        internal static float ConquestRatioFull = 1f;   // 1.7 征服只需 1× 需求吨位 ⇒ 叛乱对齐也用 1×
        /// <summary>[SMS.2.CQ] mode2: 开局/满期概率系数（与 duke 的 0.01/0.5 同形）</summary>
        internal static float ConquestMultStart = 0.6f;
        internal static float ConquestMultFull = 1f;
        /// <summary>[SMS.1.PROP] 是否在 RefreshHull 内布点（默认 0：交给 deck-clutter 路径）。</summary>
        internal static bool PropInRefreshHull = true;   // 3.22 原版不再创建 DeckProps，装饰物只能由我们布点（默认必须为 1）
        /// <summary>[SMS.1.OCEAN] 守夜人方案（默认关；port90 证实会误伤船体）。</summary>
        internal static bool OceanKeeper = false;
        /// <summary>[SMS.1.OCEAN] port92 的分叉方案（默认关；现由 GameManager 的模式分支统一负责）。</summary>
        internal static bool OceanDukeAllWater = false;

        // ---- [SMS.1.OCEAN] 简化海面可调参数（供各 modder 自选观感）----
        internal static float StaticOceanGloss = 0.15f;     // _Glossiness
        internal static float StaticOceanMetallic = 0.85f;  // _Metallic（小值而非 0，保留一点金属感）
        internal static float StaticOceanTiling = 400f;     // _MainTex 平铺
        internal static float StaticOceanR = 0.03f;         // 颜色 R
        internal static float StaticOceanG = 0.08f;         // 颜色 G
        internal static float StaticOceanB = 0.13f;         // 颜色 B
        internal static float StaticOceanAlpha = 0.9f;
        /// <summary>[SMS.1.OCEAN] 简化/空水模式下的鱼雷雷迹补偿。</summary>
        internal static bool TorWakeHint = true;
        // ---- 鱼雷雷迹可调参数 ----
        internal static float TorWakeSeconds = 25f;    // 保留时间（**战斗内秒**，随游戏时间倍速流逝）
        internal static float TorWakeFadePow = 2.5f;     // 渐隐强度：alpha ∝ t^n（n 越大，旧段消失越快）
        internal static float TorWakeWidth = 2f;     // 条带宽度（米）
        internal static float TorWakeAlpha = 160f;
        /// <summary>[SMS.1.OCEAN] 只为本方可见的鱼雷绘制雷迹（避免通过雷迹提前发现敌雷）。</summary>
        internal static bool TorWakeVisibleOnly = true;
        /// <summary>[SMS.2.WAKE] 鱼雷消失后雷迹保留时间（秒）。</summary>
        internal static float TorWakeLinger = 10f;
        /// <summary>[SMS.2.WAKE] 用鱼雷实际渲染包围盒取点（否则用 transform.position）。</summary>
        internal static bool TorWakeUseRenderBounds = true;
        /// <summary>[SMS.2.WAKE] 仅当鱼雷模型可见时才画雷迹（防"未发现敌雷"剧透）。</summary>
        internal static bool TorWakeRequireModel = true;
        internal static int TorWakeMinRenderers = 2;   // 暂默认关（模型判定待校准）
        /// <summary>[SMS.2.WAKE] 鱼雷模型取证日志（独立开关，默认关）。</summary>
        internal static bool TorWakeProbe = false;
        /// <summary>[SMS.2.WAKE] 鱼雷未被原版记录可见性时是否放行（0=隐藏，防剧透）。</summary>
        internal static bool TorWakeFailOpen = true;   // 原版不常调用 Torpedo.ShowForOthers，默认放行；防剧透待玩家舰队识别
        /// <summary>[SMS.2.WAKE] 只为我方阵营可见的舰船/鱼雷绘制航迹（wake_visible_only）。</summary>
        internal static bool WakeVisibleOnly = true;
        // ---- [SMS.2.OCEAN] 原版水底 FakePlaneToFixWiresGlitch ----
        internal static float NoneWaterBottomMode = 1f;      // 0=不动 1=改色 2=关闭 3=下移
        internal static float NoneWaterBottomR = 0.02f;
        internal static float NoneWaterBottomG = 0.05f;
        internal static float NoneWaterBottomB = 0.09f;
        internal static float NoneWaterBottomHeight = -200f;
        // ---- [SMS.2.OCEAN] 原版海水（oceanType=0）材质微调 ----
        internal static bool VanillaOceanTweak = false;   // 默认关，先探针
        // OceanFFT 公开字段（战斗海面）
        internal static float VanillaOceanB = 0.13f;  // mode=3 时的位移量（叠加在原 -100 上）
        // ---- [SMS.2.WAKE] 船尾航迹 ----
        internal static bool ShipWakeHint = true;
        internal static float ShipWakeSeconds = 20f;
        internal static float ShipWakeFadePow = 2.5f;
        internal static float ShipWakeWidth = 2.5f;
        internal static float ShipWakeAlpha = 120f;
        internal static float ShipWakeExtraWidth = 2f;
        internal static float ShipWakeR = 0.86f;
        internal static float ShipWakeG2 = 0.92f;
        internal static float ShipWakeB = 0.96f;
        internal static float ShipWakePoints = 6f;
        internal static float ShipWakeDash = 1f;
        internal static float ShipWakeOrigin = 1f;
        internal static float ShipWakeColorMode = 1f;
        internal static float ShipWakeMaxShips = 60f;
        /// <summary>[SMS.2.HC] 射击概率量化网格；原版 0.01（1%），默认改 0.001（0.1%）。</summary>
        internal static float HitChanceGrid = 0.001f;
        /// <summary>[SMS.2.HC] 诊断：打印 HitChance/HitChanceCalc 的实际数值。</summary>
        internal static bool HitChanceProbe = true;
        internal static float ShipWakeMaxDistance = 30000f;  // 超距不更新航迹（米）   // 航迹同时处理的船数上限（0=不限）   // 1=仿航行灯彩色航迹   // 0=舰艉 1=舯段     // 基础亮度（0-255，最新点的 alpha）      // 不透明度（1=不透明；<1 走 Transparent）
        /// <summary>[SMS.2.WAKE] 单帧位移超过该值视为瞬移（清除该点列）/ teleport threshold (m)</summary>
        internal static float ShipWakeMaxJump = 500f;

        /// <summary>[SMS.1.OCEAN] 启用静态海面（duke 的低质海面方案，需要 TAFData 贴图）。</summary>
        internal static bool StaticOcean = false;
        /// <summary>
        /// E：改造开始后，对被选中的船强制重算造价/重量（修"改造后仍按旧造价"）。
        /// </summary>
        internal static bool RefitCostRefresh = true;
        /// <summary>E：是否也对 AI 改造刷新造价（默认关；开启用于验证，见 docs/44）。</summary>
        internal static bool RefitAiToo = false;
/// <summary>[P] 只读诊断：预制池缺舰种时列出该实例实际有哪些键。</summary>
        internal static bool PredefPoolProbe = false;
        /// <summary>[P] 真实桶缺舰种时，回退到别名桶（{包序}_{玩家}）取设计。</summary>
        internal static bool PredefAliasFallback = true;
        /// <summary>[SMS.1.PF] 当 taf_force_no_predef_designs=1 时，一并关掉预制设计取用（避免 TAF 越界崩溃）。</summary>
        internal static bool PredefForceOff = true;
        /// <summary>[SMS.1.TON] 实存船原始吨位校验（只读）。</summary>
        internal static bool TonnageCheck = true;
        /// <summary>[SMS.1.TON] 严格判据：tonnage 必须与 design 完全一致（容差 0.1 吨）。</summary>
        internal static bool TonnageCheckStrict = true;
        /// <summary>[SMS.1.TON] 把不一致的实存船 tonnage 回填为设计值（默认关）。</summary>
        internal static bool TonnageRepair = false;
        /// <summary>[SMS.1.STA] 设计 status 稽核（只读）。</summary>
        internal static bool DesignStatusAudit = true;
        /// <summary>[SMS.1.STA] 把 status!=0 的设计统一置 0（默认关）。</summary>
        internal static bool DesignStatusFix = true;
        /// <summary>[L] 抑制 TAF 的 "has no valid predefined designs" 逐包报错（噪音，非真缺口）。</summary>
        internal static bool SuppressPredefPackErrors = true;
        /// <summary>[T] 修正 dirtycheater「立即完成当前科技」对 SMS 科技树的匹配。</summary>
        internal static bool CheatTechFix = true;
        /// <summary>[Y] 非原版舰种：只读探针（log 层，不改行为）。</summary>
        internal static bool ShipTypeProbe = false;
        /// <summary>[Y2] 非原版舰种：行为接管（UI 寻址扩展，postfix only）。</summary>
        internal static bool ShipTypeApply = true;
        /// <summary>[Y3] IsPartAvailable 只读探针（零假定、无额外配置）。</summary>
        internal static bool IsPartProbe = false;
        /// <summary>[H] 船体舰种归属只读诊断（一次）。</summary>
        internal static bool HullTypeDump = false;
        /// <summary>[Y5] 只读诊断：构造器舰种参数来源。</summary>
        internal static bool ConstructorTypeProbe = false;
        /// <summary>
        /// E 的强度：true = 用改造设计**覆写**被选中船的状态（再重算造价）；
        ///           false = 只做 NeedRecalcStats + CalcWeightAndCost。
        /// </summary>
        internal static bool RefitStateOverwrite = false;
        /// <summary>
        /// E：FromStore 之后是否再强制 CalcWeightAndCost。**默认关** ——
        /// 强制重算会漏掉装甲重量/材料成本，导致吨位与造价偏低（第 26 轮实测）。
        /// </summary>
        internal static bool RefitForceRecalc = false;
        /// <summary>
        /// E：在改造开始后**延后两帧**再重算重量/造价（等装甲与分舱权重结算完）。
        /// 立刻算会偏低、完全不算会偏高 —— 这是实测出来的正确时机。默认开。
        /// </summary>
        internal static bool RefitDeferredRecalc = false;
        /// <summary>
        /// E：FromStore 之后调用 RefreshHull(true)+RefreshMounts()+CalcWeightAndCost，
        /// 补上"分区/装甲结算"这一步（不触碰 3D：不调用 RefreshDecor/ReloadModels）。
        /// </summary>
        internal static bool RefitFullRecalc = false;
        /// <summary>
        /// E（推荐方案）：改造开始时只调用 `Ship.NeedRecalcStats()` 置缓存失效，
        /// 由引擎在界面刷新时惰性重算 —— 对应 1.7 新增的缓存机制（1.5.1.6 无缓存故无此 bug）。
        /// </summary>
        internal static bool RefitInvalidate = false;   // 隐藏开关：会产生非法值，禁止启用
        /// <summary>E 的性能测量开关（测试用）：对选中船做 ToStore→FromStore 自往返并计时。默认关。</summary>
        internal static bool RefitBench = false;
        /// <summary>E 的诊断：改造开始时打印"设计 / 船"的造价与吨位，用于与造船厂内的正确值对照。</summary>
        internal static bool RefitDiag = false;
        /// <summary>
        /// E 方案 B（推荐）：改造中的船在 Cost()/MaintenanceCost() 上直接返回"待应用改造设计"的值。
        /// 不写缓存、不重建部件；只在 status==Refit 且 designShipForRefit 存在时生效。
        /// </summary>
        internal static bool RefitCostDisplay = true;
        /// <summary>
        /// F：TAF 的 AI 隐藏构造 workaround 会把真船下沉 1000m 且不还原
        /// ⇒ 本开关负责记录并在 LeaveConstructor / 离开 World 时还原。
        /// </summary>
        internal static bool AiRefitRestore = true;
        /// <summary>
        /// **调试**：把所有 AI 性格的 `aiRefit`（改装意愿）设为该值；0=关闭。
        /// 仅用于加速验证 Module F（让 AI 更快去改装），正式游玩应保持 0。
        /// </summary>
        internal static float DebugAiRefit = 0f;
        /// <summary>
        /// **G1 探针**（只读）：记录 AI 现场生成设计时"为哪个舰种、重试几次"。
        /// 默认 0。开启后零副作用，仅写日志。
        /// </summary>
        internal static bool DebugAiDesignProbe = false;
                /// <summary>[SMS.2.G2] 混合模式下允许抽"超前当前年"的年数。</summary>
        /// <summary>[SMS.2.G2] T1 基础容忍：设计 techs 数 - 玩家已完成科技数 的允许差值。</summary>
                /// <summary>[SMS.2.G2][T3] 兜底"大跃进"总开关：1=开启（包链抽不到时补科技）。</summary>
        internal static bool TrueMixedCatchup = true;
        /// <summary>[SMS.2.G2][T3] 兜底时回退年数下限。</summary>
        internal static int TrueMixedCatchupBackMin = 3;
        /// <summary>[SMS.2.G2][T3] 兜底时回退年数上限。</summary>
        internal static int TrueMixedCatchupBackMax = 5;
        /// <summary>[SMS.2.G2][T3] 兜底冷却回合数。</summary>
        internal static int TrueMixedCatchupCooldownTurns = 6;
internal static int TrueMixedTechTolerance = 8;
        /// <summary>[SMS.2.G2] 年份补正起始年（每 step 年 +1 容忍）。</summary>
        internal static int TrueMixedTechYearBase = 1920;
        /// <summary>[SMS.2.G2] 年份补正步长（年）。</summary>
        internal static int TrueMixedTechYearStep = 2;
internal static int TrueMixedYearAhead = 2;
        /// <summary>[SMS.2.G2] 混合模式下回溯窗口年数（从 year_ahead 往前的年数）。</summary>
internal static int TrueMixedYearWindow = 10;
internal static bool TrueMixed = false;
        internal static int DesignCapPerType = 6;
        internal static bool DebugParamTrace = false;
        internal static bool KeepLastDesign = true;
        internal static bool SharedBackfillProbe = true;
        internal static bool SharedBackfillAggressive = true;
        internal static bool ReparationCopyFix = true;
        internal static int SharedBackfillMax = 0;
        internal static int KeepLastDesignAgeTurns = 36;
        internal static bool KeepLastDesignTriggerPredef = true;

        internal static bool IsApply { get { return string.Equals(Mode, "apply", StringComparison.OrdinalIgnoreCase); } }

        private static readonly Dictionary<string, string> _raw = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>读取一份 CSV 到 _raw（后读覆盖先读）。缺失文件不报错，仅记录。</summary>
        private static void LoadFile(string fileName)
        {
            // 只从 Mods/ 读取；TAFData/ 下的 DEBUG 配置仅作**分发备份**（用的时候由用户自己拿出来）
            string path = Path.Combine(AppContext.BaseDirectory, "Mods", fileName);
            try
            {
                if (!File.Exists(path))
                {
                    HotfixLog.Info("[cfg] Mods/" + fileName + " not found -- skipped");
                    return;
                }
                int before = _raw.Count;
                foreach (string rawLine in File.ReadAllLines(path, Encoding.UTF8))
                {
                    string line = rawLine.Trim();
                    if (line.Length == 0) continue;
                    if (line[0] == '#' || line.StartsWith("//")) continue;

                    List<string> cells = SplitCsv(line);
                    if (cells.Count < 2) continue;

                    string name = cells[0].Trim();
                    if (name.Length > 0 && name[0] == '\uFEFF') name = name.Substring(1).Trim();
                    if (name.Length == 0) continue;
                    if (name[0] == '@') name = name.Substring(1);
                    if (name.Equals("name", StringComparison.OrdinalIgnoreCase)) continue;

                    _raw[name] = cells[1].Trim();
                }
                HotfixLog.Info("[cfg] loaded " + _raw.Count + " entries from " + path + " (new keys: " + (_raw.Count - before) + ")");
            }
            catch (Exception e)
            {
                HotfixLog.Error("[cfg] load " + fileName + " failed: " + e.Message);
            }
        }

        internal static void Load()
        {
            // 读取优先级（低 → 高）：params.csv（TAF 内部） < SMS 核心配置 < 每批次热修调试配置
            LoadFile("SMS_UAD_TAF_CustomizedCore.config.csv");   // 固化配置（发布态）
            LoadFile(CfgName);                                   // 每批次热修调试配置（覆盖）

            Mode = GetStr("mode", Mode);
            RefitChainFix = GetBool("refit_chain_fix", RefitChainFix);
            ShipLossGuard = GetBool("ship_loss_guard", ShipLossGuard);
            DuplicateDesignGuard = GetBool("duplicate_design_guard", DuplicateDesignGuard);
            SunkGraceWindow = GetBool("sunk_grace_window", SunkGraceWindow);
            SunkGraceTurns = GetInt("sunk_grace_turns", SunkGraceTurns);
            PriorityRepairOverCrew = GetBool("priority_repair_over_crew", PriorityRepairOverCrew);
            Verbose = GetBool("verbose", Verbose);
            DeserializeProbe = GetBool("deserialize_probe", DeserializeProbe);
            ShipDesignRelink = GetBool("ship_design_relink", ShipDesignRelink);
            ShipDesignRegen = GetBool("ship_design_regen", ShipDesignRegen);
            ReparationMothball = GetBool("reparation_mothball", ReparationMothball);
            SceneProbe = GetBool("scene_probe", SceneProbe);
            MatProbe = GetBool("mat_probe", MatProbe);
            DecorAwakeGuard = GetBool("decor_awake_guard", DecorAwakeGuard);
            DecorAwakeSwallow = GetBool("decor_awake_swallow", DecorAwakeSwallow);
            DecorHideOccluded = GetBool("decor_hide_occluded", DecorHideOccluded);
            MiddleShadowOff = GetBool("middle_shadow_off", MiddleShadowOff);
            HullDataApply = GetBool("hull_data_apply", HullDataApply);
            ShipGenLimits = GetInt("shipgen_limits", ShipGenLimits);
            StaticOceanProbe = GetBool("static_ocean_probe", StaticOceanProbe);
            DecalSize = GetBool("decal_size", DecalSize);
            RefreshHull = GetBool("refresh_hull", RefreshHull);
            SectionProbe = GetBool("section_probe", SectionProbe);
            SectionMatForce = GetInt("section_mat_force", SectionMatForce);
            SectionLodOff = GetInt("section_lod_off", SectionLodOff);
            SectionNoBatch = GetBool("section_no_batch", SectionNoBatch);
            SectionProbeMax = GetInt("section_probe_max", SectionProbeMax);
            SectionUvNormalize = GetBool("section_uv_normalize", SectionUvNormalize);
            SectionReflectionOff = GetBool("section_reflection_off", SectionReflectionOff);
            SectionProbeAnchor = GetBool("section_probe_anchor", SectionProbeAnchor);
            ConstructorProbes = GetBool("constructor_probes", ConstructorProbes);
            ConstructorProbeMode = GetInt("constructor_probe_mode", ConstructorProbeMode);
            ConstructorProbeGlobalIntensity = GetFloat("constructor_probe_global_intensity", ConstructorProbeGlobalIntensity);
            ConstructorProbeY = GetFloat("constructor_probe_y", ConstructorProbeY);
            ConquestProbe = GetBool("conquest_probe", ConquestProbe);
            UiDumpGeneral = GetBool("ui_dump_general", UiDumpGeneral);
            LogNoFile = GetBool("log_no_file", LogNoFile);
            ConquestFixTypes = GetStr("conquest_fix_types", ConquestFixTypes);
            ConquestChanceMode = GetInt("conquest_chance_mode", ConquestChanceMode);
            ConquestRatioFull = GetFloat("conquest_ratio_full", ConquestRatioFull);
            ConquestMultStart = GetFloat("conquest_mult_start", ConquestMultStart);
            ConquestMultFull = GetFloat("conquest_mult_full", ConquestMultFull);
            PropInRefreshHull = GetBool("prop_in_refresh_hull", PropInRefreshHull);
            OceanKeeper = GetBool("ocean_keeper", OceanKeeper);
            OceanDukeAllWater = GetBool("ocean_duke_all_water", OceanDukeAllWater);
            StaticOceanGloss = GetFloat("static_ocean_gloss", StaticOceanGloss);
            StaticOceanMetallic = GetFloat("static_ocean_metallic", StaticOceanMetallic);
            StaticOceanTiling = GetFloat("static_ocean_tiling", StaticOceanTiling);
            StaticOceanR = GetFloat("static_ocean_r", StaticOceanR);
            StaticOceanG = GetFloat("static_ocean_g", StaticOceanG);
            StaticOceanB = GetFloat("static_ocean_b", StaticOceanB);
            StaticOceanAlpha = GetFloat("static_ocean_alpha", StaticOceanAlpha);
            TorWakeHint = GetBool("torp_wake_hint", TorWakeHint);
            TorWakeSeconds = GetFloat("torp_wake_seconds", TorWakeSeconds);
            TorWakeFadePow = GetFloat("torp_wake_fade_pow", TorWakeFadePow);
            TorWakeWidth = GetFloat("torp_wake_width", TorWakeWidth);
            TorWakeAlpha = GetFloat("torp_wake_alpha", TorWakeAlpha);
            TorWakeVisibleOnly = GetBool("torp_wake_visible_only", TorWakeVisibleOnly);
            TorWakeLinger = GetFloat("torp_wake_linger", TorWakeLinger);
            TorWakeUseRenderBounds = GetBool("torp_wake_use_render_bounds", TorWakeUseRenderBounds);
            TorWakeFailOpen = GetBool("torp_wake_fail_open", TorWakeFailOpen);
            TorWakeRequireModel = GetBool("torp_wake_require_model", TorWakeRequireModel);
            TorWakeMinRenderers = GetInt("torp_wake_min_renderers", TorWakeMinRenderers);
            TorWakeProbe = GetBool("torp_wake_probe", TorWakeProbe);
            WakeVisibleOnly = GetBool("wake_visible_only", WakeVisibleOnly);
            HitChanceGrid = GetFloat("hit_chance_grid", HitChanceGrid);
            HitChanceProbe = GetBool("hit_chance_probe", HitChanceProbe);
            NoneWaterBottomMode = GetFloat("none_waterbottom_mode", NoneWaterBottomMode);
            NoneWaterBottomR = GetFloat("none_waterbottom_r", NoneWaterBottomR);
            NoneWaterBottomG = GetFloat("none_waterbottom_g", NoneWaterBottomG);
            NoneWaterBottomB = GetFloat("none_waterbottom_b", NoneWaterBottomB);
            NoneWaterBottomHeight = GetFloat("none_waterbottom_height", NoneWaterBottomHeight);
            ShipWakeHint = GetBool("ship_wake_hint", ShipWakeHint);
            ShipWakeSeconds = GetFloat("ship_wake_seconds", ShipWakeSeconds);
            ShipWakeFadePow = GetFloat("ship_wake_fade_pow", ShipWakeFadePow);
            ShipWakeWidth = GetFloat("ship_wake_width", ShipWakeWidth);
            ShipWakeAlpha = GetFloat("ship_wake_alpha", ShipWakeAlpha);
            ShipWakeExtraWidth = GetFloat("ship_wake_extra_width", ShipWakeExtraWidth);
            ShipWakeR = GetFloat("ship_wake_r", ShipWakeR);
            ShipWakeG2 = GetFloat("ship_wake_g", ShipWakeG2);
            ShipWakeB = GetFloat("ship_wake_b", ShipWakeB);
            ShipWakePoints = GetFloat("ship_wake_points", ShipWakePoints);
            ShipWakeDash = GetFloat("ship_wake_dash", ShipWakeDash);
            ShipWakeOrigin = GetFloat("ship_wake_origin", ShipWakeOrigin);
            ShipWakeColorMode = GetFloat("ship_wake_color_mode", ShipWakeColorMode);
            ShipWakeMaxShips = GetFloat("ship_wake_max_ships", ShipWakeMaxShips);
            ShipWakeMaxDistance = GetFloat("ship_wake_max_distance", ShipWakeMaxDistance);
            ShipWakeMaxJump = GetFloat("ship_wake_max_jump", ShipWakeMaxJump);
            StaticOcean = GetBool("static_ocean", StaticOcean);
            RefitCostRefresh = GetBool("refit_cost_refresh", RefitCostRefresh);
            RefitAiToo = GetBool("refit_ai_too", RefitAiToo);
            PredefPoolProbe = GetBool("predef_pool_probe", PredefPoolProbe);
            PredefAliasFallback = GetBool("predef_alias_fallback", PredefAliasFallback);
            PredefForceOff = GetBool("predef_force_off", PredefForceOff);
            TonnageCheck = GetBool("tonnage_check", TonnageCheck);
            TonnageCheckStrict = GetBool("tonnage_check_strict", TonnageCheckStrict);
            TonnageRepair = GetBool("tonnage_repair", TonnageRepair);
            DesignStatusAudit = GetBool("design_status_audit", DesignStatusAudit);
            DesignStatusFix = GetBool("design_status_fix", DesignStatusFix);
            SuppressPredefPackErrors = GetBool("suppress_predef_pack_errors", SuppressPredefPackErrors);
            CheatTechFix = GetBool("cheat_tech_fix", CheatTechFix);
            ShipTypeProbe = GetBool("shiptype_probe", ShipTypeProbe);
            ShipTypeApply = GetBool("shiptype_apply", ShipTypeApply);
            IsPartProbe = GetBool("shiptype_part_probe", IsPartProbe);
            HullTypeDump = GetBool("hull_type_dump", HullTypeDump);
            ConstructorTypeProbe = GetBool("constructor_type_probe", ConstructorTypeProbe);
            RefitStateOverwrite = GetBool("refit_state_overwrite", RefitStateOverwrite);
            RefitForceRecalc = GetBool("refit_force_recalc", RefitForceRecalc);
            RefitDeferredRecalc = GetBool("refit_deferred_recalc", RefitDeferredRecalc);
            RefitFullRecalc = GetBool("refit_full_recalc", RefitFullRecalc);
            RefitInvalidate = GetBool("refit_invalidate", RefitInvalidate);
            RefitBench = GetBool("refit_bench", RefitBench);
            RefitDiag = GetBool("refit_diag", RefitDiag);
            RefitCostDisplay = GetBool("refit_cost_display", RefitCostDisplay);
            AiRefitRestore = GetBool("ai_refit_restore", AiRefitRestore);
            DebugAiRefit = GetFloat("debug_ai_refit", DebugAiRefit);
            DebugAiDesignProbe = GetBool("debug_ai_design_probe", DebugAiDesignProbe);
            TrueMixed = GetBool("true_mixed", TrueMixed);
            TrueMixedYearAhead = GetInt("true_mixed_year_ahead", TrueMixedYearAhead);
            TrueMixedTechTolerance = GetInt("true_mixed_tech_tolerance", TrueMixedTechTolerance);
            TrueMixedCatchup = GetBool("true_mixed_catchup", TrueMixedCatchup);
            TrueMixedCatchupBackMin = GetInt("true_mixed_catchup_back_min", TrueMixedCatchupBackMin);
            TrueMixedCatchupBackMax = GetInt("true_mixed_catchup_back_max", TrueMixedCatchupBackMax);
            TrueMixedCatchupCooldownTurns = GetInt("true_mixed_catchup_cooldown_turns", TrueMixedCatchupCooldownTurns);
            TrueMixedTechYearBase = GetInt("true_mixed_tech_year_base", TrueMixedTechYearBase);
            TrueMixedTechYearStep = GetInt("true_mixed_tech_year_step", TrueMixedTechYearStep);
            TrueMixedYearWindow = GetInt("true_mixed_year_window", TrueMixedYearWindow);
            DesignCapPerType = GetInt("design_cap_per_type", DesignCapPerType);
            DebugParamTrace = GetBool("debug_param_trace", DebugParamTrace);
            KeepLastDesign = GetBool("keep_last_design", KeepLastDesign);
            SharedBackfillProbe = GetBool("shared_backfill_probe", SharedBackfillProbe);
            SharedBackfillAggressive = GetBool("shared_backfill_aggressive", SharedBackfillAggressive);
            ReparationCopyFix = GetBool("reparation_copy_fix", ReparationCopyFix);
            SharedBackfillMax = GetInt("shared_backfill_max", SharedBackfillMax);
            KeepLastDesignAgeTurns = GetInt("keep_last_design_age_turns", KeepLastDesignAgeTurns);
            KeepLastDesignTriggerPredef = GetBool("keep_last_design_trigger_predef", KeepLastDesignTriggerPredef);
        }

        /// <summary>极简 CSV 行解析：支持双引号包裹与 "" 转义。</summary>
        private static List<string> SplitCsv(string line)
        {
            List<string> cells = new List<string>();
            StringBuilder cur = new StringBuilder();
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
                else
                {
                    if (c == '"') inQuotes = true;
                    else if (c == ',') { cells.Add(cur.ToString()); cur.Length = 0; }
                    else cur.Append(c);
                }
            }
            cells.Add(cur.ToString());
            return cells;
        }

        private static string GetStr(string key, string def)
        {
            string v;
            return _raw.TryGetValue(key, out v) ? v : def;
        }

        private static bool GetBool(string key, bool def)
        {
            string v;
            if (!_raw.TryGetValue(key, out v)) return def;
            v = v.Trim().ToLowerInvariant();
            if (v == "1" || v == "true" || v == "yes" || v == "on") return true;
            if (v == "0" || v == "false" || v == "no" || v == "off") return false;
            return def;
        }

        private static int GetInt(string key, int def)
        {
            string v;
            if (!_raw.TryGetValue(key, out v)) return def;
            int n;
            return int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out n) ? n : def;
        }
        private static float GetFloat(string key, float def)
        {
            string v;
            if (!_raw.TryGetValue(key, out v)) return def;
            float f;
            return float.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out f) ? f : def;
        }
    }
}
