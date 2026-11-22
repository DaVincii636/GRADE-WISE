/// <reference path="gw-globals.d.ts" />
// Student > My courses (Views/Student/Index.cshtml): day and date box and course search filter. The filter bar is hidden in the markup and
// only shown here, so without JavaScript every course is simply listed.

(function () {
    // ---------- Day and date: from the browser, so they match the student's own time zone ----------
    var names = ["Sunday", "Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday"];
    var now = new Date();
    var two = function (n) { return n < 10 ? "0" + n : "" + n; };
    var day = document.getElementById("gw-day");
    var date = document.getElementById("gw-date");
    if (day) { day.textContent = names[now.getDay()]; }
    if (date) { date.textContent = two(now.getMonth() + 1) + "/" + two(now.getDate()) + "/" + now.getFullYear(); }

    // ---------- Course search filter ----------
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

    // A search box is only useful for a longer list of courses.
    if (rows.length < 4) {
        return;
    }

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
        search.value = "";
        apply();
        search.focus();
    }

    search.addEventListener("input", apply);
    if (clearBtn) { clearBtn.addEventListener("click", clear); }
    var inlineClear = emptyRow.querySelector("[data-fc-clear]");
    if (inlineClear) { inlineClear.addEventListener("click", clear); }

    controls.hidden = false;
    apply();
})();
