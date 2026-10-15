# CODELY.md — Genesis Valley / 创世谷 项目上下文

> 本文件为 Codely CLI 在本目录工作时的指导上下文。项目权威规格是 `Genesis_Valley_V1_GDD.md`，本文件与其冲突时以 GDD 为准。

## 项目概述

**Genesis Valley / 创世纪谷（创世谷）**：一款 PC 端上帝视角模拟建造游戏。玩家塑造一个温暖的微缩山谷、召唤居民，观察他们根据地形、水源和资源建立村庄，并可最多 4 人合作共同干预这个过程。设计参考：Tiny Glade（舒适构图与创造反馈）、WorldBox（生命观察）、Black & White（神明干预感）、Dwarf Fortress（个人事件记录）。

- **引擎/技术**：团结引擎（Tuanjie Engine，Unity 中国版）、C#、URP 渲染管线
- **目标平台**：Windows PC，后续 Steam Demo
- **制作方式**：个人或小团队，使用 AI Coding Agent 辅助实现
- **V1 核心范围**：500×500 米世界、初始 10 位居民（常规 20～50 人）、最多 4 人合作（房主运行模拟）、约 30 分钟完整发展循环、存档/载入
- **当前状态（0.9.0）**：单机 V1 玩法、美术、音频、UI、存档已在 `GenesisValley/Assets/Game/` 实现（约 2.2 万行 C#），第三方 CC0 素材在 `Assets/ThirdParty/`；四人联机未实现。详见 `PROJECT_STATUS.md`，首次打开按 `docs/SETUP.md` 运行菜单「Genesis Valley › 一键配置」。

## 目录结构

| 路径 | 说明 |
|---|---|
| `Genesis_Valley_V1_GDD.md` | ★ 权威设计文档（V1.1，2026-10-13）：玩法、美术、技术、资产与交付的完整规格，共 22 节 |
| `GenesisValley/` | 团结引擎工程（已创建：1.10.4 / 2022.3.62t16，URP 14.2.0-t1 空白模板），含 Assets、Packages（manifest+packages-lock）、ProjectSettings、Library；游戏代码将按 GDD 第 10 节组织进 `Assets/Game/` |
| `创世谷：多人共建山谷村落.png` | AI 生成的视觉目标参考图（多人共创村庄全景 + UI 草案，对应 GDD 第 19 节图 10 方向） |
| `暮色河岸灯火.png` | AI 生成的视觉目标参考图（黄昏河岸村庄夜景 + UI 草案，对应 GDD 第 19 节图 09 方向） |
| `.codely-cli/settings.json` | Codely CLI 配置（模型选择等） |

两张 PNG 是 AI 生成参考图而非实机截图，仅用于讨论风格、构图与发展阶段；不可从图片推断模型拓扑、许可、实际性能或功能完成度。

## GDD 章节速查

| 节 | 内容 | | 节 | 内容 |
|---|---|---|---|---|
| 1-2 | 游戏定位与 V1 完成定义 | | 11 | 地形/河流/植被实现 |
| 3 | 首次进入到 30 分钟体验脚本 | | 12 | 寻路与动态世界 |
| 4 | 操作、工具与造物约束 | | 13 | 程序化建筑与道路 |
| 5 | 模拟数值与经济初始表 | | 14 | 四人联机：Host Authority 与同步规则 |
| 6 | 村民 Utility AI 与 VillagePlanner | | 15 | 存档、错误恢复与关键测试清单 |
| 7 | 美术风格规范、调色板与验收 | | 16 | 性能目标与排查顺序 |
| 8-9 | 素材来源与 Blender→引擎流水线 | | 17 | 制作路线（阶段 A～E）与验收 |
| 10 | 技术架构与工程组织 | | 18 | AI Coding Agent 使用规范 |
| 19 | 十张参考图用途与生图规范 | | 20-22 | 风险降级、开工清单、官方资料 |

## 技术架构（GDD 第 10 节，已落地；Networking/ 尚未创建）

`Assets/Game/` 下分为：`Core/`（时钟、ID、命令、事件、配置）、`World/`（高度、水、生态、空间查询）、`Simulation/`（需求、任务、库存、聚落规划）、`Buildings/`、`Presentation/`、`UI/`、`Networking/`、`Persistence/`、`Editor/`、`Tests/`、`Art/ Configs/ Scenes/`。

