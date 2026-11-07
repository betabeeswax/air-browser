using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using System.Text.Json;
using System.Diagnostics;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;

namespace AIRBrowser;

internal sealed class TransparentOverlayPanel : Panel
{
    protected override CreateParams CreateParams
    {
        get { var cp = base.CreateParams; cp.ExStyle |= 0x00000020; return cp; }
    }
}

public sealed class MainForm : Form
{
    private readonly string _root;
    private readonly string _runtime;
    private readonly bool _portable;
    private readonly WebView2 _shell = new() { Dock = DockStyle.Fill };
    private readonly TransparentOverlayPanel _shellPanel = new() { Dock = DockStyle.None, Height = 67, BackColor = Color.Transparent };
    private readonly System.Windows.Forms.Timer _overlayAnimation = new() { Interval = 15 };
    private int _overlayTarget = 67;
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
    private readonly System.Windows.Forms.Timer _updateTimer = new() { Interval = 6 * 60 * 60 * 1000 };
    private bool _updateCheckRunning;
    private string ProfileFolder => Path.Combine(_root,"Profiles",Safe(_profile));
    private static string Safe(string s)=>string.Concat(s.Select(c=>char.IsLetterOrDigit(c)||c=='-'?c:'_'));

    public MainForm(string? initialUrl = null, string? profile = null)
    {
        Text="AIR Browser"; Name="AIRBrowser"; ShowIcon=true; MinimumSize=new Size(900,600); Size=new Size(1440,900);
        StartPosition=FormStartPosition.CenterScreen; BackColor=Color.FromArgb(8,13,22); Icon=LoadIcon(); if (!string.IsNullOrWhiteSpace(profile)) _profile = profile;
        _portable=File.Exists(Path.Combine(AppContext.BaseDirectory,"portable.mode"));
        var portable=_portable;
        var portableRoot=Path.Combine(AppContext.BaseDirectory,"AIR-Browser-Data");
        _root=portable&&CanWrite(portableRoot)?portableRoot:Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"AIR Browser");
        Directory.CreateDirectory(_root);
        try { if (File.Exists(ProfilesFile)) { var saved = JsonSerializer.Deserialize<List<string>>(File.ReadAllText(ProfilesFile)); if (saved is { Count: > 0 }) { _profiles.Clear(); _profiles.AddRange(saved.Distinct(StringComparer.OrdinalIgnoreCase)); if (!_profiles.Contains("Personal")) _profiles.Insert(0, "Personal"); } } } catch { }
        _runtime=Path.Combine(AppContext.BaseDirectory,"FixedRuntime","Microsoft.WebView2.FixedVersionRuntime.156.0.4314.56.x64");
        _shellPanel.Controls.Add(_shell);
        _browserPanel.Dock = DockStyle.Fill;
        _shellPanel.Location = Point.Empty;
        _shellPanel.Width = ClientSize.Width;
        _shellPanel.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        Controls.Add(_browserPanel); Controls.Add(_shellPanel);
        _shellPanel.BringToFront();
        Resize += (_, _) => { _shellPanel.Width = ClientSize.Width; _shellPanel.BringToFront(); };
        _overlayAnimation.Tick += (_, _) =>
        {
            var delta = _overlayTarget - _shellPanel.Height;
            if (Math.Abs(delta) <= 8)
            {
                _shellPanel.Height = _overlayTarget;
                _overlayAnimation.Stop();
                return;
            }
            _shellPanel.Height += Math.Sign(delta) * Math.Max(8, Math.Abs(delta) / 4);
        };
        Shown+=async (_,_)=>{await InitializeShellAsync(initialUrl);StartAutomaticUpdates();};
        FormClosing+=(_,_)=>{_overlayAnimation.Stop();_overlayAnimation.Dispose();_updateTimer.Stop();_updateTimer.Dispose();foreach(var t in _tabs)t.View.Dispose();_shell.Dispose();};
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

