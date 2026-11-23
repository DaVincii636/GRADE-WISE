/// <reference path="gw-globals.d.ts" />
// Faculty > My courses (Views/Faculty/Index.cshtml): search filter for the course list.
// The filter bar is hidden in the markup and only shown here, so without JavaScript
// the full course list is simply listed.

(function () {
    var controls = document.getElementById("gw-fc-controls");
    var searchEl = document.getElementById("gw-fc-search");
    var list = document.getElementById("gw-fc-list");
    var countEl = document.getElementById("gw-fc-count");
    var emptyRow = document.getElementById("gw-fc-empty");
    var clearBtn = document.getElementById("gw-fc-clear");

    if (!controls || !(searchEl instanceof HTMLInputElement) || !list || !countEl || !emptyRow) {
        return;
    }
    var search = searchEl;
    var rows = Array.prototype.slice.call(list.querySelectorAll("[data-course-row]"));

    function apply() {
        var query = search.value.trim().toLowerCase();
        var shown = 0;

        rows.forEach(function (row) {
            var haystack = row.getAttribute("data-search") || "";
            var match = query === "" || haystack.indexOf(query) !== -1;
            row.hidden = !match;
            if (match) { shown++; }
        });

        emptyRow.hidden = shown !== 0;
        countEl.textContent = query === ""
            ? rows.length + (rows.length === 1 ? " course" : " courses")
            : shown + " of " + rows.length + (rows.length === 1 ? " course" : " courses");
    }

    function clear() {
        var had = search.value !== "";
        search.value = "";
        apply();
        search.focus();
        if (had && window.GW && window.GW.toast) { window.GW.toast("success", "Filters cleared."); }
    }

    search.addEventListener("input", apply);
    if (clearBtn) { clearBtn.addEventListener("click", clear); }
    var inlineClear = emptyRow.querySelector("[data-fc-clear]");
    if (inlineClear) { inlineClear.addEventListener("click", clear); }

    controls.hidden = false;
    apply();
})();
