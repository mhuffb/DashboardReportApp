using DashboardReportApp.Models;
using MySql.Data.MySqlClient;
using System;
using System.Collections.Generic;
using static Microsoft.EntityFrameworkCore.DbLoggerCategory.Database;

namespace DashboardReportApp.Services
{
    public class AdminProcessChangeRequestService
    {
        private readonly string _connectionString;
        private readonly string _uploadFolder;

        public AdminProcessChangeRequestService(IConfiguration configuration)
        {
            _connectionString = configuration.GetConnectionString("MySQLConnection");
            _uploadFolder = @"\\SINTERGYDC2024\Vol1\Visual Studio Programs\VSP\Uploads";

        }

        public List<ProcessChangeRequestModel> GetAllRequests()
        {
            var requests = new List<ProcessChangeRequestModel>();
            string query = "SELECT * FROM ProcessChangeRequest ORDER BY id DESC";

            using (var connection = new MySqlConnection(_connectionString))
            {
                connection.Open();
                using (var command = new MySqlCommand(query, connection))
                using (var reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        requests.Add(new ProcessChangeRequestModel
                        {
                            Id = Convert.ToInt32(reader["Id"]),
                            Timestamp = Convert.ToDateTime(reader["Timestamp"]),
                            Part = reader["Part"]?.ToString(),
                            Requester = reader["Requester"]?.ToString(),
                            ReqDate = Convert.ToDateTime(reader["ReqDate"]),
                            Request = reader["Request"]?.ToString(),
                            UpdateDateTime = reader["UpdateDateTime"] == DBNull.Value ? null : Convert.ToDateTime(reader["UpdateDateTime"]),
                            UpdatedBy = reader["UpdatedBy"]?.ToString(),
                            UpdateResult = reader["UpdateResult"]?.ToString(),
                            FileAddress2 = reader["FileAddress2"]?.ToString(),
                            FileAddress1 = reader["FileAddress1"]?.ToString(),
                            TestRequested = reader["TestRequested"]?.ToString()
                        });
                    }
                }
            }

            return requests;
        }

        public void AddRequest(ProcessChangeRequestModel request, IFormFile file)
        {
            // Initialize file path
            string fileAddress1 = null;
            string filePath = null;
            // Handle file upload
            if (file != null && file.Length > 0)
            {
                var fileName = Path.GetFileName(file.FileName);
                 filePath = Path.Combine(_uploadFolder, $"ProcessChangeRequestFileAddress1_{request.Id}");

                try
                {
                    // Save the file to the specified folder
                    using (var stream = new FileStream(filePath, FileMode.Create))
                    {
                        file.CopyTo(stream);
                    }

                    // Save relative file path for database
                }
                catch (Exception ex)
                {
                    // Log or handle the file save error
                    Console.WriteLine($"Error saving file: {ex.Message}");
                    throw new Exception("File upload failed.");
                }
            }

            // Insert data into the database
            string insertQuery = @"INSERT INTO ProcessChangeRequest (Part, Requester, ReqDate, Request, FileAddress1, TestRequested) 
                           VALUES (@Part, @Requester, @ReqDate, @Request, @FileAddress1, @TestRequested)";

            using (var connection = new MySqlConnection(_connectionString))
            {
                connection.Open();

                using (var command = new MySqlCommand(insertQuery, connection))
                {
                    // Add parameters for the INSERT query
                    command.Parameters.AddWithValue("@Part", request.Part);
                    command.Parameters.AddWithValue("@Requester", request.Requester);
                    command.Parameters.AddWithValue("@ReqDate", request.ReqDate ?? DateTime.Today);
                    command.Parameters.AddWithValue("@Request", request.Request);
                    command.Parameters.AddWithValue("@FileAddress1", string.IsNullOrEmpty(filePath) ? DBNull.Value : fileAddress1);
                    command.Parameters.AddWithValue("@TestRequested",
    string.IsNullOrWhiteSpace(request.TestRequested) ? DBNull.Value : int.Parse(request.TestRequested));


                    // Execute the query
                    command.ExecuteNonQuery();

                    // Retrieve the ID of the inserted record
                    int newId = (int)command.LastInsertedId;

                    // If a file was uploaded, update the record with the file path
                    if (!string.IsNullOrEmpty(fileAddress1))
                    {
                        string updateQuery = @"UPDATE ProcessChangeRequest SET FileAddress1 = @FileAddress1 WHERE Id = @Id";

                        using (var updateCommand = new MySqlCommand(updateQuery, connection))
                        {
                            updateCommand.Parameters.AddWithValue("@Id", newId);
                            updateCommand.Parameters.AddWithValue("@FileAddress1", fileAddress1);
                            updateCommand.ExecuteNonQuery();
                        }
                    }
                }
            }
        }

