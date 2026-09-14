(function () {
    const escape = value => String(value ?? '—').replace(/[&<>"']/g, c => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]));
    window.reportExport = {
        previewDocument(report, company) {
            const tables = (report.tables || []).map(table => {
                const cells = (values, tag) => values.map(value => `<${tag}>${escape(value)}</${tag}>`).join('');
                return `<section>${table.title ? `<h3>${escape(table.title)}</h3>` : ''}${table.summary ? `<p>${escape(table.summary)}</p>` : ''}
                    <table><thead><tr>${cells(table.columns, 'th')}</tr></thead><tbody>
                    ${table.rows.length ? table.rows.map(row => `<tr>${cells(row, 'td')}</tr>`).join('') : `<tr><td colspan="${table.columns.length}">لا توجد سجلات</td></tr>`}
                    </tbody>${table.totals ? `<tfoot><tr>${cells(table.totals, 'td')}</tr></tfoot>` : ''}</table></section>`;
            }).join('');
            const html = `<!doctype html><html lang="ar" dir="rtl"><head><meta charset="utf-8"><title>${escape(report.title)}</title><style>
                *{box-sizing:border-box}body{margin:0;font-family:"Segoe UI",Tahoma,Arial,sans-serif;color:#202532}
                h2{text-align:center;color:#4437ba}h3{font-size:14px;margin:18px 0 8px;break-after:avoid}p{white-space:pre-wrap}
                table{width:100%;border-collapse:collapse;table-layout:fixed}th,td{border:1px solid #dfe2e8;text-align:right;overflow-wrap:anywhere;white-space:pre-wrap}
                th{background:#5948d6;color:white}tbody tr:nth-child(even) td{background:#f8f8fc}tfoot td{background:#f0effa;font-weight:bold}thead{display:table-header-group}
                </style></head><body><h2>${escape(report.title)}</h2><p>الفترة المالية: ${escape(company.periodName)}</p>${report.description ? `<p>${escape(report.description)}</p>` : ''}${tables}</body></html>`;
            const context = { ...company, landscape: report.landscape };
            return reportDesigner.apply(journalEntryExport.portraitDocument(html, context), context);
        },
        print(report, company) { return journalEntryExport.printHtml(this.previewDocument(report, company)); }
    };
})();
