(function initExamCalculator() {
    const toggleBtn = document.getElementById("examCalcToggleBtn");
    const panel = document.getElementById("examCalcPanel");
    if (!toggleBtn || !panel) {
        return;
    }

    const MSG = window.__RC_EXAM_CALC || {};
    const displayExpr = panel.querySelector("#examCalcExpr");
    const displayResult = panel.querySelector("#examCalcResult");
    const angleModeBtn = panel.querySelector("#examCalcAngleMode");
    const closeBtn = panel.querySelector("#examCalcClose");
    const dragHandle = panel.querySelector(".exam-calc-drag-handle");

    let expression = "";
    let lastResult = "0";
    let angleDegrees = true;

    function updateAngleLabel() {
        if (!angleModeBtn) {
            return;
        }
        angleModeBtn.textContent = angleDegrees
            ? (MSG.deg || "DEG")
            : (MSG.rad || "RAD");
        angleModeBtn.setAttribute("aria-pressed", angleDegrees ? "true" : "false");
    }

    function render() {
        if (displayExpr) {
            displayExpr.textContent = expression || "0";
        }
        if (displayResult) {
            displayResult.textContent = lastResult;
        }
    }

    function appendToken(token) {
        expression += token;
        render();
    }

    function clearAll() {
        expression = "";
        render();
    }

    function clearEntry() {
        expression = "";
        render();
    }

    function backspace() {
        expression = expression.slice(0, -1);
        render();
    }

    function toggleSign() {
        if (!expression) {
            expression = "-";
            render();
            return;
        }
        if (expression.startsWith("-")) {
            expression = expression.slice(1);
        } else {
            expression = "-" + expression;
        }
        render();
    }

    function normalizeExpression(raw) {
        let s = String(raw || "").trim();
        if (!s) {
            return "";
        }
        s = s.replace(/÷/g, "/").replace(/×/g, "*").replace(/−/g, "-");
        s = s.replace(/π/g, "PI");
        s = s.replace(/\^/g, "**");
        s = s.replace(/(\d)([a-z(])/gi, "$1*$2");
        s = s.replace(/\)(\d)/g, ")*$1");
        s = s.replace(/\)([a-z(])/gi, ")*$1");
        return s;
    }

    function evaluateExpression(raw) {
        const s = normalizeExpression(raw);
        if (!s) {
            return 0;
        }
        if (!/^[0-9+\-*/().,\s*a-zA-Z]+$/.test(s)) {
            throw new Error("invalid");
        }
        const trigIn = angleDegrees ? "x*Math.PI/180" : "x";
        const trigOutMul = angleDegrees ? "*180/Math.PI" : "";
        const body = `
            const PI = Math.PI;
            const E = Math.E;
            const sin = (x) => Math.sin(${trigIn});
            const cos = (x) => Math.cos(${trigIn});
            const tan = (x) => Math.tan(${trigIn});
            const asin = (x) => Math.asin(x)${trigOutMul};
            const acos = (x) => Math.acos(x)${trigOutMul};
            const atan = (x) => Math.atan(x)${trigOutMul};
            const log = (x) => Math.log10(x);
            const ln = (x) => Math.log(x);
            const sqrt = (x) => Math.sqrt(x);
            const abs = (x) => Math.abs(x);
            const pow = (a, b) => Math.pow(a, b);
            return (${s});
        `;
        const value = Function(body)();
        if (!Number.isFinite(value)) {
            throw new Error("invalid");
        }
        return value;
    }

    function formatResult(value) {
        const rounded = Math.round(value * 1e10) / 1e10;
        return String(rounded);
    }

    function calculate() {
        try {
            const value = evaluateExpression(expression);
            lastResult = formatResult(value);
            expression = lastResult;
            render();
        } catch (err) {
            lastResult = MSG.error || "Error";
            render();
        }
    }

    function insertFunction(name) {
        expression += `${name}(`;
        render();
    }

    panel.addEventListener("click", (e) => {
        const btn = e.target.closest("[data-calc]");
        if (!btn || btn.disabled) {
            return;
        }
        e.preventDefault();
        e.stopPropagation();
        const action = btn.dataset.calc;
        switch (action) {
            case "0": case "1": case "2": case "3": case "4":
            case "5": case "6": case "7": case "8": case "9":
            case ".": case "+": case "-": case "*": case "/":
            case "(": case ")":
                appendToken(action);
                break;
            case "pi":
                appendToken("π");
                break;
            case "e":
                appendToken("e");
                break;
            case "pow":
                appendToken("^");
                break;
            case "sq":
                appendToken("^2");
                break;
            case "inv":
                appendToken("1/(");
                break;
            case "sqrt":
                insertFunction("sqrt");
                break;
            case "sin":
                insertFunction("sin");
                break;
            case "cos":
                insertFunction("cos");
                break;
            case "tan":
                insertFunction("tan");
                break;
            case "asin":
                insertFunction("asin");
                break;
            case "acos":
                insertFunction("acos");
                break;
            case "atan":
                insertFunction("atan");
                break;
            case "log":
                insertFunction("log");
                break;
            case "ln":
                insertFunction("ln");
                break;
            case "ans":
                appendToken(lastResult);
                break;
            case "clear":
                clearAll();
                break;
            case "clear-entry":
                clearEntry();
                break;
            case "backspace":
                backspace();
                break;
            case "sign":
                toggleSign();
                break;
            case "equals":
                calculate();
                break;
            case "angle":
                angleDegrees = !angleDegrees;
                updateAngleLabel();
                break;
            default:
                break;
        }
    });

    function setOpen(open) {
        panel.classList.toggle("hidden", !open);
        panel.setAttribute("aria-hidden", open ? "false" : "true");
        toggleBtn.setAttribute("aria-expanded", open ? "true" : "false");
    }

    toggleBtn.addEventListener("click", () => {
        setOpen(panel.classList.contains("hidden"));
    });

    closeBtn?.addEventListener("click", () => setOpen(false));

    document.addEventListener("keydown", (e) => {
        if (e.key === "Escape" && !panel.classList.contains("hidden")) {
            setOpen(false);
        }
    });

    let dragActive = false;
    let dragOffsetX = 0;
    let dragOffsetY = 0;

    function onPointerMove(e) {
        if (!dragActive) {
            return;
        }
        const maxX = Math.max(8, window.innerWidth - panel.offsetWidth - 8);
        const maxY = Math.max(8, window.innerHeight - panel.offsetHeight - 8);
        const x = Math.min(maxX, Math.max(8, e.clientX - dragOffsetX));
        const y = Math.min(maxY, Math.max(8, e.clientY - dragOffsetY));
        panel.style.left = `${x}px`;
        panel.style.top = `${y}px`;
        panel.style.right = "auto";
        panel.style.bottom = "auto";
    }

    function stopDrag() {
        dragActive = false;
        document.removeEventListener("pointermove", onPointerMove);
        document.removeEventListener("pointerup", stopDrag);
        panel.classList.remove("is-dragging");
    }

    dragHandle?.addEventListener("pointerdown", (e) => {
        if (e.button !== 0) {
            return;
        }
        dragActive = true;
        const rect = panel.getBoundingClientRect();
        dragOffsetX = e.clientX - rect.left;
        dragOffsetY = e.clientY - rect.top;
        panel.classList.add("is-dragging");
        document.addEventListener("pointermove", onPointerMove);
        document.addEventListener("pointerup", stopDrag);
        e.preventDefault();
    });

    updateAngleLabel();
    render();
})();
