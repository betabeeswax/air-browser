const $=id=>document.getElementById(id);let tabs=[],active=null,profiles=["Personal","Work"],dragId=null,toastTimer,collapsedGroups=new Set();
function send(type,extra={}){window.chrome?.webview?.postMessage({type,...extra})}
function toast(s){const t=$("toast");t.textContent=s;t.classList.add("show");clearTimeout(toastTimer);toastTimer=setTimeout(()=>t.classList.remove("show"),2600)}
function render(){
 const host=$("tabs");host.replaceChildren();
 for(const t of tabs){
  const group=t.group&&t.group!=="Ungrouped"?t.group:null;
  const first=group&&tabs.find(x=>x.group===group)?.id===t.id;
  const collapsed=group&&collapsedGroups.has(group);
  const b=document.createElement("div");
  b.className="tab"+(t.id===active?" active":"")+(group?" in-group":"")+(collapsed&&!first?" group-collapsed":"");
  b.draggable=true;b.dataset.id=t.id;b.innerHTML='<span class="name"></span><button class="close" title="Close tab">×</button>';
  if(first){const label=document.createElement("button");label.className="tab-group-label";label.textContent=(collapsed?"▸ ":"▾ ")+group;label.title=collapsed?"Expand group":"Collapse group";label.draggable=false;label.onclick=e=>{e.stopPropagation();collapsedGroups.has(group)?collapsedGroups.delete(group):collapsedGroups.add(group);render()};host.append(label)}
  b.querySelector(".name").textContent=collapsed&&first?"":t.title;
  b.addEventListener("click",e=>{if(!e.target.closest(".close")&&!e.target.closest(".tab-group-label")){active=t.id;render();send("select",{id:t.id})}});
  b.querySelector(".close").addEventListener("click",e=>{e.stopPropagation();send("close",{id:t.id})});
  b.addEventListener("dragstart",e=>{dragId=t.id;e.dataTransfer.setData("text/plain",t.id);e.dataTransfer.effectAllowed="move";requestAnimationFrame(()=>b.classList.add("dragging"))});
  b.addEventListener("dragend",e=>{b.classList.remove("dragging");if(dragId===t.id&&(e.clientX<0||e.clientY<0||e.clientX>innerWidth||e.clientY>innerHeight))send("detachTab",{id:t.id});dragId=null});
  b.addEventListener("dragover",e=>{e.preventDefault();b.classList.add("dragover")});
  b.addEventListener("dragleave",()=>b.classList.remove("dragover"));
  b.addEventListener("drop",e=>{e.preventDefault();b.classList.remove("dragover");const source=e.dataTransfer.getData("text/plain");if(source&&source!==t.id)send("groupTabs",{source,target:t.id})});
  host.append(b);
 }
}
function navigate(){const s=JSON.parse(localStorage.getItem("airSettings")||"{}");send("navigate",{value:$("address").value,engine:s.defaultEngine||"Bing"})}
function hidePanels(){["menuPanel","settingsPanel","profilesPanel","accountPanel"].forEach(id=>$(id).classList.add("hidden"));send("overlay",{expanded:false})}
function showPanel(id){hidePanels();$(id).classList.remove("hidden");send("overlay",{expanded:true})}
function profileMeta(){return JSON.parse(localStorage.getItem("airProfileMeta")||"{}")}
function renderProfiles(){const meta=profileMeta();const sel=$("profile");const current=meta[window.currentProfile]||{};sel.textContent=(current.avatar||"●")+" "+(current.label||window.currentProfile||"Personal");const list=$("profileList");list.replaceChildren();profiles.forEach(p=>{const data=meta[p]||{};const row=document.createElement("div");row.className="profile-item";const dot=document.createElement("span");dot.className="profile-dot";if(data.avatarImage){dot.textContent="";dot.style.backgroundImage='url("'+data.avatarImage+'")';dot.style.backgroundSize="cover";dot.style.backgroundPosition="center"}else{dot.style.backgroundImage="";dot.textContent=data.avatar||p.slice(0,1).toUpperCase()}const choose=document.createElement("button");choose.textContent=(data.label||p)+(p===window.currentProfile?" · Current":"");choose.onclick=()=>{send("profile",{name:p});hidePanels()};const edit=document.createElement("button");edit.textContent="✎";edit.title="Edit profile";edit.onclick=()=>{window.editingProfile=p;$("editProfileName").value=data.label||p;$("editProfileAvatar").value=data.avatar||"●";$("editProfilePhoto").value="";window.pendingProfilePhoto=data.avatarImage||"";$("profileEditor").classList.remove("hidden")};row.append(dot,choose,edit);if(p!=="Personal"){const del=document.createElement("button");del.textContent="×";del.title="Delete profile";del.onclick=()=>{if(confirm("Delete profile '"+p+"' and its local browser data?"))send("deleteProfile",{name:p})};row.append(del)}list.append(row)})}
function profileSettingsKey(){return "airSettings:"+encodeURIComponent(window.currentProfile||"Personal")}\nfunction readProfileSettings(){try{return JSON.parse(localStorage.getItem(profileSettingsKey())||localStorage.getItem("airSettings")||"{}")}catch{return {}}}\nfunction applySettings(){const s=readProfileSettings();$("theme").value=s.theme||"dark";$("compactTabs").checked=!!s.compactTabs;$("showBookmarks").checked=s.showBookmarks!==false;$("confirmClose").checked=s.confirmClose!==false;$("restoreTabs").checked=!!s.restoreTabs;$("defaultEngine").value=s.defaultEngine||"Bing";$("suggestions").checked=s.suggestions!==false;$("blockPopups").checked=s.blockPopups!==false;$("doNotTrack").checked=!!s.doNotTrack;$("customStartupUrl").value=s.customStartupUrl||"";document.querySelectorAll('input[name="startup"]').forEach(r=>r.checked=r.value===(s.startup||"home"));document.body.classList.toggle("light",s.theme==="light");document.body.dataset.theme=s.theme||"dark";document.body.classList.toggle("compact",!!s.compactTabs);$("bookmark").classList.toggle("hidden",s.showBookmarks===false)}
function saveSettings(){const s={};["theme","compactTabs","showBookmarks","confirmClose","restoreTabs","defaultEngine","suggestions","blockPopups","doNotTrack","customStartupUrl"].forEach(id=>{const e=$(id);s[id]=e.type==="checkbox"?e.checked:e.value});s.startup=document.querySelector('input[name="startup"]:checked')?.value||"home";localStorage.setItem(profileSettingsKey(),JSON.stringify(s));send("settingsChanged",{settings:s});applySettings();toast("Settings saved")}
$("go").onclick=navigate;$("address").addEventListener("keydown",e=>{if(e.key==="Enter")navigate()});$("newTab").onclick=()=>send("newTab");$("back").onclick=()=>send("back");$("forward").onclick=()=>send("forward");$("reload").onclick=()=>send("reload");$("bookmark").onclick=()=>send("bookmark");$("profile").onclick=()=>{renderProfiles();showPanel("profilesPanel")};$("linkAccount").onclick=()=>{updateAccountView();setAccountMode("signin");showPanel("accountPanel")};
$("accountQuick").onclick=()=>{updateAccountView();setAccountMode("signin");showPanel("accountPanel")};$("menu").onclick=()=>{if($("menuPanel").classList.contains("hidden"))showPanel("menuPanel");else hidePanels()};
document.addEventListener("click",e=>{const action=e.target.closest("[data-action]")?.dataset.action;if(action){hidePanels();({newTab:()=>send("newTab"),duplicateTab:()=>send("duplicateTab"),copyLink:()=>send("copyLink"),bookmarks:()=>send("showBookmarks"),history:()=>send("showHistory"),downloads:()=>send("downloads"),update:()=>send("update"),settings:()=>showPanel("settingsPanel"),profiles:()=>{renderProfiles();showPanel("profilesPanel")},about:()=>{showPanel("settingsPanel");document.querySelector('[data-section="about"]').click()}})[action]?.();return}const close=e.target.closest("[data-close]")?.dataset.close;if(close){hidePanels();return}if(!e.target.closest(".popover")&&!e.target.closest("#menu")&&!e.target.closest("#profile")&&![...document.querySelectorAll(".popover")].every(p=>p.classList.contains("hidden")))hidePanels()});
document.querySelectorAll("[data-section]").forEach(b=>b.onclick=()=>{document.querySelectorAll("[data-section]").forEach(x=>x.classList.toggle("selected",x===b));document.querySelectorAll("[data-page]").forEach(x=>x.classList.toggle("hidden",x.dataset.page!==b.dataset.section))});
["theme","compactTabs","showBookmarks","confirmClose","restoreTabs","defaultEngine","suggestions","blockPopups","doNotTrack","customStartupUrl"].forEach(id=>$(id).addEventListener("change",saveSettings));document.querySelectorAll('input[name="startup"]').forEach(r=>r.addEventListener("change",saveSettings));
$("createProfile").onclick=()=>{const name=$("newProfileName").value.trim();if(name){send("createProfile",{name});$("newProfileName").value="";hidePanels()}};
$("editProfilePhoto").onchange=e=>{const file=e.target.files?.[0];if(!file)return;if(file.size>3*1024*1024){toast("Choose an image smaller than 3 MB");e.target.value="";return}const reader=new FileReader();reader.onload=()=>{window.pendingProfilePhoto=reader.result;toast("Picture selected")};reader.readAsDataURL(file)};
$("removeProfilePhoto").onclick=()=>{window.pendingProfilePhoto="";toast("Profile picture will be removed")};
$("startBackgroundFile").onchange=e=>{const file=e.target.files?.[0];if(!file)return;if(!["image/png","image/jpeg","image/webp"].includes(file.type)||file.size>4*1024*1024){toast("Choose a PNG, JPG or WebP image under 4 MB");e.target.value="";return}const reader=new FileReader();reader.onload=()=>send("saveStartBackground",{dataUrl:reader.result});reader.readAsDataURL(file)};
$("removeStartBackground").onclick=()=>send("removeStartBackground");
$("saveProfileEdit").onclick=()=>{const p=window.editingProfile;if(!p)return;const meta=profileMeta();meta[p]={label:$("editProfileName").value.trim()||p,avatar:$("editProfileAvatar").value,avatarImage:window.pendingProfilePhoto||""};localStorage.setItem("airProfileMeta",JSON.stringify(meta));$("profileEditor").classList.add("hidden");renderProfiles();toast("Profile updated")};

