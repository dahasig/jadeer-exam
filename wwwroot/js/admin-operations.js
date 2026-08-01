const examCreateForm = document.getElementById("examCreateForm");
const examTitle = document.getElementById("examTitle");
const totalMarksInput = document.getElementById("totalMarks");
const passThresholdInput = document.getElementById("passThreshold");
const practicalEnabledInput = document.getElementById("practicalEnabled");
const practicalMaxMarksInput = document.getElementById("practicalMaxMarks");
const questionExamId = document.getElementById("questionExamId");
const mcqExamIdHidden = document.getElementById("mcqExamIdHidden");
const essayExamIdHidden = document.getElementById("essayExamIdHidden");
const importQuestionsExamIdHidden = document.getElementById("importQuestionsExamIdHidden");
const assignCandidateExamId = document.getElementById("assignCandidateExamId");
const importCandidatesExamId = document.getElementById("importCandidatesExamId");
const MIN_QUESTION_DURATION_SECONDS = 10;
const questionSuccessMsg = document.getElementById("questionSuccessMsg");
const questionBudgetError = document.getElementById("questionBudgetError");
const previewExamBtn = document.getElementById("previewExamBtn");
const publishExamBtn = document.getElementById("publishExamBtn");
const publishExamForm = document.getElementById("publishExamForm");
const addMcqBtn = document.getElementById("addMcqBtn");
const addEssayBtn = document.getElementById("addEssayBtn");
const examCreateBtn = document.getElementById("examCreateBtn");
const goToCandidatesBtn = document.getElementById("goToCandidatesBtn");
const goToCandidatesBtnDisabled = document.getElementById("goToCandidatesBtnDisabled");
const MAX_QUESTIONS = 40;

const BUDGET_MSG = window.__RC_OPS_BUDGET || {};
const WIZARD_MSG = window.__RC_OPS_WIZARD || {};
const examDepartmentId = document.getElementById("examDepartmentId");
const examCreateError = document.getElementById("examCreateError");
const isAdminUser = document.body.dataset.isAdmin === "true";

function normalizeDurationPicker(wrap) {
    const valueEl = wrap?.querySelector(".duration-picker-value");
    const unitEl = wrap?.querySelector(".duration-picker-unit");
    const amount = Number(valueEl?.value);
    if (!amount || amount < 1) {
        return null;
    }
    const unit = unitEl?.value || "minutes";
    const storage = wrap?.dataset.storage || "seconds";
    if (storage === "minutes") {
        return unit === "hours" ? amount * 60 : amount;
    }
    return unit === "hours" ? amount * 3600 : amount * 60;
}

function applyDurationPicker(wrap) {
    const hidden = wrap?.querySelector(".duration-picker-hidden");
    if (!hidden) {
        return null;
    }
    const storage = wrap?.dataset.storage || "seconds";
    const raw = normalizeDurationPicker(wrap);
    if (raw == null) {
        hidden.value = "";
        return null;
    }
    if (storage === "seconds" && raw < MIN_QUESTION_DURATION_SECONDS) {
        hidden.value = "";
        return null;
    }
    hidden.value = String(raw);
    return raw;
}

function initDurationPickers(root) {
    (root || document).querySelectorAll(".duration-picker-wrap").forEach((wrap) => {
        wrap.querySelectorAll(".duration-picker-value, .duration-picker-unit").forEach((el) => {
            el.addEventListener("change", () => applyDurationPicker(wrap));
            el.addEventListener("input", () => applyDurationPicker(wrap));
        });
        applyDurationPicker(wrap);
    });
}

function getExamDurationWrap() {
    return examCreateForm?.querySelector(".duration-picker-wrap");
}

function examDurationReady() {
    return normalizeDurationPicker(getExamDurationWrap()) != null;
}

function syncPracticalFields() {
    const enabled = Boolean(practicalEnabledInput?.checked);
    practicalMaxMarksInput?.classList.toggle("hidden", !enabled);
    if (practicalMaxMarksInput) {
        practicalMaxMarksInput.required = enabled;
        if (!enabled) {
            practicalMaxMarksInput.value = "";
        }
    }
    const total = Number(totalMarksInput?.value);
    if (enabled && total > 1 && practicalMaxMarksInput) {
        practicalMaxMarksInput.max = String(total - 1);
    }
}

