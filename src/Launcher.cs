// dsh-launcher — DeepSeek Harness 启动器（v2 插件版）
// WPF 单窗口：
//  - 完全透明背景，无边框
//  - 动画：用户绑定的图片绕**竖中轴**（圆心）3D 旋转，3 秒一次（0→90° 换图 →90→0）
//  - 淡蓝色圆环（虚线）绕图标旋转
//  - 动画本体尺寸 ≤ 屏幕 10%；下方蓝色"DSH正在启动中"；报错红字显示在下一行
//  - 隐藏命令行启动 DSH（主/备用/自定义方法），轮询就绪后自动开浏览器并关闭
//  - 配置：<exeDir>/config.json 或 %DSH_HOME%/launcher-config.json（插件写入）
// 编译：scripts/build.ps1（.NET Framework 4.x csc，C# 5 兼容语法）
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;
using System.Windows.Shapes;
using System.Windows.Threading;
using Path = System.IO.Path;

namespace DshLauncher
{
    public class LaunchMethod
    {
        public string name;
        public string file;
        public List<string> args;
    }

    public class LauncherConfig
    {
        public string url = "http://127.0.0.1:3080";
        public int port = 3080;
        public int timeoutSeconds = 120;
        public string workingDirectory = "";
        public string iconPath = "";
        public string stickerDir = "";
        public string mode = "wait";   // wait（等待）：已运行则弹按钮；direct（直接）：已运行直接打开网页
        public List<string> pnpmCandidates = new List<string>();
        public List<LaunchMethod> methods = new List<LaunchMethod>();
    }

    public class App : Application
    {
        [STAThread]
        public static void Main()
        {
            var app = new App();
            app.Run(new MainWindow());
        }
    }

    public class MainWindow : Window
    {
        private const string RES_PREFIX = "dsh-launcher.";
        private LauncherConfig cfg;
        private string exeDir;
        private string homeConfigPath;

        private TextBlock txtMain;    // 蓝色 "DSH正在启动中"
        private TextBlock txtError;   // 红色报错（下一行）
        private Ellipse ring;
        private StackPanel btnPanel;
        private Button btnOpen, btnRestart, btnRetry, btnExit, btnStop;
        private RotateTransform ringRotate;
        // 竖中轴 3D 旋转（Viewport3D 平面绕 Y 轴，轴在圆心），等比缩放、不拉伸
        private Viewport3D vp3d;
        private DiffuseMaterial quadMat;
        private AxisAngleRotation3D rotY;
        private double pixelToWorld;   // 屏幕像素 → 3D 世界单位换算
        private MeshGeometry3D stickerMesh;

        private List<BitmapImage> stickerFrames = new List<BitmapImage>();
        private int stickerIndex = 0;
        private bool animating = false;
        private double animSize;   // 动画区边长（固定，文字变化不改变布局）
        private Process startedProcess;

        private string[] bootMsgs = {
            "DSH正在启动中",
            "DSH正在启动中…鲸鱼尾巴甩起来了喵",
            "DSH正在启动中…稍等喵(｡･ω･｡)",
            "DSH正在启动中…深海女仆正在泡茶！"
        };
        private string errMain = "呜……启动失败了喵(´；ω；`)";

        public MainWindow()
        {
            exeDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            string home = Environment.GetEnvironmentVariable("DSH_HOME");
            if (string.IsNullOrEmpty(home))
                home = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".dsh");
            homeConfigPath = Path.Combine(home, "launcher-config.json");
            cfg = LoadConfig();
            BuildUi();
            Loaded += async (s, e) =>
            {
                CenterWindow();
                await Run();
            };
        }

        // ---------- 配置 ----------
        private LauncherConfig LoadConfig()
        {
            var c = DefaultConfig();
            string path = homeConfigPath;
            if (!File.Exists(path)) path = Path.Combine(exeDir, "config.json");
            if (File.Exists(path))
            {
                try { c = ParseConfigJson(File.ReadAllText(path), c); }
                catch { }
            }
            return c;
        }

