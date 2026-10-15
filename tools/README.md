# tools

素材预处理与程序化生成脚本（Python 3.11+，依赖 Pillow、numpy、opencv-python、pycairo、scipy）。
这些脚本是生成 `Assets/ThirdParty` 与 `Assets/Game/Art` 时实际使用的版本，脚本里的路径（`/data/...`）是当时的工作目录，复用前请修改顶部的路径常量。

| 脚本 | 作用 |
|---|---|
| `prep_thirdparty.py` | 从 Quaternius 原始压缩包挑选模型、缩放贴图、转换 Unity 法线、打包 URP MaskMap（R 金属 / G AO / A 光滑度）、重新调色树叶 |
| `tex_prep.py` | Poly Haven 地面贴图缩放 + 统一色调（使 7 种地面在同一调色板内） |
| `gen_tex.py` | 特效粒子、UI 笔刷/虚线/选择环、水面波纹法线 |
| `gen_icons.py` | 43 个界面图标 |
| `gen_icon.py` | 应用图标 |
| `gen_audio.py` | 配乐 / 环境声 / 玩法音效合成：`python3 gen_audio.py OUT_DIR [amb|sfx|music]` |
