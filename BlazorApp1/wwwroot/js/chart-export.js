window.chartExport = {
    previewDocument(rows, report = {}) {
        const escape = value => String(value ?? '').replace(/[&<>"']/g, c => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]));
        const html = `<!doctype html><html lang="ar" dir="rtl"><head><meta charset="utf-8"><title>دليل الحسابات</title>
            <style>@page{size:A4 landscape;margin:12mm}body{font-family:Tahoma,Arial,sans-serif;color:#17233b;font-size:11px}h1{font-size:22px}table{width:100%;border-collapse:collapse}th,td{border:1px solid #dce1ec;padding:7px;text-align:right;overflow-wrap:anywhere}th{background:#eef2ff}thead{display:table-header-group}tr{break-inside:avoid}.branch{font-weight:bold;background:#f6f8ff}.number{direction:ltr;text-align:right;white-space:nowrap}</style></head><body>
            <h1>دليل الحسابات — الشجرة الكاملة</h1><p>عدد العناصر: ${rows.length} | ${escape(new Date().toLocaleDateString('ar-SA'))}</p>
            <table><thead><tr><th>المستوى</th><th>رقم الحساب</th><th>اسم الحساب</th><th>رقم الحساب الرئيسي</th><th>المسار الكامل</th></tr></thead><tbody>
            ${rows.map(r => `<tr class="${r.level < 6 ? 'branch' : ''}"><td>${escape(r.level)}</td><td class="number">${escape(r.number)}</td><td>${escape(r.name)}</td><td class="number">${escape(r.parentNumber || '—')}</td><td>${escape(r.path)}</td></tr>`).join('')}
            </tbody></table></body></html>`;
        return reportDesigner.apply(journalEntryExport.portraitDocument(html, { ...report, landscape: true }), { ...report, landscape: true });
    },
    print(rows, report = {}) { return journalEntryExport.printHtml(this.previewDocument(rows, report)); },
    downloadBase64(fileName, contentType, base64Data) {
        const blob = new Blob([Uint8Array.from(atob(base64Data), c => c.charCodeAt(0))], { type: contentType });
        const url = URL.createObjectURL(blob);
        const link = document.createElement('a');
        link.href = url; link.download = fileName;
        document.body.appendChild(link); link.click(); link.remove();
        setTimeout(() => URL.revokeObjectURL(url), 30000);
    }
};
