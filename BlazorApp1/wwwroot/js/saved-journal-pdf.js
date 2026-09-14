// Local canvas rendering preserves Arabic shaping without external fonts/services.
// Each page is embedded as a JPEG in a standard PDF; no print dialog is required.
window.savedJournalPdf = {
    async download(report) {
        await document.fonts.ready;
        const design = report.design;
        const width = 794, height = 1123, margin = design ? design.margin * 794 / 210 : 40;
        let bottom = 1030;
        const baseFont = design?.fontSize || 13, rowStep = baseFont + 6;
        const printedAt = new Date().toLocaleString('ar-SA', { calendar: 'gregory' });
        let logo = null;
        if (report.companyLogo) {
            logo = new Image();
            const value = report.companyLogo;
            const base64 = typeof value === 'string' ? value : btoa(Array.from(value, x => String.fromCharCode(x)).join(''));
            logo.src = base64.startsWith('data:image/') ? base64 : `data:image/png;base64,${base64}`;
            try { await logo.decode(); } catch { logo = null; }
        }
        const images = [];
        let canvas, ctx, y;
        const money = value => Number(value || 0).toLocaleString('en-US', { minimumFractionDigits: 2, maximumFractionDigits: 2 });
        const date = value => value ? String(value).slice(0, 10).replaceAll('-', '/') : '—';
        const text = (value, x, top, size = baseFont, bold = false, ink = '#20304a', align = 'right') => {
            ctx.font = `${bold ? 'bold ' : ''}${size}px Tahoma, Arial, sans-serif`;
            ctx.fillStyle = ink; ctx.textAlign = align; ctx.direction = 'rtl';
            ctx.fillText(String(value ?? ''), x, top);
        };
        const wrap = (value, maxWidth, size = baseFont) => {
            ctx.font = `${size}px Tahoma, Arial, sans-serif`;
            const result = [];
            for (const paragraph of String(value ?? '').split(/\r?\n/)) {
                let line = '';
                for (const word of paragraph.split(/\s+/)) {
                    if (line && ctx.measureText(line + ' ' + word).width > maxWidth) { result.push(line); line = ''; }
                    for (const char of (line ? ' ' : '') + word) {
                        if (line && ctx.measureText(line + char).width > maxWidth) { result.push(line); line = ''; }
                        line += char;
                    }
                }
                result.push(line);
            }
            return result.length ? result : [''];
        };
        const elementLines = (element, available) => wrap(window.reportDesigner.value(element, report), available, element.fontSize);
        const drawElement = (element, top, left, available, measure = false) => {
            const x = element.align === 'left' ? left : element.align === 'center' ? left + available / 2 : left + available;
            if (element.kind === 'logo') {
                if (logo && !measure) { const scale = Math.min(76 / logo.naturalWidth, 54 / logo.naturalHeight); const w = logo.naturalWidth * scale; ctx.drawImage(logo, element.align === 'left' ? left : element.align === 'center' ? x - w / 2 : x - w, top, w, logo.naturalHeight * scale); }
                return logo ? 62 : 0;
            }
            if (element.kind === 'line') { if (!measure) { ctx.strokeStyle = design.accent; ctx.beginPath(); ctx.moveTo(left, top + 6); ctx.lineTo(left + available, top + 6); ctx.stroke(); } return 12; }
            const lines = elementLines(element, available);
            if (!measure) lines.forEach((line, i) => text(line, x, top + element.fontSize + i * (element.fontSize + 5), element.fontSize, element.bold, element.color, element.align));
            return lines.length * (element.fontSize + 5) + 4;
        };
        const drawZone = (zone, top, measure = false) => {
            const elements = (design?.elements || []).filter(e => e.zone === zone);
            if (zone === 'footer') {
                const available = (width - 2 * margin) / Math.max(1, elements.length);
                return Math.max(0, ...elements.map((e,i) => drawElement(e, top, width - margin - available * (i+1), available - 12, measure)));
            }
            let used = 0;
            for (const element of elements) used += drawElement(element, top + used, margin, width - 2 * margin, measure);
            return used;
        };
        const bodyZone = zone => {
            for (const element of (design?.elements || []).filter(e => e.zone === zone)) {
                if (element.kind === 'logo' || element.kind === 'line') {
                    const h = drawElement(element, y, margin, width-2*margin,true);
                    if (y + h > bottom) { finish(); newPage(); }
                    y += drawElement(element,y,margin,width-2*margin);
                } else {
                    for (const line of elementLines(element,width-2*margin)) {
                        if (y + element.fontSize + 8 > bottom) { finish(); newPage(); }
                        const x = element.align === 'left' ? margin : element.align === 'center' ? width/2 : width-margin;
                        text(line,x,y+element.fontSize,element.fontSize,element.bold,element.color,element.align); y += element.fontSize+5;
                    }
                    y += 4;
                }
            }
        };
        const finish = () => {
            ctx.strokeStyle = '#dce2ec'; ctx.beginPath(); ctx.moveTo(margin, bottom + 18); ctx.lineTo(width - margin, bottom + 18); ctx.stroke();
            if (design) drawZone('footer', bottom + 24);
            else {
            wrap(`المستخدم: ${report.printedBy || report.userInfo || '—'}`, 350, 11).slice(0, 2).forEach((line, i) => text(line, width - margin, bottom + 38 + i * 15, 11));
            text(`تاريخ الطباعة: ${printedAt}`, 390, bottom + 38, 11);
            }
            text(`صفحة ${images.length + 1} · القيد ${report.entryId}`, width - margin, height - 18, 10);
            images.push(Uint8Array.from(atob(canvas.toDataURL('image/jpeg', .94).split(',')[1]), c => c.charCodeAt(0)));
            canvas.width = canvas.height = 0;
        };
        const newPage = () => {
            canvas = document.createElement('canvas'); canvas.width = width * 2; canvas.height = height * 2;
            ctx = canvas.getContext('2d'); ctx.scale(2, 2); ctx.fillStyle = '#fff'; ctx.fillRect(0, 0, width, height);
            if (design) {
                bottom = height - margin - Math.max(38, drawZone('footer', 0, true)) - 32;
                y = margin + drawZone('header', margin);
                if (y > bottom - 180) throw new Error('قلّل عدد عناصر الرأس أو حجمها لإتاحة مساحة لبيانات السجل.');
            } else {
            if (logo) {
                const scale = Math.min(86 / logo.naturalWidth, 70 / logo.naturalHeight);
                ctx.drawImage(logo, margin, 30, logo.naturalWidth * scale, logo.naturalHeight * scale);
            }
            y = 48;
            for (const line of wrap(report.companyName || 'المنشأة', width - margin * 2 - 112, 21)) {
                text(line, width - margin, y, 21, true); y += 28;
            }
            y = Math.max(y, 100);
            ctx.strokeStyle = '#5948d6'; ctx.lineWidth = 2; ctx.beginPath(); ctx.moveTo(margin, y); ctx.lineTo(width - margin, y); ctx.stroke();
            }
            y += 28;
            for (const line of wrap(`قيد محاسبي رقم ${report.entryId} — ${report.source || ''}`, width - margin * 2, 16)) {
                text(line, width - margin, y, 16, true); y += 24;
            }
            y += 8;
        };
        const paragraph = value => {
            for (const line of wrap(value, width - margin * 2)) {
                if (y > bottom - 25) { finish(); newPage(); }
                text(line, width - margin, y); y += 21;
            }
        };
        newPage();
        if (design) { bodyZone('before'); y += baseFont + 6; }
        for (const value of [
            [report.companyAddress, report.companyPhone].filter(Boolean).join(' | '),
            `السجل التجاري: ${report.commercialRecord || '—'} | الرقم الضريبي: ${report.taxNumber || '—'}`,
            `تسلسل القيد: ${report.serialNumber ?? '—'} | التاريخ: ${date(report.entryDate)} | الفترة: ${report.period || '—'}`,
            `جهة التعامل: ${report.card || '—'} | المرجع: ${report.reference || '—'}`,
            `البنك: ${report.bank || '—'} | طريقة الدفع: ${report.paymentMethod || '—'}`,
            `مركز 1: ${report.center1 || '—'} | مركز 2: ${report.center2 || '—'} | ${report.isArchived ? 'مؤرشف' : 'غير مؤرشف'}`,
            `البيان: ${report.description || '—'}`
        ]) if (value) paragraph(value);
        if (report.userInfo) paragraph(report.userInfo);
        y += 12;
        // Columns from right to left: number, account, description, debit, credit.
        const columns = [34, 178, 278, 112, 112].map(w => w * (width - 2 * margin) / 714);
        const row = (cells, head = false, start = 0, count = null) => {
            const lines = cells.map((v, i) => Array.isArray(v) ? v : wrap(v, columns[i] - 16));
            const n = count ?? Math.max(...lines.map(x => x.length));
            const rowHeight = n * rowStep + 12;
            let right = width - margin;
            for (let i = 0; i < columns.length; i++) {
                const left = right - columns[i];
                ctx.fillStyle = head ? (design?.accent || '#edf0fa') : '#fff'; ctx.fillRect(left, y, columns[i], rowHeight);
                ctx.strokeStyle = '#dce2ec'; ctx.lineWidth = .6; ctx.strokeRect(left, y, columns[i], rowHeight);
                lines[i].slice(start, start + n).forEach((v, j) => text(v, right - 8, y + rowStep + j * rowStep, baseFont, head, head && design ? "#ffffff" : "#20304a"));
                right = left;
            }
            y += rowHeight;
        };
        const headings = () => row(['#', 'رقم الحساب / اسم الحساب', 'الوصف / المستند / المركز', 'مدين', 'دائن'], true);
        if (y > bottom - 70) { finish(); newPage(); }
        headings();
        for (const line of report.lines || []) {
            const detail = [line.description, line.documentReference ? `مرجع: ${line.documentReference}` : '',
                line.documentDate ? `تاريخ: ${date(line.documentDate)}` : '', line.center ? `مركز: ${line.center}` : ''].filter(Boolean).join('\n');
            const cells = [String(line.number), `${line.accountCode}\n${line.accountName}`, detail, money(line.debit), money(line.credit)];
            const wrapped = cells.map((v, i) => wrap(v, columns[i] - 16));
            const length = Math.max(...wrapped.map(x => x.length));
            // Keep ordinary rows together; split only rows taller than a full page.
            const fullHeight = length * rowStep + 12;
            if (y + fullHeight > bottom && fullHeight <= bottom - 125) { finish(); newPage(); headings(); }
            let start = 0;
            while (start < length) {
                if (y + 31 > bottom) { finish(); newPage(); headings(); }
                const count = Math.min(length - start, Math.floor((bottom - y - 12) / rowStep));
                row(wrapped, false, start, count); start += count;
            }
        }
        if (y + 50 > bottom) { finish(); newPage(); headings(); }
        row(['', 'الإجمالي', '', money((report.lines || []).reduce((s, x) => s + Number(x.debit || 0), 0)), money((report.lines || []).reduce((s, x) => s + Number(x.credit || 0), 0))], true);
        if (design) { y += 12; bodyZone('after'); }
        finish();

        const encoder = new TextEncoder(), chunks = [], offsets = [0]; let position = 0;
        const add = data => { const bytes = typeof data === 'string' ? encoder.encode(data) : data; chunks.push(bytes); position += bytes.length; };
        const object = (id, content) => { offsets[id] = position; add(`${id} 0 obj\n${content}\nendobj\n`); };
        add('%PDF-1.4\n');
        object(1, '<< /Type /Catalog /Pages 2 0 R >>');
        object(2, `<< /Type /Pages /Count ${images.length} /Kids [${images.map((_, i) => `${3 + i * 3} 0 R`).join(' ')}] >>`);
        images.forEach((jpeg, i) => {
            const page = 3 + i * 3, content = page + 1, picture = page + 2;
            object(page, `<< /Type /Page /Parent 2 0 R /MediaBox [0 0 595.28 841.89] /Resources << /XObject << /Im ${picture} 0 R >> >> /Contents ${content} 0 R >>`);
            const commands = 'q 595.28 0 0 841.89 0 0 cm /Im Do Q\n';
            object(content, `<< /Length ${encoder.encode(commands).length} >>\nstream\n${commands}endstream`);
            offsets[picture] = position;
            add(`${picture} 0 obj\n<< /Type /XObject /Subtype /Image /Width ${width * 2} /Height ${height * 2} /ColorSpace /DeviceRGB /BitsPerComponent 8 /Filter /DCTDecode /Length ${jpeg.length} >>\nstream\n`);
            add(jpeg); add('\nendstream\nendobj\n');
        });
        const xref = position;
        add(`xref\n0 ${offsets.length}\n0000000000 65535 f \n`);
        offsets.slice(1).forEach(offset => add(`${String(offset).padStart(10, '0')} 00000 n \n`));
        add(`trailer\n<< /Size ${offsets.length} /Root 1 0 R >>\nstartxref\n${xref}\n%%EOF`);
        const url = URL.createObjectURL(new Blob(chunks, { type: 'application/pdf' }));
        const link = document.createElement('a'); link.href = url; link.download = `قيد_${report.entryId}.pdf`;
        document.body.appendChild(link); link.click(); link.remove();
        setTimeout(() => URL.revokeObjectURL(url), 30000);
    }
};
