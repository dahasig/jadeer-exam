(() => {
    function parseEventTime(text) {
        if (!text) {
            return null;
        }
        const norm = text.trim().replace(" ", "T");
        const d = new Date(norm);
        return Number.isNaN(d.getTime()) ? null : d;
    }

    function buildHeatmap(times) {
        const slots = Array.from({ length: 24 }, (_, h) => ({ hour: h, count: 0 }));
        for (const t of times) {
            slots[t.getHours()].count += 1;
        }
        const max = Math.max(1, ...slots.map((s) => s.count));
        const wrap = document.createElement("section");
        wrap.className = "card panel";
        const cfg = window.__RC_REPORTS || {};
        const title = cfg.heatmapTitle || "خريطة كثافة التنبيهات (24 ساعة)";
        const cellTpl = cfg.heatmapCell || "{0}:00 — {1} تنبيه";
        wrap.innerHTML = `<h3>${title}</h3><div class="event-heatmap"></div>`;
        const hm = wrap.querySelector(".event-heatmap");
        for (const s of slots) {
            const cell = document.createElement("div");
            cell.className = "event-heatmap-cell";
            const ratio = s.count / max;
            cell.style.height = `${20 + ratio * 52}px`;
            cell.style.opacity = `${0.2 + ratio * 0.8}`;
            const hour = String(s.hour).padStart(2, "0");
            cell.title = cellTpl.replace("{0}", hour).replace("{1}", String(s.count));
            cell.innerHTML = `<span>${s.count}</span><small>${String(s.hour).padStart(2, "0")}</small>`;
            hm.appendChild(cell);
        }
        return wrap;
    }

    function initMonitoringHeatmap() {
        const rows = Array.from(document.querySelectorAll(".event-time"));
        if (!rows.length) {
            return;
        }
        const times = rows.map((n) => parseEventTime(n.textContent)).filter(Boolean);
        if (!times.length) {
            return;
        }
        const grid = document.querySelector(".page-content") || document.querySelector(".app-main");
        if (!grid) {
            return;
        }
        const heatmap = buildHeatmap(times);
        const firstPanel = grid.querySelector(".panel");
        if (firstPanel) {
            firstPanel.insertAdjacentElement("afterend", heatmap);
        } else {
            grid.prepend(heatmap);
        }
    }

    document.addEventListener("DOMContentLoaded", initMonitoringHeatmap);
})();

