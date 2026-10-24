// The embedded feedback control. No framework, no third-party code: a native <dialog>, fetch, and the
// browser's own screen-capture picker for the optional screenshot.
//
// Accessibility notes live in _Feedback.cshtml. What this file adds: focus returns to the button on close,
// the first field is focused on open, the character counter and status are updated in their live regions,
// and the error region is written as text (never colour alone).
(function () {
    "use strict";

    var openButton = document.getElementById("feedback-open");
    var dialog = document.getElementById("feedback-dialog");
    var form = document.getElementById("feedback-form");
    if (!openButton || !dialog || !form || typeof dialog.showModal !== "function") {
        return; // Without <dialog> support the control stays hidden rather than half-working.
    }

    var message = document.getElementById("feedback-message");
    var counter = document.getElementById("feedback-count");
    var screenshotBox = document.getElementById("feedback-screenshot");
    var error = document.getElementById("feedback-error");
    var status = document.getElementById("feedback-status");
    var sendButton = document.getElementById("feedback-send");
    var cancelButton = document.getElementById("feedback-cancel");
    var maxChars = parseInt(message.getAttribute("maxlength"), 10) || 2000;
    var maxScreenshotBytes = 1024 * 1024;
    var lastFocus = null;

    openButton.hidden = false;

    function setError(text) {
        error.textContent = text || "";
    }

    function setStatus(text) {
        status.textContent = text || "";
    }

    function updateCounter() {
        var left = maxChars - message.value.length;
        counter.textContent = left.toLocaleString() + (left === 1 ? " character left" : " characters left");
    }

    function open() {
        lastFocus = document.activeElement;
        setError("");
        setStatus("");
        updateCounter();
        dialog.showModal();
        message.focus();
    }

    function close() {
        dialog.close();
    }

    dialog.addEventListener("close", function () {
        // showModal restores focus in most browsers; do it explicitly so it is not browser-dependent.
        if (lastFocus && typeof lastFocus.focus === "function") {
            lastFocus.focus();
        } else {
            openButton.focus();
        }
    });

    // Native <dialog> keeps focus out of the page behind, but Chromium lets Tab visit the browser chrome
    // between the last and first control. The APG dialog pattern wraps instead, so do that explicitly.
    dialog.addEventListener("keydown", function (event) {
        if (event.key !== "Tab") {
            return;
        }
        var tabbable = Array.prototype.filter.call(
            dialog.querySelectorAll("button, textarea, input, select, a[href], [tabindex]:not([tabindex='-1'])"),
            function (el) { return !el.disabled && el.offsetParent !== null; });
        if (tabbable.length === 0) {
            return;
        }
        var first = tabbable[0];
        var last = tabbable[tabbable.length - 1];
        if (event.shiftKey && document.activeElement === first) {
            event.preventDefault();
            last.focus();
        } else if (!event.shiftKey && document.activeElement === last) {
            event.preventDefault();
            first.focus();
        }
    });

    // Clicking the backdrop closes the dialog; a click inside does not.
    dialog.addEventListener("click", function (event) {
        if (event.target === dialog) {
            close();
        }
    });

    openButton.addEventListener("click", open);
    cancelButton.addEventListener("click", close);
    message.addEventListener("input", updateCounter);

    // One frame of the tab the person chooses, as a PNG data URL under the cap. Scaled down and retried
    // when the first encoding is too large. Resolves to null when the person cancels the picker.
    function captureScreenshot() {
        if (!navigator.mediaDevices || !navigator.mediaDevices.getDisplayMedia) {
            return Promise.reject(new Error("unsupported"));
        }
        return navigator.mediaDevices.getDisplayMedia({ video: { displaySurface: "browser" }, audio: false, preferCurrentTab: true })
            .then(function (stream) {
                var video = document.createElement("video");
                video.srcObject = stream;
                video.muted = true;
                return video.play().then(function () {
                    return new Promise(function (resolve) { setTimeout(resolve, 150); }); // let the first frame land
                }).then(function () {
                    var scale = 1;
                    var dataUrl = null;
                    for (var attempt = 0; attempt < 4; attempt++) {
                        var canvas = document.createElement("canvas");
                        canvas.width = Math.max(1, Math.round(video.videoWidth * scale));
                        canvas.height = Math.max(1, Math.round(video.videoHeight * scale));
                        canvas.getContext("2d").drawImage(video, 0, 0, canvas.width, canvas.height);
                        dataUrl = canvas.toDataURL("image/png");
                        if (dataUrl.length * 3 / 4 <= maxScreenshotBytes) {
                            break;
                        }
                        dataUrl = null;
                        scale = scale * 0.6;
                    }
                    stream.getTracks().forEach(function (track) { track.stop(); });
                    return dataUrl;
                });
            })
            .catch(function (err) {
                if (err && (err.name === "NotAllowedError" || err.name === "AbortError")) {
                    return null; // the person cancelled the picker
                }
                throw err;
            });
    }

    function tokenValue() {
        var input = form.querySelector("input[name='__RequestVerificationToken']");
        return input ? input.value : "";
    }

    form.addEventListener("submit", function (event) {
        event.preventDefault();
        setError("");
        var text = message.value.trim();
        if (!text) {
            setError("Type what happened before sending.");
            message.focus();
            return;
        }
        if (text.length > maxChars) {
            setError("Keep it under " + maxChars.toLocaleString() + " characters.");
            message.focus();
            return;
        }

        sendButton.disabled = true;
        cancelButton.disabled = true;
        setStatus("Sending...");

        var capture = screenshotBox && screenshotBox.checked ? captureScreenshot() : Promise.resolve(null);
        capture.then(function (screenshot) {
            if (screenshotBox && screenshotBox.checked && !screenshot) {
                setStatus("No screenshot was attached. Sending the message only.");
            }
            return fetch(form.getAttribute("action"), {
                method: "POST",
                credentials: "same-origin",
                headers: {
                    "Content-Type": "application/json",
                    "RequestVerificationToken": tokenValue()
                },
                body: JSON.stringify({ message: text, screenshotDataUrl: screenshot })
            });
        }).then(function (response) {
            // An expired session is answered with a redirect to sign-in, which fetch follows to a 200 HTML
            // page. That is not success: the endpoint never saw the message.
            if (response.redirected) {
                throw new Error("Your session has ended. Reload the page, sign in, and send again.");
            }
            return response.json().catch(function () { return {}; }).then(function (body) {
                if (!response.ok) {
                    throw new Error(body.error || ("Your feedback could not be sent (" + response.status + ")."));
                }
                if (!body || typeof body.sink !== "string") {
                    throw new Error("The server answered in an unexpected way. Reload the page and try again.");
                }
                var reference = body.reference && body.reference !== "logged" ? " Ticket " + body.reference + "." : "";
                setStatus("Thank you. Your feedback was sent." + reference);
                message.value = "";
                if (screenshotBox) { screenshotBox.checked = false; }
                updateCounter();
                setTimeout(close, 2500);
            });
        }).catch(function (err) {
            setStatus("");
            setError(err && err.message === "unsupported"
                ? "This browser cannot take a screenshot here. Untick the box and send the message only."
                : (err && err.message) || "Your feedback could not be sent. Try again later.");
        }).then(function () {
            sendButton.disabled = false;
            cancelButton.disabled = false;
        });
    });
})();