核心约定：
- `WorldState` 是权威世界数据；`CommandProcessor` 验证玩家命令；`SimulationRunner` 固定步推进（拟定 10Hz）；`VillagePlanner` 只发布任务、不直接加资源；`TaskBoard` 管理领取与预约；`InventorySystem` 执行原子库存转移
- 所有实体有稳定 ID，所有跨实体关系存 ID，不存场景对象引用；存档用 DTO + schemaVersion + 配置 Hash，不序列化场景对象
- 普通 C# 数据结构起步，不引入 ECS；仅 Profiler 证明热点后才考虑 Jobs/Burst，并先确认版本兼容
- 寻路：自建 2D 通行网格 + A*（约 2 米/格），全项目只保留一条寻路实现
- 联机：房主权威（Host Authority）+ 命令/事件与状态快照双通道；先小工程验证 Netcode for GameObjects 在锁定团结版本的兼容性，禁止同时接入多个网络库
- 第三方素材放 `Assets/ThirdParty`（不原地改动），批准资产导出到 `Assets/Game/Art`；需保留资产台账（作者、许可、版本、Hash）

## 构建与运行

- **编辑器**：团结引擎 1.10.4（内部版本 2022.3.62t16），安装于 `D:\Tuanjie\Editor\2022.3.62t16`；工程已锁定版本（`ProjectSettings/ProjectVersion.txt` + `Packages/packages-lock.json` 均已生成，GDD 开工清单第 1 项完成）
- **打开工程（GUI）**：`tuanjie.exe open 'D:\Genesisi valley(unity版）\GenesisValley'`
- **batchmode（构建/自动化验证）**：
  `Start-Process -FilePath 'D:\Tuanjie\Editor\2022.3.62t16\Editor\Tuanjie.exe' -ArgumentList '-batchmode','-quit','-projectPath','D:\Genesisi valley(unity版）\GenesisValley','-logFile','<日志绝对路径>' -Wait -PassThru`
- **本机已知坑**：
  - PowerShell 用 `&` 调 GUI 程序立即返回、`$LASTEXITCODE` 不更新 → 必须 `Start-Process -Wait -PassThru` 取真实退出码
  - batchmode 内部报错退出码也可能是 0 → 成败要 grep 日志标记，不能只看退出码
  - 编辑器 GUI 打开工程时持有 `Temp/TuanjieLockfile` → 同时跑 batchmode 会因锁冲突退出码 1（非故障），需先关 GUI 编辑器或等其完成
  - 工程路径含空格与全角括号 → 参数必须逐项传入 `-ArgumentList`，不要手工拼命令行字符串
  - C 盘空间长期紧张 → 大文件（安装包、日志、缓存）一律放 D 盘
- **测试**：`Assets/Game/Tests/` 尚未建立；关键用例清单见 GDD 第 15 节（库存守恒、材料不复制、同树不重复砍、存档前后 Hash 一致、晚加入得到同一世界等）
- **Windows 可执行构建**：尚未配置；GDD 阶段 A 要求当天能构建出可执行程序

## 开发约定要点（从 GDD 提炼）

- 尺度：1 引擎单位 = 1 米；成人高 1.65～1.8 米；建筑模块 2 米基准网格，装饰可半米级偏移
- 美术：固定调色板（草地 #84976A、屋顶 #A76249 等，见 GDD 7.1），材质统一映射为草/树叶/木/石/墙/瓦六类；每轮验收保存全景/中景/近景三个固定镜头
- 河流不做真实流体，但湿度、取水点、捕鱼、通行阻断必须真实进入模拟；第一阶段只打通食物、木材、水三条真实链路
- 撤销只反演可安全反演的地形/植被编辑；已被消费的资源不跨时间倒流
- 优先级铁律：一次改造能被理解 > 村民真实执行任务 > 聚落能维持 > 画面统一 > 存档可靠 > 合作一致；天气、动物、宗教、战斗、科技等在此之后

## AI Agent 工作规范（GDD 第 18 节）

