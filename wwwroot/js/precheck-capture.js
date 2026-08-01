const precheckVideo = document.getElementById("precheckVideo");
const precheckCanvas = document.getElementById("precheckCanvas");
const faceGuideText = document.getElementById("faceGuideText");
const captureFaceBtn = document.getElementById("captureFaceBtn");
const captureIdBtn = document.getElementById("captureIdBtn");
const enablePrecheckCameraBtn = document.getElementById("enablePrecheckCameraBtn");
const captureStatusText = document.getElementById("captureStatusText");
const captureErrorText = document.getElementById("captureErrorText");
const faceImageBase64Input = document.getElementById("faceImageBase64");
const idCardImageBase64Input = document.getElementById("idCardImageBase64");
const submitPrecheckBtn = document.getElementById("submitPrecheckBtn");

const MSG = window.__RC_PRECHECK || {};

let streamRef = null;
let faceCheckTimer = null;
let faceAligned = false;
let faceCaptured = false;
let idCaptured = false;
let faceGuidanceFallback = false;
let cameraReady = false;
let guidanceStarted = false;

function setError(msg) {
    if (!captureErrorText) {
        return;
    }
    if (!msg) {
        captureErrorText.textContent = "";
        captureErrorText.classList.add("hidden");
        return;
    }
    captureErrorText.textContent = msg;
    captureErrorText.classList.remove("hidden");
}

function updateSubmitState() {
    const agree = document.querySelector("input[name='agree']");
    const agreed = agree ? agree.checked : false;
    if (submitPrecheckBtn) {
        submitPrecheckBtn.disabled = !(cameraReady && faceCaptured && idCaptured && agreed);
    }
}

function captureFrameBase64() {
    if (!precheckVideo || !precheckCanvas) {
        return null;
    }
    const vw = precheckVideo.videoWidth;
    const vh = precheckVideo.videoHeight;
    if (!vw || !vh) {
        return null;
    }
    precheckCanvas.width = vw;
    precheckCanvas.height = vh;
    const ctx = precheckCanvas.getContext("2d");
    ctx.drawImage(precheckVideo, 0, 0, vw, vh);
    return precheckCanvas.toDataURL("image/jpeg", 0.88);
}

async function startCamera() {
    if (!navigator.mediaDevices?.getUserMedia) {
        throw new Error("unsupported");
    }
    if (streamRef) {
        return;
    }
    streamRef = await navigator.mediaDevices.getUserMedia({
        video: { facingMode: "user" },
        audio: false
    });
    if (precheckVideo) {
        precheckVideo.srcObject = streamRef;
        await precheckVideo.play().catch(() => {});
    }
    cameraReady = true;
    setError("");
    if (captureStatusText) {
        captureStatusText.textContent = MSG.cameraReady || "الكاميرا جاهزة. ضع وجهك في منتصف الإطار.";
    }
    if (enablePrecheckCameraBtn) {
        enablePrecheckCameraBtn.disabled = true;
        enablePrecheckCameraBtn.classList.add("is-done");
    }
}

