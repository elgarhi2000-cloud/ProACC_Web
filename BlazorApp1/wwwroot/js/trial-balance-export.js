(function () {
    const value = (input) => input == null || input === "" ? "-" : String(input);
    const escapeHtml = (input) => value(input).replaceAll("&", "&amp;").replaceAll("<", "&lt;").replaceAll(">", "&gt;").replaceAll('"', "&quot;").replaceAll("'", "&#039;");
    const amount = (input) => Number(input || 0).toLocaleString("en-US", { minimumFractionDigits: 2, maximumFractionDigits: 2 });
    const date = (input) => input ? new Date(input).toLocaleDateString("en-GB") : "-";
    const amountClass = (input) => Number(input || 0) < 0 ? "negative" : Number(input || 0) > 0 ? "positive" : "zero";

    function buildDocument(report) {
        const levelHeaders = report.includeLevels ? ["المستوى 1", "المستوى 2", "المستوى 3", "المستوى 4", "المستوى 5"] : [];
        const headers = ["رقم الحساب", "اسم الحساب", ...levelHeaders, "رصيد افتتاحي", "مدين الفترة", "دائن الفترة", "رصيد الفترة", "إجمالي مدين", "إجمالي دائن", "رصيد ختامي"];
        const rows = (report.rows || []).map((row) => {
            const levels = report.includeLevels ? [row.level1, row.level2, row.level3, row.level4, row.level5].map(item => `<td>${escapeHtml(item)}</td>`).join("") : "";
            return `<tr><td class="code">${escapeHtml(row.accountId)}</td><td>${escapeHtml(row.accountName)}</td>${levels}<td class="number ${amountClass(row.openingBalance)}">${amount(row.openingBalance)}</td><td class="number positive">${amount(row.periodDebit)}</td><td class="number negative-text">${amount(row.periodCredit)}</td><td class="number ${amountClass(row.periodBalance)}">${amount(row.periodBalance)}</td><td class="number positive">${amount(row.totalDebit)}</td><td class="number negative-text">${amount(row.totalCredit)}</td><td class="number strong ${amountClass(row.closingBalance)}">${amount(row.closingBalance)}</td></tr>`;
        }).join("");
        const sums = (key) => (report.rows || []).reduce((total, row) => total + Number(row[key] || 0), 0);
        const leadColumns = report.includeLevels ? 7 : 2;

        return `<!doctype html><html lang="ar" dir="rtl"><head><meta charset="utf-8"><title>ميزان المراجعة</title><style>
        @page { size: ${report.includeLevels ? "A3" : "A4"} landscape; margin: 9mm; }
        *{box-sizing:border-box} body{margin:0;color:#252b37;background:#fff;font-family:"Segoe UI",Tahoma,Arial,sans-serif;font-size:9px}.company{text-align:center;padding-bottom:8px;border-bottom:2px solid #5948d6}.company h1{margin:0 0 3px;font-size:18px}.company p{margin:2px 0;color:#68707d}.title{text-align:center;margin:9px 0 3px;color:#4437ba;font-size:15px}.period{text-align:center;margin-bottom:9px;color:#626a77}table{width:100%;table-layout:fixed;border-collapse:collapse}thead{display:table-header-group}th{padding:6px 4px;color:#fff;background:#5948d6;border:1px solid #493abb;white-space:nowrap}td{padding:5px 4px;border:1px solid #dfe2e8;vertical-align:middle;overflow-wrap:anywhere}tbody tr:nth-child(even) td{background:#f8f8fc}tfoot td{background:#f0effa;font-weight:800}.code,.number{direction:ltr;text-align:right;font-variant-numeric:tabular-nums}.positive{color:#13795b}.negative,.negative-text{color:#b63f50}.zero{color:#858c98}.strong{font-weight:800}.footer{margin-top:7px;display:flex;justify-content:space-between;color:#7a818e}
        </style></head><body><header class="company"><h1>${escapeHtml(report.companyName)}</h1><p>${escapeHtml(report.companyAddress)}${report.companyPhone ? " | هاتف: " + escapeHtml(report.companyPhone) : ""}</p><p>${report.commercialRecord ? "السجل التجاري: " + escapeHtml(report.commercialRecord) : ""}${report.taxNumber ? " | الرقم الضريبي: " + escapeHtml(report.taxNumber) : ""}</p></header><h2 class="title">ميزان المراجعة</h2><div class="period">الفترة المالية: ${escapeHtml(report.periodName)} | من ${date(report.fromDate)} إلى ${date(report.toDate)}</div><table><thead><tr>${headers.map(header => `<th>${header}</th>`).join("")}</tr></thead><tbody>${rows || `<tr><td colspan="${headers.length}" style="text-align:center">لا توجد بيانات</td></tr>`}</tbody><tfoot><tr><td colspan="${leadColumns}">الإجمالي</td><td class="number ${amountClass(sums("openingBalance"))}">${amount(sums("openingBalance"))}</td><td class="number positive">${amount(sums("periodDebit"))}</td><td class="number negative-text">${amount(sums("periodCredit"))}</td><td class="number ${amountClass(sums("periodBalance"))}">${amount(sums("periodBalance"))}</td><td class="number positive">${amount(sums("totalDebit"))}</td><td class="number negative-text">${amount(sums("totalCredit"))}</td><td class="number strong ${amountClass(sums("closingBalance"))}">${amount(sums("closingBalance"))}</td></tr></tfoot></table><footer class="footer"><span>${report.rows.length} حساب</span><span>تاريخ التصدير: ${new Date().toLocaleString("ar-SA-u-ca-gregory")}</span></footer></body></html>`;
    }

    window.trialBalanceExport = {
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
