# Data Layout Calibrator v0.4-v0.6 路线图

状态：规划中  
建立日期：2026-09-02  
基线版本：`v0.3.0-preview.1`  
目标仓库：`Yanagisawa2002/data-layout-calibrator`

## 总览

| 版本 | 重点 | 难度 | 项目价值 |
| --- | --- | --- | --- |
| v0.4 | Layout × Kernel × Execution 因子拆分 | 中 | 极高 |
| v0.4 | Paired / hierarchical statistics | 中高 | 极高 |
| v0.4 | Lifetime break-even + advantage envelope | 中高 | 极高 |
| v0.5 | 自适应淘汰和 Pareto frontier | 中 | 高 |
| v0.5 | Source Generator storage / codec scaffolds | 高 | 高 |
| v0.5 | Profile fingerprint、缓存和解析 | 中 | 高 |
| v0.6 | 硬件计数器与因果解释 | 高 | 极高 |
| v0.6 | 多设备、多 ISA、多 workload 验证 | 高 | 极高 |

路线图按版本与条目顺序执行。允许在接口边界稳定后并行开发，但后续条目不得绕过前置的正确性、AOT 与统计门禁。

## 2026-09-02 vNext 集成检查点

状态：四个实现分支已按条目顺序合入 `codex/vnext-integration`；这是未发布的
foundation，不代表 v0.4、v0.5 或 v0.6 完成。package version 仍为
`0.3.0-preview.1`，既有 schema-2 evidence 不改写。

| 条目 | 已集成 | 尚未满足的完成门禁 |
| --- | --- | --- |
| 1. 因子拆分 | 显式 layout/kernel/batch/execution policy、branchless AoS control、AoSoA8 与执行拓扑协议；merged-tree Mono/IL2CPP Burst AOT 已通过 | AoSoA4/AoSoA16、aligned/padded controls、完整 crossed main-effect/interaction 分析 |
| 2. 配对/层级统计 | blocked order metadata、paired log-ratio bootstrap、同一 device 的 process hierarchy、冻结 holdout、稳定 fallback 状态；5 次真实同机 Player evidence 已保留 | 对五次真实 evidence 形成 process-level hierarchical aggregate、device-level hierarchy |
| 3. Advantage envelope | break-even regimes、immutable calibration/holdout cells、summary/renderer、scientific replicate adapter | 真实 axis scan 与正式 Player envelope evidence |
| 4. 自适应/Pareto | conservative quick elimination、strict point-P95 frontier、audit-only exhaustive regret | 在正式候选矩阵上证明 exhaustive-equivalence/regret 与 calibration cost reduction |
| 5. Generator scaffold | 两个不同 Sample record 的 AoS/SoA/AoSoA storage/codec scaffold、diagnostics、deterministic tests；Mono/IL2CPP AOT probe 已通过 | 将 scaffold 接入正式 workload storage |
| 6. Fingerprint/cache | exact fingerprint、integrity codec/store/resolver、非 Optimized 强制 AoS；Mono/IL2CPP Player resolution probe 已通过 | 权威 Player-side CPU/ISA/build provenance source、compatibility governance |
| 7. Counters | fail-closed optional provider contract、raw/derived/overhead/evidence-level models | 至少一个真实 provider、counter-enabled Release suite、overhead control 与 independently retained mechanism artifact |
| 8. Device/ISA/workload | versioned planning manifest、process/physical-device identity separation、local artifact re-verification；两个现有 workload 已有 5 次同机 IL2CPP Player evidence | 当前规划矩阵仍为 0 executable / 18 blocked；无注册 device、无额外 workload、ISA coverage 或 cross-device CI |

共享协议已由 [`ADR 0006`](adr/0006-vnext-integration-protocol.md) 冻结：
candidate canonical bytes、schema 版本、log-ratio uncertainty method、minimum-effect
decision rule、multiplicity v1、point-P95 Pareto、regret 公式，以及 external
envelope reference。任何未完成项必须继续显示为 pending，不得用 Editor 或
synthetic fixture 代替正式证据。

## v0.4：可信优势区间

### 1. Layout × Kernel × Execution 因子拆分

目标：把“完整候选更快”进一步拆解为布局、kernel 形态、batch 与执行拓扑各自及交互产生的影响。

计划：

- 将候选描述扩展为显式的 `LayoutPolicy`、`KernelPolicy`、`BatchPolicy` 与 `ExecutionPolicy` 维度。
- 为 Particle workload 增加 branchless AoS 负面对照，避免把分支消除或 SIMD 收益错误归因于布局。
- 加入 AoSoA4、AoSoA8、AoSoA16，以及必要的 aligned / padded 对照。
- 至少实现 `FrameFaithful` 与 `DependencyChain`；`TemporalBlock<K>` 仅对明确声明可重排的语义开放。
- 保持每个比较单元具有同一 canonical ingress、export、parity 与 lifetime contract。

