const M = window.__RC_EXAM_MONITOR || {};
const pageLocale = document.body.dataset.pageLang === "en" || document.documentElement.lang === "en" ? "en-US" : "ar-SA";

function mon(key, fallback, ...args) {
    let s = M[key] ?? fallback;
    args.forEach((v, i) => {
        s = s.replace(`{${i}}`, String(v));
    });
    return s;
}

function exprLabel(label) {
    return (M.expr && M.expr[label]) || label;
}

const examId = document.body.dataset.examId;
const evidenceIdentity = {
    exam: document.body.dataset.evidenceExam,
    candidate: document.body.dataset.evidenceCandidate || document.body.dataset.monitorUser
};
const warningToast = document.getElementById("warningToast");
const submitButtons = document.querySelectorAll(".submit-exam-btn");
const statusText = document.getElementById("statusText");
const enableCameraBtn = document.getElementById("enableCameraBtn");
const monitorVideo = document.getElementById("monitorVideo");
const monitorCanvas = document.getElementById("monitorCanvas");
const cameraBanner = document.getElementById("cameraBanner");
const cameraStateText = document.getElementById("cameraStateText");
const sessionPingText = document.getElementById("sessionPingText");
const cameraLockOverlay = document.getElementById("cameraLockOverlay");
const cameraLockDetail = document.getElementById("cameraLockDetail");
const cameraLockActionBtn = document.getElementById("cameraLockActionBtn");

let examSubmitted = false;
let examSubmitting = false;
const pendingCaptures = new Set();
const examIdNumeric = Number.parseInt(String(examId || ""), 10);

let cameraStream = null;
let cameraStartInProgress = false;
let examCameraUnlocked = false;
let faceModelsReady = false;
let faceMonitorTimer = null;
let absentFaceStreak = 0;
let gazeAwayStreak = 0;
let faceAnalysisMs = 1600;
let audioMonitorStream = null;
let audioContext = null;
let analyserNode = null;
let audioMonitorTimer = null;
let noiseStreak = 0;
let noiseQuietStreak = 0;
let noiseEpisodeActive = false;
let noiseLastLoggedAt = 0;
let audioNoiseWarningActive = false;
let audioRmsThreshold = 0.012;
let adaptiveAudioRmsThreshold = audioRmsThreshold;
let audioBaselineMargin = 0.004;
let audioMaxAdaptiveThreshold = 0.040;
let audioSpikeMultiplier = 1.30;
let audioStreakTrigger = 2;
let audioBaselineWindowMs = 2500;
let audioEpisodeGapMs = 6000;
let audioTickMs = 300;
let audioQuietRollingAvg = 0;
let audioMonitorReady = false;
let audioPostFailWarned = false;
let audioUsesDedicatedStream = false;
let audioBaselineStartedAt = 0;
let audioBaselineSamples = [];
let audioBaselineDone = false;
const lastThrottleByKey = Object.create(null);
const FACE_WEIGHTS_BASE = "https://cdn.jsdelivr.net/npm/face-api.js@0.22.2/weights";
const recentBehaviorEvents = [];
const evidenceBuffer = [];

let SNAPSHOT_THROTTLE_MS = {
    NO_FACE_STREAK: 32000,
    MULTIPLE_FACES: 36000,
    STRESS_EXPRESSION: 42000,
    PERIODIC_CHECK: 120000
};

const SESSION_STORAGE_KEY = `rc_exam_client_sid_${examId}`;
let clientSessionId = sessionStorage.getItem(SESSION_STORAGE_KEY);
if (!clientSessionId && typeof crypto !== "undefined" && crypto.randomUUID) {
    clientSessionId = crypto.randomUUID();
    sessionStorage.setItem(SESSION_STORAGE_KEY, clientSessionId);
} else if (!clientSessionId) {
    clientSessionId = `${Date.now()}-${Math.random().toString(16).slice(2)}`;
    sessionStorage.setItem(SESSION_STORAGE_KEY, clientSessionId);
}
let heartbeatFailStreak = 0;
let riskScore = 0;
let riskDecayTimer = null;
let periodicSnapshotTimer = null;
const EVENT_AGG_WINDOW_MS = 60000;
/** أحداث تُسجَّل كل مرة دون دمج (للمراجعة والفرز بعد الاختبار) */
const NO_AGGREGATE_EVENTS = new Set(["EXTERNAL_NOISE_DETECTED"]);
const eventAggState = new Map();

const EVENT_SEVERITY_POINTS = {
    LOW: 3,
    MEDIUM: 8,
    HIGH: 15
};

const EVENT_EXTRA_POINTS = {
    TAB_SWITCH: 8,
    DEVTOOLS_ATTEMPT: 12,
    MULTIPLE_FACES_DETECTED: 12,
    CAMERA_FACE_ABSENT: 10,
    SESSION_MISMATCH: 14,
    HEARTBEAT_STALL: 6,
    GAZE_AWAY_PROLONGED: 11,
    EXTERNAL_NOISE_DETECTED: 7
};

/** مدة مقطع الفيديو المرفق مع كل ملاحظة/لقطة مراقبة (مللي ثانية) */
const SNAPSHOT_VIDEO_MS = 5500;
const CRITICAL_EVIDENCE_VIDEO_MS = 7000;

const CORRELATION_EVENT_WINDOW_MS = 90000;

function examContext() {
    return Number.isFinite(examIdNumeric) ? { examId: examIdNumeric } : {};
}

async function fetchMonitoringClientConfig() {
    try {
        const r = await fetch("/monitoring/client-config");
        if (!r.ok) {
            return null;
        }
        return await r.json();
    } catch {
        return null;
    }
}

function applyClientConfig(cfg) {
    if (!cfg || typeof cfg !== "object") {
        return;
    }
    const t = cfg.snapshotThrottleMs;
    if (t && typeof t === "object") {
        if (typeof t.NO_FACE_STREAK === "number") {
            SNAPSHOT_THROTTLE_MS.NO_FACE_STREAK = t.NO_FACE_STREAK;
        }
        if (typeof t.MULTIPLE_FACES === "number") {
            SNAPSHOT_THROTTLE_MS.MULTIPLE_FACES = t.MULTIPLE_FACES;
        }
        if (typeof t.STRESS_EXPRESSION === "number") {
            SNAPSHOT_THROTTLE_MS.STRESS_EXPRESSION = t.STRESS_EXPRESSION;
        }
        if (typeof t.PERIODIC_CHECK === "number") {
            SNAPSHOT_THROTTLE_MS.PERIODIC_CHECK = t.PERIODIC_CHECK;
        }
    }
    if (typeof cfg.faceAnalysisIntervalMs === "number" && cfg.faceAnalysisIntervalMs >= 800) {
        faceAnalysisMs = cfg.faceAnalysisIntervalMs;
    }
    const audio = cfg.audio;
    if (audio && typeof audio === "object") {
        if (typeof audio.rmsThreshold === "number" && audio.rmsThreshold > 0.005) {
            audioRmsThreshold = audio.rmsThreshold;
        }
        if (typeof audio.baselineMargin === "number" && audio.baselineMargin >= 0) {
            audioBaselineMargin = audio.baselineMargin;
        }
        if (typeof audio.maxAdaptiveThreshold === "number" && audio.maxAdaptiveThreshold > 0.01) {
            audioMaxAdaptiveThreshold = audio.maxAdaptiveThreshold;
        }
        if (typeof audio.spikeMultiplier === "number" && audio.spikeMultiplier >= 1.05) {
            audioSpikeMultiplier = Math.min(2.0, audio.spikeMultiplier);
        }
        if (typeof audio.baselineWindowMs === "number" && audio.baselineWindowMs >= 1500) {
            audioBaselineWindowMs = audio.baselineWindowMs;
        }
        if (typeof audio.streakTrigger === "number" && audio.streakTrigger >= 1) {
            audioStreakTrigger = Math.min(4, Math.round(audio.streakTrigger));
        }
        if (typeof audio.episodeGapMs === "number" && audio.episodeGapMs >= 2000) {
            audioEpisodeGapMs = audio.episodeGapMs;
        }
        if (typeof audio.tickMs === "number" && audio.tickMs >= 200) {
            audioTickMs = Math.min(2000, Math.round(audio.tickMs));
        }
        adaptiveAudioRmsThreshold = audioRmsThreshold;
    }
}

