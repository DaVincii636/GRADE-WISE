using System;
using System.Configuration;
using MongoDB.Bson;
using MongoDB.Driver;
using GradeWise.Models;

namespace GradeWise.Data
{
    public class MongoDbContext
    {
        private static readonly Lazy<IMongoDatabase> SharedDatabase =
            new Lazy<IMongoDatabase>(() =>
            {
                var settings = MongoClientSettings.FromConnectionString(
                    ConfigurationManager.ConnectionStrings["MongoConnection"].ConnectionString);

                settings.MinConnectionPoolSize = 3;
                settings.MaxConnectionIdleTime = TimeSpan.FromMinutes(30);
                settings.ServerSelectionTimeout = TimeSpan.FromSeconds(10);

                return new MongoClient(settings).GetDatabase("GradeWiseDb");
            }, System.Threading.LazyThreadSafetyMode.PublicationOnly);

        private readonly IMongoDatabase _database;

        public MongoDbContext()
        {
            _database = SharedDatabase.Value;
        }

        // Existing collections
        public IMongoCollection<User> Users => _database.GetCollection<User>("Users");
        public IMongoCollection<Course> Courses => _database.GetCollection<Course>("Courses");
        public IMongoCollection<GradeRecord> Grades => _database.GetCollection<GradeRecord>("Grades");
        public IMongoCollection<AcademicTerm> Terms => _database.GetCollection<AcademicTerm>("Terms");
        public IMongoCollection<ScoreSnapshot> Snapshots => _database.GetCollection<ScoreSnapshot>("Snapshots");
        public IMongoCollection<Notification> Notifications => _database.GetCollection<Notification>("Notifications");

        // Migration method
        public void AddReadableIdFields()
        {
            AddReadableIdField("Courses", "course_id");
            AddReadableIdField("Users", "user_id");
            AddReadableIdField("Grades", "grade_id");
            AddReadableIdField("Terms", "term_id");
            AddReadableIdField("Snapshots", "snapshot_id");
            AddReadableIdField("Notifications", "notification_id");
        }

        private void AddReadableIdField(string collectionName, string fieldName)
        {
            var collection = _database.GetCollection<BsonDocument>(collectionName);

            var documents = collection
                .Find(FilterDefinition<BsonDocument>.Empty)
                .ToList();

            foreach (var document in documents)
            {
                var id = document["_id"];

                string readableId = id.IsString
                    ? id.AsString
                    : id.ToString();

                var filter = Builders<BsonDocument>.Filter.Eq("_id", id);

                var update = Builders<BsonDocument>.Update
                    .Set(fieldName, readableId);

                collection.UpdateOne(filter, update);
            }
        }
    }
}
