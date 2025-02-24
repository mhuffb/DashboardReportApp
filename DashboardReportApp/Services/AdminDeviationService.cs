using DashboardReportApp.Models;
using MySql.Data.MySqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.AspNetCore.Http;
using System;
using System.Collections.Generic;
using System.IO;

namespace DashboardReportApp.Services
{
    public class AdminDeviationService
    {
        private readonly string _connectionString;
        private readonly string _uploadFolder;

        public AdminDeviationService(IConfiguration configuration)
        {
            _connectionString = configuration.GetConnectionString("MySQLConnection");
            // Files are stored under wwwroot/DeviationUploads
            _uploadFolder = @"\\SINTERGYDC2024\Vol1\VSP\Uploads";
            if (!Directory.Exists(_uploadFolder))
            {
                Directory.CreateDirectory(_uploadFolder);
            }
        }

        public List<AdminDeviationModel> GetAllDeviations()
        {
            var deviations = new List<AdminDeviationModel>();

            using (MySqlConnection conn = new MySqlConnection(_connectionString))
            {
                conn.Open();
                using (MySqlCommand cmd = new MySqlCommand("SELECT * FROM Deviation ORDER BY id DESC", conn))
                {
                    using (MySqlDataReader reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
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
                                FileAddress1 = reader.IsDBNull(reader.GetOrdinal("FileAddress1")) ? string.Empty : reader.GetString("FileAddress1"),
                                FileAddress2 = reader.IsDBNull(reader.GetOrdinal("FileAddress2")) ? string.Empty : reader.GetString("FileAddress2")
                            });
                        }
                    }
                }
            }
            return deviations;
        }

        public void UpdateDeviation(AdminDeviationModel deviation, IFormFile? file1, IFormFile? file2)
        {
            // Use existing file paths if no new file is provided
            string filePath1 = deviation.FileAddress1;
            string filePath2 = deviation.FileAddress2;

            if (file1 != null && file1.Length > 0)
            {
                filePath1 = SaveFile(file1);
            }
            if (file2 != null && file2.Length > 0)
            {
                filePath2 = SaveFile2(file2);
            }

            using (MySqlConnection conn = new MySqlConnection(_connectionString))
            {
                conn.Open();
                using (MySqlCommand cmd = new MySqlCommand(@"
                    UPDATE deviation 
                    SET  Part = @Part,
                         SentDateTime = @SentDateTime, 
                         Discrepancy = @Discrepancy, 
                         Operator = @Operator, 
                         CommMethod = @CommMethod, 
                         Disposition = @Disposition, 
                         ApprovedBy = @ApprovedBy, 
                         DateTimeCASTReview = @DateTimeCASTReview, 
                         FileAddress1 = @FileAddress1,
                         FileAddress2 = @FileAddress2
                    WHERE Id = @Id", conn))
                {
                    cmd.Parameters.AddWithValue("@Id", deviation.Id);
                    cmd.Parameters.AddWithValue("@Part", deviation.Part);
                    cmd.Parameters.AddWithValue("@SentDateTime", deviation.SentDateTime.HasValue ? (object)deviation.SentDateTime.Value : DBNull.Value);
                    cmd.Parameters.AddWithValue("@Discrepancy", deviation.Discrepancy);
                    cmd.Parameters.AddWithValue("@Operator", deviation.Operator);
                    cmd.Parameters.AddWithValue("@CommMethod", deviation.CommMethod);
                    cmd.Parameters.AddWithValue("@Disposition", deviation.Disposition);
                    cmd.Parameters.AddWithValue("@ApprovedBy", deviation.ApprovedBy);
                    cmd.Parameters.AddWithValue("@DateTimeCASTReview", deviation.DateTimeCASTReview.HasValue ? (object)deviation.DateTimeCASTReview.Value : DBNull.Value);
                    cmd.Parameters.AddWithValue("@FileAddress1", string.IsNullOrEmpty(filePath1) ? (object)DBNull.Value : filePath1);
                    cmd.Parameters.AddWithValue("@FileAddress2", string.IsNullOrEmpty(filePath2) ? (object)DBNull.Value : filePath2);
                    cmd.ExecuteNonQuery();
                }
            }
        }

        // Saves the uploaded file and returns a relative URL (like "/DeviationUploads/DeviationFile_12345.jpg")
        public string SaveFile(IFormFile file)
        {
            if (file == null || file.Length == 0)
            {
                throw new ArgumentException("File is null or empty.", nameof(file));
            }

            // Ensure the upload folder exists
            if (!Directory.Exists(_uploadFolder))
            {
                Directory.CreateDirectory(_uploadFolder);
            }

            // Create a unique filename: "HoldTagFile_637622183523457159.pdf", etc.
            var extension = Path.GetExtension(file.FileName);
            var uniqueName = "DeviationFile1_" + DateTime.Now.Ticks + extension;
            var finalPath = Path.Combine(_uploadFolder, uniqueName);

            // Copy the file to disk
            using (var stream = new FileStream(finalPath, FileMode.Create))
            {
                file.CopyTo(stream);
            }

            // Return the path so we can save it in record.FileAddress1
            return finalPath;
        }
        public string SaveFile2(IFormFile file)
        {
            if (file == null || file.Length == 0)
            {
                throw new ArgumentException("File is null or empty.", nameof(file));
            }

            // Ensure the upload folder exists
            if (!Directory.Exists(_uploadFolder))
            {
                Directory.CreateDirectory(_uploadFolder);
            }

            // Create a unique filename: "HoldTagFile_637622183523457159.pdf", etc.
            var extension = Path.GetExtension(file.FileName);
            var uniqueName = "DeviationFile2_" + DateTime.Now.Ticks + extension;
            var finalPath = Path.Combine(_uploadFolder, uniqueName);

            // Copy the file to disk
            using (var stream = new FileStream(finalPath, FileMode.Create))
            {
                file.CopyTo(stream);
            }

            // Return the path so we can save it in record.FileAddress1
            return finalPath;
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
