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

> 本 exe 未签名；如果 SmartScreen 提示，选择“更多信息 → 仍要运行”。

快捷键：

| 快捷键 | 作用 |
| --- | --- |
| `Ctrl+Shift+F9` | 显示/隐藏 Overlay |
| `Ctrl+Shift+F10` | 立即刷新数据 |
| `Ctrl+Shift+F11` | 重新加载配置 |
| `Ctrl+Shift+F12` | 保存当前客户端截图到 `captures`（用于适配/排障） |

托盘菜单：

- 预览 Overlay（用于校准位置，不需要 LCU）；
- 刷新数据；
- 重新加载配置；
- 打开配置目录；
- 退出。

## 配置

首次运行会生成：

```text
%APPDATA%\AramOverlay\config.json
```

主要字段：

```json
{
  "DataUrl": "https://raw.githubusercontent.com/Luckking77/aram-data/main/data/latest.json",
  "BenchFirstX": 0.289,
  "BenchPitchX": 0.0456,
  "BenchY": 0.066,
  "AllyX": 0.038,
  "AllyFirstY": 0.173,
  "AllyPitchY": 0.089,
  "BadgeFontRatio": 0.0115,
  "ShowAllyRows": true,
  "HideWhenNotForeground": true
}
```

坐标都是相对客户端客户区的比例（0–1），与分辨率/DPI 无关。若位置有偏移，用托盘「预览 Overlay」对照调整：

- `BenchFirstX`：第一个席位中心 X；
- `BenchPitchX`：席位间距 X；
- `BenchY`：席位胜率 Y；
- `AllyX`：队友头像胜率 X；
- `AllyFirstY` / `AllyPitchY`：队友第一行 Y / 行距。

改完保存，托盘「重新加载配置」或 `Ctrl+Shift+F11`。

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
