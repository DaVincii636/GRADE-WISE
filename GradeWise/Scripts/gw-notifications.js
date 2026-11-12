/*
 * GradeWise student notifications (Phase 7): the bell button and its dropdown.
 *
 * The server draws the list (Views/Notifications/Bell.cshtml). This file only handles clicks:
 * open and close, mark read or unread, delete, delete all and mark all read. Every change is a
 * POST with the anti-forgery token and answers { ok, unread }; the page is updated without a reload.
 *
 * ES5 only and ASCII only, so the bundle minifier accepts it.
 */
(function (root) {
    "use strict";

    var doc = root.document;
    var host = doc.getElementById("gw-notif");
    if (!host) { return; }

    var MAX_VISIBLE = 5;   // newest items shown before the list scrolls

    var btn = doc.getElementById("gw-notif-btn");
    var panel = doc.getElementById("gw-notif-panel");
    var list = doc.getElementById("gw-notif-list");
    var menu = doc.getElementById("gw-notif-menu");
    var badge = doc.getElementById("gw-notif-badge");
    var unreadRow = doc.getElementById("gw-notif-unreadrow");
    var countEl = doc.getElementById("gw-notif-count");
    var wordEl = doc.getElementById("gw-notif-word");
    var emptyEl = doc.getElementById("gw-notif-empty");
    var deleteAllBtn = doc.getElementById("gw-notif-delete-all");
    var markAllBtn = doc.getElementById("gw-notif-mark-all");
    var liveEl = doc.getElementById("gw-notif-live");
    var toggleItem = menu.querySelector('[data-act="toggle"]');
    var viewItem = menu.querySelector('[data-act="view"]');

    var URLS = {
        read: host.getAttribute("data-url-read"),
        unread: host.getAttribute("data-url-unread"),
        readAll: host.getAttribute("data-url-readall"),
        remove: host.getAttribute("data-url-delete"),
        removeAll: host.getAttribute("data-url-deleteall"),
        course: host.getAttribute("data-url-course")
    };

    var menuLi = null;        // the list item the open menu belongs to
    var menuTrigger = null;   // its three-dots button

    // ---------- small helpers ----------

    function closest(el, cls) {
        while (el && el !== doc) {
            if (el.classList && el.classList.contains(cls)) { return el; }
            el = el.parentNode;
        }
        return null;
    }

    function items() {
        return [].slice.call(list.querySelectorAll(".gw-notif-item"));
    }

    function pad(n) { return n < 10 ? "0" + n : "" + n; }

    // now, 5m, 24h, 2d, and MM/dd/yyyy after 7 days (computed in the browser from the UTC time)
    function relative(iso) {
        var t = Date.parse(iso);
        if (isNaN(t)) { return null; }

        var seconds = Math.floor((Date.now() - t) / 1000);
        if (seconds < 0) { seconds = 0; }
        if (seconds < 60) { return "now"; }

        var minutes = Math.floor(seconds / 60);
        if (minutes < 60) { return minutes + "m"; }

        var hours = Math.floor(minutes / 60);
        if (hours < 24) { return hours + "h"; }

        var days = Math.floor(hours / 24);
        if (days < 7) { return days + "d"; }

        var d = new Date(t);
        return pad(d.getMonth() + 1) + "/" + pad(d.getDate()) + "/" + d.getFullYear();
    }

    function refreshTimes() {
        var all = items();
        for (var i = 0; i < all.length; i++) {
            var text = relative(all[i].getAttribute("data-created"));
            var slot = all[i].querySelector(".gw-notif-time");
            if (text && slot) { slot.textContent = text; }
        }
    }

    function courseUrl(courseId) {
        return URLS.course.replace("__ID__", encodeURIComponent(courseId)) + "#course-summary";
    }

    function fail() {
        if (root.GW && root.GW.alert) {
            root.GW.alert({
                title: "Could not update",
                message: "Something went wrong. Please refresh the page and try again."
            });
        }
    }

    function post(url, id) {
        var token = doc.querySelector('#gw-notif-token input[name="__RequestVerificationToken"]');
        var body = "__RequestVerificationToken=" + encodeURIComponent(token ? token.value : "");
        if (id) { body += "&id=" + encodeURIComponent(id); }

        return root.fetch(url, {
            method: "POST",
            credentials: "same-origin",
            headers: {
                "Content-Type": "application/x-www-form-urlencoded; charset=UTF-8",
                "X-Requested-With": "XMLHttpRequest"
            },
            body: body
        }).then(function (response) {
            return response.json();
        }).then(function (data) {
            if (!data || !data.ok) { throw new Error("not ok"); }
            return data;
        });
    }

    // ---------- showing the state ----------

    function setUnread(n) {
        host.setAttribute("data-unread", n);
        countEl.textContent = n;
        wordEl.textContent = n === 1 ? "notification" : "notifications";
        unreadRow.hidden = n <= 0;
        badge.hidden = n <= 0;
        badge.textContent = n > 9 ? "9+" : "" + n;
        if (liveEl) {
            liveEl.textContent = n === 0 ? "No unread notifications" : n + " unread notifications";
        }
    }

    function setReadState(li, read) {
        if (read) { li.classList.remove("is-unread"); } else { li.classList.add("is-unread"); }
    }

    function syncEmpty() {
        var has = items().length > 0;
        emptyEl.hidden = has;
        deleteAllBtn.hidden = !has;
    }

    // Show the newest 5 items; anything older scrolls inside the list.
    function sizeList() {
        list.style.maxHeight = "";
        list.style.overflowY = "";

        var all = items();
        if (all.length <= MAX_VISIBLE) { return; }

        var height = 0;
        for (var i = 0; i < MAX_VISIBLE; i++) { height += all[i].offsetHeight; }
        list.style.maxHeight = height + "px";
        list.style.overflowY = "auto";
    }

    // ---------- open and close ----------

    function panelOpen() { return !panel.hidden; }

    function openPanel() {
        panel.hidden = false;
        btn.setAttribute("aria-expanded", "true");
        refreshTimes();
        sizeList();
    }

    function closePanel(returnFocus) {
        closeMenu();
        panel.hidden = true;
        btn.setAttribute("aria-expanded", "false");
        if (returnFocus) { btn.focus(); }
    }

    function closeMenu() {
        if (menu.hidden) { return; }
        menu.hidden = true;
        if (menuTrigger) { menuTrigger.setAttribute("aria-expanded", "false"); }
        menuLi = null;
        menuTrigger = null;
    }

    // The menu is positioned from the three-dots button, so the scrolling list never clips it.
    function openMenu(li, trigger) {
        closeMenu();
        menuLi = li;
        menuTrigger = trigger;

        toggleItem.textContent = li.classList.contains("is-unread") ? "Mark as read" : "Mark as unread";

        var course = li.getAttribute("data-course");
        var canView = li.getAttribute("data-kind") !== "removed" && !!course;
        viewItem.hidden = !canView;
        if (canView) { viewItem.setAttribute("href", courseUrl(course)); }

        menu.hidden = false;
        trigger.setAttribute("aria-expanded", "true");

        var rect = trigger.getBoundingClientRect();
        var width = menu.offsetWidth;
        var height = menu.offsetHeight;
        var left = Math.max(8, Math.min(rect.right - width, root.innerWidth - width - 8));
        var top = rect.bottom + 4;
        if (top + height > root.innerHeight - 8 && rect.top - height - 4 >= 8) {
            top = rect.top - height - 4;   // open upward near the bottom of the screen
        }
        menu.style.left = left + "px";
        menu.style.top = top + "px";

        var first = menu.querySelector(".gw-notif-menuitem:not([hidden])");
        if (first) { first.focus(); }
    }

    // ---------- actions ----------

    function navigate(courseId) {
        var target = courseUrl(courseId);
        var here = root.location.pathname + root.location.search;
        if (target.split("#")[0] !== here) {
            try { root.sessionStorage.setItem("gwScroll", "1"); } catch (e) { }
        }
        root.location.href = target;
    }

    function openItem(li) {
        var id = li.getAttribute("data-id");
        var course = li.getAttribute("data-course");
        var go = li.getAttribute("data-kind") !== "removed" && !!course;

        var marked = Promise.resolve();
        if (li.classList.contains("is-unread")) {
            marked = post(URLS.read, id).then(function (data) {
                setReadState(li, true);
                setUnread(data.unread);
            });
        }

        marked.then(function () {
            if (go) { navigate(course); }
        }, function () {
            if (go) { navigate(course); }
        });
    }

    function toggleRead(li) {
        var makeRead = li.classList.contains("is-unread");
        post(makeRead ? URLS.read : URLS.unread, li.getAttribute("data-id")).then(function (data) {
            setReadState(li, makeRead);
            setUnread(data.unread);
        }, fail);
    }

    function removeItem(li) {
        post(URLS.remove, li.getAttribute("data-id")).then(function (data) {
            var next = li.nextElementSibling || li.previousElementSibling;
            li.parentNode.removeChild(li);
            setUnread(data.unread);
            syncEmpty();
            sizeList();

            var focusTarget = next ? next.querySelector(".gw-notif-main") : null;
            (focusTarget || btn).focus();
        }, fail);
    }

    function markAll() {
        post(URLS.readAll).then(function (data) {
            var all = items();
            for (var i = 0; i < all.length; i++) { setReadState(all[i], true); }
            setUnread(data.unread);
        }, fail);
    }

    function deleteAll() {
        if (!root.GW || !root.GW.confirm) { return; }

        closePanel(true);
        root.GW.confirm({
            title: "Delete all notifications?",
            message: "Every notification will be removed. You cannot undo this.",
            okText: "Delete all",
            cancelText: "Cancel",
            tone: "danger"
        }).then(function (ok) {
            if (!ok) { return; }
            post(URLS.removeAll).then(function (data) {
                list.innerHTML = "";
                setUnread(data.unread);
                syncEmpty();
                sizeList();
            }, fail);
        });
    }

    // ---------- wiring ----------

    btn.addEventListener("click", function () {
        if (panelOpen()) { closePanel(false); } else { openPanel(); }
    });

    list.addEventListener("click", function (e) {
        var more = closest(e.target, "gw-notif-more");
        if (more) {
            var li = closest(more, "gw-notif-item");
            if (menuLi === li && !menu.hidden) { closeMenu(); } else { openMenu(li, more); }
            return;
        }

        var main = closest(e.target, "gw-notif-main");
        if (main) { openItem(closest(main, "gw-notif-item")); }
    });

    list.addEventListener("scroll", closeMenu);
    root.addEventListener("resize", closeMenu);

    menu.addEventListener("click", function (e) {
        var item = closest(e.target, "gw-notif-menuitem");
        if (!item || !menuLi) { return; }

        var act = item.getAttribute("data-act");
        var li = menuLi;
        closeMenu();

        if (act === "toggle") {
            toggleRead(li);
        } else if (act === "delete") {
            removeItem(li);
        } else if (act === "view") {
            e.preventDefault();
            openItem(li);
        }
    });

    menu.addEventListener("keydown", function (e) {
        if (e.key !== "ArrowDown" && e.key !== "ArrowUp") { return; }

        var all = [].slice.call(menu.querySelectorAll(".gw-notif-menuitem:not([hidden])"));
        var at = all.indexOf(doc.activeElement);
        var next = e.key === "ArrowDown" ? at + 1 : at - 1;
        if (next < 0) { next = all.length - 1; }
        if (next >= all.length) { next = 0; }
        all[next].focus();
        e.preventDefault();
    });

    doc.addEventListener("keydown", function (e) {
        if (e.key !== "Escape" && e.key !== "Esc") { return; }

        if (!menu.hidden) {
            var trigger = menuTrigger;
            closeMenu();
            if (trigger) { trigger.focus(); }
        } else if (panelOpen()) {
            closePanel(true);
        }
    });

    doc.addEventListener("click", function (e) {
        var t = e.target;
        if (!menu.hidden && !menu.contains(t) && !closest(t, "gw-notif-more")) { closeMenu(); }
        if (panelOpen() && !host.contains(t)) { closePanel(false); }
    });

    deleteAllBtn.addEventListener("click", deleteAll);
    markAllBtn.addEventListener("click", markAll);

    refreshTimes();
})(window);
