using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.Web.WebView2.Core;
using System.Collections.ObjectModel;
using System.Text.Json;
using Windows.System;

namespace AIRBrowser;

public sealed partial class MainWindow : Window
{
    private readonly string _root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AIR Browser");
    private readonly ObservableCollection<string> _profiles = new() { "Personal", "Work" };
    private readonly List<Bookmark> _bookmarks = new();
    private string _profile = "Personal";
    private string _engine = "Bing";
    private bool _changingProfile;
    private WebView2? ActiveWeb => (BrowserTabs.SelectedItem as TabViewItem)?.Tag as WebView2;

    public MainWindow()
    {
        InitializeComponent();
        Title = "AIR Browser";
        ExtendsContentIntoTitleBar = false;
        Directory.CreateDirectory(_root);
        ProfilePicker.ItemsSource = _profiles;
        ProfilePicker.SelectedIndex = 0;
        SearchEnginePicker.ItemsSource = new[] { "Bing", "Google", "DuckDuckGo" };
        SearchEnginePicker.SelectedIndex = 0;
        LoadBookmarks();
        _ = AddTabAsync();
    }

    private string ProfileFolder => Path.Combine(_root, "Profiles", SafeName(_profile));
    private static string SafeName(string value) => string.Concat(value.Select(c => char.IsLetterOrDigit(c) || c == '-' ? c : '_'));

