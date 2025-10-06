using DashboardReportApp.Models;
using Microsoft.AspNetCore.Authentication.OAuth;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using MySql.Data.MySqlClient;
using System;
using System.Collections.Generic;
using System.IO;

namespace DashboardReportApp.Services
{
    public class AdminDeviationService
    {
        private readonly string _connectionString;
        private readonly string _baseUploadFolder;

        public AdminDeviationService(IConfiguration configuration, IOptionsMonitor<PathOptions> pathOptions, IWebHostEnvironment env)
        {
            _connectionString = configuration.GetConnectionString("MySQLConnection")
                ?? throw new InvalidOperationException("MySQLConnection missing");

            var configured = pathOptions.CurrentValue.DeviationUploads;

            // If relative, anchor to content root
            _baseUploadFolder = Path.IsPathFullyQualified(configured)
                ? configured
                : Path.GetFullPath(Path.Combine(env.ContentRootPath, configured));

            Directory.CreateDirectory(_baseUploadFolder); // idempotent
        }

        public List<AdminDeviationModel> GetAllDeviations()
        {
            var deviations = new List<AdminDeviationModel>();

            using var conn = new MySqlConnection(_connectionString);
            conn.Open();
            using var cmd = new MySqlCommand("SELECT * FROM Deviation ORDER BY id DESC", conn);
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                var fa1 = reader.IsDBNull(reader.GetOrdinal("FileAddress1")) ? string.Empty : reader.GetString("FileAddress1");
                var fa2 = reader.IsDBNull(reader.GetOrdinal("FileAddress2")) ? string.Empty : reader.GetString("FileAddress2");

                // Normalize to just the file name in memory too (handles legacy rows with full paths)
                fa1 = ToFileNameSafe(fa1);
                fa2 = ToFileNameSafe(fa2);

                deviations.Add(new AdminDeviationModel
                {
                    Id = reader.GetInt32("Id"),
                    Timestamp = reader.GetDateTime("Timestamp"),
                    Part = reader.IsDBNull(reader.GetOrdinal("Part")) ? string.Empty : reader.GetString("Part"),
                    SentDateTime = reader.IsDBNull(reader.GetOrdinal("SentDateTime")) ? (DateTime?)null : reader.GetDateTime("SentDateTime"),
                    Discrepancy = reader.IsDBNull(reader.GetOrdinal("Discrepancy")) ? string.Empty : reader.GetString("Discrepancy"),
                    Operator = reader.IsDBNull(reader.GetOrdinal("Operator")) ? string.Empty : reader.GetString("Operator"),
                    CommMethod = reader.IsDBNull(reader.GetOrdinal("CommMethod")) ? string.Empty : reader.GetString("CommMethod"),
                    Disposition = reader.IsDBNull(reader.GetOrdinal("Disposition")) ? string.Empty : reader.GetString("Disposition"),
                    ApprovedBy = reader.IsDBNull(reader.GetOrdinal("ApprovedBy")) ? string.Empty : reader.GetString("ApprovedBy"),
                    DateTimeCASTReview = reader.IsDBNull(reader.GetOrdinal("DateTimeCASTReview")) ? (DateTime?)null : reader.GetDateTime("DateTimeCASTReview"),
                    FileAddress1 = fa1, // store/display just name
                    FileAddress2 = fa2
                });
            }
            return deviations;
        }

