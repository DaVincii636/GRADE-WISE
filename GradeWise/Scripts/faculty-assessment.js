/// <reference path="gw-globals.d.ts" />
// Faculty > Assessment scores page (Views/Faculty/Assessment.cshtml):
//   - the highest possible score box also sets the limit of every score box and the percent chips
//   - student search
//   - live count of scores entered and the average
// ASCII only.

(function () {
    var maxInput = document.getElementById("gw-as-max");
    var scoreInputs = [].slice.call(document.querySelectorAll("[data-as-score]"));
    var maxLabels = [].slice.call(document.querySelectorAll("[data-as-max-label]"));
    var enteredEl = document.getElementById("gw-as-entered");
    var missingEl = document.getElementById("gw-as-missing");
    var averageEl = document.getElementById("gw-as-average");

    /** @param {number | null} pct @returns {string} */
    function tone(pct) {
        if (pct === null) { return "none"; }
        return pct >= 75 ? "high" : pct >= 65 ? "mid" : "low";
    }

    /** @param {number} n @returns {string} */
    function fmt(n) { return n.toFixed(2); }

    function currentMax() {
        if (!(maxInput instanceof HTMLInputElement)) { return 0; }
        var v = parseFloat(maxInput.value);
        return isFinite(v) && v > 0 ? v : 0;
    }

    function refresh() {
        var max = currentMax();
        var entered = 0;
        var sum = 0;

        scoreInputs.forEach(function (input) {
            if (!(input instanceof HTMLInputElement)) { return; }
            if (max > 0) { input.max = String(max); }

            var row = input.closest("tr");
            var chip = row ? row.querySelector("[data-as-pct]") : null;
            var value = input.value.trim() === "" ? NaN : parseFloat(input.value);
            var pct = max > 0 && isFinite(value) ? value / max * 100 : null;

            if (pct !== null) { entered++; sum += pct; }
            if (chip) {
                chip.textContent = pct === null ? "\u2014" : fmt(pct);
                chip.className = "gw-fc-chip gw-sr-ps gw-sr-ps-" + tone(pct);
            }
            input.classList.toggle("gw-input-bad", isFinite(value) && max > 0 && (value < 0 || value > max + 0.0000001));
        });

        maxLabels.forEach(function (el) { el.textContent = max > 0 ? String(max) : ""; });
        if (enteredEl) { enteredEl.textContent = String(entered); }
        if (missingEl) { missingEl.textContent = String(scoreInputs.length - entered); }
        if (averageEl) { averageEl.textContent = entered === 0 ? "\u2014" : fmt(sum / entered); }
    }

    scoreInputs.forEach(function (input) { input.addEventListener("input", refresh); });
    if (maxInput) { maxInput.addEventListener("input", refresh); }

    // ---------- student search ----------
    var controls = document.getElementById("gw-sr-controls");
    var searchEl = document.getElementById("gw-sr-search");
    var countEl = document.getElementById("gw-sr-count");
    var clearBtn = document.getElementById("gw-sr-clear");
    var emptyRow = document.querySelector(".gw-sr-nomatch");

    if (controls && searchEl instanceof HTMLInputElement && countEl && emptyRow instanceof HTMLElement) {
        var search = searchEl;
        var empty = emptyRow;
        var rows = [].slice.call(document.querySelectorAll("tr[data-student]"));

        var applySearch = function () {
            var query = search.value.trim().toLowerCase();
            var shown = 0;
            rows.forEach(function (row) {
                var match = query === "" || (row.getAttribute("data-student") || "").indexOf(query) !== -1;
                row.hidden = !match;
                if (match) { shown++; }
            });
            empty.hidden = shown !== 0;
            countEl.textContent = query === ""
                ? rows.length + (rows.length === 1 ? " student" : " students")
                : shown + " of " + rows.length + (rows.length === 1 ? " student" : " students");
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
        var inlineClear = empty.querySelector("[data-sr-clear]");
        if (inlineClear) { inlineClear.addEventListener("click", clearSearch); }

        controls.hidden = false;
        applySearch();
    }

    refresh();
})();
