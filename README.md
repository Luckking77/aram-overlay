# ARAM Overlay（P0）

Windows 选人页胜率 Overlay：把 101 国服「海克斯大乱斗」英雄胜率直接叠在 League 客户端的英雄头像上。

- 顶部「可用」最多 10 个槽位；
- 左侧 5 行队伍（含自己）；
- 不注入客户端、不读内存、不自动选人/换英雄/设技能；
- 数据来源：腾讯 101 数据站（`101.qq.com`），通过 `aram-data` 仓库每 5 小时检查更新；
- 非商用项目；数据版权/条款归腾讯/101。

## 运行要求

- Windows 10/11 x64；
- .NET Framework 4.8（Windows 10/11 自带）；
- League 客户端正在运行，并进入 **海克斯大乱斗选人**。

## 使用

1. 解压后双击 `AramOverlay.exe`；
2. 托盘出现 `ARAM Overlay` 图标；
3. 启动 League 客户端，进入海克斯大乱斗选人；
4. 选人阶段会自动显示胜率；离开选人/进入游戏自动隐藏。

队友行会在名字后追加两行：

```text
胜率47.9% · 近50场 52.3% · 3.2KDA
海斗分：60 · 人上人
```

数据通过 LCU 只读拉取该队友最近的海斗对局（queue 2400），最多 50 场，缓存 10 分钟。

海斗分是本项目自研的 0–100 分：

- 50 分为基准；
- 胜率权重最高，KDA 次之；
- 不足 50 场按场次扣分，少于 10 场封顶 55；
- 评价：`夯`（90+）/ `顶级`（75+）/ `人上人`（60+）/ `NPC`（35+）/ `拉完了`（<35）。

> 本 exe 未签名；如果 SmartScreen 提示，选择“更多信息 → 仍要运行”。

快捷键：

| 快捷键 | 作用 |
| --- | --- |
| `Ctrl+Shift+F9` | 显示/隐藏 Overlay |
| `Ctrl+Shift+F10` | 立即刷新数据 |
| `Ctrl+Shift+F11` | 重新加载配置 |
| `Ctrl+Shift+F12` | 保存当前游戏/客户端截图（含 Overlay，用于校准/排障） |

托盘菜单：

- 预览 Overlay（30 秒，选人阶段显示真实 badge，用于校准）；
- 刷新数据；
- 重新加载配置；
- 打开配置目录；
- 退出。

## 配置

首次运行会在 **exe 同目录**生成：

```text
AramOverlayData\config.json
AramOverlayData\captures\
AramOverlayData\overlay.log
```

如果 exe 所在目录不可写（例如放在 Program Files），会自动回退到：

```text
%APPDATA%\AramOverlay\
```

进入选人阶段还会自动保存一张**不含 Overlay** 的原始客户端截图（`auto-client-*`），用于校准；手动截图/托盘截图会包含 Overlay。

主要字段：

```json
{
  "DataUrl": "https://raw.githubusercontent.com/Luckking77/aram-data/main/data/latest.json",
  "OcrCommand": "",
  "BenchFirstX": 0.292,
  "BenchPitchX": 0.0315,
  "BenchY": 0.090,
  "AllyX": 0.180,
  "AllyFirstY": 0.386,
  "AllyPitchY": 0.093,
  "BadgeFontRatio": 0.0165,
  "ShowAllyRows": true,
  "HideWhenNotForeground": true
}
```

坐标都是相对客户端客户区的比例（0–1）。顶部席位按客户端宽度自动选择四档布局（1920/1600 实测，1280/1024 外推），`BenchFirstX`、`BenchPitchX` 仅作为旧版/预览回退。队友行使用 `AllyX`、`AllyFirstY`、`AllyPitchY`。若位置有偏移，用托盘「预览 Overlay」对照调整：

- `BenchFirstX`：第一个席位中心 X；
- `BenchPitchX`：席位间距（相对宽度）；
- `BenchY`：席位胜率 Y（头像下方）；
- `AllyX`：队友名字后的胜率 X；
- `AllyFirstY` / `AllyPitchY`：队友第一行 Y / 行距。

改完保存，托盘「重新加载配置」或 `Ctrl+Shift+F11`。

## 海克斯适配 Overlay（A+2）

选人阶段记住当前英雄；进入游戏后，每 1.2 秒截取游戏客户区，按 HexBox 预设的 3 个海克斯卡标题区域做 OCR，匹配到至少 2 张卡时在卡片下方显示：

```text
顶级 76
```

- 数据：`data/augment-fit.json`（国服整体海克斯胜率 + 国服英雄选取率，自研适配分）；
- 卡牌区域：1920 用实测区域，2560×1440 用 HexBox 预设区域；评分放在卡片内底部空白处，不压标题/描述；OCR 匹配到 2 张以上会自动保存 `augment-raw-*.png` 和 `augment-overlay-*.png`；
- 刷新：每 500ms 扫描；优先使用常驻 `worker`（模型不重复加载），失败 3 次后禁用并回退到 3 张卡一次批量 `--images`；
- OCR：外接命令，默认查找 exe 同级 `ocr\hexbox-ocr.exe`；也可配置：

```json
"OcrCommand": "C:\\path\\to\\hexbox-ocr.exe"
```

- 评分/来源/许可与逆向说明见 [OCR-SOURCES.md](OCR-SOURCES.md)；
- **OCR 后端不随包分发**，公开分发前必须替换为开源实现或取得授权；
- 独占全屏可能无法置顶，建议无边框/窗口化。

## 队友近50场分析

- 数据源：LCU `lol-match-history/v1/products/lol/{puuid}/matches`，只读；
- 过滤 `queueId=2400`（海克斯大乱斗）；
- 统计：场次、胜率、KDA、场均死亡；
- 样本分档：`<10` 样本少、`<30` 样本中、`>=30` 样本足；
- 计算 Wilson 95% 胜率下界用于内部置信判断；
- 只显示在队友名字后，不弹独立面板。

> 这是本项目的自研统计，**不是 Hexdata 的原创海斗分**。Hexdata 海斗分需要登录/授权，当前没有公开 API，未接入。

## 数据

- 仓库：`https://github.com/Luckking77/aram-data`
- 客户端读取：`data/latest.json`
- 服务端：GitHub Actions 每 5 小时检查 101 最新日期；日期不变不提交；
- 客户端启动时读本地缓存/种子；后台刷新失败不影响显示。

## 构建

需要 Windows 自带的 .NET Framework 4.x 编译器：

```powershell
powershell -ExecutionPolicy Bypass -File build.ps1
```

生成 `AramOverlay.exe`。

## 限制

- 目前只覆盖选人页；游戏内海克斯 OCR 不在 P0。
- 依赖 LCU 判断 `ChampSelect`；若客户端未启动或 LCU 读取失败，Overlay 会隐藏。
- 101 接口无公开文档，字段/限制可能变化；请低频请求并保留缓存。
- 国服 TP/反作弊兼容性需在真实客户端验证；本项目不注入、不读内存、无全局键鼠钩子。