    private async Task InitializeShellAsync(string? initialUrl = null)
    {
        try{
            var env=await EnvironmentForAsync("AIR-Interface");
            _shell.DefaultBackgroundColor = Color.Transparent;
            await _shell.EnsureCoreWebView2Async(env);
            _shell.CoreWebView2.Settings.AreDefaultContextMenusEnabled=false;
            _shell.CoreWebView2.WebMessageReceived+=ShellMessageReceived;
            var html=Path.Combine(AppContext.BaseDirectory,"UI","index.html");
            if(!File.Exists(html))throw new FileNotFoundException("AIR Browser interface files are missing.",html);
            _shell.CoreWebView2.Navigate(new Uri(html).AbsoluteUri);
            _shellReady=true;_status="Ready";
            await AddTabAsync(initialUrl);
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
                case "overlay": _overlayTarget = m.TryGetProperty("expanded", out var expanded) && expanded.GetBoolean() ? 500 : 67; _shellPanel.BringToFront(); _overlayAnimation.Start(); break;
                case "newTab": await AddTabAsync();break;
                case "select": Select(Get("id"));break;
                case "close": Close(Get("id"));break;
                case "navigate": _engine=Get("engine");Navigate(Get("value"));break;
                case "back": if(_active?.View.CoreWebView2?.CanGoBack==true)_active.View.CoreWebView2.GoBack();break;
                case "forward": if(_active?.View.CoreWebView2?.CanGoForward==true)_active.View.CoreWebView2.GoForward();break;
                case "reload": _active?.View.CoreWebView2?.Reload();break;
                case "bookmark": SaveBookmark();break;
                case "exportBookmarks": ExportBookmarks();break;
                case "importBookmarks": ImportBookmarks();break;
                case "importPasswords": ImportPasswords();break;
                case "exportPasswords": ExportPasswords();break;
                case "newGroup": CreateGroup(Get("name"));break;
                case "groupTabs": GroupTabs(Get("source"),Get("target"));break;
                case "detachTab": { var tab = _tabs.FirstOrDefault(x => x.Id == Get("id")); var url = tab?.View.CoreWebView2?.Source; if (tab is not null && !string.IsNullOrWhiteSpace(url)) { var window = new MainForm(url, _profile); window.Show(); Close(tab.Id); } break; }
                case "moveToGroup": MoveToGroup(Get("id"),Get("group"));break;
                case "selectGroup": SelectGroup(Get("group"));break;
                case "profile": SwitchProfile(Get("name"));break;
                case "createProfile": CreateProfile(Get("name"));break;
                case "deleteProfile": DeleteProfile(Get("name"));break;
                case "showBookmarks": ShowBookmarks();break;
                case "showHistory": ShowHistory();break;
                case "downloads": Process.Start(new ProcessStartInfo("explorer.exe",Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),"Downloads")){UseShellExecute=true});break;
                case "clearData": if(_active?.View.CoreWebView2 is not null){await _active.View.CoreWebView2.Profile.ClearBrowsingDataAsync();SetStatus("Browsing data cleared");}break;
                case "update": await CheckForUpdatesAsync(true);break;
                case "setDefaultBrowser": Process.Start(new ProcessStartInfo("ms-settings:defaultapps") { UseShellExecute = true });break;
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

