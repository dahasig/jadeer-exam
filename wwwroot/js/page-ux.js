(() => {
    function normalizeTabLabel(text) {
        return (text || "").replace(/\s+/g, " ").trim();
    }

    /** Hide panel titles that repeat the active tab label (exact or "tab — details"). */
    function hideDuplicateTabPanelTitles(root) {
        const bar = root.querySelector("[data-tab-bar]");
        if (!bar) {
            return;
        }
        const tabByPanelId = new Map();
        bar.querySelectorAll("[data-tab-target]").forEach((btn) => {
            tabByPanelId.set(btn.getAttribute("data-tab-target"), normalizeTabLabel(btn.textContent));
        });
        root.querySelectorAll("[data-tab-panel]").forEach((panel) => {
            const tabLabel = tabByPanelId.get(panel.getAttribute("data-tab-panel"));
            if (!tabLabel) {
                return;
            }
            panel.querySelectorAll(
                ":scope > .panel-heading, :scope > .sub-heading, :scope > h2, :scope > h3"
            ).forEach((heading) => {
                const title = normalizeTabLabel(heading.textContent);
                if (!title) {
                    return;
                }
                const duplicate =
                    title === tabLabel
                    || title.startsWith(tabLabel + " ")
                    || title.startsWith(tabLabel + "—")
                    || title.startsWith(tabLabel + "-")
                    || title.startsWith(tabLabel + ":");
                if (duplicate) {
                    heading.remove();
                }
            });
        });
    }

    function initTabs(root) {
        if (root.hasAttribute("data-ops-wizard") || root.hasAttribute("data-reports-tabs")) {
            return;
        }
        const bar = root.querySelector("[data-tab-bar]");
        if (!bar) {
            return;
        }
        const buttons = Array.from(bar.querySelectorAll("[data-tab-target]"));
        const panels = Array.from(root.querySelectorAll("[data-tab-panel]"));
        if (!buttons.length || !panels.length) {
            return;
        }

        hideDuplicateTabPanelTitles(root);

        function activate(id, persist) {
            const target = buttons.find((b) => b.getAttribute("data-tab-target") === id);
            if (!target) {
                return;
            }
            buttons.forEach((btn) => {
                const on = btn.getAttribute("data-tab-target") === id;
                btn.classList.toggle("is-active", on);
                btn.setAttribute("aria-selected", on ? "true" : "false");
            });
            panels.forEach((panel) => {
                const on = panel.getAttribute("data-tab-panel") === id;
                panel.classList.toggle("is-active", on);
                panel.hidden = !on;
            });
            if (persist !== false) {
                try {
                    localStorage.setItem(root.getAttribute("data-tab-store") || "rc_tab", id);
                } catch (_) { /* ignore */ }
            }
        }

        const fromUrl = new URLSearchParams(location.search).get("tab")
            || (location.hash ? location.hash.replace("#", "") : null);
        const preset = root.getAttribute("data-default-tab");
        const storedKey = root.getAttribute("data-tab-store");
        let initialId = fromUrl;
        if (!initialId) {
            if (preset) {
                initialId = preset;
            } else if (storedKey) {
                try {
                    const saved = localStorage.getItem(storedKey);
                    if (saved && buttons.some((b) => b.getAttribute("data-tab-target") === saved)) {
                        initialId = saved;
                    }
                } catch (_) { /* ignore */ }
            }
        }
        if (!initialId || !buttons.some((b) => b.getAttribute("data-tab-target") === initialId)) {
            const marked = buttons.find((b) => b.classList.contains("is-active"));
            initialId = marked ? marked.getAttribute("data-tab-target") : buttons[0].getAttribute("data-tab-target");
        }
        activate(initialId, !fromUrl);

        bar.addEventListener("click", (e) => {
            const btn = e.target.closest("[data-tab-target]");
            if (!btn || !bar.contains(btn)) {
                return;
            }
            e.preventDefault();
            activate(btn.getAttribute("data-tab-target"));
        });
    }

    document.addEventListener("DOMContentLoaded", () => {
        document.querySelectorAll("[data-ux-tabs]").forEach(initTabs);
    });
})();
