// Small JS helpers for the email designer.

export async function downloadFromStream(fileName, contentType, streamRef) {
    const buffer = await streamRef.arrayBuffer();
    const blob = new Blob([buffer], { type: contentType });
    const url = URL.createObjectURL(blob);
    const a = document.createElement("a");
    a.href = url;
    a.download = fileName ?? "download";
    document.body.appendChild(a);
    a.click();
    a.remove();
    setTimeout(() => URL.revokeObjectURL(url), 1000);
}

export async function copyText(text) {
    await navigator.clipboard.writeText(text);
}

let keyHandler = null;

// Firefox won't start an HTML5 drag unless dataTransfer has data; Blazor can't set it, so we do.
function onDragStart(e) {
    const el = e.target instanceof Element ? e.target.closest("[draggable=true]") : null;
    if (el && e.dataTransfer) {
        e.dataTransfer.effectAllowed = "move";
        try { e.dataTransfer.setData("text/plain", ""); } catch { /* ignore */ }
    }
}

function isTyping(target) {
    return target instanceof HTMLElement &&
        (target.isContentEditable || ["INPUT", "TEXTAREA", "SELECT"].includes(target.tagName));
}

export function registerShortcuts(dotnet) {
    unregisterShortcuts();
    keyHandler = (e) => {
        if (isTyping(e.target)) return;
        const mod = e.ctrlKey || e.metaKey;
        const key = e.key.toLowerCase();
        if (mod && key === "z" && !e.shiftKey) { e.preventDefault(); dotnet.invokeMethodAsync("Undo"); }
        else if (mod && (key === "y" || (key === "z" && e.shiftKey))) { e.preventDefault(); dotnet.invokeMethodAsync("Redo"); }
        else if (e.key === "Delete" || e.key === "Backspace") { e.preventDefault(); dotnet.invokeMethodAsync("DeleteSelected"); }
    };
    document.addEventListener("keydown", keyHandler);
    document.addEventListener("dragstart", onDragStart, true);
}

export function unregisterShortcuts() {
    if (keyHandler) document.removeEventListener("keydown", keyHandler);
    document.removeEventListener("dragstart", onDragStart, true);
    keyHandler = null;
}
