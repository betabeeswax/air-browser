// ===== AIR Browser Widgets =====

const Widgets = {
  types: {
    weather: {
      name: "Weather",
      icon: "W",
      description: "Current weather for your location"
    },
    "quick-links": {
      name: "Quick Links",
      icon: "L",
      description: "Your favorite sites"
    },
    clock: {
      name: "Clock",
      icon: "C",
      description: "Current time"
    },
    notes: {
      name: "Quick Notes",
      icon: "N",
      description: "Simple notes"
    }
  },

  render(type, container, data = {}) {
    const widget = document.createElement("div");
    widget.className = "widget";
    widget.dataset.type = type;

    const header = document.createElement("div");
    header.className = "widget-header";
    header.innerHTML = `
      <span class="widget-title">${this.types[type]?.icon || ""} ${this.types[type]?.name || type}</span>
      <button class="widget-remove" title="Remove widget">x</button>
    `;
    widget.appendChild(header);

    const body = document.createElement("div");
    body.className = "widget-body";
    widget.appendChild(body);

    switch (type) {
      case "weather":
        this._renderWeather(body);
        break;
      case "quick-links":
        this._renderQuickLinks(body, data);
        break;
      case "clock":
        this._renderClock(body);
        break;
      case "notes":
        this._renderNotes(body);
        break;
      default:
        body.innerHTML = `<p style="color:var(--text-dim)">Unknown widget</p>`;
    }

    header.querySelector(".widget-remove").addEventListener("click", () => {
      if (window.App && window.App.removeWidget) {
        window.App.removeWidget(type);
      }
      widget.remove();
    });

    container.appendChild(widget);
    return widget;
  },

  async _renderWeather(body) {
    body.innerHTML = `<p style="color:var(--text-dim)">Loading weather...</p>`;

    try {
      let lat = 43.65;
      let lon = -80.5;

      if (navigator.geolocation) {
        const pos = await new Promise((resolve, reject) => {
          navigator.geolocation.getCurrentPosition(resolve, reject, { timeout: 5000 });
        }).catch(() => null);

        if (pos) {
          lat = pos.coords.latitude;
          lon = pos.coords.longitude;
        }
      }

      const url = `https://api.open-meteo.com/v1/forecast?latitude=${lat}&longitude=${lon}&current=temperature_2m,relative_humidity_2m,weather_code,wind_speed_10m&timezone=auto`;
      const res = await fetch(url);
      const data = await res.json();

      if (!data.current) throw new Error("No weather data");

      const temp = Math.round(data.current.temperature_2m);
      const humidity = data.current.relative_humidity_2m;
      const wind = Math.round(data.current.wind_speed_10m);
      const code = data.current.weather_code;

      const weatherMap = {
        0: "Clear sky",
        1: "Mainly clear",
        2: "Partly cloudy",
        3: "Overcast",
        45: "Foggy",
        48: "Foggy",
        51: "Light drizzle",
        61: "Light rain",
        63: "Rain",
        65: "Heavy rain",
        71: "Light snow",
        73: "Snow",
        80: "Rain showers",
        95: "Thunderstorm"
      };

      const desc = weatherMap[code] || "Unknown";

      body.innerHTML = `
        <div class="weather-temp">${temp} C</div>
        <div class="weather-desc">${desc}</div>
        <div class="weather-details">
          <span>Humidity ${humidity}%</span>
          <span>Wind ${wind} km/h</span>
        </div>
      `;
    } catch (e) {
      body.innerHTML = `
        <div class="weather-temp">-- C</div>
        <div class="weather-desc">Could not load weather</div>
        <p style="font-size:12px;color:var(--text-dim);margin-top:8px;">Allow location or try again later</p>
      `;
    }
  },

  _renderQuickLinks(body, data) {
    const links = data.links || [
      { name: "Bing", url: "https://www.bing.com" },
      { name: "Google", url: "https://www.google.com" },
      { name: "GitHub", url: "https://github.com" },
      { name: "YouTube", url: "https://www.youtube.com" }
    ];

    body.innerHTML = links.map(l => `
      <div class="list-item" data-url="${l.url}">
        <span class="icon">*</span>
        <div class="info">
          <div class="title">${l.name}</div>
        </div>
      </div>
    `).join("");

    body.querySelectorAll(".list-item").forEach(el => {
      el.addEventListener("click", () => {
        if (window.App) window.App.navigate(el.dataset.url);
      });
    });
  },

  _renderClock(body) {
    const update = () => {
      const now = new Date();
      body.innerHTML = `
        <div style="font-size:2rem;font-weight:700;color:var(--accent)">
          ${now.toLocaleTimeString([], { hour: "2-digit", minute: "2-digit" })}
        </div>
        <div style="color:var(--text-dim);margin-top:4px">
          ${now.toLocaleDateString([], { weekday: "long", month: "short", day: "numeric" })}
        </div>
      `;
    };
    update();
    setInterval(update, 1000);
  },

  _renderNotes(body) {
    const key = "air_widget_notes";
    const saved = localStorage.getItem(key) || "";
    body.innerHTML = `
      <textarea id="notes-area" style="width:100%;height:100px;background:var(--bg-main);border:1px solid var(--border);color:var(--text);border-radius:8px;padding:10px;resize:vertical;font-size:14px;outline:none;" placeholder="Write a quick note...">${saved}</textarea>
    `;
    const area = body.querySelector("#notes-area");
    area.addEventListener("input", () => {
      localStorage.setItem(key, area.value);
    });
  }
};

window.Widgets = Widgets;