    private async Task AddTabAsync(string? address = null)
    {
        var tab = new TabViewItem { Header = "New tab" };
        var host = new Grid { Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Transparent) };
        var web = new WebView2();
        host.Children.Add(web);
        tab.Content = host;
        tab.Tag = web;
        BrowserTabs.TabItems.Add(tab);
        BrowserTabs.SelectedItem = tab;
        try
        {
            Directory.CreateDirectory(ProfileFolder);
            var environment = await CoreWebView2Environment.CreateAsync(null, ProfileFolder, null);
            await web.EnsureCoreWebView2Async(environment);
            web.CoreWebView2.Settings.AreDefaultContextMenusEnabled = true;
            web.CoreWebView2.Settings.IsStatusBarEnabled = false;
            web.CoreWebView2.DocumentTitleChanged += (_, _) =>
            {
                tab.Header = string.IsNullOrWhiteSpace(web.CoreWebView2.DocumentTitle) ? "New tab" : web.CoreWebView2.DocumentTitle;
            };
            web.CoreWebView2.NavigationStarting += (_, e) =>
            {
                AddressBox.Text = e.Uri;
                StatusText.Text = "Loading " + e.Uri;
            };
            web.CoreWebView2.NavigationCompleted += (_, e) =>
            {
                StatusText.Text = e.IsSuccess ? "Ready" : "This page could not be loaded";
                UpdateNavigationButtons();
                if (Uri.TryCreate(web.Source?.ToString(), UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https")
                    SaveHistory(uri.ToString(), tab.Header?.ToString() ?? uri.Host);
            };
            web.CoreWebView2.NewWindowRequested += (_, e) =>
            {
                e.Handled = true;
                AddTabAsync(e.Uri);
            };
            ShowStartPage(host);
            if (!string.IsNullOrWhiteSpace(address)) Navigate(address);
        }
        catch (Exception ex)
        {
            StatusText.Text = "WebView2 could not start: " + ex.Message;
            ShowStartPage(host);
        }
    }

    private void ShowStartPage(Grid host)
    {
        var panel = new Grid { Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(255, 8, 13, 22)), Margin = new Thickness(36) };
        var stack = new StackPanel { Spacing = 18, MaxWidth = 900, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        stack.Children.Add(new TextBlock { Text = "Welcome to AIR", FontSize = 38, FontWeight = Microsoft.UI.Text.FontWeights.Bold, Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.White) });
        stack.Children.Add(new TextBlock { Text = "A calmer way to browse.", FontSize = 17, Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(255, 150, 174, 205)) });
        var search = new TextBox { PlaceholderText = "Search the web or enter an address", Height = 48, FontSize = 16 };
        search.KeyDown += (_, e) => { if (e.Key == VirtualKey.Enter) { AddressBox.Text = search.Text; Navigate(search.Text); } };
        stack.Children.Add(search);
        var tiles = new Grid { ColumnSpacing = 12 };
        for (int i = 0; i < 3; i++) tiles.ColumnDefinitions.Add(new ColumnDefinition());
        string[] names = { "Bookmarks", "Weather widget", "Recent pages" };
        string[] details = { "Your saved websites appear here.", "Weather widget can be enabled in Start Page settings.", "Your browsing history stays in this profile." };
        for (int i = 0; i < names.Length; i++)
        {
            var card = new Border { Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(255, 17, 29, 46)), CornerRadius = new CornerRadius(12), Padding = new Thickness(18), Margin = new Thickness(i == 0 ? 0 : 4, 0, 4, 0) };
            var content = new StackPanel { Spacing = 8 };
            content.Children.Add(new TextBlock { Text = names[i], FontSize = 17, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.White) });
            content.Children.Add(new TextBlock { Text = details[i], TextWrapping = TextWrapping.Wrap, Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(255, 150, 174, 205)) });
            card.Child = content;
            Grid.SetColumn(card, i); tiles.Children.Add(card);
        }
        stack.Children.Add(tiles);
        panel.Children.Add(stack);
        host.Children.Add(panel);
    }

    private string SearchUrl(string query)
    {
        var baseUrl = _engine switch { "Google" => "https://www.google.com/search?q=", "DuckDuckGo" => "https://duckduckgo.com/?q=", _ => "https://www.bing.com/search?q=" };
        return baseUrl + Uri.EscapeDataString(query);
    }

    private void Navigate(string input)
    {
        var web = ActiveWeb;
        if (web?.CoreWebView2 is null) return;
        var value = input.Trim();
        if (string.IsNullOrWhiteSpace(value)) return;
        Uri target;
        if (Uri.TryCreate(value, UriKind.Absolute, out var parsed) && (parsed.Scheme == "http" || parsed.Scheme == "https"))
            target = parsed;
        else if (!value.Contains(' ') && value.Contains('.') && Uri.TryCreate("https://" + value, UriKind.Absolute, out parsed))
            target = parsed;
        else
        {
            web.CoreWebView2.Navigate(SearchUrl(value));
            return;
        }
        web.CoreWebView2.Navigate(target.ToString());
    }

    private void NewTab_Click(object sender, RoutedEventArgs e) => _ = AddTabAsync();
    private void BrowserTabs_AddTabButtonClick(TabView sender, object args) => _ = AddTabAsync();
    private void BrowserTabs_TabCloseRequested(TabView sender, TabViewTabCloseRequestedEventArgs args)
    {
        sender.TabItems.Remove(args.Tab);
        if (sender.TabItems.Count == 0) _ = AddTabAsync();
    }
    private void BrowserTabs_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ActiveWeb?.Source is not null) AddressBox.Text = ActiveWeb.Source.ToString();
        UpdateNavigationButtons();
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
        _bookmarks.Add(item); SaveBookmarks(); StatusText.Text = "Bookmark saved";
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
    private async void ProfilePicker_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_changingProfile || ProfilePicker.SelectedItem is not string selected || selected == _profile) return;
        _changingProfile = true;
        _profile = selected;
        _bookmarks.Clear(); LoadBookmarks();
        BrowserTabs.TabItems.Clear();
        await AddTabAsync();
        _changingProfile = false;
        StatusText.Text = "Profile: " + _profile;
    }
    private async void ShowSettings_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new ContentDialog
        {
            Title = "AIR Browser settings",
            Content = "This native preview uses separate browser data folders for each profile. AIR Sync, Microsoft/Google account connections, password manager, tab groups, widget management and theme editor are planned next modules. No account credentials are collected by this preview.",
            CloseButtonText = "Close",
            XamlRoot = BrowserTabs.XamlRoot
        };
        await dialog.ShowAsync();
    }
    private record Bookmark(string Url, string Title);
    private record HistoryEntry(string Url, string Title, DateTimeOffset VisitedAt);
}