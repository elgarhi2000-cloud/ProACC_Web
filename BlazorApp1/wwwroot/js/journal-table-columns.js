(function () {
    const canvas = document.createElement("canvas");
    const context = canvas.getContext("2d");

    function clamp(value, minimum, maximum) {
        return Math.min(maximum, Math.max(minimum, value));
    }

    function getCellText(cell) {
        const input = cell.querySelector("input, textarea");
        if (input) return input.value || input.placeholder || "";
        const labeledControl = cell.querySelector("button span");
        if (labeledControl) return labeledControl.textContent.trim();
        return cell.textContent.trim();
    }

    function measureCell(cell) {
        const style = window.getComputedStyle(cell);
        context.font = `${style.fontWeight} ${style.fontSize} ${style.fontFamily}`;
        const textWidth = context.measureText(getCellText(cell)).width;
        const padding = parseFloat(style.paddingLeft || 0) + parseFloat(style.paddingRight || 0);
        const controlsAllowance = cell.querySelector("button, input, textarea") ? 30 : 14;
        return Math.ceil(textWidth + padding + controlsAllowance);
    }

    function updateTableWidth(table) {
        const headers = Array.from(table.tHead?.rows[0]?.cells || []);
        const total = headers.reduce((sum, header) => {
            const assignedWidth = parseFloat(header.style.width);
            return sum + (Number.isFinite(assignedWidth) ? assignedWidth : header.getBoundingClientRect().width);
        }, 0);
        const available = table.parentElement?.clientWidth || 0;
        table.style.width = `${Math.max(total, available)}px`;
    }

    function fitColumn(table, columnIndex) {
        const header = table.tHead?.rows[0]?.cells[columnIndex];
        if (!header) return;

        const minimum = Number(header.dataset.minWidth || 60);
        const maximum = Number(header.dataset.maxWidth || 500);
        let desired = measureCell(header);

        for (const body of Array.from(table.tBodies)) {
            for (const row of Array.from(body.rows)) {
                const cell = row.cells[columnIndex];
                if (cell) desired = Math.max(desired, measureCell(cell));
            }
        }

        header.style.width = `${clamp(desired, minimum, maximum)}px`;
    }

    function fitTable(table) {
        const headers = Array.from(table.tHead?.rows[0]?.cells || []);
        headers.forEach((_, index) => fitColumn(table, index));
        updateTableWidth(table);
    }

    window.journalEntryTableColumns = {
        fit(tableId) {
            const table = document.getElementById(tableId);
            if (!table) return;
            window.requestAnimationFrame(() => fitTable(table));
        }
    };

    document.addEventListener("dblclick", (event) => {
        const handle = event.target.closest(".journal-column-resizer");
        if (!handle) return;
        const header = handle.closest("th");
        const table = handle.closest("table");
        if (!header || !table) return;
        event.preventDefault();
        fitColumn(table, header.cellIndex);
        updateTableWidth(table);
    });

    document.addEventListener("pointerdown", (event) => {
        const handle = event.target.closest(".journal-column-resizer");
        if (!handle) return;

        const header = handle.closest("th");
        const table = handle.closest("table");
        if (!header || !table) return;

        event.preventDefault();
        const minimum = Number(header.dataset.minWidth || 60);
        const maximum = Number(header.dataset.maxWidth || 500);
        const startX = event.clientX;
        const startWidth = header.getBoundingClientRect().width;
        const startTableWidth = table.getBoundingClientRect().width;
        const isRtl = window.getComputedStyle(table).direction === "rtl";

        handle.classList.add("is-resizing");
        table.classList.add("is-resizing-columns");
        handle.setPointerCapture?.(event.pointerId);

        const onMove = (moveEvent) => {
            const rawDelta = moveEvent.clientX - startX;
            const delta = isRtl ? -rawDelta : rawDelta;
            const width = clamp(startWidth + delta, minimum, maximum);
            header.style.width = `${width}px`;
            table.style.width = `${Math.max(startTableWidth + (width - startWidth), table.parentElement?.clientWidth || 0)}px`;
        };

        const onEnd = () => {
            handle.classList.remove("is-resizing");
            table.classList.remove("is-resizing-columns");
            document.removeEventListener("pointermove", onMove);
            document.removeEventListener("pointerup", onEnd);
            document.removeEventListener("pointercancel", onEnd);
        };

        document.addEventListener("pointermove", onMove);
        document.addEventListener("pointerup", onEnd);
        document.addEventListener("pointercancel", onEnd);
    });
})();