async function ensureAudioContextRunning() {
    if (!audioContext) {
        return;
    }
    if (audioContext.state === "suspended") {
        try {
            await audioContext.resume();
        } catch (_) {
            /* يُعاد المحاولة في التيك التالي */
        }
    }
}

function restartAudioMonitorTimer() {
    if (audioMonitorTimer) {
        clearInterval(audioMonitorTimer);
    }
    audioMonitorTimer = window.setInterval(runAudioMonitorTick, audioTickMs);
}

async function monitoringPost(url, bodyObj) {
    const id = crypto.randomUUID?.() ?? Date.now() + "-" + Math.random();
    const body = JSON.stringify({ ...examContext(), ...bodyObj,
        ...(url === "/monitoring/snapshot" && crypto.randomUUID ? { uploadId: id } : {})
    });
    if (url === "/monitoring/snapshot") {
        try {
            await monitoringOutbox.add({ id, url, payload: body });
            void monitoringOutbox.flush();
            return null;
        } catch {
            // Try the server directly if browser storage is unavailable.
        }
    }
    try {
        const r = await fetch(url, {
            method: "POST",
            headers: { "Content-Type": "application/json" },
            credentials: "same-origin",
            signal: AbortSignal.timeout(15000),
            body
        });
        if (r.ok && !r.redirected) {
            if (url === "/monitoring/snapshot"
                && (await r.clone().json().catch(() => null))?.status === "partial") warnMonitoringStorage();
            void monitoringOutbox.flush();
            return r;
        }
        if ([400, 403, 413, 415, 422].includes(r.status)) {
            warnMonitoringStorage();
            return null;
        }
        if (!audioPostFailWarned && (r.status === 401 || r.status === 403)) {
            audioPostFailWarned = true;
            showWarning(mon("saveFail", "تعذر حفظ بعض أحداث المراقبة — أعد تسجيل الدخول إن استمرت المشكلة"));
        }
    } catch {
        /* يسجّل لاحقاً في الطابور */
    }
    try {
        await monitoringOutbox.add({ id, url, payload: body });
    } catch {
        warnMonitoringStorage();
    }
    return null;
}

window.addEventListener("monitoring-upload-rejected", warnMonitoringStorage);

function warnMonitoringStorage() {
    showWarning(pageLocale === "en-US"
        ? "Some monitoring evidence could not be saved. Contact the exam supervisor."
        : "تعذر حفظ بعض أدلة المراقبة. تواصل مع مراقب الاختبار.");
}

function canThrottleSnapshot(key, ms) {
    const now = Date.now();
    const last = lastThrottleByKey[key] || 0;
    if (now - last < ms) {
        return false;
    }
    lastThrottleByKey[key] = now;
    return true;
}

function pushBehaviorEvent(eventType) {
    const now = Date.now();
    recentBehaviorEvents.push({ eventType, ts: now });
    while (recentBehaviorEvents.length && now - recentBehaviorEvents[0].ts > 2 * CORRELATION_EVENT_WINDOW_MS) {
        recentBehaviorEvents.shift();
    }
}

function hasRecentEvent(eventType, withinMs = CORRELATION_EVENT_WINDOW_MS) {
    const now = Date.now();
    return recentBehaviorEvents.some((e) => e.eventType === eventType && now - e.ts <= withinMs);
}

function appendEvidenceFrame() {
    const img = captureCompressedJpeg();
    if (!img) {
        return;
    }
    evidenceBuffer.push({ ts: Date.now(), imageBase64: img });
    while (evidenceBuffer.length > 6) {
        evidenceBuffer.shift();
    }
}

async function recordShortVideoClip(maxMs = 8000) {
    if (!cameraStream || typeof MediaRecorder === "undefined") {
        return null;
    }
    const stream = buildEvidenceStream();
    if (!stream || stream.getVideoTracks().length === 0) {
        return null;
    }
    const canvas = document.createElement("canvas");
    let videoTrack;
    try {
        if (typeof canvas.captureStream !== "function"
            || !window.jadeerEvidence.draw(canvas, monitorVideo, evidenceIdentity, 720)) return null;
        videoTrack = canvas.captureStream(12).getVideoTracks()[0];
    } catch {
        return null;
    }
    if (!videoTrack) return null;
    const recordingStream = new MediaStream([videoTrack, ...stream.getAudioTracks()]);
    const hasAudio = recordingStream.getAudioTracks().length > 0;
    const frameTimer = window.setInterval(() => {
        window.jadeerEvidence.draw(canvas, monitorVideo, evidenceIdentity, 720);
    }, 1000 / 12);
    try {
        const mime = pickEvidenceMimeType(hasAudio);
        const options = { videoBitsPerSecond: 500000, audioBitsPerSecond: 64000 };
        if (mime) options.mimeType = mime;
        let recorder;
        try {
            recorder = new MediaRecorder(recordingStream, options);
        } catch (_) {
            recorder = new MediaRecorder(recordingStream, { videoBitsPerSecond: 500000 });
        }
        const chunks = [];
        recorder.ondataavailable = (e) => {
            if (e.data && e.data.size > 0) {
                chunks.push(e.data);
            }
        };
        const stopped = new Promise((resolve) => {
            recorder.onstop = () => {
                if (!chunks.length) {
                    resolve(null);
                    return;
                }
                const blobType = chunks[0].type || recorder.mimeType || mime || "application/octet-stream";
                const blob = new Blob(chunks, { type: blobType });
                if (blob.size < 800) {
                    resolve(null);
                    return;
                }
                const reader = new FileReader();
                reader.onload = () => resolve(reader.result);
                reader.onerror = () => resolve(null);
                reader.readAsDataURL(blob);
            };
            recorder.onerror = () => resolve(null);
        });
        // timeslice يضمن وصول بيانات الصوت/الفيديو تدريجياً
        recorder.start(250);
        await new Promise((r) => window.setTimeout(r, maxMs));
        if (recorder.state !== "inactive") {
            recorder.stop();
        }
        let finalizationTimer;
        try {
            return await Promise.race([
                stopped,
                new Promise((resolve) => { finalizationTimer = window.setTimeout(() => resolve(null), 2500); })
            ]);
        } finally {
            window.clearTimeout(finalizationTimer);
        }
    } catch (_) {
        return null;
    } finally {
        window.clearInterval(frameTimer);
        videoTrack.stop();
    }
}