    private const string DinoHtml = """
<!doctype html>
<html lang="en"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><title>AIR Dino</title>
<style>
*{box-sizing:border-box}body{margin:0;min-height:100vh;background:radial-gradient(ellipse at 50% 15%,#203b5d,#0b1422 70%);color:#edf5ff;font:16px Segoe UI,Arial;display:grid;place-items:center}.wrap{width:min(900px,94vw);text-align:center}.eyebrow{color:#78b8ff;font-size:11px;letter-spacing:4px;font-weight:700}.title{font-size:38px;font-weight:800;margin:5px 0}.sub{color:#9eb4d0;font-size:13px;margin:0 0 22px}.board{position:relative;border:1px solid #385777;border-radius:18px;background:linear-gradient(#dceeff,#f5f9ff);padding:16px;box-shadow:0 20px 70px #0008}canvas{display:block;width:100%;height:auto;max-height:300px}.score{position:absolute;right:30px;top:24px;color:#263b55;font:700 14px Consolas,monospace}.controls{color:#9eb4d0;font-size:12px;margin-top:17px}.restart{margin-top:14px;background:#1768d6;color:white;border:0;border-radius:9px;padding:10px 20px;font:600 13px Segoe UI;cursor:pointer}.restart:hover{background:#3287ef}.back{display:inline-block;margin-top:14px;color:#8ec5ff;text-decoration:none;font-size:12px}
</style></head><body><main class="wrap"><div class="eyebrow">A LITTLE AIR ORIGINAL</div><div class="title">AIR Dino</div><p class="sub">A tiny game for the times when the web goes quiet.</p><div class="board"><canvas id="game" width="860" height="260"></canvas><div class="score" id="score">00000</div></div><div class="controls">SPACE / ↑ / CLICK / TAP to jump · Avoid the cactuses · Press R to restart</div><button class="restart" id="restart">Play again</button><br><a class="back" href="https://www.bing.com">Back to browsing</a></main>
<script>
const c=document.getElementById('game'),ctx=c.getContext('2d'),scoreEl=document.getElementById('score');let dino,obstacles,score,speed,frame,over,started,last=0;
function reset(){dino={x:82,y:190,vy:0,ground:190};obstacles=[];score=0;speed=5;frame=0;over=false;started=false;scoreEl.textContent='00000';draw()}
function jump(){if(over){reset();started=true;return}if(!started)started=true;if(dino.y>=dino.ground-1)dino.vy=-12.5}
function draw(){ctx.clearRect(0,0,860,260);let sky=ctx.createLinearGradient(0,0,0,260);sky.addColorStop(0,'#e8f4ff');sky.addColorStop(1,'#f8fbff');ctx.fillStyle=sky;ctx.fillRect(0,0,860,260);ctx.fillStyle='#c6d9eb';for(let i=0;i<6;i++){let x=(i*173-(frame*0.35)%1038+1038)%1038;ctx.beginPath();ctx.arc(x,54+(i%3)*22,13,0,7);ctx.arc(x+15,54+(i%3)*22,18,0,7);ctx.arc(x+32,54+(i%3)*22,12,0,7);ctx.fill()}ctx.strokeStyle='#7895b2';ctx.lineWidth=2;ctx.beginPath();ctx.moveTo(0,211);ctx.lineTo(860,211);ctx.stroke();ctx.fillStyle='#193653';ctx.fillRect(dino.x,dino.y-33,30,33);ctx.fillRect(dino.x+19,dino.y-48,20,21);ctx.fillStyle='#f8fbff';ctx.fillRect(dino.x+31,dino.y-42,4,4);ctx.fillStyle='#193653';ctx.fillRect(dino.x+5,dino.y-7,7,7);ctx.fillRect(dino.x+21,dino.y-7,7,7);for(const o of obstacles){ctx.fillStyle='#2c855d';ctx.fillRect(o.x+8,211-o.h,11,o.h);ctx.fillRect(o.x,211-o.h+12,9,8);ctx.fillRect(o.x+17,211-o.h+22,10,8)}if(!started&&!over){ctx.fillStyle='#31506e';ctx.font='bold 18px Segoe UI';ctx.textAlign='center';ctx.fillText('Press SPACE or tap to start',430,130)}if(over){ctx.fillStyle='#17334f';ctx.font='bold 28px Segoe UI';ctx.textAlign='center';ctx.fillText('GAME OVER',430,120);ctx.font='14px Segoe UI';ctx.fillText('Press SPACE or click to try again',430,146)}}
function tick(t){if(!last)last=t;const dt=Math.min(2,(t-last)/16.67);last=t;if(started&&!over){frame+=dt;score+=dt*0.12;speed=Math.min(11,5+score/90);dino.vy+=0.62*dt;dino.y=Math.min(dino.ground,dino.y+dino.vy*dt);if(dino.y>=dino.ground)dino.vy=0;if(frame>55&&Math.random()<0.025*dt&&(!obstacles.length||obstacles[obstacles.length-1].x<610))obstacles.push({x:880,h:30+Math.random()*24});for(const o of obstacles)o.x-=speed*dt;obstacles=obstacles.filter(o=>o.x>-40);for(const o of obstacles)if(dino.x+25>o.x&&dino.x+5<o.x+28&&dino.y>211-o.h+4)over=true;scoreEl.textContent=String(Math.floor(score)).padStart(5,'0')}draw();requestAnimationFrame(tick)}
window.addEventListener('keydown',e=>{if(e.code==='Space'||e.code==='ArrowUp'){e.preventDefault();jump()}if(e.key.toLowerCase()==='r')reset()});c.addEventListener('pointerdown',jump);document.getElementById('restart').onclick=()=>{reset();started=true};reset();requestAnimationFrame(tick);
</script></body></html>
""";

