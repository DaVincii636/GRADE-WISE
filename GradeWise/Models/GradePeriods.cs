namespace GradeWise.Models
{
    /// <summary>
    /// The two grading periods of a course. Every category belongs to one of them.
    /// Each period's weights add up to 100, and each period counts for <see cref="Share"/> of the final grade.
    /// </summary>
    public static class GradePeriods
    {
        public const string Midterm = "Midterm";
        public const string FinalTerm = "Final Term";

        public static readonly string[] All = { Midterm, FinalTerm };

        /// <summary>Each period is half of the final grade. Change it here, in one place.</summary>
        public const double Share = 0.5;

        /// <summary>Anything that is not "Final Term" (including a missing value from an old document) is Midterm.</summary>
        public static string Normalize(string period)
        {
            return period == FinalTerm ? FinalTerm : Midterm;
        }
    }

    /// <summary>
    /// The "grade scope" of a course: which numbers students and faculty see.
    /// The keys match the tab keys of the faculty Course page. Anything else means Midterm.
    /// </summary>
    public static class GradeScopes
    {
        public const string Midterm = "midterm";
        public const string FinalTerm = "final-term";
        public const string FinalGrade = "final-grade";

        public static readonly string[] All = { Midterm, FinalTerm, FinalGrade };

        /// <summary>The key when it is valid, otherwise Midterm (so old and bad values are safe).</summary>
        public static string Normalize(string scope)
        {
            return scope == FinalTerm || scope == FinalGrade ? scope : Midterm;
        }

        public static string Label(string scope)
        {
            switch (Normalize(scope))
            {
                case FinalTerm: return "Final Term";
                case FinalGrade: return "Final Grade";
                default: return "Midterm";
            }
        }

        /// <summary>The grading period behind a scope, or null for Final Grade (both periods).</summary>
        public static string PeriodOf(string scope)
        {
            switch (Normalize(scope))
            {
                case FinalTerm: return GradePeriods.FinalTerm;
                case FinalGrade: return null;
                default: return GradePeriods.Midterm;
            }
        }

        /// <summary>True when the category counts in this scope (every category counts in Final Grade).</summary>
        public static bool Includes(string scope, GradeCategory category)
        {
            var period = PeriodOf(scope);
            return period == null || GradePeriods.Normalize(category.Period) == period;
        }
    }
}
