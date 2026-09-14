window.reportDesigner = (() => {
    const color = value => /^#[0-9a-f]{6}$/i.test(value || '') ? value : '#263247';
    const number = (value, min, max, fallback) => Math.min(max, Math.max(min, Number(value) || fallback));
    function value(element, report) {
        if (element.kind === 'text') return element.text || '';
        const field = element.field === 'companyDetails' ? [report.companyAddress, report.companyPhone ? 'هاتف: ' + report.companyPhone : '', report.commercialRecord ? 'السجل التجاري: ' + report.commercialRecord : '', report.taxNumber ? 'الرقم الضريبي: ' + report.taxNumber : ''].filter(Boolean).join(' | ')
            : element.field === 'printedAt' ? new Date().toLocaleString('ar-SA', { calendar: 'gregory' })
            : element.field === 'periodName' ? report.periodName || report.period : report[element.field];
        return (element.text || '') + (field == null || field === '' ? '—' : String(field));
    }
    function apply(html, report) {
        const design = report.design;
        if (!design) return html;
        const doc = new DOMParser().parseFromString(html, 'text/html');
        const zone = (name) => {
            const container = doc.createElement('div'); container.className = 'designed-zone designed-' + name;
            for (const element of (design.elements || []).filter(e => e.zone === name)) {
                const item = doc.createElement('div'); item.className = 'designed-element';
                Object.assign(item.style, { textAlign: ['left','center','right'].includes(element.align) ? element.align : 'right', color: color(element.color), fontSize: number(element.fontSize, 9, 24, 12) + 'px', fontWeight: element.bold ? '700' : '400', whiteSpace: 'pre-wrap', overflowWrap: 'anywhere', marginBottom: '4px' });
                if (element.kind === 'logo') {
                    if (report.companyLogo) { const img = doc.createElement('img'); img.src = journalEntryExport.logoSource(report.companyLogo); img.alt = 'شعار المنشأة'; img.style.cssText = 'width:76px;height:54px;object-fit:contain'; item.append(img); }
                } else if (element.kind === 'line') { item.style.borderBottom = `1px solid ${color(design.accent)}`; item.style.height = '6px'; }
                else { item.textContent = value(element, report); if (element.kind === 'field' && element.field === 'printedAt') { item.dataset.printTimestamp = ''; item.dataset.printPrefix = element.text || ''; } }
                container.append(item);
            }
            return container;
        };
        let header = doc.querySelector('.print-brand, header.company, header');
        if (header) header.replaceChildren(zone('header'));
        else { header = doc.createElement('header'); header.className = 'print-brand'; header.append(zone('header')); doc.body.prepend(header); }
        let footer = doc.querySelector('.print-footer');
        if (!footer) { footer = doc.createElement('footer'); footer.className = 'print-footer'; doc.body.prepend(footer); }
        footer.replaceChildren(zone('footer'));
        const content = doc.querySelector('.print-layout > tbody > tr > td') || doc.body;
        content.prepend(zone('before')); content.append(zone('after'));
        const style = doc.createElement('style');
        style.textContent = `@page{size:A4 ${report.landscape ? 'landscape' : 'portrait'};margin:${number(design.margin,8,22,12)}mm ${number(design.margin,8,22,12)}mm 22mm}
          body{font-size:${number(design.fontSize,9,16,12)}px!important}table:not(.print-layout) td,table:not(.print-layout) th{font-size:${number(design.fontSize,9,16,12)}px!important;overflow-wrap:anywhere;white-space:normal!important}
          table:not(.print-layout) th{background:${color(design.accent)}!important;color:white!important;border-color:${color(design.accent)}!important}
          .print-brand{display:block!important;border-color:${color(design.accent)}!important;padding-bottom:8px!important;margin-bottom:10px!important}.print-brand img{flex:none}
          .print-footer{display:block!important}.designed-footer{display:flex;gap:16px;align-items:flex-end}.designed-footer>div{flex:1;min-width:0}
          .designed-before,.designed-after{margin:10px 0}.designed-element{break-inside:avoid}.report-title,.invoice-title h2{color:${color(design.accent)}!important}
          .meta{gap:6px;margin:8px 0}.meta-item{padding:5px 7px;min-height:32px}.meta-item strong{font-size:inherit}.party{padding:8px}.party div{margin-top:2px}.summary{gap:10px;margin-top:10px}.notes{margin:8px 0}
          @media screen{body{padding:${number(design.margin,8,22,12)}mm!important;display:flex;flex-direction:column}.print-footer{order:2}}`;
        doc.head.append(style);
        return '<!doctype html>\n' + doc.documentElement.outerHTML;
    }
    function preview(design, company, username) {
        const report = { ...company, design, printedBy: username, entryId: 1201, serialNumber: 12, entryDate: '2026-09-10', period: '2026', periodName:'2026', reference: 'REF-1201', source: 'قيد يومية', card: 'جهة تعامل تجريبية', description: 'بيانات تجريبية لمعاينة التصميم فقط', bank:'البنك', paymentMethod:'تحويل بنكي', fromDate:'2026-01-01',toDate:'2026-09-10', lines: Array.from({length:4},(_,i)=>({number:i+1,accountCode:'1110100'+i,accountName:'حساب تجريبي',description:'تفاصيل الحركة المحاسبية',debit:i%2?0:1250,credit:i%2?1250:0,documentReference:'DOC-'+i,documentDate:'2026-09-10',center:'الإدارة'})) };
        if (design.kind === 'general-journal') return generalJournalExport.previewDocument({...report, rows:report.lines.map(x=>({...x,transId:x.number,glid:1201,serialNo:12,glDate:report.entryDate,accountId:x.accountCode}))});
        if (design.kind === 'trial-balance') return trialBalanceExport.previewDocument({...report,includeLevels:false,rows:report.lines.map(x=>({accountId:x.accountCode,accountName:x.accountName,openingBalance:100,periodDebit:1250,periodCredit:1000,periodBalance:250,totalDebit:1350,totalCredit:1000,closingBalance:350}))});
        if (design.kind === 'chart') return chartExport.previewDocument([{level:6,number:1110100,name:'حساب تجريبي',parentNumber:11101,path:'الأصول / النقدية / الحساب'}],report);
        const titles = { employees: 'الموظفون', holidays: 'الإجازات', payroll: 'مسيرات الرواتب', 'employee-reports': 'تقارير الموظفين', 'account-statement': 'كشف حساب', 'financial-position': 'المركز المالي', 'income-statement': 'قائمة الدخل', 'cost-centers': 'مراكز التكلفة', reports: 'ملخص التقارير' };
        if (titles[design.kind]) return reportExport.previewDocument({ title: titles[design.kind], description: 'بيانات تجريبية لمعاينة التصميم فقط', tables: [{ title: 'تفاصيل التقرير', columns: ['الرقم', 'البيان', 'القيمة'], rows: [['1', 'سجل تجريبي', '1,250.00'], ['2', 'سجل تجريبي', '2,500.00']] }] }, report);
        const voucher = ['receipt','payment'].includes(design.kind) ? {title: design.kind === 'receipt' ? 'سند قبض' : 'سند صرف', number:1201,amount:2500,isReceipt:design.kind==='receipt'} : null;
        const invoice = ['sales','purchase'].includes(design.kind) ? {isPurchase:design.kind==='purchase',fields:{sellerName:company.companyName,sellerAddress:company.companyAddress,sellerVat:company.taxNumber,sellerCr:company.commercialRecord,buyerName:'عميل تجريبي',buyerAddress:'الرياض',buyerVat:'310123456789013',number:1201,issueDate:'2026-09-10',supplyDate:'2026-09-10',paymentTerms:'نقدًا'},items:[{code:'ITEM-01',description:'خدمة تجريبية',quantity:2,unit:'خدمة',unitPrice:1000,discount:0,net:2000,rate:15,tax:300}]} : null;
        return journalEntryExport.previewDocument(report,voucher,invoice);
    }
    return {apply,preview,value};
})();
