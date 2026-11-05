// ===== AIR Browser Main App =====

const App = {
  data: null,
  tabs: [],
  activeTabId: null,
  tabIdCounter: 1,

  engines: {
    bing: "https://www.bing.com/search?q=",
    google: "https://www.google.com/search?q=",
    duckduckgo: "https://duckduckgo.com/?q=",
    brave: "https://search.brave.com/search?q="
  },

  init() {
    this.data = Storage.load();
    this.applyTheme();
    this.setupEventListeners();
    this.renderProfiles();
    this.renderWidgets();
    this.createNewTab(true);
    this.updateSearchEngineSelect();
    this.showToast("Welcome to AIR Browser");
  },

  applyTheme() {
    const profile = Storage.getCurrentProfile(this.data);
    document.body.classList.toggle("dark", profile.settings.darkMode !== false);
  },

  renderProfiles() {
    const select = document.getElementById("profile-select");
    select.innerHTML = "";
    for (const id in this.data.profiles) {
      const p = this.data.profiles[id];
      const opt = document.createElement("option");
      opt.value = id;
      opt.textContent = p.name;
      if (id === this.data.currentProfileId) opt.selected = true;
      select.appendChild(opt);
    }
  },

  switchProfile(id) {
    if (Storage.switchProfile(id)) {
      this.data = Storage.load();
      this.applyTheme();
      this.renderProfiles();
      this.renderWidgets();
      this.updateSearchEngineSelect();
      this.showToast("Switched to profile: " + this.data.profiles[id].name);
    }
  },

  createNewTab(isStartPage = false) {
    const id = this.tabIdCounter++;
    const tab = {
      id,
      title: isStartPage ? "New Tab" : "Loading...",
      url: isStartPage ? "air://start" : "",
      isStart: isStartPage,
      groupId: null
    };
    this.tabs.push(tab);
    this.activeTabId = id;
    this.renderTabs();
    this.showPage(isStartPage ? "start" : "browser");
    return id;
  },

  closeTab(id) {
    const idx = this.tabs.findIndex(t => t.id === id);
    if (idx === -1) return;
    this.tabs.splice(idx, 1);
    if (this.tabs.length === 0) {
      this.createNewTab(true);
    } else if (this.activeTabId === id) {
      this.activeTabId = this.tabs[Math.max(0, idx - 1)].id;
      this.activateTab(this.activeTabId);
    }
    this.renderTabs();
  },

  activateTab(id) {
    this.activeTabId = id;
    const tab = this.tabs.find(t => t.id === id);
    if (!tab) return;
    this.renderTabs();
    if (tab.isStart || tab.url === "air://start") {
      this.showPage("start");
      document.getElementById("address-bar").value = "";
    } else {
      this.showPage("browser");
      document.getElementById("address-bar").value = tab.url;
      const frame = document.getElementById("web-frame");
      if (frame.src !== tab.url) frame.src = tab.url;
    }
  },

  renderTabs() {
    const container = document.getElementById("tabs-container");
    container.innerHTML = "";
    this.tabs.forEach(tab => {
      const el = document.createElement("div");
      el.className = "tab" + (tab.id === this.activeTabId ? " active" : "");
      if (tab.groupId) {
        el.classList.add("group-colored");
        const group = this.getTabGroup(tab.groupId);
        if (group) el.style.setProperty("--group-color", group.color);
      }
      el.innerHTML = `
        <span class="tab-title">${this.escapeHtml(tab.title)}</span>
        <span class="tab-close">x</span>
      `;
      el.querySelector(".tab-title").addEventListener("click", () => this.activateTab(tab.id));
      el.querySelector(".tab-close").addEventListener("click", (e) => {
        e.stopPropagation();
        this.closeTab(tab.id);
      });
      container.appendChild(el);
    });
  },

  navigate(input) {
    if (!input || !input.trim()) return;
    input = input.trim();

    let url = input;
    if (!input.includes(".") && !input.startsWith("http") && !input.startsWith("air://")) {
      const engine = this.getCurrentSearchEngine();
      url = this.engines[engine] + encodeURIComponent(input);
    } else if (!input.startsWith("http") && !input.startsWith("air://")) {
      url = "https://" + input;
    }

    let tab = this.tabs.find(t => t.id === this.activeTabId);
    if (!tab) {
      this.createNewTab(false);
      tab = this.tabs.find(t => t.id === this.activeTabId);
    }

    tab.url = url;
    tab.isStart = false;
    tab.title = this.extractTitle(url);

    this.showPage("browser");
    document.getElementById("address-bar").value = url;
    document.getElementById("web-frame").src = url;
    this.renderTabs();
    this.addToHistory(url, tab.title);
  },

  goHome() {
    const tab = this.tabs.find(t => t.id === this.activeTabId);
    if (tab) {
      tab.url = "air://start";
      tab.isStart = true;
      tab.title = "New Tab";
    }
    this.showPage("start");
    document.getElementById("address-bar").value = "";
    this.renderTabs();
  },

  showPage(name) {
    document.querySelectorAll(".page").forEach(p => p.classList.remove("active"));
    if (name === "start") {
      document.getElementById("start-page").classList.add("active");
    } else {
      document.getElementById("browser-view").classList.add("active");
    }
  },

  getCurrentSearchEngine() {
    const profile = Storage.getCurrentProfile(this.data);
    return profile.searchEngine || "bing";
  },

  updateSearchEngineSelect() {
    const select = document.getElementById("search-engine-select");
    select.value = this.getCurrentSearchEngine();
  },

  setSearchEngine(engine) {
    const profile = Storage.getCurrentProfile(this.data);
    profile.searchEngine = engine;
    Storage.save(this.data);
    this.showToast("Search engine set to " + engine);
  },

  renderWidgets() {
    const container = document.getElementById("widgets-container");
    container.innerHTML = "";
    const profile = Storage.getCurrentProfile(this.data);
    const widgets = profile.widgets || ["weather"];
    widgets.forEach(type => {
      Widgets.render(type, container);
    });
  },

  removeWidget(type) {
    const profile = Storage.getCurrentProfile(this.data);
    profile.widgets = (profile.widgets || []).filter(w => w !== type);
    Storage.save(this.data);
    this.showToast("Widget removed");
  },

  addWidget(type) {
    const profile = Storage.getCurrentProfile(this.data);
    if (!profile.widgets) profile.widgets = [];
    if (!profile.widgets.includes(type)) {
      profile.widgets.push(type);
      Storage.save(this.data);
      this.renderWidgets();
      this.showToast((Widgets.types[type]?.name || type) + " added");
    }
  },

  openBookmarks() {
    this.openSidePanel("Bookmarks", this.renderBookmarksContent());
  },

  renderBookmarksContent() {
    const profile = Storage.getCurrentProfile(this.data);
    const bookmarks = profile.bookmarks || [];
    let html = `
      <button class="btn" style="width:100%;margin-bottom:12px" id="add-bookmark-btn">+ Add Bookmark</button>
    `;
    if (bookmarks.length === 0) {
      html += `<p style="color:var(--text-dim);text-align:center;padding:20px">No bookmarks yet</p>`;
    } else {
      html += bookmarks.map((b, i) => `
        <div class="list-item" data-url="${this.escapeHtml(b.url)}">
          <span class="icon">*</span>
          <div class="info">
            <div class="title">${this.escapeHtml(b.title || b.url)}</div>
            <div class="subtitle">${this.escapeHtml(b.url)}</div>
          </div>
          <button class="widget-remove" data-index="${i}" title="Delete">x</button>
        </div>
      `).join("");
    }
    return html;
  },

  addBookmark(url, title) {
    const profile = Storage.getCurrentProfile(this.data);
    if (!profile.bookmarks) profile.bookmarks = [];
    profile.bookmarks.push({ url, title: title || url, added: Date.now() });
    Storage.save(this.data);
    this.showToast("Bookmark added");
  },

  openTabGroups() {
    this.openSidePanel("Tab Groups", this.renderTabGroupsContent());
  },

  getTabGroup(id) {
    const profile = Storage.getCurrentProfile(this.data);
    return (profile.tabGroups || []).find(g => g.id === id);
  },

  renderTabGroupsContent() {
    const profile = Storage.getCurrentProfile(this.data);
    const groups = profile.tabGroups || [];
    let html = `
      <button class="btn" style="width:100%;margin-bottom:12px" id="add-group-btn">+ New Tab Group</button>
    `;
    if (groups.length === 0) {
      html += `<p style="color:var(--text-dim);text-align:center;padding:20px">No tab groups yet.<br>Create one and assign tabs to it.</p>`;
    } else {
      html += groups.map(g => `
        <div class="list-item" style="border-left:4px solid ${g.color}">
          <div class="info">
            <div class="title">${this.escapeHtml(g.name)}</div>
            <div class="subtitle">${this.tabs.filter(t => t.groupId === g.id).length} tabs</div>
          </div>
        </div>
      `).join("");
    }
    return html;
  },

  createTabGroup(name, color = "#3b82f6") {
    const profile = Storage.getCurrentProfile(this.data);
    if (!profile.tabGroups) profile.tabGroups = [];
    const id = "group_" + Date.now();
    profile.tabGroups.push({ id, name, color });
    Storage.save(this.data);
    this.showToast("Tab group \"" + name + "\" created");
    return id;
  },

  addToHistory(url, title) {
    const profile = Storage.getCurrentProfile(this.data);
    if (!profile.history) profile.history = [];
    profile.history.unshift({ url, title, time: Date.now() });
    if (profile.history.length > 200) profile.history.length = 200;
    Storage.save(this.data);
  },

  openSidePanel(title, contentHtml) {
    document.getElementById("side-title").textContent = title;
    document.getElementById("side-content").innerHTML = contentHtml;
    document.getElementById("side-panel").classList.remove("hidden");

    const addBm = document.getElementById("add-bookmark-btn");
    if (addBm) {
      addBm.onclick = () => {
        const url = prompt("Enter URL:");
        if (url) {
          const title = prompt("Enter title (optional):") || url;
          this.addBookmark(url, title);
          this.openBookmarks();
        }
      };
    }

    const addGroup = document.getElementById("add-group-btn");
    if (addGroup) {
      addGroup.onclick = () => {
        const name = prompt("Group name:");
        if (name) {
          const colors = ["#3b82f6", "#22c55e", "#eab308", "#ef4444", "#a855f7", "#ec4899"];
          const color = colors[Math.floor(Math.random() * colors.length)];
          this.createTabGroup(name, color);
          this.openTabGroups();
        }
      };
    }

    document.querySelectorAll("#side-content .list-item[data-url]").forEach(el => {
      el.addEventListener("click", (e) => {
        if (e.target.classList.contains("widget-remove")) return;
        this.navigate(el.dataset.url);
        this.closeSidePanel();
      });
    });

    document.querySelectorAll("#side-content .widget-remove[data-index]").forEach(btn => {
      btn.addEventListener("click", (e) => {
        e.stopPropagation();
        const idx = parseInt(btn.dataset.index);
        const profile = Storage.getCurrentProfile(this.data);
        profile.bookmarks.splice(idx, 1);
        Storage.save(this.data);
        this.openBookmarks();
      });
    });
  },

  closeSidePanel() {
    document.getElementById("side-panel").classList.add("hidden");
  },

  openModal(title, bodyHtml) {
    document.getElementById("modal-title").textContent = title;
    document.getElementById("modal-body").innerHTML = bodyHtml;
    document.getElementById("modal-overlay").classList.remove("hidden");
  },

  closeModal() {
    document.getElementById("modal-overlay").classList.add("hidden");
  },

  openMenu() {
    const html = `
      <div class="list-item" id="menu-export"><span class="icon">E</span><div class="info"><div class="title">Export Data</div><div class="subtitle">Download backup JSON</div></div></div>
      <div class="list-item" id="menu-import"><span class="icon">I</span><div class="info"><div class="title">Import Data</div><div class="subtitle">Restore from backup</div></div></div>
      <div class="list-item" id="menu-github"><span class="icon">G</span><div class="info"><div class="title">GitHub Sync</div><div class="subtitle">Coming soon</div></div></div>
      <div class="list-item" id="menu-new-profile"><span class="icon">P</span><div class="info"><div class="title">New Profile</div><div class="subtitle">Create another profile</div></div></div>
      <div class="list-item" id="menu-themes"><span class="icon">T</span><div class="info"><div class="title">Themes</div><div class="subtitle">Change look</div></div></div>
      <div class="list-item" id="menu-passwords"><span class="icon">K</span><div class="info"><div class="title">Passwords</div><div class="subtitle">Local only for now</div></div></div>
    `;
    this.openSidePanel("Menu", html);

    document.getElementById("menu-export").onclick = () => {
      Storage.exportData();
      this.showToast("Data exported");
      this.closeSidePanel();
    };

    document.getElementById("menu-import").onclick = () => {
      const input = document.createElement("input");
      input.type = "file";
      input.accept = ".json";
      input.onchange = (e) => {
        const file = e.target.files[0];
        if (!file) return;
        const reader = new FileReader();
        reader.onload = (ev) => {
          if (Storage.importData(ev.target.result)) {
            this.data = Storage.load();
            this.renderProfiles();
            this.renderWidgets();
            this.showToast("Data imported successfully");
          } else {
            this.showToast("Import failed - invalid file");
          }
        };
        reader.readAsText(file);
      };
      input.click();
      this.closeSidePanel();
    };

    document.getElementById("menu-github").onclick = () => {
      this.showToast("GitHub sync will be added soon. For now use Export/Import.");
    };

    document.getElementById("menu-new-profile").onclick = () => {
      const name = prompt("Profile name:");
      if (name) {
        const id = Storage.createProfile(name);
        this.data = Storage.load();
        this.switchProfile(id);
        this.closeSidePanel();
      }
    };

    document.getElementById("menu-themes").onclick = () => {
      this.showToast("Theme switcher coming in next update");
    };

    document.getElementById("menu-passwords").onclick = () => {
      this.showToast("Password manager (local only) coming soon");
    };
  },

  openAddWidget() {
    let html = `<p style="color:var(--text-dim);margin-bottom:16px">Choose a widget to add:</p>`;
    for (const [key, val] of Object.entries(Widgets.types)) {
      html += `
        <div class="list-item" data-widget="${key}">
          <span class="icon">${val.icon}</span>
          <div class="info">
            <div class="title">${val.name}</div>
            <div class="subtitle">${val.description}</div>
          </div>
        </div>
      `;
    }
    this.openModal("Add Widget", html);

    document.querySelectorAll("#modal-body .list-item").forEach(el => {
      el.addEventListener("click", () => {
        this.addWidget(el.dataset.widget);
        this.closeModal();
      });
    });
  },

  extractTitle(url) {
    try {
      const u = new URL(url);
      return u.hostname.replace("www.", "");
    } catch {
      return url.slice(0, 30);
    }
  },

  escapeHtml(str) {
    return String(str)
      .replace(/&/g, "&amp;")
      .replace(/</g, "&lt;")
      .replace(/>/g, "&gt;")
      .replace(/"/g, "&quot;");
  },

  showToast(msg) {
    const toast = document.getElementById("toast");
    toast.textContent = msg;
    toast.classList.remove("hidden");
    setTimeout(() => toast.classList.add("hidden"), 2500);
  },

  setupEventListeners() {
    document.getElementById("new-tab-btn").onclick = () => this.createNewTab(true);
    document.getElementById("home-btn").onclick = () => this.goHome();
    document.getElementById("back-btn").onclick = () => history.back();
    document.getElementById("forward-btn").onclick = () => history.forward();
    document.getElementById("reload-btn").onclick = () => {
      const frame = document.getElementById("web-frame");
      if (frame.src) frame.src = frame.src;
    };

    document.getElementById("go-btn").onclick = () => {
      this.navigate(document.getElementById("address-bar").value);
    };
    document.getElementById("address-bar").addEventListener("keydown", (e) => {
      if (e.key === "Enter") this.navigate(e.target.value);
    });

    document.getElementById("search-engine-select").onchange = (e) => {
      this.setSearchEngine(e.target.value);
    };

    document.getElementById("profile-select").onchange = (e) => {
      this.switchProfile(e.target.value);
    };

    document.getElementById("bookmarks-btn").onclick = () => this.openBookmarks();
    document.getElementById("tab-groups-btn").onclick = () => this.openTabGroups();
    document.getElementById("menu-btn").onclick = () => this.openMenu();
    document.getElementById("close-side").onclick = () => this.closeSidePanel();

    document.getElementById("close-modal").onclick = () => this.closeModal();
    document.getElementById("modal-overlay").addEventListener("click", (e) => {
      if (e.target.id === "modal-overlay") this.closeModal();
    });

    document.getElementById("add-widget-btn").onclick = () => this.openAddWidget();
    document.getElementById("customize-btn").onclick = () => {
      this.showToast("More customization coming soon");
    };

    document.getElementById("profile-btn").onclick = () => {
      this.openMenu();
    };
  }
};

document.addEventListener("DOMContentLoaded", () => {
  App.init();
  window.App = App;
});
