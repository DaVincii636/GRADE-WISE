// Account > Change your password (Views/Account/ForceChangePassword.cshtml), shown after signing in
// with a temporary password. Client-side help only: show / hide buttons, the password rules ticked off
// while typing, a "passwords match" line, and messages under the fields before the form is sent.
// The server checks everything again (ValidationHelper.PasswordProblem and the match check in
// AccountController.ForceChangePassword); the rules and messages here are the same as there.

(function () {
    var formEl = document.getElementById("gw-forcepw-form");
    if (!(formEl instanceof HTMLFormElement)) { return; }
    var form = formEl;

    /** @param {string} id @returns {HTMLInputElement} */
    function requireInput(id) {
        var el = document.getElementById(id);
        if (!(el instanceof HTMLInputElement)) { throw new Error("Change password page: #" + id + " is missing."); }
        return el;
    }
    /** @param {string} id @returns {HTMLElement} */
    function requireElement(id) {
        var el = document.getElementById(id);
        if (!el) { throw new Error("Change password page: #" + id + " is missing."); }
        return el;
    }

    var newPw = requireInput("newPassword");
    var confirmPw = requireInput("confirmPassword");
    var newError = requireElement("newPassword-error");
    var confirmError = requireElement("confirmPassword-error");
    var rulesList = requireElement("gw-pw-rules");
    var matchLine = requireElement("gw-pw-match");
    var submitBtn = form.querySelector('button[type="submit"]');

    // the same limits as ValidationHelper (passed in by the page)
    var MIN = parseInt(rulesList.getAttribute("data-min") || "8", 10);
    var MAX = parseInt(rulesList.getAttribute("data-max") || "128", 10);
    // char.IsLetter / char.IsDigit on the server: any Unicode letter / decimal digit.
    // (Built with RegExp so older script targets accept the file; very old browsers fall back to A-Z / 0-9.)
    var LETTER = unicodeClass("\\p{L}", /[A-Za-z]/);
    var DIGIT = unicodeClass("\\p{Nd}", /[0-9]/);

    /**
     * @param {string} pattern  a Unicode property escape
     * @param {RegExp} fallback
     * @returns {RegExp}
     */
    function unicodeClass(pattern, fallback) {
        try { return new RegExp(pattern, "u"); } catch (err) { return fallback; }
    }

    var tried = false;   // after the first Save, messages update while typing
    form.noValidate = true;   // the messages below replace the browser's bubbles (without JavaScript, "required" still works)

    // ---------- show / hide ----------
    var toggles = form.querySelectorAll("[data-password-toggle]");
    for (var t = 0; t < toggles.length; t++) {
        var toggleEl = toggles[t];
        if (toggleEl instanceof HTMLButtonElement) { setUpToggle(toggleEl); }
    }

    /** @param {HTMLButtonElement} toggle */
    function setUpToggle(toggle) {
        var input = document.getElementById(toggle.getAttribute("data-password-toggle") || "");
        if (!(input instanceof HTMLInputElement)) { return; }
        var field = input;
        var iconVisible = toggle.querySelector('[data-icon="visible"]');   // plain eye
        var iconHidden = toggle.querySelector('[data-icon="hidden"]');     // eye with a line through it

        function sync() {
            var visible = field.type === "text";
            if (iconVisible instanceof SVGElement) { iconVisible.style.display = visible ? "block" : "none"; }
            if (iconHidden instanceof SVGElement) { iconHidden.style.display = visible ? "none" : "block"; }
            var label = visible ? "Hide password" : "Show password";
            toggle.setAttribute("aria-pressed", visible ? "true" : "false");
            toggle.setAttribute("aria-label", label);
            toggle.title = label;
        }

        field.type = "password";   // always start hidden
        sync();
        toggle.hidden = false;
        toggle.addEventListener("click", function () {
            // keep the caret where it was
            var start = field.selectionStart, end = field.selectionEnd;
            field.type = field.type === "password" ? "text" : "password";
            sync();
            field.focus();
            if (start !== null && end !== null) { field.setSelectionRange(start, end); }
        });
    }

    // ---------- rules ----------
    /** @param {string} value */
    function rulesFor(value) {
        return {
            length: value.length >= MIN,
            letter: LETTER.test(value),
            number: DIGIT.test(value)
        };
    }

    /**
     * The server's message for a new password, or "" when it is fine (ValidationHelper.PasswordProblem).
     * @param {string} value
     */
    function passwordProblem(value) {
        if (!value) { return "Enter a new password."; }
        if (value.length > MAX) { return "The new password must be " + MAX + " characters or fewer."; }
        var r = rulesFor(value);
        if (!r.length || !r.letter || !r.number) {
            return "The new password must be at least " + MIN + " characters with at least one letter and one number.";
        }
        return "";
    }

    /** The server's message for the confirmation, or "" when it is fine. */
    function confirmProblem() {
        if (!confirmPw.value) { return "Confirm your new password."; }
        if (confirmPw.value !== newPw.value) { return "The new password and its confirmation do not match."; }
        return "";
    }

    /**
     * @param {HTMLInputElement} input
     * @param {HTMLElement} errorEl
     * @param {string} text  "" clears the message
     */
    function showError(input, errorEl, text) {
        errorEl.textContent = text;
        errorEl.hidden = !text;
        input.classList.toggle("gw-input-bad", !!text);
        input.setAttribute("aria-invalid", text ? "true" : "false");
    }

    /** Ticks the rules off while typing; a rule that is still not met is marked only after the first Save. */
    function renderRules() {
        var value = newPw.value;
        /** @type {Record<string, boolean>} */
        var met = rulesFor(value);
        var items = rulesList.querySelectorAll("[data-rule]");
        for (var i = 0; i < items.length; i++) {
            var item = items[i];
            var rule = item.getAttribute("data-rule") || "";
            var ok = !!met[rule];
            item.classList.toggle("gw-pw-rule-ok", ok);
            item.classList.toggle("gw-pw-rule-bad", !ok && tried);
            var state = item.querySelector(".gw-sr-only");
            if (!state) {
                state = document.createElement("span");
                state.className = "gw-sr-only";
                item.appendChild(state);
            }
            state.textContent = ok ? " (done)" : " (not yet)";
        }
    }

    /** "Passwords match" / "do not match yet" under the confirmation, once something is typed there. */
    function renderMatch() {
        var c = confirmPw.value;
        if (!c) {
            matchLine.hidden = true;
            matchLine.textContent = "";
            return;
        }
        var same = c === newPw.value;
        matchLine.hidden = false;
        matchLine.textContent = same ? "Passwords match." : "Passwords do not match yet.";
        matchLine.classList.toggle("gw-pw-match-ok", same);
        matchLine.classList.toggle("gw-pw-match-bad", !same);
    }

    function renderErrors() {
        if (!tried) { return; }
        showError(newPw, newError, passwordProblem(newPw.value));
        showError(confirmPw, confirmError, confirmProblem());
    }

    function update() {
        renderRules();
        renderMatch();
        renderErrors();
    }

    newPw.addEventListener("input", update);
    confirmPw.addEventListener("input", update);

    form.addEventListener("submit", function (e) {
        tried = true;
        update();
        var bad = passwordProblem(newPw.value) ? newPw : confirmProblem() ? confirmPw : null;
        if (bad) {
            e.preventDefault();
            bad.focus();
            return;
        }
        // send once
        if (submitBtn instanceof HTMLButtonElement) {
            submitBtn.disabled = true;
            submitBtn.textContent = "Saving...";
        }
    });

    update();
})();
