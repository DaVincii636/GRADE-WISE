/// <reference path="gw-globals.d.ts" />
/*
 * GradeWise shared modal dialogs (Phase 2).
 *
 *   GW.confirm({ title, message, okText, cancelText, tone })  -> Promise<boolean>
 *     (instead of "message" you may pass "paragraphs": [[{ text, bold, muted }, ...], ...], one inner array per paragraph)
 *   GW.alert({ title, message, okText })                       -> Promise<void>
 *   GW.modal.open(elementOrId) / GW.modal.close(element)       -> markup-defined modals
 *
 * Declarative (no script needed on the page):
 *   <form data-confirm="Delete this account?" data-confirm-title="Delete account?"
 *         data-confirm-ok="Delete" data-confirm-tone="danger">
 *   <button data-confirm="..."> and <a data-confirm="..."> work the same way.
 *
 * Markup-defined modal: an element with class "gw-modal-backdrop" and the "hidden" attribute.
 *   <button data-modal-open="my-modal">, <button data-modal-close> inside it,
 *   and data-open-on-load on the backdrop opens it when the page loads.
 *
 * Esc and a click on the dark backdrop close the top modal; Tab stays inside it;
 * focus goes back to whatever opened it. ASCII only in this file.
 * Types: Scripts/gw-globals.d.ts (editor only).
 */
