const panels = new WeakMap();
const MAX_BYTES = 25 * 1024 * 1024;
export function validateFiles(files) {
    if (!files.length || files.length > 20) throw new Error("اختر من ملف واحد إلى 20 ملفًا في المرة الواحدة.");
    for (const file of files) {
        if (file.size > MAX_BYTES) throw new Error(`الملف ${file.name} يتجاوز 25 ميجابايت.`);
        if (!file.name || /[\\/\x00-\x1f]/.test(file.name) || file.name === "." || file.name === "..") throw new Error("اسم الملف غير صالح.");
    }
}
export function entrySegments(context) {
    if (!/^proacc-[a-f0-9]{16}$/.test(context.companyFolder) || !/^\d+-\d+$/.test(context.entryFolder)) throw new Error("تعذر تحديد مجلد القيد.");
    return [context.companyFolder, context.entryFolder];
}
export async function entryDirectory(root, context, create = false) {
    let current = root;
    for (const segment of entrySegments(context)) {
        try { current = await current.getDirectoryHandle(segment, { create }); }
        catch (error) { if (!create && error.name === "NotFoundError") return null; throw error; }
    }
    return current;
}
export function uniqueName(name) {
    const split = name.lastIndexOf(".");
    const stem = split > 0 ? name.slice(0, split) : name;
    const ext = split > 0 ? name.slice(split) : "";
    return `${stem}-${crypto.randomUUID()}${ext}`;
}
function userError(error) {
    if (error.name === "AbortError") return "تم إلغاء الاختيار.";
    if (error.name === "NotAllowedError" || error.name === "SecurityError") return "لم يُمنح إذن الوصول للمجلد. اضغط اختيار المجلد واسمح بالوصول، أو افتح الموقع في Chrome أو Edge على الكمبيوتر.";
    if (error.name === "NotFoundError") return "لم يعد الملف أو المجلد موجودًا. اختر المجلد مجددًا.";
    return error.message || "تعذر الوصول إلى المرفقات المحلية.";
}
export function unmount(host) {
    const state = panels.get(host);
    if (state) { state.closed = true; state.abort.abort(); }
    panels.delete(host);
    if (host) host.replaceChildren();
}
export function mount(host, context, callback) {
    unmount(host);
    const state = { closed: false, busy: false, root: null, abort: new AbortController() };
    panels.set(host, state);
    const element = (tag, text, cls) => { const el = document.createElement(tag); if (text) el.textContent = text; if (cls) el.className = cls; return el; };
    host.append(element("p", "تُحفظ الملفات على جهازك فقط، ولا تُرفع إلى السيرفر. اختر المجلد المعرّف في إعدادات المنشأة وامنح المتصفح إذن الوصول إليه."));
    host.append(element("small", "المجلد المعرّف في إعدادات المنشأة:"));
    const configured = element("p", context.configuredPath, "text-break"); configured.dir = "ltr"; host.append(configured);
    host.append(element("small", "يطلب المتصفح اختيار المجلد عند فتح المرفقات. لا يمكن للموقع التحقق من المسار الكامل؛ تأكد من اختيار المجلد الصحيح."));
    const actions = element("div", "", "local-attachment-actions");
    const choose = element("button", "اختيار مجلد المرفقات", "btn btn-primary btn-sm"); choose.type = "button";
    const uploadLabel = element("label", "إضافة ملفات من الجهاز", "btn btn-outline-primary btn-sm local-attachment-upload");
    const input = element("input"); input.type = "file"; input.multiple = true; input.setAttribute("aria-label", "إضافة ملفات من الجهاز"); uploadLabel.append(input);
    const refresh = element("button", "تحديث القائمة", "btn btn-outline-secondary btn-sm"); refresh.type = "button";
    actions.append(choose, uploadLabel, refresh); host.append(actions);
    const selected = element("p", "لم يتم اختيار مجلد بعد.", "text-break"); host.append(selected);
    const status = element("div", "", "alert alert-info"); status.setAttribute("role", "status"); host.append(status);
    const list = element("ul", "", "list-group"); host.append(list);
    const supported = window.isSecureContext && typeof window.showDirectoryPicker === "function";
    function message(text, bad = false) { if (state.closed) return; status.textContent = text; status.className = bad ? "alert alert-danger" : "alert alert-info"; }
    function controls() {
        choose.disabled = state.busy || !supported;
        input.disabled = state.busy || !state.root;
        refresh.disabled = state.busy || !state.root;
        for (const button of list.querySelectorAll("button")) button.disabled = state.busy;
        uploadLabel.classList.toggle("disabled", input.disabled);
    }
    async function authorize() {
        const current = await callback.invokeMethodAsync("AuthorizeLocalOperation");
        if (state.closed) throw new DOMException("Closed", "AbortError");
        if (current.scope !== context.scope || current.entryFolder !== context.entryFolder || current.companyFolder !== context.companyFolder) throw new Error("تغيرت المنشأة أو إعداداتها. أغلق المرفقات وافتحها مجددًا.");
    }
    async function run(action) {
        if (state.busy || state.closed) return;
        state.busy = true; controls(); message("جاري معالجة الملفات على جهازك...");
        try { await action(); } catch (error) { message(userError(error), true); }
        finally { state.busy = false; if (!state.closed) controls(); }
    }
    async function showFiles() {
        const directory = await entryDirectory(state.root, context);
        const names = [];
        if (directory) for await (const [name, handle] of directory.entries()) { if (handle.kind === "file") names.push(name); }
        if (state.closed) return;
        list.replaceChildren();
        for (const name of names.sort((a,b) => a.localeCompare(b))) {
            const item = element("li", "", "list-group-item d-flex justify-content-between align-items-center gap-3");
            const button = element("button", "تنزيل نسخة", "btn btn-outline-primary btn-sm"); button.type = "button";
            button.addEventListener("click", () => run(async () => {
                await authorize();
                const handle = await directory.getFileHandle(name);
                const file = await handle.getFile();
                if (file.size > MAX_BYTES) throw new Error("حجم الملف يتجاوز 25 ميجابايت.");
                if (state.closed) return;
                const url = URL.createObjectURL(file);
                const anchor = element("a"); anchor.href = url; anchor.download = name; anchor.click();
                setTimeout(() => URL.revokeObjectURL(url), 60000);
                message("تم تجهيز نسخة الملف للتنزيل من جهازك.");
            }), {signal:state.abort.signal});
            item.append(element("span", name, "text-break"), button); list.append(item);
        }
        message(names.length ? `عدد المرفقات: ${names.length}` : "لا توجد مرفقات محلية لهذا القيد في المجلد المختار.");
    }
    choose.addEventListener("click", () => {
        if (state.busy || !supported) return;
        // Invoke the picker directly in the native click event, before any server round trip.
        const picking = window.showDirectoryPicker({mode:"readwrite",id:"proacc-attachments"});
        run(async () => {
            const root = await picking;
            await authorize();
            state.root = root;
            selected.textContent = `${root.name} / ${context.companyFolder} / ${context.entryFolder}`;
            selected.dir = "ltr";
            await showFiles();
        });
    }, {signal:state.abort.signal});
    refresh.addEventListener("click", () => run(async () => { await authorize(); await showFiles(); }), {signal:state.abort.signal});
    input.addEventListener("change", () => {
        const files = Array.from(input.files || []);
        if (!files.length) return;
        run(async () => {
            let saved = 0;
            try {
                validateFiles(files);
                await authorize();
                const directory = await entryDirectory(state.root, context, true);
                for (const file of files) {
                    if (state.closed) break;
                    // A unique name prevents replacing an existing attachment, including another tab's file.
                    const name = uniqueName(file.name);
                    const handle = await directory.getFileHandle(name, {create:true});
                    try { const writable = await handle.createWritable(); await file.stream().pipeTo(writable, {signal:state.abort.signal}); saved++; }
                    catch(error) { await directory.removeEntry(name).catch(() => {}); throw error; }
                }
                await showFiles();
                message(`تم حفظ ${saved} ملف على جهازك.`);
            } catch(error) { message(`${saved ? `تم حفظ ${saved} ملف. ` : ""}${userError(error)}`, true); }
            finally { input.value = ""; }
        });
    }, {signal:state.abort.signal});
    controls();
    message(supported ? "اختر مجلد المرفقات للبدء. الحد الأقصى 20 ملفًا، و25 ميجابايت لكل ملف." : "هذا المتصفح لا يدعم حفظ المرفقات في مجلد محلي. افتح الموقع عبر HTTPS في Chrome أو Edge على الكمبيوتر.", !supported);
}
