/// <reference path="gw-globals.d.ts" />
// Faculty > Course scores page (Views/Faculty/Course.cshtml):
//   - Midterm / Final Term / Final Grade tabs
//   - one category open at a time in the score table, so the table never needs a sideways scrollbar
//   - student search
// ASCII only.

(function () {
    // ---------- tabs ----------
    var tabs = [].slice.call(document.querySelectorAll('[role="tab"][data-tab]'));
    var activeTabInput = document.getElementById("active-tab");

    /**
     * @param {HTMLElement} tab
     * @param {boolean} focus
     */
    function selectTab(tab, focus) {
        tabs.forEach(function (t) {
            var on = t === tab;
            t.setAttribute("aria-selected", on ? "true" : "false");
            t.tabIndex = on ? 0 : -1;
            var panel = document.getElementById(t.getAttribute("aria-controls") || "");
            if (panel) { panel.hidden = !on; }
        });
        if (activeTabInput instanceof HTMLInputElement) { activeTabInput.value = tab.getAttribute("data-tab") || ""; }
        if (focus) { tab.focus(); }
        refreshLayout();
    }

    tabs.forEach(function (tab) {
        tab.addEventListener("click", function () { selectTab(tab, false); });
        tab.addEventListener("keydown", function (e) {
            if (e.key !== "ArrowRight" && e.key !== "ArrowLeft") { return; }
            var next = tabs[(tabs.indexOf(tab) + (e.key === "ArrowRight" ? 1 : tabs.length - 1)) % tabs.length];
            selectTab(next, true);
        });
    });

    // ---------- score tables: one open category ----------
    var tables = [].slice.call(document.querySelectorAll("[data-sr-table]"))
        .filter(function (t) { return t.querySelector("th[data-cat]"); });

    /** @param {Element} el @returns {string} */
    function storeKey(el) {
        var panel = el.closest(".gw-tabpanel");
        return "gw-sr:" + location.pathname + ":" + (panel ? panel.id : "");
    }

    /** @param {Element} table @returns {string} the open category index, or "" when none is open */
    function openOf(table) {
        var th = table.querySelector("th[data-cat].is-open");
        return th ? (th.getAttribute("data-cat") || "") : "";
    }

    /**
     * Sizes one group of header texts so they span their tiles: as wide as fits in one line,
     * and smaller as more columns are added. The group shares one size so the row stays even.
     * @param {HTMLElement[]} texts the .gw-sr-fit elements
     * @param {number} max biggest font size in px
     * @param {number} min smallest font size in px
     * @param {boolean} shared true: the whole group uses one size; false: each text fits its own tile
     */
    function fitGroup(texts, max, min, shared) {
        /** @type {number[]} */
        var sizes = texts.map(function (t) {
            var tile = t.closest(".gw-sr-gi");
            if (t.parentElement) { t.parentElement.classList.remove("gw-sr-fit-wrap"); }
            t.style.fontSize = max + "px";
            var avail = tile instanceof HTMLElement ? tile.clientWidth - 8 : 0;
            var w = t.getBoundingClientRect().width;
            return avail > 0 && w > 0 ? max * avail / w : max;
        });
        var common = Math.min.apply(null, sizes.concat([max]));
        texts.forEach(function (t, i) {
            var tile = t.closest(".gw-sr-gi");
            var size = Math.max(min, Math.floor((shared ? common : Math.min(max, sizes[i])) * 10) / 10);
            t.style.fontSize = size + "px";
            // still too wide at the smallest size: let long names break onto a second line
            if (tile instanceof HTMLElement && size <= min && t.getBoundingClientRect().width > tile.clientWidth - 6 && t.parentElement) {
                t.parentElement.classList.add("gw-sr-fit-wrap");
            }
        });
    }

    /** Header tiles of the open category: names, Total/PS and "+" fill the width of their tile. */
    function fitHeads(table) {
        var head = table.querySelector("th.is-open .gw-sr-grid-head");
        if (!head) { return; }
        [].forEach.call(head.querySelectorAll(".gw-sr-gi a"), function (a) {
            if (!a.querySelector(".gw-sr-fit")) {
                var span = document.createElement("span");
                span.className = "gw-sr-fit";
                span.textContent = a.textContent;
                a.textContent = "";
                a.appendChild(span);
            }
        });
        var all = function (sel) { return [].slice.call(head.querySelectorAll(sel)); };
        fitGroup(all(".gw-sr-gi:not(.gw-sr-gi-calc):not(.gw-sr-gi-add) .gw-sr-fit"), 15, 7, false);
        fitGroup(all(".gw-sr-gi-calc .gw-sr-fit"), 13, 6, true);
        fitGroup(all(".gw-sr-gi-add .gw-sr-fit"), 24, 12, true);
    }

    function refreshLayout() {
        tables.forEach(fitHeads);
    }

    /**
     * @param {Element} table
     * @param {string} cat category index, or "" to close all
     */
    function openCategory(table, cat) {
        [].forEach.call(table.querySelectorAll("[data-cat]"), function (el) {
            el.classList.toggle("is-open", cat !== "" && el.getAttribute("data-cat") === cat);
        });
        var panel = table.closest(".gw-tabpanel");
        [].forEach.call((panel || table).querySelectorAll("[data-sr-cat]"), function (btn) {
            var on = cat !== "" && btn.getAttribute("data-sr-cat") === cat;
            btn.setAttribute(btn.classList.contains("gw-sr-chip") ? "aria-pressed" : "aria-expanded", on ? "true" : "false");
        });
        try { sessionStorage.setItem(storeKey(table), cat === "" ? "none" : cat); } catch (err) { /* storage may be blocked */ }
        fitHeads(table);
    }

    document.addEventListener("click", function (e) {
        var target = e.target;
        if (!(target instanceof Element)) { return; }
        var btn = target.closest("[data-sr-cat]");
        if (!btn) { return; }
        var panel = btn.closest(".gw-tabpanel");
        var table = panel ? panel.querySelector("[data-sr-table]") : null;
        if (!table) { return; }
        var cat = btn.getAttribute("data-sr-cat") || "";
        openCategory(table, openOf(table) === cat ? "" : cat);
    });

    tables.forEach(function (table) {
        // Boxes with a bad value (after a failed save) mark their category and open the first one.
        var bad = table.querySelectorAll(".gw-input-bad");
        var panel = table.closest(".gw-tabpanel");
        var firstBad = "";
        [].forEach.call(bad, function (input) {
            var cell = input.closest("[data-cat]");
            var cat = cell ? cell.getAttribute("data-cat") || "" : "";
            if (cat && !firstBad) { firstBad = cat; }
            if (panel && cat) {
                [].forEach.call(panel.querySelectorAll('.gw-sr-chip[data-sr-cat="' + cat + '"]'), function (chip) {
                    chip.classList.add("gw-sr-chip-bad");
                });
            }
        });

        var first = table.querySelector("th[data-cat]");
        var start = first ? first.getAttribute("data-cat") || "" : "";
        if (firstBad) {
            start = firstBad;
        } else {
            try {
                var saved = sessionStorage.getItem(storeKey(table));
                if (saved === "none") { start = ""; }
                else if (saved && table.querySelector('th[data-cat="' + saved + '"]')) { start = saved; }
            } catch (err) { /* ignore */ }
        }
        openCategory(table, start);
    });

    // A box the browser rejects (above its highest possible score) may sit in a closed category or another tab:
    // show it so the browser can point at it.
    var scoreForm = document.getElementById("score-form");
    if (scoreForm) {
        scoreForm.addEventListener("invalid", function (e) {
            var input = e.target;
            if (!(input instanceof Element)) { return; }
            var panel = input.closest(".gw-tabpanel");
            if (panel && panel.hidden) {
                var tab = document.getElementById("tab-" + panel.id.replace(/^panel-/, ""));
                if (tab instanceof HTMLElement) { selectTab(tab, false); }
            }
            var cell = input.closest("[data-cat]");
            var table = input.closest("[data-sr-table]");
            if (cell && table && !cell.classList.contains("is-open")) {
                openCategory(table, cell.getAttribute("data-cat") || "");
            }
        }, true);
    }

    window.addEventListener("resize", refreshLayout);
    if (document.fonts && document.fonts.ready) { document.fonts.ready.then(refreshLayout); }

    // ---------- student search ----------
    var controls = document.getElementById("gw-sr-controls");
    var searchEl = document.getElementById("gw-sr-search");
    var countEl = document.getElementById("gw-sr-count");
    var clearBtn = document.getElementById("gw-sr-clear");

    if (controls && searchEl instanceof HTMLInputElement && countEl) {
        var search = searchEl;
        var allRows = [].slice.call(document.querySelectorAll("tr[data-student]"));
        var studentTotal = document.querySelectorAll(".gw-sr-final tr[data-student]").length;

        var applySearch = function () {
            var query = search.value.trim().toLowerCase();
            var shown = 0;
            allRows.forEach(function (row) {
                var match = query === "" || (row.getAttribute("data-student") || "").indexOf(query) !== -1;
                row.hidden = !match;
                if (match && row.closest(".gw-sr-final")) { shown++; }
            });
            [].forEach.call(document.querySelectorAll(".gw-sr-nomatch"), function (row) {
                row.hidden = shown !== 0;
            });
            countEl.textContent = query === ""
                ? studentTotal + (studentTotal === 1 ? " student" : " students")
                : shown + " of " + studentTotal + (studentTotal === 1 ? " student" : " students");
        };

        var clearSearch = function () {
            var had = search.value !== "";
            search.value = "";
            applySearch();
            search.focus();
            if (had && window.GW && window.GW.toast) { window.GW.toast("success", "Search cleared."); }
        };

        search.addEventListener("input", applySearch);
        if (clearBtn) { clearBtn.addEventListener("click", clearSearch); }
        [].forEach.call(document.querySelectorAll("[data-sr-clear]"), function (b) {
            b.addEventListener("click", clearSearch);
        });

        controls.hidden = false;
        applySearch();
    }

    refreshLayout();
})();
