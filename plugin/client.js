// dsh-launcher — client half
// 在 Web 设置页注册「启动器设置」页（settings.section），通过 POST /launcher/api
// 读写 host 半边的配置；目录字段用 DSH 原生目录选择器（ctx.workspaces.pickDirectory）。
window.__ModuleLoader__.load({
  id: "@dsh-external/dsh-launcher",
  factory: (require) => {
    var module = { exports: {} };
    var exports = module.exports;
    Object.defineProperty(exports, Symbol.toStringTag, { value: "Module" });
    var React = require("react");
    var el = React.createElement;

    var api = function (payload) {
      return fetch("/launcher/api", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify(payload)
      }).then(function (r) { return r.json(); });
    };

    var st = {
      root: { display: "flex", flexDirection: "column", gap: 10, padding: "6px 0", maxWidth: 760 },
      field: { display: "flex", flexDirection: "column", gap: 3 },
      fieldGrow: { display: "flex", flexDirection: "column", gap: 3, flex: "1 1 220px", minWidth: 220 },
      label: { fontSize: 12, fontWeight: 600, opacity: 0.75 },
      input: { border: "1px solid rgba(128,128,128,.35)", background: "transparent", borderRadius: 6, padding: "7px 10px", fontSize: 13, color: "inherit", width: "100%", boxSizing: "border-box" },
      pathRow: { display: "flex", gap: 6, alignItems: "center" },
      pickBtn: { border: "1px solid rgba(128,128,128,.35)", background: "transparent", borderRadius: 6, padding: "7px 12px", fontSize: 12, cursor: "pointer", color: "inherit", whiteSpace: "nowrap" },
      row: { display: "flex", gap: 10, flexWrap: "wrap" },
      hint: { fontSize: 11, opacity: 0.5, marginTop: 2 },
      actions: { display: "flex", gap: 10, alignItems: "center", marginTop: 2 },
      btn: { border: "1px solid rgba(128,128,128,.35)", background: "transparent", borderRadius: 6, padding: "7px 16px", fontSize: 13, cursor: "pointer", color: "inherit" },
      btnPrimary: { border: 0, background: "rgba(90,140,255,.9)", color: "#fff", borderRadius: 6, padding: "7px 18px", fontSize: 13, cursor: "pointer", fontWeight: 600 },
      saved: { color: "#2ecc71", fontSize: 12 },
      err: { color: "#ff6b6b", fontSize: 12 },
      note: { fontSize: 11, opacity: 0.55, lineHeight: 1.6 }
    };

    function Field(props) {
      return el("div", { style: props.grow ? st.fieldGrow : st.field },
        el("div", { style: st.label }, props.label),
        props.children,
        props.hint ? el("div", { style: st.hint }, props.hint) : null
      );
    }

    function PathField(props) {
      return el("div", { style: st.field },
        el("div", { style: st.label }, props.label),
        el("div", { style: st.pathRow },
          el("input", { style: st.input, value: props.value || "", onChange: function (e) { props.onChange(e.target.value); } }),
          el("button", { type: "button", style: st.pickBtn, onClick: props.onPick }, "选择…")
        ),
        props.hint ? el("div", { style: st.hint }, props.hint) : null
      );
    }

    function SettingsPanel(props) {
      var ws = props.workspaces;
      var state = React.useState({ status: "loading", value: null, error: "", saved: false });
      var s = state[0], setS = state[1];

      function load() {
        setS({ status: "loading", value: null, error: "", saved: false });
        api({ action: "get" }).then(function (res) {
          if (res && res.ok) setS({ status: "done", value: res.value, error: "", saved: false });
          else setS({ status: "error", value: null, error: (res && res.error) || "读取配置失败", saved: false });
        }).catch(function (e) {
          setS({ status: "error", value: null, error: String((e && e.message) || e), saved: false });
        });
      }

      React.useEffect(function () { load(); }, []);

      if (s.status === "loading") return el("div", { style: st.note }, "正在读取配置…");
      if (s.status === "error") return el("div", null,
        el("div", { style: st.err }, s.error),
        el("button", { style: st.btn, onClick: load, marginTop: 8 }, "重试")
      );

      var v = s.value || {};

      function patch(updates) {
        setS(function (prev) {
          var base = prev.value || {};
          var next = {};
          for (var k in base) next[k] = base[k];
          for (var k2 in updates) next[k2] = updates[k2];
          return { status: "done", value: next, error: "", saved: false };
        });
      }

      function pickDir(field) {
        if (!ws || typeof ws.pickDirectory !== "function") return;
        ws.pickDirectory().then(function (p) {
          if (p) { var u = {}; u[field] = p; patch(u); }
        }).catch(function () {});
      }

      function save() {
        var cur = s.value || {};
        var p = {
          workingDirectory: cur.workingDirectory,
          port: Number(cur.port),
          url: cur.url,
          timeoutSeconds: Number(cur.timeoutSeconds),
          launchMethod: cur.launchMethod,
          customCommand: cur.customCommand,
          customArgs: String(cur.customArgs || "").split(",").map(function (x) { return x.trim(); }).filter(Boolean),
          iconPath: cur.iconPath,
          stickerDir: cur.stickerDir,
          mode: cur.mode || "wait"
        };
        setS({ status: "done", value: cur, error: "", saved: false });
        api({ action: "set", patch: p }).then(function (res) {
          if (res && res.ok) setS({ status: "done", value: res.value, error: "", saved: true });
          else setS({ status: "done", value: cur, error: (res && res.error) || "保存失败", saved: false });
        }).catch(function (e) {
          setS({ status: "done", value: cur, error: String((e && e.message) || e), saved: false });
        });
      }

      return el("div", { style: st.root },
        el(PathField, {
          label: "工作目录（DSH 所在目录）",
          hint: "启动 DSH 的工作目录；分发到其它机器时必改",
          value: v.workingDirectory,
          onChange: function (x) { patch({ workingDirectory: x }); },
          onPick: function () { pickDir("workingDirectory"); }
        }),
        el("div", { style: st.row },
          el(Field, { label: "端口", grow: true },
            el("input", { style: st.input, type: "number", value: v.port, onChange: function (e) { patch({ port: e.target.value }); } })
          ),
          el(Field, { label: "网址", grow: true },
            el("input", { style: st.input, value: v.url || "", onChange: function (e) { patch({ url: e.target.value }); } })
          ),
          el(Field, { label: "超时（秒）", hint: "等待后端就绪的最长时间，超过则提示启动失败", grow: true },
            el("input", { style: st.input, type: "number", value: v.timeoutSeconds, onChange: function (e) { patch({ timeoutSeconds: e.target.value }); } })
          )
        ),
        el(Field, { label: "启动方式" },
          el("select", { style: st.input, value: v.launchMethod || "pnpm", onChange: function (e) { patch({ launchMethod: e.target.value }); } },
            el("option", { value: "pnpm" }, "pnpm dsh web（推荐）"),
            el("option", { value: "node" }, "node 直启 built CLI"),
            el("option", { value: "custom" }, "自定义命令")
          )
        ),
        v.launchMethod === "custom"
          ? el("div", { style: st.row },
              el(Field, { label: "自定义命令", grow: true },
                el("input", { style: st.input, value: v.customCommand || "", onChange: function (e) { patch({ customCommand: e.target.value }); } })
              ),
              el(Field, { label: "自定义参数（逗号分隔）", grow: true },
                el("input", { style: st.input, value: Array.isArray(v.customArgs) ? v.customArgs.join(", ") : (v.customArgs || ""), onChange: function (e) { patch({ customArgs: e.target.value }); } })
              )
            )
          : null,
        el(Field, { label: "开启模式", hint: "等待：已运行时弹按钮让你选打开/重启/关闭；直接：已运行时直接打开网页" },
          el("select", { style: st.input, value: v.mode || "wait", onChange: function (e) { patch({ mode: e.target.value }); } },
            el("option", { value: "wait" }, "等待模式"),
            el("option", { value: "direct" }, "直接开启")
          )
        ),
        el(PathField, {
          label: "图标路径（可选）",
          hint: "选图标所在目录会自动识别目录内的 png/ico/jpg/webp/bmp 图片，也可直接填文件路径（留空用默认窗口图标）",
          value: v.iconPath,
          onChange: function (x) { patch({ iconPath: x }); },
          onPick: function () { pickDir("iconPath"); }
        }),
        el(PathField, {
          label: "表情目录（可选）",
          hint: "选表情所在目录（支持 png/webp/jpg/bmp/gif，按文件名顺序播放），留空则不显示表情",
          value: v.stickerDir,
          onChange: function (x) { patch({ stickerDir: x }); },
          onPick: function () { pickDir("stickerDir"); }
        }),
        el("div", { style: st.actions },
          el("button", { style: st.btnPrimary, onClick: save }, "保存"),
          s.saved ? el("span", { style: st.saved }, "已保存 ✓") : null,
          s.error ? el("span", { style: st.err }, s.error) : null
        ),
        el("div", { style: st.note }, "保存后写入 %DSH_HOME%\\launcher-config.json；唤起启动器请用 /launcher 命令，或直接双击 plugin\\dist\\dsh-launcher.exe。")
      );
    }

    var inject = ["slots"];

    function apply(ctx) {
      var slots = ctx.get("slots");
      if (slots === undefined) return;
      var workspaces = ctx.get("workspaces");
      slots.inject("settings.section", function () {
        return slots.register(
          { name: "settings.section", id: "launcher", order: 40, label: "启动器设置" },
          function () { return el(SettingsPanel, { workspaces: workspaces }); }
        );
      });
    }

    exports.inject = inject;
    exports.apply = apply;
    return module.exports;
  }
});
