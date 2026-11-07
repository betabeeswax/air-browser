using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using System.Text.Json;
using System.Diagnostics;
using System.Net.Http;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Runtime.InteropServices;

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
    private readonly Panel _statusPanel = new() { Dock = DockStyle.None, Height = 24, BackColor = Color.FromArgb(13,24,39) };
    private readonly Label _statusLabel = new() { Dock = DockStyle.Fill, ForeColor = Color.FromArgb(151,177,207), TextAlign = ContentAlignment.MiddleLeft, Font = new Font("Segoe UI", 8.5f), Padding = new Padding(10,0,0,0), Text = "Ready" };
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
    [DllImport("user32.dll")] private static extern bool ReleaseCapture();
    [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr hWnd,int msg,IntPtr wParam,IntPtr lParam);
    protected override void WndProc(ref Message m)
    {
        const int WM_NCHITTEST=0x84, HTCLIENT=1;
        base.WndProc(ref m);
        if(m.Msg!=WM_NCHITTEST||m.Result.ToInt32()!=HTCLIENT||WindowState==FormWindowState.Maximized)return;
        var p=PointToClient(Cursor.Position);const int edge=7;
        bool left=p.X<=edge,right=p.X>=ClientSize.Width-edge,top=p.Y<=edge,bottom=p.Y>=ClientSize.Height-edge;
        if(top&&left)m.Result=(IntPtr)13;else if(top&&right)m.Result=(IntPtr)14;else if(bottom&&left)m.Result=(IntPtr)16;else if(bottom&&right)m.Result=(IntPtr)17;
        else if(left)m.Result=(IntPtr)10;else if(right)m.Result=(IntPtr)11;else if(top)m.Result=(IntPtr)12;else if(bottom)m.Result=(IntPtr)15;
    }

    public MainForm(string? initialUrl = null, string? profile = null)
    {
        Text="AIR Browser"; Name="AIRBrowser"; FormBorderStyle=FormBorderStyle.None; ShowIcon=true; MinimumSize=new Size(900,600); Size=new Size(1440,900);
        StartPosition=FormStartPosition.CenterScreen; BackColor=Color.FromArgb(8,13,22); Icon=LoadIcon(); if (!string.IsNullOrWhiteSpace(profile)) _profile = profile;
        _portable=File.Exists(Path.Combine(AppContext.BaseDirectory,"portable.mode"));
        var portable=_portable;
        var portableRoot=Path.Combine(AppContext.BaseDirectory,"AIR-Browser-Data");
        _root=portable&&CanWrite(portableRoot)?portableRoot:Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"AIR Browser");
        Directory.CreateDirectory(_root);
        try { if (File.Exists(ProfilesFile)) { var saved = JsonSerializer.Deserialize<List<string>>(File.ReadAllText(ProfilesFile)); if (saved is { Count: > 0 }) { _profiles.Clear(); _profiles.AddRange(saved.Distinct(StringComparer.OrdinalIgnoreCase)); if (!_profiles.Contains("Personal")) _profiles.Insert(0, "Personal"); } } } catch { }
        _runtime=Path.Combine(AppContext.BaseDirectory,"FixedRuntime","Microsoft.WebView2.FixedVersionRuntime.156.0.4314.56.x64");
        _shellPanel.Controls.Add(_shell);
        _browserPanel.Dock = DockStyle.None;
        _shellPanel.Location = Point.Empty;
        _shellPanel.Width = ClientSize.Width;
        _shellPanel.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        Controls.Add(_browserPanel); _statusPanel.Controls.Add(_statusLabel); Controls.Add(_statusPanel); Controls.Add(_shellPanel);
        LayoutBrowserSurface();
        _shellPanel.BringToFront();
        Resize += (_, _) => { LayoutBrowserSurface(); _shellPanel.Width = ClientSize.Width; _shellPanel.BringToFront(); };
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

    private void LayoutBrowserSurface()
    {
        // Keep the page viewport strictly below the 67px browser chrome and above the bottom status strip.
        const int chromeHeight = 67;
        const int statusHeight = 24;
        var width = Math.Max(0, ClientSize.Width);
        var height = Math.Max(0, ClientSize.Height);
        var statusTop = Math.Max(chromeHeight, height - statusHeight);
        _browserPanel.Bounds = new Rectangle(0, chromeHeight, width, Math.Max(0, statusTop - chromeHeight));
        _statusPanel.Bounds = new Rectangle(0, statusTop, width, Math.Min(statusHeight, height));
    }

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
                case "providerSignIn": await StartProviderSignInAsync(Get("provider"));break;
                case "windowMinimize": WindowState=FormWindowState.Minimized;break;
                case "windowMaximize": WindowState=WindowState==FormWindowState.Maximized?FormWindowState.Normal:FormWindowState.Maximized;break;
                case "windowClose": Close();break;
                case "dragWindow": if(WindowState!=FormWindowState.Maximized){ReleaseCapture();SendMessage(Handle,0x00A1,(IntPtr)2,IntPtr.Zero);}break;
                case "managePasswords": ManagePasswords();break;
                case "syncExport": ExportSyncData();break;
                case "syncImport": if(m.TryGetProperty("payload",out var syncPayload))ImportSyncData(syncPayload);break;
                case "overlay": _overlayTarget = m.TryGetProperty("expanded", out var expanded) && expanded.GetBoolean() ? 500 : 67; _shellPanel.BringToFront(); _overlayAnimation.Start(); break;
                case "newTab": await AddTabAsync();break;
                case "select": Select(Get("id"));break;
                case "close": Close(Get("id"));break;
                case "navigate": _engine=Get("engine");Navigate(Get("value"));break;
                case "back": if(_active?.View.CoreWebView2?.CanGoBack==true)_active.View.CoreWebView2.GoBack();break;
                case "forward": if(_active?.View.CoreWebView2?.CanGoForward==true)_active.View.CoreWebView2.GoForward();break;
                case "reload": _active?.View.CoreWebView2?.Reload();break;
                case "duplicateTab": if(_active?.View.CoreWebView2?.Source is string duplicateUrl && Uri.TryCreate(duplicateUrl,UriKind.Absolute,out var duplicateUri) && duplicateUri.Scheme is "http" or "https") await AddTabAsync(duplicateUrl);break;
                case "copyLink": if(_active?.View.CoreWebView2?.Source is string pageUrl) { Clipboard.SetText(pageUrl); SetStatus("Page link copied"); }break;
                case "zoomIn": if(_active is not null)_active.View.ZoomFactor=Math.Min(3,_active.View.ZoomFactor+0.1);break;
                case "zoomOut": if(_active is not null)_active.View.ZoomFactor=Math.Max(0.5,_active.View.ZoomFactor-0.1);break;
                case "zoomReset": if(_active is not null)_active.View.ZoomFactor=1;break;
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
            view.CoreWebView2.NavigationCompleted+=(_,a)=>{if(a.IsSuccess){RecordHistory(view.CoreWebView2.Source);_ = FillSavedPasswordAsync(view);}SetStatus(a.IsSuccess?"Ready":"Page could not be loaded");PushState();};
            view.CoreWebView2.NewWindowRequested+=async(_,a)=>{a.Handled=true;await AddTabAsync(a.Uri);};
            view.CoreWebView2.WebMessageReceived += (_, message) =>
            {
                try
                {
                    using var data = JsonDocument.Parse(message.WebMessageAsJson);
                    var root = data.RootElement;
                    if (root.TryGetProperty("type", out var type) && type.GetString() == "airSavePassword")
                    {
                        var url = root.TryGetProperty("url", out var u) ? u.GetString() ?? "" : "";
                        var username = root.TryGetProperty("username", out var n) ? n.GetString() ?? "" : "";
                        var password = root.TryGetProperty("password", out var p) ? p.GetString() ?? "" : "";
                        var title = root.TryGetProperty("title", out var t) ? t.GetString() ?? "" : "";
                        if (Uri.TryCreate(url, UriKind.Absolute, out var origin) && origin.Scheme == "https" && password.Length > 0 &&
                            MessageBox.Show(this, "Save this password for " + origin.Host + " in the " + _profile + " profile?", "Save password", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
                        {
                            SavePasswordRecord(origin.GetLeftPart(UriPartial.Authority), username, password, title);
                            SetStatus("Password saved for " + origin.Host);
                        }
                    }
                }
                catch { }
            };
            await view.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync(@"(()=>{document.addEventListener('submit',e=>{const f=e.target;if(!(f instanceof HTMLFormElement)||location.protocol!=='https:')return;const p=f.querySelector('input[type=password]');if(!p||!p.value)return;const u=f.querySelector('input[autocomplete=username],input[type=email],input[name*=user i],input[type=text]');chrome.webview.postMessage({type:'airSavePassword',url:location.origin,username:u?.value||'',password:p.value,title:document.title});},true)})();");
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

    private const string ArcadeHtml = """
<!doctype html><html><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><title>AIR Arcade</title>
<style>
*{box-sizing:border-box}body{margin:0;min-height:100vh;background:radial-gradient(ellipse at 50% 0,#1c3b60,#091321 75%);color:#edf5ff;font:15px Segoe UI,Arial;display:grid;place-items:center}.wrap{width:min(900px,94vw);padding:34px 12px;text-align:center}.tag{color:#80bdff;letter-spacing:4px;font-size:10px;font-weight:700}.title{font-size:36px;font-weight:800;margin:5px}.desc{color:#9eb4d0;font-size:12px}.games{display:flex;gap:8px;justify-content:center;flex-wrap:wrap;margin:22px 0}.games button,.action{border:1px solid #385777;background:#172b45;color:#eaf4ff;border-radius:10px;padding:10px 17px;cursor:pointer}.games button.active,.action{background:#1768d6;border-color:#398ce8}.stage{position:relative;background:#f2f7fd;color:#18314d;border-radius:18px;padding:16px;box-shadow:0 20px 60px #0008;min-height:300px;display:grid;place-items:center}.stage canvas{max-width:100%;height:auto}.stage h2{margin:0 0 8px}.memory{display:grid;grid-template-columns:repeat(4,65px);gap:8px}.memory button{height:65px;border:0;border-radius:10px;background:#d4e5f6;font-size:27px;cursor:pointer}.memory button.open{background:#b8d9f8}.score{position:absolute;right:20px;top:14px;font:700 13px Consolas,monospace}.hint{color:#a5bad4;font-size:12px;margin-top:13px}
</style></head><body><main class="wrap"><div class="tag">A LITTLE AIR ORIGINAL</div><div class="title">AIR Arcade</div><div class="desc">Four tiny games hiding inside your browser.</div><nav class="games"><button data-game="snake">Snake</button><button data-game="pong">Pong</button><button data-game="memory">Memory</button><button data-game="clicker">Clicker</button></nav><section class="stage" id="stage"><h2>Choose a game above</h2></section><div class="hint" id="hint">Try air://snake · air://pong · air://memory · air://clicker</div></main>
<script>
const stage=document.getElementById('stage'),hint=document.getElementById('hint');let loop=0;const initialGame='__INITIAL_GAME__';
function choose(g){cancelAnimationFrame(loop);document.querySelectorAll('[data-game]').forEach(b=>b.classList.toggle('active',b.dataset.game===g));stage.innerHTML='';if(g==='snake')snake();if(g==='pong')pong();if(g==='memory')memory();if(g==='clicker')clicker()}
document.querySelectorAll('[data-game]').forEach(b=>b.onclick=()=>choose(b.dataset.game));if(initialGame!=='__INITIAL_GAME__')choose(initialGame);
function snake(){stage.innerHTML='<div class="score" id="sc">0</div><canvas id="c" width="520" height="260"></canvas>';const c=document.getElementById('c'),x=c.getContext('2d'),sc=document.getElementById('sc'),n=20;let body=[{x:8,y:6},{x:7,y:6},{x:6,y:6}],dir={x:1,y:0},food={x:14,y:7},score=0,last=0;function key(e){if(e.key==='ArrowUp'&&dir.y!==1)dir={x:0,y:-1};if(e.key==='ArrowDown'&&dir.y!==-1)dir={x:0,y:1};if(e.key==='ArrowLeft'&&dir.x!==1)dir={x:-1,y:0};if(e.key==='ArrowRight'&&dir.x!==-1)dir={x:1,y:0}}window.addEventListener('keydown',key);function tick(t){if(t-last>105){last=t;let h={x:body[0].x+dir.x,y:body[0].y+dir.y};if(h.x<0||h.y<0||h.x>=26||h.y>=13||body.some(p=>p.x===h.x&&p.y===h.y)){window.removeEventListener('keydown',key);hint.textContent='Snake ended · choose Snake again to restart';return}body.unshift(h);if(h.x===food.x&&h.y===food.y){score++;sc.textContent=score;food={x:Math.floor(Math.random()*26),y:Math.floor(Math.random()*13)}}else body.pop()}x.fillStyle='#f2f7fd';x.fillRect(0,0,520,260);x.fillStyle='#173b5e';body.forEach(p=>x.fillRect(p.x*n+1,p.y*n+1,n-2,n-2));x.fillStyle='#e46a49';x.fillRect(food.x*n+2,food.y*n+2,n-4,n-4);loop=requestAnimationFrame(tick)}hint.textContent='Use the arrow keys to steer.';loop=requestAnimationFrame(tick)}
function pong(){stage.innerHTML='<div class="score" id="sc">0 : 0</div><canvas id="c" width="520" height="280"></canvas>';const c=document.getElementById('c'),x=c.getContext('2d'),sc=document.getElementById('sc');let y=110,ball={x:260,y:140,vx:4,vy:3},score=0,ai=110;function key(e){if(e.key==='ArrowUp')y-=25;if(e.key==='ArrowDown')y+=25}window.addEventListener('keydown',key);function tick(){x.fillStyle='#f2f7fd';x.fillRect(0,0,520,280);x.fillStyle='#c3d8ed';for(let yy=0;yy<280;yy+=18)x.fillRect(258,yy,4,10);x.fillStyle='#173b5e';x.fillRect(12,Math.max(0,Math.min(220,y)),9,60);x.fillRect(499,ai,9,60);x.beginPath();x.arc(ball.x,ball.y,8,0,7);x.fill();ball.x+=ball.vx;ball.y+=ball.vy;if(ball.y<8||ball.y>272)ball.vy*=-1;ai+=(ball.y-30-ai)*.045;if(ball.x<22&&ball.y>y&&ball.y<y+60)ball.vx=Math.abs(ball.vx)*1.05;if(ball.x>490&&ball.y>ai&&ball.y<ai+60)ball.vx=-Math.abs(ball.vx);if(ball.x<0||ball.x>520){if(ball.x>520)score++;sc.textContent=score+' : 0';ball={x:260,y:140,vx:ball.x<0?4:-4,vy:3}}loop=requestAnimationFrame(tick)}hint.textContent='Move your paddle with ↑ and ↓. First to score wins!';loop=requestAnimationFrame(tick)}
function memory(){const icons=['🌙','🚀','⭐','🍀','🎮','💎','🐱','🔥'],deck=[...icons,...icons].sort(()=>Math.random()-.5);let opened=[],matched=0,moves=0;stage.innerHTML='<div><div class="score" id="sc">Moves: 0</div><div class="memory" id="grid"></div></div>';const grid=document.getElementById('grid'),sc=document.getElementById('sc');deck.forEach((v,i)=>{const b=document.createElement('button');b.textContent='?';b.onclick=()=>{if(b.classList.contains('open')||opened.length===2)return;b.textContent=v;b.classList.add('open');opened.push({b,v});if(opened.length===2){moves++;sc.textContent='Moves: '+moves;if(opened[0].v===opened[1].v){matched+=2;opened=[];if(matched===16)hint.textContent='You matched them all in '+moves+' moves!'}else setTimeout(()=>{opened.forEach(o=>{o.b.textContent='?';o.b.classList.remove('open')});opened=[]},650)}};grid.append(b)});hint.textContent='Find all eight matching pairs.'}
function clicker(){let count=0;stage.innerHTML='<div><div class="score" id="sc">0 clicks</div><h2>Click the AIR core</h2><button id="core" style="width:145px;height:145px;border-radius:50%;border:7px solid #a7d3ff;background:linear-gradient(145deg,#4da5ff,#1253a1);color:white;font-size:40px;font-weight:800;cursor:pointer;box-shadow:0 10px 30px #4d9be855">A</button><p>How fast can you click?</p></div>';const b=document.getElementById('core'),sc=document.getElementById('sc');b.onclick=()=>{count++;sc.textContent=count+' clicks';b.style.transform='scale(.95)';setTimeout(()=>b.style.transform='',70)};hint.textContent='Click the AIR core and chase your high score.'}
</script></body></html>
""";

    private void Navigate(string value){if(_active is null)return;NavigateEntry(_active,value);}
    private void NavigateEntry(TabEntry t,string value)
    {
        if(t.View.CoreWebView2 is null)return;value=value.Trim();if(value.Length==0)return;
        if(value.Equals("air://dino",StringComparison.OrdinalIgnoreCase)||value.Equals("chrome://dino",StringComparison.OrdinalIgnoreCase)){t.Title="AIR Dino";t.View.CoreWebView2.NavigateToString(DinoHtml);SetStatus("Playing AIR Dino");PushState();return;}
        var game=value.ToLowerInvariant() switch {"air://snake"=>"Snake","air://pong"=>"Pong","air://memory"=>"Memory","air://clicker"=>"Clicker",_=>""};
        if(game.Length>0){t.Title="AIR "+game;t.View.CoreWebView2.NavigateToString(ArcadeHtml.Replace("__INITIAL_GAME__",game.ToLowerInvariant()));SetStatus("Playing AIR "+game);PushState();return;}
        if(value.Equals("air://arcade",StringComparison.OrdinalIgnoreCase)){t.Title="AIR Arcade";t.View.CoreWebView2.NavigateToString(ArcadeHtml);SetStatus("AIR Arcade");PushState();return;}
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

    private async Task StartProviderSignInAsync(string providerName)
    {
        var provider = providerName.Equals("Microsoft", StringComparison.OrdinalIgnoreCase) ? "azure" : "google";
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        try
        {
            listener.Start();
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            var redirect = $"http://localhost:{port}/auth/callback/";
            var verifier = Base64Url(RandomNumberGenerator.GetBytes(32));
            var challenge = Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
            var authUrl = AIR_AUTH_URL + "/auth/v1/authorize?provider=" + provider +
                "&redirect_to=" + Uri.EscapeDataString(redirect) +
                "&code_challenge=" + Uri.EscapeDataString(challenge) +
                "&code_challenge_method=s256" +
                (provider == "azure" ? "&scopes=email" : "") + "&prompt=select_account";
            SetStatus("Opening " + providerName + " sign-in…");
            Process.Start(new ProcessStartInfo(authUrl) { UseShellExecute = true });

            using var client = await listener.AcceptTcpClientAsync().WaitAsync(TimeSpan.FromMinutes(4));
            using var stream = client.GetStream();
            using var reader = new StreamReader(stream, Encoding.ASCII, false, 1024, true);
            var requestLine = await reader.ReadLineAsync();
            string? line;
            do { line = await reader.ReadLineAsync(); } while (!string.IsNullOrEmpty(line));
            var rawTarget = requestLine?.Split(' ').ElementAtOrDefault(1) ?? "/";
            var requestUri = new Uri(new Uri(redirect), rawTarget);
            var query = System.Web.HttpUtility.ParseQueryString(requestUri.Query);
            var code = query["code"];
            var error = query["error_description"] ?? query["error"];
            var page = string.IsNullOrWhiteSpace(code)
                ? "<h2>Sign-in did not finish</h2><p>Return to AIR Browser and check the message there.</p>"
                : "<h2>You're signed in!</h2><p>You can close this tab and return to AIR Browser.</p>";
            var bytes = Encoding.UTF8.GetBytes("<!doctype html><meta charset='utf-8'><title>AIR Account</title><body style='font:18px Segoe UI;background:#101827;color:white;padding:48px'>" + page + "</body>");
            var header = Encoding.ASCII.GetBytes("HTTP/1.1 200 OK\r\nContent-Type: text/html; charset=utf-8\r\nContent-Length: " + bytes.Length + "\r\nConnection: close\r\n\r\n");
            await stream.WriteAsync(header); await stream.WriteAsync(bytes); await stream.FlushAsync();

            if (string.IsNullOrWhiteSpace(code))
                throw new InvalidOperationException(string.IsNullOrWhiteSpace(error) ? "The provider did not return a sign-in code. Check that it is enabled in Supabase." : error);
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
            var body = JsonSerializer.Serialize(new { auth_code = code, code_verifier = verifier });
            using var response = await http.PostAsync(AIR_AUTH_URL + "/auth/v1/token?grant_type=pkce",
                new StringContent(body, Encoding.UTF8, "application/json"));
            var json = await response.Content.ReadAsStringAsync();
            if (!response.IsSuccessStatusCode)
                throw new InvalidOperationException("Supabase could not finish sign-in. Check provider Client ID/Secret and the localhost redirect URL. " + json);
            using var sessionDoc = JsonDocument.Parse(json);
            Send("authSession", sessionDoc.RootElement.Clone());
            SetStatus("Signed in to AIR Account for " + _profile);
        }
        catch (TimeoutException) { SetStatus("Sign-in timed out. Try again."); Send("toast", new { value = "Sign-in timed out. Check the provider setup guide." }); }
        catch (Exception ex) { SetStatus("AIR Account sign-in: " + ex.Message); Send("toast", new { value = "Sign-in failed: " + ex.Message }); }
        finally { listener.Stop(); }
    }

    private static string Base64Url(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+','-').Replace('/','_');

    private void ExportSyncData()
    {
        try
        {
            Directory.CreateDirectory(ProfileFolder);
            List<string> ReadList(string name)
            {
                var path = Path.Combine(ProfileFolder, name);
                return File.Exists(path) ? JsonSerializer.Deserialize<List<string>>(File.ReadAllText(path)) ?? new() : new();
            }
            Send("syncData", new { profile = _profile, bookmarks = ReadList("bookmarks.json"), history = ReadList("history.json") });
        }
        catch (Exception ex) { SetStatus("Could not prepare AIR Sync data: " + ex.Message); }
    }

    private void ImportSyncData(JsonElement payload)
    {
        try
        {
            Directory.CreateDirectory(ProfileFolder);
            foreach (var name in new[] { "bookmarks", "history" })
            {
                if (!payload.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.Array) continue;
                var values = value.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.String)
                    .Select(x => x.GetString() ?? "").Where(x => x.Length < 4096).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                File.WriteAllText(Path.Combine(ProfileFolder, name + ".json"), JsonSerializer.Serialize(values));
            }
            SetStatus("AIR Sync profile restored");
            Send("toast", new { value = "This profile was restored from AIR Sync." });
        }
        catch (Exception ex) { SetStatus("Could not restore AIR Sync data: " + ex.Message); }
    }

    private void SaveBookmark(){if(_active?.View.CoreWebView2?.Source is not string url)return;var file=Path.Combine(ProfileFolder,"bookmarks.json");Directory.CreateDirectory(ProfileFolder);var list=File.Exists(file)?JsonSerializer.Deserialize<List<string>>(File.ReadAllText(file))??new():new List<string>();if(!list.Contains(url))list.Add(url);File.WriteAllText(file,JsonSerializer.Serialize(list));SetStatus("Bookmark saved");}

    private const string AIR_AUTH_URL = "https://rkwpxzotvneoavrczkzo.supabase.co";
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

    private List<Dictionary<string,string>> LoadPasswordRecords()
    {
        try
        {
            if (!File.Exists(PasswordVaultFile)) return new();
            var plain = ProtectedData.Unprotect(File.ReadAllBytes(PasswordVaultFile), null, DataProtectionScope.CurrentUser);
            return JsonSerializer.Deserialize<List<Dictionary<string,string>>>(plain) ?? new();
        }
        catch { return new(); }
    }
    private void SavePasswordRecord(string url,string username,string password,string title)
    {
        Directory.CreateDirectory(ProfileFolder);
        var records=LoadPasswordRecords();
        records.RemoveAll(x=>string.Equals(x.GetValueOrDefault("url"),url,StringComparison.OrdinalIgnoreCase)&&x.GetValueOrDefault("username")==username);
        records.Add(new Dictionary<string,string>{{"url",url},{"username",username},{"password",password},{"name",title}});
        var encrypted=ProtectedData.Protect(JsonSerializer.SerializeToUtf8Bytes(records),null,DataProtectionScope.CurrentUser);
        File.WriteAllBytes(PasswordVaultFile,encrypted);
    }
    private async Task FillSavedPasswordAsync(WebView2 view)
    {
        try
        {
            if(view.CoreWebView2?.Source is not string source||!Uri.TryCreate(source,UriKind.Absolute,out var uri)||uri.Scheme!="https")return;
            var origin=uri.GetLeftPart(UriPartial.Authority);
            var record=LoadPasswordRecords().FirstOrDefault(x=>string.Equals(x.GetValueOrDefault("url"),origin,StringComparison.OrdinalIgnoreCase));
            if(record is null)return;
            var json=JsonSerializer.Serialize(new{username=record.GetValueOrDefault("username"),password=record.GetValueOrDefault("password")});
            var script="(()=>{const d="+json+";const p=document.querySelector('input[type=password]');if(!p||p.value)return;const u=document.querySelector('input[autocomplete=username],input[type=email],input[name*=user i],input[type=text]');if(u&&!u.value){u.value=d.username||'';u.dispatchEvent(new Event('input',{bubbles:true}));u.dispatchEvent(new Event('change',{bubbles:true}));}p.value=d.password||'';p.dispatchEvent(new Event('input',{bubbles:true}));p.dispatchEvent(new Event('change',{bubbles:true}));return true})()";
            await view.CoreWebView2.ExecuteScriptAsync(script);
        }
        catch { }
    }
    private void ManagePasswords()
    {
        var records=LoadPasswordRecords();
        using var dialog=new Form{Text="AIR Password Manager — "+_profile,Size=new Size(720,480),MinimumSize=new Size(520,340),StartPosition=FormStartPosition.CenterParent,BackColor=Color.FromArgb(13,23,38),ForeColor=Color.White};
        var list=new ListView{Dock=DockStyle.Fill,View=View.Details,FullRowSelect=true,GridLines=true,BackColor=Color.FromArgb(20,34,54),ForeColor=Color.White};
        list.Columns.Add("Website",260);list.Columns.Add("Username",280);
        foreach(var r in records){var row=new ListViewItem(r.GetValueOrDefault("url")??"");row.SubItems.Add(r.GetValueOrDefault("username")??"");list.Items.Add(row);}
        var buttons=new FlowLayoutPanel{Dock=DockStyle.Bottom,Height=48,FlowDirection=FlowDirection.RightToLeft,Padding=new Padding(8)};
        var remove=new Button{Text="Delete selected",Width=125,Height=28,BackColor=Color.FromArgb(160,55,65),ForeColor=Color.White,FlatStyle=FlatStyle.Flat};
        var close=new Button{Text="Close",Width=90,Height=28};
        remove.Click+=(_,_)=>{if(list.SelectedIndices.Count==0)return;var index=list.SelectedIndices[0];var target=records[index];if(MessageBox.Show(dialog,"Delete the saved password for "+target.GetValueOrDefault("url")+"?","Delete password",MessageBoxButtons.YesNo,MessageBoxIcon.Warning)!=DialogResult.Yes)return;records.RemoveAt(index);var encrypted=ProtectedData.Protect(JsonSerializer.SerializeToUtf8Bytes(records),null,DataProtectionScope.CurrentUser);File.WriteAllBytes(PasswordVaultFile,encrypted);list.Items.RemoveAt(index);};
        close.Click+=(_,_)=>dialog.Close();buttons.Controls.Add(remove);buttons.Controls.Add(close);dialog.Controls.Add(list);dialog.Controls.Add(buttons);dialog.ShowDialog(this);
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
    private void SetStatus(string value){_status=value;if(!IsDisposed&&IsHandleCreated)_statusLabel.Text=value;Send("status",new{value});}
    private void PushState(){if(!_shellReady)return;Send("state",new{tabs=_tabs.Select(t=>new{id=t.Id,title=t.Title,group=t.Group}).ToArray(),groups=_groups.ToArray(),active=_active?.Id,address=_active?.View.CoreWebView2?.Source??"",status=_status,profile=_profile,profiles=_profiles.ToArray()});}
    private void Send(string type,object data){if(!_shellReady||_shell.CoreWebView2 is null)return;_shell.CoreWebView2.PostWebMessageAsJson(JsonSerializer.Serialize(new{type,data}));}
    private sealed class TabEntry(string id,WebView2 view,string title){public string Id=id;public WebView2 View=view;public string Title=title;public string Group="Ungrouped";}
}