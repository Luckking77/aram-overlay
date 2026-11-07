# OCR 与海克斯适配数据：来源、改写范围与限制

## 1. 本项目实际使用的数据

### 海克斯整体胜率/选取率（国服）

- 来源：腾讯 101 `fuwen_aram_rune_rank_v2`
- 日期：由 `scripts/build-augment-fit.mjs` 写入；当前生成日期见 `data/augment-fit.json`
- 字段：`augmentId`、国服整体 `winRate`、国服整体 `pickRate`
- 这是**国服整体**数据，不是某个英雄的胜率。

### 英雄 × 海克斯选取率（国服）

- 来源：ARAMGG client data（`data.dtodo.cn`）的 `champions/{id}.json`
- 字段：`stats.pickRate`（国服来源）
- **未使用** ARAMGG 的英雄 × 海克斯 `winRate`：其 `winRateSource=aramgg-client-upload`、`winRateRegion=WORLD`，不是国服胜率。

### 适配分（自研）

`build-augment-fit.mjs` 计算：

```text
championShare = 英雄该海克斯选取率 / 英雄全部海克斯选取率总和
overallShare  = 国服整体该海克斯选取率 / 国服全部海克斯选取率总和
lift           = championShare / overallShare

score = 50 + (国服整体胜率 - 50%) × 120 + log2(lift) × 8
```

- 评分 0–100；
- 评价：`夯` / `顶级` / `人上人` / `NPC` / `拉完了`；
- 这是**适配度**，不是 Hexdata 官方海斗分，也不是英雄胜率。

## 2. HexBox OCR 逆向观察（REA）

目标：

```text
C:\Users\50353\Desktop\dsh\_work\hexbox\ocr\hexbox-ocr.exe
sha256 ddbcc047f2e3311e010c2ee6a9e06f58902db32598cd185033c5dad1b1de19ab
```

REA Evidence：

- `open_binary`：PE x86_64，SHA256 同上；
- `inspect_artifact`：`ev_6105c177cca9a9c4927c1355d7233b66d47235a9eb74687177f8cf49b58a28cc`；
- 静态清单 `compiled-ocr-manifest.json`：Nuitka 4.2，依赖 RapidFuzz、mss、numpy、onnxruntime、pillow；编译模块 `augment_pipeline`、`preset_ocr`、`service`、`worker`；
- 模型：`ch_PP-OCRv4_rec.onnx`、`ch_PP-OCRv4_rec_int8.onnx`、`PP-OCRv5_mobile_rec.onnx`、`ppocr_keys_v1.txt`；
- 预设：`data/augment_presets.json` 给出 1920/2560/3840/5120 的 3 个海克斯卡标题矩形；本项目又按一张真实国服选卡截图（1920×1080）校准了标题区域和评分位置；
- 运行观察：`hexbox-ocr.exe service --image <png>` 返回 JSON：

```json
{"lines":[{"text":"小蜜蜂！！！","score":0.98}],"backend":"onnx-ppocr-rec"}
```

### 复用范围

- **复用**：卡牌区域预设坐标、OCR 输出结构、PP-OCR 模型/后处理思路；
- **未复制**：Nuitka 生成的 Python 字节码、HexBox 业务逻辑、云端接口、专有 UI；
- **不随本项目分发** `hexbox-ocr.exe` 及其 Python 运行时；
- 本项目的 `AugmentOcr.cs` 是 C# 进程桥，调用用户本机已有的 OCR 后端；`OCR-SOURCES.md` 记录来源。

## 3. 开源参考与许可

- PaddleOCR：<https://github.com/PaddlePaddle/PaddleOCR>（Apache-2.0）；PP-OCRv4/v5 模型与 `ppocr_keys` 字典；
- RapidOCR：<https://github.com/RapidAI/RapidOCR>（Apache-2.0）；预处理/CTC 解码思路；
- Tesseract：<https://github.com/tesseract-ocr/tesseract>（Apache-2.0）；作为可替换 OCR 后端的候选；
- HexBox / Hexdata：仅用于本地逆向与行为研究，不复制其专有代码或数据接口。

> 若要公开分发带 OCR 的版本，必须把 `hexbox-ocr.exe` 替换为上述开源引擎的独立实现，或取得 HexBox/Hexdata 的书面授权。

## 4. 集成方式

1. 将 `data/augment-fit.json` 放到 exe 的 `data\` 目录；
2. 在 `AramOverlayData\config.json` 设置：

```json
"OcrCommand": "C:\\path\\to\\hexbox-ocr.exe"
```

3. 启动 Overlay，进入海斗选人并选英雄；
4. 进入游戏后，程序每 1.2 秒截取游戏客户区，按预设 3 个标题区域 OCR；
5. 匹配到至少 2 个海克斯时，在 3 张卡下方显示 `评价 分数`；否则隐藏。

## 5. 限制

- 当前 OCR 后端是**用户本机外部命令**，不随包分发；
- 只支持预设的 1920×1080 布局按比例缩放；异形 UI/UI 缩放需要补充预设；
- 独占全屏（exclusive fullscreen）下 WPF 置顶窗口可能不可见，建议无边框/窗口化；
- 需要选人阶段启动过 Overlay，才能记住当前英雄；
- OCR 可能把非卡牌文字误识别；只有 2 张以上匹配才显示；
- 海克斯名称使用项目内 `augment-fit.json` 的国服名称，别名/错字需要补充映射。