function pickEvidenceMimeType(preferAudio) {
    const withAudio = [
        "video/webm;codecs=vp8,opus",
        "video/webm;codecs=vp9,opus",
        "video/webm",
        "video/mp4;codecs=avc1.42E01E,mp4a.40.2",
        "video/mp4"
    ];
    const videoOnly = [
        "video/webm;codecs=vp8",
        "video/webm",
        "video/mp4;codecs=avc1.42E01E",
        "video/mp4"
    ];
    const list = preferAudio ? withAudio : videoOnly;
    for (const m of list) {
        if (MediaRecorder.isTypeSupported(m)) {
            return m;
        }
    }
    return "";
}

/** فيديو الكاميرا + مسار الصوت (من الكاميرا أو ميكروفون المراقبة) لكل ملاحظة */
function buildEvidenceStream() {
    if (!cameraStream) {
        return null;
    }
    const videoTracks = cameraStream.getVideoTracks().filter((t) => t.readyState === "live");
    if (!videoTracks.length) {
        return null;
    }
    let audioTracks = cameraStream.getAudioTracks().filter((t) => t.readyState === "live" && t.enabled !== false);
    if (!audioTracks.length && audioMonitorStream) {
        audioTracks = audioMonitorStream.getAudioTracks().filter((t) => t.readyState === "live" && t.enabled !== false);
    }
    return new MediaStream([...videoTracks, ...audioTracks]);
}

async function uploadSnapshotWithImage(eventType, severity, details, imageBase64, videoBase64 = null,
                                       confidenceScore = null) {
    const snapshotType = eventType.endsWith("_SNAPSHOT") ? eventType : eventType + "_SNAPSHOT";
    if (!imageBase64 && !videoBase64) {
        await monitoringPost("/monitoring/event", {
            eventType: snapshotType, severity, confidenceScore,
            details: (details || "") + " | تعذر التقاط الصورة والفيديو"
        });
        return;
    }
    const stream = buildEvidenceStream();
    const hasAudioTrack = !!(stream && stream.getAudioTracks().length > 0);
    const hasBoth = !!(imageBase64 && videoBase64);
    let docHint;
    if (hasBoth && hasAudioTrack) {
        docHint = "إلزامي: صورة + فيديو مع صوت";
    } else if (hasBoth) {
        docHint = "إلزامي: صورة + فيديو (الصوت غير متاح على الجهاز)";
    } else if (imageBase64) {
        docHint = "صورة فقط (تعذر الفيديو بعد إعادة المحاولة)";
    } else {
        docHint = "فيديو فقط (تعذرت الصورة)";
    }
    await monitoringPost("/monitoring/snapshot", {
        eventType: snapshotType,
        severity,
        confidenceScore,
        details: details ? `${details} | توثيق: ${docHint}` : `توثيق: ${docHint}`,
        imageBase64: imageBase64 || undefined,
        videoBase64: videoBase64 || undefined
    });
}

async function captureEvidenceBundle(baseEventType, severity, details) {
    const pre = evidenceBuffer.length ? evidenceBuffer[evidenceBuffer.length - 1].imageBase64 : captureCompressedJpeg();
    const during = captureCompressedJpeg();
    let videoClip = await recordShortVideoClip(CRITICAL_EVIDENCE_VIDEO_MS);
    if (!videoClip) {
        videoClip = await recordShortVideoClip(4000);
    }
    await Promise.all([
        uploadSnapshotWithImage(baseEventType + "_PRE", severity, details + " | دليل قبل الحدث", pre, videoClip),
        uploadSnapshotWithImage(baseEventType + "_DURING", severity, details + " | دليل أثناء الحدث", during, videoClip)
    ]);
    await new Promise((resolve) => window.setTimeout(resolve, 2000));
    const post = captureCompressedJpeg();
    const postVideo = await recordShortVideoClip(3500);
    await uploadSnapshotWithImage(baseEventType + "_POST", severity, details + " | دليل بعد الحدث", post, postVideo);
}

async function evaluateCorrelationRules(triggerEventType, triggerSeverity, triggerDetails) {
    const ruleA = hasRecentEvent("TAB_SWITCH") && hasRecentEvent("CAMERA_FACE_ABSENT") && hasRecentEvent("GAZE_AWAY_PROLONGED");
    const ruleB = hasRecentEvent("DEVTOOLS_ATTEMPT") && (hasRecentEvent("SESSION_MISMATCH", 120000) || hasRecentEvent("TAB_SWITCH", 120000));
    if (!ruleA && !ruleB) {
        return;
    }
    if (!canThrottleSnapshot("CRITICAL_CORRELATION", 90000)) {
        return;
    }
    const ruleId = ruleA ? "RULE-A(TAB+ABSENT+GAZE)" : "RULE-B(DEVTOOLS+SESSION/TAB)";
    const merged =
        `CorrelationRule=${ruleId} | Trigger=${triggerEventType}/${triggerSeverity} | ` +
        `detail=${triggerDetails || "-"}`;
    const eventLog = monitoringPost("/monitoring/event", {
        ...examContext(),
        eventType: "CRITICAL_CORRELATED_PATTERN",
        severity: "HIGH",
        details: merged
    });
    applyRiskDelta("CRITICAL_CORRELATED_PATTERN", "HIGH");
    showWarning(mon("critical", "تم تفعيل تنبيه حرج مركب للمرشح"));
    if (!examSubmitting && !examSubmitted) {
        void trackCapture(captureEvidenceBundle("CRITICAL_CORRELATED_PATTERN", "HIGH", merged));
    }
    await eventLog;
}

function renderRiskHud() {
    if (!statusText) {
        return;
    }
    statusText.textContent = mon("monitoringActive", "المراقبة الذكية مفعلة");
}

function applyRiskDelta(eventType, severity) {
    const sev = EVENT_SEVERITY_POINTS[(severity || "MEDIUM").toUpperCase()] ?? 6;
    const extra = EVENT_EXTRA_POINTS[eventType] ?? 0;
    riskScore = Math.max(0, Math.min(100, riskScore + sev + extra));
    renderRiskHud();
}

function computeAdaptiveSnapshotIntervalMs() {
    if (riskScore >= 70) {
        return 15000;
    }
    if (riskScore >= 35) {
        return 45000;
    }
    return 120000;
}

function startPeriodicSnapshots() {
    const startedAt = Date.now();
    periodicSnapshotTimer = setInterval(() => {
        const interval = Math.max(computeAdaptiveSnapshotIntervalMs(), SNAPSHOT_THROTTLE_MS.PERIODIC_CHECK);
        const lastCapture = lastThrottleByKey.PERIODIC_CHECK || startedAt;
        if (cameraStream && examCameraUnlocked && Date.now() - lastCapture >= interval
            && canThrottleSnapshot("PERIODIC_CHECK", interval)) {
            uploadSnapshot(
                "PERIODIC_CHECK",
                "LOW",
                `لقطة مرجعية دورية متكيفة مع مستوى المخاطرة (score=${Math.round(riskScore)})`
            ).catch(() => {});
        }
    }, 1000);
}