        public void UpdateRequest(ProcessChangeRequestModel model, IFormFile? file1, IFormFile? file2)
        {
            // Use existing values if no new file is uploaded
            string filePath1 = model.FileAddress1;
            string filePath2 = model.FileAddress2;

            // Process first file upload if provided
            if (file1 != null && file1.Length > 0)
            {
                if (!Directory.Exists(_uploadFolder))
                {
                    Directory.CreateDirectory(_uploadFolder);
                }
                var fileExtension1 = Path.GetExtension(file1.FileName);
                var fileName1 = $"ProcessChangeRequest_File1_{model.Id}{fileExtension1}";
                filePath1 = Path.Combine(_uploadFolder, fileName1);

                try
                {
                    using (var stream = new FileStream(filePath1, FileMode.Create))
                    {
                        file1.CopyTo(stream);
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error saving file1: {ex.Message}");
                    throw new Exception("File 1 upload failed.");
                }
            }

            // Process second file upload if provided
            if (file2 != null && file2.Length > 0)
            {
                if (!Directory.Exists(_uploadFolder))
                {
                    Directory.CreateDirectory(_uploadFolder);
                }
                var fileExtension2 = Path.GetExtension(file2.FileName);
                var fileName2 = $"ProcessChangeRequest_File2_{model.Id}{fileExtension2}";
                filePath2 = Path.Combine(_uploadFolder, fileName2);

                try
                {
                    using (var stream = new FileStream(filePath2, FileMode.Create))
                    {
                        file2.CopyTo(stream);
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error saving file2: {ex.Message}");
                    throw new Exception("File 2 upload failed.");
                }
            }

            // Update the database with the new file paths for both fields
            string query = @"UPDATE ProcessChangeRequest 
                     SET Part = @Part, Requester = @Requester, ReqDate = @ReqDate, 
                         Request = @Request, UpdatedBy = @UpdatedBy, 
                         UpdateResult = @UpdateResult, 
                         FileAddress1 = @FileAddress1, 
                         FileAddress2 = @FileAddress2,
                         TestRequested = @TestRequested
                     WHERE Id = @Id";

            using (var connection = new MySqlConnection(_connectionString))
            {
                connection.Open();
                using (var command = new MySqlCommand(query, connection))
                {
                    command.Parameters.AddWithValue("@Id", model.Id);
                    command.Parameters.AddWithValue("@Part", model.Part);
                    command.Parameters.AddWithValue("@Requester", model.Requester);
                    command.Parameters.AddWithValue("@ReqDate", model.ReqDate);
                    command.Parameters.AddWithValue("@Request", model.Request);
                    command.Parameters.AddWithValue("@UpdatedBy", model.UpdatedBy);
                    command.Parameters.AddWithValue("@UpdateResult", model.UpdateResult);
                    command.Parameters.AddWithValue("@FileAddress1", string.IsNullOrEmpty(filePath1) ? DBNull.Value : filePath1);
                    command.Parameters.AddWithValue("@FileAddress2", string.IsNullOrEmpty(filePath2) ? DBNull.Value : filePath2);
                    command.Parameters.AddWithValue("@TestRequested",
                        string.IsNullOrWhiteSpace(model.TestRequested) ? DBNull.Value : int.Parse(model.TestRequested));

                    command.ExecuteNonQuery();
                }
            }

            Console.WriteLine("Database updated with FileAddress1: " + filePath1 + " and FileAddress2: " + filePath2);
        }




        public void UpdateFileAddress(int id, string fileAddress1)
        {
            
            Console.WriteLine($"Updating Request ID = {id}, FileAddress1 = {fileAddress1}");

            string query = @"UPDATE ProcessChangeRequest SET FileAddress1 = @FileAddress1 WHERE Id = @Id";

            using (var connection = new MySqlConnection(_connectionString))
            {
                connection.Open();
                using (var command = new MySqlCommand(query, connection))
                {
                    command.Parameters.AddWithValue("@Id", id);
                    command.Parameters.AddWithValue("@FileAddress1", fileAddress1);

                    var rowsAffected = command.ExecuteNonQuery();
                    Console.WriteLine($"Rows Affected: {rowsAffected}");
                }
            }
        }

        public void UpdateFileAddress2(int id, string fileAddress2)
        {

            Console.WriteLine($"Updating Request ID = {id}, FileAddress1 = {fileAddress2}");

            string query = @"UPDATE ProcessChangeRequest SET FileAddress2 = @FileAddress2 WHERE Id = @Id";

            using (var connection = new MySqlConnection(_connectionString))
            {
                connection.Open();
                using (var command = new MySqlCommand(query, connection))
                {
                    command.Parameters.AddWithValue("@Id", id);
                    command.Parameters.AddWithValue("@FileAddress2", fileAddress2);

                    var rowsAffected = command.ExecuteNonQuery();
                    Console.WriteLine($"Rows Affected: {rowsAffected}");
                }
            }
        }

    }
}
