using System.Diagnostics;
using System.Net;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.RegularExpressions;

/*
 * # 致翻阅此代码的人：
 * 请记得不必硬扛所有难题。好好活着已是珍贵，
 * 愿你平安幸福，渡过所有难熬的时刻。
 */

namespace SpaceTranslate;

internal static class Program
{
    internal static readonly string DataDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SpaceTranslate");
    internal static readonly string ConfigPath = Path.Combine(DataDirectory, "settings.json");
    internal static readonly string DiagnosticsLogPath = Path.Combine(DataDirectory, "diagnostics.log");
    [STAThread]
    private static void Main()
    {
        using var mutex = new Mutex(true, @"Local\SpaceTranslate-1", out bool first);
        if (!first) { MessageBox.Show("空格翻译已经在运行。请在任务栏右下角找到它的托盘图标。", "空格翻译"); return; }
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
        try { var cfg = Settings.Load(ConfigPath); Application.Run(new MainForm(cfg)); }
        catch (Exception e)
        { MessageBox.Show("无法启动空格翻译：\n" + e.Message + "\n\n配置位置：" + ConfigPath, "空格翻译", MessageBoxButtons.OK, MessageBoxIcon.Error); }
    }
    internal static readonly object LogLock = new();
    internal static void Log(Settings cfg, string message)
    {
        try
        {
            if (!cfg.LogDiagnostics) return;
            SupportLog.Write(message);
            string line = $"[{DateTime.Now:HH:mm:ss.fff}] {message}{Environment.NewLine}";
            if (File.Exists(DiagnosticsLogPath) && new FileInfo(DiagnosticsLogPath).Length > 512 * 1024)
                File.Move(DiagnosticsLogPath, DiagnosticsLogPath + ".previous", true);
            lock (LogLock) File.AppendAllText(DiagnosticsLogPath, line);
        }
        catch { }
    }
}

internal sealed class StatusPopup : Form
{
    private readonly Label text;
    private ComboBox? languagePicker;
    internal bool ChoosingLanguage => languagePicker?.DroppedDown == true;
    internal void ConfigureLanguage(string language, Action<string> changed)
    {
        languagePicker = FeatureUi.LanguagePicker(language, changed);
        languagePicker.Location = new Point(18, 87); languagePicker.Width = 280;
        Controls.Add(languagePicker);
    }
    internal void SetLanguage(string language)
    {
        if (languagePicker != null) languagePicker.SelectedIndex = Languages.Names.Keys.ToList().IndexOf(language);
    }
    internal StatusPopup()
    {
        AutoScaleMode = AutoScaleMode.Dpi; FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false; TopMost = true; BackColor = BrandTheme.DarkBlue;
        Size = new Size(320, 128); Padding = new Padding(18);
        text = new Label { Location = new Point(18, 8), Size = new Size(284, 74), ForeColor = Color.White, Font = new Font("Microsoft YaHei UI", 10), TextAlign = ContentAlignment.MiddleLeft };
        Controls.Add(text);
    }
    protected override bool ShowWithoutActivation => true;
    protected override CreateParams CreateParams
    { get { var p = base.CreateParams; p.ExStyle |= 0x08000000 | 0x80; return p; } }
    internal void Message(string value)
    {
        text.Text = value;
        var area = Screen.FromPoint(Cursor.Position).WorkingArea;
        Location = new Point(area.Right - Width - 20, area.Bottom - Height - 20);
        if (!Visible) Show();
    }
}

