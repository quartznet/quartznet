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
        // The elements that opened the dialogs still open, newest last: a dialog opened from a dialog returns
        // focus to the first one when it closes.
        const openers = [];
        const tabbable = "a[href], button:not([disabled]), input:not([disabled]):not([type='hidden']), select:not([disabled]), textarea:not([disabled]), [tabindex]:not([tabindex='-1'])";

        const controls = function (dialog) {
            return Array.from(dialog.querySelectorAll(tabbable)).filter(function (element) {
                return element.getClientRects().length > 0;
            });
        };

        return {
            /**
             * ModalDialog's first render: remembers what had focus, then moves focus into the dialog, to the
             * element marked data-autofocus or to the dialog itself.
             */
            open: function (dialog) {
                openers.push(document.activeElement instanceof HTMLElement ? document.activeElement : null);
                if (!dialog) {
                    return;
                }

                const preferred = dialog.querySelector("[data-autofocus]");
                (preferred || dialog).focus();
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

    window.quartzDashboardKeys = window.quartzDashboardKeys || {
        /**
         * A grid that moves focus with the arrow keys in C#: the keys must not also scroll the page. Blazor cannot
         * prevent a default for some keys and not others, so the element does it for those keys alone.
         */
        holdArrowKeys: function (element) {
            if (!element || element.__qzHoldArrowKeys) {
                return;
            }

            const keys = ["ArrowUp", "ArrowDown", "ArrowLeft", "ArrowRight", "Home", "End"];
            element.__qzHoldArrowKeys = function (event) {
                if (keys.indexOf(event.key) >= 0) {
                    event.preventDefault();
                }
            };

            element.addEventListener("keydown", element.__qzHoldArrowKeys);
        }
    };

    window.quartzDashboardVisibility = window.quartzDashboardVisibility || (function () {
        const observers = new Map();
        let lastId = 0;

        return {
            /**
             * Tells a polling page (AutoRefresh.OnVisibilityChanged) when its tab is hidden and when it is shown,
             * so its timer stops while nobody can see it. A tab that is hidden already is reported at once.
             * Answers the id unobserve takes; 0 is never one.
             */
            observe: function (dotNetRef) {
                if (!dotNetRef) {
                    return 0;
                }

                const id = ++lastId;
                const notify = function () {
                    dotNetRef.invokeMethodAsync("OnVisibilityChanged", document.visibilityState !== "hidden")
                        .catch(function () { });
                };

                observers.set(id, notify);
                document.addEventListener("visibilitychange", notify);
                if (document.visibilityState === "hidden") {
                    notify();
                }

                return id;
            },

            unobserve: function (id) {
                const notify = observers.get(id);
                if (!notify) {
                    return;
                }

                document.removeEventListener("visibilitychange", notify);
                observers.delete(id);
            }
        };
    })();

    window.quartzDashboardDom = window.quartzDashboardDom || {
        /**
         * Axis pan without Blazor pointermove traffic: preview locally, commit once on pointerup.
         */
        attachTimelineAxisPan: function (axisElement, timelineRoot, dotNetRef, options) {
            if (!axisElement || !dotNetRef) {
                return;
            }

            this.detachTimelineAxisPan(axisElement);

            const state = {
                dotNetRef: dotNetRef,
                timelineRoot: timelineRoot,
                windowSpanSeconds: Number(options?.windowSpanSeconds) || 0,
                panSeconds: Number(options?.panSeconds) || 0,
                maxPanSeconds: Number(options?.maxPanSeconds) || 0,
                panning: false,
                pointerId: null,
                startX: 0,
                startPanSeconds: 0,
                widthPx: 0,
                onPointerDown: null,
                onPointerMove: null,
                onPointerUp: null
            };

            const clearPreview = function () {
                axisElement.style.transform = "";
                axisElement.classList.remove("qz-timeline-axis-track-panning");
                if (!state.timelineRoot) {
                    return;
                }

                const tracks = state.timelineRoot.querySelectorAll(".qz-timeline-trigger-track");
                for (let i = 0; i < tracks.length; i++) {
                    tracks[i].style.transform = "";
                }
            };

            const applyPreview = function (translatePx) {
                const value = translatePx ? ("translateX(" + translatePx + "px)") : "";
                axisElement.style.transform = value;
                if (!state.timelineRoot) {
                    return;
                }

                const tracks = state.timelineRoot.querySelectorAll(".qz-timeline-trigger-track");
                for (let i = 0; i < tracks.length; i++) {
                    tracks[i].style.transform = value;
                }
            };

            const clampPan = function (seconds) {
                if (!(seconds > 0)) {
                    return 0;
                }

                if (state.maxPanSeconds > 0 && seconds > state.maxPanSeconds) {
                    return state.maxPanSeconds;
                }

                return seconds;
            };

            const panFromPointer = function (clientX) {
                if (!(state.widthPx > 1) || !(state.windowSpanSeconds > 0)) {
                    return state.startPanSeconds;
                }

                const deltaFraction = (state.startX - clientX) / state.widthPx;
                return clampPan(state.startPanSeconds + (state.windowSpanSeconds * deltaFraction));
            };

            state.onPointerDown = function (event) {
                if (event.button !== undefined && event.button !== 0) {
                    return;
                }

                state.panning = true;
                state.pointerId = event.pointerId;
                state.startX = event.clientX;
                state.startPanSeconds = state.panSeconds;
                state.widthPx = axisElement.getBoundingClientRect().width;
                axisElement.classList.add("qz-timeline-axis-track-panning");
                try {
                    axisElement.setPointerCapture(event.pointerId);
                }
                catch {
                }
            };

            state.onPointerMove = function (event) {
                if (!state.panning || event.pointerId !== state.pointerId) {
                    return;
                }

                // Drag left → older (positive pan) → content shifts right.
                applyPreview(state.startX - event.clientX);
            };

            state.onPointerUp = function (event) {
                if (!state.panning || (state.pointerId !== null && event.pointerId !== state.pointerId)) {
                    return;
                }

                state.panning = false;
                const movedPx = Math.abs(event.clientX - state.startX);
                const nextPan = panFromPointer(event.clientX);
                clearPreview();
                try {
                    axisElement.releasePointerCapture(event.pointerId);
                }
                catch {
                }

                state.pointerId = null;
                if (movedPx < 3) {
                    return;
                }

                if (Math.abs(nextPan - state.startPanSeconds) < 0.5) {
                    return;
                }

                state.panSeconds = nextPan;
                state.dotNetRef.invokeMethodAsync("OnAxisPanCommitted", nextPan).catch(function () { });
            };

            axisElement.addEventListener("pointerdown", state.onPointerDown);
            axisElement.addEventListener("pointermove", state.onPointerMove);
            axisElement.addEventListener("pointerup", state.onPointerUp);
            axisElement.addEventListener("pointercancel", state.onPointerUp);
            axisElement.__qzTimelineAxisPan = state;
        },

        updateTimelineAxisPan: function (axisElement, options) {
            const state = axisElement && axisElement.__qzTimelineAxisPan;
            if (!state) {
                return;
            }

            if (state.panning) {
                return;
            }

            state.windowSpanSeconds = Number(options?.windowSpanSeconds) || 0;
            state.panSeconds = Number(options?.panSeconds) || 0;
            state.maxPanSeconds = Number(options?.maxPanSeconds) || 0;
        },

        detachTimelineAxisPan: function (axisElement) {
            const state = axisElement && axisElement.__qzTimelineAxisPan;
            if (!state) {
                return;
            }

            axisElement.removeEventListener("pointerdown", state.onPointerDown);
            axisElement.removeEventListener("pointermove", state.onPointerMove);
            axisElement.removeEventListener("pointerup", state.onPointerUp);
            axisElement.removeEventListener("pointercancel", state.onPointerUp);
            axisElement.style.transform = "";
            axisElement.classList.remove("qz-timeline-axis-track-panning");
            delete axisElement.__qzTimelineAxisPan;
        },

        /**
         * Put .qz-content back at the top after a path change. Query-only navigations keep the offset.
         */
        resetScrollTop: function (element) {
            if (!element) {
                return;
            }

            element.scrollTop = 0;
        },

        /**
         * Toggle .qz-timeline-sticky-pinned when stuck under .qz-header (drops top radius). DOM only.
         */
        attachTimelineStickyPin: function (stickyElement) {
            if (!stickyElement) {
                return;
            }

            this.detachTimelineStickyPin(stickyElement);

            const scrollRoot = stickyElement.closest(".qz-content");
            // -1px top rootMargin: stuck flush → not fully visible → ratio < 1.
            const observer = new IntersectionObserver(
                function (entries) {
                    const entry = entries[0];
                    if (!entry) {
                        return;
                    }

                    stickyElement.classList.toggle(
                        "qz-timeline-sticky-pinned",
                        entry.intersectionRatio < 1);
                },
                {
                    root: scrollRoot,
                    threshold: [1],
                    rootMargin: "-1px 0px 0px 0px"
                });

            observer.observe(stickyElement);
            stickyElement.__qzTimelineStickyPin = observer;
        },

        detachTimelineStickyPin: function (stickyElement) {
            const observer = stickyElement && stickyElement.__qzTimelineStickyPin;
            if (!observer) {
                return;
            }

            observer.disconnect();
            stickyElement.classList.remove("qz-timeline-sticky-pinned");
            delete stickyElement.__qzTimelineStickyPin;
        },

        /**
         * Space below and above a chevron for the Timeline actions menu. C# decides whether to flip
         * (TimelineService.ActionsMenuOpensUp); the default menu height lives only there.
         */
        actionsMenuSpace: function (anchorElement) {
            if (!anchorElement || typeof anchorElement.getBoundingClientRect !== "function") {
                return { spaceBelow: 0, spaceAbove: 0 };
            }

            const rect = anchorElement.getBoundingClientRect();
            return {
                spaceBelow: window.innerHeight - rect.bottom,
                spaceAbove: rect.top
            };
        }
    };
}
