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

    // ---------- Goal text on each course: the goal the student picked on the What-If simulator ----------
    // Same key and same formulas as the Course page alert (see student-goal.js and whatif.js).
    var goalList = document.getElementById("gw-fc-list");
    var calc = window.GradeWiseWhatIf;
    if (goalList) {
        var student = goalList.getAttribute("data-student") || "";
        var scale = (goalList.getAttribute("data-scale") || "").split(";").map(function (pair) {
            var parts = pair.split(":");
            return { min: parseFloat(parts[0]), grade: parseFloat(parts[1]) };
        }).filter(function (step) { return isFinite(step.min) && isFinite(step.grade); });
        var f2 = function (n) { return n.toFixed(2); };
        var eqOf = function (value) {
            for (var i = 0; i < scale.length; i++) { if (Math.round(value * 1e6) / 1e6 >= scale[i].min) { return scale[i].grade; } }
            return 5;
        };

        Array.prototype.forEach.call(goalList.querySelectorAll("[data-course-row]"), function (row) {
            var out = row.querySelector("[data-goal-text]");
            if (!out) { return; }
            var goal = null;
            try {
                var v = parseFloat(localStorage.getItem("gw.goal." + student + "." + row.getAttribute("data-course")));
                if (isFinite(v) && v >= 0 && v <= 100) { goal = v; }
            } catch (e) { }
            if (goal === null) { return; }

            var head = "Your goal: " + f2(eqOf(goal)) + " (" + f2(goal) + "%)";
            var state = "info";
            var text = head;
            if (row.getAttribute("data-ready") === "1") {
                var earned = parseFloat(row.getAttribute("data-earned"));
                var remaining = parseFloat(row.getAttribute("data-remaining"));
                var result = calc ? calc.checkTarget(goal, earned, remaining) : null;
                if (result) {
                    if (result.status === "Secured") { state = "ok"; text = head + ". Secured."; }
                    else if (result.status === "NothingLeft") { state = "warn"; text = head + ". Not reached."; }
                    else if (result.status === "OutOfReach") { state = "warn"; text = head + ". Out of reach this term."; }
                    else { text = head + ". Needs an average of " + f2(result.required) + " on the remaining " + f2(remaining) + "%."; }
                }
            }
            out.textContent = text;
            out.className = "gw-sg-goaltext gw-sg-goal-" + state;
        });
    }

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
