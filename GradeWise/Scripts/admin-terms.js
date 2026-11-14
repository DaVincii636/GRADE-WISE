/// <reference path="gw-globals.d.ts" />
// Admin > Academic terms page (Views/Admin/Terms.cshtml).
// Moved out of the view so Visual Studio checks it as plain JavaScript: JSDoc type notes
// cannot be written reliably inside a Razor view (the @ has to be escaped there).
// Types for the term store, the server helper and window.GW: Scripts/gw-globals.d.ts.

// =====================================================================
// 1) TEMPORARY TERM STORE (in memory only)
// Holds the terms for this page view. Nothing is written to the database, localStorage
// or sessionStorage; a reload starts again from the real terms the server sent.
// To connect the database later, replace the bodies of update / archive / restore
// (and load start / end dates) with calls to new Admin actions. Every method already
// returns a Promise, so the page code below does not need to change.
// =====================================================================

window.GradeWiseTermStore = (function () {
    var PATTERN = /^\s*(1st Semester|2nd Semester|Summer)(?: Term)?\s*,\s*[SA]\.?\s*Y\.?\s*(\d{4})\s*-\s*(\d{4})\s*$/i;
    /** @type {GWTerm[]} */
    var terms = [];
    var changed = false;

    /** @param {string | null | undefined} name */
    function parseName(name) {
        var m = PATTERN.exec(name || "");
        if (!m) { return { semester: "", academicYear: "" }; }
        var s = m[1].toLowerCase();
        var semester = s.charAt(0) === "1" ? "1st Semester" : s.charAt(0) === "2" ? "2nd Semester" : "Summer";
        return { semester: semester, academicYear: m[2] + "-" + m[3] };
    }

    // Saved names keep the existing "S.Y." format so they match the terms already in the database.
    /**
     * @param {string} semester
     * @param {string} academicYear
     */
    function buildName(semester, academicYear) {
        return semester + ", S.Y. " + academicYear;
    }

    /**
     * A copy, so the page cannot change the store by accident.
     * @param {GWTerm} t
     * @returns {GWTerm}
     */
    function copy(t) { return Object.assign({}, t); }

    /** @param {string | null} id */
    function find(id) {
        for (var i = 0; i < terms.length; i++) { if (terms[i].id === id) { return terms[i]; } }
        return null;
    }

    /**
     * @param {string} message
     * @returns {Promise<never>}
     */
    function fail(message) { return Promise.reject(new Error(message)); }

    /** @type {GWTermStoreApi} */
    var api = {
        parseName: parseName,
        buildName: buildName,

        // initial: [{ id, name, isCurrent }] as stored in the database today
        load: function (initial) {
            terms = (initial || []).map(function (t) {
                var p = parseName(t.name);
                return {
                    id: t.id,
                    name: t.name,
                    semester: p.semester,
                    academicYear: p.academicYear,
                    startDate: "",       // not stored in the database yet
                    endDate: "",         // not stored in the database yet
                    isCurrent: !!t.isCurrent,
                    archived: false,     // not stored in the database yet
                    archivedOn: ""
                };
            });
            changed = false;
        },

        list: function () { return terms.map(copy); },

        get: function (id) { var t = find(id); return t ? copy(t) : null; },

        hasChanges: function () { return changed; },

        // changes: { semester, academicYear, startDate, endDate } (already checked by the page)
        update: function (id, changes) {
            var t = find(id);
            if (!t) { return fail("That term no longer exists."); }
            t.semester = changes.semester;
            t.academicYear = changes.academicYear;
            t.name = buildName(changes.semester, changes.academicYear);
            t.startDate = changes.startDate;
            t.endDate = changes.endDate;
            changed = true;
            return Promise.resolve(copy(t));
        },

        archive: function (id, today) {
            var t = find(id);
            if (!t) { return fail("That term no longer exists."); }
            if (t.isCurrent) { return fail("The term in use cannot be archived. Make another term current first."); }
            t.archived = true;
            t.archivedOn = today;
            changed = true;
            return Promise.resolve(copy(t));
        },

        restore: function (id) {
            var t = find(id);
            if (!t) { return fail("That term no longer exists."); }
            t.archived = false;
            t.archivedOn = "";
            changed = true;
            return Promise.resolve(copy(t));
        }
    };
    return api;
})();

// =====================================================================
// 2) EXISTING SERVER ACTIONS (saved to the database)
// Add term -> POST /Admin/AddTerm (name)       Make current -> POST /Admin/SetCurrentTerm/{id}
// Both reload the page, which also clears the temporary store above.
// =====================================================================

window.GradeWiseTermServer = (function () {
    var root = document.querySelector(".gw-terms");
    /** @type {GWTermServerApi} */
    var api = {
        setCurrent: function (id) {
            var form = document.getElementById("term-current-form");
            var setUrl = root ? root.getAttribute("data-set-current-url") : null;
            if (!(form instanceof HTMLFormElement) || !setUrl) { return; }
            form.action = setUrl.replace(/\/$/, "") + "/" + encodeURIComponent(id);
            form.submit();
        }
        // Add term submits the add / edit form itself (see "add / edit form" below).
    };
    return api;
})();