function validateExamCreateForm() {
    const errors = [];
    if (!examTitle?.value.trim()) {
        errors.push(WIZARD_MSG.errorTitle || "أدخل اسم الاختبار.");
    }
    if (!examDurationReady()) {
        errors.push(WIZARD_MSG.errorDuration || "حدّد مدة الاختبار.");
    }
    if (isAdminUser && !examDepartmentId?.value) {
        errors.push(WIZARD_MSG.errorDepartment || "اختر الإدارة.");
    }
    const total = Number(totalMarksInput?.value);
    const pass = Number(passThresholdInput?.value);
    if (!total || total < 1) {
        errors.push(WIZARD_MSG.errorMarks || "أدخل درجة الاختبار الكلية.");
    }
    if (!pass || pass < 1 || (total > 0 && pass > total)) {
        errors.push(WIZARD_MSG.errorPass || "درجة النجاح غير صالحة.");
    }
    if (practicalEnabledInput?.checked) {
        const practical = Number(practicalMaxMarksInput?.value);
        if (!practical || practical < 1 || (total > 0 && practical >= total)) {
            errors.push(WIZARD_MSG.errorPractical || "درجة الاختبار العملي غير صالحة.");
        }
    }
    return errors;
}

function updateExamCreateBtnState() {
    if (!examCreateBtn) {
        return;
    }
    examCreateBtn.disabled = validateExamCreateForm().length > 0;
}

function updateGoToCandidatesBtnState(qcount) {
    const ready = qcount >= 1;
    if (goToCandidatesBtn) {
        goToCandidatesBtn.classList.toggle("hidden", !ready);
    }
    if (goToCandidatesBtnDisabled) {
        goToCandidatesBtnDisabled.classList.toggle("hidden", ready);
    }
}

function showExamCreateError(errors) {
    if (!examCreateError) {
        return;
    }
    if (errors?.length) {
        examCreateError.textContent = errors.join(" ");
        examCreateError.classList.remove("hidden");
    } else {
        examCreateError.classList.add("hidden");
    }
}

function syncExamDurationHidden() {
    applyDurationPicker(getExamDurationWrap());
}

function getQueryParam(name) {
    return new URLSearchParams(window.location.search).get(name);
}

function resolveExamId() {
    return getQueryParam("examId")
        || document.body.dataset.selectedExamId
        || questionExamId?.value
        || "";
}

function syncExamIdToForms() {
    const id = resolveExamId();
    if (questionExamId && id && !questionExamId.value) {
        questionExamId.value = id;
    }
    const activeId = questionExamId?.value || id;
    if (mcqExamIdHidden) {
        mcqExamIdHidden.value = activeId;
    }
    if (essayExamIdHidden) {
        essayExamIdHidden.value = activeId;
    }
    if (importQuestionsExamIdHidden) {
        importQuestionsExamIdHidden.value = activeId;
    }
    if (assignCandidateExamId && activeId) {
        assignCandidateExamId.value = activeId;
    }
    if (importCandidatesExamId && activeId) {
        importCandidatesExamId.value = activeId;
    }
}

function formatMinutes(seconds) {
    const m = Math.floor(seconds / 60);
    const h = Math.floor(m / 60);
    const rm = m % 60;
    if (h > 0 && rm > 0) {
        return `${h} ${BUDGET_MSG.hour || "س"} ${rm} ${BUDGET_MSG.minute || "د"}`;
    }
    if (h > 0) {
        return `${h} ${BUDGET_MSG.hour || "س"}`;
    }
    return `${m} ${BUDGET_MSG.minute || "د"}`;
}

function selectedExamBudget() {
    const opt = questionExamId?.selectedOptions?.[0];
    if (!opt || !opt.value) {
        return null;
    }
    return {
        totalSeconds: Number(opt.dataset.totalSeconds || 0),
        remainingSeconds: Number(opt.dataset.remainingSeconds || 0),
        totalMarks: opt.dataset.totalMarks === "" || opt.dataset.totalMarks == null
            ? null
            : Number(opt.dataset.totalMarks),
        remainingMarks: opt.dataset.remainingMarks === "" || opt.dataset.remainingMarks == null
            ? null
            : Number(opt.dataset.remainingMarks),
    };
}

