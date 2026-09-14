export function open(dialog) {
    if (!dialog.open) dialog.showModal();
}

export function close(dialog) {
    if (dialog.open) dialog.close();
}
