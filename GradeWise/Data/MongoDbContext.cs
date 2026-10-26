using System;
using System.Configuration;
using GradeWise.Models;
using MongoDB.Driver;

namespace GradeWise.Data
{
    /// <summary>
    /// Single entry point to MongoDB. Reads its settings from appSettings
    /// (which come from Secrets.config) and exposes the collections.
    /// MongoClient is thread-safe and meant to be shared, so it is created once.
    /// </summary>
    public class MongoDbContext
    {
        private static readonly Lazy<MongoDbContext> _instance =
            new Lazy<MongoDbContext>(() => new MongoDbContext());

        public static MongoDbContext Instance => _instance.Value;

        private readonly IMongoDatabase _database;

        private MongoDbContext()
        {
            var connectionString = ConfigurationManager.AppSettings["MongoConnectionString"];
            var databaseName = ConfigurationManager.AppSettings["MongoDatabaseName"];

            if (string.IsNullOrWhiteSpace(connectionString) || string.IsNullOrWhiteSpace(databaseName))
            {
                throw new ConfigurationErrorsException(
                    "MongoConnectionString / MongoDatabaseName not found. " +
                    "Copy Secrets.config.example to Secrets.config and fill it in.");
            }

            var settings = MongoClientSettings.FromConnectionString(connectionString);
            // Fail fast (5s) instead of the 30s default when MongoDB isn't running.
            settings.ServerSelectionTimeout = TimeSpan.FromSeconds(5);

            _database = new MongoClient(settings).GetDatabase(databaseName);
        }

        public IMongoCollection<User> Users => _database.GetCollection<User>("users");

        public IMongoCollection<Course> Courses => _database.GetCollection<Course>("courses");

        public IMongoCollection<GradeRecord> Grades => _database.GetCollection<GradeRecord>("grades");
    }
}