function updateQuestionBudgetUi() {
    const budget = selectedExamBudget();
    document.querySelectorAll("#mcqQuestionForm input[name='mark'], #essayQuestionForm input[name='mark']").forEach((input) => {
        if (budget?.remainingMarks != null) {
            input.max = String(Math.max(1, budget.remainingMarks));
        } else {
            input.removeAttribute("max");
        }
    });
}

function validateQuestionAgainstBudget(form) {
    const budget = selectedExamBudget();
    if (!budget) {
        return "noexam";
    }
    const mark = Number(form.querySelector("input[name='mark']")?.value);
    const wrap = form.querySelector(".duration-picker-wrap");
    const seconds = applyDurationPicker(wrap);
    if (seconds == null) {
        return "duration";
    }
    if (seconds > budget.remainingSeconds) {
        return "time";
    }
    if (budget.remainingMarks != null && mark > budget.remainingMarks) {
        return "marks";
    }
    return null;
}

function showBudgetError(code) {
    if (!questionBudgetError) {
        return;
    }
    const map = {
        time: BUDGET_MSG.errorTime,
        marks: BUDGET_MSG.errorMarks,
        duration: BUDGET_MSG.errorDuration,
        noexam: BUDGET_MSG.errorNoExam,
        wizard: WIZARD_MSG.errorWizardQuestions,
        fields: WIZARD_MSG.errorFields,
        image_size: BUDGET_MSG.errorImageSize,
        image_type: BUDGET_MSG.errorImageType,
        image_save: BUDGET_MSG.errorImageSave,
    };
    const msg = map[code];
    if (msg) {
        questionBudgetError.textContent = msg;
        questionBudgetError.classList.remove("hidden");
    } else {
        questionBudgetError.classList.add("hidden");
    }
}

function showUrlErrors() {
    const err = getQueryParam("error");
    if (!err) {
        return;
    }
    const tab = getQueryParam("tab") || "exam";
    const examMap = {
        title: WIZARD_MSG.errorTitle,
        duration: WIZARD_MSG.errorDuration,
        department: WIZARD_MSG.errorDepartment,
        no_dept: WIZARD_MSG.errorNoDept,
        marks: WIZARD_MSG.errorMarks,
        pass: WIZARD_MSG.errorPass,
    };
    if (tab === "exam" && examMap[err]) {
        showExamCreateError([examMap[err]]);
    } else if (tab === "questions" && err === "wizard_questions") {
        showBudgetError("wizard");
    } else if (tab === "questions" && (err === "time" || err === "marks" || err === "duration")) {
        showBudgetError(err);
    } else if (tab === "questions" && err === "fields") {
        showBudgetError("fields");
    } else if (tab === "questions" && (err === "image_size" || err === "image_type" || err === "image_save")) {
        showBudgetError(err);
    }
}

function updateQuestionWizardUi() {
    const examId = questionExamId?.value || resolveExamId();
    const qcount = Number(getQueryParam("qcount") || document.body.dataset.initialQcount || "0");
    syncExamIdToForms();
    updateQuestionBudgetUi();
    showUrlErrors();

    if (getQueryParam("added") === "1" && questionSuccessMsg) {
        questionSuccessMsg.classList.remove("hidden");
        window.setTimeout(() => questionSuccessMsg.classList.add("hidden"), 2800);
    }
    if (previewExamBtn && examId) {
        previewExamBtn.href = `/admin/exams/${examId}/preview`;
        previewExamBtn.classList.remove("hidden");
    }
    if (publishExamBtn && publishExamForm && examId) {
        publishExamBtn.classList.toggle("hidden", qcount < 1);
        publishExamForm.action = `/admin/exams/${examId}/publish`;
    }
    const atMax = qcount >= MAX_QUESTIONS;
    if (addMcqBtn) {
        addMcqBtn.disabled = atMax;
    }
    if (addEssayBtn) {
        addEssayBtn.disabled = atMax;
    }
    updateGoToCandidatesBtnState(qcount);
}

function hasSelectedExamId() {
    return Boolean(
        questionExamId?.value
        || mcqExamIdHidden?.value
        || essayExamIdHidden?.value
        || resolveExamId()
    );
}

function syncQuestionFormExamId(form) {
    const examId = questionExamId?.value || resolveExamId();
    const hidden = form?.querySelector("input[name='examId']");
    if (hidden && examId) {
        hidden.value = examId;
    }
    return examId;
}

