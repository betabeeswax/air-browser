using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using WinRT.Interop;
using Microsoft.Web.WebView2.Core;
using System.Collections.ObjectModel;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Windows.Foundation;
using Windows.System;
using Windows.ApplicationModel.DataTransfer;

namespace AIRBrowser;

public sealed partial class MainWindow : Window
{
    private readonly string _root;
    private readonly bool _portableMode;
    private readonly bool _portableDataFallback;
    private static readonly ConcurrentDictionary<string, Lazy<Task<CoreWebView2Environment>>> SharedWebViewEnvironments = new();
    private readonly ObservableCollection<string> _profiles = new() { "Personal", "Work" };
    private readonly ObservableCollection<string> _groups = new() { "Ungrouped" };
    private readonly List<TabEntry> _tabs = new();
    private readonly List<Bookmark> _bookmarks = new();
    private readonly Dictionary<string, PasswordRecord> _passwords = new();
    private string _profile = "Personal";
    private string _engine = "Bing";
    private bool _changingProfile;
    private bool _changingTabSelection;
    private bool _changingGroup;
    private bool _sidebarExpanded = true;
    private bool _verticalMode;
    private TabEntry? _activeEntry;
    private WebView2? ActiveWeb => _activeEntry?.Web;
    private TabEntry? ActiveEntry => _activeEntry;
    private string ProfileFolder => Path.Combine(_root, "Profiles", SafeName(_profile));
    private string PasswordFile => Path.Combine(ProfileFolder, "passwords.bin");
    private static string SafeName(string value) => string.Concat(value.Select(c => char.IsLetterOrDigit(c) || c == '-' ? c : '_'));

    public MainWindow()
    {
        var portableMarker = Path.Combine(AppContext.BaseDirectory, "portable.mode");
        _portableMode = File.Exists(portableMarker);
        var portableRoot = Path.Combine(AppContext.BaseDirectory, "AIR-Browser-Data");
        var localRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            _portableMode ? "AIR Browser Portable" : "AIR Browser");
        if (_portableMode && TryPrepareWritableFolder(portableRoot))
        {
            _root = portableRoot;
            _portableDataFallback = false;
        }
        else
        {
            _root = localRoot;
            _portableDataFallback = _portableMode;
            Directory.CreateDirectory(_root);
        }
        App.SetMainWindowXamlLoading(true);
        try
        {
            InitializeComponent();
            ApplyWindowIcon();
        }
        finally
        {
            App.SetMainWindowXamlLoading(false);
        }

        // Register keyboard shortcuts in code instead of XAML. This avoids
        // WinUI XAML loader failures while keeping the same browser shortcuts.
        AddKeyboardShortcut(VirtualKey.T, NewTab_Accelerator);
        AddKeyboardShortcut(VirtualKey.L, FocusAddress_Accelerator);
        AddKeyboardShortcut(VirtualKey.D, Bookmark_Accelerator);
        AddKeyboardShortcut(VirtualKey.W, CloseTab_Accelerator);

