(() => {
    const STORAGE_KEY = "rc_theme_mode";
    const root = document.documentElement;

    function getInitialTheme() {
        const stored = localStorage.getItem(STORAGE_KEY);
        if (stored === "light" || stored === "dark") {
            return stored;
        }
        return window.matchMedia && window.matchMedia("(prefers-color-scheme: dark)").matches
            ? "dark"
            : "light";
    }

    function labelFor(theme) {
        const t = window.__RC_THEME;
        const dark = t?.dark;
        const light = t?.light;
        const valid = (s) => typeof s === "string" && s.length > 0 && !s.startsWith("??");
        if (valid(dark) && valid(light)) {
            return theme === "dark" ? light : dark;
        }
        const isEn = document.documentElement.lang === "en";
        if (isEn) {
            return theme === "dark" ? "Light mode" : "Dark mode";
        }
        return theme === "dark" ? "الوضع الفاتح" : "الوضع الداكن";
    }

    function iconFor(theme) {
        return theme === "dark" ? "☀" : "🌙";
    }

    function applyTheme(theme) {
        root.setAttribute("data-theme", theme);
        localStorage.setItem(STORAGE_KEY, theme);
        document.querySelectorAll("[data-theme-trigger], .theme-toggle-slot, .theme-toggle-floating").forEach((btn) => {
            const label = labelFor(theme);
            const icon = iconFor(theme);
            const loginIcon = btn.querySelector(".login-theme-toggle-icon");
            const loginText = btn.querySelector(".login-theme-toggle-text");
            if (loginIcon && loginText) {
                loginText.textContent = label;
                btn.setAttribute("aria-label", label);
                return;
            }
            const themeLabel = btn.querySelector(".theme-toggle-label");
            const themeIcon = btn.querySelector(".theme-toggle-icon");
            if (themeLabel && themeIcon) {
                themeIcon.textContent = icon;
                themeLabel.textContent = label;
                btn.setAttribute("aria-label", label);
                return;
            }
            if (btn.classList.contains("sidebar-theme-btn") && !btn.querySelector(".nav-label")) {
                btn.textContent = icon + " " + label;
            } else if (btn.classList.contains("login-theme-floating")) {
                btn.textContent = icon;
            } else {
                btn.textContent = icon + " " + label;
            }
            btn.setAttribute("aria-label", label);
        });
    }

    function bindToggle(el) {
        if (!el || el.dataset.themeBound === "1") {
            return;
        }
        el.dataset.themeBound = "1";
        el.addEventListener("click", () => {
            const next = root.getAttribute("data-theme") === "dark" ? "light" : "dark";
            applyTheme(next);
        });
    }

    function injectFloatingIfNeeded() {
        if (document.querySelector(".app-sidebar") || document.querySelector(".theme-toggle-floating")) {
            return;
        }
        if (!document.body || document.body.classList.contains("auth-body")) {
            const btn = document.createElement("button");
            btn.type = "button";
            btn.className = "theme-toggle-floating";
            btn.setAttribute("data-theme-trigger", "");
            document.body.appendChild(btn);
            bindToggle(btn);
        }
    }

    applyTheme(getInitialTheme());

    document.addEventListener("DOMContentLoaded", () => {
        document.querySelectorAll("[data-theme-trigger]").forEach(bindToggle);
        injectFloatingIfNeeded();
        applyTheme(root.getAttribute("data-theme") || "light");
    });
})();