function startRiskDecay() {
    if (riskDecayTimer) {
        return;
    }
    riskDecayTimer = setInterval(() => {
        if (riskScore > 0) {
            riskScore = Math.max(0, riskScore - 1);
            renderRiskHud();
        }
    }, 10000);
}

function computeAudioRms(analyser) {
    if (!analyser) {
        return 0;
    }
    const data = new Uint8Array(analyser.fftSize);
    analyser.getByteTimeDomainData(data);
    let sum = 0;
    for (let i = 0; i < data.length; i += 1) {
        const v = (data[i] - 128) / 128;
        sum += v * v;
    }
    return Math.sqrt(sum / data.length);
}

function stopAudioMonitor() {
    if (audioMonitorTimer) {
        clearInterval(audioMonitorTimer);
        audioMonitorTimer = null;
    }
    if (audioContext) {
        try {
            audioContext.close();
        } catch (_) {}
        audioContext = null;
    }
    if (audioMonitorStream && audioUsesDedicatedStream) {
        audioMonitorStream.getAudioTracks().forEach((t) => t.stop());
    }
    audioMonitorStream = null;
    audioUsesDedicatedStream = false;
    analyserNode = null;
    noiseStreak = 0;
    noiseQuietStreak = 0;
    noiseEpisodeActive = false;
    audioNoiseWarningActive = false;
    audioMonitorReady = false;
    audioQuietRollingAvg = 0;
}

function isAudioLoud(rms) {
    if (!audioBaselineDone) {
        return false;
    }
    // عتبة ثابتة/تكيّفية
    if (rms > adaptiveAudioRmsThreshold) {
        return true;
    }
    // قفزة مفاجئة فوق مستوى الهدوء الحالي
    const quiet = Math.max(0.002, audioQuietRollingAvg || 0.002);
    if (rms > quiet * audioSpikeMultiplier) {
        return true;
    }
    // كلام واضح تقريباً فوق ضوضاء الخلفية
    if (rms > quiet + 0.008 && rms > 0.010) {
        return true;
    }
    return false;
}

function runAudioMonitorTick() {
    if (!analyserNode) {
        return;
    }
    void ensureAudioContextRunning();
    const rms = computeAudioRms(analyserNode);
    if (!audioBaselineDone) {
        audioBaselineSamples.push(rms);
        if (Date.now() - audioBaselineStartedAt >= audioBaselineWindowMs) {
            const avg = audioBaselineSamples.length
                ? audioBaselineSamples.reduce((a, b) => a + b, 0) / audioBaselineSamples.length
                : audioRmsThreshold;
            const adaptive = Math.max(audioRmsThreshold, avg + audioBaselineMargin);
            adaptiveAudioRmsThreshold = Math.min(audioMaxAdaptiveThreshold, Number(adaptive.toFixed(4)));
            audioBaselineDone = true;
            audioQuietRollingAvg = avg;
        }
    }
    if (audioBaselineDone && rms <= adaptiveAudioRmsThreshold) {
        audioQuietRollingAvg = audioQuietRollingAvg
            ? audioQuietRollingAvg * 0.92 + rms * 0.08
            : rms;
    }
    appendEvidenceFrame();
    const loud = isAudioLoud(rms);
    const now = Date.now();
    if (loud) {
        noiseStreak += 1;
        noiseQuietStreak = 0;
        // السماح بإعادة تسجيل حادثة أثناء ضوضاء متواصلة بعد فاصل زمني
        if (noiseEpisodeActive && noiseLastLoggedAt > 0 && now - noiseLastLoggedAt >= audioEpisodeGapMs) {
            noiseEpisodeActive = false;
        }
    } else {
        noiseStreak = 0;
        noiseQuietStreak += 1;
    }

    if (loud && noiseStreak >= audioStreakTrigger) {
        if (!noiseEpisodeActive) {
            noiseEpisodeActive = true;
            noiseLastLoggedAt = now;
            const confidenceScore = Math.max(40, Math.min(98, Math.round((rms / Math.max(0.001, adaptiveAudioRmsThreshold)) * 60)));
            const at = new Date().toLocaleTimeString(pageLocale, { hour: "2-digit", minute: "2-digit", second: "2-digit" });
            const detail = `رصد ضوضاء/أصوات خارجية عند ${at} (rms=${rms.toFixed(3)}, baseline=${adaptiveAudioRmsThreshold.toFixed(3)})`;
            void logBehaviorEvent("EXTERNAL_NOISE_DETECTED", "MEDIUM", detail, true, confidenceScore);
        }
        // تبقى الملاحظة ظاهرة طوال وجود الصوت
        showWarning(mon("externalNoise", "تم رصد أصوات خارجية أثناء الاختبار"), true);
        audioNoiseWarningActive = true;
    } else if (!loud && noiseQuietStreak >= 2 && (noiseEpisodeActive || audioNoiseWarningActive)) {
        // تختفي الملاحظة بعد سكون قصير لتفادي الوميض
        noiseEpisodeActive = false;
        hideAudioNoiseWarningIfShown();
    }
}

function hideAudioNoiseWarningIfShown() {
    if (!audioNoiseWarningActive) {
        return;
    }
    audioNoiseWarningActive = false;
    const expected = mon("externalNoise", "تم رصد أصوات خارجية أثناء الاختبار");
    if (warningToast && warningToast.textContent === expected) {
        clearWarning();
    }
}

async function acquireDedicatedAudioStream() {
    if (!navigator.mediaDevices?.getUserMedia) {
        return null;
    }
    return navigator.mediaDevices.getUserMedia({
        audio: {
            echoCancellation: false,
            noiseSuppression: false,
            autoGainControl: false,
            channelCount: 1
        },
        video: false
    });
}

async function startAudioMonitor(sharedStream) {
    stopAudioMonitor();
    if (typeof window.AudioContext === "undefined" && typeof window.webkitAudioContext === "undefined") {
        showWarning(mon("audioUnsupported", "المتصفح لا يدعم تحليل الصوت"));
        return;
    }
    try {
        try {
            audioMonitorStream = await acquireDedicatedAudioStream();
            audioUsesDedicatedStream = true;
        } catch (_) {
            if (sharedStream && sharedStream.getAudioTracks().length > 0) {
                audioMonitorStream = sharedStream;
                audioUsesDedicatedStream = false;
            } else {
                throw new Error("no_audio_track");
            }
        }

        const AudioCtx = window.AudioContext || window.webkitAudioContext;
        audioContext = new AudioCtx();
        await ensureAudioContextRunning();
        const source = audioContext.createMediaStreamSource(audioMonitorStream);
        analyserNode = audioContext.createAnalyser();
        analyserNode.fftSize = 2048;
        analyserNode.smoothingTimeConstant = 0.2;
        source.connect(analyserNode);

        audioBaselineStartedAt = Date.now();
        audioBaselineSamples = [];
        audioBaselineDone = false;
        audioQuietRollingAvg = 0;
        noiseLastLoggedAt = 0;
        noiseEpisodeActive = false;
        noiseStreak = 0;
        noiseQuietStreak = 0;
        audioNoiseWarningActive = false;
        adaptiveAudioRmsThreshold = audioRmsThreshold;
        audioMonitorReady = true;
        restartAudioMonitorTimer();

        updateCameraFooter(true, mon("audioActive", "رصد الصوت: مفعّل"));
        if (statusText) {
            statusText.textContent = mon(
                "monitoringAudio",
                "المراقبة + رصد الأصوات (عتبة {0})",
                adaptiveAudioRmsThreshold.toFixed(3)
            );
        }
        void logEvent(
            "AUDIO_MONITOR_STARTED",
            "LOW",
            `ميكروفون مراقبة الصوت نشط (مستقل=${audioUsesDedicatedStream})`
        );
    } catch (e) {
        audioMonitorReady = false;
        const reason = e && e.name ? e.name : "unknown";
        showWarning(mon("micFail", "تعذر تفعيل الميكروفون؛ لن تُسجَّل مخالفات الأصوات"), true);
        void logEvent(
            "AUDIO_MONITOR_DENIED",
            "MEDIUM",
            `تعذر تفعيل مراقبة الضوضاء (${reason})`
        );
    }
}