        Title = "AIR Browser";
        ExtendsContentIntoTitleBar = false;
        Directory.CreateDirectory(_root);
        ProfilePicker.ItemsSource = _profiles;
        ProfilePicker.SelectedIndex = 0;
        SearchEnginePicker.ItemsSource = new[] { "Bing", "Google", "DuckDuckGo" };
        SearchEnginePicker.SelectedIndex = 0;
        TabGroupPicker.ItemsSource = _groups;
        LoadBookmarks();
        LoadGroups();
        RefreshTabGroupsBar();
        LoadPasswords();
        RefreshVerticalTabs();
        SetTabOrientation(false);
        if (_portableDataFallback)
            StatusText.Text = "Portable folder is read-only. Browser data is being saved under your Windows user profile.";
        else if (_portableMode)
            StatusText.Text = "Portable mode • data is stored beside AIR Browser.";
        _ = AddTabAsync();
    }

    private void ApplyWindowIcon()
    {
        try
        {
            var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "air-browser.ico");
            if (!File.Exists(iconPath))
                iconPath = Path.Combine(AppContext.BaseDirectory, "air-browser.ico");
            if (!File.Exists(iconPath))
            {
                App.WriteDiagnostic("AIR Browser icon file was not found", null);
                return;
            }

            var hwnd = WindowNative.GetWindowHandle(this);
            var windowId = Win32Interop.GetWindowIdFromWindow(hwnd);
            AppWindow.GetFromWindowId(windowId).SetIcon(iconPath);
        }
        catch (Exception ex)
        {
            App.WriteDiagnostic("Could not apply AIR Browser window icon", ex);
        }
    }

    private void AddKeyboardShortcut(VirtualKey key, TypedEventHandler<KeyboardAccelerator, KeyboardAcceleratorInvokedEventArgs> handler)
    {
        var accelerator = new KeyboardAccelerator
        {
            Key = key,
            Modifiers = VirtualKeyModifiers.Control
        };
        accelerator.Invoked += handler;
        RootLayout.KeyboardAccelerators.Add(accelerator);
    }

    private static bool TryPrepareWritableFolder(string folder)
    {
        try
        {
            Directory.CreateDirectory(folder);
            var probe = Path.Combine(folder, ".air-write-test-" + Guid.NewGuid().ToString("N"));
            File.WriteAllText(probe, "ok");
            File.Delete(probe);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private Task<CoreWebView2Environment> GetSharedWebViewEnvironmentAsync()
    {
        var webViewData = Path.Combine(_root, "WebView2");
        Directory.CreateDirectory(webViewData);

        // AIR ships its own pinned WebView2 engine so it does not depend on
        // the Evergreen Runtime being installed separately on Windows.
        const string fixedRuntimeVersion = "156.0.4314.56";
        var fixedRuntimeFolder = Path.Combine(
            AppContext.BaseDirectory,
            "FixedRuntime",
            $"Microsoft.WebView2.FixedVersionRuntime.{fixedRuntimeVersion}.x64");
        var runtimeExecutable = Path.Combine(fixedRuntimeFolder, "msedgewebview2.exe");
        if (!File.Exists(runtimeExecutable))
            throw new FileNotFoundException(
                $"AIR's bundled WebView2 runtime ({fixedRuntimeVersion}) is missing. Reinstall AIR Browser.",
                runtimeExecutable);

        var environmentKey = webViewData + "|" + fixedRuntimeFolder;
        var lazy = SharedWebViewEnvironments.GetOrAdd(
            environmentKey,
            _ => new Lazy<Task<CoreWebView2Environment>>(
                async () => await CoreWebView2Environment.CreateWithOptionsAsync(
                    fixedRuntimeFolder,
                    webViewData,
                    new CoreWebView2EnvironmentOptions())));
        return lazy.Value;
    }

    private async Task AddTabAsync(string? address = null)
    {
        var tabButton = new Button
        {
            Content = "New tab",
            MinWidth = 132,
            MaxWidth = 220,
            Height = 32,
            Padding = new Thickness(12, 4, 12, 4),
            CornerRadius = new CornerRadius(9),
            Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(255, 28, 42, 60)),
            Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.White)
        };
        var host = new Grid { Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Transparent) };
        var web = new WebView2();
        host.Children.Add(web);
        var entry = new TabEntry(tabButton, web, host);
        tabButton.Click += (_, _) => SelectTab(entry);
        tabButton.CanDrag = true;
        tabButton.DragStarting += (_, e) =>
        {
            e.Data.SetText(entry.Id.ToString());
            e.AllowedOperations = DataPackageOperation.Move;
        };
        tabButton.AllowDrop = true;
        tabButton.DragOver += (_, e) =>
        {
            e.AcceptedOperation = DataPackageOperation.Move;
            e.Handled = true;
        };
        tabButton.Drop += async (_, e) => await DropTabOnTabAsync(e, entry);
        var closeButton = new Button { Content = "×", Padding = new Thickness(6, 2, 6, 2), MinWidth = 28, Height = 30, CornerRadius = new CornerRadius(8) };
        closeButton.Click += (_, _) => CloseTab(entry);
        var tabVisual = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2 };
        tabVisual.Children.Add(tabButton);
        tabVisual.Children.Add(closeButton);
        BrowserTabsPanel.Children.Add(tabVisual);
        entry.Visual = tabVisual;
        _tabs.Add(entry);
        SelectTab(entry);
        ShowEntry(entry);
        RefreshVerticalTabs();
        try
        {
            Directory.CreateDirectory(ProfileFolder);
            var environment = await GetSharedWebViewEnvironmentAsync();
            var controllerOptions = environment.CreateCoreWebView2ControllerOptions();
            controllerOptions.ProfileName = SafeName(_profile);
            await web.EnsureCoreWebView2Async(environment, controllerOptions);
            web.CoreWebView2.Settings.AreDefaultContextMenusEnabled = true;
            web.CoreWebView2.Settings.IsStatusBarEnabled = false;
            web.CoreWebView2.Settings.AreBrowserAcceleratorKeysEnabled = true;

            web.CoreWebView2.DownloadStarting += (_, download) =>
            {
                var downloads = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
                Directory.CreateDirectory(downloads);
                var fileName = Path.GetFileName(download.ResultFilePath);
                if (string.IsNullOrWhiteSpace(fileName)) fileName = "AIR-Download";
                download.ResultFilePath = Path.Combine(downloads, fileName);
                StatusText.Text = "Downloading " + fileName;
                download.DownloadOperation.StateChanged += (_, _) =>
                {
                    if (download.DownloadOperation.State == CoreWebView2DownloadState.Completed)
                        StatusText.Text = "Download complete: " + fileName;
                    else if (download.DownloadOperation.State == CoreWebView2DownloadState.Interrupted)
                        StatusText.Text = "Download interrupted: " + fileName;
                };
            };

            await web.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync("""
                document.addEventListener('submit', function(event) {
                    const form = event.target;
                    const password = form.querySelector('input[type="password"]');
                    if (!password || !password.value || !window.chrome?.webview) return;
                    const username = form.querySelector('input[autocomplete="username"],input[type="email"],input[type="text"]');
                    window.chrome.webview.postMessage(JSON.stringify({
                        type: 'air-save-password',
                        origin: location.origin,
                        username: username?.value || '',
                        password: password.value
                    }));
                }, true);
                """);
            web.CoreWebView2.WebMessageReceived += async (_, message) =>
            {
                try
                {
                    using var data = JsonDocument.Parse(message.TryGetWebMessageAsString());
                    var root = data.RootElement;
                    if (root.GetProperty("type").GetString() != "air-save-password") return;
                    var origin = root.GetProperty("origin").GetString() ?? "";
                    var username = root.GetProperty("username").GetString() ?? "";
                    var password = root.GetProperty("password").GetString() ?? "";
                    var senderUri = Uri.TryCreate(message.Source, UriKind.Absolute, out var sourceUri) ? sourceUri : null;
                    if (Uri.TryCreate(origin, UriKind.Absolute, out var originUri)
                        && originUri.Scheme is "https" or "http"
                        && senderUri is not null
                        && senderUri.GetLeftPart(UriPartial.Authority).Equals(originUri.GetLeftPart(UriPartial.Authority), StringComparison.OrdinalIgnoreCase)
                        && password.Length > 0)
                        await AskToSavePasswordAsync(originUri.GetLeftPart(UriPartial.Authority), username, password);
                }
                catch { }
            };

            Grid? startPage = null;
            web.CoreWebView2.DocumentTitleChanged += (_, _) =>
            {
                var title = string.IsNullOrWhiteSpace(web.CoreWebView2.DocumentTitle) ? "New tab" : web.CoreWebView2.DocumentTitle;
                entry.Tab.Content = entry.Group == "Ungrouped" ? title : $"● {entry.Group}  ·  {title}";
                RefreshVerticalTabs();
            };
            web.CoreWebView2.NavigationStarting += (_, e) =>
            {
                if (startPage is not null)
                {
                    host.Children.Remove(startPage);
                    startPage = null;
                }
                if (ActiveWeb == web) AddressBox.Text = e.Uri;
                StatusText.Text = "Loading " + e.Uri;
            };
            web.CoreWebView2.NavigationCompleted += async (_, e) =>
            {
                StatusText.Text = e.IsSuccess ? "Ready" : "This page could not be loaded";
                UpdateNavigationButtons();
                if (Uri.TryCreate(web.Source?.ToString(), UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https")
                {
                    SaveHistory(uri.ToString(), entry.Tab.Content?.ToString() ?? uri.Host);
                    if (_savedTabGroups.TryGetValue(uri.ToString(), out var group) && _groups.Contains(group))
                    {
                        entry.Group = group;
                        UpdateTabHeader(entry);
                    }
                    await TryAutofillAsync(web, uri);
                }
            };
            web.CoreWebView2.NewWindowRequested += (sender, e) =>
            {
                e.Handled = true;
                _ = AddTabAsync(e.Uri);
            };
            startPage = ShowStartPage(host);
            if (!string.IsNullOrWhiteSpace(address)) NavigateEntry(entry, address);
        }
        catch (Exception ex)
        {
            App.WriteDiagnostic("WebView2 initialization failed", ex);
            StatusText.Text = "Browser engine could not start. Details saved to %LOCALAPPDATA%\\AIR Browser\\startup.log";
            var errorPage = ShowStartPage(host);
            var errorPanel = new StackPanel { Spacing = 8, MaxWidth = 900, HorizontalAlignment = HorizontalAlignment.Center };
            errorPanel.Children.Add(new TextBlock
            {
                Text = "AIR could not start its browser engine.",
                Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Orange),
                FontSize = 16,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
            });
            errorPanel.Children.Add(new TextBlock
            {
                Text = ex.Message,
                TextWrapping = TextWrapping.Wrap,
                Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.White)
            });
            var installRuntime = new Button { Content = "Install Microsoft WebView2 Runtime" };
            installRuntime.Click += async (_, _) =>
                await Launcher.LaunchUriAsync(new Uri("https://developer.microsoft.com/microsoft-edge/webview2/"));
            errorPanel.Children.Add(installRuntime);
            errorPage.Children.Add(errorPanel);
        }
    }

    private void ShowEntry(TabEntry? entry)
    {
        ContentHost.Children.Clear();
        if (entry is not null) ContentHost.Children.Add(entry.Host);
        UpdateNavigationButtons();
        if (entry?.Web.Source is not null) AddressBox.Text = entry.Web.Source.ToString();
        else AddressBox.Text = "";
        _changingGroup = true;
        TabGroupPicker.SelectedItem = entry?.Group ?? "Ungrouped";
        _changingGroup = false;
    }

    private void RefreshVerticalTabs()
    {
        var selected = ActiveEntry;
        VerticalTabs.ItemsSource = null;
        VerticalTabs.ItemsSource = _tabs.Select(t => t.DisplayTitle).ToList();
        if (selected is not null)
        {
            var index = _tabs.IndexOf(selected);
            if (index >= 0) VerticalTabs.SelectedIndex = index;
        }
    }

    private void UpdateTabHeader(TabEntry entry)
    {
        var title = entry.Web.CoreWebView2?.DocumentTitle;
        if (string.IsNullOrWhiteSpace(title)) title = "New tab";
        entry.Tab.Content = entry.Group == "Ungrouped" ? title : $"● {entry.Group}  ·  {title}";
        RefreshVerticalTabs();
        RefreshTabGroupsBar();
        SaveGroups();
    }

    private void RefreshTabGroupsBar()
    {
        TabGroupsBar.Children.Clear();
        foreach (var group in _groups.Where(g => g != "Ungrouped"))
        {
            var count = _tabs.Count(t => t.Group == group);
            var button = new Button
            {
                Content = $"▦  {group}  ({count})",
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Left,
                Padding = new Thickness(11, 7, 9, 7),
                CornerRadius = new CornerRadius(8),
                Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(
                    ActiveEntry?.Group == group
                        ? Microsoft.UI.ColorHelper.FromArgb(255, 29, 91, 151)
                        : Microsoft.UI.ColorHelper.FromArgb(255, 25, 37, 53)),
                Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.White)
            };
            ToolTipService.SetToolTip(button, "Click to open this group. Drag a tab here to add it.");
            button.Click += (_, _) =>
            {
                var first = _tabs.FirstOrDefault(t => t.Group == group);
                if (first is not null) SelectTab(first);
            };
            button.AllowDrop = true;
            button.DragOver += (_, e) =>
            {
                e.AcceptedOperation = DataPackageOperation.Move;
                e.Handled = true;
            };
            button.Drop += async (_, e) => await DropTabIntoGroupAsync(e, group);
            TabGroupsBar.Children.Add(button);
        }
        var ungrouped = new Button
        {
            Content = $"＋  Ungrouped  ({_tabs.Count(t => t.Group == "Ungrouped")})",
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Left,
            Padding = new Thickness(11, 7, 9, 7),
            CornerRadius = new CornerRadius(8)
        };
        ungrouped.AllowDrop = true;
        ungrouped.DragOver += (_, e) => { e.AcceptedOperation = DataPackageOperation.Move; e.Handled = true; };
        ungrouped.Drop += async (_, e) => await DropTabIntoGroupAsync(e, "Ungrouped");
        TabGroupsBar.Children.Add(ungrouped);
    }

    private async Task<TabEntry?> GetDraggedTabAsync(DragEventArgs e)
    {
        if (!e.DataView.Contains(StandardDataFormats.Text)) return null;
        var id = await e.DataView.GetTextAsync();
        return _tabs.FirstOrDefault(t => t.Id.ToString() == id);
    }

    private async Task DropTabOnTabAsync(DragEventArgs e, TabEntry target)
    {
        var source = await GetDraggedTabAsync(e);
        if (source is null || source == target) return;

        string group;
        if (target.Group != "Ungrouped")
            group = target.Group;
        else if (source.Group != "Ungrouped")
            group = source.Group;
        else
        {
            var number = 1;
            while (_groups.Contains("Group " + number)) number++;
            group = "Group " + number;
            _groups.Add(group);
        }

        AssignGroup(source, group);
        AssignGroup(target, group);
        SelectTab(source);
        StatusText.Text = $"Created or updated {group}. Tabs are now grouped together.";
    }

    private async Task DropTabIntoGroupAsync(DragEventArgs e, string group)
    {
        var source = await GetDraggedTabAsync(e);
        if (source is null) return;
        AssignGroup(source, group);
        SelectTab(source);
        StatusText.Text = group == "Ungrouped" ? "Tab removed from its group" : $"Tab added to {group}";
    }

    private Grid ShowStartPage(Grid host)
    {
        var panel = new Grid { Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(255, 8, 13, 22)), Margin = new Thickness(36) };
        var stack = new StackPanel { Spacing = 18, MaxWidth = 900, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        stack.Children.Add(new TextBlock { Text = "Welcome to AIR", FontSize = 38, FontWeight = Microsoft.UI.Text.FontWeights.Bold, Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.White) });
        stack.Children.Add(new TextBlock { Text = "A calmer way to browse.", FontSize = 17, Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(255, 150, 174, 205)) });
        var search = new TextBox { PlaceholderText = "Search the web or enter an address", Height = 48, FontSize = 16 };
        search.KeyDown += (_, e) => { if (e.Key == VirtualKey.Enter) Navigate(search.Text); };
        stack.Children.Add(search);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
        var bookmarkButton = new Button { Content = "☆ Open bookmarks" };
        bookmarkButton.Click += ShowBookmarks_Click;
        var passwordButton = new Button { Content = "🔑 Password manager" };
        passwordButton.Click += ShowPasswords_Click;
        actions.Children.Add(bookmarkButton);
        actions.Children.Add(passwordButton);
        stack.Children.Add(actions);
        var tiles = new Grid { ColumnSpacing = 12 };
        for (int i = 0; i < 3; i++) tiles.ColumnDefinitions.Add(new ColumnDefinition());
        string[] names = { "Bookmarks", "Tab groups", "Private by profile" };
        string[] details = { "Save pages with the star button or Ctrl+D.", "Keep related tabs together in named groups.", "Your profile keeps its own browsing history." };
        for (int i = 0; i < names.Length; i++)
        {
            var card = new Border { Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(255, 17, 29, 46)), CornerRadius = new CornerRadius(12), Padding = new Thickness(18), Margin = new Thickness(4, 0, 4, 0) };
            var content = new StackPanel { Spacing = 8 };
            content.Children.Add(new TextBlock { Text = names[i], FontSize = 17, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.White) });
            content.Children.Add(new TextBlock { Text = details[i], TextWrapping = TextWrapping.Wrap, Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(255, 150, 174, 205)) });
            card.Child = content;
            Grid.SetColumn(card, i);
            tiles.Children.Add(card);
        }
        stack.Children.Add(tiles);
        panel.Children.Add(stack);
        host.Children.Add(panel);
        return panel;
    }

    private string SearchUrl(string query)
    {
        var baseUrl = _engine switch { "Google" => "https://www.google.com/search?q=", "DuckDuckGo" => "https://duckduckgo.com/?q=", _ => "https://www.bing.com/search?q=" };
        return baseUrl + Uri.EscapeDataString(query);
    }

    private void Navigate(string input)
    {
        var entry = ActiveEntry;
        if (entry is not null) NavigateEntry(entry, input);
    }

    private void NavigateEntry(TabEntry entry, string input)
    {
        var web = entry.Web;
        if (web.CoreWebView2 is null) return;
        var value = input.Trim();
        if (string.IsNullOrWhiteSpace(value)) return;
        if (Uri.TryCreate(value, UriKind.Absolute, out var parsed) && parsed.Scheme is "http" or "https")
            web.CoreWebView2.Navigate(parsed.ToString());
        else if (!value.Contains(' ') && value.Contains('.') && Uri.TryCreate("https://" + value, UriKind.Absolute, out parsed))
            web.CoreWebView2.Navigate(parsed.ToString());
        else web.CoreWebView2.Navigate(SearchUrl(value));
    }

    private void NewTab_Click(object sender, RoutedEventArgs e) => _ = AddTabAsync();
    private void SelectTab(TabEntry entry)
    {
        _activeEntry = entry;
        ShowEntry(entry);
        foreach (var item in _tabs)
            item.Tab.Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(
                ReferenceEquals(item, entry)
                    ? Microsoft.UI.ColorHelper.FromArgb(255, 23, 104, 214)
                    : Microsoft.UI.ColorHelper.FromArgb(255, 25, 39, 58));
        RefreshVerticalTabs();
        RefreshTabGroupsBar();
    }
    private void CloseTab(TabEntry entry)
    {
        var index = _tabs.IndexOf(entry);
        if (index < 0) return;
        _tabs.Remove(entry);
        if (entry.Visual.Parent is Panel panel) panel.Children.Remove(entry.Visual);
        if (ReferenceEquals(_activeEntry, entry))
        {
            _activeEntry = null;
            if (_tabs.Count == 0) { ContentHost.Children.Clear(); _ = AddTabAsync(); }
            else SelectTab(_tabs[Math.Clamp(index - 1, 0, _tabs.Count - 1)]);
        }
        RefreshVerticalTabs();
    }
    private void VerticalTabs_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_changingTabSelection || VerticalTabs.SelectedIndex < 0 || VerticalTabs.SelectedIndex >= _tabs.Count) return;
        _changingTabSelection = true;
        SelectTab(_tabs[VerticalTabs.SelectedIndex]);
        _changingTabSelection = false;
        ShowEntry(ActiveEntry);
    }
    private void HorizontalTabs_Click(object sender, RoutedEventArgs e) => SetTabOrientation(false);
    private void VerticalTabs_Click(object sender, RoutedEventArgs e) => SetTabOrientation(true);
    private void SetTabOrientation(bool vertical)
    {
        _verticalMode = vertical;
        BrowserTabsPanel.Visibility = vertical ? Visibility.Collapsed : Visibility.Visible;
        VerticalTabs.Visibility = vertical ? Visibility.Visible : Visibility.Collapsed;
        SidebarTitle.Text = vertical ? "VERTICAL TABS" : "AIR SIDEBAR";
        VerticalTabsButton.Background = vertical ? new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(255, 23, 104, 214)) : null;
        HorizontalTabsButton.Background = vertical ? null : new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(255, 23, 104, 214));
        StatusText.Text = vertical ? "Vertical tabs enabled" : "Horizontal tabs enabled";
    }
    private void SidebarToggle_Click(object sender, RoutedEventArgs e)
    {
        _sidebarExpanded = !_sidebarExpanded;
        SidebarColumn.Width = new GridLength(_sidebarExpanded ? 210 : 48);
        SidebarToggle.Content = _sidebarExpanded ? "◀" : "▶";
        SidebarTitle.Visibility = _sidebarExpanded ? Visibility.Visible : Visibility.Collapsed;
        GroupLabel.Visibility = _sidebarExpanded ? Visibility.Visible : Visibility.Collapsed;
        TabGroupPicker.Visibility = _sidebarExpanded ? Visibility.Visible : Visibility.Collapsed;
        NewGroupButton.Content = _sidebarExpanded ? "＋ Create tab group" : "＋";
        BookmarksSideButton.Content = _sidebarExpanded ? "☆  Bookmarks" : "☆";
        PasswordsSideButton.Content = _sidebarExpanded ? "🔑  Passwords" : "🔑";
        HistorySideButton.Content = _sidebarExpanded ? "◷  History" : "◷";
        DownloadsSideButton.Content = _sidebarExpanded ? "↓  Downloads" : "↓";
    }

    private void Go_Click(object sender, RoutedEventArgs e) => Navigate(AddressBox.Text);
    private void AddressBox_KeyDown(object sender, KeyRoutedEventArgs e) { if (e.Key == VirtualKey.Enter) Navigate(AddressBox.Text); }
    private void Back_Click(object sender, RoutedEventArgs e) { if (ActiveWeb?.CanGoBack == true) ActiveWeb.GoBack(); }
    private void Forward_Click(object sender, RoutedEventArgs e) { if (ActiveWeb?.CanGoForward == true) ActiveWeb.GoForward(); }
    private void Reload_Click(object sender, RoutedEventArgs e) => ActiveWeb?.Reload();
    private void UpdateNavigationButtons()
    {
        BackButton.IsEnabled = ActiveWeb?.CanGoBack == true;
        ForwardButton.IsEnabled = ActiveWeb?.CanGoForward == true;
    }
    private void SearchEnginePicker_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (SearchEnginePicker.SelectedItem is string engine) _engine = engine;
    }
    private void Bookmark_Click(object sender, RoutedEventArgs e)
    {
        var web = ActiveWeb;
        if (web?.Source is null || web.Source.Scheme is not ("http" or "https")) return;
        var item = new Bookmark(web.Source.ToString(), web.CoreWebView2?.DocumentTitle ?? web.Source.Host);
        if (_bookmarks.Any(b => b.Url == item.Url)) { StatusText.Text = "Already bookmarked"; return; }
        _bookmarks.Add(item);
        SaveBookmarks();
        StatusText.Text = "☆ Bookmark saved";
    }
    private void LoadBookmarks()
    {
        try
        {
            var path = Path.Combine(ProfileFolder, "bookmarks.json");
            if (File.Exists(path)) _bookmarks.AddRange(JsonSerializer.Deserialize<List<Bookmark>>(File.ReadAllText(path)) ?? new());
        }
        catch { }
    }
    private void SaveBookmarks()
    {
        Directory.CreateDirectory(ProfileFolder);
        File.WriteAllText(Path.Combine(ProfileFolder, "bookmarks.json"), JsonSerializer.Serialize(_bookmarks, new JsonSerializerOptions { WriteIndented = true }));
    }
    private void SaveHistory(string url, string title)
    {
        try
        {
            Directory.CreateDirectory(ProfileFolder);
            var path = Path.Combine(ProfileFolder, "history.json");
            var history = File.Exists(path) ? JsonSerializer.Deserialize<List<HistoryEntry>>(File.ReadAllText(path)) ?? new() : new();
            history.Add(new HistoryEntry(url, title, DateTimeOffset.UtcNow));
            File.WriteAllText(path, JsonSerializer.Serialize(history.TakeLast(1000)));
        }
        catch { }
    }

    private async void ShowBookmarks_Click(object sender, RoutedEventArgs e)
    {
        var items = _bookmarks.ToList();
        var list = new ListView { ItemsSource = items.Select(b => b.Title + Environment.NewLine + b.Url).ToList(), IsItemClickEnabled = true, MaxHeight = 420 };
        var dialog = new ContentDialog { Title = "Bookmarks — " + _profile, Content = list, CloseButtonText = "Close", XamlRoot = RootLayout.XamlRoot };
        list.ItemClick += (_, args) => { var index = list.Items.IndexOf(args.ClickedItem); if (index >= 0) { Navigate(items[index].Url); dialog.Hide(); } };
        await dialog.ShowAsync();
    }
    private async void ShowHistory_Click(object sender, RoutedEventArgs e)
    {
        var entries = new List<HistoryEntry>();
        try
        {
            var path = Path.Combine(ProfileFolder, "history.json");
            if (File.Exists(path)) entries = JsonSerializer.Deserialize<List<HistoryEntry>>(File.ReadAllText(path)) ?? new();
        }
        catch { }
        entries = entries.OrderByDescending(h => h.VisitedAt).ToList();
        var list = new ListView { ItemsSource = entries.Select(h => h.Title + Environment.NewLine + h.Url + Environment.NewLine + h.VisitedAt.ToLocalTime().ToString("g")).ToList(), IsItemClickEnabled = true, MaxHeight = 460 };
        var dialog = new ContentDialog { Title = "Browsing history — " + _profile, Content = list, PrimaryButtonText = "Clear history", CloseButtonText = "Close", XamlRoot = RootLayout.XamlRoot };
        list.ItemClick += (_, args) => { var index = list.Items.IndexOf(args.ClickedItem); if (index >= 0) { Navigate(entries[index].Url); dialog.Hide(); } };
        var result = await dialog.ShowAsync();
        if (result == ContentDialogResult.Primary)
        {
            File.WriteAllText(Path.Combine(ProfileFolder, "history.json"), "[]");
            StatusText.Text = "Browsing history cleared";
        }
    }
    private void ShowDownloads_Click(object sender, RoutedEventArgs e)
    {
        var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
        Directory.CreateDirectory(folder);
        Process.Start(new ProcessStartInfo("explorer.exe", folder) { UseShellExecute = true });
    }

    private async void NewGroup_Click(object sender, RoutedEventArgs e)
    {
        var nameBox = new TextBox { PlaceholderText = "Example: School, Shopping, Research", MaxLength = 32 };
        var dialog = new ContentDialog { Title = "Create a tab group", Content = nameBox, PrimaryButtonText = "Create group", CloseButtonText = "Cancel", XamlRoot = RootLayout.XamlRoot, DefaultButton = ContentDialogButton.Primary };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
        var name = nameBox.Text.Trim();
        if (name.Length == 0) return;
        if (!_groups.Contains(name)) _groups.Add(name);
        RefreshTabGroupsBar();
        SaveGroups();
        if (ActiveEntry is not null) AssignGroup(ActiveEntry, name);
        TabGroupPicker.SelectedItem = name;
    }
    private void TabGroupPicker_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_changingGroup || TabGroupPicker.SelectedItem is not string group || ActiveEntry is null) return;
        AssignGroup(ActiveEntry, group);
    }
    private void AssignGroup(TabEntry entry, string group)
    {
        entry.Group = group;
        UpdateTabHeader(entry);
        if (entry.Web.Source is not null) _savedTabGroups[entry.Web.Source.ToString()] = group;
        SaveGroups();
        RefreshTabGroupsBar();
        StatusText.Text = group == "Ungrouped" ? "Tab removed from its group" : "Tab added to " + group;
    }
    private readonly Dictionary<string, string> _savedTabGroups = new();
    private void LoadGroups()
    {
        try
        {
            var path = Path.Combine(ProfileFolder, "tab-groups.json");
            if (!File.Exists(path)) return;
            var state = JsonSerializer.Deserialize<GroupState>(File.ReadAllText(path));
            if (state is null) return;
            foreach (var group in state.Groups.Where(g => !string.IsNullOrWhiteSpace(g) && !_groups.Contains(g))) _groups.Add(group);
            foreach (var pair in state.TabGroups) _savedTabGroups[pair.Key] = pair.Value;
        }
        catch { }
    }
    private void SaveGroups()
    {
        try
        {
            Directory.CreateDirectory(ProfileFolder);
            var state = new GroupState(_groups.Where(g => g != "Ungrouped").ToList(), _savedTabGroups);
            File.WriteAllText(Path.Combine(ProfileFolder, "tab-groups.json"), JsonSerializer.Serialize(state, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { }
    }

    private void LoadPasswords()
    {
        try
        {
            if (!File.Exists(PasswordFile)) return;
            var encrypted = File.ReadAllBytes(PasswordFile);
            var clear = ProtectedData.Unprotect(encrypted, Encoding.UTF8.GetBytes("AIR Browser Passwords v1"), DataProtectionScope.CurrentUser);
            var records = JsonSerializer.Deserialize<List<PasswordRecord>>(clear) ?? new();
            foreach (var record in records) _passwords[record.Origin + "|" + record.Username] = record;
        }
        catch { _passwords.Clear(); }
    }
    private void SavePasswords()
    {
        Directory.CreateDirectory(ProfileFolder);
        var clear = JsonSerializer.SerializeToUtf8Bytes(_passwords.Values.ToList());
        var encrypted = ProtectedData.Protect(clear, Encoding.UTF8.GetBytes("AIR Browser Passwords v1"), DataProtectionScope.CurrentUser);
        File.WriteAllBytes(PasswordFile, encrypted);
    }
    private async Task AskToSavePasswordAsync(string origin, string username, string password)
    {
        var userBox = new TextBox { Header = "Username or email", Text = username };
        var passBox = new PasswordBox { Header = "Password", Password = password };
        var panel = new StackPanel { Spacing = 10 };
        panel.Children.Add(new TextBlock { Text = "AIR detected a sign-in form. Save this login encrypted on this Windows account?", TextWrapping = TextWrapping.Wrap });
        panel.Children.Add(new TextBlock { Text = origin, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
        panel.Children.Add(userBox);
        panel.Children.Add(passBox);
        var dialog = new ContentDialog { Title = "Save password?", Content = panel, PrimaryButtonText = "Save password", CloseButtonText = "Not now", XamlRoot = RootLayout.XamlRoot, DefaultButton = ContentDialogButton.Primary };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
        var record = new PasswordRecord(origin, userBox.Text.Trim(), passBox.Password);
        _passwords[origin + "|" + record.Username] = record;
        SavePasswords();
        StatusText.Text = "Password saved encrypted on this PC";
    }
    private async Task TryAutofillAsync(WebView2 web, Uri uri)
    {
        var record = _passwords.Values.FirstOrDefault(p => p.Origin.Equals(uri.GetLeftPart(UriPartial.Authority), StringComparison.OrdinalIgnoreCase));
        if (record is null) return;
        var user = JsonSerializer.Serialize(record.Username);
        var pass = JsonSerializer.Serialize(record.Password);
        var script = $"(()=>{{const p=document.querySelector('input[type=password]');if(!p)return;const u=document.querySelector('input[autocomplete=username],input[type=email],input[type=text]');if(u&&!u.value){{u.value={user};u.dispatchEvent(new Event('input',{{bubbles:true}}));}}if(!p.value){{p.value={pass};p.dispatchEvent(new Event('input',{{bubbles:true}}));}}}})();";
        try { await web.CoreWebView2.ExecuteScriptAsync(script); } catch { }
    }
    private async void ShowPasswords_Click(object sender, RoutedEventArgs e)
    {
        var records = _passwords.Values.OrderBy(p => p.Origin).ToList();
        var list = new ListView { ItemsSource = records.Select(p => p.Origin + Environment.NewLine + (string.IsNullOrEmpty(p.Username) ? "(no username)" : p.Username)).ToList(), MaxHeight = 360 };
        var dialog = new ContentDialog { Title = "Saved passwords (" + records.Count + ")", Content = list, PrimaryButtonText = "Fill selected", SecondaryButtonText = "Delete selected", CloseButtonText = "Close", XamlRoot = RootLayout.XamlRoot };
        var result = await dialog.ShowAsync();
        var index = list.SelectedIndex;
        if (index < 0 || index >= records.Count) return;
        var selected = records[index];
        if (result == ContentDialogResult.Primary && ActiveWeb?.Source is not null && ActiveWeb.Source.GetLeftPart(UriPartial.Authority).Equals(selected.Origin, StringComparison.OrdinalIgnoreCase))
            await TryAutofillAsync(ActiveWeb, ActiveWeb.Source);
        else if (result == ContentDialogResult.Secondary)
        {
            _passwords.Remove(selected.Origin + "|" + selected.Username);
            SavePasswords();
            StatusText.Text = "Saved login deleted";
        }
    }

    private async void ProfilePicker_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_changingProfile || ProfilePicker.SelectedItem is not string selected || selected == _profile) return;
        _changingProfile = true;
        _profile = selected;
        _bookmarks.Clear();
        _groups.Clear();
        _groups.Add("Ungrouped");
        _savedTabGroups.Clear();
        _passwords.Clear();
        LoadBookmarks();
        LoadGroups();
        LoadPasswords();
        RefreshTabGroupsBar();
        TabGroupPicker.ItemsSource = null;
        TabGroupPicker.ItemsSource = _groups;
        BrowserTabsPanel.Children.Clear();
        _activeEntry = null;
        _tabs.Clear();
        ContentHost.Children.Clear();
        RefreshVerticalTabs();
        await AddTabAsync();
        _changingProfile = false;
        StatusText.Text = "Profile: " + _profile;
    }

    private async void ShowSettings_Click(object sender, RoutedEventArgs e)
    {
        var orientation = new StackPanel { Spacing = 8 };
        orientation.Children.Add(new TextBlock { Text = "Choose how your tabs are displayed." });
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        var horizontal = new Button { Content = "Horizontal tabs" };
        horizontal.Click += (_, _) => { SetTabOrientation(false); };
        var vertical = new Button { Content = "Vertical tabs" };
        vertical.Click += (_, _) => { SetTabOrientation(true); };
        buttons.Children.Add(horizontal);
        buttons.Children.Add(vertical);
        orientation.Children.Add(buttons);
        orientation.Children.Add(new TextBlock { Text = "Password manager saves logins only after you approve. Passwords are encrypted for this Windows user account and are never synced in this preview.", TextWrapping = TextWrapping.Wrap });
        var dialog = new ContentDialog { Title = "AIR Browser settings", Content = orientation, CloseButtonText = "Close", XamlRoot = RootLayout.XamlRoot };
        await dialog.ShowAsync();
    }

    private void NewTab_Accelerator(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args) { _ = AddTabAsync(); args.Handled = true; }
    private void FocusAddress_Accelerator(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args) { AddressBox.Focus(FocusState.Programmatic); AddressBox.SelectAll(); args.Handled = true; }
    private void Bookmark_Accelerator(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args) { Bookmark_Click(this, new RoutedEventArgs()); args.Handled = true; }
    private void CloseTab_Accelerator(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        if (ActiveEntry is not null) CloseTab(ActiveEntry);
        args.Handled = true;
    }

    private sealed class TabEntry(Button tab, WebView2 web, Grid host)
    {
        public Guid Id { get; } = Guid.NewGuid();
        public Button Tab { get; } = tab;
        public FrameworkElement Visual { get; set; } = tab;
        public WebView2 Web { get; } = web;
        public Grid Host { get; } = host;
        public string Group { get; set; } = "Ungrouped";
        public string DisplayTitle => (Group == "Ungrouped" ? "" : "● " + Group + "  ·  ") + (Web.CoreWebView2?.DocumentTitle ?? "New tab");
    }
    private record Bookmark(string Url, string Title);
    private record HistoryEntry(string Url, string Title, DateTimeOffset VisitedAt);
    private record PasswordRecord(string Origin, string Username, string Password);
    private record GroupState(List<string> Groups, Dictionary<string, string> TabGroups);
}