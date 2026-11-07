using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using System.Text.Json;

namespace AIRBrowser;

public sealed class MainForm : Form
{
    private readonly string _root;
    private readonly string _runtime;
    private readonly WebView2 _shell = new() { Dock = DockStyle.Fill };
    private readonly Panel _shellPanel = new() { Dock = DockStyle.Top, Height = 180 };
    private readonly Panel _browserPanel = new() { Dock = DockStyle.Fill, BackColor = Color.FromArgb(8,13,22) };
    private readonly List<TabEntry> _tabs = new();
    private readonly List<string> _groups = new() { "Ungrouped" };
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
        Text="AIR Browser"; Name="AIRBrowser"; MinimumSize=new Size(900,600); Size=new Size(1440,900);
        StartPosition=FormStartPosition.CenterScreen; BackColor=Color.FromArgb(8,13,22); Icon=LoadIcon();
        var portable=File.Exists(Path.Combine(AppContext.BaseDirectory,"portable.mode"));
        var portableRoot=Path.Combine(AppContext.BaseDirectory,"AIR-Browser-Data");
        _root=portable&&CanWrite(portableRoot)?portableRoot:Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"AIR Browser");
        Directory.CreateDirectory(_root);
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
                case "menu": OpenMenu();break;
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
            view.CoreWebView2.NavigationCompleted+=(_,a)=>{SetStatus(a.IsSuccess?"Ready":"Page could not be loaded");PushState();};
            view.CoreWebView2.NewWindowRequested+=async(_,a)=>{a.Handled=true;await AddTabAsync(a.Uri);};
            view.CoreWebView2.DownloadStarting+=(_,a)=>{var folder=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),"Downloads");Directory.CreateDirectory(folder);var name=Path.GetFileName(a.ResultFilePath);if(!string.IsNullOrWhiteSpace(name))a.ResultFilePath=Path.Combine(folder,name);SetStatus("Downloading "+name);};
            if(!string.IsNullOrWhiteSpace(url))NavigateEntry(tab,url);else view.CoreWebView2.NavigateToString(StartHtml);
        }catch(Exception ex){SetStatus("Browser engine error: "+ex.Message);}
    }

    private const string StartHtml="<!doctype html><html><head><meta charset='utf-8'><title>New tab</title><style>body{margin:0;background:#080d16;color:#eaf2ff;font:16px Segoe UI;display:grid;place-items:center;height:100vh}.box{text-align:center}.logo{margin:auto;background:#1768d6;border-radius:22px;width:82px;height:82px;display:grid;place-items:center;font-size:50px;font-weight:bold}.brand{font-size:38px;letter-spacing:9px;margin:18px}p{color:#8fa8c8}a{color:#7bb5ff;margin:12px;text-decoration:none}</style></head><body><div class='box'><div class='logo'>A</div><div class='brand'>AIR</div><p>Your browser. Your space.</p><a href='https://www.google.com'>Google</a><a href='https://www.youtube.com'>YouTube</a><a href='https://www.wikipedia.org'>Wikipedia</a></div></body></html>";

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
    private void SwitchProfile(string name){if(name is not ("Personal" or "Work")||name==_profile)return;_profile=name;foreach(var t in _tabs){_browserPanel.Controls.Remove(t.View);t.View.Dispose();}_tabs.Clear();_active=null;_ = AddTabAsync();SetStatus("Profile: "+name);}
    private void OpenMenu(){var m=new ContextMenuStrip();m.Items.Add("New tab",null,async(_,_)=>await AddTabAsync());m.Items.Add("Bookmarks",null,(_,_)=>ShowBookmarks());m.Items.Add("History",null,(_,_)=>ShowHistory());m.Items.Add("Downloads",null,(_,_)=>Process.Start(new ProcessStartInfo("explorer.exe",Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),"Downloads")){UseShellExecute=true}));m.Items.Add("Check for updates",null,(_,_)=>Process.Start(new ProcessStartInfo("https://github.com/betabeeswax/air-browser/releases/latest"){UseShellExecute=true}));m.Show(Cursor.Position);}
    private void ShowBookmarks(){var file=Path.Combine(ProfileFolder,"bookmarks.json");var list=File.Exists(file)?JsonSerializer.Deserialize<List<string>>(File.ReadAllText(file))??new():new List<string>();var selected=ShowList("Bookmarks",list);if(selected is not null)Navigate(selected);}
    private void ShowHistory(){var file=Path.Combine(ProfileFolder,"history.json");var list=File.Exists(file)?JsonSerializer.Deserialize<List<string>>(File.ReadAllText(file))??new():new List<string>();var selected=ShowList("History",list.AsEnumerable().Reverse().ToList());if(selected is not null)Navigate(selected);}
    private string? ShowList(string title,List<string> values){using var form=new Form{Text=title,Size=new Size(700,500),StartPosition=FormStartPosition.CenterParent,BackColor=Color.FromArgb(13,23,38)};var list=new ListBox{Dock=DockStyle.Fill,BackColor=Color.FromArgb(20,34,54),ForeColor=Color.White,DataSource=values};form.Controls.Add(list);return form.ShowDialog(this)==DialogResult.OK?list.SelectedItem as string:null;}
    private void SetStatus(string value){_status=value;Send("status",new{value});}
    private void PushState(){if(!_shellReady)return;Send("state",new{tabs=_tabs.Select(t=>new{id=t.Id,title=t.Title,group=t.Group}).ToArray(),groups=_groups.ToArray(),active=_active?.Id,address=_active?.View.CoreWebView2?.Source??"",status=_status});}
    private void Send(string type,object data){if(!_shellReady||_shell.CoreWebView2 is null)return;_shell.CoreWebView2.PostWebMessageAsJson(JsonSerializer.Serialize(new{type,data}));}
    private sealed class TabEntry(string id,WebView2 view,string title){public string Id=id;public WebView2 View=view;public string Title=title;public string Group="Ungrouped";}
}