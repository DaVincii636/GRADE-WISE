using System;
using System.Configuration;
using MongoDB.Driver;
using GradeWise.Models;

namespace GradeWise.Data
{
    public class MongoDbContext
    {
        // One MongoClient (and one connection pool) for the whole app. The driver is built to be shared
        // and is thread-safe, so every "new MongoDbContext()" just reuses it. Do not dispose it.
        private static readonly Lazy<IMongoDatabase> SharedDatabase = new Lazy<IMongoDatabase>(() =>
            new MongoClient(ConfigurationManager.ConnectionStrings["MongoConnection"].ConnectionString)
                .GetDatabase("GradeWiseDb"));

        private readonly IMongoDatabase _database;

        public MongoDbContext()
        {
            _database = SharedDatabase.Value;
        }

        // Exposing your collections so your controllers/services can query them
        public IMongoCollection<User> Users => _database.GetCollection<User>("Users");
        public IMongoCollection<Course> Courses => _database.GetCollection<Course>("Courses");
        public IMongoCollection<GradeRecord> Grades => _database.GetCollection<GradeRecord>("Grades");
        public IMongoCollection<AcademicTerm> Terms => _database.GetCollection<AcademicTerm>("Terms");
        public IMongoCollection<ScoreSnapshot> Snapshots => _database.GetCollection<ScoreSnapshot>("Snapshots");
        public IMongoCollection<Notification> Notifications => _database.GetCollection<Notification>("Notifications");
    }
}