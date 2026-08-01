const questionBlocks = Array.from(document.querySelectorAll(".question-block"));
const timerLabel = document.getElementById("questionTimer");
const progressLabel = document.getElementById("questionProgress");
const nextBtn = document.getElementById("nextQuestionBtn");
const prevBtn = document.getElementById("prevQuestionBtn");
const lastQuestionNote = document.getElementById("lastQuestionNote");

let activeQuestionIndex = 0;
let timerHandle = null;
const remainingByQuestion = new Map();
const expiredQuestions = new Set();
const totalQuestions = questionBlocks.length;

function formatSeconds(sec) {
    const total = Math.max(0, Number(sec) || 0);
    const m = Math.floor(total / 60);
    const s = total % 60;
    return `${String(m).padStart(2, "0")}:${String(s).padStart(2, "0")}`;
}

function formatProgress(displayNum, total) {
    const tpl = progressLabel?.dataset.template || "السؤال {0} من {1}";
    return tpl.replace("{0}", displayNum).replace("{1}", total);
}

function lockQuestion(block) {
    if (!block) {
        return;
    }
    block.querySelectorAll("input,textarea").forEach((el) => {
        el.setAttribute("disabled", "disabled");
    });
}

function updateButtons() {
    if (prevBtn) {
        prevBtn.disabled = activeQuestionIndex <= 0;
    }
    const isLast = activeQuestionIndex >= totalQuestions - 1;
    if (nextBtn) {
        nextBtn.disabled = isLast;
        nextBtn.classList.toggle("is-disabled", isLast);
    }
    if (lastQuestionNote) {
        lastQuestionNote.classList.toggle("hidden", !isLast);
    }
    if (progressLabel && totalQuestions > 0) {
        const displayNum = Number(questionBlocks[activeQuestionIndex]?.dataset.displayNumber) || activeQuestionIndex + 1;
        progressLabel.textContent = formatProgress(displayNum, totalQuestions);
    }
}

function resolveQuestionDurationSeconds(block) {
    const durationRaw = Number.parseInt(String(block?.dataset.durationSeconds || ""), 10);
    if (Number.isFinite(durationRaw) && durationRaw > 0) {
        return durationRaw;
    }
    const examMinutes = Number.parseInt(String(document.body.dataset.examDurationMinutes || ""), 10);
    if (Number.isFinite(examMinutes) && examMinutes > 0 && totalQuestions > 0) {
        return Math.max(30, Math.floor((examMinutes * 60) / totalQuestions));
    }
    return 90;
}

function startQuestionTimer(block) {
    if (timerHandle) {
        clearInterval(timerHandle);
        timerHandle = null;
    }
    if (!block || !timerLabel) {
        return;
    }
    const qid = String(block.dataset.questionId || "");
    const durationSeconds = resolveQuestionDurationSeconds(block);
    const prefix = timerLabel.dataset.timePrefix || "الوقت المتبقي";
    if (!remainingByQuestion.has(qid)) {
        remainingByQuestion.set(qid, durationSeconds);
    }
    const tick = () => {
        const current = remainingByQuestion.get(qid) || 0;
        timerLabel.textContent = `${prefix}: ${formatSeconds(current)}`;
    };
    tick();
    timerHandle = window.setInterval(() => {
        const current = (remainingByQuestion.get(qid) || 0) - 1;
        remainingByQuestion.set(qid, current);
        tick();
        if (current <= 0) {
            clearInterval(timerHandle);
            timerHandle = null;
            expiredQuestions.add(qid);
            lockQuestion(block);
            if (activeQuestionIndex < totalQuestions - 1) {
                showQuestion(activeQuestionIndex + 1);
            }
        }
    }, 1000);
}

function showQuestion(index) {
    if (!questionBlocks.length) {
        return;
    }
    activeQuestionIndex = Math.max(0, Math.min(index, totalQuestions - 1));
    questionBlocks.forEach((block, i) => {
        block.style.display = i === activeQuestionIndex ? "" : "none";
    });
    const active = questionBlocks[activeQuestionIndex];
    const qid = String(active.dataset.questionId || "");
    if (expiredQuestions.has(qid)) {
        lockQuestion(active);
    }
    startQuestionTimer(active);
    updateButtons();
}

if (questionBlocks.length > 0) {
    showQuestion(0);
}

nextBtn?.addEventListener("click", () => {
    if (activeQuestionIndex < totalQuestions - 1) {
        showQuestion(activeQuestionIndex + 1);
    }
});
prevBtn?.addEventListener("click", () => showQuestion(activeQuestionIndex - 1));
