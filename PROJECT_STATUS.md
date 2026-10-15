# 项目状态（V1 · 0.9.0 单机版）

> 更新：本次交付。对照 `Genesis_Valley_V1_GDD.md`。
> ⚠ 代码在本地离线工具链里编译通过（游戏 + 编辑器代码 0 错误），模拟层 19 项测试通过；**还没有在团结编辑器里实机运行过**，第一次打开时请按 `docs/SETUP.md` 运行「一键配置」，有报错请把 Console 第一条红字发回来。

## 已完成

| GDD | 内容 | 位置 |
|---|---|---|
| §2 世界 | 500×500 m 高度场、种子化生成（外围山脊 + 中心平原 + 浅洼地），确定性 | `World/`、`Simulation/WorldGenerator.cs` |
| §4 工具 | 抬升 / 降低 / 平整笔刷、河流（实时合法性校验与原因提示）、森林、召唤、桥梁意向；每笔划一个事务，可撤销 | `Presentation/ToolController.cs`、`Simulation/CommandProcessor.cs`、`UndoHistory.cs` |
| §5–6 模拟 | 10 Hz 固定步；需求（饥饿/疲劳）、Utility AI、任务板预约、原子库存、VillagePlanner 选址建房 / 仓库 / 农田 / 渔屋 / 桥、人口迁入 | `Simulation/` |
| §11 生态 | 河流湿润度扩散、农田依赖湿润、树木生长与伐木再生 | `Simulation/Ecology.cs`、`World/WaterMap.cs` |
| §12 寻路 | 2 m 通行网格 + A*，地形 / 水 / 建筑变化后增量刷新，桥梁连通两岸 | `World/PassabilityGrid.cs`、`Pathfinder.cs` |
| §13 建筑 | Medieval Village 模块化蓝图：3 种住宅、3 种仓库、渔屋 + 栈桥、拱桥；分阶段施工动画、炊烟、夜间窗光 | `Presentation/Blueprints/`、`World/BuildingPresenter.cs` |
| §7 美术 | Quaternius 低模风格统一材质；7 层 PBR 地形；自定义天空（日夜四时段、星空）、水体（深度着色、岸线泡沫、波纹法线）、植被风动 + 顶点 AO；URP 后期（调色、Bloom、暗角、微缩景深）；三档画质 | `Shaders/`、`Presentation/Environment/` |
| 村民 | 男女模块化角色（5 套服装配色、发型、兜帽、胡子随机），人形重定向动画（走、跑、待机、交谈、砍树、拾取搬运、蹲下劳作、跪地修建、坐下、跳舞庆祝），按任务手持工具 | `Presentation/Villagers/`、`Editor/GVCharacterBuilder.cs` |
| 声音 | 3 首原创配乐按昼夜交叉淡入；风 / 河 / 篝火 / 夜虫环境声按镜头位置混音；70+ 玩法与界面音效 | `Presentation/Audio/AudioDirector.cs` |
| UI | 标题界面（新建 / 继续 / 设置）、HUD（资源、人口、时间与速度）、工具栏、检视面板（村民 / 建筑详情）、目标与里程碑、村庄故事流、存档 / 读档窗口、设置（画面 / 声音 / 操作 / 按键重绑定）、暂停菜单、拍照模式、调试叠加 | `UI/` |
| §15 存档 | DTO + schemaVersion + 配置 Hash；原子写入 + 备份恢复；读档后位级确定性 | `Persistence/SaveGame.cs` |
| 工程化 | 一键配置（导入规则 → 材质 → 角色 → 美术目录 → URP 设置 → 主场景）、一键构建、批处理入口、配置报告、素材台账与 Hash | `Editor/`、`docs/` |

## 未完成 / 已知差距

| 项 | 说明 |
|---|---|
| **§14 四人合作联机** | 未实现。架构已为它预留：所有玩家操作都是可序列化的 `Command`，经 `CommandProcessor` 验证，模拟确定性有测试保证——接入 Netcode for GameObjects 仍需实现连接管理、命令转发、状态同步、掉线恢复和一致性校验。建议先按 GDD 做 NGO 兼容性小工程。 |
| 实机验证 | 没有在团结编辑器中实际运行 / 截图 / 测帧率；材质参数、灯光强度、镜头节奏可能需要在编辑器里微调。 |
| 生育 / 家庭事件 | 只有迁入，没有出生。 |
| Steam / 本地化 | 只有简体中文界面。 |
| `.meta` 文件 | 新增代码与素材的 `.meta` 首次导入后由引擎生成；已有工程文件的 `.meta` 保留，请将新增的一并提交。 |

## 建议下一步

1. 打开工程 → 一键配置 → 运行，按 GDD §7.4 美术验收逐条过一遍（镜头 45° 远景、黄昏河岸、夜景灯火）。
2. 用 Profiler 看 30 分钟后的村庄（GDD §16：目标 1080p 60 fps）。
3. 阶段 D：联机原型。