- **GDD 是项目规格；当前完成状态应写入独立的 `PROJECT_STATUS.md`（尚未创建，首次动工时建立）**，不与 GDD 混写
- 每次只做**一个**有明确输入、输出与验收的任务；不得将"任务完成"定义为写完脚本——需构建、运行、报告结果
- 实现前先读取项目现有结构与锁定版本，复用已有相机/命令等系统；若 API 不兼容，先做最小替代验证再改主工程
- 美术改造必须先看固定三镜头截图，不能仅凭代码猜观感；不得用生图替代实机场景验证
- 系统边界可以分工，但多个 Agent 不得同时修改库存和网络协议；每个变更保持可回滚，先最小纵切再扩大范围

## Codely Structured Memories

### User
- [2026-10-15 03:19:50] 本机C盘容量小(199GB)且长期紧张，2026-10-15大清理后空闲约22GB（已关闭休眠、清回收站/更新缓存/AI工具缓存）。大型生成资产、模型缓存、编辑器安装包应放D盘，避免向C盘（含用户Temp）写入大文件。

### Feedback
- [2026-10-15 03:21:38] 本机用 `tuanjie.exe install <semver>` 装编辑器不可靠：先因 C 盘空间校验(需3.86GB)直接 download_failed，空间清理后又报 INSTALL_ERROR（Hub 日志在 %APPDATA%\TuanjieHub\logs\info-log.json，CLI 仍显示 Successful，不可只看退出码）。可靠路径：让 CLI 下载完安装器后，把 TuanjieSetup64-<ver>.exe 挪到 D 盘，用 Start-Process -Verb RunAs -Wait -PassThru 手动执行 `/S /D=<安装目录>` 静默安装，完成后 list-installed 能自动识别。
- [2026-10-15 18:57:07] 本机 run_shell_command 用的是 PowerShell 5.1：`Start-Process -ArgumentList` 各项**不会自动加引号**，含空格/全角字符的路径会被编辑器截断（-projectPath 实际只收到 "D:\Genesisi"），表现为 batchmode 秒退退出码 1 且 -logFile 指定日志不存在。正确写法：拼一个单字符串内嵌双引号传给 -ArgumentList，如 `$argline = '-batchmode -quit -projectPath "' + $proj + '" -executeMethod X.Y.Z -logFile "D:\Tuanjie\gv.log"'; Start-Process $editor -ArgumentList $argline -Wait -PassThru`。勿用数组逐项传含空格路径（5.1 无引号，pwsh 7.3+ 才自动加）。
- [2026-10-15 19:05:08] 1.10.4 编辑器是绕过 Hub 手动静默安装的，根目录缺 modules.json，导致 `tuanjie.exe install-modules 1.10.4 --list` 报 "undefined is not an object"。修复：从 2022.3.61t14 的 modules.json 提取所需条目（UTF-8 无 BOM，PS5.1 须用 [IO.File]::ReadAllText 读，否则 CJK 名损坏 JSON 解析失败）写入 2022.3.62t16\modules.json 再走官方 CLI 安装。语言包条目 URL（new-translate.cdn.unity.cn/v1/live/54/2022.3/zh-hans 等）不含 t 补丁号，跨版本通用；平台模块 URL 含具体 tXX 版本，跨版本复用清单会装错安装器。简体中文包 2026-10-15 已装（Editor\Data\Localization\zh-hans.po）。

### Project
- [2026-10-15 18:56:55] 2026-10-15：GenesisValley 工程锁定团结引擎 1.10.4（2022.3.62t16），编辑器装于 D:\Tuanjie\Editor\2022.3.62t16。仓库与工程于当日重组进 Genesisi-valley-unity-/ 子目录（独立 git）。v0.9.0 单机版已交付并完成一键配置+首次导入，commit 0819971 提交全部生成 .meta 固定引用。另注意：`tuanjie.exe projects create` 会自动启动编辑器 GUI 并持有 Temp/TuanjieLockfile——GUI 开着时 batchmode 确实抢不到锁，但 2026-10-15 晨 batchmode 失败的主因是 PowerShell 5.1 的 -ArgumentList 不自动加引号（见 Feedback）。



### Reference
