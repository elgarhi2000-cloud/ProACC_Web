(function () {
    const text = (value) => value == null || value === "" ? "-" : String(value);
    const escapeHtml = (value) => text(value)
        .replaceAll("&", "&amp;")
        .replaceAll("<", "&lt;")
        .replaceAll(">", "&gt;")
        .replaceAll('"', "&quot;")
        .replaceAll("'", "&#039;");
    const formatNumber = (value) => Number(value || 0).toLocaleString("en-US", {
        minimumFractionDigits: 2,
        maximumFractionDigits: 2
    });
    const formatDate = (value) => {
        if (!value) return "-";
        const date = new Date(value);
        return Number.isNaN(date.getTime()) ? text(value) : date.toLocaleDateString("en-GB");
    };

    function logoSource(value) {
        if (!value) return "";
        const base64 = typeof value === "string" ? value : btoa(Array.from(value, x => String.fromCharCode(x)).join(""));
        return base64.startsWith("data:image/") ? base64 : `data:image/png;base64,${base64}`;
    }
    const brand = report => `<header class="print-brand">${report.companyLogo ? `<img src="${escapeHtml(logoSource(report.companyLogo))}" alt="شعار المنشأة">` : ""}<div><h1>${escapeHtml(report.companyName)}</h1><p>${escapeHtml(report.companyAddress)} ${report.companyPhone ? " | هاتف: " + escapeHtml(report.companyPhone) : ""}</p><p>السجل التجاري: ${escapeHtml(report.commercialRecord)} | الرقم الضريبي: ${escapeHtml(report.taxNumber)}</p></div></header>`;
    function portraitDocument(html, report) {
        const footer = `<footer class="print-footer"><span>المستخدم: ${escapeHtml(report.printedBy || report.userInfo)}</span><span data-print-timestamp>تاريخ الطباعة: ${escapeHtml(new Date().toLocaleString("ar-SA", { calendar: "gregory" }))}</span></footer>`;
        // Repeated table sections reserve space for the company header and fixed footer on every page.
        return html.replace(/<header[\s\S]*?<\/header>/, "")
            .replace(/<footer[\s\S]*?<\/footer>/g, match => match.includes("مسودة للمراجعة") ? match.replace(/footer/g, "aside") : "")
            .replace("</style>", `
                @page { size: A4 ${report.landscape ? 'landscape' : 'portrait'}; margin: 12mm 12mm 22mm; @bottom-center { content: counter(page); font: 9px Arial; color: #626978; } }
                body { font-family: "Segoe UI", Tahoma, Arial, sans-serif; font-size: 12px; line-height: 1.65; print-color-adjust: exact; -webkit-print-color-adjust: exact; }
                table:not(.print-layout) th { background: #5948d6; color: white; }
                .print-layout { width: 100%; table-layout: fixed; border: 0; }
                .print-layout > thead > tr > td, .print-layout > tbody > tr > td { padding: 0; border: 0; background: transparent; white-space: normal; }
                .print-layout > tbody > tr { break-inside: auto; }
                .print-brand { display: flex; justify-content: flex-start; align-items: center; gap: 18px; padding: 0 0 14px; margin-bottom: 16px; border-bottom: 2px solid #5948d6; text-align: right; }
                .print-brand img { width: 76px; height: 70px; object-fit: contain; flex-shrink: 0; }
                .print-brand h1 { font-size: 22px; margin: 0 0 5px; overflow-wrap: anywhere; }
                .print-brand p { font-size: 10px; color: #626978; margin: 2px 0; overflow-wrap: anywhere; }
                .print-footer { position: fixed; bottom: 0; left: 0; right: 0; margin: 0; display: flex; justify-content: space-between; gap: 16px; border-top: 1px solid #dfe2e8; padding-top: 6px; font-size: 10px; line-height: 1.4; color: #626978; }
                .print-layout > tfoot { display: table-footer-group; }
                .print-layout > tfoot > tr > td { height: 14mm; border: 0; background: transparent; padding: 0; }
                .print-footer span { max-width: 50%; overflow-wrap: anywhere; }
                .meta { grid-template-columns: repeat(2, minmax(0, 1fr)); gap: 8px; }
                .meta-item, .field, .party, .meta > div { break-inside: avoid; min-width: 0; overflow-wrap: anywhere; }
                .meta-item strong { font-size: 12px; white-space: pre-wrap; }
                .meta-item label { font-size: 10px; }
                .meta.journal-meta { grid-template-columns: repeat(4, minmax(0, 1fr)); gap: 4px; margin: 6px 0 8px; }
                .journal-meta .meta-item { display: flex; flex-wrap: wrap; align-items: baseline; column-gap: 6px; row-gap: 0; min-height: 0; padding: 3px 6px; border-radius: 3px; line-height: 1.5; }
                .journal-meta .meta-item label { margin: 0; font-size: 10px; }
                .journal-meta .meta-item strong { font-size: inherit; min-width: 0; overflow-wrap: anywhere; }
                .journal-meta .journal-description { grid-column: 1 / -1; order: 1; }
                th, td { padding: 8px 6px; }
                tr { break-inside: avoid; }
                tfoot { display: table-row-group; }
                .detail { white-space: pre-wrap; font-size: 11px; }
                .muted { display: block; color: #697386; font-size: 10px; }
                .number, .num { overflow-wrap: anywhere; }
                .invoice-title { display: flex; justify-content: space-between; align-items: center; gap: 12px; margin-bottom: 12px; }
                .invoice-items { font-size: 10px; }
                .invoice-items td, .invoice-items th { padding: 6px 4px; }
                aside { margin-top: 16px; padding: 8px; background: #fff5dc; color: #765522; font-size: 10px; }
                @media screen { body { max-width: ${report.landscape ? '297' : '210'}mm; margin: auto; padding: 12mm; } .print-footer { position: static; margin-top: 24px; } }
            </style>`)
            .replace("<body>", `<body>${footer}<table class="print-layout"><thead><tr><td>${brand(report)}</td></tr></thead><tbody><tr><td>`)
            .replace("</body>", `${report.userInfo ? `<div class="notes"><span class="muted">بيانات مُعد السجل: ${escapeHtml(report.userInfo)}</span></div>` : ""}</td></tr></tbody><tfoot><tr><td></td></tr></tfoot></table></body>`);
    }
    async function prepareFrame(frame) {
        for (const timestamp of frame.contentDocument.querySelectorAll('[data-print-timestamp]'))
            timestamp.textContent = `${timestamp.dataset.printPrefix ?? 'تاريخ الطباعة: '}${new Date().toLocaleString("ar-SA", { calendar: "gregory" })}`;
        await frame.contentDocument.fonts.ready;
        await Promise.all(Array.from(frame.contentDocument.images, img => img.decode().catch(() => {})));
    }

    function buildPrintDocument(report) {
        const lines = Array.isArray(report.lines) ? report.lines : [];
        const totalDebit = lines.reduce((sum, line) => sum + Number(line.debit || 0), 0);
        const totalCredit = lines.reduce((sum, line) => sum + Number(line.credit || 0), 0);
        const rows = lines.map((line) => `
            <tr>
                <td class="center">${escapeHtml(line.number)}</td>
                <td><span class="code">${escapeHtml(line.accountCode)}</span><br>${escapeHtml(line.accountName)}</td>
                <td class="detail">${escapeHtml(line.description)}<span class="muted">رقم المستند: ${escapeHtml(line.documentReference)}<br>تاريخ المستند: ${escapeHtml(formatDate(line.documentDate))}<br>مركز فرعي: ${escapeHtml(line.center)}</span></td>
                <td class="number debit">${formatNumber(line.debit)}</td>
                <td class="number credit">${formatNumber(line.credit)}</td>
            </tr>`).join("");

        return `<!doctype html>
<html lang="ar" dir="rtl">
<head>
    <meta charset="utf-8">
    <title>قيد يومية ${escapeHtml(report.entryId || "جديد")}</title>
    <style>
        @page { size: A4 portrait; margin: 11mm; }
        * { box-sizing: border-box; }
        body { margin: 0; color: #202532; background: #fff; font-family: "Segoe UI", Tahoma, Arial, sans-serif; font-size: 10px; }
        .company { padding-bottom: 10px; text-align: center; border-bottom: 2px solid #5948d6; }
        .company h1 { margin: 0 0 4px; color: #27223f; font-size: 20px; }
        .company p { margin: 2px 0; color: #626978; }
        .report-title { margin: 12px 0 8px; text-align: center; color: #4437ba; font-size: 16px; }
        .meta { margin-bottom: 10px; display: grid; grid-template-columns: repeat(4, 1fr); gap: 6px; }
        .meta-item { min-height: 37px; padding: 6px 8px; border: 1px solid #dfe2e8; border-radius: 5px; }
        .meta-item.wide { grid-column: span 2; }
        .meta-item label { display: block; margin-bottom: 3px; color: #727987; font-size: 9px; font-weight: 700; }
        .meta-item strong { color: #2e3441; font-size: 10px; font-weight: 700; }
        table { width: 100%; table-layout: fixed; border-collapse: collapse; }
        thead { display: table-header-group; }
        th { padding: 7px 5px; color: #fff; background: #5948d6; border: 1px solid #4d3dc2; font-weight: 700; }
        td { padding: 6px 5px; border: 1px solid #dfe2e8; vertical-align: middle; overflow-wrap: anywhere; }
        tbody tr:nth-child(even) td { background: #f8f8fc; }
        tfoot td { background: #f0effa; font-weight: 800; }
        .center { text-align: center; }
        .code, .number { direction: ltr; text-align: right; font-variant-numeric: tabular-nums; }
        .debit { color: #13795b; }
        .credit { color: #b63f50; }
        .footer { margin-top: 8px; display: flex; justify-content: space-between; color: #7a818e; font-size: 9px; }
    </style>
</head>
<body>
    <header class="company">
        <h1>${escapeHtml(report.companyName)}</h1>
        <p>${escapeHtml(report.companyAddress)} ${report.companyPhone ? " | هاتف: " + escapeHtml(report.companyPhone) : ""}</p>
        <p>${report.commercialRecord ? "السجل التجاري: " + escapeHtml(report.commercialRecord) : ""} ${report.taxNumber ? " | الرقم الضريبي: " + escapeHtml(report.taxNumber) : ""}</p>
    </header>
    <h2 class="report-title">قيد يومية</h2>
    <section class="meta journal-meta">
        <div class="meta-item"><label>رقم القيد</label><strong>${escapeHtml(report.entryId || "جديد")}</strong></div>
        <div class="meta-item"><label>تسلسل القيد</label><strong>${escapeHtml(report.serialNumber)}</strong></div>
        <div class="meta-item"><label>تاريخ القيد</label><strong>${escapeHtml(formatDate(report.entryDate))}</strong></div>
        <div class="meta-item"><label>الفترة المالية</label><strong>${escapeHtml(report.period)}</strong></div>
        <div class="meta-item wide"><label>نوع القيد</label><strong>${escapeHtml(report.source)}</strong></div>
        <div class="meta-item"><label>رقم المرجع</label><strong>${escapeHtml(report.reference)}</strong></div>
        <div class="meta-item"><label>حالة الأرشفة</label><strong>${report.isArchived ? "مؤرشف" : "غير مؤرشف"}</strong></div>
        <div class="meta-item journal-description"><label>بيان القيد</label><strong>${escapeHtml(report.description)}</strong></div>
        <div class="meta-item"><label>جهة التعامل</label><strong>${escapeHtml(report.card)}</strong></div>
        <div class="meta-item"><label>اسم البنك</label><strong>${escapeHtml(report.bank)}</strong></div>
        <div class="meta-item"><label>طريقة الدفع</label><strong>${escapeHtml(report.paymentMethod)}</strong></div>
        <div class="meta-item"><label>مركز 1 / مركز 2</label><strong>${escapeHtml(report.center1)} / ${escapeHtml(report.center2)}</strong></div>
    </section>
    <table>
        <colgroup><col style="width:5%"><col style="width:25%"><col style="width:40%"><col style="width:15%"><col style="width:15%"></colgroup>
        <thead><tr><th>#</th><th>رقم الحساب / اسم الحساب</th><th>الوصف / المستند / المركز</th><th>مدين</th><th>دائن</th></tr></thead>
        <tbody>${rows || '<tr><td colspan="5" class="center">لا توجد سجلات</td></tr>'}</tbody>
        <tfoot><tr><td colspan="3">الإجمالي</td><td class="number debit">${formatNumber(totalDebit)}</td><td class="number credit">${formatNumber(totalCredit)}</td></tr></tfoot>
    </table>
    <footer class="footer"><span>المستخدم: ${escapeHtml(report.userInfo)}</span><span>تاريخ الطباعة: ${new Date().toLocaleString("ar-SA")}</span></footer>
</body>
</html>`;
    }

    function arabicInteger(number) {
        const ones = ["", "واحد", "اثنان", "ثلاثة", "أربعة", "خمسة", "ستة", "سبعة", "ثمانية", "تسعة", "عشرة", "أحد عشر", "اثنا عشر", "ثلاثة عشر", "أربعة عشر", "خمسة عشر", "ستة عشر", "سبعة عشر", "ثمانية عشر", "تسعة عشر"];
        const tens = ["", "", "عشرون", "ثلاثون", "أربعون", "خمسون", "ستون", "سبعون", "ثمانون", "تسعون"];
        const hundreds = ["", "مائة", "مائتان", "ثلاثمائة", "أربعمائة", "خمسمائة", "ستمائة", "سبعمائة", "ثمانمائة", "تسعمائة"];
        if (!number) return "صفر";
        if (number < 20) return ones[number];
        if (number < 100) return [ones[number % 10], tens[Math.floor(number / 10)]].filter(Boolean).join(" و");
        if (number < 1000) return [hundreds[Math.floor(number / 100)], number % 100 ? arabicInteger(number % 100) : ""].filter(Boolean).join(" و");
        for (const [scale, singular, dual, plural] of [[1e12, "تريليون", "تريليونان", "تريليونات"], [1e9, "مليار", "ملياران", "مليارات"], [1e6, "مليون", "مليونان", "ملايين"], [1000, "ألف", "ألفان", "آلاف"]]) {
            if (number < scale) continue;
            const count = Math.floor(number / scale);
            const group = count === 1 ? singular : count === 2 ? dual : arabicInteger(count) + " " + (count <= 10 ? plural : singular);
            return group + (number % scale ? " و" + arabicInteger(number % scale) : "");
        }
    }

    function buildVoucherDocument(report, voucher) {
        const minorUnits = Math.round(Number(voucher.amount) * 100);
        if (!Number.isSafeInteger(minorUnits) || minorUnits <= 0) throw new Error("مبلغ السند غير صالح للطباعة");
        const amountWords = "فقط " + arabicInteger(Math.floor(minorUnits / 100)) + " ريال سعودي" + (minorUnits % 100 ? " و" + arabicInteger(minorUnits % 100) + " هللة" : "") + " لا غير";
        const field = (label, value, wide = false) => `<div class="field ${wide ? "wide" : ""}"><label>${escapeHtml(label)}</label><strong>${escapeHtml(value)}</strong></div>`;
        return `<!doctype html><html lang="ar" dir="rtl"><head><meta charset="utf-8"><title>${escapeHtml(voucher.title)} ${escapeHtml(voucher.number)}</title>
        <style>
        @page { size: A4 portrait; margin: 16mm; }
        * { box-sizing: border-box; } body { margin: 0; font: 14px Tahoma, Arial, sans-serif; color: #243047; line-height: 1.8; }
        header { text-align: center; border-bottom: 2px solid #5948d6; padding-bottom: 16px; } h1 { font-size: 23px; margin: 0; } header p { margin: 4px 0; }
        h2 { text-align: center; color: #4437ba; margin: 22px 0; } .fields { display: grid; grid-template-columns: 1fr 1fr; gap: 12px; }
        .field { border: 1px solid #dfe2e8; padding: 12px; border-radius: 6px; overflow-wrap: anywhere; } .wide { grid-column: 1 / -1; }
        label { display: block; font-size: 12px; color: #697386; margin-bottom: 5px; } strong { white-space: pre-wrap; } .signatures { display: flex; justify-content: space-around; gap: 20px; margin-top: 55px; text-align: center; break-inside: avoid; }
        .signatures div { flex: 1; border-top: 1px solid #8790a0; padding-top: 10px; } footer { margin-top: 28px; color: #697386; font-size: 10px; }
        </style></head><body><header><h1>${escapeHtml(report.companyName)}</h1><p>${escapeHtml(report.companyAddress)}</p>${report.companyPhone ? `<p>هاتف: ${escapeHtml(report.companyPhone)}</p>` : ""}</header>
        <h2>${escapeHtml(voucher.title)}</h2><section class="fields">
        ${field("رقم السند", voucher.number || "جديد")}${field("تاريخ السند", formatDate(report.entryDate))}
        ${field(voucher.isReceipt ? "استلمنا من / العميل" : "يصرف إلى / المستفيد أو المورد", report.card, true)}
        ${field("المبلغ بالأرقام (ريال سعودي)", formatNumber(voucher.amount))}${field("رقم المرجع", report.reference)}
        ${field("المبلغ بالأحرف", amountWords, true)}${field("البنك", report.bank)}${field("طريقة السداد", report.paymentMethod)}
        ${field("بيان السند", report.description, true)}</section>
        <section class="signatures"><div>إعداد</div><div>اعتماد</div><div>توقيع المستلم</div></section>
        <footer>المستخدم: ${escapeHtml(report.userInfo)} | تاريخ الطباعة: ${escapeHtml(new Date().toLocaleString("ar-SA"))}</footer></body></html>`;
    }

    function buildInvoiceDocument(report, invoice) {
        const f = invoice.fields;
        const items = invoice.items;
        const sum = key => items.reduce((total, item) => total + Math.round(Number(item[key]) * 100), 0) / 100;
        const net = sum("net"), tax = sum("tax"), discount = sum("discount");
        const qr = typeof invoice.qrCodeImage === "string" && /^data:image\/png;base64,[A-Za-z0-9+/=]+$/.test(invoice.qrCodeImage)
            ? `<section class="invoice-qr"><img src="${invoice.qrCodeImage}" alt="رمز QR لبيانات الفاتورة"><div>رمز QR لبيانات الفاتورة</div></section>` : "";
        const field = (label, value) => `<div><label>${escapeHtml(label)}</label><strong>${escapeHtml(value)}</strong></div>`;
        const party = (title, prefix) => `<section class="party"><h3>${title}</h3>${field("الاسم", f[prefix + "Name"])}${field("العنوان الوطني", f[prefix + "Address"])}${field("الرقم الضريبي", f[prefix + "Vat"])}${prefix === "seller" ? field("السجل التجاري", f.sellerCr) : ""}</section>`;
        const groups = new Map();
        items.forEach(item => {
            const group = groups.get(item.rate) || { net: 0, tax: 0 };
            group.net += Math.round(item.net * 100); group.tax += Math.round(item.tax * 100);
            groups.set(item.rate, group);
        });
        return `<!doctype html><html lang="ar" dir="rtl"><head><meta charset="utf-8"><title>فاتورة ${invoice.isPurchase ? "مشتريات" : "مبيعات"} ${escapeHtml(f.number)}</title>
        <style>
        @page { size: A4 portrait; margin: 12mm; } * { box-sizing: border-box; }
        body { margin: 0; color: #263247; font: 11px Tahoma, Arial, sans-serif; line-height: 1.6; }
        header { display: flex; justify-content: space-between; align-items: center; border-bottom: 3px solid #5948d6; padding-bottom: 10px; } h1 { font-size: 22px; margin: 0; } h2 { font-size: 18px; margin: 0; color: #5948d6; } h3 { margin: 0 0 6px; font-size: 13px; }
        .badge { color: #925d08; background: #fff5dc; padding: 5px 10px; } .meta { display: grid; grid-template-columns: repeat(4, 1fr); gap: 10px; margin: 12px 0; }
        label { color: #6b7280; display: block; font-size: 10px; } strong { white-space: pre-wrap; overflow-wrap: anywhere; }
        .parties { display: grid; grid-template-columns: 1fr 1fr; gap: 12px; margin-bottom: 12px; } .party { padding: 10px; border: 1px solid #dfe2e8; border-radius: 6px; } .party div { margin-top: 4px; }
        table { width: 100%; border-collapse: collapse; table-layout: fixed; } th { background: #eeecfa; } td, th { border: 1px solid #dfe2e8; padding: 7px 5px; overflow-wrap: anywhere; } td.num { direction: ltr; text-align: center; font-variant-numeric: tabular-nums; } thead { display: table-header-group; } tr { break-inside: avoid; }
        .summary { display: grid; grid-template-columns: 1fr 1fr; gap: 16px; margin-top: 14px; break-inside: avoid; } .totals div { display: flex; justify-content: space-between; padding: 6px 10px; border-bottom: 1px solid #dfe2e8; } .grand { background: #eeecfa; font-size: 14px; font-weight: bold; }
        .notes { margin: 12px 0; white-space: pre-wrap; overflow-wrap: anywhere; } footer { margin-top: 16px; border-top: 1px solid #dfe2e8; padding-top: 8px; color: #6b7280; font-size: 10px; }
        </style></head><body>
        <header><div><h1>${escapeHtml(f.sellerName)}</h1><span>${invoice.isPurchase ? "نسخة مراجعة لفاتورة المورد" : "مبيعات"}</span></div><h2>فاتورة ضريبية</h2></header>
        <div class="invoice-title"><h2>فاتورة ${invoice.isPurchase ? "مشتريات" : "مبيعات"}</h2></div><section class="meta">${field("رقم الفاتورة", f.number)}${field("تاريخ ووقت الإصدار (السعودية)", formatDate(f.issueDate) + (f.issueTime ? " · " + f.issueTime : ""))}${field("تاريخ التوريد", formatDate(f.supplyDate))}${field("العملة", "ريال سعودي SAR")}</section>
        <div class="parties">${party("البائع / المورد", "seller")}${party("المشتري / العميل", "buyer")}</div>
        <table class="invoice-items"><colgroup><col style="width:28%"><col style="width:9%"><col style="width:13%"><col style="width:10%"><col style="width:13%"><col style="width:12%"><col style="width:15%"></colgroup>
        <thead><tr><th>الصنف / الوصف</th><th>الكمية / الوحدة</th><th>سعر الوحدة قبل الضريبة</th><th>الخصم</th><th>الصافي الخاضع للضريبة</th><th>الضريبة / النسبة</th><th>الإجمالي شامل الضريبة</th></tr></thead><tbody>
        ${items.map(item => `<tr><td>${escapeHtml(item.description)}<span class="muted">كود الصنف: ${escapeHtml(item.code)}</span></td><td class="num">${escapeHtml(item.quantity)}<span class="muted">${escapeHtml(item.unit)}</span></td><td class="num">${Number(item.unitPrice).toLocaleString("en-US", { maximumFractionDigits: 6, minimumFractionDigits: 2 })}</td><td class="num">${formatNumber(item.discount)}</td><td class="num">${formatNumber(item.net)}</td><td class="num">${formatNumber(item.tax)}<span class="muted">${escapeHtml(item.rate)}%</span></td><td class="num">${formatNumber((Math.round(item.net * 100) + Math.round(item.tax * 100)) / 100)}</td></tr>`).join("")}
        </tbody></table>
        <section class="summary"><div><h3>ملخص ضريبة القيمة المضافة</h3><table><thead><tr><th>النسبة</th><th>الوعاء الضريبي</th><th>الضريبة</th></tr></thead><tbody>${Array.from(groups, ([rate, value]) => `<tr><td>${escapeHtml(rate)}%</td><td class="num">${formatNumber(value.net / 100)}</td><td class="num">${formatNumber(value.tax / 100)}</td></tr>`).join("")}</tbody></table>${qr}</div>
        <div class="totals"><div><span>الإجمالي قبل الخصم والضريبة</span><b>${formatNumber(net + discount)}</b></div><div><span>إجمالي الخصومات</span><b>${formatNumber(discount)}</b></div><div><span>الإجمالي دون الضريبة</span><b>${formatNumber(net)}</b></div><div><span>ضريبة القيمة المضافة</span><b>${formatNumber(tax)}</b></div><div class="grand"><span>الإجمالي شامل الضريبة (ر.س)</span><b>${formatNumber(net + tax)}</b></div></div></section>
        <div class="notes">${field("بيان الفاتورة", report.description)}</div>
        <section class="meta">${field("طريقة السداد", report.paymentMethod)}${field("البنك", report.bank)}${field("شروط السداد", f.paymentTerms)}${field("مرجع القيد الداخلي", report.entryId)}</section>
        ${f.taxReason ? `<div class="notes">${field("سبب ورمز المعاملة الضريبية الخاصة", f.taxReason)}</div>` : ""}

        </body></html>`;
    }

    function compactInvoiceDocument(html) {
        // Apply after the shared report layout and saved design; other reports stay unchanged.
        return html.replace("</head>", `<style>
            body { line-height: 1.35; }
            .print-brand { padding-bottom: 5px!important; margin-bottom: 7px!important; }
            .print-brand h1 { margin-bottom: 3px; font-size: 19px; }
            .print-brand p { margin: 1px 0; }
            .print-brand .designed-header { display: flex; flex-wrap: wrap; align-items: center; justify-content: flex-start; gap: 3px 10px; }
            .print-brand .designed-element { margin-bottom: 0!important; }
            .invoice-title { margin-bottom: 5px; }
            .invoice-title h2 { font-size: 17px; }
            .meta { grid-template-columns: repeat(4,minmax(0,1fr)); gap: 4px 8px!important; margin: 6px 0!important; }
            .meta strong { font-size: 11px; }
            .parties { gap: 8px; margin-bottom: 7px; }
            .party { padding: 6px 8px!important; }
            .party h3 { margin-bottom: 4px; }
            .party div { margin-top: 3px!important; display: grid; grid-template-columns: 78px minmax(0,1fr); gap: 4px; }
            .party strong { font-size: 11px; }
            .invoice-items td, .invoice-items th { padding: 4px!important; }
            .summary { gap: 12px!important; margin-top: 7px!important; align-items: start; }
            .summary h3 { margin: 0 0 4px; }
            .summary td, .summary th { padding: 3px 5px!important; }
            .totals div { padding: 4px 6px; gap: 6px; }
            .totals b { white-space: nowrap; }
            .grand { font-size: 12px; }
            .invoice-qr { margin: 5px 0 0; text-align: center; break-inside: avoid; }
            .invoice-qr img { display: block; width: 36mm; height: 36mm; margin: 0 auto; }
            .invoice-qr div { font-size: 9px; }
            .notes { margin: 5px 0!important; }
            .notes > div { display: flex; flex-wrap: wrap; gap: 4px 8px; }
            .designed-before, .designed-after { margin: 4px 0!important; }
            .print-layout > tfoot > tr > td { height: 7mm; }
            </style></head>`);
    }

    window.journalEntryExport = {
        logoSource,
        portraitDocument,
        previewDocument(report, voucher, invoice) {
            const html = portraitDocument(invoice ? buildInvoiceDocument(report, invoice) : voucher ? buildVoucherDocument(report, voucher) : buildPrintDocument(report), report);
            const designed = window.reportDesigner ? reportDesigner.apply(html, report) : html;
            return invoice ? compactInvoiceDocument(designed) : designed;
        },
        async printPreview(frame) {
            if (!frame?.contentDocument || !frame.contentWindow) throw new Error("Preview is not ready");
            await prepareFrame(frame);
            frame.contentWindow.focus();
            frame.contentWindow.print();
        },
        async print(report, voucher, invoice) {
            const html = this.previewDocument(report, voucher, invoice);
            return this.printHtml(html);
        },
        async printHtml(html) {
            const frame = document.createElement("iframe");
            frame.style.position = "fixed";
            frame.style.left = "-10000px";
            frame.style.top = "0";
            frame.style.width = "1px";
            frame.style.height = "1px";
            frame.setAttribute("aria-hidden", "true");
            document.body.appendChild(frame);
            const doc = frame.contentDocument;
            doc.open();
            doc.write(html);
            doc.close();
            await prepareFrame(frame);
            frame.contentWindow.addEventListener("afterprint", () => frame.remove(), { once: true });
            frame.contentWindow.focus();
            frame.contentWindow.print();

        },

        async downloadPdf(fileName, streamReference) {
            const data = await streamReference.arrayBuffer();
            const url = URL.createObjectURL(new Blob([data], { type: "application/pdf" }));
            const link = document.createElement("a");
            link.href = url; link.download = fileName;
            document.body.appendChild(link); link.click(); link.remove();
            window.setTimeout(() => URL.revokeObjectURL(url), 30000);
        },
        downloadBase64(fileName, contentType, base64Data) {
            const binary = atob(base64Data);
            const bytes = new Uint8Array(binary.length);
            for (let i = 0; i < binary.length; i++) bytes[i] = binary.charCodeAt(i);
            const url = URL.createObjectURL(new Blob([bytes], { type: contentType }));
            const link = document.createElement("a");
            link.href = url;
            link.download = fileName;
            document.body.appendChild(link);
            link.click();
            link.remove();
            window.setTimeout(() => URL.revokeObjectURL(url), 1000);
        }
    };
})();
