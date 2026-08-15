# DSH 启动器插件（dsh-launcher）

Windows 桌面启动器 + DeepSeek Harness（DSH）插件。双击 exe 或在聊天里输入 `/launcher` → **隐藏命令行**启动 DSH → 自动打开默认浏览器进入 `http://127.0.0.1:3080`。

- **加载动画**：用户绑定的表情绕**竖中轴（圆心）3D 旋转**，3 秒一次（转到 90° 侧面隐形时切换，等比缩放不拉伸）；淡蓝色虚线圆环绕图标旋转；完全透明背景，动画本体 ≤ 屏幕 10%。
- **设置页**：Web 设置页新增「启动器设置」，可配置工作目录、端口、网址、超时、启动方式、图标、表情目录、开启模式（目录字段带原生目录选择器）。
- **启动方式**：`pnpm dsh web`（主）/ `node` 直启 built CLI（备用）/ 自定义命令，主失败自动切备用。
- **端口占用**：`等待模式` 弹按钮让你选「打开 / 重启 / 关闭服务」；`直接开启` 模式直接打开网页。
- 纯代码 exe（约 34 KB，不含任何图片），桌面图标与加载动画表情由用户在设置页自行绑定本地素材。

---

## 这个包是什么

一个包 = **host 半边**（Node 侧 Cordis 插件，注册 `launcher` 设置命名空间、`/launcher` 命令、`/launcher/api` 读写通道，见 `plugin/index.js`）+ **client 半边**（浏览器侧设置页，见 `plugin/client.js`）+ **启动器 exe**（WPF，`src/Launcher.cs` 编译而来）。

包通过两处声明接入 DSH：

| 声明 | 作用 |
| --- | --- |
| `dsh.bundle.patch`（`cordis.patch.yml`） | 让 DSH 识别为**插件包**：`dsh plugin --profile <名> add <包名>` 一条命令安装并自动接线 |
| `dsh.client` + `exports["./client"]` | 让 web 客户端自动加载浏览器设置页 |

---

## 安装

### 0. 前提条件

- 已安装 DeepSeek Harness（`npm install -g @deepseek-ai/dsh`，或桌面应用）。
- 方法 A（推荐）需要 **pnpm**：`npm install -g pnpm`（或 `corepack enable`）。

### 1. 方法 A：一条命令安装（npm / git / tarball）

```bash
# npm（发布后）
dsh plugin --profile web add @dsh-external/dsh-launcher

# 从 GitHub / Gitee 直接安装
dsh plugin --profile web add github:<你的用户名>/dsh-launcher
dsh plugin --profile web add https://gitee.com/<你的用户名>/dsh-launcher.git

# 本地 tarball（先 pnpm pack 打包）
dsh plugin --profile web add ./@dsh-external-dsh-launcher-0.1.0.tgz
```

`dsh plugin add` 会自动完成三件事：在 `~/.dsh/profiles/web` 里用 pnpm 安装、把包名写进 `dsh.profile.bundles`、重启后按包内 `cordis.patch.yml` 挂载插件——**无需手改任何配置文件**。

其它 profile 同理，把 `web` 换成你的 profile 名。

### 2. 方法 B：本地源码安装

```bash
git clone <仓库地址>
dsh plugin --profile web add ./dsh-launcher/plugin
```

### 3. 重启并验证

重启 `dsh web`（或桌面应用），刷新 `http://127.0.0.1:3080`：

- 设置页出现 **「启动器设置」**；
- 聊天输入 `/launcher` 会弹出启动动画窗口。

---

## 配置

打开 **设置 → 启动器设置**，点「保存」即可。配置持久化到 `~/.dsh/settings.yaml` 的 `launcher:` 分节，并同步写入 `~/.dsh/launcher-config.json`（exe 读取它）。

| 字段 | 默认 | 说明 |
| --- | --- | --- |
| 工作目录 | 空 | DSH 所在目录（**必填**，分发到其它机器时改成那台机器的路径） |
| 端口 | 3080 | 监听端口 |
| 网址 | http://127.0.0.1:3080 | 就绪后打开的地址 |
| 超时（秒） | 120 | 等待后端就绪的最长时间 |
| 启动方式 | pnpm | `pnpm dsh web` / `node` 直启 / 自定义命令 |
| 开启模式 | 等待 | 等待：已运行弹按钮（打开/重启/关闭）；直接：已运行直接打开网页 |
| 图标路径 | 空 | 可填图标文件，或用选择器选图标所在目录（留空用默认窗口图标） |
| 表情目录 | 空 | 留空不显示表情（仅圆环）；目录内 png/webp/jpg 按文件名顺序播放 |

> exe 也可以脱离插件独立使用：直接双击 `dist/dsh-launcher.exe`，读 `dist/config.json` 或 `~/.dsh/launcher-config.json`。

---

## 构建（给维护者）

```powershell
pwsh scripts/build.ps1   # 编译 exe → dist/ + plugin/
```

需要 .NET Framework 4.x 的 `csc.exe`（Windows 自带）。

## 卸载

```bash
dsh plugin --profile web remove @dsh-external/dsh-launcher
```

## 常见问题

| 现象 | 原因 / 处理 |
| --- | --- |
| 提示「请先配置工作目录」 | 首次使用未配置。设置页「启动器设置」→ 工作目录，填 DSH 所在目录后保存 |
| `/launcher` 没反应 | 插件未激活或未重启。确认安装后重启了 `dsh web` |
| 设置页没有「启动器设置」 | 重启 `dsh web` 并刷新浏览器页面 |
| 启动失败「没找到可用启动方式」 | 检查工作目录是否正确、pnpm 是否在 PATH |
| 端口被占用 | 等待模式弹按钮让你选打开/重启/关闭；直接开启模式自动打开网页 |
| `dsh plugin` 报 pnpm not found | `npm install -g pnpm` |

## 目录结构

```
dsh-launcher/
├── src/Launcher.cs      # WPF 启动器源码（C#5 / .NET Framework 4.x）
├── scripts/build.ps1    # 编译脚本
├── assets/              # 素材说明（仓库不含图片，见 assets/ASSETS-LICENSES.md）
├── dist/                # 独立 exe + config.json（可单独双击使用）
└── plugin/              # DSH 插件 bundle（host + client + exe）
    ├── package.json     # dsh.bundle + dsh.client manifest
    ├── cordis.patch.yml # 挂载 host 行
    ├── index.js         # host 半边
    ├── client.js        # client 半边（设置页）
    └── dist/            # 随插件分发的 exe
```

## 许可

纯代码，MIT，见 `LICENSE`。本仓库**不含任何图片素材**——图标与表情由使用者自行准备并配置，见 `assets/ASSETS-LICENSES.md`。

## 索引

<!-- INDEX-START -->
| name | type | size | created | modified | notes |
|------|------|------|---------|----------|-------|
| .gitignore | file | 40 B | 2026-08-16 | 2026-08-16 | |
| config.json | file | 355 B | 2026-08-15 | 2026-08-16 | |
| LICENSE | file | 1.2 KB | 2026-08-16 | 2026-08-16 | |
| .git | dir | - | 2026-08-15 | 2026-08-16 | |
| assets | dir | - | 2026-08-15 | 2026-08-16 | |
| dist | dir | - | 2026-08-15 | 2026-08-16 | |
| plugin | dir | - | 2026-08-15 | 2026-08-16 | |
| scripts | dir | - | 2026-08-15 | 2026-08-16 | |
| src | dir | - | 2026-08-15 | 2026-08-16 | |
<!-- INDEX-END -->
