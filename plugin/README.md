# dsh-launcher 插件（bundle）

`@dsh-external/dsh-launcher` —— DeepSeek Harness 的 Windows 启动器插件。安装进 profile 后：

- Web 设置页新增 **「启动器设置」** 页面，配置路径/图标/表情/启动方式；
- `/launcher` 斜杠命令唤起启动器 exe（隐藏命令行启动 + DS娘翻转动画 + 自动开浏览器）；
- 配置保存后写入 `%DSH_HOME%\launcher-config.json`，exe 读取它工作。

## 配置方式（设置页）

重启 `dsh web` 后，打开 **设置 → 启动器设置**，编辑后点「保存」。配置持久化到 `%DSH_HOME%\settings.yaml` 的 `launcher:` 分节，并同步写 `launcher-config.json`。

### 配置字段

| 字段 | 类型 | 默认 | 说明 |
|---|---|---|---|
| `workingDirectory` | string | 空 | DSH 工作目录（必填，各机器按需配置） |
| `port` | number | `3080` | 监听端口 |
| `url` | string | `http://127.0.0.1:3080` | 就绪后打开的地址 |
| `timeoutSeconds` | number | `120` | 启动超时 |
| `launchMethod` | `pnpm\|node\|custom` | `pnpm` | 启动方式 |
| `customCommand` | string | `''` | 自定义命令（`custom` 时用） |
| `customArgs` | string[] | `[]` | 自定义参数（`custom` 时用） |
| `iconPath` | string | `''` | 图标文件路径（空 = 默认窗口图标） |
| `stickerDir` | string | `''` | 表情目录（空 = 不显示表情，仅圆环） |
| `mode` | `wait\|direct` | `wait` | 开启模式：`wait` 已运行弹按钮（打开/重启/关闭）；`direct` 已运行直接打开网页 |

## 架构

一个 bundle = host 半边 + client 半边：

- `index.js`（host 半边）：注册 `launcher` settings 命名空间 + `/launcher/api` 设置读写通道 + `/launcher` 命令。
- `client.js`（client 半边）：注册 `settings.section`，渲染设置页表单，通过 fetch `/launcher/api` 读写配置。

## 安装 / 分发

```bash
# 本地路径安装
dsh plugin --profile web add ./plugin

# 从 GitHub / Gitee 直接安装
dsh plugin --profile web add github:XQ-rxslcq/dsh-launcher
dsh plugin --profile web add https://gitee.com/qqiong-art/dsh-launcher.git

# tarball 分发（先打包）
pnpm pack                                  # → @dsh-external-dsh-launcher-0.1.0.tgz
dsh plugin --profile web add ./@dsh-external-dsh-launcher-0.1.0.tgz
```

装完重启 `dsh web` 生效。

## 目录索引

<!-- INDEX-START -->
| name | type | size | created | modified | notes |
|------|------|------|---------|----------|-------|
| client.js | file | 9.9 KB | 2026-08-15 | 2026-08-16 | |
| cordis.patch.yml | file | 445 B | 2026-08-15 | 2026-08-15 | |
| index.js | file | 5.1 KB | 2026-08-15 | 2026-08-16 | |
| package.json | file | 825 B | 2026-08-15 | 2026-08-15 | |
| dist | dir | - | 2026-08-15 | 2026-08-15 | |
<!-- INDEX-END -->