const AIR_AUTH_URL="https://rkwpxzotvneoavrczkzo.supabase.co";
const AIR_AUTH_KEY="sb_publishable_XZ71KE8FXhRoPdC1K7tEJw_tN4wReB_";
let accountMode="signin";
function setAccountMode(mode){
 accountMode=mode;
 $("accountModeIn").classList.toggle("selected",mode==="signin");
 $("accountModeUp").classList.toggle("selected",mode==="signup");
 $("accountSubmit").textContent=mode==="signin"?"Sign in":"Create AIR Account";
 $("accountTitle").textContent=mode==="signin"?"Welcome back":"Create your AIR Account";
 $("accountHeading").textContent=mode==="signin"?"Good to see you again.":"One account for all of AIR.";
 $("accountIntro").textContent=mode==="signin"?"Sign in to your AIR Account. Browsing also works without an account.":"Create an AIR Account for your AIR profile and secure AIR Sync.";
 $("accountPassword").autocomplete=mode==="signin"?"current-password":"new-password";
}
function showAccountMessage(message,isError=false){const el=$("accountMessage");el.textContent=message;el.style.color=isError?"#ff9b9b":""}
function sessionKey(){return "airAuthSession:"+encodeURIComponent(window.currentProfile||"Personal")}
if(!localStorage.getItem("airAuthSession:Personal")&&localStorage.getItem("airAuthSession"))localStorage.setItem("airAuthSession:Personal",localStorage.getItem("airAuthSession"));
function storedAirSession(){try{return JSON.parse(localStorage.getItem(sessionKey())||"null")}catch{return null}}
function updateAccountView(){
 const session=storedAirSession(),signed=!!session?.access_token;
 $("accountQuick").textContent=signed?"Account ✓":"Sign in";$("accountQuick").classList.toggle("signed-in",signed);
 $("accountSigned").classList.toggle("hidden",!signed);
 $("accountModeIn").classList.toggle("hidden",signed);$("accountModeUp").classList.toggle("hidden",signed);
 $("accountEmail").parentElement.classList.toggle("hidden",signed);$("accountPassword").parentElement.classList.toggle("hidden",signed);
 $("accountSubmit").classList.toggle("hidden",signed);
 $("accountSignedEmail").textContent=session?.user?.email||"Signed in to AIR Account";
 if(signed){$("accountHeading").textContent="You're signed in."; $("accountIntro").textContent="Your AIR Account is ready. You can securely back up and restore this browser profile with AIR Sync."; $("accountTitle").textContent="Your account";}
}
async function submitAirAccount(){
 const email=$("accountEmail").value.trim(),password=$("accountPassword").value;
 if(!email||!password){showAccountMessage("Enter your email and password.",true);return}
 if(password.length<8){showAccountMessage("Your password must be at least 8 characters.",true);return}
 const button=$("accountSubmit");button.disabled=true;button.textContent=accountMode==="signin"?"Signing in…":"Creating account…";
 try{
  const endpoint=accountMode==="signin"?"/auth/v1/token?grant_type=password":"/auth/v1/signup";
  const response=await fetch(AIR_AUTH_URL+endpoint,{method:"POST",headers:{"apikey":AIR_AUTH_KEY,"Content-Type":"application/json"},body:JSON.stringify({email,password})});
  const data=await response.json();
  if(!response.ok)throw new Error(data.msg||data.message||data.error_description||"Could not complete sign-in.");
  if(data.access_token){localStorage.setItem(sessionKey(),JSON.stringify(data));updateAccountView();showAccountMessage("You're signed in. AIR Account is ready on this device.");toast("Signed in to AIR Account");}
  else if(accountMode==="signup"){showAccountMessage("Check your email for a confirmation link, then come back and sign in.");}
  else throw new Error("The sign-in response did not include a session.");
 }catch(error){showAccountMessage(error.message||"Sign-in failed. Please try again.",true)}
 finally{button.disabled=false;button.textContent=accountMode==="signin"?"Sign in":"Create AIR Account"}
}