        private LauncherConfig DefaultConfig()
        {
            var c = new LauncherConfig();
            // workingDirectory 留空由用户配置；pnpmCandidates 留空靠 PATH 查找 pnpm
            var m1 = new LaunchMethod();
            m1.name = "pnpm dsh web（主）";
            m1.file = "pnpm";
            m1.args = new List<string> { "dsh", "web" };
            var m2 = new LaunchMethod();
            m2.name = "node 直启 built CLI（备用）";
            m2.file = "node";
            m2.args = new List<string> { "apps/cli/lib/bin.js", "web" };
            c.methods.Add(m1);
            c.methods.Add(m2);
            return c;
        }

        // 极简 JSON 解析（C#5 / .NET4 兼容）
        private LauncherConfig ParseConfigJson(string json, LauncherConfig def)
        {
            var c = def;
            Func<string, string> GetStr = key =>
            {
                var m = Regex.Match(json, "\"" + key + "\"\\s*:\\s*\"([^\"]*)\"");
                return m.Success ? m.Groups[1].Value : null;
            };
            Func<string, string> GetInt = key =>
            {
                var m = Regex.Match(json, "\"" + key + "\"\\s*:\\s*(\\d+)");
                return m.Success ? m.Groups[1].Value : null;
            };
            string s;
            if ((s = GetStr("url")) != null) c.url = s;
            if ((s = GetInt("port")) != null) { int p; if (int.TryParse(s, out p)) c.port = p; }
            if ((s = GetInt("timeoutSeconds")) != null) { int t; if (int.TryParse(s, out t)) c.timeoutSeconds = t; }
            if ((s = GetStr("workingDirectory")) != null) c.workingDirectory = s;
            if ((s = GetStr("iconPath")) != null) c.iconPath = s;
            if ((s = GetStr("stickerDir")) != null) c.stickerDir = s;
            if ((s = GetStr("mode")) != null) c.mode = s;

            // methods: [ { "name":..,"file":..,"args":[..] }, ... ]
            var methods = new List<LaunchMethod>();
            var mBlock = Regex.Match(json, "\"methods\"\\s*:\\s*\\[([^\\]]*)\\]");
            if (mBlock.Success)
            {
                foreach (Match mm in Regex.Matches(mBlock.Groups[1].Value,
                    "\\{\\s*\"name\"\\s*:\\s*\"([^\"]*)\"[^}]*?\"file\"\\s*:\\s*\"([^\"]*)\"[^}]*?\"args\"\\s*:\\s*\\[([^\\]]*)\\]\\s*\\}"))
                {
                    var lm = new LaunchMethod();
                    lm.name = mm.Groups[1].Value;
                    lm.file = mm.Groups[2].Value;
                    lm.args = new List<string>();
                    foreach (Match a in Regex.Matches(mm.Groups[3].Value, "\"([^\"]*)\""))
                        lm.args.Add(a.Groups[1].Value);
                    methods.Add(lm);
                }
            }
            if (methods.Count > 0) c.methods = methods;

            // pnpmCandidates: [ "..", ".." ]
            var pc = Regex.Match(json, "\"pnpmCandidates\"\\s*:\\s*\\[([^\\]]*)\\]");
            if (pc.Success)
            {
                var list = new List<string>();
                foreach (Match a in Regex.Matches(pc.Groups[1].Value, "\"([^\"]*)\""))
                    list.Add(a.Groups[1].Value);
                if (list.Count > 0) c.pnpmCandidates = list;
            }
            return c;
        }