function bindQuestionForm(formId) {
    const form = document.getElementById(formId);
    form?.addEventListener("submit", (e) => {
        syncExamIdToForms();
        const examId = syncQuestionFormExamId(form);
        form.querySelectorAll(".duration-picker-wrap").forEach((wrap) => applyDurationPicker(wrap));
        if (!examId) {
            e.preventDefault();
            showBudgetError("noexam");
            return;
        }
        const err = validateQuestionAgainstBudget(form);
        if (err) {
            e.preventDefault();
            showBudgetError(err);
        } else {
            showBudgetError(null);
        }
    });
}

examCreateForm?.addEventListener("submit", (e) => {
    syncExamDurationHidden();
    const errors = validateExamCreateForm();
    if (errors.length) {
        e.preventDefault();
        showExamCreateError(errors);
    } else {
        showExamCreateError(null);
    }
});

examTitle?.addEventListener("input", () => {
    showExamCreateError(null);
    updateExamCreateBtnState();
});
totalMarksInput?.addEventListener("input", () => {
    showExamCreateError(null);
    syncPracticalFields();
    updateExamCreateBtnState();
});
passThresholdInput?.addEventListener("input", () => {
    showExamCreateError(null);
    syncPracticalFields();
    updateExamCreateBtnState();
});
practicalEnabledInput?.addEventListener("change", () => {
    showExamCreateError(null);
    syncPracticalFields();
    updateExamCreateBtnState();
});
practicalMaxMarksInput?.addEventListener("input", () => {
    showExamCreateError(null);
    updateExamCreateBtnState();
});
syncPracticalFields();
examDepartmentId?.addEventListener("change", () => {
    showExamCreateError(null);
    updateExamCreateBtnState();
});
getExamDurationWrap()?.querySelectorAll(".duration-picker-value, .duration-picker-unit").forEach((el) => {
    el.addEventListener("change", () => {
        syncExamDurationHidden();
        showExamCreateError(null);
        updateExamCreateBtnState();
    });
});

questionExamId?.addEventListener("change", () => {
    const url = new URL(window.location.href);
    const qtype = getQueryParam("qtype") || "mcq";
    if (questionExamId.value) {
        url.searchParams.set("examId", questionExamId.value);
    } else {
        url.searchParams.delete("examId");
    }
    url.searchParams.set("view", "create");
    url.searchParams.set("tab", "questions");
    url.searchParams.set("qtype", qtype);
    url.searchParams.delete("qcount");
    url.searchParams.delete("added");
    url.searchParams.delete("error");
    window.location.assign(url.toString());
});

bindQuestionForm("mcqQuestionForm");
bindQuestionForm("essayQuestionForm");

function initQuestionImagePickers(root) {
    (root || document).querySelectorAll(".question-image-field").forEach((field) => {
        const input = field.querySelector("input[type='file'][name='image']");
        const nameEl = field.querySelector(".question-image-upload-name");
        const previewWrap = field.querySelector(".question-image-preview");
        const previewImg = field.querySelector(".question-image-preview-img");
        if (!input) {
            return;
        }
        input.addEventListener("change", () => {
            const file = input.files?.[0];
            if (!file) {
                if (nameEl) {
                    nameEl.textContent = "";
                }
                previewWrap?.classList.add("hidden");
                if (previewImg) {
                    previewImg.removeAttribute("src");
                }
                return;
            }
            if (nameEl) {
                nameEl.textContent = file.name;
            }
            if (previewImg && previewWrap) {
                previewImg.src = URL.createObjectURL(file);
                previewWrap.classList.remove("hidden");
            }
        });
    });
}

initQuestionImagePickers(document);

document.getElementById("importQuestionsExcelForm")?.addEventListener("submit", (e) => {
    syncExamIdToForms();
    if (!importQuestionsExamIdHidden?.value) {
        e.preventDefault();
        showBudgetError("noexam");
        questionExamId?.focus();
    }
});

publishExamBtn?.addEventListener("click", () => {
    if (publishExamForm?.action) {
        publishExamForm.submit();
    }
});

initDurationPickers(document);
syncExamDurationHidden();
syncExamIdToForms();
updateExamCreateBtnState();
updateQuestionWizardUi();
