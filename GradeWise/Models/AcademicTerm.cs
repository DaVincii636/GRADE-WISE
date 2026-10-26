namespace GradeWise.Models
{
    /// <summary>An academic term, e.g. "1st Semester, S.Y. 2026-2027". Exactly one is current.</summary>
    public class AcademicTerm
    {
        public string Id { get; set; }

        public string Name { get; set; }

        public bool IsCurrent { get; set; }
    }
}
