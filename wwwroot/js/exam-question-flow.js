(() => {
    const blocks = Array.from(document.querySelectorAll(".question-block"));
    const timer = document.getElementById("questionTimer");
    const progress = document.getElementById("questionProgress");
    const next = document.getElementById("nextQuestionBtn");
    const previous = document.getElementById("prevQuestionBtn");
    const lastNote = document.getElementById("lastQuestionNote");
    const expiredMessage = document.getElementById("expiredQuestionsNote");
    const examData = document.body.dataset;
    let clockStorage;
    try {
        clockStorage = examData.questionClockId ? window.localStorage : window.sessionStorage;
    } catch {
        // Current-page timing can still work without browser storage.
    }
    const clockKey = "jadeer-question-clock:" + JSON.stringify([
        examData.examId, examData.monitorUser, examData.questionClockId || ""
    ]);

    function loadClock() {
        try {
            return JSON.parse(clockStorage?.getItem(clockKey) || "null");
        } catch {
            return null;
        }
    }

    const clock = loadClock() || { remaining: {}, answers: {}, activeId: null, deadline: null };
    const questionId = (block) => block.dataset.questionId;
    let active = blocks.findIndex(block => questionId(block) === clock.activeId);
    let handle = null;

    function saveClock() {
        try {
            clockStorage?.setItem(clockKey, JSON.stringify(clock));
        } catch {
            // Keep enforcing expiration for the current page if browser storage is unavailable.
        }
    }

    function isExpired(index) {
        return clock.remaining[questionId(blocks[index])] === 0;
    }

    function findOpen(from, step) {
        for (let i = from + step; i >= 0 && i < blocks.length; i += step) {
            if (!isExpired(i)) return i;
        }
        return -1;
    }

    function restoreAnswer(block) {
        const qid = questionId(block);
        if (!Object.hasOwn(clock.answers, qid)) return;
        const answer = clock.answers[qid];
        const essay = block.querySelector("textarea");
        if (essay) {
            essay.value = answer;
        } else {
            block.querySelectorAll("input[type='radio']").forEach(input => {
                input.checked = input.value === answer;
            });
        }
    }

    function rememberAnswer(block) {
        const essay = block.querySelector("textarea");
        clock.answers[questionId(block)] = essay
            ? essay.value
            : block.querySelector("input[type='radio']:checked")?.value || "";
    }

    function questionMilliseconds(block) {
        const configured = Number.parseInt(block.dataset.durationSeconds || "", 10);
        if (configured > 0) return configured * 1000;
        const examMinutes = Number.parseInt(examData.examDurationMinutes || "", 10);
        return (examMinutes > 0 ? Math.max(30, Math.floor(examMinutes * 60 / blocks.length)) : 90) * 1000;
    }

    function showCountdown(milliseconds) {
        if (!timer) return;
        const seconds = Math.ceil(Math.max(0, milliseconds) / 1000);
        const minutes = String(Math.floor(seconds / 60)).padStart(2, "0");
        timer.textContent = (timer.dataset.timePrefix || "الوقت المتبقي") + ": "
            + minutes + ":" + String(seconds % 60).padStart(2, "0");
    }

    function expireActive() {
        const block = blocks[active];
        if (!block) return;
        const qid = questionId(block);
        rememberAnswer(block);
        clock.remaining[qid] = 0;
        clock.deadline = null;
        block.querySelectorAll("input,textarea").forEach(input => { input.disabled = true; });
        saveClock();
    }

    function updateNavigation() {
        const before = active >= 0 ? findOpen(active, -1) : -1;
        const after = active >= 0 ? findOpen(active, 1) : -1;
        if (previous) previous.disabled = before < 0;
        if (next) {
            next.disabled = after < 0;
            next.classList.toggle("is-disabled", after < 0);
        }
        lastNote?.classList.toggle("hidden", active < 0 || after >= 0);
        expiredMessage?.classList.toggle("hidden", active >= 0 || blocks.length === 0);
        if (progress) {
            const template = progress.dataset.template || "السؤال {0} من {1}";
            progress.textContent = active < 0 ? "" : template
                .replace("{0}", blocks[active].dataset.displayNumber || active + 1)
                .replace("{1}", blocks.length);
        }
    }

    function showQuestion(index, resume = false) {
        if (index >= 0 && (!blocks[index] || isExpired(index))) return;
        window.clearInterval(handle);
        handle = null;
        const previousDeadline = resume ? clock.deadline : null;
        active = index;
        clock.activeId = active >= 0 ? questionId(blocks[active]) : null;
        clock.deadline = null;
        blocks.forEach((block, i) => { block.style.display = i === active ? "" : "none"; });

        if (active >= 0) {
            clock.deadline = previousDeadline ?? (Date.now()
                + (clock.remaining[clock.activeId] ?? questionMilliseconds(blocks[active])));
            handle = window.setInterval(tick, 250);
            showCountdown(clock.deadline - Date.now());
        } else {
            showCountdown(0);
        }
        saveClock();
        updateNavigation();
    }

    function tick() {
        if (active < 0 || clock.deadline == null) return;
        const remaining = clock.deadline - Date.now();
        if (remaining > 0) {
            showCountdown(remaining);
            return;
        }
        expireActive();
        const after = findOpen(active, 1);
        showQuestion(after >= 0 ? after : findOpen(active, -1));
    }

    function navigate(step) {
        if (active < 0 || clock.deadline == null) return;
        const remaining = clock.deadline - Date.now();
        if (remaining <= 0) {
            tick();
            return;
        }
        const destination = findOpen(active, step);
        if (destination < 0) return;
        clock.remaining[clock.activeId] = remaining;
        rememberAnswer(blocks[active]);
        showQuestion(destination);
    }

    function startQuestionClock() {
        if (!blocks.length) return;
        if (active >= 0 && clock.deadline != null && clock.deadline <= Date.now()) expireActive();
        if (active >= 0 && !isExpired(active)) {
            showQuestion(active, true);
            return;
        }
        const after = findOpen(active, 1);
        showQuestion(after >= 0 || active < 0 ? after : findOpen(active, -1));
    }

    blocks.forEach((block, index) => {
        block.style.display = "none";
        if (Object.hasOwn(clock.answers, questionId(block))) restoreAnswer(block);
        else rememberAnswer(block);
        if (isExpired(index)) {
            block.querySelectorAll("input,textarea").forEach(input => { input.disabled = true; });
        }
        block.addEventListener("input", () => {
            if (index !== active || clock.deadline == null || clock.deadline <= Date.now()) {
                restoreAnswer(block);
                return;
            }
            rememberAnswer(block);
            saveClock();
        });
    });

    next?.addEventListener("click", () => navigate(1));
    previous?.addEventListener("click", () => navigate(-1));
    if (document.body.classList.contains("exam-camera-locked")) {
        document.addEventListener("jadeer-camera-ready", startQuestionClock, { once: true });
    } else {
        startQuestionClock();
    }

    window.clearExamQuestionClock = () => {
        window.clearInterval(handle);
        handle = null;
        clock.deadline = null;
        try { clockStorage?.removeItem(clockKey); } catch { /* Browser did not allow storage access. */ }
    };
})();