async function logBehaviorEvent(eventType, severity, details, withSnapshot = true, confidenceScore = null) {
    // كل ملاحظة تُسجَّل فوراً مع صورة + فيديو (لا دمج يُسقِط أحداثاً وسطية)
    await logEventWithSnapshot(eventType, severity, details, confidenceScore);
}

function dominantExpression(expr) {
    if (!expr || typeof expr !== "object") {
        return { label: "unknown", score: 0 };
    }
    let label = "neutral";
    let score = 0;
    for (const [k, v] of Object.entries(expr)) {
        if (typeof v === "number" && v > score) {
            score = v;
            label = k;
        }
    }
    return { label, score };
}

/** تعابير تُعتبر غير مطمئنة ضمن سياسة الاختبار (مرجع فقط؛ ليس تشخيصاً طبياً) */
function stressLevelFromDominant(label, score) {
    const t = {
        angry: 0.4,
        fearful: 0.36,
        disgusted: 0.36,
        sad: 0.52,
        surprised: 0.62
    };
    const need = t[label];
    if (need == null) {
        return { stress: false, reason: null };
    }
    if (score >= need) {
        return { stress: true, reason: label };
    }
    return { stress: false, reason: null };
}

function sessionPing(ok, detail) {
    if (!sessionPingText) {
        return;
    }
    if (ok) {
        const time = new Date().toLocaleTimeString(pageLocale, {
            hour: "2-digit",
            minute: "2-digit",
            second: "2-digit"
        });
        sessionPingText.textContent = mon("sessionActive", "الجلسة: نشطة — آخر اتصال {0}", time);
        return;
    }
    sessionPingText.textContent = detail || mon("sessionConflict", "تعارض أو انقطاع");
}

async function sessionStartRequest() {
    const res = await fetch(`/exam/${examId}/session/start`, {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ clientSessionId })
    });
    const data = await res.json().catch(() => ({}));
    return { res, data };
}

async function sessionHeartbeatRequest() {
    const res = await fetch(`/exam/${examId}/session/heartbeat`, {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ clientSessionId })
    });
    return res;
}

async function initExamSession() {
    const { res, data } = await sessionStartRequest();
    if (res.status === 409 && data.status === "finished") {
        sessionPing(false, mon("sessionFinished", "اختبار مسلّم"));
        alert(data.message || mon("alreadySubmitted", "تم تسليم هذا الاختبار مسبقاً."));
        window.location.href = "/dashboard";
        return false;
    }
    if (res.status === 409) {
        sessionPing(false, mon("otherWindow", "نافذة أخرى مفتوحة"));
        const msg = data.message || mon("otherWindowMsg", "يجب إغلاق نافذة الاختبار الأخرى قبل المتابعة.");
        alert(msg);
        window.location.href = "/dashboard";
        return false;
    }
    if (res.ok) {
        heartbeatFailStreak = 0;
        sessionPing(true);
    } else {
        sessionPing(false, mon("sessionNotRegistered", "لم يتم تسجيل الجلسة"));
    }
    return true;
}

async function runHeartbeat() {
    try {
        const res = await sessionHeartbeatRequest();
        if (res.ok) {
            heartbeatFailStreak = 0;
            sessionPing(true);
            void monitoringOutbox.flush();
            return;
        }
        heartbeatFailStreak += 1;
        sessionPing(false, mon("checkConnection", "تحقق من الاتصال"));
        if (heartbeatFailStreak >= 4) {
            showWarning(mon("heartbeatStall", "اضطراب متكرر في اتصال الجلسة؛ راجع شبكتك بدون مغادرة الاختبار"));
            await logEventWithSnapshot("HEARTBEAT_STALL", "MEDIUM", "فشل نبض الجلسة عدة مرات متتالية");
            heartbeatFailStreak = 0;
        }
        if (res.status === 409) {
            showWarning(mon("sessionMismatch", "هذه الصفحة لم تعد الجلسة الرسمية. حدّث الصفحة بعد إغلاق النوافذ الأخرى."));
            await logEventWithSnapshot("SESSION_MISMATCH", "HIGH", "تهيئة أو تعارض جلسة الطلب مع الخادم");
        }
    } catch (_) {
        heartbeatFailStreak += 1;
        sessionPing(false, mon("networkError", "خطأ شبكة"));
    }
}

function collectAnswers() {
    const answers = {};
    document.querySelectorAll(".question-block").forEach((block) => {
        const qid = block.dataset.questionId;
        const essay = block.querySelector("textarea.essay-answer");
        if (essay) {
            answers[qid] = essay.value ? essay.value.trim() : "";
            return;
        }
        const selected = block.querySelector("input[type='radio']:checked");
        answers[qid] = selected ? selected.value : "";
    });
    return answers;
}

let warningHideTimer = null;

function showWarning(msg, sticky = false) {
    if (!warningToast) {
        return;
    }
    warningToast.textContent = msg;
    warningToast.classList.remove("hidden");
    if (warningHideTimer) {
        clearTimeout(warningHideTimer);
        warningHideTimer = null;
    }
    if (!sticky) {
        warningHideTimer = window.setTimeout(() => {
            warningToast.classList.add("hidden");
            warningHideTimer = null;
        }, 2600);
    }
    renderRiskHud();
}

function clearWarning() {
    if (!warningToast) {
        return;
    }
    if (warningHideTimer) {
        clearTimeout(warningHideTimer);
        warningHideTimer = null;
    }
    warningToast.classList.add("hidden");
}

async function logEvent(eventType, severity, details, confidenceScore = null) {
    applyRiskDelta(eventType, severity);
    pushBehaviorEvent(eventType);
    void uploadSnapshot(eventType, severity, details, confidenceScore);
    await Promise.all([
        monitoringPost("/monitoring/event", { eventType, severity, details, confidenceScore }),
        evaluateCorrelationRules(eventType, severity, details)
    ]);
}

function captureCompressedJpeg(maxWidth = 720, quality = 0.72) {
    if (!monitorVideo || !monitorCanvas || !cameraStream) {
        return null;
    }
    if (!window.jadeerEvidence.draw(monitorCanvas, monitorVideo, evidenceIdentity, maxWidth)) return null;
    return monitorCanvas.toDataURL("image/jpeg", quality);
}

