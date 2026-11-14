/// <reference path="gw-globals.d.ts" />
// Admin > Accounts page (Views/Admin/Index.cshtml): Edit account modal, temporary password copy,
// tabs / filters / paging, and the student import.
// Moved out of the view so Visual Studio checks it as plain JavaScript: JSDoc type notes
// cannot be written reliably inside a Razor view (the @ has to be escaped there).

(function () {
    // ---------- element lookups ----------
    // find...: the element, or null when it is not on the page (or is a different kind of element).
    // require...: the element; stops with a clear message if the page markup was changed.

    /**
     * @template {Element} T
     * @param {string} id
     * @param {{ new (): T, prototype: T }} type  e.g. HTMLInputElement
     * @returns {T | null}
     */
    function findById(id, type) {
        var el = document.getElementById(id);
        return el instanceof type ? el : null;
    }

    /**
     * @template {Element} T
     * @param {ParentNode} root
     * @param {string} selector
     * @param {{ new (): T, prototype: T }} type
     * @returns {T | null}
     */
    function findIn(root, selector, type) {
        var el = root.querySelector(selector);
        return el instanceof type ? el : null;
    }

    /**
     * @template {Element} T
     * @param {string} id
     * @param {{ new (): T, prototype: T }} type
     * @returns {T}
     */
    function requireById(id, type) {
        var el = findById(id, type);
        if (!el) { throw new Error("Accounts page: #" + id + " is missing or is the wrong kind of element."); }
        return el;
    }

    /**
     * @template {Element} T
     * @param {ParentNode} root
     * @param {string} selector
     * @param {{ new (): T, prototype: T }} type
     * @returns {T}
     */
    function requireIn(root, selector, type) {
        var el = findIn(root, selector, type);
        if (!el) { throw new Error("Accounts page: " + selector + " is missing or is the wrong kind of element."); }
        return el;
    }

    /**
     * Every element matching the selector that is of the given kind.
     * @template {Element} T
     * @param {ParentNode} root
     * @param {string} selector
     * @param {{ new (): T, prototype: T }} type
     * @returns {T[]}
     */
    function allIn(root, selector, type) {
        var found = root.querySelectorAll(selector);
        /** @type {T[]} */
        var list = [];
        for (var i = 0; i < found.length; i++) {
            var el = found[i];
            if (el instanceof type) { list.push(el); }
        }
        return list;
    }


    // Edit account modal: fill it from the clicked row, then open it with the shared GW.modal.
    // Confirmations use the shared data-confirm dialog (gw-modal.js), which sits above this modal.
    (function () {
        var modalEl = findById("gw-account-modal", HTMLElement);
        var gw = window.GW;
        if (!modalEl || !gw || !gw.modal) return;
        var modal = modalEl;
        var GW = gw;

        /**
         * @param {string} field
         * @param {string} value
         */
        function setText(field, value) {
            allIn(modal, '[data-acct-field="' + field + '"]', HTMLElement).forEach(function (el) {
                el.textContent = value;
            });
        }

        var accessText = requireIn(modal, "[data-acct-access-text]", HTMLElement);
        var selfNote = requireIn(modal, "[data-acct-self-note]", HTMLElement);
        var accessForm = requireIn(modal, '[data-acct-form="access"]', HTMLFormElement);
        var activeInput = requireIn(modal, "[data-acct-active-input]", HTMLInputElement);
        var disableBtn = requireIn(modal, '[data-acct-btn="disable"]', HTMLButtonElement);
        var enableBtn = requireIn(modal, '[data-acct-btn="enable"]', HTMLButtonElement);
        var resetForm = requireIn(modal, '[data-acct-form="reset"]', HTMLFormElement);
        var resetBtn = requireIn(modal, '[data-acct-btn="reset"]', HTMLButtonElement);
        var deleteForm = requireIn(modal, '[data-acct-form="delete"]', HTMLFormElement);
        var deleteBtn = requireIn(modal, '[data-acct-btn="delete"]', HTMLButtonElement);
        var deleteInput = requireIn(modal, "[data-acct-delete-input]", HTMLInputElement);
        var deleteSection = requireIn(modal, "[data-acct-delete-section]", HTMLElement);
        var deleteBlockedBox = requireIn(modal, "[data-acct-delete-blocked]", HTMLElement);
        var deleteBlockedReason = requireIn(modal, "[data-acct-delete-reason]", HTMLElement);
        var deleteAllowedBox = requireIn(modal, "[data-acct-delete-allowed]", HTMLElement);
        var statusChip = requireIn(modal, '[data-acct-field="status"]', HTMLElement);
        var roleBadge = requireIn(modal, "[data-acct-role-badge]", HTMLElement);
        /** @type {Record<string, string>} */
        var ROLE_CLASS = { "Administrator": "admin", "Faculty": "faculty", "Student": "student" };

        /**
         * @typedef {{ name: string, login: string, role: string, active: boolean, me: boolean,
         *             setActiveUrl: string, resetUrl: string, deleteUrl: string, deleteBlocked: string }} Account
         */
        /** @type {Account | null} the account the modal is showing */
        var current = null;

        function deleteMatches() {
            if (!current || current.deleteBlocked) return false;
            return deleteInput.value.trim().toLowerCase() === current.login.toLowerCase();
        }

        function syncDeleteButton() {
            deleteBtn.disabled = !deleteMatches();
        }

        /** @param {Element} btn  the row's Edit button, which carries the account's details */
        function fill(btn) {
            /** @type {Account} */
            var c = {
                name: btn.getAttribute("data-name") || "",
                login: btn.getAttribute("data-login") || "",
                role: btn.getAttribute("data-role") || "",
                active: btn.getAttribute("data-active") === "true",
                me: btn.getAttribute("data-me") === "true",
                setActiveUrl: btn.getAttribute("data-set-active-url") || "",
                resetUrl: btn.getAttribute("data-reset-url") || "",
                deleteUrl: btn.getAttribute("data-delete-url") || "",
                deleteBlocked: btn.getAttribute("data-delete-blocked") || ""
            };
            current = c;

            setText("name", c.name + (c.me ? " (you)" : ""));
            setText("login", c.login);
            roleBadge.textContent = c.role;
            roleBadge.className = "gw-badge gw-acct-role gw-acct-role-" + (ROLE_CLASS[c.role] || "student");
            statusChip.textContent = c.active ? "Active" : "Disabled";
            statusChip.className = "gw-status " + (c.active ? "gw-status-active" : "gw-status-off");

            // ----- access: the server also refuses to disable your own account -----
            selfNote.hidden = !c.me;
            accessForm.hidden = c.me;
            accessText.hidden = c.me;
            accessForm.action = c.setActiveUrl;
            activeInput.value = c.active ? "false" : "true";
            disableBtn.hidden = !c.active;
            enableBtn.hidden = c.active;
            accessText.textContent = c.active
                ? "This account is active. Disabling it stops " + c.name + " from signing in; their records are kept."
                : "This account is disabled. " + c.name + " cannot sign in until it is enabled again.";
            disableBtn.setAttribute("data-confirm",
                "Disable " + c.name + " (" + c.login + ")? They are signed out and cannot sign in until you enable the account again.");
            enableBtn.setAttribute("data-confirm",
                "Enable " + c.name + " (" + c.login + ")? They can sign in again with their current password.");

            // ----- reset password -----
            resetForm.action = c.resetUrl;
            resetBtn.setAttribute("data-confirm",
                "Reset the password for " + c.name + " (" + c.login + ")? Their old password stops working right away.");

            // ----- delete: never for your own account; explain up front when the server would refuse -----
            deleteSection.hidden = c.me;
            deleteBlockedBox.hidden = !c.deleteBlocked;
            deleteBlockedReason.textContent = c.deleteBlocked;
            deleteAllowedBox.hidden = !!c.deleteBlocked;
            deleteForm.action = c.deleteBlocked ? "" : c.deleteUrl;
            deleteInput.value = "";
            deleteBtn.setAttribute("data-confirm",
                "Permanently delete " + c.name + " (" + c.login + ")? This cannot be undone.");
            syncDeleteButton();
        }

        deleteInput.addEventListener("input", syncDeleteButton);
        deleteInput.addEventListener("keydown", function (e) {
            // Enter must not bypass the confirmation when the ID does not match yet.
            if (e.key === "Enter" && !deleteMatches()) e.preventDefault();
        });
        deleteForm.addEventListener("submit", function (e) {
            if (!deleteMatches()) e.preventDefault();
        }, true);

        document.addEventListener("click", function (e) {
            var target = e.target;
            if (!(target instanceof Element)) return;
            var btn = target.closest("[data-account-edit]");
            if (!btn) return;
            e.preventDefault();
            fill(btn);
            GW.modal.open(modal, { focus: findIn(modal, ".gw-modal-close", HTMLElement) });
        });
    })();


    // Copy the one-time temporary password (the box is only shown once).
    // Shows "Copied" on the button and a message under it; if copying is blocked,
    // the password is selected so the admin can press Ctrl+C.
    (function () {
        var copyBtn = findById("copy-password", HTMLButtonElement);
        var box = findById("temp-password", HTMLElement);
        if (copyBtn && box) { setUpCopy(copyBtn, box); }

        /**
         * @param {HTMLButtonElement} copyBtn
         * @param {HTMLElement} box  holds the password text
         */
        function setUpCopy(copyBtn, box) {
            var label = requireIn(copyBtn, "[data-copy-label]", HTMLElement);
            var iconCopy = requireIn(copyBtn, '[data-copy-icon="copy"]', HTMLElement);
            var iconDone = requireIn(copyBtn, '[data-copy-icon="done"]', HTMLElement);
            var status = requireById("copy-status", HTMLElement);
            /** @type {number | undefined} */
            var resetTimer;

            /**
             * @param {string} state  "done" | "failed" | "idle"
             * @param {string} message
             */
            function setState(state, message) {
                copyBtn.classList.toggle("gw-copy-done", state === "done");
                label.textContent = state === "done" ? "Copied" : "Copy";
                iconCopy.hidden = state === "done";
                iconDone.hidden = state !== "done";
                status.textContent = message || "";
                status.classList.toggle("gw-copy-status-bad", state === "failed");
            }

            function selectPassword() {
                var range = document.createRange();
                range.selectNodeContents(box);
                var sel = window.getSelection();
                if (!sel) { return; }
                sel.removeAllRanges();
                sel.addRange(range);
            }

            function copied() {
                setState("done", "Password copied to the clipboard.");
                clearTimeout(resetTimer);
                resetTimer = setTimeout(function () { setState("idle", ""); }, 4000);
            }

            function failed() {
                selectPassword();
                setState("failed", "Could not copy automatically. The password is selected: press Ctrl+C (or Cmd+C) to copy it.");
            }

            /** @param {string} text */
            function legacyCopy(text) {
                var area = document.createElement("textarea");
                area.value = text;
                area.setAttribute("readonly", "readonly");
                area.style.position = "fixed";
                area.style.top = "0";
                area.style.left = "-9999px";
                document.body.appendChild(area);
                area.select();
                var ok = false;
                try { ok = document.execCommand("copy"); } catch (err) { ok = false; }
                document.body.removeChild(area);
                copyBtn.focus();
                return ok;
            }

            copyBtn.addEventListener("click", function () {
                var text = (box.textContent || "").trim();   // only the password itself
                if (!text) { failed(); return; }
                if (navigator.clipboard && window.isSecureContext) {
                    navigator.clipboard.writeText(text).then(copied, function () {
                        if (legacyCopy(text)) { copied(); } else { failed(); }
                    });
                } else if (legacyCopy(text)) {
                    copied();
                } else {
                    failed();
                }
            });
        }
    })();

    // ---------- Accounts list: role tabs, search, status filter, sort and pages ----------
    // Works on the rows the server rendered (the real accounts). Only the address bar remembers the
    // chosen tab and page (?role=Student&page=2); nothing is stored in the browser.
    (function () {
        var controls = findById("gw-acct-controls", HTMLElement);
        var table = findById("gw-acct-table", HTMLTableElement);
        if (!controls || !table) { return; }
        var tbody = table.tBodies[0];
        var rows = allIn(tbody, "tr[data-acct-row]", HTMLTableRowElement);
        if (!rows.length) { return; }

        var tabs = allIn(controls, "[data-role-tab]", HTMLElement);
        var search = requireById("gw-acct-search", HTMLInputElement);
        var statusSel = requireById("gw-acct-status", HTMLSelectElement);
        var sortSel = requireById("gw-acct-sort", HTMLSelectElement);
        var sizeSel = requireById("gw-acct-size", HTMLSelectElement);
        var range = requireById("gw-acct-range", HTMLElement);
        var pageInfo = requireById("gw-acct-pageinfo", HTMLElement);
        var emptyRow = requireById("gw-acct-empty", HTMLTableRowElement);
        var emptyText = requireById("gw-acct-empty-text", HTMLElement);
        var resultbar = requireById("gw-acct-resultbar", HTMLElement);
        var footbar = requireById("gw-acct-footbar", HTMLElement);
        var stepButtons = allIn(document, "[data-page-step]", HTMLButtonElement);
        var clearBtn = requireById("gw-acct-clear", HTMLButtonElement);
        var emptyClear = findIn(emptyRow, "[data-acct-clear]", HTMLElement);

        /** @type {Record<string, string>} */
        var ROLE_LABEL = { "": "accounts", "Administrator": "administrators", "Faculty": "faculty accounts", "Student": "students" };
        var state = { role: "", page: 1 };

        /**
         * A row's data-* value in lower case, for searching and sorting.
         * @param {Element} r
         * @param {string} key
         */
        function text(r, key) { return (r.getAttribute("data-" + key) || "").toLowerCase(); }

        /** @typedef {(a: Element, b: Element) => number} RowOrder */
        /** @type {Record<string, RowOrder>} the Sort by choices */
        var SORTS = {
            "role": function (a, b) {
                return (Number(a.getAttribute("data-role-order")) - Number(b.getAttribute("data-role-order"))) || text(a, "name").localeCompare(text(b, "name"));
            },
            "name-asc": function (a, b) { return text(a, "name").localeCompare(text(b, "name")); },
            "name-desc": function (a, b) { return text(b, "name").localeCompare(text(a, "name")); },
            "id-asc": function (a, b) { return text(a, "login").localeCompare(text(b, "login"), undefined, { numeric: true }); },
            "newest": function (a, b) { return text(b, "created").localeCompare(text(a, "created")) || text(a, "name").localeCompare(text(b, "name")); },
            "oldest": function (a, b) { return text(a, "created").localeCompare(text(b, "created")) || text(a, "name").localeCompare(text(b, "name")); }
        };

        /** @param {Element} r */
        function matches(r) {
            var q = search.value.trim().toLowerCase();
            if (state.role && r.getAttribute("data-role") !== state.role) { return false; }
            if (statusSel.value && r.getAttribute("data-status") !== statusSel.value) { return false; }
            if (q && (text(r, "name") + " " + text(r, "login") + " " + text(r, "email")).indexOf(q) === -1) { return false; }
            return true;
        }

        function render() {
            var size = parseInt(sizeSel.value, 10) || 10;
            var list = rows.filter(matches).sort(SORTS[sortSel.value] || SORTS.role);
            var pages = Math.max(1, Math.ceil(list.length / size));
            if (state.page > pages) { state.page = pages; }
            if (state.page < 1) { state.page = 1; }
            var start = (state.page - 1) * size;
            var shown = list.slice(start, start + size);

            // put rows in sorted order, show only this page
            rows.forEach(function (r) { r.hidden = true; });
            list.forEach(function (r) { tbody.insertBefore(r, emptyRow); });
            shown.forEach(function (r) { r.hidden = false; });

            var noun = ROLE_LABEL[state.role] || "accounts";
            emptyRow.hidden = list.length !== 0;
            var filtered = !!(search.value.trim() || statusSel.value);
            emptyText.textContent = filtered ? "No " + noun + " match these filters." : "No " + noun + " yet.";
            if (emptyClear) { emptyClear.hidden = !filtered; }

            range.textContent = list.length
                ? (start + 1) + "\u2013" + (start + shown.length) + " of " + list.length + " " + noun
                : "0 " + noun;
            pageInfo.textContent = "Page " + state.page + " of " + pages;
            stepButtons.forEach(function (b) {
                var step = parseInt(b.getAttribute("data-page-step") || "", 10);
                b.disabled = step < 0 ? state.page <= 1 : state.page >= pages;
            });
            footbar.hidden = pages <= 1;
            writeUrl();
        }

        /**
         * @param {HTMLElement} tab
         * @param {boolean} focus  move keyboard focus to the tab
         */
        function selectTab(tab, focus) {
            tabs.forEach(function (t) {
                var on = t === tab;
                t.setAttribute("aria-selected", on ? "true" : "false");
                t.tabIndex = on ? 0 : -1;
            });
            state.role = tab.getAttribute("data-role-tab") || "";
            state.page = 1;
            if (focus) { tab.focus(); }
            render();
        }

        tabs.forEach(function (tab, i) {
            tab.addEventListener("click", function () { selectTab(tab, false); });
            tab.addEventListener("keydown", function (e) {
                if (e.key !== "ArrowRight" && e.key !== "ArrowLeft") { return; }
                e.preventDefault();
                var next = tabs[(i + (e.key === "ArrowRight" ? 1 : tabs.length - 1)) % tabs.length];
                selectTab(next, true);
            });
        });

        function resetPageAndRender() { state.page = 1; render(); }
        search.addEventListener("input", resetPageAndRender);
        [statusSel, sortSel, sizeSel].forEach(function (s) { s.addEventListener("change", resetPageAndRender); });

        function clearFilters() {
            search.value = "";
            statusSel.value = "";
            resetPageAndRender();
            search.focus();
        }
        clearBtn.addEventListener("click", clearFilters);
        if (emptyClear) { emptyClear.addEventListener("click", clearFilters); }

        stepButtons.forEach(function (b) {
            b.addEventListener("click", function () {
                state.page += parseInt(b.getAttribute("data-page-step") || "", 10);
                render();
                if (b.closest("#gw-acct-footbar")) { resultbar.scrollIntoView({ block: "nearest" }); }
            });
        });

        // ----- the address bar keeps the tab and page, so Back / refresh return to the same view -----
        function writeUrl() {
            if (!window.history || !history.replaceState) { return; }
            var params = new URLSearchParams(window.location.search);
            if (state.role) { params.set("role", state.role); } else { params.delete("role"); }
            if (state.page > 1) { params.set("page", String(state.page)); } else { params.delete("page"); }
            if (sortSel.value !== "role") { params.set("sort", sortSel.value); } else { params.delete("sort"); }
            var qs = params.toString();
            history.replaceState(null, "", window.location.pathname + (qs ? "?" + qs : "") + window.location.hash);
        }

        /**
         * The tab for a role ("" is All), or the first tab.
         * @param {string | null} role
         * @returns {HTMLElement}
         */
        function tabFor(role) {
            return tabs.filter(function (t) { return t.getAttribute("data-role-tab") === role; })[0] || tabs[0];
        }

        // ----- a just-created account: open its role tab and page, and highlight it -----
        // The success message names the new account's ID in brackets, e.g. "... (26-0001)."
        function showNewAccount() {
            // The success toast (from gw-modal.js) carries the message text.
            var gw = window.GW;
            var shown = gw && gw.flashes ? gw.flashes() : [];
            var okText = shown.filter(function (f) { return f.kind === "success"; }).map(function (f) { return f.text; }).join(" ");
            if (!okText) { return false; }
            var m = /\(([^()]+)\)/.exec(okText);
            if (!m) { return false; }
            var id = m[1].trim().toLowerCase();
            var row = rows.filter(function (r) { return text(r, "login") === id; })[0];
            if (!row) { return false; }
            selectTab(tabFor(row.getAttribute("data-role")), false);
            var size = parseInt(sizeSel.value, 10) || 10;
            var list = rows.filter(matches).sort(SORTS[sortSel.value] || SORTS.role);
            state.page = Math.floor(list.indexOf(row) / size) + 1;
            render();
            row.classList.add("gw-acct-row-new");
            return true;
        }

        var params = new URLSearchParams(window.location.search);
        var sortParam = params.get("sort");
        if (sortParam && SORTS[sortParam]) { sortSel.value = sortParam; }
        controls.hidden = false;
        resultbar.hidden = false;
        if (!showNewAccount()) {
            state.role = params.get("role") || "";
            selectTab(tabFor(state.role), false);
            state.page = parseInt(params.get("page") || "", 10) || 1;
            render();
        }
    })();


    // ---------- Import students (CSV or .xlsx) ----------
    // 1) read the file in the browser, 2) check every row with the Add user rules, 3) create the
    // valid rows one by one through the existing CreateUser action. Each request is a normal form
    // post with the anti-forgery token, so the server validates every account exactly as it does
    // for Add user (and emails the temporary password).
    (function () {
        var modalEl = findById("gw-import-modal", HTMLElement);
        var openBtnEl = findIn(document, "[data-import-open]", HTMLButtonElement);
        var gw = window.GW;
        if (!modalEl || !openBtnEl || !gw || !gw.modal) { return; }
        var modal = modalEl;
        var openBtn = openBtnEl;
        var GW = gw;
        openBtn.hidden = false;

        var form = requireById("gw-import-form", HTMLFormElement);
        var fileInput = requireById("gw-import-file", HTMLInputElement);
        var sectionInput = requireById("gw-import-section", HTMLInputElement);
        var errorBox = requireIn(modal, "[data-import-error]", HTMLElement);
        var results = requireIn(modal, "[data-import-results]", HTMLElement);
        var rowsBody = requireIn(modal, "[data-import-rows]", HTMLElement);
        var onlyProblems = requireIn(modal, "[data-import-only-problems]", HTMLInputElement);
        var progress = requireIn(modal, "[data-import-progress]", HTMLElement);
        var bar = requireIn(modal, "[data-import-bar]", HTMLElement);
        var progressText = requireIn(modal, "[data-import-progress-text]", HTMLElement);
        var runBtn = requireIn(modal, "[data-import-run]", HTMLButtonElement);
        var stopBtn = requireIn(modal, "[data-import-stop]", HTMLButtonElement);
        var refreshBtn = requireIn(modal, "[data-import-refresh]", HTMLButtonElement);
        var downloadBtn = requireIn(modal, "[data-import-download]", HTMLButtonElement);
        var templateBtn = requireIn(modal, "[data-import-template]", HTMLButtonElement);
        var doneChip = requireIn(modal, "[data-import-done-chip]", HTMLElement);
        var failedChip = requireIn(modal, "[data-import-failed-chip]", HTMLElement);
        var closeBtns = allIn(modal, "[data-import-close]", HTMLButtonElement);
        var createUrl = form.getAttribute("action") || form.action;
        var token = requireIn(form, 'input[name="__RequestVerificationToken"]', HTMLInputElement).value;

        var MAX_ROWS = 500;
        var STUDENT_NO = /^\d{2}-\d{4}$/;
        var NAME = /^[A-Za-z\u00C0-\u00D6\u00D8-\u00F6\u00F8-\u024F](?:[A-Za-z\u00C0-\u00D6\u00D8-\u00F6\u00F8-\u024F .'-]*[A-Za-z\u00C0-\u00D6\u00D8-\u00F6\u00F8-\u024F.])?$/;
        var EMAIL = /^[A-Za-z0-9!#$%&'*+\/=?^_`{|}~-]+(?:\.[A-Za-z0-9!#$%&'*+\/=?^_`{|}~-]+)*@(?:[A-Za-z0-9](?:[A-Za-z0-9-]{0,61}[A-Za-z0-9])?\.)+[A-Za-z]{2,24}$/;
        /** @type {Record<string, string>} how a suffix may be typed -> how it is saved */
        var SUFFIXES = { "jr": "Jr.", "jr.": "Jr.", "sr": "Sr.", "sr.": "Sr.", "ii": "II", "iii": "III", "iv": "IV", "v": "V" };
        /** @type {Record<string, string[]>} field -> accepted column names (lower case, letters only) */
        var COLUMNS = {
            loginId: ["studentnumber", "studentno", "studentid", "idnumber", "studentnum"],
            first: ["firstname", "givenname", "first"],
            middle: ["middlename", "middle", "middleinitial"],
            last: ["lastname", "surname", "familyname", "last"],
            suffix: ["suffix", "namesuffix"],
            email: ["email", "emailaddress", "mail"]
        };
        var TEMPLATE = "student_number,first_name,middle_name,last_name,suffix,email\r\n";

        // existing accounts on this page, to catch duplicates before sending anything
        /** @type {Record<string, boolean>} */
        var takenIds = {};
        /** @type {Record<string, boolean>} */
        var takenEmails = {};
        allIn(document, "tr[data-acct-row]", HTMLTableRowElement).forEach(function (r) {
            takenIds[(r.getAttribute("data-login") || "").toLowerCase()] = true;
            takenEmails[(r.getAttribute("data-email") || "").toLowerCase()] = true;
        });

        /**
         * One row of the file and what happened to it.
         * @typedef {{ row: number, loginId: string, first: string, middle: string, last: string, suffix: string,
         *             email: string, fullName: string, problems: string[], state: string, message: string }} ImportRecord
         */
        /** @typedef {{ ok: boolean, stop?: boolean, message?: string }} CreateResult */

        /** @type {ImportRecord[]} */
        var records = [];
        var running = false;
        var stopRequested = false;

        // ---------- reading files ----------
        /**
         * @param {string} textValue
         * @returns {string[][]} rows of cells
         */
        function parseCsv(textValue) {
            /** @type {string[][]} */
            var out = [];
            /** @type {string[]} */
            var row = [];
            var field = "", i = 0, q = false, c;
            textValue = textValue.replace(/^\uFEFF/, "");
            while (i < textValue.length) {
                c = textValue.charAt(i);
                if (q) {
                    if (c === '"') {
                        if (textValue.charAt(i + 1) === '"') { field += '"'; i++; } else { q = false; }
                    } else { field += c; }
                } else if (c === '"') { q = true; }
                else if (c === "," || c === ";" || c === "\t") { row.push(field); field = ""; }
                else if (c === "\n" || c === "\r") {
                    if (c === "\r" && textValue.charAt(i + 1) === "\n") { i++; }
                    row.push(field); out.push(row); row = []; field = "";
                } else { field += c; }
                i++;
            }
            if (field !== "" || row.length) { row.push(field); out.push(row); }
            return out;
        }

        /** @typedef {{ has: (name: string) => boolean, text: (name: string) => Promise<string | null> }} ZipReader */

        /**
         * Minimal .xlsx reader: an .xlsx file is a zip of XML files.
         * @param {ArrayBuffer} buffer
         * @returns {ZipReader}
         */
        function readZip(buffer) {
            var view = new DataView(buffer);
            var eocd = -1;
            for (var i = buffer.byteLength - 22; i >= Math.max(0, buffer.byteLength - 65557); i--) {
                if (view.getUint32(i, true) === 0x06054b50) { eocd = i; break; }
            }
            if (eocd < 0) { throw new Error("not-zip"); }
            var count = view.getUint16(eocd + 10, true);
            var offset = view.getUint32(eocd + 16, true);
            /** @type {Record<string, { method: number, size: number, local: number }>} */
            var entries = {};
            for (var n = 0; n < count; n++) {
                if (view.getUint32(offset, true) !== 0x02014b50) { throw new Error("not-zip"); }
                var method = view.getUint16(offset + 10, true);
                var size = view.getUint32(offset + 20, true);
                var nameLen = view.getUint16(offset + 28, true);
                var extraLen = view.getUint16(offset + 30, true);
                var commentLen = view.getUint16(offset + 32, true);
                var local = view.getUint32(offset + 42, true);
                var name = new TextDecoder().decode(new Uint8Array(buffer, offset + 46, nameLen));
                entries[name] = { method: method, size: size, local: local };
                offset += 46 + nameLen + extraLen + commentLen;
            }
            return {
                has: function (name) { return !!entries[name]; },
                text: function (name) {
                    var e = entries[name];
                    if (!e) { return Promise.resolve(null); }
                    var start = e.local + 30 + view.getUint16(e.local + 26, true) + view.getUint16(e.local + 28, true);
                    var data = new Uint8Array(buffer, start, e.size);
                    if (e.method === 0) { return Promise.resolve(new TextDecoder().decode(data)); }
                    if (e.method !== 8 || typeof DecompressionStream === "undefined") { return Promise.reject(new Error("no-inflate")); }
                    var stream = new Blob([data]).stream().pipeThrough(new DecompressionStream("deflate-raw"));
                    return new Response(stream).text();
                }
            };
        }

        /** @param {string} textValue */
        function xml(textValue) { return new DOMParser().parseFromString(textValue, "application/xml"); }

        /**
         * Column number (0-based) from a cell reference such as "C12".
         * @param {string | null} ref
         */
        function colIndex(ref) {
            var letters = (ref || "").replace(/[0-9]/g, ""), n = 0;
            for (var i = 0; i < letters.length; i++) { n = n * 26 + (letters.charCodeAt(i) - 64); }
            return n - 1;
        }

        /**
         * The text of every <t> inside an element, joined.
         * @param {Element} el
         */
        function textRuns(el) {
            var runs = el.getElementsByTagName("t");
            var out = "";
            for (var i = 0; i < runs.length; i++) { out += runs[i].textContent || ""; }
            return out;
        }

        /**
         * Reads the first worksheet of an .xlsx file.
         * @param {ArrayBuffer} buffer
         * @returns {Promise<string[][]>} rows of cells
         */
        function parseXlsx(buffer) {
            var zip = readZip(buffer);
            return Promise.all([zip.text("xl/workbook.xml"), zip.text("xl/_rels/workbook.xml.rels"), zip.text("xl/sharedStrings.xml")])
            .then(function (parts) {
                var workbook = parts[0], rels = parts[1], strings = parts[2];
                if (!workbook) { throw new Error("not-xlsx"); }
                var sheetPath = "xl/worksheets/sheet1.xml";
                var firstSheet = xml(workbook).getElementsByTagName("sheet")[0];
                var relId = firstSheet ? firstSheet.getAttribute("r:id") : null;
                if (relId && rels) {
                    var relList = xml(rels).getElementsByTagName("Relationship");
                    for (var r = 0; r < relList.length; r++) {
                        if (relList[r].getAttribute("Id") === relId) {
                            var target = relList[r].getAttribute("Target");
                            if (target) { sheetPath = target.charAt(0) === "/" ? target.slice(1) : "xl/" + target; }
                        }
                    }
                }
                /** @type {string[]} */
                var shared = [];
                if (strings) {
                    var items = xml(strings).getElementsByTagName("si");
                    for (var s = 0; s < items.length; s++) { shared.push(textRuns(items[s])); }
                }
                return zip.text(sheetPath).then(function (sheetText) {
                    if (!sheetText) { throw new Error("not-xlsx"); }
                    /** @type {string[][]} */
                    var out = [];
                    var rowEls = xml(sheetText).getElementsByTagName("row");
                    for (var ri = 0; ri < rowEls.length; ri++) {
                        /** @type {string[]} */
                        var cells = [];
                        var cellEls = rowEls[ri].getElementsByTagName("c");
                        for (var ci = 0; ci < cellEls.length; ci++) {
                            var c = cellEls[ci];
                            var type = c.getAttribute("t");
                            var v = c.getElementsByTagName("v")[0];
                            var value = "";
                            if (type === "s") { value = v ? (shared[parseInt(v.textContent || "", 10)] || "") : ""; }
                            else if (type === "inlineStr") { value = textRuns(c); }
                            else { value = v ? (v.textContent || "") : ""; }
                            var idx = c.getAttribute("r") ? colIndex(c.getAttribute("r")) : cells.length;
                            cells[idx] = value;
                        }
                        for (var k = 0; k < cells.length; k++) { if (cells[k] === undefined) { cells[k] = ""; } }
                        out.push(cells);
                    }
                    return out;
                });
            });
        }

        /**
         * @param {File} file
         * @returns {Promise<string[][]>} rows of cells
         */
        function readFile(file) {
            var name = (file.name || "").toLowerCase();
            if (/\.xls$/.test(name)) {
                return Promise.reject(new Error("Old Excel files (.xls) are not supported. Save the sheet as .xlsx or .csv and try again."));
            }
            if (/\.xlsx$/.test(name)) {
                return file.arrayBuffer().then(parseXlsx).catch(/** @param {unknown} err */ function (err) {
                    if (err instanceof Error && err.message === "no-inflate") {
                        throw new Error("This browser cannot open Excel files. Save the sheet as .csv, or use a current version of Chrome, Edge or Firefox.");
                    }
                    throw new Error("This Excel file could not be read. Save it as .xlsx or .csv and try again.");
                });
            }
            if (/\.csv$/.test(name) || file.type === "text/csv") { return file.text().then(parseCsv); }
            return Promise.reject(new Error("Choose a .csv or .xlsx file."));
        }

        // ---------- checking rows ----------
        /** @param {unknown} v */
        function clean(v) { return String(v == null ? "" : v).replace(/\s+/g, " ").trim(); }
        /** @param {unknown} h  a column name */
        function key(h) { return clean(h).toLowerCase().replace(/[^a-z]/g, ""); }

        /**
         * Which column holds each field.
         * @param {string[]} header
         * @returns {Record<string, number>}
         */
        function mapHeader(header) {
            /** @type {Record<string, number>} */
            var map = {};
            header.forEach(function (h, i) {
                var k = key(h);
                Object.keys(COLUMNS).forEach(function (field) {
                    if (map[field] === undefined && COLUMNS[field].indexOf(k) !== -1) { map[field] = i; }
                });
            });
            return map;
        }

        /**
         * Checks every row with the Add user rules.
         * @param {string[][]} table
         * @returns {ImportRecord[]}
         */
        function buildRecords(table) {
            var nonEmpty = table.filter(function (r) { return r.some(function (c) { return clean(c) !== ""; }); });
            if (!nonEmpty.length) { throw new Error("The file is empty."); }
            var headerRow = nonEmpty[0];
            var map = mapHeader(headerRow);
            var missing = ["loginId", "first", "last", "email"].filter(function (f) { return map[f] === undefined; });
            if (missing.length) {
                /** @type {Record<string, string>} */
                var names = { loginId: "student_number", first: "first_name", last: "last_name", email: "email" };
                throw new Error("The first row must name the columns. Missing: " + missing.map(function (f) { return names[f]; }).join(", ") + ". Download the template to see the format.");
            }
            var dataRows = nonEmpty.slice(1);
            if (!dataRows.length) { throw new Error("The file has column names but no students."); }
            if (dataRows.length > MAX_ROWS) { throw new Error("The file has " + dataRows.length + " rows. Import up to " + MAX_ROWS + " at a time."); }

            /** @type {Record<string, number>} student number -> row it first appeared on */
            var seenIds = {};
            /** @type {Record<string, number>} email -> row it first appeared on */
            var seenEmails = {};
            return dataRows.map(function (cells) {
                /** @param {string} f */
                function get(f) { return map[f] === undefined ? "" : clean(cells[map[f]]); }
                /** @type {ImportRecord} */
                var rec = {
                    row: table.indexOf(cells) + 1,
                    loginId: get("loginId"),
                    first: get("first"), middle: get("middle"), last: get("last"),
                    suffix: get("suffix"),
                    email: get("email").toLowerCase(),
                    fullName: "",
                    problems: [], state: "", message: ""
                };
                var p = rec.problems;
                var idKey = rec.loginId.toLowerCase();

                if (!rec.loginId) { p.push("Student number is missing."); }
                else if (!STUDENT_NO.test(rec.loginId)) { p.push("Student number must be YY-0000."); }
                else if (/-0000$/.test(rec.loginId)) { p.push("Student number cannot end in 0000."); }
                else if (takenIds[idKey]) { p.push("Student number is already used by an account."); }
                else if (seenIds[idKey]) { p.push("Same student number as row " + seenIds[idKey] + "."); }

                if (!rec.first) { p.push("First name is missing."); } else if (!NAME.test(rec.first)) { p.push("First name has characters that are not allowed."); }
                if (rec.middle && !NAME.test(rec.middle)) { p.push("Middle name has characters that are not allowed."); }
                if (!rec.last) { p.push("Last name is missing."); } else if (!NAME.test(rec.last)) { p.push("Last name has characters that are not allowed."); }
                if (rec.suffix) {
                    var s = SUFFIXES[rec.suffix.toLowerCase()];
                    if (s) { rec.suffix = s; } else { p.push("Suffix must be Jr., Sr., II, III, IV or V."); }
                }
                rec.fullName = [rec.first, rec.middle, rec.last, rec.suffix].filter(function (x) { return x; }).join(" ");
                if (rec.fullName.length > 100) { p.push("Full name is longer than 100 characters."); }

                if (!rec.email) { p.push("Email is missing."); }
                else if (!EMAIL.test(rec.email) || rec.email.length > 254) { p.push("Email is not valid (it needs a domain ending like .com)."); }
                else if (takenEmails[rec.email]) { p.push("Email is already used by an account."); }
                else if (seenEmails[rec.email]) { p.push("Same email as row " + seenEmails[rec.email] + "."); }

                if (!p.length) {
                    seenIds[idKey] = rec.row;
                    seenEmails[rec.email] = rec.row;
                    rec.state = "ready";
                } else {
                    rec.state = "problem";
                }
                return rec;
            });
        }

        // ---------- showing results ----------
        /** @type {Record<string, string>} */
        var STATE_LABEL = { ready: "Ready", problem: "Needs fixing", creating: "Creating...", created: "Created", failed: "Not created", skipped: "Not sent" };

        /** @param {string} state */
        function count(state) { return records.filter(function (r) { return r.state === state; }).length; }

        /**
         * @param {string} name
         * @param {number} value
         */
        function setCount(name, value) {
            var counter = modal.querySelector('[data-import-count="' + name + '"]');
            if (counter) { counter.textContent = String(value); }
        }

        function renderRows() {
            rowsBody.innerHTML = "";
            records.forEach(function (rec) {
                var needsAttention = rec.state === "problem" || rec.state === "failed" || rec.state === "skipped";
                if (onlyProblems.checked && !needsAttention) { return; }
                var tr = document.createElement("tr");
                tr.className = "gw-import-row-" + rec.state;
                [String(rec.row), rec.loginId || "-", rec.fullName || "-", rec.email || "-"].forEach(function (v, i) {
                    var td = document.createElement("td");
                    td.textContent = v;
                    if (i === 3) { td.className = "gw-import-cell-email"; }
                    tr.appendChild(td);
                });
                var result = document.createElement("td");
                var badge = document.createElement("span");
                badge.className = "gw-status gw-import-state gw-import-state-" + rec.state;
                badge.textContent = STATE_LABEL[rec.state] || rec.state;
                result.appendChild(badge);
                var detail = rec.state === "problem" ? rec.problems.join(" ") : rec.message;
                if (detail) {
                    var d = document.createElement("span");
                    d.className = "gw-import-detail";
                    d.textContent = detail;
                    result.appendChild(d);
                }
                tr.appendChild(result);
                rowsBody.appendChild(tr);
            });
            setCount("ready", count("ready"));
            setCount("problem", count("problem"));
            setCount("created", count("created"));
            setCount("failed", count("failed") + count("skipped"));
            var attempted = count("created") + count("failed") + count("skipped");
            doneChip.hidden = !attempted;
            failedChip.hidden = !(count("failed") + count("skipped"));
            downloadBtn.hidden = !(count("problem") + count("failed") + count("skipped"));
            runBtn.disabled = running || !count("ready");
            runBtn.textContent = count("ready") ? "Import " + count("ready") + (count("ready") === 1 ? " student" : " students") : "Import students";
        }

        /** @param {string} message  "" hides the box */
        function showError(message) {
            errorBox.textContent = message || "";
            errorBox.hidden = !message;
        }

        function reset() {
            records = [];
            rowsBody.innerHTML = "";
            results.hidden = true;
            progress.hidden = true;
            refreshBtn.hidden = true;
            stopBtn.hidden = true;
            runBtn.hidden = false;
            downloadBtn.hidden = true;
            onlyProblems.checked = false;
            showError("");
            renderRows();
        }

        fileInput.addEventListener("change", function () {
            reset();
            var file = fileInput.files && fileInput.files[0];
            if (!file) { return; }
            readFile(file).then(function (table) {
                records = buildRecords(table);
                results.hidden = false;
                onlyProblems.checked = count("problem") > 0 && count("ready") > 20;
                renderRows();
                if (count("problem")) {
                    GW.toast("warning", count("problem") + (count("problem") === 1 ? " row needs" : " rows need") + " fixing and will be skipped. " + count("ready") + " ready to import.");
                }
            }).catch(/** @param {unknown} err */ function (err) {
                var message = err instanceof Error && err.message ? err.message : "The file could not be read.";
                showError(message);
                GW.toast("error", message);
            });
        });

        onlyProblems.addEventListener("change", renderRows);
        form.addEventListener("submit", function (e) { e.preventDefault(); });   // Enter must not post the form

        // ---------- creating accounts ----------
        /**
         * Reads the reasons from the Add user page the server sent back.
         * @param {string} html
         * @returns {CreateResult}
         */
        function reasonsFrom(html) {
            var doc = new DOMParser().parseFromString(html, "text/html");
            // #server-errors is the Add user page's list; older pages only have .gw-error-list.
            var items = doc.querySelectorAll("#server-errors li");
            if (!items.length) { items = doc.querySelectorAll(".gw-error-list li"); }
            /** @type {string[]} */
            var reasons = [];
            for (var i = 0; i < items.length; i++) {
                var t = clean(items[i].textContent);
                if (t && reasons.indexOf(t) === -1) { reasons.push(t); }
            }
            return { ok: false, message: reasons.length ? reasons.join(" ") : "The server did not create this account." };
        }

        /**
         * Posts one row to the existing CreateUser action.
         * @param {ImportRecord} rec
         * @returns {Promise<CreateResult>}
         */
        function createOne(rec) {
            var body = new URLSearchParams();
            body.append("__RequestVerificationToken", token);
            body.append("fullName", rec.fullName);
            body.append("loginId", rec.loginId);
            body.append("email", rec.email);
            body.append("role", "Student");
            return fetch(createUrl, {
                method: "POST",
                body: body,
                credentials: "same-origin",
                headers: { "Content-Type": "application/x-www-form-urlencoded" }
            }).then(
                /**
                 * @param {Response} res
                 * @returns {CreateResult | Promise<CreateResult>}
                 */
                function (res) {
                    var url = (res.url || "").toLowerCase();
                    if (url.indexOf("/account/login") !== -1) {
                        return { ok: false, stop: true, message: "Your sign-in expired. Sign in again, then import the remaining rows." };
                    }
                    if (!res.ok) { return { ok: false, message: "The server could not create this account (error " + res.status + ")." }; }
                    if (res.redirected && url.indexOf("/admin/createuser") === -1) { return { ok: true }; }
                    // The Add user page came back: it lists the server's reasons.
                    return res.text().then(reasonsFrom);
                },
                /** @returns {CreateResult} */
                function () {
                    return { ok: false, stop: true, message: "Could not reach the server. Check the connection, then import the remaining rows." };
                });
        }

        /** @param {boolean} on */
        function setRunning(on) {
            running = on;
            runBtn.hidden = on;
            stopBtn.hidden = !on;
            stopBtn.disabled = false;
            fileInput.disabled = on;
            sectionInput.disabled = on;
            closeBtns.forEach(function (b) { b.disabled = on; });
        }

        function runImport() {
            var queue = records.filter(function (r) { return r.state === "ready"; });
            var total = queue.length, done = 0;
            var section = clean(sectionInput.value);
            stopRequested = false;
            setRunning(true);
            progress.hidden = false;

            function step() {
                bar.style.width = Math.round(done / total * 100) + "%";
                var next = stopRequested ? undefined : queue.shift();
                if (!next) {
                    queue.forEach(function (r) { r.state = "skipped"; r.message = "Stopped before this row was sent."; });
                    finish(section);
                    return;
                }
                var rec = next;
                rec.state = "creating";
                progressText.textContent = "Creating " + (done + 1) + " of " + total + ": " + rec.fullName + " (" + rec.loginId + ")";
                renderRows();
                createOne(rec).then(function (result) {
                    done++;
                    rec.state = result.ok ? "created" : "failed";
                    rec.message = result.ok ? "Temporary password emailed." : (result.message || "");
                    if (result.stop) { stopRequested = true; }
                    renderRows();
                    step();
                });
            }
            step();
        }

        /** @param {string} section  the optional section name, for the messages */
        function finish(section) {
            setRunning(false);
            runBtn.hidden = true;
            refreshBtn.hidden = !count("created");
            var label = section ? " for " + section : "";
            progressText.textContent = count("created") + " of " + (count("created") + count("failed") + count("skipped")) +
                " student accounts created" + label + "." +
                (count("failed") + count("skipped") + count("problem") ? " Download the rows to fix, correct them and import that file." : "");
            bar.style.width = "100%";
            onlyProblems.checked = count("failed") + count("skipped") + count("problem") > 0;
            renderRows();

            var made = count("created"), notMade = count("failed") + count("skipped");
            var forSection = section ? " for " + section : "";
            if (made && !notMade) {
                GW.toast("success", made + (made === 1 ? " student account" : " student accounts") + " created" + forSection + ". Temporary passwords were emailed.");
            } else if (made) {
                GW.toast("warning", made + " created" + forSection + ", " + notMade + " not created. Check the list for the reasons.");
            } else {
                GW.toast("error", "No student accounts were created" + forSection + ". Check the list for the reasons.");
            }
        }

        runBtn.addEventListener("click", function () {
            var n = count("ready");
            if (!n) { return; }
            var section = clean(sectionInput.value);
            /** @type {GWDialogPiece[][]} */
            var paragraphs = [
                [{ text: "Create " }, { text: n + (n === 1 ? " student account" : " student accounts"), bold: true }, { text: section ? " for " + section + "?" : "?" }],
                [{ text: "Each student is emailed a temporary password. Accounts are created one at a time; keep this window open until it finishes.", muted: true }]
            ];
            if (count("problem")) {
                paragraphs.push([{ text: count("problem") + (count("problem") === 1 ? " row has" : " rows have") + " problems and will be skipped.", muted: true }]);
            }
            GW.confirm({ title: "Import students?", paragraphs: paragraphs, okText: "Create accounts" }).then(function (ok) {
                if (ok) { runImport(); }
            });
        });

        stopBtn.addEventListener("click", function () {
            stopRequested = true;
            stopBtn.disabled = true;
            progressText.textContent = "Stopping after the current row...";
        });

        refreshBtn.addEventListener("click", function () {
            window.location.href = window.location.pathname + "?role=Student&sort=newest";
        });

        // ---------- files for the admin ----------
        /** @param {unknown} v */
        function csvValue(v) {
            var s = String(v == null ? "" : v);
            return /[",\r\n]/.test(s) ? '"' + s.replace(/"/g, '""') + '"' : s;
        }

        /**
         * @param {string} name  file name
         * @param {string} content  CSV text
         */
        function download(name, content) {
            var blob = new Blob(["\uFEFF" + content], { type: "text/csv;charset=utf-8" });
            var a = document.createElement("a");
            a.href = URL.createObjectURL(blob);
            a.download = name;
            document.body.appendChild(a);
            a.click();
            setTimeout(function () { URL.revokeObjectURL(a.href); a.remove(); }, 0);
        }

        templateBtn.addEventListener("click", function () {
            download("student-import-template.csv", TEMPLATE);
        });

        downloadBtn.addEventListener("click", function () {
            var lines = [TEMPLATE.trim() + ",problem"];
            records.filter(function (r) { return r.state === "problem" || r.state === "failed" || r.state === "skipped"; })
                .forEach(function (r) {
                    lines.push([r.loginId, r.first, r.middle, r.last, r.suffix, r.email,
                        r.state === "problem" ? r.problems.join(" ") : r.message].map(csvValue).join(","));
                });
            var section = clean(sectionInput.value).replace(/[^A-Za-z0-9_-]+/g, "-");
            download("students-to-fix" + (section ? "-" + section : "") + ".csv", lines.join("\r\n") + "\r\n");
        });

        // ---------- open / close ----------
        openBtn.addEventListener("click", function () {
            if (!running && count("created")) { form.reset(); fileInput.value = ""; reset(); }
            GW.modal.open(modal, { focus: sectionInput });
        });
        closeBtns.forEach(function (b) {
            b.addEventListener("click", function () { if (!running) { GW.modal.close(modal); } });
        });
        // Esc / backdrop clicks are handled by the shared modal script; block them while importing.
        modal.addEventListener("keydown", function (e) { if (running && e.key === "Escape") { e.stopPropagation(); e.preventDefault(); } }, true);
        modal.addEventListener("click", function (e) { if (running && e.target === modal) { e.stopPropagation(); } }, true);
        window.addEventListener("beforeunload", function (e) { if (running) { e.preventDefault(); e.returnValue = ""; } });
    })();
})();
