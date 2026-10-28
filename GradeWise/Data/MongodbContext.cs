using System.Configuration;
using MongoDB.Driver;
using GradeWise.Models;

namespace GradeWise.Data
{
    public class MongoDbContext
    {
        private readonly IMongoDatabase _database;

        public MongoDbContext()
        {
            // Pulls the connection string we just added to Web.config
            var connectionString = ConfigurationManager.ConnectionStrings["MongoConnection"].ConnectionString;

            // Connects to MongoDB Atlas
            var client = new MongoClient(connectionString);

            // Names your database on Atlas (you can change "GradeWiseDb" to whatever you prefer)
            _database = client.GetDatabase("GradeWiseDb");
        }

        // Exposing your collections so your controllers/services can query them
        public IMongoCollection<User> Users => _database.GetCollection<User>("Users");
        public IMongoCollection<Course> Courses => _database.GetCollection<Course>("Courses");
        public IMongoCollection<GradeRecord> Grades => _database.GetCollection<GradeRecord>("Grades");
        public IMongoCollection<AcademicTerm> Terms => _database.GetCollection<AcademicTerm>("Terms");
        public IMongoCollection<ScoreSnapshot> Snapshots => _database.GetCollection<ScoreSnapshot>("Snapshots");
    }
}