// Nearby observations share a recording that covers each event, while each gets its own image.
let activeObservationClip = null;

function observationClip() {
    const now = Date.now();
    if (activeObservationClip && activeObservationClip.until - now >= 1500)
        return activeObservationClip.promise;

    const next = { until: now + SNAPSHOT_VIDEO_MS, promise: recordShortVideoClip(SNAPSHOT_VIDEO_MS) };
    activeObservationClip = next;
    void next.promise.finally(() => {
        if (activeObservationClip === next) activeObservationClip = null;
    }).catch(() => {});
    return next.promise;
}

function uploadSnapshot(eventType, severity, details, confidenceScore = null) {
    if (examSubmitting || examSubmitted) return Promise.resolve();
    return trackCapture(captureSnapshot(eventType, severity, details, confidenceScore));
}

function trackCapture(promise) {
    pendingCaptures.add(promise);
    return promise.catch(() => warnMonitoringStorage()).finally(() => pendingCaptures.delete(promise));
}

async function captureSnapshot(eventType, severity, details, confidenceScore) {
    let image = captureCompressedJpeg();
    let video = cameraStream ? await observationClip() : null;
    if (!video && cameraStream && typeof MediaRecorder !== "undefined")
        video = await observationClip();
    if (!image) image = captureCompressedJpeg();

    await uploadSnapshotWithImage(eventType, severity, details, image, video, confidenceScore);
}

async function logEventWithSnapshot(eventType, severity, details, confidenceScore = null) {
    // يمر عبر logEvent الذي يُرفق الصورة والفيديو+الصوت إلزامياً لكل ملاحظة
    await logEvent(eventType, severity, details, confidenceScore);
}

async function autoSave() {
    await fetch(`/exam/${examId}/autosave`, {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ answers: JSON.stringify(collectAnswers()) })
    });
}

async function submitExam() {
    if (examSubmitting || examSubmitted) return;
    examSubmitting = true;
    submitButtons.forEach((button) => { button.disabled = true; });
    try {
        const response = await fetch(`/exam/${examId}/submit`, {
            method: "POST",
            headers: { "Content-Type": "application/json" },
            body: JSON.stringify({ answers: JSON.stringify(collectAnswers()) })
        });
        if (!response.ok || response.redirected) {
            const result = await response.json().catch(() => ({}));
            showWarning(result.message || mon("submitFailed", "تعذر تسليم الاختبار. حاول مرة أخرى."));
            return;
        }
        examSubmitted = true;
        window.clearExamQuestionClock?.();
        // Wait for already-started captures to reach the durable browser outbox.
        // The dashboard continues uploads if network confirmation takes longer.
        await Promise.allSettled(pendingCaptures);
        await Promise.race([
            monitoringOutbox.flush(),
            new Promise((resolve) => window.setTimeout(resolve, 8000))
        ]);
        alert(mon("submitSuccess", "تم تسليم الاختبار بنجاح."));
        stopCamera();
        window.location.href = "/dashboard";
    } catch {
        showWarning(mon("submitFailed", "تعذر تسليم الاختبار. حاول مرة أخرى."));
    } finally {
        examSubmitting = false;
        if (!examSubmitted) submitButtons.forEach((button) => { button.disabled = false; });
    }
}

function stopFaceMonitor() {
    if (faceMonitorTimer !== null) {
        clearInterval(faceMonitorTimer);
        faceMonitorTimer = null;
    }
}

async function loadFaceModels() {
    if (typeof faceapi === "undefined") {
        return false;
    }
    try {
        await faceapi.nets.tinyFaceDetector.loadFromUri(FACE_WEIGHTS_BASE);
        await faceapi.nets.faceLandmark68TinyNet.loadFromUri(FACE_WEIGHTS_BASE);
        await faceapi.nets.faceExpressionNet.loadFromUri(FACE_WEIGHTS_BASE);
        faceModelsReady = true;
        return true;
    } catch (e) {
        faceModelsReady = false;
        showWarning(mon("faceModelFail", "تعذر تحميل تحليل الوجه؛ لن تعمل تنبيهات تموضع/انحراف الوجه"));
        await logEvent(
            "FACE_MODEL_LOAD_FAILED",
            "LOW",
            "تعذر تحميل نماذج تحليل الوجه من الشبكة؛ تبقى المراقبة بالكاميرا والأحداث السلوكية فقط"
        );
        return false;
    }
}

function updateCameraFooter(videoOk, emotionHint) {
    if (!cameraStateText) {
        return;
    }
    const cam = videoOk
        ? mon("cameraActive", "الكاميرا: نشطة للمراقبة الإلزامية")
        : mon("cameraStopped", "الكاميرا: متوقّفة");
    cameraStateText.textContent = emotionHint ? `${cam} — ${emotionHint}` : cam;
}

