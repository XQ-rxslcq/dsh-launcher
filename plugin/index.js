// dsh-launcher — host half
// 1) 注册 `launcher` settings 命名空间（持久化到 %DSH_HOME%/settings.yaml），
//    变化即写 %DSH_HOME%/launcher-config.json（启动器 exe 读取）。
// 2) 注册 POST /launcher/api：client 半边设置页通过它读写配置
//    （绕开 api-proxy 的 settings 白名单，因为第三方命名空间不暴露给浏览器）。
// 3) /launcher 命令 → 唤起启动器 exe（隐藏命令行 + DS娘翻转动画 + 自动开浏览器）。
import { join, dirname } from 'node:path'
import { homedir } from 'node:os'
import { writeFileSync, existsSync } from 'node:fs'
import { spawn } from 'node:child_process'
import { createRequire } from 'node:module'
import { fileURLToPath } from 'node:url'

const pluginDir = dirname(fileURLToPath(import.meta.url))

// schemastery 由 DSH 安装提供。link:（源码）安装时插件自身没有 node_modules，
// 因此用 createRequire 锚定到 DSH 的 profile node_modules 解析。
const dshHome = process.env.DSH_HOME || join(homedir(), '.dsh')
const requireFromDsh = createRequire(join(dshHome, 'profiles', 'node_modules', 'index.js'))
const schemaMod = requireFromDsh('@deepseek-ai/schemastery')
const Schema = schemaMod.default || schemaMod

export const name = 'launcher'
export const inject = ['settings', 'commands']

export function apply(ctx) {
  const schema = Schema.object({
    workingDirectory: Schema.string().default(''),
    port: Schema.number().default(3080),
    url: Schema.string().default('http://127.0.0.1:3080'),
    timeoutSeconds: Schema.number().default(120),
    launchMethod: Schema.union(['pnpm', 'node', 'custom']).default('pnpm'),
    customCommand: Schema.string().default(''),
    customArgs: Schema.array(Schema.string()).default([]),
    iconPath: Schema.string().default(''),
    stickerDir: Schema.string().default(''),
    mode: Schema.union(['wait', 'direct']).default('wait')
  })

  const scope = ctx.settings.register('launcher', schema)

  const buildMethods = (v) => {
    const port = String(v.port || 3080)
    if (v.launchMethod === 'node') {
      return [{ name: 'node 直启 built CLI', file: 'node', args: ['apps/cli/lib/bin.js', 'web', '--port', port] }]
    }
    if (v.launchMethod === 'custom') {
      const args = Array.isArray(v.customArgs) ? v.customArgs : []
      return [{ name: '自定义命令', file: v.customCommand || 'node', args }]
    }
    return [
      { name: 'pnpm dsh web（主）', file: 'pnpm', args: ['dsh', 'web', '--port', port] },
      { name: 'node 直启 built CLI（备用）', file: 'node', args: ['apps/cli/lib/bin.js', 'web', '--port', port] }
    ]
  }

  const writeConfig = () => {
    try {
      const v = scope.get()
      const home = process.env.DSH_HOME || join(homedir(), '.dsh')
      const cfg = {
        url: v.url,
        port: v.port,
        timeoutSeconds: v.timeoutSeconds,
        workingDirectory: v.workingDirectory,
        mode: v.mode,
        pnpmCandidates: [
          join(homedir(), 'AppData', 'Roaming', 'npm', 'pnpm.cmd'),
          join(homedir(), 'AppData', 'Local', 'pnpm', 'pnpm.CMD')
        ],
        methods: buildMethods(v),
        iconPath: v.iconPath || '',
        stickerDir: v.stickerDir || ''
      }
      writeFileSync(join(home, 'launcher-config.json'), JSON.stringify(cfg, null, 2))
    } catch { /* 写失败不阻断 */ }
  }

  scope.watch(() => writeConfig())
  writeConfig()

  // 重新编译 exe 并嵌入指定图标（仅源码安装可用；覆盖 plugin/dist 与 dist 下的 exe）
  const compileExe = async (iconPath) => {
    try {
      const buildScript = join(pluginDir, '..', 'scripts', 'build.ps1')
      if (!existsSync(buildScript)) {
        return { ok: false, error: '当前安装方式不含编译源码，仅源码安装（link）可在线编译；请手动在项目目录运行 scripts/build.ps1' }
      }
      const args = ['-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', buildScript]
      if (iconPath) args.push('-Icon', iconPath)
      const result = await new Promise((resolve) => {
        let out = ''
        let err = ''
        let child
        try {
          child = spawn('powershell.exe', args, { stdio: ['ignore', 'pipe', 'pipe'] })
        } catch (e) {
          resolve({ code: -1, out: '', err: '无法启动 powershell：' + String((e && e.message) || e) })
          return
        }
        child.stdout.on('data', (c) => { out += c })
        child.stderr.on('data', (c) => { err += c })
        child.on('error', (e) => { resolve({ code: -1, out, err: String((e && e.message) || e) }) })
        child.on('close', (code) => { resolve({ code, out, err }) })
      })
      if (result.code === 0) return { ok: true, output: String(result.out || '').trim() }
      return { ok: false, error: String(result.err || result.out || '编译失败').trim() }
    } catch (e) {
      return { ok: false, error: (e && e.message) ? e.message : String(e) }
    }
  }

  // ---------- 设置页读写通道 ----------
  const readBody = (req) => new Promise((resolve) => {
    let d = ''
    req.on('data', (c) => { d += c })
    req.on('end', () => { try { resolve(JSON.parse(d)) } catch { resolve({}) } })
    req.on('error', () => resolve({}))
  })
  const sendJson = (res, obj) => {
    res.writeHead(200, { 'Content-Type': 'application/json; charset=utf-8', 'Cache-Control': 'no-store' })
    res.end(JSON.stringify(obj))
  }

  const webServer = ctx.get('webServer')
  if (webServer && typeof webServer.register === 'function') {
    webServer.register({
      kind: 'exact',
      path: '/launcher/api',
      handler: async (req, res) => {
        try {
          const body = await readBody(req)
          if (body.action === 'get') {
            sendJson(res, { ok: true, value: scope.get() })
          } else if (body.action === 'set') {
            await scope.update(body.patch || {})
            writeConfig()
            sendJson(res, { ok: true, value: scope.get() })
          } else if (body.action === 'compile') {
            sendJson(res, await compileExe(body.iconPath || ''))
          } else {
            sendJson(res, { ok: false, error: 'unknown action: ' + String(body.action) })
          }
        } catch (e) {
          sendJson(res, { ok: false, error: (e && e.message) ? e.message : String(e) })
        }
      }
    })
  }

  ctx.commands.register({
    name: 'launcher',
    description: '启动/重启 DeepSeek Harness（弹出 DS娘 启动动画窗口）',
    handler: () => {
      try {
        writeConfig()
        const exe = join(pluginDir, 'dist', 'dsh-launcher.exe')
        spawn(exe, [], { detached: true, stdio: 'ignore' }).unref()
        return { kind: 'success', text: '已唤起启动器，动画窗口应已弹出喵～' }
      } catch (e) {
        return { kind: 'error', text: '启动器唤起失败：' + (e && e.message ? e.message : String(e)) }
      }
    }
  })
}