完成标准：

- 可以分别报告 layout、kernel、batch、execution 的主效应与关键交互，而不是只报告候选名称。
- 所有新候选通过 EditMode、Mono Burst AOT、IL2CPP Burst AOT、parity 与零托管分配门禁。
- 现有固定结果 schema 保持可迁移或提供明确的 schema-version migration。

### 2. Paired / hierarchical statistics

目标：利用同一测量 block 内的配对关系，并正确表达 sample、round、Player process 与 device 层级的不确定性。

计划：

- 采用 randomized blocked / ABBA 或 Latin-square 执行顺序。
- 对基线与候选使用 paired block bootstrap；速度比优先使用 log-ratio 表达。
- calibration 阶段冻结 tuned AoS 与最终候选，holdout 阶段禁止重新调参。
- 多 Player 启动采用 process-level hierarchical bootstrap；未来多设备数据再增加 device 层。
- 为 statistical tie、inconclusive、regression 分别定义稳定结果状态。
- 记录选择后悔值（selection regret）和必要的多重比较控制策略。

完成标准：

- 测试覆盖配对重采样、层级重采样、固定种子复现、tie 回退和 holdout 隔离。
- 报告明确区分单次 Player CI、跨进程 CI 与描述性范围。
- 不再把同机多进程结果表述为多设备证据。

### 3. Lifetime break-even + advantage envelope

目标：从单一 `LifetimeTicks` 的赢家升级为带不确定性的布局优势区间。

核心模型：

```text
AmortizedCost(candidate, lifetime)
  = ResidentP95(candidate)
  + (IngressP95(candidate) + ExportP95(candidate)) / lifetime
```

计划：

- 计算候选相对 tuned AoS 的 break-even lifetime 及 bootstrap 置信区间。
- 输出 piecewise winner、可信优势区、统计灰区与 AoS 回退区。
- 扫描 element count、lifetime、hot/cold ratio、worker count 与 execution policy。
- 生成不可变的 `advantage-envelope.json`；展示层只能读取结果，不得重新选择赢家。
- 报告 peak、median、floor、coverage 与最差 CI 下界，避免单点 cherry-picking。

完成标准：

- 固定输入可重现完全相同的 envelope 与可视化数据。
- 每个 cell 独立通过 parity、allocation、minimum-effect、CI 与 holdout 门禁。
- 热力图/GIF 与 JSON 决策一致，并有自动化一致性测试。

## v0.5：可扩展候选与部署选择

### 4. 自适应淘汰和 Pareto frontier

目标：候选矩阵扩大后，减少明显失败候选的测量成本，同时保留独立的最终统计验证。

计划：

- 阶段一执行 parity、allocation、memory 与 contract feasibility screen。
- 阶段二以少量 blocked samples 进行 quick calibration。
- 淘汰即使采用乐观置信界也无法达到最低收益阈值的候选。
- 以 resident cost、boundary cost 与 resident bytes 构建 Pareto frontier，删除被严格支配候选。
- 只对 finalists 执行完整采样与 bootstrap；最终候选只在未使用的 holdout 上确认。

完成标准：

- 在固定候选集上与 exhaustive search 产生一致或在预设 regret 内的最终决定。
- 明确记录被淘汰原因、阶段、样本数和置信边界。
- 证明加速的是 calibration 过程，而不是通过削弱最终证据获得速度。

### 5. Source Generator storage / codec scaffolds

目标：生成数据布局与边界编解码的机械样板，同时保持业务 kernel 与语义由开发者明确提供。

计划：

- 设计显式、版本化的 record/access schema 与 hot/cold 字段标注。
- 生成 AoS、SoA、AoSoA storage scaffold、ingress/export codec、dispose 与 parity field map。
- 延续直接构造、确定性顺序和无 reflection 的 AOT 原则。
- 对不支持的字段、对齐、嵌套、别名或语义给出编译期 diagnostic。
- 不自动改写任意业务代码，不声称替代 Burst 向量化或编译器优化。

完成标准：

- 至少两个结构差异明显的 workload 使用生成 scaffold。
- Generator 单元测试覆盖确定性输出、diagnostics 和 schema 兼容性。
- Mono Burst AOT 与 IL2CPP Burst AOT consumer project 通过。

### 6. Profile fingerprint、缓存和解析

目标：只在环境与二进制真正兼容时复用冻结选择，避免 stale profile。

计划：