(/** @param {Window} root */ function (root) {
    "use strict";

    var doc = root.document;
    var FOCUSABLE = 'a[href], button:not([disabled]), input:not([disabled]):not([type="hidden"]), ' +
                    'select:not([disabled]), textarea:not([disabled]), [tabindex]:not([tabindex="-1"])';

    /** @typedef {{ el: HTMLElement, opener: Element | null, onClose: (() => void) | null }} ModalEntry */

    /** @type {ModalEntry[]} open modals, top is last */
    var stack = [];
    /** @type {Element[]} elements the user already confirmed (so the replayed action passes through) */
    var approved = [];

    /** @param {Element} el */
    function isApproved(el) { return approved.indexOf(el) !== -1; }
    /** @param {Element} el */
    function approve(el) { approved.push(el); }
    /** @param {Element} el */
    function unapprove(el) {
        var i = approved.indexOf(el);
        if (i !== -1) approved.splice(i, 1);
    }

    /**
     * The focusable elements inside el that are shown (or already focused).
     * @param {ParentNode} el
     * @returns {HTMLElement[]}
     */
    function visibleFocusables(el) {
        var found = el.querySelectorAll(FOCUSABLE);
        /** @type {HTMLElement[]} */
        var list = [];
        for (var i = 0; i < found.length; i++) {
            var n = found[i];
            if (n instanceof HTMLElement && (n.offsetWidth > 0 || n.offsetHeight > 0 || n === doc.activeElement)) {
                list.push(n);
            }
        }
        return list;
    }

    /** @returns {ModalEntry | null} */
    function topEntry() { return stack.length ? stack[stack.length - 1] : null; }

    /**
     * @param {string | HTMLElement | null} target  the modal or its id
     * @returns {HTMLElement | null}
     */
    function modalElement(target) {
        return typeof target === "string" ? doc.getElementById(target) : target;
    }

    // ---------- open / close (any element with class gw-modal-backdrop) ----------

    /**
     * @param {string | HTMLElement | null} target
     * @param {GWModalOpenOptions} [options]
     * @returns {HTMLElement | null}
     */
    function open(target, options) {
        var el = modalElement(target);
        if (!el) return null;
        options = options || {};

        /** @type {ModalEntry} */
        var entry = { el: el, opener: doc.activeElement, onClose: options.onClose || null };
        stack.push(entry);

        el.hidden = false;
        doc.body.classList.add("gw-modal-open");

        var autofocus = el.querySelector("[data-autofocus]");
        var first = options.focus || (autofocus instanceof HTMLElement ? autofocus : null) || visibleFocusables(el)[0];
        if (first) first.focus();
        return el;
    }

    /** @param {string | HTMLElement | null} target */
    function close(target) {
        var el = modalElement(target);
        if (!el) return;

        var index = -1;
        for (var i = stack.length - 1; i >= 0; i--) {
            if (stack[i].el === el) { index = i; break; }
        }
        if (index === -1) { el.hidden = true; return; }

        var entry = stack.splice(index, 1)[0];
        el.hidden = true;
        if (!stack.length) doc.body.classList.remove("gw-modal-open");

        var opener = entry.opener;
        if ((opener instanceof HTMLElement || opener instanceof SVGElement) && doc.body.contains(opener)) {
            opener.focus();
        }
        if (entry.onClose) entry.onClose();
    }

    doc.addEventListener("keydown", function (e) {
        var top = topEntry();
        if (!top) return;

        if (e.key === "Escape" || e.key === "Esc") {
            e.preventDefault();
            close(top.el);
            return;
        }

        if (e.key === "Tab") {
            var items = visibleFocusables(top.el);
            if (!items.length) { e.preventDefault(); return; }
            var firstItem = items[0];
            var lastItem = items[items.length - 1];
            var active = doc.activeElement;
            if (!top.el.contains(active)) {
                e.preventDefault();
                firstItem.focus();
            } else if (e.shiftKey && active === firstItem) {
                e.preventDefault();
                lastItem.focus();
            } else if (!e.shiftKey && active === lastItem) {
                e.preventDefault();
                firstItem.focus();
            }
        }
    });

    // click on the dark backdrop itself (not on the card) closes the top modal
    doc.addEventListener("mousedown", function (e) {
        var top = topEntry();
        if (top && e.target === top.el) close(top.el);
    });

    // ---------- the reusable confirm / alert dialog ----------

    /** @typedef {{ backdrop: HTMLDivElement, title: HTMLHeadingElement, message: HTMLDivElement, ok: HTMLButtonElement, cancel: HTMLButtonElement, foot: HTMLDivElement }} Dialog */

    /** @type {Dialog | null} */
    var dialog = null;
    /** @type {Promise<void>} */
    var queue = Promise.resolve();

    /** @returns {Dialog} */
    function build() {
        if (dialog) return dialog;

        var backdrop = doc.createElement("div");
        backdrop.className = "gw-modal-backdrop gw-modal-backdrop--top";
        backdrop.hidden = true;

        var card = doc.createElement("div");
        card.className = "gw-panel gw-card-raised gw-modal-card gw-modal-card--sm";
        card.setAttribute("role", "dialog");
        card.setAttribute("aria-modal", "true");
        card.setAttribute("aria-labelledby", "gw-dialog-title");
        card.setAttribute("aria-describedby", "gw-dialog-message");

        var head = doc.createElement("div");
        head.className = "gw-panel-head";
        var title = doc.createElement("h2");
        title.className = "gw-panel-title";
        title.id = "gw-dialog-title";
        head.appendChild(title);

        var body = doc.createElement("div");
        body.className = "gw-panel-body gw-modal-body";
        var message = doc.createElement("div");
        message.id = "gw-dialog-message";
        body.appendChild(message);

        var foot = doc.createElement("div");
        foot.className = "gw-modal-foot";
        var cancel = doc.createElement("button");
        cancel.type = "button";
        cancel.className = "gw-btn gw-btn-outline";
        var ok = doc.createElement("button");
        ok.type = "button";
        ok.className = "gw-btn";
        foot.appendChild(cancel);
        foot.appendChild(ok);

        card.appendChild(head);
        card.appendChild(body);
        card.appendChild(foot);
        backdrop.appendChild(card);
        doc.body.appendChild(backdrop);

        dialog = { backdrop: backdrop, title: title, message: message, ok: ok, cancel: cancel, foot: foot };
        return dialog;
    }

    /**
     * Fills the message box: one paragraph from "message", or several (with bold or muted pieces) from "paragraphs".
     * @param {HTMLElement} box
     * @param {GWDialogOptions} opts
     */
    function fillMessage(box, opts) {
        while (box.firstChild) box.removeChild(box.firstChild);

        var list = opts.paragraphs;
        if (!list || !list.length) list = [[{ text: opts.message || "" }]];

        list.forEach(function (pieces) {
            var p = doc.createElement("p");
            pieces.forEach(function (piece) {
                if (piece.muted) p.className = "gw-muted";
                if (piece.bold) {
                    var strong = doc.createElement("strong");
                    strong.textContent = piece.text;
                    p.appendChild(strong);
                } else {
                    p.appendChild(doc.createTextNode(piece.text));
                }
            });
            box.appendChild(p);
        });
    }

    /**
     * Shows the dialog once and resolves true (OK) or false (Cancel, Esc, backdrop click).
     * @param {GWDialogOptions} opts
     * @param {boolean} withCancel  false for a notice with only OK
     * @returns {Promise<boolean>} true when OK was chosen
     */
    function show(opts, withCancel) {
        return new Promise(function (resolve) {
            var d = build();
            var danger = opts.tone === "danger";
            var settled = false;

            d.title.textContent = opts.title || (withCancel ? "Please confirm" : "Notice");
            fillMessage(d.message, opts);
            d.ok.textContent = opts.okText || "OK";
            d.cancel.textContent = opts.cancelText || "Cancel";
            d.cancel.hidden = !withCancel;
            d.ok.className = "gw-btn" + (danger ? " gw-btn-danger" : "");
            d.foot.className = "gw-modal-foot" + (danger ? " gw-modal-foot--danger" : "");

            /** @param {boolean} result */
            function finish(result) {
                if (settled) return;
                settled = true;
                d.ok.removeEventListener("click", onOk);
                d.cancel.removeEventListener("click", onCancel);
                close(d.backdrop);
                resolve(result);
            }
            function onOk() { finish(true); }
            function onCancel() { finish(false); }

            d.ok.addEventListener("click", onOk);
            d.cancel.addEventListener("click", onCancel);

            // Esc or backdrop click closes through close(), which calls onClose: that counts as Cancel.
            open(d.backdrop, {
                focus: (danger && withCancel) ? d.cancel : d.ok,
                onClose: function () { finish(false); }
            });
        });
    }

    /**
     * Runs dialogs one after another.
     * @template T
     * @param {() => Promise<T>} fn
     * @returns {Promise<T>}
     */
    function enqueue(fn) {
        var next = queue.then(fn);
        queue = next.then(function () {}, function () {});
        return next;
    }

    /**
     * @param {GWDialogOptions} [opts]
     * @returns {Promise<boolean>} resolves true when the user confirms
     */
    function confirmDialog(opts) {
        var options = opts || {};
        return enqueue(function () { return show(options, true); });
    }

    /**
     * @param {GWDialogOptions} [opts]
     * @returns {Promise<void>} resolves when the notice is closed
     */
    function alertDialog(opts) {
        var options = opts || {};
        return enqueue(function () { return show(options, false); }).then(function () {});
    }

    // ---------- declarative: data-confirm ----------

    /**
     * @param {Element} el  the form, button or link with data-confirm
     * @returns {GWDialogOptions}
     */
    function optionsFrom(el) {
        return {
            title: el.getAttribute("data-confirm-title") || "Please confirm",
            message: el.getAttribute("data-confirm") || "",
            okText: el.getAttribute("data-confirm-ok") || "OK",
            cancelText: el.getAttribute("data-confirm-cancel") || "Cancel",
            tone: el.getAttribute("data-confirm-tone") || ""
        };
    }

    /**
     * Submit a form after OK. A plain submit() skips the submit event, so the clicked button's
     * name and value are copied into a hidden input first (the server often reads them).
     * @param {HTMLFormElement} form
     * @param {HTMLElement | null} submitter
     */
    function submitForm(form, submitter) {
        if ((submitter instanceof HTMLButtonElement || submitter instanceof HTMLInputElement) && submitter.name) {
            var hidden = doc.createElement("input");
            hidden.type = "hidden";
            hidden.name = submitter.name;
            hidden.value = submitter.value;
            form.appendChild(hidden);
        }
        HTMLFormElement.prototype.submit.call(form);
    }

    doc.addEventListener("click", function (e) {
        var target = e.target;
        if (!(target instanceof Element)) return;
        // a link or a button
        var el = target.closest("a[data-confirm], button[data-confirm]");
        if (!(el instanceof HTMLAnchorElement || el instanceof HTMLButtonElement)) return;
        var clicked = el;

        if (isApproved(clicked)) { unapprove(clicked); return; }   // the replayed click after OK

        // the form a submit button sends, or null for a link or a plain button
        var form = clicked instanceof HTMLButtonElement && clicked.form &&
                   (clicked.type === "submit" || !clicked.getAttribute("type")) ? clicked.form : null;

        // Let the browser show its own "fill this in" hints before asking anything.
        if (form && !form.checkValidity()) return;

        e.preventDefault();
        e.stopImmediatePropagation();

        confirmDialog(optionsFrom(clicked)).then(function (ok) {
            if (!ok) return;
            if (form) {
                approve(form);
                submitForm(form, clicked);
            } else {
                approve(clicked);
                clicked.click();
            }
        });
    }, true);

    doc.addEventListener("submit", function (e) {
        var form = e.target;
        if (!(form instanceof HTMLFormElement) || !form.matches("form[data-confirm]")) return;
        var confirmForm = form;

        if (isApproved(confirmForm)) { unapprove(confirmForm); return; }

        e.preventDefault();
        var submitter = e.submitter || null;

        confirmDialog(optionsFrom(confirmForm)).then(function (ok) {
            if (!ok) return;
            approve(confirmForm);
            submitForm(confirmForm, submitter);
        });
    }, true);

    // ---------- markup-defined modals: openers, closers, open-on-load ----------

    doc.addEventListener("click", function (e) {
        var target = e.target;
        if (!(target instanceof Element)) return;

        var opener = target.closest("[data-modal-open]");
        if (opener) {
            e.preventDefault();
            open(opener.getAttribute("data-modal-open"));
            return;
        }

        var closer = target.closest("[data-modal-close]");
        if (closer) {
            var backdrop = closer.closest(".gw-modal-backdrop");
            if (backdrop instanceof HTMLElement) {
                e.preventDefault();
                close(backdrop);
            }
        }
    });

    function openOnLoad() {
        var el = doc.querySelector(".gw-modal-backdrop[data-open-on-load]");
        if (el instanceof HTMLElement) open(el);
    }

    if (doc.readyState === "loading") {
        doc.addEventListener("DOMContentLoaded", openOnLoad);
    } else {
        openOnLoad();
    }


    // ---------- Toasts ----------
    // GW.toast("success" | "warning" | "error", message, { title, lines, duration })
    // Short notices in the top-right corner. They float above the page (no layout shift), close on
    // their own after a few seconds (paused while hovered or focused) and have a close button.
    /** @type {HTMLDivElement | null} */
    var toastStack = null;
    /** @type {Record<string, string>} */
    var TOAST_TITLES = { success: "Done", warning: "Heads up", error: "Not done" };
    /** @type {Record<string, number>} */
    var TOAST_TIMES = { success: 4000, warning: 5000, error: 5000 };

    /** @returns {HTMLDivElement} */
    function toastHost() {
        if (toastStack && doc.body.contains(toastStack)) return toastStack;
        var host = doc.createElement("div");
        host.className = "gw-toasts";
        host.setAttribute("aria-label", "Notifications");
        doc.body.appendChild(host);
        toastStack = host;
        return host;
    }

    /**
     * @param {GWToastKind} kind  "success" | "warning" | "error"
     * @param {string} message
     * @param {GWToastOptions} [options]
     * @returns {GWToastHandle}
     */
    function toast(kind, message, options) {
        var opts = options || {};
        if (!TOAST_TITLES[kind]) kind = "success";
        var host = toastHost();
        while (host.children.length >= 4 && host.firstElementChild) host.removeChild(host.firstElementChild);

        var el = doc.createElement("div");
        el.className = "gw-toast gw-toast-" + kind;
        el.setAttribute("role", kind === "error" ? "alert" : "status");
        el.setAttribute("aria-live", kind === "error" ? "assertive" : "polite");

        var body = doc.createElement("div");
        body.className = "gw-toast-body";
        var title = doc.createElement("span");
        title.className = "gw-toast-title";
        title.textContent = opts.title || TOAST_TITLES[kind];
        body.appendChild(title);
        var text = doc.createElement("p");
        text.className = "gw-toast-text";
        text.textContent = message;
        body.appendChild(text);
        if (opts.lines && opts.lines.length) {
            var list = doc.createElement("ul");
            list.className = "gw-toast-list";
            opts.lines.forEach(function (line) {
                var li = doc.createElement("li");
                li.textContent = line;
                list.appendChild(li);
            });
            body.appendChild(list);
        }
        el.appendChild(body);

        var closeBtn = doc.createElement("button");
        closeBtn.type = "button";
        closeBtn.className = "gw-toast-close";
        closeBtn.setAttribute("aria-label", "Dismiss notification");
        closeBtn.innerHTML = "<span aria-hidden=\"true\">&times;</span>";
        el.appendChild(closeBtn);

        var timer = doc.createElement("span");
        timer.className = "gw-toast-timer";
        el.appendChild(timer);

        var duration = opts.duration || TOAST_TIMES[kind];
        var left = duration, started = 0, gone = false;
        /** @type {number | undefined} the running close timer, if any */
        var handle;

        function dismiss() {
            if (gone) return;
            gone = true;
            clearTimeout(handle);
            el.classList.add("gw-toast-leaving");
            setTimeout(function () { if (el.parentNode) el.parentNode.removeChild(el); }, 200);
        }
        function run() {
            started = Date.now();
            timer.style.animationDuration = duration + "ms";
            timer.style.animationPlayState = "running";
            handle = setTimeout(dismiss, left);
        }
        function pause() {
            if (gone || !handle) return;
            clearTimeout(handle);
            handle = undefined;
            left -= Date.now() - started;
            timer.style.animationPlayState = "paused";
        }
        function resume() {
            if (gone || handle || el.contains(doc.activeElement)) return;
            run();
        }

        closeBtn.addEventListener("click", dismiss);
        el.addEventListener("mouseenter", pause);
        el.addEventListener("mouseleave", resume);
        el.addEventListener("focusin", pause);
        el.addEventListener("focusout", function () { setTimeout(resume, 0); });

        host.appendChild(el);
        run();
        return { close: dismiss };
    }


    // ---------- Server messages as toasts ----------
    // Any element marked data-gw-flash="success" | "warning" | "error" (the banners pages render from
    // TempData) is shown as a toast once and removed from the page. Without JavaScript the banner
    // simply stays. GW.flashes() returns what was shown, for page scripts that need the text.
    /** @type {GWFlash[] | null} */
    var flashed = null;

    /** @returns {GWFlash[]} */
    function flashes() {
        if (flashed) return flashed;
        /** @type {GWFlash[]} */
        var shown = [];
        flashed = shown;
        /** @type {Record<string, boolean>} */
        var seen = {};
        var items = doc.querySelectorAll("[data-gw-flash]");
        for (var i = 0; i < items.length; i++) {
            var item = items[i];
            var kind = item.getAttribute("data-gw-flash") || "success";
            var text = (item.textContent || "").replace(/\s+/g, " ").trim();
            if (item.parentNode) item.parentNode.removeChild(item);
            if (!text || seen[kind + "|" + text]) continue;   // never the same notice twice
            seen[kind + "|" + text] = true;
            shown.push({ kind: kind, text: text });
            toast(kind, text);
        }
        return shown;
    }

    /** @type {GWApi} */
    var api = {
        confirm: confirmDialog,
        alert: alertDialog,
        modal: { open: open, close: close },
        toast: toast,
        flashes: flashes
    };
    // Keep anything another script already put on window.GW.
    root.GW = Object.assign(root.GW || {}, api);

    if (doc.readyState === "loading") {
        doc.addEventListener("DOMContentLoaded", flashes);
    } else {
        flashes();
    }
})(window);
