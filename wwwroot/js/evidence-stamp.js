(() => {
    const formatter = new Intl.DateTimeFormat("en-GB", {
        timeZone: "Asia/Riyadh",
        year: "numeric", month: "2-digit", day: "2-digit",
        hour: "2-digit", minute: "2-digit", second: "2-digit", hourCycle: "h23"
    });

    function draw(canvas, video, identity, maxWidth) {
        if (video.readyState < 2 || !video.videoWidth || !video.videoHeight) return false;
        const scale = Math.min(1, maxWidth / video.videoWidth);
        const width = Math.round(video.videoWidth * scale);
        const height = Math.round(video.videoHeight * scale);
        if (canvas.width !== width) canvas.width = width;
        if (canvas.height !== height) canvas.height = height;
        const ctx = canvas.getContext("2d");
        if (!ctx) return false;
        ctx.drawImage(video, 0, 0, width, height);

        const size = Math.max(11, Math.min(20, Math.floor(width / 34), Math.floor(height / 8)));
        const pad = Math.max(4, Math.floor(size / 2));
        const lineHeight = Math.ceil(size * 1.22);
        const panelHeight = lineHeight * 3 + 2 * pad;
        ctx.fillStyle = "rgba(12, 23, 37, 0.82)";
        ctx.fillRect(0, height - panelHeight, width, panelHeight);
        ctx.fillStyle = "#fff";
        ctx.font = "600 " + size + "px Tahoma, Arial, sans-serif";
        ctx.textAlign = "right";
        ctx.textBaseline = "middle";
        const fields = [
            [identity.exam, "rtl"],
            [identity.candidate, "rtl"],
            [formatter.format(new Date()) + " (UTC+3)", "ltr"]
        ];
        fields.forEach(([label, direction], row) => {
            ctx.direction = direction;
            ctx.fillText(label, width - pad,
                height - panelHeight + pad + lineHeight * (row + 0.5), width - 2 * pad);
        });
        return true;
    }

    window.jadeerEvidence = { draw };
})();
