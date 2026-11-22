const monitoringOutbox = (() => {
    const IDB_NAME = "rc_smart_exam_monitor";
    const IDB_STORE = "outbox";
    const IDB_VER = 2;
    const monitorUser = document.querySelector("[data-monitor-user]")?.dataset.monitorUser;
    let outboxFlush = null;

    function openMonitorDb() {
        return new Promise((resolve, reject) => {
            const req = indexedDB.open(IDB_NAME, IDB_VER);
            req.onerror = () => reject(req.error);
            req.onblocked = () => reject(new Error("Monitoring storage is busy."));
            req.onsuccess = () => resolve(req.result);
            req.onupgradeneeded = () => {
                const db = req.result;
                const store = db.objectStoreNames.contains(IDB_STORE)
                    ? req.transaction.objectStore(IDB_STORE)
                    : db.createObjectStore(IDB_STORE, { keyPath: "id" });
                store.createIndex("ownerCreatedAt", ["owner", "createdAt"]);
            };
        });
    }

    async function outboxAdd(row) {
        const db = await openMonitorDb();
        try {
            await new Promise((resolve, reject) => {
                const tx = db.transaction(IDB_STORE, "readwrite");
                tx.objectStore(IDB_STORE).add({ ...row, owner: monitorUser, createdAt: Date.now() });
                tx.oncomplete = () => resolve();
                tx.onabort = () => reject(tx.error);
            });
        } finally {
            db.close();
        }
    }

    async function outboxListSorted() {
        const db = await openMonitorDb();
        const rows = await new Promise((resolve, reject) => {
            const tx = db.transaction(IDB_STORE, "readonly");
            const range = IDBKeyRange.bound([monitorUser, 0], [monitorUser, Infinity]);
            const r = tx.objectStore(IDB_STORE).index("ownerCreatedAt").getAll(range, 8);
            r.onsuccess = () => resolve(r.result || []);
            r.onerror = () => reject(r.error);
        });
        db.close();
        return rows;
    }

    async function outboxDelete(id) {
        const db = await openMonitorDb();
        await new Promise((resolve, reject) => {
            const tx = db.transaction(IDB_STORE, "readwrite");
            tx.objectStore(IDB_STORE).delete(id);
            tx.oncomplete = () => resolve();
            tx.onerror = () => reject(tx.error);
        });
        db.close();
    }

    function flushMonitoringOutbox() {
        if (!monitorUser) return Promise.resolve();
        outboxFlush ??= drainMonitoringOutbox().finally(() => { outboxFlush = null; });
        return outboxFlush;
    }

    async function drainMonitoringOutbox() {
        try {
            while (true) {
                const rows = await outboxListSorted();
                if (rows.length === 0) return;
                for (const row of rows) {
                    const r = await fetch(row.url, {
                        method: "POST",
                        headers: { "Content-Type": "application/json" },
                        signal: AbortSignal.timeout(30000),
                        body: row.payload
                    });
                    if (r.redirected) return;
                    if (r.ok && row.url === "/monitoring/snapshot") {
                        const result = await r.json().catch(() => null);
                        if (result?.status === "partial")
                            window.dispatchEvent(new Event("monitoring-upload-rejected"));
                    }
                    if (!r.ok) {
                        if (![400, 403, 413, 415, 422].includes(r.status)) return;
                        window.dispatchEvent(new Event("monitoring-upload-rejected"));
                    }
                    await outboxDelete(row.id);
                }
            }
        } catch {
            // Leave pending rows for the next retry.
        }
    }

    if (document.querySelector("[data-monitor-outbox-status]")) {
        window.addEventListener("monitoring-upload-rejected", () => {
            document.querySelector("[data-monitor-outbox-status]").hidden = false;
        });
        window.addEventListener("online", () => void flushMonitoringOutbox());
        window.setInterval(() => void flushMonitoringOutbox(), 15000);
        void flushMonitoringOutbox();
    }

    return { add: outboxAdd, flush: flushMonitoringOutbox };
})();
