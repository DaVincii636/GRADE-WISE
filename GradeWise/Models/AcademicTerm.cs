using System;
using MongoDB.Bson.Serialization.Attributes;

namespace GradeWise.Models
{
    /// <summary>An academic term, e.g. "1st Semester, S.Y. 2026-2027". Exactly one is current.</summary>
    public class AcademicTerm
    {
        public string Id { get; set; }

        public string Name { get; set; }

        public bool IsCurrent { get; set; }

        /// <summary>
        /// First day of the term (a calendar date; stored as a date only, so it never shifts with time zones).
        /// Null for terms saved before dates were stored; those documents simply have no StartDate field.
        /// </summary>
        [BsonDateTimeOptions(DateOnly = true)]
        [BsonIgnoreIfNull]
        public DateTime? StartDate { get; set; }

        /// <summary>Last day of the term (date only). Null when not set, like StartDate.</summary>
        [BsonDateTimeOptions(DateOnly = true)]
        [BsonIgnoreIfNull]
        public DateTime? EndDate { get; set; }
    }
}
