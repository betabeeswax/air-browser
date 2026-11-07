using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using System.Text.Json;
using System.Diagnostics;
using System.Net.Http;

namespace AIRBrowser;

public sealed class MainForm : Form
{
    private readonly string _root;
    private readonly string _runtime;
    private readonly WebView2 _shell = new() { Dock = DockStyle.Fill };
    private readonly Panel _shellPanel = new() { Dock = DockStyle.Top, Height = 151 };
    private readonly Panel _browserPanel = new() { Dock = DockStyle.Fill, BackColor = Color.FromArgb(8,13,22) };
    private readonly List<TabEntry> _tabs = new();
    private readonly List<string> _groups = new() { "Ungrouped" };
    private readonly List<string> _profiles = new() { "Personal", "Work" };
    private string ProfilesFile => Path.Combine(_root, "profiles.json");
    private readonly Dictionary<string, CoreWebView2Environment> _environments = new();
    private TabEntry? _active;
    private string _profile = "Personal";
    private string _engine = "Bing";
    private string _status = "Starting AIR Browser…";
    private bool _shellReady;
    private string ProfileFolder => Path.Combine(_root,"Profiles",Safe(_profile));
    private static string Safe(string s)=>string.Concat(s.Select(c=>char.IsLetterOrDigit(c)||c=='-'?c:'_'));