- fingerprint 至少包含 workload/schema/candidate hash、Unity、Burst、backend、build flags、CPU/ISA、worker count 与关键 calibration settings。
- 定义 profile store、resolver、失效原因与 tuned AoS fallback。
- 区分 exact match、compatible match 与 unsupported match；默认只信任 exact match。
- 缓存原始 suite、最终 decision 与 provenance，不缓存展示层重新计算的选择。
- 为 schema 和 profile migration 建立兼容性测试。

完成标准：

- 修改候选、编译器、后端或关键设置会可靠地使旧 profile 失效。
- 缺失、损坏、不兼容 profile 均安全回退 AoS。
- Release Player 能在无 reflection 情况下解析冻结 profile。

## v0.6：因果证据与跨平台验证

### 7. 硬件计数器与因果解释

目标：从“快多少”扩展到“为什么快”，但复用现有 profiler、OS counter 与 Burst 工具，不重造底层采样器或编译器。

计划：

- 定义可选 `ICounterProvider`，缺少 provider 时不影响核心校准。
- 优先采集 cycles/element、instructions/element、IPC、cache misses、branch misses、带宽与 schedule/wait fraction。
- 将 counter 数据与候选、round、process、environment fingerprint 关联。
- 记录 Burst Inspector/assembly artifact 的 hash 与 provenance；因果表述必须与证据等级匹配。
- 设计 counter overhead 校准和启用/禁用对照，避免测量器主导结果。

完成标准：

- 至少一个支持平台完成 counter-enabled suite，并验证计数器关闭时性能决策不被破坏。
- 报告区分相关性、机制证据与经过控制实验支持的因果结论。
- 固定结果包含原始 counters、派生指标和采集失败状态。

### 8. 多设备、多 ISA、多 workload 验证

目标：建立经过分层统计验证的适用范围，而不是把单机结果外推为通用结论。

计划：

- 覆盖至少 AMD64、Intel64 与 ARM64；按可用条件加入 Windows、Linux 和 Apple Silicon。
- 每个平台执行多次独立 Release Player 启动，记录温度、频率/电源策略和后台干扰元数据。
- 增加访问模式明显不同的 workload，并继续保留负面对照。
- 输出 device-specific envelope 与跨设备汇总；不强迫所有设备共享同一赢家。
- 评估 compiler/engine upgrade 前后的 profile 失效与重新校准行为。

候选 workload：

- Skeletal transform / animation
- ECS-like state update
- Sensor fusion / trajectory integration
- Spatial hash / neighborhood query
- Audio mixing
- Image / voxel CPU kernels

完成标准：

- 至少三个 ISA/CPU family 或明确记录因硬件可用性缩减后的正式矩阵。
- 至少四类不同访问模式的 workload，其中包含两个负面对照。
- 发布的数据能支持 device-specific 结论、跨设备层级 CI 和明确的非适用范围。

## 并行实施边界

建议维持一个主集成任务，并适度拆分为四个并行工作流：

1. **Scientific design**：条目 1、2，负责因子拆分、测量设计和统计门禁。
2. **Decision engine**：条目 3、4，负责 break-even、Pareto、自适应淘汰和 immutable envelope。
3. **Plugin/tooling**：条目 5、6，负责 Generator、profile fingerprint、cache 与 AOT consumer。
4. **Evidence lab**：条目 7、8，先建立 provider 和测试矩阵；真实多设备运行取决于可用硬件。

并行任务不得同时修改同一核心文件；共享协议变更先由主集成任务冻结。所有工作通过独立分支/worktree 提交，再由主集成任务顺序合并、解决 schema 迁移、运行全套测试并生成最终交付报告。

## 全局非妥协门禁

- Tuned AoS 始终是安全基线；tie 或证据不足时回退 AoS。
- 展示层只读取冻结结果，绝不重新挑选结果。
- 新算法必须有确定性单元测试与失败路径测试。
- 新候选必须通过 canonical parity、完整 ingress/export 和 lifetime amortization。
- 正式性能主张只能来自非 Development Release Player，并明确 backend、device 与环境。
- Derived reciprocal-latency throughput 必须标注为派生量；直接吞吐量需要独立固定预算测试。
- 所有证据保存 raw samples、hash、settings、environment、decision 与 provenance。
- 不宣称自动优化任意代码、全局最优、跨平台通用提升或未经验证的因果机制。

## 交付定义

每个版本只有同时满足以下条件才可发布：

- 核心、Generator、renderer 与 schema tests 全部通过。
- Mono Burst AOT 与 IL2CPP Burst AOT consumer 验证通过。
- 固定正式证据可从 raw samples 重放并得到相同 decision。
- README、package documentation、ADR、CHANGELOG 与 release notes 同步。
- 敏感信息、公司路径、凭据与第三方授权检查通过。
- 主分支保持可构建，并创建可追溯 tag/Release。
