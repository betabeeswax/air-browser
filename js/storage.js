// ===== AIR Browser Storage System =====
// Uses localStorage + IndexedDB-style structure via JSON

const Storage = {
  KEY: "air_browser_data_v1",

  defaults: {
    version: 1,
    currentProfileId: "default",
    profiles: {
      default: {
        id: "default",
        name: "Default",
        searchEngine: "bing",
        theme: "dark-blue",
        widgets: ["weather", "quick-links"],
        bookmarks: [],
        passwords: [],
        tabGroups: [],
        history: [],
        settings: {
          darkMode: true,
          homepage: "air"
        }
      }
    },
    themes: {
      "dark-blue": {
        name: "Dark Blue (Default)",
        colors: {
          bgMain: "#0a0c10",
          bgChrome: "#11151c",
          accent: "#3b82f6"
        }
      },
      "dark-purple": {
        name: "Dark Purple",
        colors: {
          bgMain: "#0f0a14",
          bgChrome: "#1a1224",
          accent: "#a855f7"
        }
      },
      "dark-green": {
        name: "Dark Green",
        colors: {
          bgMain: "#0a120e",
          bgChrome: "#111c16",
          accent: "#22c55e"
        }
      }
    },
    globalSettings: {
      lastSync: null,
      githubConnected: false
    }
  },

  load() {
    try {
      const raw = localStorage.getItem(this.KEY);
      if (!raw) {
        this.save(this.defaults);
        return structuredClone(this.defaults);
      }
      const data = JSON.parse(raw);
      return this._merge(this.defaults, data);
    } catch (e) {
      console.error("Storage load error:", e);
      return structuredClone(this.defaults);
    }
  },

  save(data) {
    try {
      localStorage.setItem(this.KEY, JSON.stringify(data));
      return true;
    } catch (e) {
      console.error("Storage save error:", e);
      return false;
    }
  },

  getCurrentProfile(data) {
    return data.profiles[data.currentProfileId] || data.profiles.default;
  },

  _merge(defaults, data) {
    const result = structuredClone(defaults);
    for (const key in data) {
      if (typeof data[key] === "object" && data[key] !== null && !Array.isArray(data[key])) {
        result[key] = this._merge(result[key] || {}, data[key]);
      } else {
        result[key] = data[key];
      }
    }
    return result;
  },

  exportData() {
    const data = this.load();
    const blob = new Blob([JSON.stringify(data, null, 2)], { type: "application/json" });
    const url = URL.createObjectURL(blob);
    const a = document.createElement("a");
    a.href = url;
    a.download = `air-browser-backup-${new Date().toISOString().slice(0, 10)}.json`;
    a.click();
    URL.revokeObjectURL(url);
  },

  importData(jsonString) {
    try {
      const data = JSON.parse(jsonString);
      if (!data.profiles) throw new Error("Invalid AIR Browser data");
      this.save(data);
      return true;
    } catch (e) {
      console.error("Import failed:", e);
      return false;
    }
  },

  createProfile(name, searchEngine = "bing") {
    const data = this.load();
    const id = "profile_" + Date.now();
    data.profiles[id] = {
      id,
      name,
      searchEngine,
      theme: "dark-blue",
      widgets: ["weather"],
      bookmarks: [],
      passwords: [],
      tabGroups: [],
      history: [],
      settings: {
        darkMode: true,
        homepage: "air"
      }
    };
    this.save(data);
    return id;
  },

  switchProfile(profileId) {
    const data = this.load();
    if (data.profiles[profileId]) {
      data.currentProfileId = profileId;
      this.save(data);
      return true;
    }
    return false;
  }
};

window.Storage = Storage;