function base64Bytes(bytes){let s="";bytes.forEach(b=>s+=String.fromCharCode(b));return btoa(s)}
function fromBase64(s){return Uint8Array.from(atob(s),c=>c.charCodeAt(0))}
async function deriveSyncKey(passphrase,salt){
 const material=await crypto.subtle.importKey("raw",new TextEncoder().encode(passphrase),"PBKDF2",false,["deriveKey"]);
 return crypto.subtle.deriveKey({name:"PBKDF2",salt,iterations:250000,hash:"SHA-256"},material,{name:"AES-GCM",length:256},false,["encrypt","decrypt"]);
}
async function syncCredentials(){let session=storedAirSession();const pass=$("syncPassphrase")?.value||"";if(!session?.access_token||!session?.user?.id){showAccountMessage("Sign in to AIR Account before using AIR Sync.",true);return null}if(pass.length<10){showAccountMessage("Use a sync passphrase with at least 10 characters.",true);return null}if(session.refresh_token){const refreshed=await fetch(AIR_AUTH_URL+"/auth/v1/token?grant_type=refresh_token",{method:"POST",headers:{apikey:AIR_AUTH_KEY,"Content-Type":"application/json"},body:JSON.stringify({refresh_token:session.refresh_token})});if(refreshed.ok){session=await refreshed.json();localStorage.setItem(sessionKey(),JSON.stringify(session));updateAccountView()}else throw new Error("Your AIR Account session expired. Sign in again.")}return {session,pass}}
async function backupSyncData(data){
 const creds=await syncCredentials();if(!creds)return;
 try{
  const salt=crypto.getRandomValues(new Uint8Array(16)),iv=crypto.getRandomValues(new Uint8Array(12)),key=await deriveSyncKey(creds.pass,salt);
  data.settings=readProfileSettings();data.profileMeta=JSON.parse(localStorage.getItem("airProfileMeta")||"{}");
  const plain=new TextEncoder().encode(JSON.stringify(data));
  const encrypted=await crypto.subtle.encrypt({name:"AES-GCM",iv},key,plain);
  const packed=JSON.stringify({version:1,salt:base64Bytes(salt),iv:base64Bytes(iv),ciphertext:base64Bytes(new Uint8Array(encrypted))});
  const response=await fetch(AIR_AUTH_URL+"/rest/v1/air_sync_profiles?on_conflict=user_id%2Cprofile_key",{method:"POST",headers:{apikey:AIR_AUTH_KEY,Authorization:"Bearer "+creds.session.access_token,"Content-Type":"application/json",Prefer:"resolution=merge-duplicates,return=minimal"},body:JSON.stringify({user_id:creds.session.user.id,profile_key:data.profile,profile_name:data.profile,encrypted_payload:packed,encryption_version:1,updated_at:new Date().toISOString()})});
  if(!response.ok)throw new Error((await response.text()).slice(0,240));
  showAccountMessage("Encrypted backup saved for "+data.profile+". Your sync passphrase is needed to restore it.");toast("AIR Sync backup complete");
 }catch(e){showAccountMessage("Backup failed: "+e.message,true)}
}
async function restoreSyncData(){
 const creds=await syncCredentials();if(!creds)return;
 try{
  const profile=window.currentProfile||"Personal";
  const url=AIR_AUTH_URL+"/rest/v1/air_sync_profiles?select=profile_name,encrypted_payload&profile_key=eq."+encodeURIComponent(profile)+"&limit=1";
  const response=await fetch(url,{headers:{apikey:AIR_AUTH_KEY,Authorization:"Bearer "+creds.session.access_token}});
  if(!response.ok)throw new Error((await response.text()).slice(0,240));
  const rows=await response.json();if(!rows.length){showAccountMessage("No cloud backup exists for this profile yet.",true);return}
  const packed=JSON.parse(rows[0].encrypted_payload),salt=fromBase64(packed.salt),iv=fromBase64(packed.iv),cipher=fromBase64(packed.ciphertext);
  const key=await deriveSyncKey(creds.pass,salt),plain=await crypto.subtle.decrypt({name:"AES-GCM",iv},key,cipher),data=JSON.parse(new TextDecoder().decode(plain));
  if(data.profile!==profile)throw new Error("This backup belongs to a different profile.");
  if(!confirm("Restore the cloud backup for "+profile+"? This replaces this profile's local bookmarks and history."))return;
  if(data.settings)localStorage.setItem(profileSettingsKey(),JSON.stringify(data.settings));
  if(data.profileMeta)localStorage.setItem("airProfileMeta",JSON.stringify(data.profileMeta));
  applySettings();send("syncImport",{payload:{bookmarks:data.bookmarks||[],history:data.history||[],startBackground:data.startBackground||""}});
  showAccountMessage("Backup decrypted and restored for "+profile+".");
 }catch(e){showAccountMessage("Restore failed. Check your passphrase. "+e.message,true)}
}