// =====================================================================
// 3) PAGE: rules, table, filters, tabs and dialogs
// =====================================================================

(function () {
    // ---------- element lookups ----------
    // find...: the element, or null. require...: the element; stops with a clear message if the
    // page markup was changed.

    /**
     * @template {Element} T
     * @param {ParentNode} root
     * @param {string} selector
     * @param {{ new (): T, prototype: T }} type  e.g. HTMLInputElement
     * @returns {T | null}
     */
    function findIn(root, selector, type) {
        var found = root.querySelector(selector);
        return found instanceof type ? found : null;
    }

    /**
     * @template {Element} T
     * @param {ParentNode} root
     * @param {string} selector
     * @param {{ new (): T, prototype: T }} type
     * @returns {T}
     */
    function requireIn(root, selector, type) {
        var found = findIn(root, selector, type);
        if (!found) { throw new Error("Academic terms page: " + selector + " is missing or is the wrong kind of element."); }
        return found;
    }

    /**
     * @template {Element} T
     * @param {string} id
     * @param {{ new (): T, prototype: T }} type
     * @returns {T}
     */
    function requireById(id, type) {
        var found = document.getElementById(id);
        if (!(found instanceof type)) { throw new Error("Academic terms page: #" + id + " is missing or is the wrong kind of element."); }
        return found;
    }

    /**
     * @template {Element} T
     * @param {ParentNode} root
     * @param {string} selector
     * @param {{ new (): T, prototype: T }} type
     * @returns {T[]}
     */
    function allIn(root, selector, type) {
        var nodes = root.querySelectorAll(selector);
        /** @type {T[]} */
        var list = [];
        for (var i = 0; i < nodes.length; i++) {
            var node = nodes[i];
            if (node instanceof type) { list.push(node); }
        }
        return list;
    }

    var storeApi = window.GradeWiseTermStore;
    var serverApi = window.GradeWiseTermServer;
    var rootEl = findIn(document, ".gw-terms", HTMLElement);
    if (!storeApi || !serverApi || !rootEl) { return; }
    var store = storeApi;
    var server = serverApi;
    var root = rootEl;
    var today = root.getAttribute("data-today") || "";

    /** @type {GWTermRecord[]} */
    var initial = [];
    try {
        var parsed = JSON.parse(root.getAttribute("data-terms") || "[]");
        initial = Array.isArray(parsed) ? parsed : [];
    } catch (err) { initial = []; }
    store.load(initial);

    /**
     * @param {string} kind  "success" | "warning" | "error"
     * @param {string} text
     * @param {GWToastOptions} [options]
     */
    function toast(kind, text, options) {
        var gw = window.GW;
        if (gw && gw.toast) { gw.toast(kind, text, options); }
    }

    /**
     * The message of a rejected store call.
     * @param {unknown} err
     */
    function errorText(err) { return err instanceof Error ? err.message : String(err); }

    /** @type {Record<string, string>} */
    var STATUS_LABELS = { current: "Current", upcoming: "Upcoming", completed: "Completed", archived: "Archived", nodates: "No dates" };
    /** @type {Record<string, number>} */
    var SEM_ORDER = { "1st Semester": 1, "2nd Semester": 2, "Summer": 3 };
    var MONTHS = ["Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec"];

    /** @param {string} value  yyyy-mm-dd, or "" */
    function formatDate(value) {
        if (!value) { return "Not set"; }
        var p = value.split("-");
        return MONTHS[parseInt(p[1], 10) - 1] + " " + parseInt(p[2], 10) + ", " + p[0];
    }

    /** @param {GWTerm} t */
    function order(t) {
        if (!t.academicYear) { return 0; }
        return parseInt(t.academicYear.split("-")[0], 10) * 10 + (SEM_ORDER[t.semester] || 0);
    }

    // ========== RULES ==========
    // Status comes from the dates: before the start date Upcoming, after the end date Completed,
    // otherwise Current. "In use" is separate: it marks the term GradeWise uses as the current term
    // (the database flag that Make current sets).
    /** @param {GWTerm} t */
    function statusOf(t) {
        if (t.archived) { return "archived"; }
        if (!t.startDate || !t.endDate) { return "nodates"; }
        if (today < t.startDate) { return "upcoming"; }
        if (today > t.endDate) { return "completed"; }
        return "current";
    }

    /**
     * Why Make current is not allowed for this term ("" when it is allowed).
     * @param {GWTerm} t
     */
    function makeCurrentBlocker(t) {
        if (t.archived) { return "Archived terms cannot be made current."; }
        if (t.isCurrent) { return "This term is already in use."; }
        var s = statusOf(t);
        if (s === "nodates") { return "Add this term's start and end dates first (View, then Edit)."; }
        if (s === "upcoming") { return "This term has not started yet (it starts " + formatDate(t.startDate) + ")."; }
        if (s === "completed") { return "This term has already ended (" + formatDate(t.endDate) + ")."; }
        return "";
    }

    /** @param {string} academicYear  e.g. "2026-2027" */
    function academicYearRange(academicYear) {
        var y = academicYear.split("-");
        return { min: y[0] + "-01-01", max: y[1] + "-12-31" };
    }

    /** @typedef {{ startDate: string, endDate: string }} DateRange */
    /**
     * @param {DateRange} a
     * @param {DateRange} b
     */
    function overlaps(a, b) {
        return a.startDate <= b.endDate && a.endDate >= b.startDate;
    }

    /**
     * A term as typed in the form (new, edited or about to be restored).
     * @typedef {{ id: string | null, semester: string, academicYear: string, startDate: string, endDate: string, activeCheck: boolean }} TermInput
     */
    /**
     * One problem with a term. field is "year" | "semester" | "start" | "end"; missing marks an empty field.
     * @typedef {{ field: string, text: string, missing?: boolean }} TermProblem
     */

    /**
     * Checks one term (new, edited or about to be restored).
     * @param {TermInput} c
     * @returns {TermProblem[]}
     */
    function checkTerm(c) {
        /** @type {TermProblem[]} */
        var problems = [];
        if (!c.academicYear) { problems.push({ field: "year", text: "Choose an academic year.", missing: true }); }
        if (!c.semester) { problems.push({ field: "semester", text: "Choose a semester.", missing: true }); }
        if (!c.startDate) { problems.push({ field: "start", text: "Enter a start date.", missing: true }); }
        if (!c.endDate) { problems.push({ field: "end", text: "Enter an end date.", missing: true }); }

        if (c.startDate && c.endDate && c.endDate <= c.startDate) {
            problems.push({ field: "end", text: "The end date must be later than the start date." });
        }
        if (c.academicYear) {
            var range = academicYearRange(c.academicYear);
            var label = "A.Y. " + c.academicYear + " (" + formatDate(range.min) + " to " + formatDate(range.max) + ")";
            if (c.startDate && (c.startDate < range.min || c.startDate > range.max)) {
                problems.push({ field: "start", text: "The start date must fall within " + label + "." });
            }
            if (c.endDate && (c.endDate < range.min || c.endDate > range.max)) {
                problems.push({ field: "end", text: "The end date must fall within " + label + "." });
            }
        }

        var others = store.list().filter(function (t) { return t.id !== c.id; });
        if (c.academicYear && c.semester) {
            var name = store.buildName(c.semester, c.academicYear).toLowerCase();
            var twin = others.filter(function (t) {
                return (t.semester === c.semester && t.academicYear === c.academicYear) || (t.name || "").toLowerCase() === name;
            })[0];
            if (twin) {
                problems.push({ field: "semester", text: c.semester + " of A.Y. " + c.academicYear + " already exists" + (twin.archived ? " (in Archived terms)." : ".") });
                problems.push({ field: "year", text: "" });
            }
        }

        // Only active terms may not overlap (an archived term is checked again when it is restored).
        if (c.activeCheck && c.startDate && c.endDate && c.endDate > c.startDate) {
            others.filter(function (t) { return !t.archived && t.startDate && t.endDate; }).forEach(function (t) {
                if (overlaps(c, t)) {
                    problems.push({ field: "start", text: "These dates overlap " + t.name + " (" + formatDate(t.startDate) + " to " + formatDate(t.endDate) + ")." });
                }
            });
        }
        return problems;
    }

    // ========== helpers ==========
    /**
     * @template {keyof HTMLElementTagNameMap} K
     * @param {K} tag
     * @param {string | null} [cls]
     * @param {string} [text]
     * @returns {HTMLElementTagNameMap[K]}
     */
    function el(tag, cls, text) {
        var e = document.createElement(tag);
        if (cls) { e.className = cls; }
        if (text !== undefined) { e.textContent = text; }
        return e;
    }

    /**
     * A row action button.
     * @param {string} label
     * @param {string} action  data-action value
     * @param {string} [cls]  extra classes; defaults to the outline style
     * @param {string | null} [blockedReason]  when set, the button is disabled and explains why
     */
    function button(label, action, cls, blockedReason) {
        var b = el("button", "gw-btn gw-btn-small " + (cls === undefined ? "gw-btn-outline" : cls), label);
        b.type = "button";
        b.setAttribute("data-action", action);
        if (blockedReason) {
            b.disabled = true;
            b.title = blockedReason;
            b.setAttribute("aria-label", label + " (unavailable: " + blockedReason + ")");
        }
        return b;
    }

    /** @param {string} status */
    function badge(status) {
        return el("span", "gw-status gw-term-status gw-term-status-" + status, STATUS_LABELS[status] || status);
    }

    // ========== render ==========
    /** @type {Record<string, HTMLTableSectionElement>} */
    var tbodies = {
        active: requireIn(root, '[data-tbody="active"]', HTMLTableSectionElement),
        archived: requireIn(root, '[data-tbody="archived"]', HTMLTableSectionElement)
    };
    var mismatch = requireById("terms-mismatch", HTMLElement);

    /** @param {GWTerm} t */
    function renderRow(t) {
        var status = statusOf(t);
        var tr = el("tr");
        tr.setAttribute("data-term-row", "");
        tr.setAttribute("data-id", t.id);
        tr.setAttribute("data-name", t.name);
        tr.setAttribute("data-semester", t.semester);
        tr.setAttribute("data-year", t.academicYear);
        tr.setAttribute("data-status", status);
        if (t.isCurrent && !t.archived) { tr.className = "gw-term-current"; }

        // Academic year (phones: semester and status underneath)
        var first = el("td");
        first.appendChild(el("span", "gw-term-box", t.academicYear ? "A.Y. " + t.academicYear : t.name));
        first.appendChild(el("span", "gw-terms-row-sub", t.semester || ""));
        var under = el("span", "gw-terms-row-status");
        under.appendChild(t.archived ? el("span", "gw-status gw-term-status gw-term-status-archived", "Archived " + formatDate(t.archivedOn)) : badge(status));
        if (t.isCurrent && !t.archived) { under.appendChild(el("span", "gw-terms-inuse", "In use")); }
        first.appendChild(under);
        tr.appendChild(first);

        tr.appendChild(el("td", "gw-terms-col-sem", t.semester || "-"));
        tr.appendChild(el("td", "gw-terms-col-date" + (t.startDate ? "" : " gw-terms-unset"), formatDate(t.startDate)));
        tr.appendChild(el("td", "gw-terms-col-date" + (t.endDate ? "" : " gw-terms-unset"), formatDate(t.endDate)));

        var statusCell = el("td", "gw-terms-col-status");
        if (t.archived) {
            statusCell.appendChild(el("span", "gw-terms-archived-on", formatDate(t.archivedOn)));
        } else {
            statusCell.appendChild(badge(status));
            if (t.isCurrent) { statusCell.appendChild(el("span", "gw-terms-inuse", "In use")); }
        }
        tr.appendChild(statusCell);

        var actions = el("div", "gw-row-actions");
        actions.appendChild(button("View", "view"));
        if (t.archived) {
            actions.appendChild(button("Restore", "restore", "gw-btn-gold"));
        } else {
            actions.appendChild(button("Archive", "archive", "gw-btn-outline gw-terms-btn-archive",
                t.isCurrent ? "This term is in use. Make another term current before archiving it." : null));
            if (!t.isCurrent) {
                actions.appendChild(button("Make current", "current", "", makeCurrentBlocker(t) || null));
            }
        }
        var actionCell = el("td");
        actionCell.appendChild(actions);
        tr.appendChild(actionCell);
        return tr;
    }

    /**
     * The last row of a list: "no terms", or "no matches" with a Clear filters button.
     * @param {string} text
     * @param {boolean} withReset
     */
    function emptyRow(text, withReset) {
        var tr = el("tr");
        var td = el("td", "gw-empty", text);
        td.colSpan = 6;
        if (withReset) {
            var b = el("button", "gw-terms-linkbtn", "Clear filters");
            b.type = "button";
            b.setAttribute("data-filter-reset", "");
            td.appendChild(document.createTextNode(" "));
            td.appendChild(b);
            tr.setAttribute("data-filter-empty", "");
        }
        tr.appendChild(td);
        return tr;
    }

    function render() {
        var all = store.list();
        var active = all.filter(function (t) { return !t.archived; })
            .sort(function (a, b) { return order(b) - order(a) || (b.startDate || "").localeCompare(a.startDate || ""); });
        var archived = all.filter(function (t) { return t.archived; })
            .sort(function (a, b) { return (b.archivedOn || "").localeCompare(a.archivedOn || "") || order(b) - order(a); });

        [{ list: "active", terms: active, noTerms: "No terms yet. Use Add term to create one.", noMatch: "No active terms match these filters." },
         { list: "archived", terms: archived, noTerms: "No archived terms. Archived terms appear here and can be restored.", noMatch: "No archived terms match these filters." }]
        .forEach(function (set) {
            var body = tbodies[set.list];
            body.innerHTML = "";
            set.terms.forEach(function (t) { body.appendChild(renderRow(t)); });
            body.appendChild(set.terms.length ? emptyRow(set.noMatch, true) : emptyRow(set.noTerms, false));
            var counter = root.querySelector('[data-count="' + set.list + '"]');
            if (counter) { counter.textContent = String(set.terms.length); }
        });

        renderMismatch(active);
        refreshYearFilter(all);
        applyFilters();
    }

    /**
     * The term in use should be the one whose dates include today.
     * @param {GWTerm[]} active
     */
    function renderMismatch(active) {
        var inUse = active.filter(function (t) { return t.isCurrent; })[0];
        var byDate = active.filter(function (t) { return statusOf(t) === "current"; })[0];
        var text = "";
        if (byDate && (!inUse || inUse.id !== byDate.id)) {
            text = "Today falls within " + byDate.name + ", but " + (inUse ? inUse.name + " is still in use" : "no term is in use") +
                ". Use Make current on " + byDate.name + " to switch.";
        } else if (inUse && (statusOf(inUse) === "completed" || statusOf(inUse) === "upcoming")) {
            text = inUse.name + " is in use, but by its dates it is " + STATUS_LABELS[statusOf(inUse)].toLowerCase() + ".";
        }
        mismatch.textContent = text;
        mismatch.hidden = !text;
    }

    // ========== modals ==========
    /** @type {Array<Element | null>} what had focus before each open modal */
    var focusStack = [];

    /**
     * @param {HTMLElement} modal
     * @param {HTMLElement} [focusEl]  defaults to the modal's close button
     */
    function openModal(modal, focusEl) {
        focusStack.push(document.activeElement);
        modal.hidden = false;
        document.body.classList.add("gw-modal-open");
        var target = focusEl || modal.querySelector("[data-modal-close]");
        if (target instanceof HTMLElement) { target.focus(); }
    }

    /**
     * @param {Element | null} modal
     * @param {boolean} [keepFocus]  do not move focus back (another dialog opens next)
     */
    function closeModal(modal, keepFocus) {
        if (!(modal instanceof HTMLElement) || modal.hidden) { return; }
        modal.hidden = true;
        if (!root.querySelector(".gw-modal-backdrop:not([hidden])")) {
            document.body.classList.remove("gw-modal-open");
        }
        var back = focusStack.pop();
        if (!keepFocus && (back instanceof HTMLElement || back instanceof SVGElement) && document.body.contains(back)) { back.focus(); }
    }

    root.addEventListener("click", function (e) {
        var target = e.target;
        if (!(target instanceof Element)) { return; }
        var closer = target.closest("[data-modal-close]");
        if (closer) { closeModal(closer.closest(".gw-modal-backdrop")); return; }
        if (target.classList.contains("gw-modal-backdrop")) { closeModal(target); }
    });

    document.addEventListener("keydown", function (e) {
        if (e.key !== "Escape" || e.defaultPrevented) { return; }   // the shared dialog handles its own Esc
        var open = root.querySelectorAll(".gw-modal-backdrop:not([hidden])");
        if (open.length) { closeModal(open[open.length - 1]); }
    });

    // ========== confirmations (the shared dialog from gw-modal.js) ==========
    /**
     * @typedef {{ title: string, paragraphs: GWDialogPiece[][], yes: string, tone?: string, action: () => void }} ConfirmRequest
     */
    /** @param {ConfirmRequest} opts */
    function askConfirm(opts) {
        var gw = window.GW;
        if (!gw) { return; }
        gw.confirm({ title: opts.title, paragraphs: opts.paragraphs, okText: opts.yes, tone: opts.tone })
            .then(function (ok) { if (ok) { opts.action(); } });
    }

    /** @param {GWTerm} t */
    function askMakeCurrent(t) {
        var blocker = makeCurrentBlocker(t);
        if (blocker) { toast("warning", blocker); return; }
        var inUse = store.list().filter(function (x) { return x.isCurrent; })[0];
        /** @type {GWDialogPiece[][]} */
        var paragraphs = [
            [{ text: t.name, bold: true }, { text: " (" + formatDate(t.startDate) + " to " + formatDate(t.endDate) + ") will become the current term." }]
        ];
        if (inUse) { paragraphs.push([{ text: inUse.name + " will no longer be current.", muted: true }]); }
        paragraphs.push([{ text: "New courses will use the current term by default.", muted: true }]);
        if (store.hasChanges()) {
            paragraphs.push([{ text: "The page will reload, so preview changes made here (dates, edits, archiving) will be cleared.", muted: true }]);
        }
        askConfirm({
            title: "Change the current term?",
            paragraphs: paragraphs,
            yes: "Make current",
            action: function () { server.setCurrent(t.id); }
        });
    }

    /** @param {GWTerm} t */
    function askArchive(t) {
        if (t.isCurrent) { toast("warning", "This term is in use. Make another term current before archiving it."); return; }
        askConfirm({
            title: "Archive this term?",
            paragraphs: [
                [{ text: t.name, bold: true }, { text: " will move to Archived terms." }],
                [{ text: "Its courses, grades and history are kept. Nothing is deleted, and you can restore it at any time.", muted: true }]
            ],
            yes: "Archive term",
            tone: "danger",
            action: function () {
                store.archive(t.id, today).then(function (saved) {
                    render();
                    toast("success", saved.name + " was archived (preview only; resets on reload).");
                    focusRow(saved.id, "view");
                }, function (err) { toast("error", errorText(err)); });
            }
        });
    }

    /** @param {GWTerm} t */
    function askRestore(t) {
        // Dates are checked again: a restored term must not clash with the active terms.
        var problems = checkTerm({
            id: t.id, semester: t.semester, academicYear: t.academicYear,
            startDate: t.startDate, endDate: t.endDate, activeCheck: true
        }).filter(function (p) { return p.text; });
        if (problems.length) {
            toast("error", t.name + " cannot be restored yet. Open View, then Edit to fix:", {
                lines: problems.map(function (p) { return p.text; })
            });
            return;
        }
        askConfirm({
            title: "Restore this term?",
            paragraphs: [[{ text: t.name, bold: true }, { text: " (" + formatDate(t.startDate) + " to " + formatDate(t.endDate) + ") will move back to Active terms." }]],
            yes: "Restore term",
            action: function () {
                store.restore(t.id).then(function (saved) {
                    render();
                    toast("success", saved.name + " was restored (preview only; resets on reload).");
                    focusRow(saved.id, "view");
                }, function (err) { toast("error", errorText(err)); });
            }
        });
    }

    /**
     * After a row moves lists, put focus somewhere sensible.
     * @param {string} id  the term
     * @param {string} action  the data-action button to focus
     */
    function focusRow(id, action) {
        var row = root.querySelector('tr[data-term-row][data-id="' + id + '"]');
        if (!row) { return; }
        var panel = row.closest(".gw-terms-tabpanel");
        if (panel instanceof HTMLElement && panel.hidden) {
            var panelTab = document.getElementById(panel.getAttribute("aria-labelledby") || "");
            if (panelTab) { selectTab(panelTab, false); }
        }
        var b = findIn(row, '[data-action="' + action + '"]', HTMLElement);
        if (b) { b.focus(); }
    }

    // ========== add / edit form ==========
    var formModal = requireById("term-form-modal", HTMLElement);
    var form = requireById("term-form", HTMLFormElement);
    var formTitle = requireById("term-form-title", HTMLElement);
    var formNote = requireById("term-form-note", HTMLElement);
    var submitBtn = requireById("term-form-submit", HTMLButtonElement);
    var yearSel = requireById("term-year", HTMLSelectElement);
    var semSel = requireById("term-semester", HTMLSelectElement);
    var startIn = requireById("term-start", HTMLInputElement);
    var endIn = requireById("term-end", HTMLInputElement);
    var nameInput = requireById("term-name", HTMLInputElement);
    var preview = requireById("term-preview", HTMLElement);
    var feedback = requireById("term-feedback", HTMLElement);
    /** @type {Record<string, HTMLSelectElement | HTMLInputElement>} problem field -> form control */
    var FIELDS = { year: yearSel, semester: semSel, start: startIn, end: endIn };
    /** @type {GWTerm | null} the term being edited, null while adding */
    var editing = null;
    var tried = false;     // "missing" messages only after a first submit

    /** @returns {TermInput} */
    function currentInput() {
        return {
            id: editing ? editing.id : null,
            semester: semSel.value,
            academicYear: yearSel.value,
            startDate: startIn.value,
            endDate: endIn.value,
            activeCheck: !(editing && editing.archived)
        };
    }

    /** @returns {TermProblem[]} the problems shown */
    function renderForm() {
        var c = currentInput();
        var name = c.semester && c.academicYear ? store.buildName(c.semester, c.academicYear) : "";
        preview.textContent = name || "Choose an academic year and semester";
        nameInput.value = name;
        var range = c.academicYear ? academicYearRange(c.academicYear) : null;
        startIn.min = endIn.min = range ? range.min : "";
        startIn.max = endIn.max = range ? range.max : "";
        if (startIn.value) { endIn.min = startIn.value; }

        var problems = checkTerm(c).filter(function (p) { return tried || !p.missing; });
        Object.keys(FIELDS).forEach(function (k) {
            var bad = problems.some(function (p) { return p.field === k; });
            FIELDS[k].classList.toggle("gw-input-bad", bad);
            FIELDS[k].setAttribute("aria-invalid", bad ? "true" : "false");
        });
        var texts = problems.map(function (p) { return p.text; }).filter(function (t, i, a) { return t && a.indexOf(t) === i; });
        feedback.textContent = texts.join(" ");
        feedback.hidden = texts.length === 0;
        return problems;
    }

    Object.keys(FIELDS).forEach(function (k) {
        FIELDS[k].addEventListener("change", renderForm);
        FIELDS[k].addEventListener("input", renderForm);
    });

    /**
     * Adds an academic year to the form's list if it is not there (an old term being edited).
     * @param {string} year
     */
    function ensureYearOption(year) {
        if (!year) { return; }
        for (var i = 0; i < yearSel.options.length; i++) { if (yearSel.options[i].value === year) { return; } }
        var opt = el("option", null, "A.Y. " + year);
        opt.value = year;
        yearSel.appendChild(opt);
    }

    /** @param {GWTerm | null} t  the term to edit, or null to add one */
    function openForm(t) {
        editing = t || null;
        tried = false;
        form.reset();
        if (t) {
            formTitle.textContent = "Edit term";
            submitBtn.textContent = "Save changes";
            formNote.textContent = "Changes are kept on this page only (preview) and reset when the page reloads." +
                (t.archived ? " This term is archived, so overlaps are checked when it is restored." : "");
            ensureYearOption(t.academicYear);
            yearSel.value = t.academicYear || "";
            semSel.value = t.semester || "";
            startIn.value = t.startDate || "";
            endIn.value = t.endDate || "";
        } else {
            formTitle.textContent = "Add a term";
            submitBtn.textContent = "Add term";
            formNote.textContent = "The term is saved to the database by name. Its dates are checked here but not stored yet.";
        }
        submitBtn.disabled = false;
        renderForm();
        openModal(formModal, yearSel);
    }

    form.addEventListener("submit", function (e) {
        tried = true;
        var problems = renderForm();
        if (problems.length) {
            e.preventDefault();
            var first = FIELDS[problems[0].field];
            if (first) { first.focus(); }
            toast("error", editing ? "The changes were not saved. Please fix the highlighted fields." : "The term was not added. Please fix the highlighted fields.");
            return;
        }
        if (!editing) {
            // Add: let the form post to the existing AddTerm action (real save).
            submitBtn.disabled = true;
            return;
        }
        // Edit: temporary store only.
        e.preventDefault();
        var c = currentInput();
        store.update(editing.id, c).then(function (saved) {
            closeModal(formModal, true);
            render();
            toast("success", saved.name + " was updated (preview only; resets on reload).");
            focusRow(saved.id, "view");
        }, function (err) {
            toast("error", errorText(err));
        });
    });

    var addBtn = requireIn(root, "[data-term-add]", HTMLButtonElement);
    addBtn.addEventListener("click", function () { openForm(null); });

    // ========== view (Edit lives here) ==========
    var viewModal = requireById("term-view", HTMLElement);
    /** @type {GWTerm | null} */
    var viewing = null;

    /** @param {string} name  data-slot value */
    function slot(name) { return requireIn(viewModal, '[data-slot="' + name + '"]', HTMLElement); }

    /**
     * @param {GWTerm} t
     * @param {string} status
     */
    function viewNote(t, status) {
        if (t.archived) { return "Archived. Its records are kept. Restoring checks its dates against the active terms."; }
        if (status === "nodates") { return "Add the start and end dates (Edit) so the status can be worked out and the term can be made current."; }
        if (status === "upcoming") { return "Starts on " + formatDate(t.startDate) + ". It can be made current once it starts."; }
        if (status === "completed") { return "Ended on " + formatDate(t.endDate) + "."; }
        return t.isCurrent ? "Today falls within this term, and it is the term in use." : "Today falls within this term. Use Make current to start using it.";
    }

    /** @param {GWTerm} t */
    function openView(t) {
        var status = statusOf(t);
        viewing = t;
        slot("name").textContent = t.name;
        slot("year").textContent = t.academicYear ? "A.Y. " + t.academicYear : "Not in the standard format";
        slot("semester").textContent = t.semester || "Not in the standard format";
        slot("start").textContent = formatDate(t.startDate);
        slot("end").textContent = formatDate(t.endDate);
        var b = slot("status");
        b.textContent = STATUS_LABELS[status];
        b.className = "gw-status gw-term-status gw-term-status-" + status;
        slot("inuse").hidden = !(t.isCurrent && !t.archived);
        allIn(viewModal, "[data-slot-archived]", HTMLElement).forEach(function (item) { item.hidden = !t.archived; });
        slot("archived").textContent = formatDate(t.archivedOn);
        slot("note").textContent = viewNote(t, status);

        requireIn(viewModal, '[data-view-action="edit"]', HTMLButtonElement).hidden = false;
        requireIn(viewModal, '[data-view-action="restore"]', HTMLButtonElement).hidden = !t.archived;
        var mc = requireIn(viewModal, '[data-view-action="current"]', HTMLButtonElement);
        mc.hidden = t.archived || t.isCurrent;
        var blocker = makeCurrentBlocker(t);
        mc.disabled = !!blocker;
        mc.title = blocker;
        openModal(viewModal);
    }

    viewModal.addEventListener("click", function (e) {
        var target = e.target;
        if (!(target instanceof Element)) { return; }
        var b = target.closest("[data-view-action]");
        if (!(b instanceof HTMLButtonElement) || !viewing || b.disabled) { return; }
        var t = store.get(viewing.id);
        closeModal(viewModal, true);
        if (!t) { return; }
        var a = b.getAttribute("data-view-action");
        if (a === "edit") { openForm(t); }
        else if (a === "restore") { askRestore(t); }
        else { askMakeCurrent(t); }
    });

    // ========== row buttons ==========
    root.addEventListener("click", function (e) {
        var target = e.target;
        if (!(target instanceof Element)) { return; }
        var b = target.closest("tr[data-term-row] [data-action]");
        if (!(b instanceof HTMLButtonElement) || b.disabled) { return; }
        var row = b.closest("tr");
        if (!row) { return; }
        var t = store.get(row.getAttribute("data-id"));
        if (!t) { return; }
        var a = b.getAttribute("data-action");
        if (a === "view") { openView(t); }
        else if (a === "archive") { askArchive(t); }
        else if (a === "restore") { askRestore(t); }
        else if (a === "current") { askMakeCurrent(t); }
    });

    // ========== tabs ==========
    var tabs = allIn(root, '[role="tab"]', HTMLElement);
    var statusWrap = requireIn(root, "[data-filter-status-wrap]", HTMLElement);

    /**
     * @param {HTMLElement} tab
     * @param {boolean} focus  move keyboard focus to the tab
     */
    function selectTab(tab, focus) {
        for (var i = 0; i < tabs.length; i++) {
            var on = tabs[i] === tab;
            tabs[i].setAttribute("aria-selected", on ? "true" : "false");
            tabs[i].tabIndex = on ? 0 : -1;
            var tabPanel = document.getElementById(tabs[i].getAttribute("aria-controls") || "");
            if (tabPanel) { tabPanel.hidden = !on; }
        }
        statusWrap.hidden = tab.id === "tab-archived";   // status filter is for active terms
        if (focus) { tab.focus(); }
        applyFilters();
    }

    tabs.forEach(function (tab) {
        tab.addEventListener("click", function () { selectTab(tab, false); });
        tab.addEventListener("keydown", function (e) {
            if (e.key !== "ArrowRight" && e.key !== "ArrowLeft") { return; }
            var next = tabs[(tabs.indexOf(tab) + (e.key === "ArrowRight" ? 1 : tabs.length - 1)) % tabs.length];
            selectTab(next, true);
        });
    });

    // ========== filters ==========
    var fSearch = requireById("filter-search", HTMLInputElement);
    var fYear = requireById("filter-year", HTMLSelectElement);
    var fSem = requireById("filter-semester", HTMLSelectElement);
    var fStatus = requireById("filter-status", HTMLSelectElement);
    var count = requireById("filter-count", HTMLElement);
    var resetBtn = requireById("filter-reset", HTMLButtonElement);

    /** @param {GWTerm[]} all */
    function refreshYearFilter(all) {
        var keep = fYear.value;
        var years = all.map(function (t) { return t.academicYear; })
            .filter(function (y, i, a) { return y && a.indexOf(y) === i; }).sort().reverse();
        while (fYear.options.length > 1) { fYear.remove(1); }
        years.forEach(function (y) {
            var o = el("option", null, "A.Y. " + y);
            o.value = y;
            fYear.appendChild(o);
        });
        fYear.value = years.indexOf(keep) !== -1 ? keep : "";
    }

    function applyFilters() {
        var panel = root.querySelector(".gw-terms-tabpanel:not([hidden])");
        var q = fSearch.value.trim().toLowerCase();
        var shownHere = 0, totalHere = 0;
        allIn(root, ".gw-terms-tabpanel", HTMLElement).forEach(function (list) {
            var isArchived = list.getAttribute("data-list") === "archived";
            var rows = allIn(list, "tr[data-term-row]", HTMLTableRowElement);
            var shown = 0;
            rows.forEach(function (r) {
                var text = ((r.getAttribute("data-name") || "") + " " + (r.getAttribute("data-year") || "") + " " + (r.getAttribute("data-semester") || "")).toLowerCase();
                var ok = (!q || text.indexOf(q) !== -1) &&
                         (!fYear.value || r.getAttribute("data-year") === fYear.value) &&
                         (!fSem.value || r.getAttribute("data-semester") === fSem.value) &&
                         (isArchived || !fStatus.value || r.getAttribute("data-status") === fStatus.value);
                r.hidden = !ok;
                if (ok) { shown++; }
            });
            var empty = findIn(list, "[data-filter-empty]", HTMLElement);
            if (empty) { empty.hidden = shown !== 0; }
            if (list === panel) { shownHere = shown; totalHere = rows.length; }
        });
        if (!panel) { return; }
        var archivedTab = panel.getAttribute("data-list") === "archived";
        count.textContent = "Showing " + shownHere + " of " + totalHere + (archivedTab ? " archived" : " active") + (totalHere === 1 ? " term" : " terms");
    }

    function resetFilters() {
        fSearch.value = ""; fYear.value = ""; fSem.value = ""; fStatus.value = "";
        applyFilters();
    }

    fSearch.addEventListener("input", applyFilters);
    [fYear, fSem, fStatus].forEach(function (s) { s.addEventListener("change", applyFilters); });
    resetBtn.addEventListener("click", resetFilters);
    root.addEventListener("click", function (e) {
        var target = e.target;
        if (target instanceof Element && target.closest("[data-filter-reset]")) { resetFilters(); }
    });

    // ========== start ==========
    ["term-tabs", "term-filters", "terms-preview-note"].forEach(function (id) {
        var shownEl = document.getElementById(id);
        if (shownEl) { shownEl.hidden = false; }
    });
    addBtn.hidden = false;
    render();
})();