async function startFaceGuidance() {
    if (guidanceStarted) {
        return;
    }
    guidanceStarted = true;
    if (!captureFaceBtn || !faceGuideText) {
        return;
    }
    if (typeof faceapi === "undefined") {
        faceGuidanceFallback = true;
        faceAligned = true;
        captureFaceBtn.disabled = false;
        faceGuideText.textContent = MSG.faceManual || "التقط صورة وجه واضحة خلال 30 ثانية.";
        return;
    }
    try {
        await faceapi.nets.tinyFaceDetector.loadFromUri("https://cdn.jsdelivr.net/npm/face-api.js@0.22.2/weights");
    } catch (_) {
        faceGuidanceFallback = true;
        faceAligned = true;
        captureFaceBtn.disabled = false;
        faceGuideText.textContent = MSG.modelFail || "تعذر تحميل النموذج. التقط صورة وجه واضحة يدويًا.";
        setError(MSG.modelFailError || "تعذر تحميل النموذج");
        return;
    }
    faceCheckTimer = window.setInterval(async () => {
        if (!precheckVideo || precheckVideo.readyState < 2 || faceCaptured) {
            return;
        }
        let detection = null;
        try {
            detection = await faceapi.detectSingleFace(
                precheckVideo,
                new faceapi.TinyFaceDetectorOptions({ inputSize: 320, scoreThreshold: 0.4 })
            );
        } catch (_) {
            detection = null;
        }
        if (!detection) {
            faceAligned = false;
            captureFaceBtn.disabled = true;
            faceGuideText.textContent = MSG.faceCenter || "يجب أن يكون الوجه واضحًا في منتصف الإطار.";
            return;
        }
        const vw = precheckVideo.videoWidth || 1;
        const vh = precheckVideo.videoHeight || 1;
        const box = detection.box;
        const sizeRatio = (box.width * box.height) / (vw * vh);
        const centerX = box.x + box.width / 2;
        const centerY = box.y + box.height / 2;
        const dx = Math.abs(centerX - vw / 2) / vw;
        const dy = Math.abs(centerY - vh / 2) / vh;
        const sizeOk = sizeRatio >= 0.08 && sizeRatio <= 0.45;
        const centerOk = dx <= 0.16 && dy <= 0.20;
        faceAligned = sizeOk && centerOk;
        captureFaceBtn.disabled = !faceAligned;
        faceGuideText.textContent = faceAligned
            ? (MSG.faceAligned || "الوجه واضح — يمكنك التقاط صورة الوجه.")
            : (MSG.faceNotAligned || "صورة الوجه يجب أن تكون واضحة وفي منتصف الإطار.");
    }, 900);
}

async function enableCameraAndGuide() {
    try {
        await startCamera();
        await startFaceGuidance();
        updateSubmitState();
    } catch (_) {
        cameraReady = false;
        if (captureStatusText) {
            captureStatusText.textContent = MSG.cameraFail || "تعذر تشغيل الكاميرا";
        }
        setError(MSG.cameraFailError || "اضغط «السماح بالكاميرا» واختر Allow من المتصفح.");
        if (enablePrecheckCameraBtn) {
            enablePrecheckCameraBtn.disabled = false;
        }
    }
}

captureFaceBtn?.addEventListener("click", () => {
    const image = captureFrameBase64();
    if (!image || (!faceAligned && !faceGuidanceFallback)) {
        setError(MSG.faceUnclear || "صورة الوجه غير واضحة. أعد المحاولة.");
        return;
    }
    faceImageBase64Input.value = image;
    faceCaptured = true;
    captureFaceBtn.disabled = true;
    captureFaceBtn.classList.add("is-done");
    captureIdBtn.disabled = false;
    captureStatusText.textContent = MSG.faceSaved || "تم حفظ صورة الوجه.";
    faceGuideText.textContent = MSG.idPrompt || "وجّه بطاقة الهوية نحو الكاميرا.";
    if (faceCheckTimer) {
        clearInterval(faceCheckTimer);
        faceCheckTimer = null;
    }
    updateSubmitState();
});

captureIdBtn?.addEventListener("click", () => {
    const image = captureFrameBase64();
    if (!image) {
        setError(MSG.idUnclear || "صورة الهوية غير واضحة. أعد المحاولة.");
        return;
    }
    idCardImageBase64Input.value = image;
    idCaptured = true;
    captureIdBtn.disabled = true;
    captureIdBtn.classList.add("is-done");
    captureStatusText.textContent = MSG.idSaved || "تم حفظ صورة الهوية.";
    setError("");
    updateSubmitState();
});

document.querySelector("input[name='agree']")?.addEventListener("change", updateSubmitState);

enablePrecheckCameraBtn?.addEventListener("click", (e) => {
    e.preventDefault();
    void enableCameraAndGuide();
});

(async function initPrecheckCapture() {
    // محاولة تلقائية؛ إن فشل المتصفح (بدون ضغطة مستخدم) يظهر زر السماح
    await enableCameraAndGuide();
})();

window.addEventListener("beforeunload", () => {
    if (faceCheckTimer) {
        clearInterval(faceCheckTimer);
    }
    if (streamRef) {
        streamRef.getTracks().forEach((t) => t.stop());
    }
});