$("accountModeIn").onclick=()=>setAccountMode("signin");
$("accountModeUp").onclick=()=>setAccountMode("signup");
$("accountSubmit").onclick=submitAirAccount;
$("accountPassword").addEventListener("keydown",e=>{if(e.key==="Enter")submitAirAccount()});
$("syncBackup").onclick=()=>{send("syncExport")};
$("syncRestore").onclick=restoreSyncData;
$("accountTry").onclick=()=>{hidePanels();toast("You're using AIR without an account")};
$("accountSignOut").onclick=()=>{localStorage.removeItem(sessionKey());updateAccountView();setAccountMode("signin");showAccountMessage("You have signed out.");toast("Signed out")};
$("linkAccount").onclick=()=>{updateAccountView();setAccountMode("signin");showPanel("accountPanel")};
updateAccountView();setAccountMode("signin");

$("clearBrowsing").onclick=()=>{if(confirm("Clear browsing history and saved cookies for this profile?"))send("clearData")};
$("managePasswords").onclick=()=>{hidePanels();send("managePasswords")};
$("winMin").onclick=()=>send("windowMinimize");$("winMax").onclick=()=>send("windowMaximize");$("winClose").onclick=()=>send("windowClose");
document.querySelector(".brand")?.addEventListener("mousedown",()=>send("dragWindow"));
["importBookmarks","exportBookmarks","importPasswords","exportPasswords"].forEach(id=>$(id).onclick=()=>send(id));
window.chrome?.webview?.addEventListener("message",async e=>{const m=e.data;if(m.type==="state"){tabs=m.data.tabs;active=m.data.active;render();$("address").value=m.data.address||"";$("hint").textContent=m.data.status||"Ready";const previousProfile=window.currentProfile||"Personal";window.currentProfile=m.data.profile||"Personal";profiles=m.data.profiles||profiles;if(previousProfile!==window.currentProfile){applySettings();send("settingsChanged",{settings:readProfileSettings()})}renderProfiles();updateAccountView()}else if(m.type==="address")$("address").value=m.data.value;else if(m.type==="status")$("hint").textContent=m.data.value;else if(m.type==="toast")toast(m.data.value);else if(m.type==="authSession"){localStorage.setItem(sessionKey(),JSON.stringify(m.data));updateAccountView();showAccountMessage("Signed in to AIR Account. AIR Sync is now available.");toast("Signed in to AIR Account")}else if(m.type==="syncData"){await backupSyncData(m.data)}});
applySettings();send("ready");
document.addEventListener("keydown",e=>{
 const mod=e.ctrlKey||e.metaKey;
 if(!mod)return;
 const key=e.key.toLowerCase();
 if(key==="t"){e.preventDefault();send("newTab")}
 else if(key==="l"){e.preventDefault();$("address").focus();$("address").select()}
 else if(key==="w"){e.preventDefault();if(active)send("close",{id:active})}
 else if(key==="r"){e.preventDefault();send("reload")}
 else if(key==="d"){e.preventDefault();send("bookmark")}
 else if(key==="k"&&e.shiftKey){e.preventDefault();send("duplicateTab")}
 else if(key==="="||key==="+"){e.preventDefault();send("zoomIn")}
 else if(key==="-"){e.preventDefault();send("zoomOut")}
 else if(key==="0"){e.preventDefault();send("zoomReset")}
});
