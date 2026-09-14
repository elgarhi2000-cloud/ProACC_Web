(function () {
    const value = (input) => input == null || input === "" ? "-" : String(input);
    const escapeHtml = (input) => value(input).replaceAll("&", "&amp;").replaceAll("<", "&lt;").replaceAll(">", "&gt;").replaceAll('"', "&quot;").replaceAll("'", "&#039;");
    const amount = (input) => Number(input || 0).toLocaleString("en-US", { minimumFractionDigits: 2, maximumFractionDigits: 2 });
    const date = (input) => input ? new Date(input).toLocaleDateString("en-GB") : "-";

    function buildDocument(report) {
        const rows = (report.rows || []).map(row => `<tr><td>${escapeHtml(row.transId)}</td><td>${escapeHtml(row.glid)}</td><td>${escapeHtml(row.serialNo)}</td><td class="nowrap">${date(row.glDate)}</td><td class="code">${escapeHtml(row.accountId)}</td><td>${escapeHtml(row.accountName)}</td><td>${escapeHtml(row.description)}</td><td class="number debit">${amount(row.debit)}</td><td class="number credit">${amount(row.credit)}</td></tr>`).join("");
        const debitTotal = (report.rows || []).reduce((total, row) => total + Number(row.debit || 0), 0);
        const creditTotal = (report.rows || []).reduce((total, row) => total + Number(row.credit || 0), 0);
        return `<!doctype html><html lang="ar" dir="rtl"><head><meta charset="utf-8"><title>اليومية العامة</title><style>
        @page{size:A4 landscape;margin:9mm}*{box-sizing:border-box}body{margin:0;color:#252b37;background:#fff;font-family:"Segoe UI",Tahoma,Arial,sans-serif;font-size:9px}.company{text-align:center;padding-bottom:8px;border-bottom:2px solid #5948d6}.company h1{margin:0 0 3px;font-size:18px}.company p{margin:2px 0;color:#68707d}.title{text-align:center;margin:9px 0 3px;color:#4437ba;font-size:15px}.period{text-align:center;margin-bottom:9px;color:#626a77}table{width:100%;border-collapse:collapse}thead{display:table-header-group}th{padding:6px 4px;color:#fff;background:#5948d6;border:1px solid #493abb;white-space:nowrap}td{padding:5px 4px;border:1px solid #dfe2e8;vertical-align:middle}tbody tr:nth-child(even) td{background:#f8f8fc}tfoot td{background:#f0effa;font-weight:800}.code,.number{direction:ltr;text-align:right;font-variant-numeric:tabular-nums}.nowrap{white-space:nowrap}.debit{color:#13795b}.credit{color:#b63f50}.footer{margin-top:7px;display:flex;justify-content:space-between;color:#7a818e}
        </style></head><body><header class="company"><h1>${escapeHtml(report.companyName)}</h1><p>${escapeHtml(report.companyAddress)}${report.companyPhone ? " | هاتف: " + escapeHtml(report.companyPhone) : ""}</p><p>${report.commercialRecord ? "السجل التجاري: " + escapeHtml(report.commercialRecord) : ""}${report.taxNumber ? " | الرقم الضريبي: " + escapeHtml(report.taxNumber) : ""}</p></header><h2 class="title">اليومية العامة</h2><div class="period">الفترة المالية: ${escapeHtml(report.periodName)} | من ${date(report.fromDate)} إلى ${date(report.toDate)}</div><table><thead><tr><th>رقم الحركة</th><th>رقم القيد</th><th>تسلسل القيد</th><th>التاريخ</th><th>رقم الحساب</th><th>اسم الحساب</th><th>الوصف</th><th>مدين</th><th>دائن</th></tr></thead><tbody>${rows || '<tr><td colspan="9" style="text-align:center">لا توجد بيانات</td></tr>'}</tbody><tfoot><tr><td colspan="7">الإجمالي</td><td class="number debit">${amount(debitTotal)}</td><td class="number credit">${amount(creditTotal)}</td></tr></tfoot></table><footer class="footer"><span>${report.rows.length} سطر</span><span>تاريخ التصدير: ${new Date().toLocaleString("ar-SA-u-ca-gregory")}</span></footer></body></html>`;
    }

    window.generalJournalExport = {
        previewDocument(report) { return reportDesigner.apply(journalEntryExport.portraitDocument(buildDocument(report), { ...report, landscape: true }), { ...report, landscape: true }); },
        print(report) { return journalEntryExport.printHtml(this.previewDocument(report)); },
        downloadBase64(fileName, contentType, base64Data) {
            const binary = atob(base64Data); const bytes = new Uint8Array(binary.length);
            for (let i = 0; i < binary.length; i++) bytes[i] = binary.charCodeAt(i);
            const url = URL.createObjectURL(new Blob([bytes], { type: contentType }));
            const link = document.createElement("a"); link.href = url; link.download = fileName; document.body.appendChild(link); link.click(); link.remove();
            window.setTimeout(() => URL.revokeObjectURL(url), 1000);
        }
    };
})();