    public MainForm()
    {
        Text="AIR Browser"; Name="AIRBrowser"; ShowIcon=true; MinimumSize=new Size(900,600); Size=new Size(1440,900);
        StartPosition=FormStartPosition.CenterScreen; BackColor=Color.FromArgb(8,13,22); Icon=LoadIcon();
        var portable=File.Exists(Path.Combine(AppContext.BaseDirectory,"portable.mode"));
        var portableRoot=Path.Combine(AppContext.BaseDirectory,"AIR-Browser-Data");
        _root=portable&&CanWrite(portableRoot)?portableRoot:Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"AIR Browser");
        Directory.CreateDirectory(_root);
        try { if (File.Exists(ProfilesFile)) { var saved = JsonSerializer.Deserialize<List<string>>(File.ReadAllText(ProfilesFile)); if (saved is { Count: > 0 }) { _profiles.Clear(); _profiles.AddRange(saved.Distinct(StringComparer.OrdinalIgnoreCase)); if (!_profiles.Contains("Personal")) _profiles.Insert(0, "Personal"); } } } catch { }
        _runtime=Path.Combine(AppContext.BaseDirectory,"FixedRuntime","Microsoft.WebView2.FixedVersionRuntime.156.0.4314.56.x64");
        _shellPanel.Controls.Add(_shell);
        Controls.Add(_browserPanel); Controls.Add(_shellPanel);
        Shown+=async (_,_)=>await InitializeShellAsync();
        FormClosing+=(_,_)=>{foreach(var t in _tabs)t.View.Dispose();_shell.Dispose();};
    }

    private static Icon? LoadIcon(){try{var p=Path.Combine(AppContext.BaseDirectory,"Assets","air-browser.ico");if(!File.Exists(p))p=Path.Combine(AppContext.BaseDirectory,"air-browser.ico");return File.Exists(p)?new Icon(p):null;}catch{return null;}}
    private static bool CanWrite(string p){try{Directory.CreateDirectory(p);var f=Path.Combine(p,".write-test");File.WriteAllText(f,"ok");File.Delete(f);return true;}catch{return false;}}

    private async Task<CoreWebView2Environment> EnvironmentForAsync(string name)
    {
        if(_environments.TryGetValue(name,out var cached))return cached;
        var exe=Path.Combine(_runtime,"msedgewebview2.exe");
        if(!File.Exists(exe))throw new FileNotFoundException("AIR Browser's bundled WebView2 engine is missing.",exe);
        var data=Path.Combine(_root,"WebView2",Safe(name));Directory.CreateDirectory(data);
        var env=await CoreWebView2Environment.CreateAsync(_runtime,data);
        _environments[name]=env;return env;
    }

    private async Task InitializeShellAsync()
    {
        try{
            var env=await EnvironmentForAsync("AIR-Interface");
            await _shell.EnsureCoreWebView2Async(env);
            _shell.CoreWebView2.Settings.AreDefaultContextMenusEnabled=false;
            _shell.CoreWebView2.WebMessageReceived+=ShellMessageReceived;
            var html=Path.Combine(AppContext.BaseDirectory,"UI","index.html");
            if(!File.Exists(html))throw new FileNotFoundException("AIR Browser interface files are missing.",html);
            _shell.CoreWebView2.Navigate(new Uri(html).AbsoluteUri);
            _shellReady=true;_status="Ready";
            await AddTabAsync();
        }catch(Exception ex){MessageBox.Show(this,"AIR Browser could not initialize its interface or Chromium engine.\n\n"+ex.Message,"AIR Browser",MessageBoxButtons.OK,MessageBoxIcon.Error);}
    }

    private async void ShellMessageReceived(object? sender,CoreWebView2WebMessageReceivedEventArgs e)
    {
        try{
            using var doc=JsonDocument.Parse(e.WebMessageAsJson);var m=doc.RootElement;
            string type=m.GetProperty("type").GetString()??"";
            string Get(string key)=>m.TryGetProperty(key,out var v)?v.GetString()??"": "";
            switch(type){
                case "ready": PushState();break;
                case "newTab": await AddTabAsync();break;
                case "select": Select(Get("id"));break;
                case "close": Close(Get("id"));break;
                case "navigate": _engine=Get("engine");Navigate(Get("value"));break;
                case "back": if(_active?.View.CoreWebView2?.CanGoBack==true)_active.View.CoreWebView2.GoBack();break;
                case "forward": if(_active?.View.CoreWebView2?.CanGoForward==true)_active.View.CoreWebView2.GoForward();break;
                case "reload": _active?.View.CoreWebView2?.Reload();break;
                case "bookmark": SaveBookmark();break;
                case "newGroup": CreateGroup(Get("name"));break;
                case "groupTabs": GroupTabs(Get("source"),Get("target"));break;
                case "moveToGroup": MoveToGroup(Get("id"),Get("group"));break;
                case "selectGroup": SelectGroup(Get("group"));break;
                case "profile": SwitchProfile(Get("name"));break;
                case "createProfile": CreateProfile(Get("name"));break;
                case "deleteProfile": DeleteProfile(Get("name"));break;
                case "showBookmarks": ShowBookmarks();break;
                case "showHistory": ShowHistory();break;
                case "downloads": Process.Start(new ProcessStartInfo("explorer.exe",Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),"Downloads")){UseShellExecute=true});break;
                case "update": await CheckForUpdatesAsync();break;
                case "clearData": if(_active?.View.CoreWebView2 is not null){await _active.View.CoreWebView2.Profile.ClearBrowsingDataAsync();SetStatus("Browsing data cleared");}break;
            }
        }catch(Exception ex){SetStatus("Action failed: "+ex.Message);}
    }

    private async Task AddTabAsync(string? url=null)
    {
        var view=new WebView2{Dock=DockStyle.Fill,Visible=false};
        _browserPanel.Controls.Add(view);
        var tab=new TabEntry(Guid.NewGuid().ToString("N"),view,"New tab");
        _tabs.Add(tab);Select(tab.Id);PushState();
        try{
            var env=await EnvironmentForAsync(_profile);await view.EnsureCoreWebView2Async(env);
            view.CoreWebView2.Settings.AreDefaultContextMenusEnabled=true;
            view.CoreWebView2.Settings.IsStatusBarEnabled=false;
            view.CoreWebView2.DocumentTitleChanged+=(_,_)=>{tab.Title=string.IsNullOrWhiteSpace(view.CoreWebView2.DocumentTitle)?"New tab":view.CoreWebView2.DocumentTitle;PushState();};
            view.CoreWebView2.NavigationStarting+=(_,a)=>{if(_active==tab){Send("address",new{value=a.Uri});SetStatus("Loading…");}};
            view.CoreWebView2.NavigationCompleted+=(_,a)=>{if(a.IsSuccess)RecordHistory(view.CoreWebView2.Source);SetStatus(a.IsSuccess?"Ready":"Page could not be loaded");PushState();};
            view.CoreWebView2.NewWindowRequested+=async(_,a)=>{a.Handled=true;await AddTabAsync(a.Uri);};
            view.CoreWebView2.DownloadStarting+=(_,a)=>{var folder=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),"Downloads");Directory.CreateDirectory(folder);var name=Path.GetFileName(a.ResultFilePath);if(!string.IsNullOrWhiteSpace(name))a.ResultFilePath=Path.Combine(folder,name);SetStatus("Downloading "+name);};
            if(!string.IsNullOrWhiteSpace(url))NavigateEntry(tab,url);else view.CoreWebView2.NavigateToString(StartHtml);
        }catch(Exception ex){SetStatus("Browser engine error: "+ex.Message);}
    }

    private const string StartHtml = @"<!doctype html><html><head><meta charset='utf-8'><meta name='viewport' content='width=device-width'><title>New tab</title><style>
