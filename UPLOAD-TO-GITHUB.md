# 把 ARAM Overlay 上传到 GitHub，并让 AI 持续维护

> 当前代码目录：`C:\Users\50353\Desktop\dsh\aram-overlay`
> 数据仓库：`https://github.com/Luckking77/aram-data`（只放 101 快照，不要和代码混在一起）
> 建议：代码单独建一个仓库，例如 `aram-overlay`。

---

## 1. 先清理要提交的内容

代码目录里已经有 `.gitignore`，会忽略：

- `bin/`、`obj/`、`dist/`、`*.exe`；
- 本地调试文件（`hello.cs`、`demo*.png`、`render-test.png`、`AramOverlayConsole.exe`）；
- 运行日志/截图（`data/overlay.log`、`data/captures/`）。

保留：

- `*.cs`、`build.ps1`、`README.md`；
- `data/latest.json`（离线种子，107 KB，可选但建议保留）。

> 不要把 `%APPDATA%` 里的配置/日志提交；仓库里只放源码和种子。

---

## 2. 创建代码仓库

1. 打开 <https://github.com/new>；
2. Repository name：`aram-overlay`；
3. Visibility：Public（或 Private，见第 5 节）；
4. 不要勾 README（本地已有）；
5. Create repository。

得到地址：

```text
https://github.com/Luckking77/aram-overlay
```

---

## 3. 用 git 上传（推荐）

当前 `aram-overlay` 目录**已经初始化好 git**（分支 `main`，首个 commit `ce15ee7`），你只需要添加远端并 push：

```powershell
cd C:\Users\50353\Desktop\dsh\aram-overlay

git remote add origin https://github.com/Luckking77/aram-overlay.git
git push -u origin main
```

如果 remote 已存在，用：

```powershell
git remote set-url origin https://github.com/Luckking77/aram-overlay.git
git push -u origin main
```

如果提示登录：

- 用 GitHub 用户名 + **Personal Access Token** 作为密码；
- 或使用 GitHub Desktop / VS Code 的 GitHub 登录。

> DSH 沙箱里的 git 目前有 schannel/TLS 限制，可能无法直接 push；上面的命令请在你的正常终端跑。

---

## 4. 网页上传（不想装 git 时）

1. 新仓库 → **Add file → Upload files**；
2. 把 `aram-overlay` 里的源码/脚本/README 拖进去；
3. Commit。

缺点：`.gitignore`、目录结构、后续更新不如 git 方便；长期维护建议还是 git。

---

## 5. 发布 exe（可选）

源码仓库不建议提交 `dist/AramOverlay.exe`；用 **Releases** 放安装包：

1. 仓库 → **Releases → Draft a new release**；
2. Tag：`v0.1.0`；
3. 上传 `AramOverlay-v0.1.zip`；
4. Publish release。

这样用户下载 Release，而不是从源码里找 exe。

---

## 6. 上传后，怎么让 AI 直接维护

### 6.1 只读：给 URL 即可

仓库是 Public 时，我可以直接通过 GitHub API / raw 读取：

```text
https://github.com/Luckking77/aram-overlay
https://raw.githubusercontent.com/Luckking77/aram-overlay/main/...
```

我可以分析、对比、给补丁，但**不能写**。

### 6.2 推荐：本地 clone + 你 push

把仓库 clone 到当前工作区：

```powershell
cd C:\Users\50353\Desktop\dsh
git clone https://github.com/Luckking77/aram-overlay.git
```

之后：

- 我直接编辑 `C:\Users\50353\Desktop\dsh\aram-overlay` 里的源码；
- 我编译/测试；
- 你执行：

```powershell
cd C:\Users\50353\Desktop\dsh\aram-overlay
git add .
git commit -m "feat: adaptive layout"
git push
```

这是最安全的方式：**不需要把 GitHub 密码/PAT 交给 AI**。

### 6.3 让我直接 push：需要 fine-grained PAT

如果你希望我直接提交/推送，需要：

- 一个 **fine-grained PAT**；
- 只选 `aram-overlay` 仓库；
- 权限：`Contents: Read and write`、`Metadata: Read`；
- 有效期：7 天；
- 用完立即 revoke。

安全提醒：

- PAT 等同于密码；**不要长期贴在聊天里**；
- 聊天/日志可能保留 token；
- 建议用临时仓库/临时 token，做完就删。

> 如果提供 PAT，我可以走 GitHub REST API 提交（当前沙箱 git TLS 不稳定，API 更可靠）；但每次改动会产生一次 commit。

### 6.4 私有仓库

Private 仓库不能匿名读；要么：

- 给我 PAT（同上）；
- 或你把仓库临时设为 Public；
- 或只在本地 clone 后让我改，你 push。

---

## 7. 推荐日常维护流程

```text
你：git pull
我：改代码 + build + test
你：git add/commit/push
我：在下一轮 git pull 后继续
```

分支建议：

- `main`：稳定可运行；
- `dev`：我改功能；
- 每次改动一个 commit，message 写清楚（`feat:` / `fix:` / `chore:`）。

---

## 8. 数据仓库与代码仓库分开

- `aram-data`：只放 `data/latest.json` + `manifest.json` + Actions；
- `aram-overlay`：只放桌面端源码；
- 不要把 5 小时一次的数据 commit 混进代码历史。

---

## 9. 最短路径

1. 新建 `aram-overlay` 仓库；
2. 在正常终端 `git init/add/commit/push`；
3. 把仓库 URL 发我；
4. 我把代码 clone 到工作区，后续直接改；
5. 你 `git push`，或给我临时 PAT 由我推送。
