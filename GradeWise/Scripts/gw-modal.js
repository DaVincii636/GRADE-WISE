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
 */
(function (root) {
    "use strict";

    var doc = root.document;
    var FOCUSABLE = 'a[href], button:not([disabled]), input:not([disabled]):not([type="hidden"]), ' +
                    'select:not([disabled]), textarea:not([disabled]), [tabindex]:not([tabindex="-1"])';

    var stack = [];          // open modals, top is last: { el, opener, onClose }
    var approved = [];       // elements the user already confirmed (so the replayed action passes through)

    function isApproved(el) { return approved.indexOf(el) !== -1; }
    function approve(el) { approved.push(el); }
    function unapprove(el) {
        var i = approved.indexOf(el);
        if (i !== -1) approved.splice(i, 1);
    }

    function visibleFocusables(el) {
        return [].slice.call(el.querySelectorAll(FOCUSABLE)).filter(function (n) {
            return n.offsetWidth > 0 || n.offsetHeight > 0 || n === doc.activeElement;
        });
    }

    function topEntry() { return stack.length ? stack[stack.length - 1] : null; }

    // ---------- open / close (any element with class gw-modal-backdrop) ----------

    function open(target, options) {
        var el = typeof target === "string" ? doc.getElementById(target) : target;
        if (!el) return null;
        options = options || {};

        var entry = { el: el, opener: doc.activeElement, onClose: options.onClose || null };
        stack.push(entry);

        el.hidden = false;
        doc.body.classList.add("gw-modal-open");

        var first = options.focus || el.querySelector("[data-autofocus]") || visibleFocusables(el)[0];
        if (first) first.focus();
        return el;
    }

    function close(target) {
        var el = typeof target === "string" ? doc.getElementById(target) : target;
        if (!el) return;

        var index = -1;
        for (var i = stack.length - 1; i >= 0; i--) {
            if (stack[i].el === el) { index = i; break; }
        }
        if (index === -1) { el.hidden = true; return; }

        var entry = stack.splice(index, 1)[0];
        el.hidden = true;
        if (!stack.length) doc.body.classList.remove("gw-modal-open");

        if (entry.opener && typeof entry.opener.focus === "function" && doc.body.contains(entry.opener)) {
            entry.opener.focus();
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

    var dialog = null;     // { backdrop, title, message, ok, cancel, foot }
    var queue = Promise.resolve();

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

    // Fills the message box: one paragraph from "message", or several (with bold or muted pieces) from "paragraphs".
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

    // Shows the dialog once and resolves true (OK) or false (Cancel, Esc, backdrop click).
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

    function enqueue(fn) {
        var next = queue.then(fn);
        queue = next.then(function () {}, function () {});
        return next;
    }

    function confirmDialog(opts) {
        opts = opts || {};
        return enqueue(function () { return show(opts, true); });
    }

    function alertDialog(opts) {
        opts = opts || {};
        return enqueue(function () { return show(opts, false); }).then(function () {});
    }

    // ---------- declarative: data-confirm ----------

    function optionsFrom(el) {
        return {
            title: el.getAttribute("data-confirm-title") || "Please confirm",
            message: el.getAttribute("data-confirm") || "",
            okText: el.getAttribute("data-confirm-ok") || "OK",
            cancelText: el.getAttribute("data-confirm-cancel") || "Cancel",
            tone: el.getAttribute("data-confirm-tone") || ""
        };
    }

    // Submit a form after OK. A plain submit() skips the submit event, so the clicked button's
    // name and value are copied into a hidden input first (the server often reads them).
    function submitForm(form, submitter) {
        if (submitter && submitter.name) {
            var hidden = doc.createElement("input");
            hidden.type = "hidden";
            hidden.name = submitter.name;
            hidden.value = submitter.value;
            form.appendChild(hidden);
        }
        HTMLFormElement.prototype.submit.call(form);
    }

    doc.addEventListener("click", function (e) {
        var el = e.target.closest ? e.target.closest("a[data-confirm], button[data-confirm]") : null;
        if (!el) return;

        if (isApproved(el)) { unapprove(el); return; }   // the replayed click after OK

        var form = el.tagName === "BUTTON" ? el.form : null;
        var isSubmit = el.tagName === "BUTTON" && form && (el.type === "submit" || !el.getAttribute("type"));

        // Let the browser show its own "fill this in" hints before asking anything.
        if (isSubmit && !form.checkValidity()) return;

        e.preventDefault();
        e.stopImmediatePropagation();

        confirmDialog(optionsFrom(el)).then(function (ok) {
            if (!ok) return;
            if (isSubmit) {
                approve(form);
                submitForm(form, el);
            } else {
                approve(el);
                el.click();
            }
        });
    }, true);

    doc.addEventListener("submit", function (e) {
        var form = e.target;
        if (!form || !form.matches || !form.matches("form[data-confirm]")) return;

        if (isApproved(form)) { unapprove(form); return; }

        e.preventDefault();
        var submitter = e.submitter || null;

        confirmDialog(optionsFrom(form)).then(function (ok) {
            if (!ok) return;
            approve(form);
            submitForm(form, submitter);
        });
    }, true);

    // ---------- markup-defined modals: openers, closers, open-on-load ----------

    doc.addEventListener("click", function (e) {
        if (!e.target.closest) return;

        var opener = e.target.closest("[data-modal-open]");
        if (opener) {
            e.preventDefault();
            open(opener.getAttribute("data-modal-open"));
            return;
        }

        var closer = e.target.closest("[data-modal-close]");
        if (closer) {
            var backdrop = closer.closest(".gw-modal-backdrop");
            if (backdrop) {
                e.preventDefault();
                close(backdrop);
            }
        }
    });

    function openOnLoad() {
        var el = doc.querySelector(".gw-modal-backdrop[data-open-on-load]");
        if (el) open(el);
    }

    if (doc.readyState === "loading") {
        doc.addEventListener("DOMContentLoaded", openOnLoad);
    } else {
        openOnLoad();
    }

    root.GW = root.GW || {};
    root.GW.confirm = confirmDialog;
    root.GW.alert = alertDialog;
    root.GW.modal = { open: open, close: close };
})(window);
