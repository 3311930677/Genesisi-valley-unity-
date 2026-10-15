# 安装、配置与打包

## 0. 环境

- 团结引擎 **1.10.4（2022.3.62t16）**，工程已锁定版本，不要升级 URP / 引擎。
- Windows 10/11，建议独显；帧率和最低配置尚未在引擎里实测，低配设备请先选择 Performant 档位。

## 1. 放入文件

把交付包解压到仓库根目录（与 `Genesis_Valley_V1_GDD.md` 同级），覆盖合并即可。完成后应存在：

```
GenesisValley/Assets/Game/            ← 代码、着色器、生成美术、编辑器工具
GenesisValley/Assets/ThirdParty/      ← CC0/OFL 第三方素材（约 91 MB）
docs/  tools/  README.md  PROJECT_STATUS.md
```

> 注意：`.meta` 文件由引擎在首次导入时生成，新增素材与代码不含 `.meta`（原工程的已有 `.meta` 保留）。首次导入后请把生成的 `.meta` 一起提交到 Git，否则其他人拉取后引用会断。

## 2. 一键配置

1. 打开工程，等待导入完成（第一次约 3–8 分钟；导入规则由 `GVAssetPostprocessor` 自动设置：人形骨骼、动画循环、法线/遮罩贴图线性空间、植被贴图覆盖率 Mip、音乐流式加载、中文字体等）。
2. 弹窗选 **立即配置**，或菜单 **Genesis Valley › 一键配置 (Setup Everything)**。它会依次：
   1. 创建共享材质（`Assets/Game/Art/Generated/Materials`），并把所有 FBX 的内嵌材质重映射到这些材质；
   2. 生成 7 个地形图层、地形材质、HDR 天空、水体材质；
   3. 生成村民动画控制器和男女两个模块化村民预制体（服装 / 发型 / 兜帽 / 胡子随机组合）；
   4. 填写美术目录 `Assets/Game/Resources/GV_ArtCatalog.asset`，并校验所有建筑蓝图用到的模型都存在；
   5. 调整 URP 三个画质档位（深度纹理、HDR、阴影级联、SSAO）和玩家设置（产品名、图标、1920×1080 全屏窗口）；
   6. 生成主场景 `Assets/Game/Scenes/Main.scene` 并设为唯一构建场景。
3. 结束后会显示报告；随时可以从 **Genesis Valley › 查看配置报告** 再打开（`Assets/Game/Art/Generated/SetupReport.md`）。

配置是**幂等**的：之后替换/新增素材，再运行一次即可。

## 3. 运行

菜单 **Genesis Valley › 运行游戏 ▶**（会打开主场景并进入 Play）。也可以手动打开 `Assets/Game/Scenes/Main.scene` 后按 ▶。

如果没有运行配置（找不到美术目录），游戏仍可启动，但所有模型会退化为程序化占位几何体——这是故意保留的兜底，不是正式画面。

## 4. 打包 Windows

- 菜单：**Genesis Valley › 构建 Windows 版本** → `GenesisValley/Builds/Windows/GenesisValley.exe`
- 命令行（PowerShell，必须 `Start-Process -Wait -PassThru` 才能拿到真实退出码）：

```powershell
$editor = 'D:\Tuanjie\Editor\2022.3.62t16\Editor\Tuanjie.exe'
$proj   = 'D:\Genesisi valley(unity版）\GenesisValley'
# 只配置
Start-Process $editor -ArgumentList '-batchmode','-quit','-projectPath',$proj,'-executeMethod','GenesisValley.EditorTools.GVSetup.SetupBatch','-logFile',"$proj\Logs\setup.log" -Wait -PassThru
# 配置 + 构建
Start-Process $editor -ArgumentList '-batchmode','-quit','-projectPath',$proj,'-executeMethod','GenesisValley.EditorTools.GVSetup.BuildBatch','-logFile',"$proj\Logs\build.log" -Wait -PassThru
```

退出码 0 = 成功，1 = 失败（看日志里的 `✖` 行）。

## 5. 测试

- 编辑模式测试：**Window › General › Test Runner › EditMode**，`Assets/Game/Tests/EditMode/GameplayTests.cs`（19 项：世界生成确定性、库存原子转移、首座房屋建造、地形笔刷/平整/撤销、河流与桥梁、寻路绕行、存档往返与位级确定性、30 分钟无人值守村庄可持续增长等）。

## 6. 存档位置

`%USERPROFILE%\AppData\LocalLow\Genesis Valley Studio\创世谷 Genesis Valley\saves\*.gvsave`
（菜单 **Genesis Valley › 打开存档文件夹**）。画面/声音/按键设置保存在 PlayerPrefs，不进存档。

## 7. 常见问题

| 现象 | 处理 |
|---|---|
| 模型是粉红色 | 着色器还没编译完或材质没重映射 → 再运行一次「一键配置」 |
| 村民 T-pose / 不动 | 动画 FBX 未按人形导入 → 右键 `Assets/ThirdParty/Quaternius/Animations` → Reimport，然后再运行配置 |
| 中文显示为方框 | 字体被改动；确认 `Assets/ThirdParty/Fonts/*.otf` 存在（子集包含 GB2312 全部 6763 个汉字） |
| 水面没有深浅变化 | 当前 URP 资源没开深度纹理 → 运行配置会自动打开；或在 URP 资源里勾选 Depth Texture |
| 帧率低 | 设置 › 画面 › 画质预设选「流畅」；关闭微缩景深；降低植被密度 |
| 构建失败 | 看 `Logs/build.log` 里第一个 `error CS` / `✖`；最常见是没有安装 Windows Build Support 模块 |
