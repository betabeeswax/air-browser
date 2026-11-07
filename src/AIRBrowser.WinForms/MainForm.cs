using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using System.Text.Json;

namespace AIRBrowser;

public sealed class MainForm : Form
{
    private readonly string _dataRoot;
    private readonly string _runtimeFolder;
    private readonly Dictionary<string, CoreWebView2Environment> _environments = new();
    private readonly Dictionary<TabPage, WebView2> _views = new();
    private readonly List<string> _bookmarks = new();
    private readonly List<string> _history = new();
    private readonly TabControl _tabs = new();
    private readonly TextBox _address = new();
    private readonly ComboBox _profile = new();
    private readonly ComboBox _groups = new();
    private readonly Panel _sidebar = new();
    private readonly ListBox _bookmarkList = new();
    private readonly Label _status = new();
    private readonly Button _sidebarToggle = new();
    private bool _verticalTabs;
    private bool _sidebarExpanded = true;
    private bool _creatingTab;

    private string CurrentProfile => _profile.SelectedItem?.ToString() ?? "Personal";
    private string ProfileData => Path.Combine(_dataRoot, "Profiles", SafeName(CurrentProfile));
    private string BookmarkFile => Path.Combine(ProfileData, "bookmarks.json");
    private string HistoryFile => Path.Combine(ProfileData, "history.json");
    private TabPage? ActivePage => _tabs.SelectedTab;
    private WebView2? ActiveView => ActivePage is not null && _views.TryGetValue(ActivePage, out var view) ? view : null;

    public MainForm()
    {
        Text = "AIR Browser";
        Name = "AIRBrowser";
        MinimumSize = new Size(900, 600);
        Size = new Size(1440, 900);
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Color.FromArgb(8, 13, 22);
        ForeColor = Color.FromArgb(232, 241, 255);
        Font = new Font("Segoe UI", 9F);
        Icon = LoadAppIcon();

        var portable = File.Exists(Path.Combine(AppContext.BaseDirectory, "portable.mode"));
        var portableRoot = Path.Combine(AppContext.BaseDirectory, "AIR-Browser-Data");
        var localRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AIR Browser");
        _dataRoot = portable && CanWrite(portableRoot) ? portableRoot : localRoot;
        Directory.CreateDirectory(_dataRoot);
        _runtimeFolder = Path.Combine(AppContext.BaseDirectory, "FixedRuntime", "Microsoft.WebView2.FixedVersionRuntime.156.0.4314.56.x64");

        BuildInterface();
        _profile.Items.AddRange(new object[] { "Personal", "Work" });
        _profile.SelectedIndex = 0;
        LoadProfileData();
        CreateTab();
        _status.Text = portable && _dataRoot == portableRoot
            ? "Portable mode • your data stays beside AIR Browser"
            : "Ready • AIR Browser";
    }

    private static Icon? LoadAppIcon()
    {
        try { return File.Exists(Path.Combine(AppContext.BaseDirectory, "AIRBrowser.exe")) ? Icon.ExtractAssociatedIcon(Path.Combine(AppContext.BaseDirectory, "AIRBrowser.exe")) : null; }
        catch { return null; }
    }

    private static bool CanWrite(string folder)
    {
        try { Directory.CreateDirectory(folder); var p = Path.Combine(folder, ".write-test"); File.WriteAllText(p, "ok"); File.Delete(p); return true; }
        catch { return false; }
    }

    private static string SafeName(string value) => string.Concat(value.Select(c => char.IsLetterOrDigit(c) || c == '-' ? c : '_'));