    private void Navigate(string value){if(_active is null)return;NavigateEntry(_active,value);}
    private void NavigateEntry(TabEntry t,string value)
    {
        if(t.View.CoreWebView2 is null)return;value=value.Trim();if(value.Length==0)return;
        if(value.Equals("air://dino",StringComparison.OrdinalIgnoreCase)||value.Equals("chrome://dino",StringComparison.OrdinalIgnoreCase)){t.Title="AIR Dino";t.View.CoreWebView2.NavigateToString(DinoHtml);SetStatus("Playing AIR Dino");PushState();return;}
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
    private void GroupTabs(string source,string target){var a=_tabs.FirstOrDefault(t=>t.Id==source);var b=_tabs.FirstOrDefault(t=>t.Id==target);if(a is null||b is null||a==b)return;var group=b.Group!="Ungrouped"?b.Group:a.Group!="Ungrouped"?a.Group:"Group "+(_groups.Count);if(!_groups.Contains(group))_groups.Add(group);a.Group=group;b.Group=group;_tabs.Remove(a);var targetIndex=_tabs.IndexOf(b);_tabs.Insert(targetIndex+1,a);SetStatus("Tabs grouped together");PushState();}
    private void MoveToGroup(string id,string group){var t=_tabs.FirstOrDefault(x=>x.Id==id);if(t is null)return;if(group!="Ungrouped"&&!_groups.Contains(group))_groups.Add(group);t.Group=group;PushState();}
    private void SelectGroup(string group){var t=_tabs.FirstOrDefault(x=>x.Group==group);if(t is not null)Select(t);}
    private void SaveBookmark(){if(_active?.View.CoreWebView2?.Source is not string url)return;var file=Path.Combine(ProfileFolder,"bookmarks.json");Directory.CreateDirectory(ProfileFolder);var list=File.Exists(file)?JsonSerializer.Deserialize<List<string>>(File.ReadAllText(file))??new():new List<string>();if(!list.Contains(url))list.Add(url);File.WriteAllText(file,JsonSerializer.Serialize(list));SetStatus("Bookmark saved");}

    private string PasswordVaultFile => Path.Combine(ProfileFolder, "passwords.dpapi");
    private void ExportBookmarks()
    {
        try {
            var file=Path.Combine(ProfileFolder,"bookmarks.json");
            var list=File.Exists(file)?JsonSerializer.Deserialize<List<string>>(File.ReadAllText(file))??new():new List<string>();
            using var dialog=new SaveFileDialog{Title="Export AIR bookmarks",Filter="HTML bookmarks (*.html)|*.html",FileName="AIR-Bookmarks.html"};
            if(dialog.ShowDialog(this)!=DialogResult.OK)return;
            var html="<!DOCTYPE NETSCAPE-Bookmark-file-1>\n<META HTTP-EQUIV=\"Content-Type\" CONTENT=\"text/html; charset=UTF-8\">\n<TITLE>AIR Bookmarks</TITLE><H1>Bookmarks</H1><DL><p>\n";
            foreach(var url in list.Distinct())if(Uri.TryCreate(url,UriKind.Absolute,out var u)&&(u.Scheme=="http"||u.Scheme=="https"))html+="<DT><A HREF=\""+System.Net.WebUtility.HtmlEncode(url)+"\">"+System.Net.WebUtility.HtmlEncode(u.Host)+"</A>\n";
            File.WriteAllText(dialog.FileName,html+"</DL>\n",Encoding.UTF8);SetStatus("Bookmarks exported");
        } catch(Exception ex){SetStatus("Bookmark export failed: "+ex.Message);}
    }
    private void ImportBookmarks()
    {
        try {
            using var dialog=new OpenFileDialog{Title="Import bookmarks",Filter="Bookmark files (*.html;*.json)|*.html;*.json|All files (*.*)|*.*"};
            if(dialog.ShowDialog(this)!=DialogResult.OK)return;
            var content=File.ReadAllText(dialog.FileName);var imported=new List<string>();
            if(dialog.FileName.EndsWith(".json",StringComparison.OrdinalIgnoreCase)){
                using var doc=JsonDocument.Parse(content);var stack=new Stack<JsonElement>();stack.Push(doc.RootElement);
                while(stack.Count>0){var el=stack.Pop();if(el.ValueKind==JsonValueKind.Object){if(el.TryGetProperty("url",out var u)&&u.ValueKind==JsonValueKind.String)imported.Add(u.GetString()!);foreach(var p in el.EnumerateObject())if(p.Value.ValueKind is JsonValueKind.Object or JsonValueKind.Array)stack.Push(p.Value);}else if(el.ValueKind==JsonValueKind.Array)foreach(var x in el.EnumerateArray())stack.Push(x);}
            } else foreach(System.Text.RegularExpressions.Match m in System.Text.RegularExpressions.Regex.Matches(content,"HREF\\s*=\\s*[\"']([^\"']+)[\"']",System.Text.RegularExpressions.RegexOptions.IgnoreCase))imported.Add(System.Net.WebUtility.HtmlDecode(m.Groups[1].Value));
            var file=Path.Combine(ProfileFolder,"bookmarks.json");Directory.CreateDirectory(ProfileFolder);var list=File.Exists(file)?JsonSerializer.Deserialize<List<string>>(File.ReadAllText(file))??new():new List<string>();var before=list.Count;
            foreach(var u in imported)if(Uri.TryCreate(u,UriKind.Absolute,out var uri)&&(uri.Scheme=="http"||uri.Scheme=="https")&&!list.Contains(u,StringComparer.OrdinalIgnoreCase))list.Add(u);
            File.WriteAllText(file,JsonSerializer.Serialize(list));SetStatus($"Imported {list.Count-before} bookmarks");
        } catch(Exception ex){SetStatus("Bookmark import failed: "+ex.Message);}
    }
    private static List<string> ParseCsvLine(string line)
    {
        var values=new List<string>();var value=new StringBuilder();bool quoted=false;
        for(int i=0;i<line.Length;i++){char c=line[i];if(c=='"'&&quoted&&i+1<line.Length&&line[i+1]=='"'){value.Append('"');i++;}else if(c=='"')quoted=!quoted;else if(c==','&&!quoted){values.Add(value.ToString());value.Clear();}else value.Append(c);}values.Add(value.ToString());return values;
    }
    private void ImportPasswords()
    {
        try {
            using var dialog=new OpenFileDialog{Title="Import passwords from a browser CSV export",Filter="CSV files (*.csv)|*.csv"};
            if(dialog.ShowDialog(this)!=DialogResult.OK)return;
            var lines=File.ReadAllLines(dialog.FileName);if(lines.Length<2){SetStatus("No password records found");return;}
            var headers=ParseCsvLine(lines[0]).Select((v,i)=>(key:v.Trim().ToLowerInvariant(),index:i)).ToDictionary(x=>x.key,x=>x.index);
            int Find(params string[] keys){foreach(var k in keys)if(headers.TryGetValue(k,out var i))return i;return -1;}
            int urlI=Find("url","origin","login_uri"),userI=Find("username","user name"),passI=Find("password"),nameI=Find("name","title");
            if(urlI<0||userI<0||passI<0){SetStatus("CSV must contain URL, username and password columns");return;}
            var records=new List<Dictionary<string,string>>();
            foreach(var line in lines.Skip(1)){if(string.IsNullOrWhiteSpace(line))continue;var cols=ParseCsvLine(line);string Get(int i)=>i>=0&&i<cols.Count?cols[i]:"";var url=Get(urlI);var pass=Get(passI);if(!Uri.TryCreate(url,UriKind.Absolute,out var u)||(u.Scheme!="http"&&u.Scheme!="https")||pass.Length==0)continue;records.Add(new(){["url"]=url,["username"]=Get(userI),["password"]=pass,["name"]=Get(nameI)});}
            Directory.CreateDirectory(ProfileFolder);var existing=new List<Dictionary<string,string>>();
            if(File.Exists(PasswordVaultFile)){var cipher=File.ReadAllBytes(PasswordVaultFile);var plain=ProtectedData.Unprotect(cipher,null,DataProtectionScope.CurrentUser);existing=JsonSerializer.Deserialize<List<Dictionary<string,string>>>(plain)??new();}
            foreach(var r in records){existing.RemoveAll(x=>x.GetValueOrDefault("url")==r["url"]&&x.GetValueOrDefault("username")==r["username"]);existing.Add(r);}
            var encrypted=ProtectedData.Protect(JsonSerializer.SerializeToUtf8Bytes(existing),null,DataProtectionScope.CurrentUser);File.WriteAllBytes(PasswordVaultFile,encrypted);
            SetStatus($"Imported {records.Count} passwords into this Windows account's encrypted vault");
        } catch(Exception ex){SetStatus("Password import failed: "+ex.Message);}
    }
    private void ExportPasswords()
    {
        try {
            if(!File.Exists(PasswordVaultFile)){SetStatus("No imported passwords to export");return;}
            if(MessageBox.Show(this,"This export contains passwords in readable CSV text. Keep it private and delete it after importing.","Export passwords",MessageBoxButtons.YesNo,MessageBoxIcon.Warning)!=DialogResult.Yes)return;
            using var dialog=new SaveFileDialog{Title="Export passwords",Filter="CSV files (*.csv)|*.csv",FileName="AIR-Passwords.csv"};if(dialog.ShowDialog(this)!=DialogResult.OK)return;
            var plain=ProtectedData.Unprotect(File.ReadAllBytes(PasswordVaultFile),null,DataProtectionScope.CurrentUser);var records=JsonSerializer.Deserialize<List<Dictionary<string,string>>>(plain)??new();
            string Q(string? x)=>"\""+(x??"").Replace("\"","\"\"")+"\"";
            var csv="name,url,username,password\n"+string.Join("\n",records.Select(x=>string.Join(",",Q(x.GetValueOrDefault("name")),Q(x.GetValueOrDefault("url")),Q(x.GetValueOrDefault("username")),Q(x.GetValueOrDefault("password")))));
            File.WriteAllText(dialog.FileName,csv,Encoding.UTF8);SetStatus("Password CSV exported — keep it secure");
        } catch(Exception ex){SetStatus("Password export failed: "+ex.Message);}
    }
    private void SaveProfiles(){try{File.WriteAllText(ProfilesFile,JsonSerializer.Serialize(_profiles));}catch{}}
    private void CreateProfile(string name){name=name.Trim();if(name.Length==0||name.Length>32||name.Any(char.IsControl)){SetStatus("Choose a profile name up to 32 characters");return;}if(_profiles.Contains(name,StringComparer.OrdinalIgnoreCase)){SetStatus("That profile already exists");return;}_profiles.Add(name);SaveProfiles();SwitchProfile(name);SetStatus("Created profile: "+name);}
    private void DeleteProfile(string name){if(name=="Personal"||!_profiles.Contains(name))return;_profiles.Remove(name);try{var folder=Path.Combine(_root,"WebView2",Safe(name));if(Directory.Exists(folder))Directory.Delete(folder,true);var profile=Path.Combine(_root,"Profiles",Safe(name));if(Directory.Exists(profile))Directory.Delete(profile,true);}catch{}SaveProfiles();if(_profile==name)SwitchProfile("Personal");else PushState();SetStatus("Deleted profile: "+name);}
    private void SwitchProfile(string name){if(!_profiles.Contains(name)||name==_profile)return;_profile=name;foreach(var t in _tabs){_browserPanel.Controls.Remove(t.View);t.View.Dispose();}_tabs.Clear();_active=null;_ = AddTabAsync();SetStatus("Profile: "+name);}
    private void RecordHistory(string url){if(string.IsNullOrWhiteSpace(url)||!Uri.TryCreate(url,UriKind.Absolute,out var u)||u.Scheme is not ("http" or "https"))return;try{Directory.CreateDirectory(ProfileFolder);var file=Path.Combine(ProfileFolder,"history.json");var list=File.Exists(file)?JsonSerializer.Deserialize<List<string>>(File.ReadAllText(file))??new():new List<string>();list.Add(url);if(list.Count>500)list=list.TakeLast(500).ToList();File.WriteAllText(file,JsonSerializer.Serialize(list));}catch{}}
    private void StartAutomaticUpdates()
    {
        _updateTimer.Tick += async (_, _) => await CheckForUpdatesAsync();
        _updateTimer.Start();
        _ = Task.Run(async () =>
        {
            await Task.Delay(TimeSpan.FromSeconds(20));
            if (!IsDisposed && IsHandleCreated)
                BeginInvoke(new Action(async () => await CheckForUpdatesAsync()));
        });
    }

    private async Task CheckForUpdatesAsync(bool manual = false)
    {
        if (_updateCheckRunning || IsDisposed) { if (manual) SetStatus("An update check is already running…"); return; }
        _updateCheckRunning = true;
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
            http.DefaultRequestHeaders.UserAgent.ParseAdd("AIR-Browser-Updater");
            http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
            http.DefaultRequestHeaders.CacheControl = new System.Net.Http.Headers.CacheControlHeaderValue { NoCache = true, NoStore = true };
            using var response = await http.GetAsync("https://api.github.com/repos/betabeeswax/air-browser/releases/latest");
            response.EnsureSuccessStatusCode();
            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            var release = doc.RootElement;
            if ((release.TryGetProperty("draft", out var draftValue) && draftValue.GetBoolean()) ||
                (release.TryGetProperty("prerelease", out var prereleaseValue) && prereleaseValue.GetBoolean()))
            {
                if (manual) MessageBox.Show(this, "There is no newer stable AIR Browser release available.", "AIR Browser updates", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            var tag = release.TryGetProperty("tag_name", out var tagValue) ? tagValue.GetString() ?? "" : "";
            var match = System.Text.RegularExpressions.Regex.Match(tag, @"(\d+)$");
            if (!match.Success || !int.TryParse(match.Groups[1].Value, out var latestBuild))
            {
                if (manual) MessageBox.Show(this, "AIR Browser could not read the release version, so no update was installed.", "AIR Browser updates", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            // The SDK appends commit metadata to ProductVersion (for example 0.2.136+abc123).
            // Parse the actual assembly version first so build metadata can never make us think
            // the installed version is 0 and trigger a reinstall on every check.
            var installedVersion = typeof(MainForm).Assembly.GetName().Version;
            var currentBuild = installedVersion?.Build ?? 0;
            if (currentBuild <= 0)
            {
                var productVersion = Application.ProductVersion.Split('+', '-')[0];
                if (Version.TryParse(productVersion, out var parsedProductVersion))
                    currentBuild = parsedProductVersion.Build;
            }
            Debug.WriteLine($"AIR Browser update comparison: installed build {currentBuild}; latest stable build {latestBuild} ({tag}).");
            if (latestBuild <= currentBuild)
            {
                if (manual)
                {
                    SetStatus("AIR Browser is up to date");
                    MessageBox.Show(this, $"You already have the latest version of AIR Browser (build {currentBuild}).", "AIR Browser updates", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                return;
            }

            var assetName = _portable ? "AIR-Browser-Portable.zip" : "AIR-Browser-Setup.exe";
            var asset = doc.RootElement.GetProperty("assets").EnumerateArray()
                .FirstOrDefault(x => x.GetProperty("name").GetString() == assetName);
            if (asset.ValueKind == JsonValueKind.Undefined) { if (manual) MessageBox.Show(this, "A new version was found, but its update package is not available yet.", "AIR Browser updates", MessageBoxButtons.OK, MessageBoxIcon.Information); return; }
            var url = asset.GetProperty("browser_download_url").GetString();
            if (string.IsNullOrWhiteSpace(url)) return;

            SetStatus("AIR Browser update found — downloading automatically…");
            var bytes = await http.GetByteArrayAsync(url);
            if (asset.TryGetProperty("digest", out var digestElement))
            {
                var expected = digestElement.GetString();
                if (!string.IsNullOrWhiteSpace(expected) && expected.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase))
                {
                    var actual = "sha256:" + Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)).ToLowerInvariant();
                    if (!actual.Equals(expected, StringComparison.OrdinalIgnoreCase))
                        throw new InvalidDataException("The downloaded update failed its SHA-256 check.");
                }
            }

            var packageExtension = _portable ? ".zip" : ".exe";
            var package = Path.Combine(Path.GetTempPath(), "AIR-Browser-Update-" + latestBuild + packageExtension);
            await File.WriteAllBytesAsync(package, bytes);
            var app = Application.ExecutablePath;
            var updaterScript = Path.Combine(Path.GetTempPath(), "AIR-Browser-Updater-" + latestBuild + ".ps1");
            var script = "$ErrorActionPreference='Stop'\n" +
                "$package=$args[0]\n$app=$args[1]\n$processId=[int]$args[2]\n$portable=$args[3] -eq 'true'\n" +
                "Wait-Process -Id $processId -ErrorAction SilentlyContinue\n" +
                "if ($portable) { Expand-Archive -LiteralPath $package -DestinationPath (Split-Path -Parent $app) -Force } " +
                "else { $p=Start-Process -FilePath $package -ArgumentList '/VERYSILENT /SUPPRESSMSGBOXES /NORESTART' -Wait -PassThru; if ($p.ExitCode -ne 0) { throw ('Installer exited with code ' + $p.ExitCode) } }\n" +
                "Start-Process -FilePath $app\n";
            await File.WriteAllTextAsync(updaterScript, script);
            var command = "-NoProfile -WindowStyle Hidden -ExecutionPolicy Bypass -File \"" + updaterScript + "\" \"" + package + "\" \"" + app + "\" " + Environment.ProcessId + " " + (_portable ? "true" : "false");
            Process.Start(new ProcessStartInfo("powershell.exe", command)
            {
                UseShellExecute = false,
                CreateNoWindow = true
            });
            SetStatus("Installing AIR Browser update…");
            Application.Exit();
        }
        catch (Exception ex)
        {
            Debug.WriteLine("AIR Browser update check failed: " + ex);
            SetStatus("Update check failed");
            if (manual) MessageBox.Show(this, "AIR Browser could not check for updates. Please check your internet connection and try again.\n\n" + ex.Message, "AIR Browser updates", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        finally { _updateCheckRunning = false; }
    }

    private void OpenMenu()
    {
        var m = new ContextMenuStrip();
        m.Items.Add("New tab", null, async (_, _) => await AddTabAsync());
        m.Items.Add("Bookmarks", null, (_, _) => ShowBookmarks());
        m.Items.Add("History", null, (_, _) => ShowHistory());
        m.Items.Add("Downloads", null, (_, _) => Process.Start(new ProcessStartInfo("explorer.exe", Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads")) { UseShellExecute = true }));
        m.Items.Add("Check for updates", null, async (_, _) => await CheckForUpdatesAsync(true));
        m.Show(Cursor.Position);
    }
    private void ShowBookmarks(){var file=Path.Combine(ProfileFolder,"bookmarks.json");var list=File.Exists(file)?JsonSerializer.Deserialize<List<string>>(File.ReadAllText(file))??new():new List<string>();var selected=ShowList("Bookmarks",list);if(selected is not null)Navigate(selected);}
    private void ShowHistory(){var file=Path.Combine(ProfileFolder,"history.json");var list=File.Exists(file)?JsonSerializer.Deserialize<List<string>>(File.ReadAllText(file))??new():new List<string>();var selected=ShowList("History",list.AsEnumerable().Reverse().ToList());if(selected is not null)Navigate(selected);}
    private string? ShowList(string title,List<string> values){using var form=new Form{Text=title,Size=new Size(700,500),StartPosition=FormStartPosition.CenterParent,BackColor=Color.FromArgb(13,23,38)};var list=new ListBox{Dock=DockStyle.Fill,BackColor=Color.FromArgb(20,34,54),ForeColor=Color.White,DataSource=values};form.Controls.Add(list);return form.ShowDialog(this)==DialogResult.OK?list.SelectedItem as string:null;}
    private void SetStatus(string value){_status=value;Send("status",new{value});}
    private void PushState(){if(!_shellReady)return;Send("state",new{tabs=_tabs.Select(t=>new{id=t.Id,title=t.Title,group=t.Group}).ToArray(),groups=_groups.ToArray(),active=_active?.Id,address=_active?.View.CoreWebView2?.Source??"",status=_status,profile=_profile,profiles=_profiles.ToArray()});}
    private void Send(string type,object data){if(!_shellReady||_shell.CoreWebView2 is null)return;_shell.CoreWebView2.PostWebMessageAsJson(JsonSerializer.Serialize(new{type,data}));}
    private sealed class TabEntry(string id,WebView2 view,string title){public string Id=id;public WebView2 View=view;public string Title=title;public string Group="Ungrouped";}
}