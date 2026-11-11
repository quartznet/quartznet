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
        }
    };
}