/** تحليل إطار فيديو: وجوه، تعابير، لقطات مُقيّدة */
async function analyzeVideoFrameOnce() {
    if (!examCameraUnlocked || !cameraStream || !faceModelsReady || typeof faceapi === "undefined") {
        return;
    }
    if (!monitorVideo || monitorVideo.readyState < 2) {
        return;
    }
    try {
        const detections = await faceapi
            .detectAllFaces(
                monitorVideo,
                new faceapi.TinyFaceDetectorOptions({ inputSize: 320, scoreThreshold: 0.38 })
            )
            .withFaceLandmarks(true)
            .withFaceExpressions();

        const n = detections.length;
        let emotionHint = "";

        if (n === 0) {
            absentFaceStreak += 1;
            gazeAwayStreak = 0;
            emotionHint = mon("noFace", "لم يُرصد وجه أمام الكاميرا");
            updateCameraFooter(true, emotionHint);
            if (absentFaceStreak >= 4 && canThrottleSnapshot("NO_FACE_STREAK", SNAPSHOT_THROTTLE_MS.NO_FACE_STREAK)) {
                const detail = `${emotionHint} (تكرار بدون وجه قبل اللقطة)`;
                showWarning(mon("faceAbsent", "يُشتبه بغياب الوجه أمام الكاميرا؛ بقِ أمام الشاشة"));
                await logEventWithSnapshot("CAMERA_FACE_ABSENT", "HIGH", detail);
            }
            return;
        }

        absentFaceStreak = 0;

        if (n >= 2 && canThrottleSnapshot("MULTIPLE_FACES", SNAPSHOT_THROTTLE_MS.MULTIPLE_FACES)) {
            let summary = {};
            detections.slice(0, 3).forEach((d, i) => {
                const { label, score } = dominantExpression(d.expressions);
                summary[`face_${i + 1}`] = { dominant: label, score: Number(score.toFixed(3)) };
            });
            const detail = `عدد الوجوه: ${n} | ملخص أول ${Math.min(n, 3)} وجوه: ${JSON.stringify(summary)}`;
            showWarning(mon("multipleFaces", "رصد أكثر من وجه أمام الكاميرا؛ يُعتبر مخالفاً محتملاً"));
            await logEventWithSnapshot("MULTIPLE_FACES_DETECTED", "HIGH", detail);
        }

        const main = detections.reduce((best, cur) => {
            const bw = cur.detection.box.width * cur.detection.box.height;
            const aw = best.detection.box.width * best.detection.box.height;
            return bw >= aw ? cur : best;
        }, detections[0]);

        if (main.landmarks) {
            const box = main.detection.box;
            const leftEye = main.landmarks.getLeftEye();
            const rightEye = main.landmarks.getRightEye();
            const nose = main.landmarks.getNose();

            if (leftEye.length && rightEye.length && nose.length) {
                const eyeMidX =
                    (leftEye.reduce((s, p) => s + p.x, 0) / leftEye.length +
                        rightEye.reduce((s, p) => s + p.x, 0) / rightEye.length) /
                    2;
                const eyeMidY =
                    (leftEye.reduce((s, p) => s + p.y, 0) / leftEye.length +
                        rightEye.reduce((s, p) => s + p.y, 0) / rightEye.length) /
                    2;
                const noseCenter = nose[Math.floor(nose.length / 2)];
                const faceCenterX = box.x + box.width / 2;
                const faceCenterY = box.y + box.height / 2;

                const yawRatio = (noseCenter.x - faceCenterX) / Math.max(1, box.width);
                const pitchRatio = (noseCenter.y - faceCenterY) / Math.max(1, box.height);
                const eyeDriftRatio = (eyeMidX - faceCenterX) / Math.max(1, box.width);
                const verticalEyeRatio = (eyeMidY - faceCenterY) / Math.max(1, box.height);

                const gazeAway =
                    Math.abs(yawRatio) > 0.12 ||
                    Math.abs(eyeDriftRatio) > 0.11 ||
                    Math.abs(pitchRatio) > 0.16 ||
                    verticalEyeRatio > 0.12;

                if (gazeAway) {
                    gazeAwayStreak += 1;
                    if (gazeAwayStreak >= 1 && canThrottleSnapshot("GAZE_AWAY", 20000)) {
                        const approxSec = Math.max(1, Math.round((gazeAwayStreak * faceAnalysisMs) / 1000));
                        const detail = `انحراف وجه/نظرة خارج الشاشة ~${approxSec}s | yaw=${yawRatio.toFixed(
                            3
                        )} eyeDrift=${eyeDriftRatio.toFixed(3)} pitch=${pitchRatio.toFixed(3)}`;
                        showWarning(mon("gazeAway", "تم رصد انحراف النظر خارج الشاشة لفترة متصلة"));
                        await logEventWithSnapshot("GAZE_AWAY_PROLONGED", "HIGH", detail);
                    }
                } else {
                    gazeAwayStreak = 0;
                }
            }
        }

        const { label, score } = dominantExpression(main.expressions);
        const arLabel = exprLabel(label);

        emotionHint = mon(
            "emotionHint",
            "التعبير المرجّح: {0} ({1}٪)، وجوه: {2}",
            arLabel,
            (score * 100).toFixed(0),
            n
        );

        const stress = stressLevelFromDominant(label, score);
        if (stress.stress && canThrottleSnapshot("STRESS_EXPRESSION", SNAPSHOT_THROTTLE_MS.STRESS_EXPRESSION)) {
            const exprPayload = Object.fromEntries(
                Object.entries(main.expressions).map(([k, v]) => [k, Number(v.toFixed(3))])
            );
            const detail = `مؤشر توتر/سلوك غير مطمئن (مرجع تقني): ${stress.reason}=${score.toFixed(3)} | جميع التعابير: ${JSON.stringify(
                exprPayload
            )}`;
            showWarning(mon("stressExpr", "تعبير وجه غير مطمئن مسجّل للمراجعة"));
            await logEventWithSnapshot("FACE_STRESS_OR_SUSPICIOUS_EXPR", "MEDIUM", detail);
        }

        updateCameraFooter(true, emotionHint);
    } catch (e) {
        /* إطار فاشل — لا نوقف الاختبار */
    }
}

function startFaceMonitor() {
    stopFaceMonitor();
    if (!faceModelsReady) {
        updateCameraFooter(true, mon("faceUnavailable", "تحليل الوجه غير متاح (تعذر تحميل النماذج)"));
        return;
    }
    void analyzeVideoFrameOnce();
    faceMonitorTimer = window.setInterval(() => {
        appendEvidenceFrame();
        void analyzeVideoFrameOnce();
    }, faceAnalysisMs);
}

async function unlockExamUiAfterCamera() {
    if (examCameraUnlocked) {
        return;
    }
    examCameraUnlocked = true;
    document.body.classList.remove("exam-camera-locked");
    document.dispatchEvent(new Event("jadeer-camera-ready"));
    cameraLockOverlay?.classList.add("hidden");
    if (cameraBanner) {
        cameraBanner.classList.remove("hidden");
        cameraBanner.setAttribute("aria-hidden", "false");
    }
    const loaded = await loadFaceModels();
    if (loaded) {
        startFaceMonitor();
    } else {
        updateCameraFooter(true, null);
    }
}

async function startCamera() {
    if (cameraStream) {
        await unlockExamUiAfterCamera();
        return;
    }
    if (cameraStartInProgress) {
        return;
    }
    cameraStartInProgress = true;
    if (cameraLockActionBtn) {
        cameraLockActionBtn.disabled = true;
        cameraLockActionBtn.textContent = mon("loadingAnalysis", "جاري تفعيل الكاميرا…");
    }
    try {
        if (!navigator.mediaDevices?.getUserMedia) {
            if (cameraStateText) {
                cameraStateText.textContent = mon("cameraUnsupported", "الكاميرا: المتصفح لا يدعم الوصول");
            }
            if (cameraLockDetail) {
                cameraLockDetail.textContent = mon(
                    "cameraUnsupportedDetail",
                    "المتصفح لا يدعم الوصول للكاميرا. جرّب متصفحاً حديثاً (Chrome أو Edge) على http://127.0.0.1 أو HTTPS."
                );
            }
            return;
        }
        try {
            cameraStream = await navigator.mediaDevices.getUserMedia({
                video: { facingMode: "user" },
                audio: {
                    echoCancellation: false,
                    noiseSuppression: false,
                    autoGainControl: false
                }
            });
        } catch (firstErr) {
            // فيديو أولاً ثم نضيف الصوت من ميكروفون المراقبة المستقل
            cameraStream = await navigator.mediaDevices.getUserMedia({
                video: { facingMode: "user" },
                audio: false
            });
        }
        // إن وُجد فيديو بلا صوت: اربط مسار صوت للملاحظات (فيديو+صوت)
        if (cameraStream && cameraStream.getAudioTracks().length === 0) {
            try {
                const micOnly = await acquireDedicatedAudioStream();
                if (micOnly) {
                    micOnly.getAudioTracks().forEach((t) => cameraStream.addTrack(t));
                    if (!audioMonitorStream) {
                        audioMonitorStream = micOnly;
                        audioUsesDedicatedStream = true;
                    }
                }
            } catch (_) { /* الميكروفون اختياري إن رُفض */ }
        }
        if (monitorVideo) {
            monitorVideo.srcObject = cameraStream;
            await monitorVideo.play().catch(() => {});
        }
        updateCameraFooter(true, mon("loadingAnalysis", "جاري تحميل تحليل الوجه والصوت…"));
        const firstImage = captureCompressedJpeg();
        if (firstImage)
            void trackCapture(monitoringPost("/monitoring/snapshot", {
                eventType: "CAMERA_READY_SNAPSHOT", severity: "LOW",
                details: "لقطة بدء المراقبة", imageBase64: firstImage
            }));
        try {
            await logEvent("CAMERA_ENABLED", "LOW", "تم تفعيل كاميرا ومراقبة الصوت بموافقة المستخدم");
        } catch (_) { /* لا تمنع فتح الاختبار */ }
        await unlockExamUiAfterCamera();
        await startAudioMonitor(cameraStream);
    } catch (e) {
        if (cameraStateText) {
            cameraStateText.textContent = mon("cameraDenied", "الكاميرا: رفضت الصلاحية أو تعذّر الوصول");
        }
        if (cameraLockDetail) {
            cameraLockDetail.textContent = mon(
                "cameraDeniedDetail",
                "يجب السماح بالكاميرا من أيقونة القفل في شريط العنوان ثم إعادة المحاولة. بدون الكاميرا لا يُسمح بأداء الاختبار."
            );
        }
        try {
            await logEvent("CAMERA_DENIED", "HIGH", "رفض أو فشل الوصول للكاميرا أثناء الإلزام");
        } catch (_) { /* ignore */ }
        showWarning(mon("cameraRequired", "الكاميرا مطلوبة — رجاءً اسمح بالوصول ثم اضغط «السماح بالكاميرا»"));
    } finally {
        cameraStartInProgress = false;
        if (cameraLockActionBtn && !examCameraUnlocked) {
            cameraLockActionBtn.disabled = false;
            cameraLockActionBtn.textContent = mon("cameraAllowBtn", "السماح بالكاميرا والمتابعة");
        }
    }
}