        public void UpdateDeviation(AdminDeviationModel deviation, IFormFile? file1, IFormFile? file2)
        {
            // Keep existing filenames if no new upload
            string fileName1 = ToFileNameSafe(deviation.FileAddress1);
            string fileName2 = ToFileNameSafe(deviation.FileAddress2);

            if (file1 != null && file1.Length > 0)
                fileName1 = SaveAndReturnFileName(file1, "DeviationFile1_");

            if (file2 != null && file2.Length > 0)
                fileName2 = SaveAndReturnFileName(file2, "DeviationFile2_");

            using var conn = new MySqlConnection(_connectionString);
            conn.Open();

            using var cmd = new MySqlCommand(@"
                UPDATE deviation 
                   SET Part = @Part,
                       SentDateTime = @SentDateTime, 
                       Discrepancy = @Discrepancy, 
                       Operator = @Operator, 
                       CommMethod = @CommMethod, 
                       Disposition = @Disposition, 
                       ApprovedBy = @ApprovedBy, 
                       DateTimeCASTReview = @DateTimeCASTReview, 
                       FileAddress1 = @FileAddress1,
                       FileAddress2 = @FileAddress2
                 WHERE Id = @Id", conn);

            cmd.Parameters.AddWithValue("@Id", deviation.Id);
            cmd.Parameters.AddWithValue("@Part", deviation.Part ?? "");
            cmd.Parameters.AddWithValue("@SentDateTime", deviation.SentDateTime.HasValue ? (object)deviation.SentDateTime.Value : DBNull.Value);
            cmd.Parameters.AddWithValue("@Discrepancy", deviation.Discrepancy ?? "");
            cmd.Parameters.AddWithValue("@Operator", deviation.Operator ?? "");
            cmd.Parameters.AddWithValue("@CommMethod", deviation.CommMethod ?? "");
            cmd.Parameters.AddWithValue("@Disposition", deviation.Disposition ?? "");
            cmd.Parameters.AddWithValue("@ApprovedBy", deviation.ApprovedBy ?? "");
            cmd.Parameters.AddWithValue("@DateTimeCASTReview", deviation.DateTimeCASTReview.HasValue ? (object)deviation.DateTimeCASTReview.Value : DBNull.Value);
            cmd.Parameters.AddWithValue("@FileAddress1", string.IsNullOrWhiteSpace(fileName1) ? (object)DBNull.Value : fileName1);
            cmd.Parameters.AddWithValue("@FileAddress2", string.IsNullOrWhiteSpace(fileName2) ? (object)DBNull.Value : fileName2);

            cmd.ExecuteNonQuery();
        }

        // === Helpers ===

        private string SaveAndReturnFileName(IFormFile file, string prefix)
        {
            if (file == null || file.Length == 0)
                throw new ArgumentException("File is null or empty.", nameof(file));

            var ext = Path.GetExtension(file.FileName);
            var unique = prefix + DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + "_" + Path.GetRandomFileName().Replace(".", "") + ext;
            var finalPath = Path.Combine(_baseUploadFolder, unique);

            Directory.CreateDirectory(_baseUploadFolder);

            using var stream = new FileStream(finalPath, FileMode.Create, FileAccess.Write, FileShare.None);
            file.CopyTo(stream);

            // Return just the file name saved to disk
            return unique;
        }

        private static string ToFileNameSafe(string? input)
        {
            if (string.IsNullOrWhiteSpace(input)) return string.Empty;
            // If the DB value is already a plain name, this is a no-op.
            // If it's a legacy absolute/UNC path, strip to the name.
            return Path.GetFileName(input);
        }

        // If you need the absolute path for reading/serving a file:
        public string GetAbsolutePath(string? dbValue)
        {
            var name = ToFileNameSafe(dbValue);
            return string.IsNullOrWhiteSpace(name) ? "" : Path.Combine(_baseUploadFolder, name);
        }
    
        public List<string> GetAllOperatorNames()
        {
            var names = new List<string>();
            using (MySqlConnection conn = new MySqlConnection(_connectionString))
            {
                conn.Open();
                string query = "SELECT name FROM operators ORDER BY name";
                using (MySqlCommand cmd = new MySqlCommand(query, conn))
                {
                    using (MySqlDataReader reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            names.Add(reader.IsDBNull(0) ? string.Empty : reader.GetString("name"));
                        }
                    }
                }
            }
            return names;
        }

        public List<string> GetApprovedByOperators()
        {
            var names = new List<string>();
            using (MySqlConnection conn = new MySqlConnection(_connectionString))
            {
                conn.Open();
                string query = "SELECT name FROM operators WHERE allowApprDeviation = 1 ORDER BY name";
                using (MySqlCommand cmd = new MySqlCommand(query, conn))
                {
                    using (MySqlDataReader reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            names.Add(reader.IsDBNull(0) ? string.Empty : reader.GetString("name"));
                        }
                    }
                }
            }
            return names;
        }
    }
}
