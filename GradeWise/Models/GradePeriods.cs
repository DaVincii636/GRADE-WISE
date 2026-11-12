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
}