        // ---------- UI ----------
        private void BuildUi()
        {
            Title = "DSH 启动器";
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;   // 完全透明
            ResizeMode = ResizeMode.NoResize;
            ShowInTaskbar = false;
            Topmost = true;
            // 固定窗口尺寸：宽度 = 动画区；高度 = 动画区 + 固定文字/按钮区。
            // 任何文本长度变化都不再改变窗口尺寸，图标位置永不漂移。
            WindowStartupLocation = WindowStartupLocation.Manual;
            Icon = LoadIcon();

            // 动画本体尺寸：屏幕 10%
            double sw = SystemParameters.WorkArea.Width;
            double sh = SystemParameters.WorkArea.Height;
            double size = Math.Max(90, Math.Min(sw, sh) * 0.10);
            animSize = size;
            // 窗口固定宽度：给文字/按钮加宽显示区（文字不换行、按钮完整显示）。
            // 圆仍锚定屏幕中心（见 CenterWindow），窗口加宽不会移动圆。
            double winW = Math.Max(size + 100, Math.Min(420, sw * 0.5));
            Width = winW;
            Height = size + 138;

            var root = new Grid();
            Content = root;

            // 行0：圆环 + 表情（叠放；圆与图标都水平居中，窗口加宽不移动它们）
            var animGrid = new Grid
            {
                Width = size, Height = size,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0)
            };
            ring = new Ellipse
            {
                Width = size, Height = size,
                Stroke = new SolidColorBrush(Color.FromRgb(135, 206, 250)), // 淡蓝色
                StrokeThickness = 2.5,
                StrokeDashArray = new DoubleCollection { 4, 3 }
            };
            ringRotate = new RotateTransform(0, size / 2, size / 2);
            ring.RenderTransform = ringRotate;
            animGrid.Children.Add(ring);