internal sealed class MainForm : Form
{
    private readonly Settings cfg;
    private readonly OllamaClient client;
    private readonly NotifyIcon tray;
    private readonly Icon activeIcon, pausedIcon, windowIcon;
    private readonly StatusPopup popup = new();
    private readonly Label headline, detail, shortcuts;
    private readonly Button prepare, toggle, background;
    private readonly BrandProgressBar progress;
    private readonly ToolStripMenuItem trayToggle;
    private readonly ToolStripMenuItem retryCopy;
    private string? undeliveredTranslation;
    private readonly System.Windows.Forms.Timer timer = new() { Interval = 80 };
    private InputHooks? hooks;
    private CancellationTokenSource? prepareToken;
    private volatile CancellationTokenSource? activeToken;
    private long revision;
    private bool ready, paused, starting, exiting;
    private long popupUntil;
    private Work? work;
    private readonly List<int> registrations = [];
    private readonly string pausePath = Path.Combine(Program.DataDirectory, "paused");
    private sealed class Work : IDisposable
    {
        internal readonly CancellationTokenSource Cancel;
        internal readonly Stopwatch Clock = Stopwatch.StartNew();
        internal readonly string Focus;
        internal readonly long Revision;
        internal volatile bool CanReplace;
        internal string? CapturedRaw;
        internal bool CaptureFull;
        internal string Language = "en";
        internal Work(int seconds, string focus, long revision)
        { Cancel = new(TimeSpan.FromSeconds(seconds)); Focus = focus; Revision = revision; CanReplace = true; }
        public void Dispose() => Cancel.Dispose();
    }
    internal MainForm(Settings settings)
    {
        cfg = settings; cfg.Log = msg => Program.Log(cfg, msg);
        Program.Log(cfg, $"SESSION build={cfg.BuildStamp}");
        if (File.Exists(FeatureUi.TermsPath))
        {
            try
            {
                if (new FileInfo(FeatureUi.TermsPath).Length > 2_000_000) throw new InvalidDataException();
                cfg.Terms = Terminology.Parse(File.ReadAllText(FeatureUi.TermsPath, new System.Text.UTF8Encoding(false, true)).TrimStart('\uFEFF'));
            }
            catch { MessageBox.Show("自定义词库无法读取或格式无效，本次未启用。请重新导入；原文件保留。", "空格翻译"); }
        }
        client = new(cfg); paused = File.Exists(pausePath);
        Text = "空格翻译 " + cfg.BuildStamp; AutoScaleMode = AutoScaleMode.Dpi;
        Font = new Font("Microsoft YaHei UI", 10); BackColor = BrandTheme.Background; ForeColor = BrandTheme.Ink;
        ClientSize = new Size(620, 522); FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false; StartPosition = FormStartPosition.CenterScreen;
        activeIcon = BrandIcons.Load("app", SystemInformation.SmallIconSize.Width);
        pausedIcon = BrandIcons.Load("paused", SystemInformation.SmallIconSize.Width);
        windowIcon = BrandIcons.Load("app", 48); Icon = windowIcon;
        var title = new Label { Text = "空格翻译", Font = new Font(Font.FontFamily, 26, FontStyle.Bold), AutoSize = true, Location = new Point(30, 24) };
        var subtitle = new Label { Text = "写完内容，空格两下。", ForeColor = BrandTheme.Muted, AutoSize = true, Location = new Point(33, 81) };
        var tag = new Label { Text = "by linthon · 方赋海", ForeColor = BrandTheme.Blue, AutoSize = true, Location = new Point(377, 88) };
        var card = new Panel { BackColor = Color.White, Location = new Point(32, 129), Size = new Size(556, 139), Padding = new Padding(20) };
        headline = new Label { Text = "正在检查翻译环境…", Font = new Font(Font, FontStyle.Bold), Location = new Point(18, 17), Size = new Size(520, 27) };
        detail = new Label { Text = "将复用你电脑上的 Ollama。", ForeColor = BrandTheme.Muted, Location = new Point(18, 51), Size = new Size(514, 52) };
        progress = new BrandProgressBar { Location = new Point(20, 116), Size = new Size(514, 5), Style = ProgressBarStyle.Continuous };
        card.Controls.AddRange([headline, detail, progress]);
        shortcuts = new Label { Location = new Point(35, 289), Size = new Size(558, 60), ForeColor = BrandTheme.Ink, Text = $"{cfg.ToggleHotkey}    暂停 / 恢复所有翻译\n{cfg.EnglishHotkey}    译成英文       {cfg.ChineseHotkey}    译成中文" };
        prepare = ButtonAt("准备模型", 32, 365, 170, true);
        toggle = ButtonAt("暂停翻译", 214, 365, 170, false);
        background = ButtonAt("隐藏到后台", 396, 365, 192, false);
        prepare.Click += async (_, _) => await PrepareAsync();
        toggle.Click += (_, _) => Toggle(); background.Click += (_, _) => Hide();
        var configLink = new LinkLabel { Text = "编辑配置", AutoSize = true, Location = new Point(35, 432), LinkColor = BrandTheme.DarkBlue };
        configLink.LinkClicked += (_, _) => OpenNotepad();
        var installLink = new LinkLabel { Text = "安装 / 更新 Ollama", AutoSize = true, Location = new Point(141, 432), LinkColor = configLink.LinkColor };
        installLink.LinkClicked += (_, _) => OpenUrl("https://ollama.com/download/windows");
        var exitLink = new LinkLabel { Text = "退出", AutoSize = true, Location = new Point(544, 432), LinkColor = configLink.LinkColor };
        exitLink.LinkClicked += (_, _) => Exit();
        foreach (var link in new[] { configLink, installLink, exitLink })
        { link.ActiveLinkColor = BrandTheme.Blue; link.VisitedLinkColor = BrandTheme.DarkBlue; }
        Controls.AddRange([title, subtitle, tag, card, shortcuts, prepare, toggle, background, configLink, installLink, exitLink]);
        var menu = new ContextMenuStrip();
        var languageMenu = new ToolStripMenuItem("双空格目标语言");
        var languageItems = new Dictionary<string, ToolStripMenuItem>();
        ComboBox? mainPicker = null;
        void SelectLanguage(string code)
        {
            if (cfg.TargetLanguage == code) return;
            string previous = cfg.TargetLanguage;
            cfg.TargetLanguage = code;
            try { cfg.Save(Program.ConfigPath); }
            catch { cfg.TargetLanguage = previous; MessageBox.Show("目标语言保存失败，请检查配置目录权限。", "空格翻译"); }
            foreach (var pair in languageItems) pair.Value.Checked = pair.Key == cfg.TargetLanguage;
            if (mainPicker != null) mainPicker.SelectedIndex = Languages.Names.Keys.ToList().IndexOf(cfg.TargetLanguage);
            popup.SetLanguage(cfg.TargetLanguage);
            Notice($"双空格目标语言：{Languages.Names[cfg.TargetLanguage]}\n下次生效；新增语言为试用，仅交付剪贴板。");
        }
        foreach (var pair in Languages.Names)
        {
            string code = pair.Key;
            var item = new ToolStripMenuItem(pair.Value, null, (_, _) => SelectLanguage(code)) { Checked = code == cfg.TargetLanguage };
            languageItems.Add(code, item); languageMenu.DropDownItems.Add(item);
        }
        mainPicker = FeatureUi.LanguagePicker(cfg.TargetLanguage, SelectLanguage);
        mainPicker.Location = new Point(140, 470); mainPicker.Width = 135;
        Controls.Add(new Label { Text = "双空格译成", AutoSize = true, Location = new Point(35, 474), ForeColor = BrandTheme.DarkBlue });
        Controls.Add(mainPicker);
        var importButton = ButtonAt("导入词库", 293, 464, 130, false);
        var exportButton = ButtonAt("导出日志", 440, 464, 148, false);
        importButton.Click += (_, _) => FeatureUi.ImportTerms(this, cfg);
        exportButton.Click += (_, _) => FeatureUi.ExportLog(this, cfg);
        Controls.AddRange([importButton, exportButton]);
        popup.ConfigureLanguage(cfg.TargetLanguage, SelectLanguage);
        menu.Items.Add(languageMenu);
        menu.Items.Add("选择目标语言（右下角）", null, (_, _) => { Notice("选择下一次双空格翻译的目标语言。"); popupUntil = Environment.TickCount64 + 15000; });
        menu.Items.Add("导入自定义词库…", null, (_, _) => FeatureUi.ImportTerms(this, cfg));
        menu.Items.Add("导出故障日志…", null, (_, _) => FeatureUi.ExportLog(this, cfg));
        trayToggle = new ToolStripMenuItem("暂停翻译", null, (_, _) => Toggle());
        menu.Items.Add("打开", null, (_, _) => { Show(); WindowState = FormWindowState.Normal; Activate(); });
        retryCopy = new ToolStripMenuItem("重试复制译文", null, (_, _) =>
        {
            if (work != null) { Notice("请等待当前翻译完成后再复制。"); return; }
            if (undeliveredTranslation != null) DeliverTranslation(undeliveredTranslation);
        }) { Enabled = false };
        menu.Items.Add(retryCopy);
        menu.Items.Add(trayToggle); menu.Items.Add("退出", null, (_, _) => Exit());
        tray = new NotifyIcon { Icon = pausedIcon, Visible = true, Text = "空格翻译 · 准备中", ContextMenuStrip = menu };
        tray.DoubleClick += (_, _) => { Show(); Activate(); };
        timer.Tick += (_, _) => Tick(); timer.Start();
        Shown += async (_, _) =>
        {
            try
            {
                try { RuntimeHelpers.RunClassConstructor(typeof(ProtectedText).TypeHandle); }
                catch (TypeInitializationException tie)
                {
                    Program.Log(cfg, "ERROR stage=initialization type=TypeInitializationException");
                    Program.Log(cfg, "FATAL ProtectedText cctor: " + tie.ToString());
                    if (tie.InnerException != null) Program.Log(cfg, "FATAL Inner: " + tie.InnerException.ToString());
                    State("文本保护模块初始化失败", "详细错误已写入诊断日志；请重启工具。不要重新下载模型，这不是模型故障。");
                    prepare.Enabled = false; tray.Text = "空格翻译 · 初始化失败";
                    return;
                }
                Register(1, cfg.ToggleHotkey); Register(2, cfg.EnglishHotkey); Register(3, cfg.ChineseHotkey);
                hooks = new InputHooks(cfg.DoubleSpaceMilliseconds, PhysicalActivity, () =>
                {
                    string focus = Native.FocusStamp(); long v = Interlocked.Read(ref revision);
                    Post(() => _ = TranslateAsync(true, true, focus, v));
                });
                Program.Log(cfg, $"START Exe={Application.ExecutablePath} Build={cfg.BuildStamp} Model={cfg.Model} Diagnostics={cfg.LogDiagnostics}");
                await CheckAsync();
            }
            catch (Exception e) { State("无法启动全局快捷键", e.Message); prepare.Enabled = false; }
        };
    }
    private Button ButtonAt(string caption, int x, int y, int width, bool primary)
    {
        var b = new Button { Text = caption, Location = new Point(x, y), Size = new Size(width, 45), FlatStyle = FlatStyle.Flat,
            BackColor = primary ? BrandTheme.Blue : Color.White,
            ForeColor = primary ? Color.White : BrandTheme.DarkBlue, Cursor = Cursors.Hand };
        b.FlatAppearance.BorderColor = primary ? b.BackColor : BrandTheme.Border;
        b.FlatAppearance.MouseOverBackColor = primary ? BrandTheme.DarkBlue : BrandTheme.Hover;
        b.FlatAppearance.MouseDownBackColor = primary ? BrandTheme.DarkBlue : BrandTheme.Border; return b;
    }
    private void Register(int id, string text)
    {
        var key = HotkeySpec.Parse(text);
        bool ok = Native.RegisterHotKey(Handle, id, key.Modifiers | 0x4000, key.Key);
        Program.Log(cfg, $"REGISTER id={id} text={text} mod={key.Modifiers} vk={key.Key} success={ok}");
        if (!ok) throw new IOException($"{text} 被其他程序占用。请退出旧翻译工具，或编辑配置后重新启动。");
        registrations.Add(id);
    }
    protected override void WndProc(ref Message m)
    {
        if (m.Msg == 0x312)
        {
            int id = (int)m.WParam;
            Program.Log(cfg, $"WM_HOTKEY id={id} wnd={Handle} focused_our_fg={Native.OurForeground()}");
            if (id == 1) Toggle();
            else if (id is 2 or 3)
            {
                bool en = id == 2;
                Program.Log(cfg, $"HK{(en ? "E" : "Z")} trigger");
                _ = TranslateAsync(en, false, Native.FocusStamp(), Interlocked.Read(ref revision));
            }
        }
        base.WndProc(ref m);
    }
    private void PhysicalActivity()
    {
        Interlocked.Increment(ref revision);
        // Keep the low-level hook callback free of network cancellation work.
        // The UI timer cancels within 80 ms; every editor write checks revision first.
    }
    private void Post(Action action)
    { if (!IsDisposed && IsHandleCreated) { try { BeginInvoke(action); } catch (InvalidOperationException) { } } }
    private void State(string title, string description)
    { headline.Text = title; detail.Text = description; }
    private void Notice(string text)
    { popupUntil = Environment.TickCount64 + 3200; popup.Message(text); }
    private void RefreshState()
    {
        if (hooks != null) { hooks.Enabled = ready && !paused; hooks.RequestReset(); }
        toggle.Text = paused ? "恢复翻译" : "暂停翻译"; trayToggle.Text = toggle.Text;
        tray.Icon = ready && !paused ? activeIcon : pausedIcon;
        tray.Text = ready ? (paused ? "空格翻译 · 已暂停" : "空格翻译 · 双空格翻译") : "空格翻译 · 尚未就绪";
        if (ready) State(paused ? "已暂停" : "准备好了", paused ? $"剪辑或游戏时不会触发翻译。按 {cfg.ToggleHotkey} 恢复。" : "完成输入后按两次空格。翻译中继续输入或切窗，将改为剪贴板交付。");
        toggle.Enabled = ready;
    }
    private void Toggle()
    {
        if (!ready) { Notice("请先在主窗口准备翻译模型。"); return; }
        paused = !paused; try { activeToken?.Cancel(); } catch (ObjectDisposedException) { }
        if (hooks != null) hooks.RequestReset();
        Program.Log(cfg, $"TOGGLE paused={paused}");
        try { if (paused) File.WriteAllText(pausePath, "1"); else File.Delete(pausePath); }
        catch { Notice("状态已切换，但未能保存到下次启动。"); }
        RefreshState(); Notice(paused ? $"翻译已暂停\n{cfg.ToggleHotkey} 恢复" : "翻译已恢复\n写完内容，空格两下");
    }
    private async Task CheckAsync()
    {
        prepare.Enabled = false; toggle.Enabled = false;
        try
        {
            using var ct = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            bool present = await client.HasModelAsync(ct.Token);
            if (present) { await PrepareAsync(); return; }
            State("还差一个小模型", "点击“准备模型”，下载约 2.5 GB。只需首次下载，日常翻译无按次费用。");
        }
        catch { State("需要启动本地翻译服务", "点击“准备模型”，程序会尝试启动已安装的 Ollama。"); }
        finally { if (!exiting) prepare.Enabled = true; }
    }
    private static string? FindOllama()
    {
        var paths = new List<string> { Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Ollama", "ollama.exe") };
        paths.AddRange((Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator).Where(p => p.Length > 0).Select(p => Path.Combine(p.Trim('"'), "ollama.exe")));
        return paths.FirstOrDefault(File.Exists);
    }
    private async Task PrepareAsync()
    {
        if (prepareToken != null || exiting) return;
        using var prep = new CancellationTokenSource(TimeSpan.FromMinutes(20)); prepareToken = prep;
        prepare.Enabled = false; toggle.Enabled = false; ready = false; RefreshState();
        try
        {
            State("正在连接 Ollama", "首次准备包括下载和预热，完成后每次翻译最多等待 30 秒。");
            bool online;
            try { using var probe = CancellationTokenSource.CreateLinkedTokenSource(prep.Token); probe.CancelAfter(1800); using var tags = await client.GetAsync("api/tags", probe.Token); online = true; }
            catch { online = false; }
            if (!online)
            {
                string? path = FindOllama();
                if (path == null) throw new IOException("没有找到 Ollama。请点击下方“安装 / 更新 Ollama”，安装完成后回来点击重试。");
                Process.Start(new ProcessStartInfo(path, "serve") { UseShellExecute = false, CreateNoWindow = true });
                for (int i = 0; i < 12 && !online; i++)
                {
                    await Task.Delay(600, prep.Token);
                    try { using var p = CancellationTokenSource.CreateLinkedTokenSource(prep.Token); p.CancelAfter(1200); using var tags = await client.GetAsync("api/tags", p.Token); online = true; }
                    catch (OperationCanceledException) when (!prep.IsCancellationRequested) { }
                    catch (HttpRequestException) { }
                }
                if (!online) throw new IOException("Ollama 没有正常启动。请从开始菜单打开 Ollama；仍失败时更新 Ollama 后重试。");
            }
            using (var probe = CancellationTokenSource.CreateLinkedTokenSource(prep.Token))
            {
                probe.CancelAfter(3000);
                if (!await client.HasModelAsync(probe.Token))
                {
                    State("正在准备翻译模型", "下载进度会显示在这里；中断后可重新准备。不会下载其他大型模型。");
                    await client.PullAsync((message, value) => Post(() =>
                    {
                        detail.Text = message;
                        progress.Style = value.HasValue ? ProgressBarStyle.Continuous : ProgressBarStyle.Marquee;
                        if (value.HasValue) progress.Value = (int)(100 * value.Value);
                    }), prep.Token);
                }
            }
            State("正在预热模型", "首次加载可能需要 1–2 分钟。日常翻译会显示 30 秒倒计时。");
            progress.Style = ProgressBarStyle.Marquee;
            using (var warm = CancellationTokenSource.CreateLinkedTokenSource(prep.Token))
            { warm.CancelAfter(TimeSpan.FromMinutes(2)); await client.TranslateAsync("你好，请问需要多少件？", true, warm.Token); }
            if (exiting) return;
            ready = true; progress.Style = ProgressBarStyle.Continuous; progress.Value = 100;
            prepare.Text = "重新检查模型"; RefreshState(); Notice(paused ? "模型已就绪，翻译仍处于暂停状态。" : "空格翻译已就绪\n可以隐藏到后台使用了");
        }
        catch (OperationCanceledException)
        { if (!exiting) State("准备已停止", "网络下载或首次加载超时，请重试。已下载的模型无需重新完整下载。"); }
        catch (Exception e)
        { if (!exiting) State("暂时没有准备好", Friendly(e)); }
        finally
        { prepareToken = null; if (!exiting) { prepare.Enabled = true; progress.Style = ProgressBarStyle.Continuous; RefreshState(); } }
    }
    private bool SafeStrict(Work job) => !paused && !exiting && !job.Cancel.IsCancellationRequested &&
        Interlocked.Read(ref revision) == job.Revision && Native.FocusStamp() == job.Focus;
    private bool SafeLoose(Work job) => !paused && !exiting && !job.Cancel.IsCancellationRequested;
    private void DeliverTranslation(string output)
    {
        // Keep one failed delivery in memory only, so retrying does not rerun the model.
        undeliveredTranslation = output; retryCopy.Enabled = true;
        try
        {
            ClipboardTransaction.Deliver(new WindowsClipboard(), output);
            undeliveredTranslation = null; retryCopy.Enabled = false;
            Program.Log(cfg, "DELIVER clipboard_only");
            Notice("翻译完成，译文已放入剪贴板。\n按 Ctrl+V 手动粘贴。");
        }
        catch (Exception e)
        {
            Program.Log(cfg, "DELIVER failed " + e.GetType().Name);
            Notice("翻译完成，但未能写入剪贴板。\n关闭占用后，从托盘菜单“重试复制译文”。");
        }
    }
    private async Task<bool> AwaitSettle(int milliseconds, int stepMs, Work job, string focus, long rev)
    {
        int waited = 0;
        while (waited < milliseconds)
        {
            await Task.Delay(stepMs);
            waited += stepMs;
            if (paused || exiting || job.Cancel.IsCancellationRequested) return false;
            if (Native.FocusStamp() != focus) return false;
            if (Interlocked.Read(ref revision) != rev) return false;
        }
        return true;
    }
    private async Task TranslateAsync(bool english, bool spaces, string focus, long atTrigger)
    {
        string targetLanguage = spaces ? cfg.TargetLanguage : english ? "en" : "zh";
        bool ourWindow = Native.OurForeground();
        if (!ready || paused || starting || work != null || (spaces && ourWindow)) { Program.Log(cfg, "PRE early_return ready="+ready+" paused="+paused+" starting="+starting+" work="+(work!=null)+" ourFG="+ourWindow+" spaces="+spaces+" english="+english); return; }
        if (spaces && Native.Composing()) { Program.Log(cfg, "PRE composing_skip spaces="+spaces); return; }
        starting = true;
        try
        {
            for (int i = 0; Native.ModifiersDown() && i < 60; i++) await Task.Delay(15);
            long nowRev = Interlocked.Read(ref revision);
            string nowFocus = Native.FocusStamp();
            if (Native.ModifiersDown() || (!ourWindow && nowFocus != focus) || (spaces && nowRev != atTrigger) || paused || exiting)
            {
                Program.Log(cfg, "PRE modifiers_or_focus_changed after wait ourFG="+ourWindow+" english="+english+" spaces="+spaces+" atTrigger="+atTrigger+" revNow="+nowRev+" focusSame="+(nowFocus==focus));
                return;
            }
            if (!spaces && nowFocus != focus) { focus = nowFocus; }
        }
        finally { starting = false; }
        if (work != null) { Program.Log(cfg, "PRE work_already_exists english="+english); return; }
        using var job = new Work(cfg.TimeoutSeconds, focus, Interlocked.Read(ref revision));
        job.Language = targetLanguage;
        // New languages have not passed native-speaker acceptance. Preserve the
        // original input while users evaluate these experimental translations.
        if (targetLanguage is not ("en" or "zh")) job.CanReplace = false;
        long captureRev = Interlocked.Read(ref revision);
        work = job; activeToken = job.Cancel; popupUntil = 0;
        bool full = spaces, selectedByUs = false, pasted = false;
        string? raw = null; bool capturedOk = false;
        string? output = null;
        string stage = "capture";
        try
        {
            if (spaces)
            {
                Program.Log(cfg, "DS begin_settle focus_changed="+ (Native.FocusStamp()!=focus));
                if (!await AwaitSettle(80, 10, job, focus, captureRev)) { Program.Log(cfg, "DS settle_cancel"); return; }
                if (Native.Composing()) { Program.Log(cfg, "DS composing_after_settle"); return; }
                Program.Log(cfg, "DS settle_ok start_capture");
            }
            else
            {
                Program.Log(cfg, "HK begin_capture full_init=False selected_init=False");
            }
            using (var tx = new ClipboardTransaction(new WindowsClipboard()))
            {
                try
                {
                    if (spaces)
                    {
                        Native.Send(0x41, 0x11); full = true; selectedByUs = true;
                    }
                    raw = await tx.CopyAsync(() => Native.Send(0x43, 0x11), () => SafeStrict(job), job.Cancel.Token);
                    if (!spaces)
                    {
                        if (string.IsNullOrWhiteSpace(raw) && SafeStrict(job))
                        {
                            Notice("选区为空：请先用鼠标选中要翻译的文字，再按快捷键。不要依赖自动全选。");
                            Program.Log(cfg, "CAPTURE hk_empty_after_copy no_auto_select");
                            return;
                        }
                        full = false; selectedByUs = false;
                    }
                    else if (raw == null && SafeStrict(job))
                    {
                        Native.Send(0x41, 0x11); full = true; selectedByUs = true;
                        raw = await tx.CopyAsync(() => Native.Send(0x43, 0x11), () => SafeStrict(job), job.Cancel.Token);
                    }
                    if (selectedByUs && SafeStrict(job)) { Native.Send(0x27); selectedByUs = false; }
                }
                catch (OperationCanceledException) { Program.Log(cfg, "CAPTURE activity_cancel"); return; }
                catch { throw; }
            }
            if (string.IsNullOrWhiteSpace(raw)) { Program.Log(cfg, "CAPTURE empty_raw"); Notice("没有复制到可翻译的文字。"); return; }
            int cjk = 0; int sampleMax = Math.Min(raw.Length, 120);
            for (int i = 0; i < sampleMax; i++) { char ch = raw[i]; if (ch >= 0x4E00 && ch <= 0x9FFF || ch >= 0x3400 && ch <= 0x4DBF) cjk++; }
            double cjkRatio = sampleMax == 0 ? 0 : (double)cjk / sampleMax;
            capturedOk = true;
            job.CapturedRaw = raw;
            job.CaptureFull = full;
            long postCaptureRev = Interlocked.Read(ref revision);
            if (!SafeStrict(job))
            {
                job.CanReplace = false;
                Program.Log(cfg, $"CAPTURE lose_replace_eligibility immediately capture={raw.Length} chars full={full} selByUs={selectedByUs}");
            }
            else
            {
                Program.Log(cfg, $"CAPTURE ok chars={raw.Length} full={full} selByUs={selectedByUs} cjkSample={cjk}/{sampleMax} ratio={cjkRatio:F2}");
            }
            Program.Log(cfg, "MODEL begin_request");
            stage = "model";
            Program.Log(cfg, $"TARGET code={targetLanguage} terms={cfg.Terms.Entries.Count}");
            output = await client.TranslateAsync(raw.Trim(), targetLanguage, job.Cancel.Token);
            Program.Log(cfg, $"MODEL end_request out_chars={output.Length}");
            if (!SafeLoose(job)) { Program.Log(cfg, "MODEL cancelled_by_pause_or_exit"); return; }
            if (job.CanReplace)
            {
                if (Native.FocusStamp() != focus || Interlocked.Read(ref revision) != captureRev)
                {
                    job.CanReplace = false;
                    Program.Log(cfg, $"REPLACE lose_eligibility_after_model focus={(Native.FocusStamp() == focus)} rev_match={(Interlocked.Read(ref revision) == captureRev)}");
                }
            }
            if (job.CanReplace)
            {
                stage = "replace";
                Program.Log(cfg, "REPLACE attempt_strict");
                using var tx = new ClipboardTransaction(new WindowsClipboard());
                try
                {
                    if (full) { Native.Send(0x41, 0x11); selectedByUs = true; }
                    string? check = await tx.CopyAsync(() => Native.Send(0x43, 0x11), () => SafeStrict(job), job.Cancel.Token);
                    if (!SafeStrict(job)) { Program.Log(cfg, "REPLACE strict_fail_before_put"); job.CanReplace = false; }
                    else if (check != raw) { Program.Log(cfg, "REPLACE raw_mismatch"); job.CanReplace = false; }
                }
                catch (OperationCanceledException)
                {
                    Program.Log(cfg, "REPLACE activity_cancel");
                    job.CanReplace = false;
                }
                if (job.CanReplace)
                {
                    tx.Put(output);
                    if (!SafeStrict(job)) { Program.Log(cfg, "REPLACE strict_fail_after_put"); job.CanReplace = false; }
                    else
                    {
                        Native.Send(0x56, 0x11); selectedByUs = false; pasted = true;
                        undeliveredTranslation = null; retryCopy.Enabled = false;
                        await Task.Delay(cfg.PasteSettleMilliseconds);
                        Program.Log(cfg, "REPLACE success_pasted");
                    }
                }
            }
            if (!job.CanReplace && !pasted && SafeLoose(job))
            {
                stage = "deliver";
                DeliverTranslation(output);
            }
        }
        catch (OperationCanceledException)
        {
            if (!capturedOk) { Program.Log(cfg, "CANCEL before_capture"); }
            else if (job.Clock.Elapsed.TotalSeconds >= cfg.TimeoutSeconds - .1) { Program.Log(cfg, "CANCEL timeout_after_capture"); Notice($"已到 {cfg.TimeoutSeconds} 秒，原文已保留。\n可缩短内容后再试。"); }
            else { Program.Log(cfg, "CANCEL user_after_capture"); }
        }
        catch (Exception e)
        {
            Program.Log(cfg, $"ERROR stage={stage} type={e.GetType().Name}");
            if (!pasted && output != null && SafeLoose(job)) DeliverTranslation(output);
            else Notice(pasted ? "文字已替换，但剪贴板恢复失败。" : Friendly(e));
        }
        finally
        {
            try
            {
                if (selectedByUs && SafeStrict(job)) Native.Send(0x27);
            }
            catch (Exception e) { Program.Log(cfg, "CLEANUP selection_failed " + e.GetType().Name); }
            finally
            {
                activeToken = null; work = null;
                Program.Log(cfg, $"JOB released elapsed_ms={job.Clock.ElapsedMilliseconds}");
                if (popupUntil == 0) popup.Hide();
            }
        }
    }
    private void Tick()
    {
        if (work != null)
        {
            bool strict = SafeStrict(work);
            if (!strict)
            {
                if (work.CapturedRaw == null)
                {
                    try { work.Cancel.Cancel(); } catch (ObjectDisposedException) { }
                }
                else
                {
                    work.CanReplace = false;
                }
            }
            popup.Message(work.CapturedRaw == null
                ? $"正在读取 · 剩余 {Math.Max(0, cfg.TimeoutSeconds - (int)work.Clock.Elapsed.TotalSeconds)} 秒\n继续输入或按 Esc 即可取消"
                : work.CanReplace
                    ? $"译成{Languages.Names[work.Language]} · 剩余 {Math.Max(0, cfg.TimeoutSeconds - (int)work.Clock.Elapsed.TotalSeconds)} 秒\n继续输入或切换窗口将改为仅放入剪贴板"
                    : $"译成{Languages.Names[work.Language]} · 剩余 {Math.Max(0, cfg.TimeoutSeconds - (int)work.Clock.Elapsed.TotalSeconds)} 秒\n翻译完成后按 Ctrl+V 粘贴");
        }
        else if (popupUntil != 0 && Environment.TickCount64 >= popupUntil && !popup.ChoosingLanguage) { popup.Hide(); popupUntil = 0; }
    }
    private static string Friendly(Exception e) => e switch
    {
        HttpRequestException h when h.StatusCode == HttpStatusCode.NotFound => "模型不存在或 Ollama 版本过旧，请重新准备或更新 Ollama。",
        HttpRequestException => "无法完成本地翻译。请确认 Ollama 正在运行；模型报错时可更新 Ollama 后重试。",
        System.Runtime.InteropServices.ExternalException => "剪贴板正被占用，请稍后重试。",
        JsonException => "Ollama 返回格式异常，请更新 Ollama 后重试。",
        _ => e.Message.Length > 170 ? e.Message[..170] : e.Message
    };
    private void OpenNotepad()
    {
        if (work != null) activeToken?.Cancel();
        Process.Start(new ProcessStartInfo("notepad.exe") { ArgumentList = { Program.ConfigPath }, UseShellExecute = false });
        Notice("保存配置后退出并重新打开程序生效。");
    }
    private static void OpenUrl(string url) => Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (!exiting && e.CloseReason == CloseReason.UserClosing) { e.Cancel = true; Hide(); return; }
        base.OnFormClosing(e);
    }
    private async void Exit()
    {
        if (exiting) return;
        exiting = true; prepareToken?.Cancel(); activeToken?.Cancel();
        // Let clipboard finally blocks complete before disposing UI/clipboard ownership.
        for (int i = 0; work != null && i < 30; i++) await Task.Delay(50);
        foreach (int id in registrations) Native.UnregisterHotKey(Handle, id);
        hooks?.Dispose(); timer.Stop(); timer.Dispose(); tray.Visible = false; tray.Dispose(); popup.Dispose();
        client.Dispose(); Close(); activeIcon.Dispose(); pausedIcon.Dispose(); windowIcon.Dispose();
    }
}
