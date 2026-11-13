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
        {
            var settings = MongoClientSettings.FromConnectionString(
                ConfigurationManager.ConnectionStrings["MongoConnection"].ConnectionString);

            // Keep a few connections open, so a click after a quiet moment does not wait for a new
            // secure connection to Atlas to be set up.
            settings.MinConnectionPoolSize = 3;
            settings.MaxConnectionIdleTime = TimeSpan.FromMinutes(30);

            // If Atlas cannot be reached, say so after 10 seconds instead of 30.
            settings.ServerSelectionTimeout = TimeSpan.FromSeconds(10);

            return new MongoClient(settings).GetDatabase("GradeWiseDb");
        }, System.Threading.LazyThreadSafetyMode.PublicationOnly);   // a failed start is retried, not remembered

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