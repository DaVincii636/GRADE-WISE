/// <reference path="gw-globals.d.ts" />
// Student > Course page (Views/Student/Course.cshtml): the goal alert at the top of the page.
// The goal is the target grade the student picks on the What-If simulator. The simulator keeps it in this browser
// (localStorage) under "gw.goal.<student number>.<course id>", and this page only reads it.
// The formulas come from whatif.js (GradeWiseWhatIf), so every page works the same numbers out.

(function () {
    "use strict";

    var host = document.getElementById("gw-goal-alert");
    var api = window.GradeWiseWhatIf;
    if (!host || !api) { return; }

    var textEl = document.getElementById("gw-goal-text");
    var linkEl = document.getElementById("gw-goal-link");
    if (!textEl) { return; }

    var key = host.getAttribute("data-key");
    var earned = parseFloat(host.getAttribute("data-earned"));
    var remaining = parseFloat(host.getAttribute("data-remaining"));
    var scale = (host.getAttribute("data-scale") || "").split(";").map(function (pair) {
        var parts = pair.split(":");
        return { min: parseFloat(parts[0]), grade: parseFloat(parts[1]) };
    }).filter(function (step) { return isFinite(step.min) && isFinite(step.grade); });

    var goal = null;
    try {
        var saved = parseFloat(localStorage.getItem(key));
        if (isFinite(saved) && saved >= 0 && saved <= 100) { goal = saved; }
    } catch (e) { }

    // No goal yet: leave the invitation the page was sent with.
    if (goal === null) { return; }

    var f = api.format;
    var head = f(api.equivalentGrade(goal, scale)) + " (" + f(goal) + "%). ";
    var result = api.checkTarget(goal, earned, remaining);
    var kind = "info";
    var text;

    if (result.status === "Secured") {
        kind = "ok";
        text = head + "Secured. Your " + f(earned) + " points already meet it.";
    } else if (result.status === "NothingLeft") {
        kind = "warn";
        text = head + "Not reached. All work is graded and you earned " + f(earned) + ".";
    } else if (result.status === "OutOfReach") {
        kind = "warn";
        text = head + "Out of reach. Even 100 on everything left gives " + f(earned + remaining) + " at most.";
    } else {
        text = head + "You need an average of " + f(result.required) + " on the remaining " + f(remaining) + "%.";
    }

    host.className = "gw-alert gw-alert-" + kind + " gw-goal-alert";
    textEl.textContent = text;
    if (linkEl) { linkEl.textContent = "Change goal"; }
})();
