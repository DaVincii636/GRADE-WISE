/// <reference path="gw-globals.d.ts" />
// Admin > Add user page (Views/Admin/CreateUser.cshtml).
// Moved out of the view so Visual Studio checks it as plain JavaScript: JSDoc type notes
// cannot be written reliably inside a Razor view (the @ has to be escaped there).

(function () {
    var formEl = document.getElementById("user-form");
    if (!(formEl instanceof HTMLFormElement)) { return; }
    var form = formEl;

    // ---------- element lookups ----------
    // Each one checks that the element is on the page and is the expected kind, and stops the
    // script with a clear message if the markup was changed, instead of failing later.
    /** @param {string} id */
    function missing(id) { return new Error("Add user page: #" + id + " is missing or is the wrong kind of element."); }
    /** @param {string} id @returns {HTMLElement} */
    function requireElement(id) {
        var el = document.getElementById(id);
        if (!el) { throw missing(id); }
        return el;
    }
    /** @param {string} id @returns {HTMLInputElement} */
    function requireInput(id) {
        var el = document.getElementById(id);
        if (!(el instanceof HTMLInputElement)) { throw missing(id); }
        return el;
    }
    /** @param {string} id @returns {HTMLSelectElement} */
    function requireSelect(id) {
        var el = document.getElementById(id);
        if (!(el instanceof HTMLSelectElement)) { throw missing(id); }
        return el;
    }
    /** @param {string} id @returns {HTMLButtonElement} */
    function requireButton(id) {
        var el = document.getElementById(id);
        if (!(el instanceof HTMLButtonElement)) { throw missing(id); }
        return el;
    }

    // ---------- rules ----------
    // Sign-in ID per role. The server accepts letters, numbers, dots, dashes and underscores (up to 50),
    // and sign-in matches it without regard to case, so these all stay compatible.
    /** @typedef {{ label: string, prefix: string, placeholder: string, hint: string, inputMode: string, maxLength: number }} IdRule */
    /** @type {Record<string, IdRule>} */
    var ID_RULES = {
        "Student": {
            label: "Student number",
            prefix: "",
            placeholder: "26-0001",
            hint: "Format YY-0000: two-digit year, a dash, then four digits (e.g. 26-0001).",
            inputMode: "numeric",
            maxLength: 7
        },
        "Faculty": {
            label: "Faculty username",
            prefix: "fac_",
            placeholder: "juan",
            hint: "Starts with fac_. Use lowercase letters, numbers, dots or underscores (e.g. fac_juan).",
            inputMode: "text",
            maxLength: 40
        },
        "Administrator": {
            label: "Admin username",
            prefix: "adm_",
            placeholder: "juan",
            hint: "Starts with adm_. Use lowercase letters, numbers, dots or underscores (e.g. adm_juan).",
            inputMode: "text",
            maxLength: 40
        }
    };
    var STUDENT_NO = /^\d{2}-\d{4}$/;
    var USERNAME = /^[a-z0-9](?:[a-z0-9._]{0,38}[a-z0-9])?$/;
    // letters (including accented ones), with spaces, dots, apostrophes and hyphens between them
    var NAME = /^[A-Za-z\u00C0-\u00D6\u00D8-\u00F6\u00F8-\u024F](?:[A-Za-z\u00C0-\u00D6\u00D8-\u00F6\u00F8-\u024F .'-]*[A-Za-z\u00C0-\u00D6\u00D8-\u00F6\u00F8-\u024F.])?$/;
    // name@domain.ext: no spaces, no double dots, domain labels of letters / digits / hyphens,
    // and a final extension of at least two letters (.com, .ph, .edu ...)
    var EMAIL = /^[A-Za-z0-9!#$%&'*+\/=?^_`{|}~-]+(?:\.[A-Za-z0-9!#$%&'*+\/=?^_`{|}~-]+)*@(?:[A-Za-z0-9](?:[A-Za-z0-9-]{0,61}[A-Za-z0-9])?\.)+[A-Za-z]{2,24}$/;

    // ---------- elements ----------
    /** @type {HTMLInputElement[]} */
    var radios = [];
    var roleInputs = form.querySelectorAll('input[name="role"]');
    for (var ri = 0; ri < roleInputs.length; ri++) {
        var roleInput = roleInputs[ri];
        if (roleInput instanceof HTMLInputElement) { radios.push(roleInput); }
    }
    var chooseBox = requireElement("role-choose");
    var lockedBox = requireElement("role-locked");
    var lockedName = requireElement("role-locked-name");
    var roleError = requireElement("role-error");
    var details = requireElement("user-details");
    var first = requireInput("firstName");
    var middle = requireInput("middleName");
    var last = requireInput("lastName");
    var suffix = requireSelect("suffix");
    var namePreview = requireElement("name-preview");
    var nameError = requireElement("name-error");
    var loginLabel = requireElement("login-label");
    var loginPrefix = requireElement("login-prefix");
    var loginPart = requireInput("login-part");
    var loginHint = requireElement("login-hint");
    var email = requireInput("email");
    var fullNameOut = requireInput("fullName");
    var loginIdOut = requireInput("loginId");
    var submit = requireButton("user-submit");
    var role = "";
    /** @type {Record<string, boolean>} fields the user has left at least once */
    var touched = {};
    var tried = false;

    /** @param {string | null | undefined} value */
    function clean(value) { return (value || "").replace(/\s+/g, " ").trim(); }

    function focusFirstRole() { if (radios.length) { radios[0].focus(); } }

    /**
     * Shows (or clears) the message under a field and marks the field invalid.
     * @param {HTMLElement | null} input  the field, or null for messages that belong to a group
     * @param {HTMLElement | null} errorEl  the message element under it
     * @param {string} text  "" clears the message
     */
    function showError(input, errorEl, text) {
        if (!errorEl) { return; }
        errorEl.textContent = text || "";
        errorEl.hidden = !text;
        if (input) {
            input.classList.toggle("gw-input-bad", !!text);
            input.setAttribute("aria-invalid", text ? "true" : "false");
        }
    }

    // ---------- role ----------
    /**
     * @param {string} value  the chosen role
     * @param {boolean} focusNext  move on to the first name field
     */
    function lockRole(value, focusNext) {
        role = value;
        for (var i = 0; i < radios.length; i++) {
            radios[i].checked = radios[i].value === value;
            radios[i].disabled = radios[i].value !== value;   // the chosen one is still posted
        }
        lockedName.textContent = value;
        chooseBox.hidden = true;
        lockedBox.hidden = false;
        details.hidden = false;
        showError(null, roleError, "");
        applyIdRule();
        if (focusNext) { first.focus(); }
    }

    function unlockRole() {
        role = "";
        for (var i = 0; i < radios.length; i++) {
            radios[i].checked = false;
            radios[i].disabled = false;
        }
        loginPart.value = "";
        loginIdOut.value = "";
        delete touched.login;
        chooseBox.hidden = false;
        lockedBox.hidden = true;
        details.hidden = true;
        focusFirstRole();
    }

    radios.forEach(function (radio) {
        radio.addEventListener("change", function () { if (radio.checked) { lockRole(radio.value, true); } });
    });

    // ---------- change role (with confirmation) ----------
    var confirmModal = requireElement("role-confirm");
    var confirmId = requireElement("role-confirm-id");
    var changeBtn = requireButton("role-change");
    var confirmYes = requireButton("role-confirm-yes");

    /** @param {boolean} back  return focus to the Change role button */
    function closeConfirm(back) {
        confirmModal.hidden = true;
        document.body.classList.remove("gw-modal-open");
        if (back) { changeBtn.focus(); }
    }

    changeBtn.addEventListener("click", function () {
        if (!loginPart.value.trim()) { unlockRole(); return; }
        confirmId.textContent = composedLoginId() || loginPart.value.trim();
        confirmModal.hidden = false;
        document.body.classList.add("gw-modal-open");
        confirmYes.focus();
    });
    confirmYes.addEventListener("click", function () { closeConfirm(false); unlockRole(); });
    confirmModal.addEventListener("click", function (e) {
        var target = e.target;
        if (target === confirmModal || (target instanceof Element && target.closest("[data-modal-close]"))) { closeConfirm(true); }
    });
    document.addEventListener("keydown", function (e) {
        if (e.key === "Escape" && !confirmModal.hidden) { closeConfirm(true); }
    });

    // ---------- sign-in ID ----------
    function applyIdRule() {
        var rule = ID_RULES[role];
        if (!rule) { return; }
        var labelText = loginLabel.firstChild;
        if (labelText) { labelText.nodeValue = rule.label + " "; }
        loginPrefix.textContent = rule.prefix;
        loginPrefix.hidden = !rule.prefix;
        loginPart.placeholder = rule.placeholder;
        loginPart.setAttribute("inputmode", rule.inputMode);
        loginPart.maxLength = rule.maxLength;
        loginHint.textContent = rule.hint;
        normalizeLogin();
        validateLogin();
    }

    function normalizeLogin() {
        var v = loginPart.value;
        if (role === "Student") {
            var digits = v.replace(/\D/g, "").slice(0, 6);
            v = digits.length > 2 ? digits.slice(0, 2) + "-" + digits.slice(2) : digits;
        } else if (role) {
            v = v.toLowerCase().replace(/\s+/g, "");
            var prefix = ID_RULES[role].prefix;
            if (v.indexOf(prefix) === 0) { v = v.slice(prefix.length); }   // typed or pasted the prefix
        }
        if (v !== loginPart.value) { loginPart.value = v; }
    }

    function composedLoginId() {
        var part = loginPart.value.trim();
        if (!role || !part) { return ""; }
        return ID_RULES[role].prefix + part;
    }

    function loginProblem() {
        var part = loginPart.value.trim();
        if (role === "Student") {
            if (!part) { return "Enter the student number."; }
            if (!STUDENT_NO.test(part)) { return "Use the format YY-0000, for example 26-0001."; }
            if (/-0000$/.test(part)) { return "The last four digits cannot all be zero."; }
            return "";
        }
        if (!part) { return "Enter the username after " + ID_RULES[role].prefix + "."; }
        if (!USERNAME.test(part)) {
            return "Use lowercase letters, numbers, dots or underscores, starting and ending with a letter or number.";
        }
        return "";
    }

    function validateLogin() {
        var problem = role ? loginProblem() : "";
        loginIdOut.value = composedLoginId();
        showError(loginPart, document.getElementById("login-error"), (touched.login || tried) ? problem : "");
        return !problem;
    }

    loginPart.addEventListener("input", function () { normalizeLogin(); validateLogin(); });
    loginPart.addEventListener("blur", function () { touched.login = true; validateLogin(); });

    // ---------- name ----------
    function composedName() {
        return [clean(first.value), clean(middle.value), clean(last.value), suffix.value]
            .filter(function (p) { return p; }).join(" ");
    }

    /**
     * @param {HTMLInputElement} input
     * @param {boolean} required
     * @param {string} label  used in the message, e.g. "first name"
     */
    function namePartProblem(input, required, label) {
        var v = clean(input.value);
        if (!v) { return required ? "Enter the " + label + "." : ""; }
        if (!NAME.test(v)) { return "Use letters, spaces, hyphens, apostrophes or periods only."; }
        return "";
    }

    function validateName() {
        var ok = true;
        [{ input: first, required: true, label: "first name" },
         { input: middle, required: false, label: "middle name" },
         { input: last, required: true, label: "last name" }].forEach(function (f) {
            var problem = namePartProblem(f.input, f.required, f.label);
            if (problem) { ok = false; }
            showError(f.input, document.getElementById(f.input.id + "-error"), (touched[f.input.id] || tried) ? problem : "");
        });
        var full = composedName();
        var tooLong = full.length > 100;
        if (tooLong) { ok = false; }
        showError(null, nameError, tooLong ? "The full name must be 100 characters or fewer (now " + full.length + ")." : "");
        namePreview.textContent = clean(first.value) || clean(last.value) ? full : "Enter a first and last name";
        fullNameOut.value = full;
        return ok;
    }

    [first, middle, last].forEach(function (input) {
        input.addEventListener("input", validateName);
        input.addEventListener("blur", function () { input.value = clean(input.value); touched[input.id] = true; validateName(); });
    });
    suffix.addEventListener("change", validateName);

    // ---------- email ----------
    function emailProblem() {
        var v = email.value.trim();
        if (!v) { return "Enter an email address."; }
        if (/\s/.test(v)) { return "An email address cannot contain spaces."; }
        if (v.indexOf("@") === -1) { return "An email address needs an @ sign, for example name@school.edu.ph."; }
        var domain = v.slice(v.lastIndexOf("@") + 1);
        if (domain.indexOf(".") === -1) { return "Add the domain ending, for example .com or .edu.ph."; }
        if (!/\.[A-Za-z]{2,24}$/.test(domain)) { return "The domain must end with an extension like .com or .ph."; }
        if (!EMAIL.test(v) || v.length > 254) { return "Enter a valid email address, for example name@school.edu.ph."; }
        return "";
    }

    function validateEmail() {
        var problem = emailProblem();
        showError(email, document.getElementById("email-error"), (touched.email || tried) ? problem : "");
        return !problem;
    }

    email.addEventListener("input", validateEmail);
    email.addEventListener("blur", function () {
        email.value = email.value.trim().toLowerCase();
        touched.email = true;
        validateEmail();
    });

    // ---------- submit ----------
    form.addEventListener("submit", function (e) {
        tried = true;
        if (!role) {
            e.preventDefault();
            showError(null, roleError, "Choose a role first.");
            focusFirstRole();
            return;
        }
        email.value = email.value.trim().toLowerCase();
        var okName = validateName();
        var okLogin = validateLogin();
        var okEmail = validateEmail();
        if (!(okName && okLogin && okEmail)) {
            e.preventDefault();
            var bad = form.querySelector('[aria-invalid="true"]');
            if (bad instanceof HTMLElement) { bad.focus(); }
            return;
        }
        submit.disabled = true;   // send once
        submit.textContent = "Creating account...";
    });

    // ---------- server errors after a failed save ----------
    // One toast lists every reason; each reason also appears under the field it is about,
    // so it is still there after the toast has gone.
    function showServerErrors() {
        var list = document.getElementById("server-errors");
        if (!list) { return; }
        /** @type {string[]} */
        var reasons = [];
        var items = list.querySelectorAll("li");
        for (var li = 0; li < items.length; li++) {
            var reason = clean(items[li].textContent);
            if (reason) { reasons.push(reason); }
        }
        if (!reasons.length) { return; }
        var gw = window.GW;
        if (gw && gw.toast) {
            gw.toast("error", reasons.length === 1 ? "The account was not created." : "The account was not created. Please fix:",
                { lines: reasons });
        }
        /** @type {Array<[RegExp, HTMLInputElement, string]>} reason pattern, field, message element id */
        var fieldFor = [
            [/email/i, email, "email-error"],
            [/student number|username|sign-in|login|\bid\b/i, loginPart, "login-error"],
            [/first name/i, first, "firstName-error"],
            [/last name/i, last, "lastName-error"],
            [/name/i, first, "firstName-error"]
        ];
        /** @type {Record<string, boolean>} message elements that already show a reason */
        var placed = {};
        reasons.forEach(function (reason) {
            for (var i = 0; i < fieldFor.length; i++) {
                var rule = fieldFor[i];
                if (rule[0].test(reason)) {
                    var errEl = document.getElementById(rule[2]);
                    if (!errEl) { continue; }
                    var text = (placed[rule[2]] ? errEl.textContent + " " : "") + reason;
                    showError(rule[1], errEl, text);
                    placed[rule[2]] = true;
                    return;
                }
            }
        });
        var firstBad = form.querySelector('[aria-invalid="true"]');
        if (firstBad instanceof HTMLElement) { firstBad.focus(); }
    }

    // ---------- start ----------
    // After a failed save the role comes back locked, with the sign-in ID split back into its parts.
    var locked = form.getAttribute("data-locked-role");
    if (locked && ID_RULES[locked]) {
        var savedId = form.getAttribute("data-login-id") || "";
        var prefix = ID_RULES[locked].prefix;
        loginPart.value = prefix && savedId.toLowerCase().indexOf(prefix) === 0 ? savedId.slice(prefix.length) : savedId;
        lockRole(locked, false);
        tried = true;   // show what still needs fixing
        validateName();
        validateLogin();
        validateEmail();
        showServerErrors();
    } else {
        focusFirstRole();
    }
})();
