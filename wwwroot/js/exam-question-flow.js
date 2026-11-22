(() => {
    const blocks = Array.from(document.querySelectorAll(".question-block"));
    const timer = document.getElementById("questionTimer");
    const progress = document.getElementById("questionProgress");
    const next = document.getElementById("nextQuestionBtn");
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
    // Legacy exams may shuffle their markup on refresh. Keep this attempt's first order.
    if (Array.isArray(clock.order)) {
        const position = new Map(clock.order.map((id, index) => [id, index]));
        blocks.sort((a, b) => (position.get(questionId(a)) ?? blocks.length)
            - (position.get(questionId(b)) ?? blocks.length));
    }
    clock.order = blocks.map(questionId);
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

    function findNext(from) {
        for (let i = from + 1; i < blocks.length; i++) {
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

    function closeActive() {
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
        const after = active >= 0 ? findNext(active) : -1;
        if (next) {
            next.disabled = after < 0;
            next.classList.toggle("is-disabled", after < 0);
        }
        lastNote?.classList.toggle("hidden", active < 0 || after >= 0);
        expiredMessage?.classList.toggle("hidden", active >= 0 || blocks.length === 0);
        if (progress) {
            const template = progress.dataset.template || "السؤال {0} من {1}";
            progress.textContent = active < 0 ? "" : template
                .replace("{0}", active + 1)
                .replace("{1}", blocks.length);
        }
    }

    function showQuestion(index, resume = false) {
        if (index >= 0 && (!blocks[index] || isExpired(index))) return;
        window.clearInterval(handle);
        handle = null;
        const previousDeadline = resume ? clock.deadline : null;
        active = index;
        clock.completed = index < 0;
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
        closeActive();
        showQuestion(findNext(active));
    }

    function advance() {
        if (active < 0 || clock.deadline == null) return;
        const remaining = clock.deadline - Date.now();
        if (remaining <= 0) {
            tick();
            return;
        }
        const destination = findNext(active);
        if (destination < 0) return;
        closeActive();
        showQuestion(destination);
    }

    function startQuestionClock() {
        if (!blocks.length) return;
        if (clock.completed) {
            showQuestion(-1);
            return;
        }
        if (active >= 0 && clock.deadline != null && clock.deadline <= Date.now()) closeActive();
        if (active >= 0 && !isExpired(active)) {
            showQuestion(active, true);
            return;
        }
        showQuestion(findNext(active));
    }

    blocks.forEach((block, index) => {
        block.style.display = "none";
        if (index < active || clock.completed) clock.remaining[questionId(block)] = 0;
        const number = block.querySelector(".question-num");
        if (number && block.dataset.displayNumber !== String(index + 1))
            number.textContent = number.textContent.replace(/\d+$/, String(index + 1));
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

    next?.addEventListener("click", advance);
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
