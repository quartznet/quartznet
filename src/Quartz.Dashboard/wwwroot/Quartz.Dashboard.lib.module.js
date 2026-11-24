export function afterWebStarted() {
    window.quartzDashboardClipboard = window.quartzDashboardClipboard || {
        copyText: async function (text) {
            const normalized = text ?? "";
            try {
                if (navigator.clipboard && navigator.clipboard.writeText) {
                    await navigator.clipboard.writeText(normalized);
                    return true;
                }
            }
            catch {
            }

            try {
                const textArea = document.createElement("textarea");
                textArea.value = normalized;
                textArea.setAttribute("readonly", "");
                textArea.style.position = "fixed";
                textArea.style.opacity = "0";
                textArea.style.pointerEvents = "none";
                document.body.appendChild(textArea);
                textArea.select();
                textArea.setSelectionRange(0, normalized.length);
                const copied = document.execCommand("copy");
                document.body.removeChild(textArea);
                return copied;
            }
            catch {
                return false;
            }
        }
    };

    window.quartzDashboardPrefs = window.quartzDashboardPrefs || {
        get: function (key) {
            try {
                const localStorageValue = window.localStorage.getItem(key);
                if (localStorageValue) {
                    return localStorageValue;
                }
            }
            catch {
            }

            const encodedKey = encodeURIComponent(key) + "=";
            const cookieParts = document.cookie ? document.cookie.split("; ") : [];
            for (const cookiePart of cookieParts) {
                if (!cookiePart.startsWith(encodedKey)) {
                    continue;
                }

                return decodeURIComponent(cookiePart.substring(encodedKey.length));
            }

            return null;
        },
        set: function (key, value) {
            try {
                window.localStorage.setItem(key, value);
            }
            catch {
            }

            const basePath = new URL(document.baseURI).pathname;
            const encodedKey = encodeURIComponent(key);
            const encodedValue = encodeURIComponent(value);
            document.cookie = encodedKey + "=" + encodedValue + "; path=" + basePath + "; max-age=31536000; samesite=lax";
        }
    };

    window.quartzDashboardDialog = window.quartzDashboardDialog || (function () {
        // What had focus as each dialog still open opened, newest last: focus goes back to it as its dialog closes.
        const openers = [];
        const tabbable = "a[href], button:not([disabled]), input:not([disabled]):not([type='hidden']), select:not([disabled]), textarea:not([disabled]), [tabindex]:not([tabindex='-1'])";

        const controls = function (dialog) {
            return Array.from(dialog.querySelectorAll(tabbable)).filter(function (element) {
                return element.getClientRects().length > 0;
            });
        };

        return {
            /**
             * ModalDialog's first render: remembers what had focus, the control that opened the dialog, then moves
             * focus to the dialog itself.
             */
            open: function (dialog) {
                openers.push(document.activeElement instanceof HTMLElement ? document.activeElement : null);
                if (dialog) {
                    dialog.focus();
                }
            },

            /** Tab past the dialog's last control, onto the closing guard: back to its first. */
            focusFirst: function (dialog) {
                if (!dialog) {
                    return;
                }

                const items = controls(dialog);
                (items[0] || dialog).focus();
            },

            /** Shift+Tab before the dialog's first control, onto the opening guard: round to its last. */
            focusLast: function (dialog) {
                if (!dialog) {
                    return;
                }

                const items = controls(dialog);
                (items[items.length - 1] || dialog).focus();
            },

            /** The dialog went: focus returns to what opened it, if that is still on the page. */
            close: function () {
                const opener = openers.pop();
                if (opener && opener.isConnected) {
                    opener.focus();
                }
            }
        };
    })();
}
