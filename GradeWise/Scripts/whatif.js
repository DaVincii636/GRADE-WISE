/*
 * GradeWise What-If simulator (Phase 7, updated in Phase 10).
 *
 * The formulas at the top mirror Services/GradeCalculator.cs (ProjectFinal, CheckTarget, EquivalentGrade).
 * KEEP THEM IDENTICAL: if a formula or the grade scale changes in one file, change it in the other too.
 * The grade scale itself is not copied here; it is read from the target buttons the server renders
 * (GradeCalculator.Scale()), and the default target comes from GradeCalculator.PassingAverage (data-target).
 *
 * Scores and weights are percentages. "earned" is points already earned (weight x score / 100, added up),
 * "remaining" is the weight of the categories that are not graded yet.
 */
(function (root) {
    "use strict";

    var WEIGHT_TOLERANCE = 0.005;   // same as GradeCalculator.WeightTolerance
    var TARGET_TOLERANCE = 0.000001; // same as GradeCalculator.TargetTolerance
    var FAILING_GRADE = 5.0;        // same as GradeCalculator.FailingGrade

    // ---------- formulas (no page code in here, so they can be tested on their own) ----------

    function clampScore(score) {
        return Math.max(0, Math.min(100, score));
    }

    // final = earned + sum of (weight x score / 100)
    function projectFinal(earned, weights, scores) {
        var total = earned;
        var count = Math.min(weights.length, scores.length);
        for (var i = 0; i < count; i++) {
            total += weights[i] * clampScore(scores[i]) / 100;
        }
        return total;
    }

    // required = (target - earned) / remaining x 100
    // status: "NothingLeft" | "Secured" | "Reachable" | "OutOfReach"
    function checkTarget(target, earned, remaining) {
        if (remaining <= WEIGHT_TOLERANCE) {
            return { status: "NothingLeft", required: null };
        }

        var required = (target - earned) / remaining * 100;
        var status = "Reachable";
        if (required <= TARGET_TOLERANCE) status = "Secured";
        else if (required > 100 + TARGET_TOLERANCE) status = "OutOfReach";

        return { status: status, required: required };
    }

    // scale: [{ min: 97, grade: 1 }, ...] highest first. Below the last line is 5.00.
    function equivalentGrade(average, scale) {
        var value = Math.round(average * 1e6) / 1e6;
        for (var i = 0; i < scale.length; i++) {
            if (value >= scale[i].min) return scale[i].grade;
        }
        return FAILING_GRADE;
    }

    // Smallest two-decimal score that still reaches the target (so rounding never leaves you just short).
    function roundUpScore(value) {
        return Math.ceil(value * 100 - 1e-9) / 100;
    }

    function fmt(value) {
        return value.toFixed(2);
    }

    var api = {
        projectFinal: projectFinal,
        checkTarget: checkTarget,
        equivalentGrade: equivalentGrade,
        roundUpScore: roundUpScore,
        format: fmt
    };

    root.GradeWiseWhatIf = api;
    if (typeof module !== "undefined" && module.exports) {
        module.exports = api;
    }

    // ---------- page wiring ----------

    if (typeof document === "undefined") return;

    function init() {
        var host = document.getElementById("whatif");
        if (!host) return;

        var earned = parseFloat(host.getAttribute("data-earned"));
        var remaining = parseFloat(host.getAttribute("data-remaining"));
        var defaultScore = parseFloat(host.getAttribute("data-default"));
        var targetAttr = parseFloat(host.getAttribute("data-target"));
        var defaultTarget = isFinite(targetAttr) ? targetAttr : 65;   // 65 = GradeCalculator.PassingAverage

        var finalOut = document.getElementById("sim-final");
        var gradeOut = document.getElementById("sim-grade");
        var scenarioMsg = document.getElementById("sim-scenario-msg");
        var targetRange = document.getElementById("sim-target-range");
        var targetNum = document.getElementById("sim-target-num");
        var targetMsg = document.getElementById("sim-target-msg");
        var compareMsg = document.getElementById("sim-compare");
        var applyBtn = document.getElementById("sim-apply");
        var resetBtn = document.getElementById("sim-reset");

        // "How this is calculated" table: one row per slider row, in the same order.
        var calcRows = [].slice.call(document.querySelectorAll(".gw-calc-row")).map(function (tr) {
            return { formula: tr.querySelector(".gw-calc-formula"), points: tr.querySelector(".gw-calc-points") };
        });
        var calcSum = document.getElementById("calc-sum");
        var calcTotal = document.getElementById("calc-total");
        var calcGrade = document.getElementById("calc-grade");

        // Grade table, read from the target buttons so the numbers come from the server's scale.
        var chips = [].slice.call(document.querySelectorAll(".gw-chip"));
        var scale = chips.map(function (c) {
            return { min: parseFloat(c.getAttribute("data-min")), grade: parseFloat(c.getAttribute("data-grade")) };
        });

        var rows = [].slice.call(document.querySelectorAll(".gw-sim-row")).map(function (tr) {
            return {
                weight: parseFloat(tr.getAttribute("data-weight")),
                range: tr.querySelector(".gw-sim-range"),
                num: tr.querySelector(".gw-sim-num")
            };
        });

        // A box holds a valid number from 0 to 100, or it does not (empty, text, out of range).
        function readNumber(input) {
            var text = input.value.trim();
            if (text === "") return null;
            var n = Number(text);
            if (!isFinite(n) || n < 0 || n > 100) return null;
            return n;
        }

        function mark(input, bad) {
            if (bad) {
                input.classList.add("gw-input-bad");
                input.setAttribute("aria-invalid", "true");
            } else {
                input.classList.remove("gw-input-bad");
                input.removeAttribute("aria-invalid");
            }
        }

        function setMessage(el, kind, text) {
            el.className = "gw-alert gw-sim-msg" + (kind ? " gw-alert-" + kind : "");
            el.textContent = text;
        }

        function short(value) {
            return String(Math.round(value * 100) / 100);
        }

        // Fills the "How this is calculated" table from the same numbers the sliders use.
        function updateCalc(projected) {
            if (!calcSum || !calcTotal || !calcGrade) return;

            var parts = [fmt(earned)];
            rows.forEach(function (row, i) {
                var cell = calcRows[i];
                if (!cell) return;
                var value = readNumber(row.num);
                if (value === null) {
                    cell.formula.textContent = short(row.weight) + "% x (enter a score)";
                    cell.points.textContent = "\u2014";
                    return;
                }
                var points = row.weight * clampScore(value) / 100;
                cell.formula.textContent = short(row.weight) + "% x " + short(value) + " / 100";
                cell.points.textContent = fmt(points);
                parts.push(fmt(points));
            });

            if (projected === null) {
                calcSum.textContent = "Enter a score from 0 to 100 in each highlighted box to see the total.";
                calcTotal.textContent = "\u2014";
                calcGrade.textContent = "\u2014";
                return;
            }

            calcSum.textContent = parts.join(" + ") + " = " + fmt(projected);
            calcTotal.textContent = fmt(projected) + "%";
            calcGrade.textContent = fmt(equivalentGrade(projected, scale));
        }

        function update() {
            // --- scenario: projected final grade from the sliders ---
            var weights = [];
            var scores = [];
            var allValid = true;

            rows.forEach(function (row) {
                var value = readNumber(row.num);
                mark(row.num, value === null);
                if (value === null) {
                    allValid = false;
                } else {
                    weights.push(row.weight);
                    scores.push(value);
                }
            });

            var projected = null;
            if (allValid) {
                projected = projectFinal(earned, weights, scores);
                finalOut.textContent = fmt(projected) + "%";
                gradeOut.textContent = fmt(equivalentGrade(projected, scale));
                setMessage(scenarioMsg, "", "");
                scenarioMsg.style.display = "none";
            } else {
                finalOut.textContent = "\u2014";
                gradeOut.textContent = "\u2014";
                setMessage(scenarioMsg, "", "Enter a score from 0 to 100 in each highlighted box.");
                scenarioMsg.style.display = "";
            }

            updateCalc(projected);

            // --- target: what the remaining work must average ---
            var target = readNumber(targetNum);
            mark(targetNum, target === null);
            applyBtn.style.display = "none";
            compareMsg.textContent = "";

            chips.forEach(function (chip) {
                var on = target !== null && Math.abs(target - parseFloat(chip.getAttribute("data-min"))) < 1e-9;
                chip.setAttribute("aria-pressed", on ? "true" : "false");
                chip.classList.toggle("gw-chip-on", on);
            });

            if (target === null) {
                setMessage(targetMsg, "", "Enter a target from 0 to 100.");
                return;
            }

            var result = checkTarget(target, earned, remaining);
            var targetGrade = fmt(equivalentGrade(target, scale));
            var head = "Target " + fmt(target) + "% (grade " + targetGrade + "). ";

            if (result.status === "Secured") {
                setMessage(targetMsg, "ok", head + "Already secured: your " + fmt(earned) +
                    " points meet it, even if the rest of your work scored 0.");
            } else if (result.status === "OutOfReach") {
                var best = earned + remaining;
                setMessage(targetMsg, "warn", head + "Out of reach: even 100 on everything left gives at most " +
                    fmt(best) + "% (grade " + fmt(equivalentGrade(best, scale)) + ").");
            } else if (result.status === "Reachable") {
                setMessage(targetMsg, "info", head + "You need an average of " + fmt(result.required) +
                    "% across the remaining " + fmt(remaining) + "% of the course.");
                applyBtn.style.display = "";
                applyBtn.setAttribute("data-required", String(roundUpScore(result.required)));
            }

            if (projected !== null) {
                var gap = projected - target;
                if (gap >= -TARGET_TOLERANCE) {
                    compareMsg.textContent = "Your scenario finishes at " + fmt(projected) + "%, which meets this target.";
                } else {
                    compareMsg.textContent = "Your scenario finishes at " + fmt(projected) + "%, which is " +
                        fmt(-gap) + " points short of this target.";
                }
            }
        }

        // Keep each slider and its number box in step with each other.
        function link(range, num, onChange) {
            range.addEventListener("input", function () {
                num.value = range.value;
                onChange();
            });
            num.addEventListener("input", function () {
                var n = readNumber(num);
                if (n !== null) range.value = String(n);
                onChange();
            });
        }

        rows.forEach(function (row) { link(row.range, row.num, update); });
        link(targetRange, targetNum, update);

        chips.forEach(function (chip) {
            chip.addEventListener("click", function () {
                var min = chip.getAttribute("data-min");
                targetNum.value = min;
                targetRange.value = min;
                update();
            });
        });

        applyBtn.addEventListener("click", function () {
            var required = parseFloat(applyBtn.getAttribute("data-required"));
            if (!isFinite(required)) return;
            required = clampScore(required);
            rows.forEach(function (row) {
                row.num.value = String(required);
                row.range.value = String(required);
            });
            update();
        });

        resetBtn.addEventListener("click", function () {
            rows.forEach(function (row) {
                row.num.value = String(defaultScore);
                row.range.value = String(defaultScore);
            });
            targetNum.value = String(defaultTarget);
            targetRange.value = String(defaultTarget);
            update();
        });

        update();
    }

    if (document.readyState === "loading") {
        document.addEventListener("DOMContentLoaded", init);
    } else {
        init();
    }
})(typeof window !== "undefined" ? window : this);
