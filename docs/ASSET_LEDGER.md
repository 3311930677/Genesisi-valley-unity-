# 素材台账 Asset Ledger

GDD §8 要求：每个第三方素材记录作者、来源、许可、版本、Hash。所有素材均可商用。
逐文件 SHA-256 见 [ASSET_HASHES.sha256](ASSET_HASHES.sha256)（路径相对 `GenesisValley/`，共 509 个文件）。

## 第三方素材（`Assets/ThirdParty`，未在原地修改模型；贴图做了缩放/格式转换）

| 包 | 作者 / 来源 | 许可 | 版本 | 原始压缩包 SHA-256 | 在项目中的内容 |
|---|---|---|---|---|---|
| Medieval Village MegaKit (Standard) | Quaternius · quaternius.itch.io/medieval-village-megakit | CC0 1.0 | 免费 Standard 版 | `e60dea67c10f30dccccfbff92a7933f5ea5cfe99be0e2a0fa5118cceabeec5c4` | 176 个模块化建筑 FBX（墙/屋顶/门窗/楼梯/道具），7 套砖石木瓦贴图（BaseColor 2K + Unity 法线 + URP MaskMap） |
| Stylized Nature MegaKit (Standard) | Quaternius · quaternius.itch.io/stylized-nature-megakit | CC0 1.0 | 免费 Standard 版（68/116 模型） | `298f6732b872e4cf7b30e6e7abf9641c7f6dc6b326df37ac089533ed7e3d58c9` | 68 个树/灌木/草/花/蘑菇/岩石 FBX，树皮与树叶贴图；`Leaves_TwistedTree_Olive.png`、`Leaves_Bush.png` 为重新调色的衍生图 |
| Universal Base Characters (Standard) | Quaternius · quaternius.itch.io/universal-base-characters | CC0 1.0 | 免费 Standard 版 | `fdbf1804c90dfc1ea03e992bff7da2dfd1a79318e13270a660180f9308455f40` | 男/女基础身体、8 种发型/眉毛/胡子、皮肤/头发/眼睛贴图 |
| Modular Character Outfits – Fantasy (Standard) | Quaternius · quaternius.itch.io/modular-character-outfits-fantasy | CC0 1.0 | 免费 Standard 版 | `c3468b18871cc8c8f05ab14df7712baf22cb9f389cbd870babf130e595187f70` | 农民 / 游侠两套服装的 20 个模块（身体、手臂、腿、鞋、兜帽、护肩）与贴图 |
| Universal Animation Library (Standard) | Quaternius · quaternius.itch.io/universal-animation-library | CC0 1.0 | 免费 Standard 版 | `cc73fc4e495b82958207316596317a3f40b9fa38065bde1027937452da537724` | `UAL1_Standard.fbx`，43 段动画（走/跑/待机/砍/捡/坐/庆祝…），导入为人形重定向 |
| 地面 PBR 贴图 ×7 | Poly Haven · polyhaven.com（leafy_grass、forrest_ground_01、brown_mud_leaves_01、rocky_trail、aerial_rocks_02、river_small_rocks、muddy_tracks） | CC0 1.0 | 2K 下载后缩放并调色 | 见逐文件 Hash | `T_Ground_*_D/_N.jpg`（对照表见 `PolyHaven/License.txt`） |
| HDR 天空 | Poly Haven · kloofendal_48d_partly_cloudy_puresky | CC0 1.0 | 2K HDR | 见逐文件 Hash | `T_Sky_PartlyCloudy.hdr` |
| Impact Sounds 1.0 | Kenney · kenney.nl | CC0 1.0 | 1.0 | `029d734af1582474edf3a694d1b0cebc97c1c152f2f39fa34d4c2bafc5de77f8` | 木/板/金属撞击、脚步声 |
| Interface Sounds | Kenney · kenney.nl | CC0 1.0 | 1.0 | `f2193d072726d6758a5f7871b2dcc54dcce0d5c35c6f0a62f92549b327c81232` | UI 点击/确认/错误/返回 |
| RPG Audio | Kenney · kenney.nl | CC0 1.0 | 1.0 | `6dbeaf8544da958d8f2adcb4a4a4b76c1ade34a05f8ab9edccd327da7375f38b` | 砍伐、开关门、布料、木头吱呀 |
| Noto Sans CJK SC Medium / Noto Serif CJK SC Bold | Google / Adobe · github.com/notofonts/noto-cjk | SIL OFL 1.1（`Fonts/OFL.txt`） | 2.x（系统包 google-noto-cjk） | 见逐文件 Hash | 子集化为 GB2312 全部汉字 + ASCII + 常用标点（7560 字），界面 / 标题字体 |

共选用 Kenney 音效 70 个（`Kenney/Audio`），其余未入库。

## 原创 / 程序化生成素材（`Assets/Game/Art`，本项目自有）

| 素材 | 生成方式（脚本在 `tools/`） |
|---|---|
| 43 个工具/状态图标 `Icons/Icon_*.png` | `gen_icons.py`（pycairo 矢量绘制，128 px） |
| 特效与 UI 贴图 `Textures/T_FX_* / T_UI_*`、水面波纹法线 `T_Water_Ripple_N` | `gen_tex.py`（可平铺噪声 / 解析形状） |
| 3 首配乐（晨 / 河 / 暮，约 135 秒，可循环） | `gen_audio.py music`（加法合成 + 混响，原创旋律） |
| 6 条环境声循环（白天、夜晚虫鸣、风、河流、篝火、地形隆隆声） | `gen_audio.py amb` |
| 13 个玩法音效（召唤、河流生成、种树、倒树、里程碑等） | `gen_audio.py sfx` |
| 应用图标 `Branding/GV_AppIcon.png` | `gen_icon.py` |
| 运行时材质、地形图层、村民预制体、动画控制器、主场景 | 编辑器菜单「一键配置」从上述素材生成（`Assets/Game/Art/Generated`，可随时重建，不必手改） |

## 规则

- 新素材先放 `Assets/ThirdParty/<作者>/<包名>/`，保留原许可文件，并在本表补一行。
- 不允许使用许可不明、"仅限个人使用"或需要署名但无法满足署名要求的素材。
- CC0 不强制署名，但游戏制作人员名单中仍建议致谢 Quaternius、Poly Haven、Kenney。
