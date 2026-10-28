using System.Linq;
using GradeWise.Models;
using MongoDB.Driver;

namespace GradeWise.Data
{
    public static class MongoDbSeeder
    {
        public static void Initialize()
        {
            var db = new MongoDbContext();

            // Check if users already exist so we don't duplicate them every time the app starts
            var userCount = db.Users.CountDocuments(Builders<User>.Filter.Empty);
            if (userCount == 0)
            {
                // Seed Users from SampleData
                db.Users.InsertMany(SampleData.Users);
            }

            var courseCount = db.Courses.CountDocuments(Builders<Course>.Filter.Empty);
            if (courseCount == 0)
            {
                // Seed Courses from SampleData
                db.Courses.InsertMany(SampleData.Courses);
            }

            var gradeCount = db.Grades.CountDocuments(Builders<GradeRecord>.Filter.Empty);
            if (gradeCount == 0)
            {
                // Seed Grades from SampleData
                db.Grades.InsertMany(SampleData.Grades);
            }

            var termCount = db.Terms.CountDocuments(Builders<AcademicTerm>.Filter.Empty);
            if (termCount == 0)
            {
                // Seed Terms from SampleData
                db.Terms.InsertMany(SampleData.Terms);
            }

            var snapshotCount = db.Snapshots.CountDocuments(Builders<ScoreSnapshot>.Filter.Empty);
            if (snapshotCount == 0)
            {
                // Seed Snapshots from SampleData
                db.Snapshots.InsertMany(SampleData.Snapshots);
            }
        }
    }
}