    private void BuildInterface()
    {
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 5, BackColor = BackColor, Margin = Padding.Empty };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 52));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 27));
        Controls.Add(root);

        var brandBar = new Panel { Dock = DockStyle.Fill, BackColor = Color.FromArgb(13, 23, 38), Padding = new Padding(12, 7, 12, 7) };
        var logo = new Label { Text = "A", BackColor = Color.FromArgb(23, 104, 214), ForeColor = Color.White, Font = new Font("Segoe UI", 18, FontStyle.Bold), TextAlign = ContentAlignment.MiddleCenter, Size = new Size(34, 34), Location = new Point(12, 8) };
        var brand = new Label { Text = "AIR", ForeColor = Color.FromArgb(235, 244, 255), Font = new Font("Segoe UI", 15, FontStyle.Bold), AutoSize = true, Location = new Point(55, 12) };
        _profile.DropDownStyle = ComboBoxStyle.DropDownList;
        _profile.Width = 130;
        _profile.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        _profile.Location = new Point(Width - 320, 10);
        _profile.SelectedIndexChanged += (_, _) => SwitchProfile();
        var newTab = MakeButton("+ New tab");
        newTab.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        newTab.Location = new Point(Width - 180, 9);
        newTab.Click += (_, _) => CreateTab();
        var menu = MakeButton("Menu");
        menu.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        menu.Location = new Point(Width - 95, 9);
        menu.Click += (_, _) => ShowMenu(menu);
        brandBar.Controls.AddRange(new Control[] { logo, brand, _profile, newTab, menu });
        brandBar.Resize += (_, _) => { _profile.Left = brandBar.ClientSize.Width - 320; newTab.Left = brandBar.ClientSize.Width - 180; menu.Left = brandBar.ClientSize.Width - 95; };
        root.Controls.Add(brandBar, 0, 0);

        var groupBar = new FlowLayoutPanel { Dock = DockStyle.Fill, BackColor = Color.FromArgb(16, 28, 45), Padding = new Padding(10, 5, 8, 3), WrapContents = false };
        _groups.DropDownStyle = ComboBoxStyle.DropDownList;
        _groups.Width = 170;
        _groups.Items.AddRange(new object[] { "All tabs", "Ungrouped" });
        _groups.SelectedIndex = 0;
        groupBar.Controls.Add(new Label { Text = "TAB GROUP", ForeColor = Color.FromArgb(143, 168, 200), AutoSize = true, Padding = new Padding(0, 7, 4, 0) });
        groupBar.Controls.Add(_groups);
        var addGroup = MakeButton("+ Group");
        addGroup.Click += (_, _) => { var name = "Group " + (_groups.Items.Count - 1); _groups.Items.Add(name); _groups.SelectedItem = name; };
        groupBar.Controls.Add(addGroup);
        root.Controls.Add(groupBar, 0, 1);

        var nav = new FlowLayoutPanel { Dock = DockStyle.Fill, BackColor = Color.FromArgb(13, 23, 38), Padding = new Padding(8, 5, 8, 4), WrapContents = false };
        foreach (var (label, action) in new (string, Action)[] {
            ("←", () => ActiveView?.CoreWebView2?.GoBack()),
            ("→", () => ActiveView?.CoreWebView2?.GoForward()),
            ("↻", () => ActiveView?.CoreWebView2?.Reload())
        }) { var b = MakeButton(label); b.Width = 38; b.Click += (_, _) => action(); nav.Controls.Add(b); }
        _address.Dock = DockStyle.None;
        _address.Width = 600;
        _address.Height = 30;
        _address.Margin = new Padding(8, 2, 8, 2);
        _address.Anchor = AnchorStyles.Left | AnchorStyles.Right;
        _address.BackColor = Color.FromArgb(25, 39, 58);
        _address.ForeColor = Color.White;
        _address.BorderStyle = BorderStyle.FixedSingle;
        _address.Font = new Font("Segoe UI", 10F);
        _address.KeyDown += AddressKeyDown;
        nav.Controls.Add(_address);
        var go = MakeButton("Go"); go.Click += (_, _) => NavigateAddress(); nav.Controls.Add(go);
        var bookmark = MakeButton("☆"); bookmark.Width = 40; bookmark.Click += (_, _) => SaveBookmark(); nav.Controls.Add(bookmark);
        var horizontal = MakeButton("Tabs"); horizontal.Click += (_, _) => SetTabOrientation(false); nav.Controls.Add(horizontal);
        var vertical = MakeButton("Vertical"); vertical.Click += (_, _) => SetTabOrientation(true); nav.Controls.Add(vertical);
        root.Controls.Add(nav, 0, 2);

        var body = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, BackColor = BackColor, Margin = Padding.Empty };
        body.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 210));
        body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        _sidebar.BackColor = Color.FromArgb(13, 23, 38);
        _sidebar.Dock = DockStyle.Fill;
        _sidebar.Padding = new Padding(8);
        _sidebarToggle.Text = "‹  Collapse";
        _sidebarToggle.Width = 185;
        _sidebarToggle.Click += (_, _) => ToggleSidebar(body);
        _bookmarkList.Dock = DockStyle.Fill;
        _bookmarkList.BackColor = Color.FromArgb(13, 23, 38);
        _bookmarkList.ForeColor = Color.FromArgb(225, 237, 252);
        _bookmarkList.BorderStyle = BorderStyle.None;
        _bookmarkList.DoubleClick += (_, _) => { if (_bookmarkList.SelectedItem is string url) Navigate(url); };
        var sideTitle = new Label { Text = "YOUR LIBRARY", ForeColor = Color.FromArgb(143, 168, 200), AutoSize = true, Padding = new Padding(2, 12, 0, 8) };
        var sideButtons = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 42, FlowDirection = FlowDirection.LeftToRight };
        var history = MakeButton("History"); history.Click += (_, _) => ShowHistory();
        var downloads = MakeButton("Downloads"); downloads.Click += (_, _) => System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads")) { UseShellExecute = true });
        sideButtons.Controls.Add(history); sideButtons.Controls.Add(downloads);
        _sidebar.Controls.Add(_bookmarkList);
        _sidebar.Controls.Add(sideTitle);
        _sidebar.Controls.Add(_sidebarToggle);
        _sidebar.Controls.Add(sideButtons);
        body.Controls.Add(_sidebar, 0, 0);

        _tabs.Dock = DockStyle.Fill;
        _tabs.Multiline = false;
        _tabs.DrawMode = TabDrawMode.Normal;
        _tabs.SelectedIndexChanged += (_, _) => UpdateAddress();
        body.Controls.Add(_tabs, 1, 0);
        root.Controls.Add(body, 0, 3);

        _status.Dock = DockStyle.Fill;
        _status.BackColor = Color.FromArgb(13, 23, 38);
        _status.ForeColor = Color.FromArgb(143, 168, 200);
        _status.TextAlign = ContentAlignment.MiddleLeft;
        _status.Padding = new Padding(10, 0, 0, 0);
        root.Controls.Add(_status, 0, 4);
    }

    private static Button MakeButton(string text) => new()
    {
        Text = text, AutoSize = true, Height = 30, FlatStyle = FlatStyle.Flat,
        BackColor = Color.FromArgb(31, 49, 72), ForeColor = Color.FromArgb(235, 244, 255),
        Margin = new Padding(3), Padding = new Padding(8, 2, 8, 2), Cursor = Cursors.Hand
    };

    private void ToggleSidebar(TableLayoutPanel body)
    {
        _sidebarExpanded = !_sidebarExpanded;
        body.ColumnStyles[0].Width = _sidebarExpanded ? 210 : 48;
        _sidebarToggle.Text = _sidebarExpanded ? "‹  Collapse" : "›";
        _bookmarkList.Visible = _sidebarExpanded;
    }

    private void SetTabOrientation(bool vertical)
    {
        _verticalTabs = vertical;
        _tabs.Alignment = vertical ? TabAlignment.Left : TabAlignment.Top;
        _tabs.SizeMode = vertical ? TabSizeMode.Fixed : TabSizeMode.Normal;
        _tabs.ItemSize = vertical ? new Size(38, 150) : new Size(0, 0);
    }

    private async void CreateTab(string? address = null)
    {
        if (_creatingTab) return;
        _creatingTab = true;
        try
        {
            var page = new TabPage("New tab") { BackColor = Color.FromArgb(8, 13, 22), Padding = Padding.Empty };
            var view = new WebView2 { Dock = DockStyle.Fill, CreationProperties = null };
            page.Controls.Add(view);
            _tabs.TabPages.Add(page);
            _views[page] = view;
            _tabs.SelectedTab = page;
            var profileKey = SafeName(CurrentProfile);
            var environment = await GetEnvironmentAsync(profileKey);
            await view.EnsureCoreWebView2Async(environment);
            view.CoreWebView2.Settings.AreDefaultContextMenusEnabled = true;
            view.CoreWebView2.Settings.IsStatusBarEnabled = false;
            view.CoreWebView2.Settings.AreBrowserAcceleratorKeysEnabled = true;
            view.CoreWebView2.DocumentTitleChanged += (_, _) => UpdateTabTitle(page, view);
            view.CoreWebView2.NavigationStarting += (_, e) => { if (IsHandleCreated) BeginInvoke(() => { if (ActivePage == page) _address.Text = e.Uri; }); };
            view.CoreWebView2.NavigationCompleted += (_, _) => { if (IsHandleCreated) BeginInvoke(() => { UpdateTabTitle(page, view); if (ActivePage == page) UpdateAddress(); }); };
            view.CoreWebView2.NewWindowRequested += (_, e) => { e.Handled = true; CreateTab(e.Uri); };
            view.CoreWebView2.DownloadStarting += (_, e) =>
            {
                var downloads = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
                Directory.CreateDirectory(downloads);
                var name = Path.GetFileName(e.ResultFilePath);
                if (!string.IsNullOrWhiteSpace(name)) e.ResultFilePath = Path.Combine(downloads, name);
                _status.Text = "Downloading " + name;
            };
            view.CoreWebView2.WebMessageReceived += (_, _) => { };
            if (!string.IsNullOrWhiteSpace(address)) view.CoreWebView2.Navigate(NormalizeAddress(address));
            else view.CoreWebView2.NavigateToString(StartPageHtml);
        }
        catch (Exception ex)
        {
            _status.Text = "Browser engine error: " + ex.Message;
            MessageBox.Show(this, "AIR Browser could not start its Chromium engine.\n\n" + ex.Message, "AIR Browser", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally { _creatingTab = false; }
    }

    private async Task<CoreWebView2Environment> GetEnvironmentAsync(string profile)
    {
        if (_environments.TryGetValue(profile, out var cached)) return cached;
        if (!File.Exists(Path.Combine(_runtimeFolder, "msedgewebview2.exe")))
            throw new FileNotFoundException("The bundled Chromium (WebView2) runtime is missing. Please reinstall AIR Browser.", _runtimeFolder);
        var userData = Path.Combine(_dataRoot, "WebView2", profile);
        Directory.CreateDirectory(userData);
        var environment = await CoreWebView2Environment.CreateAsync(_runtimeFolder, userData);
        _environments[profile] = environment;
        return environment;
    }

    private const string StartPageHtml = """
<!doctype html><html><head><meta charset="utf-8"><meta name="viewport" content="width=device-width"><title>New tab</title>
<style>*{box-sizing:border-box}body{margin:0;background:#080d16;color:#eaf3ff;font:16px Segoe UI,Arial;display:grid;place-items:center;min-height:100vh}.wrap{width:min(720px,90vw);text-align:center}.logo{margin:auto;background:#1768d6;border-radius:22px;width:76px;height:76px;display:grid;place-items:center;font-size:48px;font-weight:700}.brand{font-size:38px;font-weight:700;letter-spacing:8px;margin:18px}p{color:#8fa8c8}a{color:#7bb5ff;text-decoration:none;margin:12px}</style></head><body><main class="wrap"><div class="logo">A</div><div class="brand">AIR</div><p>Your browser. Your space.</p><p><a href="https://www.google.com">Google</a><a href="https://www.youtube.com">YouTube</a><a href="https://www.wikipedia.org">Wikipedia</a></p></main></body></html>
""";

    private void AddressKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; NavigateAddress(); }
    }

    private void NavigateAddress()
    {
        if (!string.IsNullOrWhiteSpace(_address.Text)) Navigate(_address.Text.Trim());
    }

    private void Navigate(string value)
    {
        var view = ActiveView;
        if (view?.CoreWebView2 is null) { CreateTab(value); return; }
        view.CoreWebView2.Navigate(NormalizeAddress(value));
    }

    private string NormalizeAddress(string value)
    {
        if (Uri.TryCreate(value, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
            return uri.ToString();
        if (value.Contains('.') && !value.Contains(' ') && Uri.TryCreate("https://" + value, UriKind.Absolute, out var host))
            return host.ToString();
        return "https://www.google.com/search?q=" + Uri.EscapeDataString(value);
    }

    private void UpdateAddress()
    {
        if (ActiveView?.CoreWebView2 is { } core) _address.Text = core.Source;
        else _address.Clear();
    }

    private void UpdateTabTitle(TabPage page, WebView2 view)
    {
        var title = view.CoreWebView2?.DocumentTitle;
        if (string.IsNullOrWhiteSpace(title)) title = "New tab";
        page.Text = title.Length > 24 ? title[..24] + "…" : title;
        if (ActivePage == page) Text = (title == "New tab" ? "AIR Browser" : title + " - AIR Browser");
    }

    private void SaveBookmark()
    {
        var url = ActiveView?.CoreWebView2?.Source;
        if (string.IsNullOrWhiteSpace(url)) return;
        if (!_bookmarks.Contains(url)) _bookmarks.Add(url);
        SaveProfileData();
        RefreshBookmarks();
        _status.Text = "Bookmark saved";
    }

    private void LoadProfileData()
    {
        Directory.CreateDirectory(ProfileData);
        _bookmarks.Clear(); _history.Clear();
        try { if (File.Exists(BookmarkFile)) _bookmarks.AddRange(JsonSerializer.Deserialize<List<string>>(File.ReadAllText(BookmarkFile)) ?? new()); } catch { }
        try { if (File.Exists(HistoryFile)) _history.AddRange(JsonSerializer.Deserialize<List<string>>(File.ReadAllText(HistoryFile)) ?? new()); } catch { }
        RefreshBookmarks();
    }

    private void SaveProfileData()
    {
        Directory.CreateDirectory(ProfileData);
        File.WriteAllText(BookmarkFile, JsonSerializer.Serialize(_bookmarks, new JsonSerializerOptions { WriteIndented = true }));
        File.WriteAllText(HistoryFile, JsonSerializer.Serialize(_history.TakeLast(500), new JsonSerializerOptions { WriteIndented = true }));
    }

    private void RefreshBookmarks()
    {
        _bookmarkList.Items.Clear();
        foreach (var item in _bookmarks) _bookmarkList.Items.Add(item);
    }

    private void ShowHistory()
    {
        var text = _history.Count == 0 ? "No browsing history yet." : string.Join(Environment.NewLine, _history.TakeLast(30).Reverse());
        MessageBox.Show(this, text, "AIR Browser history", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private void SwitchProfile()
    {
        if (_tabs.TabPages.Count == 0) return;
        foreach (TabPage page in _tabs.TabPages) page.Dispose();
        _tabs.TabPages.Clear(); _views.Clear();
        LoadProfileData();
        CreateTab();
        _status.Text = "Switched to " + CurrentProfile;
    }

    private void ShowMenu(Control owner)
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add("New tab", null, (_, _) => CreateTab());
        menu.Items.Add("New window", null, (_, _) => new MainForm().Show());
        menu.Items.Add("Bookmarks", null, (_, _) => { _sidebarExpanded = true; _bookmarkList.Visible = true; });
        menu.Items.Add("History", null, (_, _) => ShowHistory());
        menu.Items.Add("Open downloads", null, (_, _) => System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads")) { UseShellExecute = true }));
        menu.Items.Add("AIR Browser releases / updates", null, (_, _) => System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("https://github.com/betabeeswax/air-browser/releases/latest") { UseShellExecute = true }));
        menu.Show(owner, new Point(0, owner.Height));
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData == (Keys.Control | Keys.T)) { CreateTab(); return true; }
        if (keyData == (Keys.Control | Keys.W)) { CloseActiveTab(); return true; }
        if (keyData == (Keys.Control | Keys.L)) { _address.Focus(); _address.SelectAll(); return true; }
        if (keyData == (Keys.Control | Keys.D)) { SaveBookmark(); return true; }
        if (keyData == (Keys.Alt | Keys.Left)) { ActiveView?.CoreWebView2?.GoBack(); return true; }
        if (keyData == (Keys.Alt | Keys.Right)) { ActiveView?.CoreWebView2?.GoForward(); return true; }
        if (keyData == (Keys.Control | Keys.R)) { ActiveView?.CoreWebView2?.Reload(); return true; }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    private void CloseActiveTab()
    {
        if (ActivePage is not { } page) return;
        _views.Remove(page);
        _tabs.TabPages.Remove(page);
        page.Dispose();
        if (_tabs.TabPages.Count == 0) CreateTab();
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        try { SaveProfileData(); } catch { }
        base.OnFormClosing(e);
    }
}