function stopCamera() {
    clearInterval(periodicSnapshotTimer);
    stopFaceMonitor();
    stopAudioMonitor();
    if (!cameraStream) {
        return;
    }
    cameraStream.getTracks().forEach((t) => t.stop());
    cameraStream = null;
    if (monitorVideo) {
        monitorVideo.srcObject = null;
    }
}

document.addEventListener("visibilitychange", () => {
    if (!document.hidden && audioMonitorReady) {
        void ensureAudioContextRunning();
        restartAudioMonitorTimer();
    }
    if (document.hidden) {
        showWarning(mon("tabSwitch", "تم اكتشاف انتقال إلى نافذة أخرى"));
        void logBehaviorEvent("TAB_SWITCH", "HIGH", "تم اكتشاف انتقال الممتحن إلى نافذة أخرى أو تصغير المتصفح", true);
    }
});

document.addEventListener(
    "pointerdown",
    () => {
        if (examCameraUnlocked) {
            void ensureAudioContextRunning();
        }
    },
    { passive: true }
);

window.addEventListener("blur", () => {
    showWarning(mon("focusLoss", "تم فقدان التركيز من النافذة"));
    void logBehaviorEvent("FOCUS_LOSS", "MEDIUM", "تم فقدان التركيز عن نافذة الاختبار", true);
});

document.addEventListener("copy", (e) => {
    e.preventDefault();
    showWarning(mon("copyBlocked", "النسخ غير مسموح أثناء الاختبار"));
    void logBehaviorEvent("COPY_BLOCKED", "MEDIUM", "تم حظر محاولة نسخ خلال الجلسة الاختبارية", true);
});

document.addEventListener("paste", (e) => {
    e.preventDefault();
    showWarning(mon("pasteBlocked", "اللصق غير مسموح أثناء الاختبار"));
    void logBehaviorEvent("PASTE_BLOCKED", "MEDIUM", "تم حظر محاولة لصق خلال الجلسة الاختبارية", true);
});

document.addEventListener("contextmenu", (e) => {
    e.preventDefault();
    showWarning(mon("rightClickBlocked", "القائمة اليمنى محظورة"));
    void logEventWithSnapshot("RIGHT_CLICK_BLOCKED", "LOW", "تم حظر استخدام القائمة اليمنى");
});

document.addEventListener("keydown", (e) => {
    const printCombo = e.key === "p" && (e.ctrlKey || e.metaKey);
    if (printCombo) {
        e.preventDefault();
        void logBehaviorEvent(
            "PRINT_ATTEMPT",
            "MEDIUM",
            "محاولة طباعة أو اختصار الطباعة أثناء الاختبار",
            true
        );
        showWarning(mon("printBlocked", "الطباعة غير مسموحة أثناء الاختبار"));
        return;
    }
    const blocked = e.key === "F12" || (e.ctrlKey && e.shiftKey && ["I", "J", "C"].includes(e.key.toUpperCase()));
    if (blocked) {
        e.preventDefault();
        showWarning(mon("devtools", "محاولة فتح أدوات المطور تم تسجيلها"));
        void logBehaviorEvent("DEVTOOLS_ATTEMPT", "HIGH", "تم رصد محاولة فتح أدوات المطور", true);
    }
});

document.addEventListener("cut", (e) => {
    e.preventDefault();
    void logBehaviorEvent("CUT_BLOCKED", "LOW", "تم حظر قص المحتوى", true);
});

window.addEventListener("beforeprint", () => {
    void logBehaviorEvent("PRINT_ATTEMPT", "MEDIUM", "ظهور محاولة الطباعة في المتصفح", true);
    showWarning(mon("printBlocked", "الطباعة غير مسموحة أثناء الاختبار"));
});

document.addEventListener("dragstart", (e) => e.preventDefault());

setInterval(autoSave, 30000);

setInterval(runHeartbeat, 15000);

setInterval(() => void monitoringOutbox.flush(), 60000);

window.addEventListener("online", () => void monitoringOutbox.flush());

submitButtons.forEach((btn) => btn.addEventListener("click", submitExam));

cameraLockActionBtn?.addEventListener("click", (e) => {
    e.preventDefault();
    e.stopPropagation();
    void startCamera();
});

window.__jadeerStartCamera = () => {
    void startCamera();
};

enableCameraBtn?.addEventListener("click", (e) => {
    e.preventDefault();
    if (!cameraStream) {
        void startCamera();
        return;
    }
    monitorVideo?.play?.().catch(() => {});
});

window.addEventListener("beforeunload", (e) => {
    if (examCameraUnlocked && !examSubmitted) {
        e.preventDefault();
        e.returnValue = "";
    }
});

window.addEventListener("pagehide", stopCamera);

// لا نطلب ملء الشاشة تلقائياً — قد يقطع تفاعل زر الكاميرا في بعض المتصفحات

(async function bootExamMonitor() {
    const cfgPromise = fetchMonitoringClientConfig();
    const proceed = await initExamSession();
    if (!proceed) {
        return;
    }
    applyClientConfig(await cfgPromise);
    if (audioMonitorTimer) {
        restartAudioMonitorTimer();
    }
    startRiskDecay();
    renderRiskHud();
    startPeriodicSnapshots();
    cameraLockOverlay?.classList.remove("hidden");
    // الكاميرا تُفعَّل فقط بعد ضغط المستخدم (متطلب المتصفح لصلاحية getUserMedia)
})();