            // 贴纸平面：绕 Y 轴（竖中轴，过圆心）旋转，等比缩放不拉伸。
            // 相机固定（fov45、z=1.7），pixelToWorld 用于像素→世界单位换算。
            vp3d = new Viewport3D
            {
                Width = size, Height = size,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            stickerMesh = new MeshGeometry3D();
            stickerMesh.TextureCoordinates = new PointCollection {
                new Point(0, 1), new Point(1, 1), new Point(1, 0), new Point(0, 0)
            };
            stickerMesh.TriangleIndices = new Int32Collection { 0, 1, 2, 0, 2, 3 };
            quadMat = new DiffuseMaterial();
            var quad = new GeometryModel3D(stickerMesh, quadMat);
            var group = new Model3DGroup();
            group.Children.Add(quad);
            group.Children.Add(new AmbientLight(Colors.White));
            rotY = new AxisAngleRotation3D(new Vector3D(0, 1, 0), 0);
            var modelVisual = new ModelVisual3D { Content = group };
            modelVisual.Transform = new RotateTransform3D(rotY);
            double fovDeg = 45;
            double tanHalf = Math.Tan(fovDeg * Math.PI / 360.0);
            double camZ = 1.7;
            double visibleH = 2 * camZ * tanHalf;
            pixelToWorld = visibleH / animSize;
            vp3d.Camera = new PerspectiveCamera(new Point3D(0, 0, camZ), new Vector3D(0, 0, -1), new Vector3D(0, 1, 0), fovDeg);
            vp3d.Children.Add(modelVisual);
            animGrid.Children.Add(vp3d);
            Grid.SetRow(animGrid, 0);
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(animSize) });
            root.Children.Add(animGrid);

            // 行2：蓝色主文字（固定高度容器；加宽显示区，不换行）
            txtMain = new TextBlock
            {
                Text = "DSH正在启动中",
                FontSize = Math.Max(14, size * 0.13),
                Foreground = new SolidColorBrush(Color.FromRgb(79, 195, 247)), // 蓝色
                FontWeight = FontWeights.SemiBold,
                HorizontalAlignment = HorizontalAlignment.Center,
                TextAlignment = TextAlignment.Center,
                TextWrapping = TextWrapping.NoWrap,
                Width = winW,
                Height = 34,
                Margin = new Thickness(0, 8, 0, 0)
            };
            Grid.SetRow(txtMain, 1);
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(42) });
            root.Children.Add(txtMain);

            // 行3：红色报错（固定高度容器；Hidden 保留占位，出现时不改变布局）
            txtError = new TextBlock
            {
                FontSize = Math.Max(12, size * 0.11),
                Foreground = new SolidColorBrush(Color.FromRgb(255, 107, 107)), // 红色
                TextAlignment = TextAlignment.Center,
                TextWrapping = TextWrapping.Wrap,
                Width = winW,
                Height = 42,
                Visibility = Visibility.Hidden,
                Margin = new Thickness(0, 2, 0, 0)
            };
            Grid.SetRow(txtError, 2);
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(44) });
            root.Children.Add(txtError);

            // 行4：按钮（Hidden 保留占位；加宽加大，完整显示）
            btnPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Visibility = Visibility.Hidden,
                Margin = new Thickness(0, 6, 0, 0)
            };
            btnOpen = MakeButton("直接打开", Color.FromRgb(46, 125, 50));
            btnOpen.Click += (s, e) => { OpenBrowser(); Close(); };
            btnRestart = MakeButton("重启服务", Color.FromRgb(230, 126, 34));
            btnRestart.Click += async (s, e) => { await RestartAndRun(); };
            btnRetry = MakeButton("重试", Color.FromRgb(230, 126, 34));
            btnRetry.Click += async (s, e) => { await StartFlow(); };
            btnExit = MakeButton("退出", Color.FromRgb(198, 40, 40));
            btnExit.Click += (s, e) => { Close(); };
            btnStop = MakeButton("关闭服务", Color.FromRgb(198, 40, 40));
            btnStop.Click += async (s, e) => { await StopService(); };
            btnPanel.Children.Add(btnOpen);
            btnPanel.Children.Add(btnRestart);
            btnPanel.Children.Add(btnRetry);
            btnPanel.Children.Add(btnExit);
            Grid.SetRow(btnPanel, 3);
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(52) });
            root.Children.Add(btnPanel);

            LoadStickers();
        }

        // 以圆为屏幕中心的基准：圆在窗口内水平居中（Left 用窗口宽），
        // 圆心的垂直位置在 窗口顶 + animSize/2，因此 Top 让圆心对准屏幕中心。
        // 窗口尺寸固定，文字变化不再触发重算，图标永不漂移。
        private void CenterWindow()
        {
            var wa = SystemParameters.WorkArea;
            Dispatcher.BeginInvoke(new Action(() =>
            {
                Left = wa.Left + (wa.Width - Width) / 2;
                Top = wa.Top + (wa.Height - animSize) / 2;
            }), DispatcherPriority.Loaded);
        }

        private Button MakeButton(string text, Color bg)
        {
            return new Button
            {
                Content = text,
                FontSize = 14,
                Foreground = Brushes.White,
                Background = new SolidColorBrush(bg),
                BorderThickness = new Thickness(0),
                Padding = new Thickness(14, 8, 14, 8),
                Margin = new Thickness(5, 0, 5, 0),
                Cursor = System.Windows.Input.Cursors.Hand
            };
        }

        private void LoadStickers()
        {
            stickerFrames.Clear();
            string dir = cfg.stickerDir;
            if (!string.IsNullOrEmpty(dir) && !Path.IsPathRooted(dir))
                dir = Path.Combine(exeDir, dir);   // 相对路径 → 相对 exe 所在目录
            if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir))
            {
                var files = new List<string>();
                foreach (var ext in new[] { "*.png", "*.webp", "*.jpg", "*.jpeg", "*.bmp", "*.gif" })
                    files.AddRange(Directory.GetFiles(dir, ext));
                files.Sort();
                foreach (var f in files)
                {
                    try
                    {
                        var bi = new BitmapImage();
                        bi.BeginInit();
                        bi.UriSource = new Uri(f);
                        bi.CacheOption = BitmapCacheOption.OnLoad;
                        bi.EndInit();
                        bi.Freeze();
                        stickerFrames.Add(bi);
                    }
                    catch { }
                }
            }
            // 无内置表情：stickerFrames 保持空（仅圆环旋转，不翻转），由用户在设置页绑定 stickerDir
            if (stickerFrames.Count > 0)
                SetSticker(stickerFrames[0]);
        }

        // 换表情：按图真实宽高比重建 3D 平面（等比、不拉伸、尽量大），
        // 约束：等比缩放填充圆的内接正方形（长边撑满、短边居中留白），旋转时四角不超圆。
        private void SetSticker(BitmapImage bi)
        {
            quadMat.Brush = new ImageBrush(bi);
            double pw = bi.PixelWidth > 0 ? bi.PixelWidth : 1;
            double ph = bi.PixelHeight > 0 ? bi.PixelHeight : 1;
            double aspect = pw / ph;                       // >1 横图，<1 竖图
            double boxWorld = (animSize / Math.Sqrt(2)) * pixelToWorld;  // 内接正方形边长（世界单位）
            double hw, hh;
            if (aspect >= 1) { hw = boxWorld / 2; hh = boxWorld / (2 * aspect); }
            else { hh = boxWorld / 2; hw = boxWorld / 2 * aspect; }
            stickerMesh.Positions.Clear();
            stickerMesh.Positions.Add(new Point3D(-hw, -hh, 0));
            stickerMesh.Positions.Add(new Point3D(hw, -hh, 0));
            stickerMesh.Positions.Add(new Point3D(hw, hh, 0));
            stickerMesh.Positions.Add(new Point3D(-hw, hh, 0));
        }

        private ImageSource LoadIcon()
        {
            string p = ResolveIconPath();
            if (p != null)
            {
                try
                {
                    var bi = new BitmapImage();
                    bi.BeginInit();
                    bi.UriSource = new Uri(p);
                    bi.CacheOption = BitmapCacheOption.OnLoad;
                    bi.EndInit();
                    bi.Freeze();
                    return bi;
                }
                catch { }
            }
            return LoadResource("app.icon.png");
        }

        // iconPath 可以是具体文件，也可以是目录（在目录里找 icon.png/icon.ico 或第一个图片）
        private string ResolveIconPath()
        {
            if (string.IsNullOrEmpty(cfg.iconPath)) return null;
            string p = cfg.iconPath;
            if (!Path.IsPathRooted(p)) p = Path.Combine(exeDir, p);   // 相对路径 → 相对 exe 所在目录
            if (File.Exists(p)) return p;
            if (Directory.Exists(p))
            {
                foreach (var name in new[] { "icon.png", "icon.ico", "icon.jpg", "icon.jpeg", "icon.webp", "icon.bmp", "icon.gif" })
                {
                    var f = Path.Combine(p, name);
                    if (File.Exists(f)) return f;
                }
                foreach (var ext in new[] { "*.png", "*.ico", "*.jpg", "*.jpeg", "*.webp", "*.bmp", "*.gif" })
                {
                    var files = Directory.GetFiles(p, ext);
                    if (files.Length > 0) return files[0];
                }
            }
            return null;
        }

        // ---------- 主流程 ----------
        private async Task Run()
        {
            ShowMain("DSH正在启动中");
            await Task.Delay(300);
            bool occupied = await IsPortOpen(cfg.port);
            if (occupied)
            {
                if (cfg.mode == "direct")
                {
                    // 直接开启：已运行则直接打开网页并关闭，不弹按钮
                    ShowMain("DSH 已经在运行，直接打开喵～");
                    await Task.Delay(300);
                    OpenBrowser();
                    Close();
                    return;
                }
                // 等待模式：已运行则展示按钮，由用户选择打开/重启/关闭服务
                ShowMain("DSH 已经在运行喵～");
                SetButtons(new[] { btnOpen, btnRestart, btnStop });
                StartFlipLoop();
                return;
            }
            await StartFlow();
        }

        private async Task RestartAndRun()
        {
            HideButtons();
            ShowMain("正在结束旧服务…");
            await Task.Delay(200);
            KillPortOwners(cfg.port);
            await Task.Delay(800);
            await StartFlow();
        }

        private async Task StopService()
        {
            HideButtons();
            ShowMain("正在关闭服务…");
            await Task.Delay(200);
            KillPortOwners(cfg.port);
            await Task.Delay(300);
            Close();
        }

        private async Task StartFlow()
        {
            if (string.IsNullOrEmpty(cfg.workingDirectory))
            {
                ShowError("请先配置工作目录喵～ 在设置页「启动器设置」或 config.json 里填 DSH 所在目录");
                SetButtons(new[] { btnExit });
                return;
            }
            HideButtons();
            ShowMain(bootMsgs[0]);
            StartFlipLoop();
            bool started = await StartDsh();
            if (!started)
            {
                StopFlipLoop();
                ShowError("没找到可用的启动方式喵…请检查启动器设置里的路径");
                SetButtons(new[] { btnRetry, btnExit });
                return;
            }
            bool ready = await WaitReady(cfg.port, cfg.timeoutSeconds);
            if (ready)
            {
                StopFlipLoop();
                ShowMain("启动成功！欢迎回来喵～(=^･ω･^=)");
                await Task.Delay(600);
                OpenBrowser();
                Close();
            }
            else
            {
                StopFlipLoop();
                ShowError("启动超时了喵…检查路径或网络后重试");
                SetButtons(new[] { btnRetry, btnExit });
            }
        }

        private async Task<bool> StartDsh()
        {
            foreach (var m in cfg.methods)
            {
                string resolved = ResolveExecutable(m.file);
                if (resolved == null) continue;
                try
                {
                    var psi = new ProcessStartInfo();
                    psi.FileName = resolved;
                    psi.Arguments = BuildArgs(m.args);
                    psi.WorkingDirectory = cfg.workingDirectory;
                    psi.UseShellExecute = false;
                    psi.CreateNoWindow = true;
                    psi.WindowStyle = ProcessWindowStyle.Hidden;
                    startedProcess = Process.Start(psi);
                    return true;
                }
                catch (Exception ex)
                {
                    ShowError("「" + m.name + "」不行喵，试试下一个…");
                    Thread.Sleep(400);
                }
            }
            return false;
        }

        private string BuildArgs(List<string> args)
        {
            var parts = new List<string>();
            foreach (var a in args)
                parts.Add(a.Contains(" ") ? "\"" + a + "\"" : a);
            return string.Join(" ", parts.ToArray());
        }

        private string ResolveExecutable(string name)
        {
            if (File.Exists(name)) return Path.GetFullPath(name);
            if (name.Equals("pnpm", StringComparison.OrdinalIgnoreCase))
                foreach (var cand in cfg.pnpmCandidates)
                    if (File.Exists(cand)) return cand;
            string pathEnv = Environment.GetEnvironmentVariable("PATH") ?? "";
            string[] exts = new[] { ".exe", ".cmd", ".bat", ".com" };
            foreach (var dir in pathEnv.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
                foreach (var ext in exts)
                {
                    string full = Path.Combine(dir.Trim(), name + ext);
                    if (File.Exists(full)) return full;
                }
            return null;
        }

        // ---------- 轮询就绪 ----------
        private async Task<bool> WaitReady(int port, int timeoutSeconds)
        {
            int total = timeoutSeconds * 1000;
            int waited = 0, step = 0;
            while (waited < total)
            {
                await Task.Delay(500);
                waited += 500; step++;
                if (step % 4 == 0) ShowMain(bootMsgs[(step / 4) % bootMsgs.Length]);
                if (await IsPortOpen(port) && await HttpReachable(cfg.url))
                    return true;
            }
            return false;
        }

        private async Task<bool> IsPortOpen(int port)
        {
            try
            {
                using (var client = new TcpClient())
                {
                    var t = client.ConnectAsync("127.0.0.1", port);
                    if (await Task.WhenAny(t, Task.Delay(1500)) == t)
                        return client.Connected;
                }
            }
            catch { }
            return false;
        }

        private async Task<bool> HttpReachable(string url)
        {
            try
            {
                var req = (HttpWebRequest)WebRequest.Create(url);
                req.Timeout = 1500; req.Method = "GET";
                using (var resp = (HttpWebResponse)await req.GetResponseAsync()) { return true; }
            }
            catch (WebException we) { if (we.Response != null) return true; }
            catch { }
            return false;
        }

        // ---------- 端口占用 ----------
        private void KillPortOwners(int port)
        {
            try
            {
                var psi = new ProcessStartInfo("netstat", "-ano")
                { UseShellExecute = false, RedirectStandardOutput = true, CreateNoWindow = true };
                using (var p = Process.Start(psi))
                {
                    string output = p.StandardOutput.ReadToEnd();
                    p.WaitForExit(3000);
                    string needle = ":" + port.ToString(CultureInfo.InvariantCulture);
                    var pids = new HashSet<int>();
                    foreach (var line in output.Split('\n'))
                    {
                        if (line.IndexOf(needle, StringComparison.Ordinal) < 0) continue;
                        if (line.IndexOf("LISTENING", StringComparison.OrdinalIgnoreCase) < 0) continue;
                        var parts = line.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                        int pid;
                        if (parts.Length > 0 && int.TryParse(parts[parts.Length - 1], out pid))
                            pids.Add(pid);
                    }
                    foreach (var pid in pids)
                    {
                        if (pid == Process.GetCurrentProcess().Id) continue;
                        try
                        {
                            Process.Start(new ProcessStartInfo("taskkill", "/PID " + pid + " /T /F")
                            { UseShellExecute = false, CreateNoWindow = true }).WaitForExit(2000);
                        }
                        catch { }
                    }
                }
            }
            catch { }
        }

        private void OpenBrowser()
        {
            try { Process.Start(new ProcessStartInfo(cfg.url) { UseShellExecute = true }); }
            catch { }
        }

        // ---------- 动画 ----------
        private void StartFlipLoop()
        {
            if (animating) return;
            animating = true;
            StartRingSpin();
            if (stickerFrames.Count > 0) FlipToNext();
        }

        private void StopFlipLoop()
        {
            animating = false;
            rotY.BeginAnimation(AxisAngleRotation3D.AngleProperty, null);
            rotY.Angle = 0;
            ringRotate.BeginAnimation(RotateTransform.AngleProperty, null);
        }

        private void StartRingSpin()
        {
            var spin = new DoubleAnimation(0, 360, TimeSpan.FromSeconds(2.4))
            { RepeatBehavior = RepeatBehavior.Forever };
            ringRotate.BeginAnimation(RotateTransform.AngleProperty, spin);
        }

        // 竖中轴翻转：绕 Y 轴 0→90（到 90° 侧面隐形）换图 →90→0 翻回
        // 竖中轴旋转：绕 Y 轴 0→90（到 90° 侧面隐形）换图 →90→0 翻回，全程 3 秒
        private void FlipToNext()
        {
            if (!animating) return;
            stickerIndex = (stickerIndex + 1) % stickerFrames.Count;
            var to90 = new DoubleAnimation(0, 90, TimeSpan.FromMilliseconds(1500));
            to90.EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseInOut };
            to90.Completed += (s, e) =>
            {
                if (!animating) return;
                SetSticker(stickerFrames[stickerIndex]);
                var back = new DoubleAnimation(90, 0, TimeSpan.FromMilliseconds(1500));
                back.EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseInOut };
                back.Completed += (s2, e2) => FlipToNext();
                rotY.BeginAnimation(AxisAngleRotation3D.AngleProperty, back);
            };
            rotY.BeginAnimation(AxisAngleRotation3D.AngleProperty, to90);
        }

        // ---------- 文字 ----------
        private void ShowMain(string text) { txtMain.Text = text; }
        private void ShowError(string text)
        {
            txtError.Text = text;
            txtError.Visibility = Visibility.Visible;
            CenterWindow();
        }
        private void ClearError() { txtError.Visibility = Visibility.Hidden; }

        private void SetButtons(Button[] buttons)
        {
            btnPanel.Children.Clear();
            foreach (var b in buttons) btnPanel.Children.Add(b);
            btnPanel.Visibility = Visibility.Visible;
            CenterWindow();
        }
        private void HideButtons() { btnPanel.Visibility = Visibility.Hidden; }

        // ---------- 资源 ----------
        private BitmapImage LoadResource(string name)
        {
            var asm = Assembly.GetExecutingAssembly();
            using (var s = asm.GetManifestResourceStream(RES_PREFIX + name))
            {
                if (s == null) return null;
                var bi = new BitmapImage();
                bi.BeginInit();
                bi.StreamSource = s;
                bi.CacheOption = BitmapCacheOption.OnLoad;
                bi.EndInit();
                bi.Freeze();
                return bi;
            }
        }
    }
}