*{box-sizing:border-box}body{margin:0;background:radial-gradient(ellipse at 50% 12%,#183b68 0,#0d1a2d 42%,#080e19 80%);color:#eef5ff;font:16px Segoe UI,Arial;min-height:100vh;display:flex;justify-content:center}.page{width:min(900px,90%);padding:9vh 20px 40px;text-align:center}.logo{margin:auto;width:70px;height:70px;border-radius:22px;background:linear-gradient(145deg,#3694ff,#114c9c);display:grid;place-items:center;font-size:43px;font-weight:800;box-shadow:0 14px 35px #0005}.brand{font-size:33px;letter-spacing:9px;margin:14px 0 5px}.tag{color:#9db5d3;margin:0 0 30px}.search{display:flex;gap:8px;max-width:650px;margin:0 auto 34px;padding:7px;background:#f4f7fc;border-radius:16px;box-shadow:0 10px 40px #0005}.search input{flex:1;min-width:0;border:0;outline:0;background:transparent;color:#172b45;padding:8px 13px;font:15px Segoe UI}.search button{border:0;border-radius:11px;background:#1768d6;color:white;padding:0 20px;font:14px Segoe UI;cursor:pointer}.label{max-width:650px;margin:0 auto 12px;text-align:left;color:#9db5d3;font-size:12px}.links{display:grid;grid-template-columns:repeat(4,1fr);gap:12px;max-width:650px;margin:auto}.link{padding:17px 8px;background:#172943;border:1px solid #2a4565;border-radius:14px;color:#edf5ff;text-decoration:none}.link:hover{background:#23436a}.ico{display:grid;place-items:center;margin:0 auto 9px;width:38px;height:38px;border-radius:12px;background:#234c7c;font-size:20px}.link small{display:block;color:#9bb2d0;font-size:11px;margin-top:4px}@media(max-width:550px){.links{grid-template-columns:repeat(2,1fr)}.page{padding-top:6vh}}
</style></head><body><main class='page'><div class='logo'>A</div><div class='brand'>AIR</div><p class='tag'>A clearer space for everything you do online.</p><form class='search' action='https://www.bing.com/search' method='get'><input name='q' placeholder='Search the web with Bing' autofocus><button>Search</button></form><div class='label'>QUICK LINKS</div><div class='links'><a class='link' href='https://www.bing.com'><span class='ico'>⌕</span>Bing<small>Search the web</small></a><a class='link' href='https://www.youtube.com'><span class='ico'>▶</span>YouTube<small>Watch videos</small></a><a class='link' href='https://www.google.com'><span class='ico'>G</span>Google<small>Explore the web</small></a><a class='link' href='https://www.wikipedia.org'><span class='ico'>W</span>Wikipedia<small>Learn something</small></a></div></main></body></html>";

    private void Navigate(string value){if(_active is null)return;NavigateEntry(_active,value);}
    private void NavigateEntry(TabEntry t,string value)
    {
        if(t.View.CoreWebView2 is null)return;value=value.Trim();if(value.Length==0)return;
        string target;
        if(Uri.TryCreate(value,UriKind.Absolute,out var uri)&&uri.Scheme is "http" or "https")target=uri.ToString();
        else if(!value.Contains(' ')&&value.Contains('.'))target="https://"+value;
        else target=(_engine switch{"Google"=>"https://www.google.com/search?q=","DuckDuckGo"=>"https://duckduckgo.com/?q=",_=>"https://www.bing.com/search?q="})+Uri.EscapeDataString(value);
        t.View.CoreWebView2.Navigate(target);
    }
    private void Select(string id){var t=_tabs.FirstOrDefault(x=>x.Id==id);if(t is null)return;Select(t);}
    private void Select(TabEntry t){_active=t;foreach(var x in _tabs)x.View.Visible=x==t;if(t.View.Parent is not null)t.View.BringToFront();Text=(t.Title=="New tab"?"AIR Browser":t.Title+" - AIR Browser");PushState();}
    private void Close(string id){var t=_tabs.FirstOrDefault(x=>x.Id==id);if(t is null)return;var index=_tabs.IndexOf(t);_tabs.Remove(t);_browserPanel.Controls.Remove(t.View);t.View.Dispose();if(_active==t){_active=null;if(_tabs.Count>0)Select(_tabs[Math.Clamp(index-1,0,_tabs.Count-1)]);else _=AddTabAsync();}PushState();}
    private void CreateGroup(string name){name=name.Trim();if(name.Length==0)return;if(!_groups.Contains(name,StringComparer.OrdinalIgnoreCase))_groups.Add(name);if(_active is not null)_active.Group=name;SetStatus("Created "+name);PushState();}
    private void GroupTabs(string source,string target){var a=_tabs.FirstOrDefault(t=>t.Id==source);var b=_tabs.FirstOrDefault(t=>t.Id==target);if(a is null||b is null||a==b)return;var group=b.Group!="Ungrouped"?b.Group:a.Group!="Ungrouped"?a.Group:"Group "+(_groups.Count);if(!_groups.Contains(group))_groups.Add(group);a.Group=group;b.Group=group;SetStatus("Tabs grouped together");PushState();}
    private void MoveToGroup(string id,string group){var t=_tabs.FirstOrDefault(x=>x.Id==id);if(t is null)return;if(group!="Ungrouped"&&!_groups.Contains(group))_groups.Add(group);t.Group=group;PushState();}
    private void SelectGroup(string group){var t=_tabs.FirstOrDefault(x=>x.Group==group);if(t is not null)Select(t);}
    private void SaveBookmark(){if(_active?.View.CoreWebView2?.Source is not string url)return;var file=Path.Combine(ProfileFolder,"bookmarks.json");Directory.CreateDirectory(ProfileFolder);var list=File.Exists(file)?JsonSerializer.Deserialize<List<string>>(File.ReadAllText(file))??new():new List<string>();if(!list.Contains(url))list.Add(url);File.WriteAllText(file,JsonSerializer.Serialize(list));SetStatus("Bookmark saved");}
    private void SaveProfiles(){try{File.WriteAllText(ProfilesFile,JsonSerializer.Serialize(_profiles));}catch{}}
    private void CreateProfile(string name){name=name.Trim();if(name.Length==0||name.Length>32||name.Any(char.IsControl)){SetStatus("Choose a profile name up to 32 characters");return;}if(_profiles.Contains(name,StringComparer.OrdinalIgnoreCase)){SetStatus("That profile already exists");return;}_profiles.Add(name);SaveProfiles();SwitchProfile(name);SetStatus("Created profile: "+name);}
    private void DeleteProfile(string name){if(name=="Personal"||!_profiles.Contains(name))return;_profiles.Remove(name);try{var folder=Path.Combine(_root,"WebView2",Safe(name));if(Directory.Exists(folder))Directory.Delete(folder,true);var profile=Path.Combine(_root,"Profiles",Safe(name));if(Directory.Exists(profile))Directory.Delete(profile,true);}catch{}SaveProfiles();if(_profile==name)SwitchProfile("Personal");else PushState();SetStatus("Deleted profile: "+name);}
    private void SwitchProfile(string name){if(!_profiles.Contains(name)||name==_profile)return;_profile=name;foreach(var t in _tabs){_browserPanel.Controls.Remove(t.View);t.View.Dispose();}_tabs.Clear();_active=null;_ = AddTabAsync();SetStatus("Profile: "+name);}
    private void RecordHistory(string url){if(string.IsNullOrWhiteSpace(url)||!Uri.TryCreate(url,UriKind.Absolute,out var u)||u.Scheme is not ("http" or "https"))return;try{Directory.CreateDirectory(ProfileFolder);var file=Path.Combine(ProfileFolder,"history.json");var list=File.Exists(file)?JsonSerializer.Deserialize<List<string>>(File.ReadAllText(file))??new():new List<string>();list.Add(url);if(list.Count>500)list=list.TakeLast(500).ToList();File.WriteAllText(file,JsonSerializer.Serialize(list));}catch{}}
    private async Task CheckForUpdatesAsync(){try{SetStatus("Checking for AIR Browser updates…");using var http=new HttpClient();http.DefaultRequestHeaders.UserAgent.ParseAdd("AIR-Browser-Updater");var json=await http.GetStringAsync("https://api.github.com/repos/betabeeswax/air-browser/releases/latest");using var doc=JsonDocument.Parse(json);var tag=doc.RootElement.TryGetProperty("tag_name",out var tagValue)?tagValue.GetString()??"":"";var latestBuild=int.TryParse(tag.Split('-').Last(),out var parsedBuild)?parsedBuild:0;var currentBuild=Version.TryParse(Application.ProductVersion,out var installedVersion)?installedVersion.Build:0;if(latestBuild>0&&currentBuild>=latestBuild){SetStatus("AIR Browser is up to date");MessageBox.Show(this,"You already have the latest AIR Browser version.","AIR Browser",MessageBoxButtons.OK,MessageBoxIcon.Information);return;}var assets=doc.RootElement.GetProperty("assets");var asset=assets.EnumerateArray().FirstOrDefault(x=>x.GetProperty("name").GetString()=="AIR-Browser-Setup.exe");if(asset.ValueKind==JsonValueKind.Undefined){SetStatus("No installer was found in the latest release");return;}var url=asset.GetProperty("browser_download_url").GetString();if(string.IsNullOrWhiteSpace(url))return;var answer=MessageBox.Show(this,"The latest AIR Browser installer is ready. Download and install it now? Your browser profiles and data will be kept.","AIR Browser update",MessageBoxButtons.YesNo,MessageBoxIcon.Information);if(answer!=DialogResult.Yes){SetStatus("Update postponed");return;}SetStatus("Downloading update…");var bytes=await http.GetByteArrayAsync(url);var installer=Path.Combine(Path.GetTempPath(),"AIR-Browser-Update.exe");await File.WriteAllBytesAsync(installer,bytes);Process.Start(new ProcessStartInfo(installer,"/VERYSILENT /SUPPRESSMSGBOXES /NORESTART"){UseShellExecute=true});Application.Exit();}catch(Exception ex){SetStatus("Update failed: "+ex.Message);MessageBox.Show(this,"Could not check or install the update. Please try again later.\n\n"+ex.Message,"AIR Browser updater",MessageBoxButtons.OK,MessageBoxIcon.Warning);}}
    private void OpenMenu(){var m=new ContextMenuStrip();m.Items.Add("New tab",null,async(_,_)=>await AddTabAsync());m.Items.Add("Bookmarks",null,(_,_)=>ShowBookmarks());m.Items.Add("History",null,(_,_)=>ShowHistory());m.Items.Add("Downloads",null,(_,_)=>Process.Start(new ProcessStartInfo("explorer.exe",Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),"Downloads")){UseShellExecute=true}));m.Items.Add("Check for updates",null,(_,_)=>Process.Start(new ProcessStartInfo("https://github.com/betabeeswax/air-browser/releases/latest"){UseShellExecute=true}));m.Show(Cursor.Position);}
    private void ShowBookmarks(){var file=Path.Combine(ProfileFolder,"bookmarks.json");var list=File.Exists(file)?JsonSerializer.Deserialize<List<string>>(File.ReadAllText(file))??new():new List<string>();var selected=ShowList("Bookmarks",list);if(selected is not null)Navigate(selected);}
    private void ShowHistory(){var file=Path.Combine(ProfileFolder,"history.json");var list=File.Exists(file)?JsonSerializer.Deserialize<List<string>>(File.ReadAllText(file))??new():new List<string>();var selected=ShowList("History",list.AsEnumerable().Reverse().ToList());if(selected is not null)Navigate(selected);}
    private string? ShowList(string title,List<string> values){using var form=new Form{Text=title,Size=new Size(700,500),StartPosition=FormStartPosition.CenterParent,BackColor=Color.FromArgb(13,23,38)};var list=new ListBox{Dock=DockStyle.Fill,BackColor=Color.FromArgb(20,34,54),ForeColor=Color.White,DataSource=values};form.Controls.Add(list);return form.ShowDialog(this)==DialogResult.OK?list.SelectedItem as string:null;}
    private void SetStatus(string value){_status=value;Send("status",new{value});}
    private void PushState(){if(!_shellReady)return;Send("state",new{tabs=_tabs.Select(t=>new{id=t.Id,title=t.Title,group=t.Group}).ToArray(),groups=_groups.ToArray(),active=_active?.Id,address=_active?.View.CoreWebView2?.Source??"",status=_status,profile=_profile,profiles=_profiles.ToArray()});}
    private void Send(string type,object data){if(!_shellReady||_shell.CoreWebView2 is null)return;_shell.CoreWebView2.PostWebMessageAsJson(JsonSerializer.Serialize(new{type,data}));}
    private sealed class TabEntry(string id,WebView2 view,string title){public string Id=id;public WebView2 View=view;public string Title=title;public string Group="Ungrouped";}
}