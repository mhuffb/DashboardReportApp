using DashboardReportApp.Models;
using MySql.Data.MySqlClient;
using System;
using System.Collections.Generic;
using static Microsoft.EntityFrameworkCore.DbLoggerCategory.Database;

namespace DashboardReportApp.Services
{
    public class ProcessChangeRequestService
    {
        private readonly string _connectionString;
        private readonly string _uploadFolder;

        public ProcessChangeRequestService(IConfiguration configuration)
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
                            FileAddress = reader["FileAddress"]?.ToString(),
                            FileAddressMediaLink = reader["FileAddressMediaLink"]?.ToString(),
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
            string fileAddressMediaLink = null;
            string filePath = null;
            // Handle file upload
            if (file != null && file.Length > 0)
            {
                var fileName = Path.GetFileName(file.FileName);
                 filePath = Path.Combine(_uploadFolder, $"ProcessChangeRequestMedia_{request.Id}");

                try
                {
                    // Save the file to the specified folder
                    using (var stream = new FileStream(filePath, FileMode.Create))
                    {
                        file.CopyTo(stream);
                    }

                    // Save relative file path for database
                    //fileAddressMediaLink = $"/uploads/ProcessChangeRequestMedia_{fileName}";
                }
                catch (Exception ex)
                {
                    // Log or handle the file save error
                    Console.WriteLine($"Error saving file: {ex.Message}");
                    throw new Exception("File upload failed.");
                }
            }

            // Insert data into the database
            string insertQuery = @"INSERT INTO ProcessChangeRequest (Part, Requester, ReqDate, Request, FileAddressMediaLink, TestRequested) 
                           VALUES (@Part, @Requester, @ReqDate, @Request, @FileAddressMediaLink, @TestRequested)";

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
                    command.Parameters.AddWithValue("@FileAddressMediaLink", string.IsNullOrEmpty(filePath) ? DBNull.Value : fileAddressMediaLink);
                    command.Parameters.AddWithValue("@TestRequested",
    string.IsNullOrWhiteSpace(request.TestRequested) ? DBNull.Value : int.Parse(request.TestRequested));


                    // Execute the query
                    command.ExecuteNonQuery();

                    // Retrieve the ID of the inserted record
                    int newId = (int)command.LastInsertedId;

                    // If a file was uploaded, update the record with the file path
                    if (!string.IsNullOrEmpty(fileAddressMediaLink))
                    {
                        string updateQuery = @"UPDATE ProcessChangeRequest SET FileAddressMediaLink = @FileAddressMediaLink WHERE Id = @Id";

                        using (var updateCommand = new MySqlCommand(updateQuery, connection))
                        {
                            updateCommand.Parameters.AddWithValue("@Id", newId);
                            updateCommand.Parameters.AddWithValue("@FileAddressMediaLink", fileAddressMediaLink);
                            updateCommand.ExecuteNonQuery();
                        }
                    }
                }
            }
        }

        public void UpdateRequest(ProcessChangeRequestModel model, IFormFile? file)
        {
            string filePath = model.FileAddress; // Use the existing address if no new file is uploaded
            Console.WriteLine("Previous File Address: " + model.FileAddress);

            if (file != null && file.Length > 0)
            {
                if (!Directory.Exists(_uploadFolder))
                {
                    Directory.CreateDirectory(_uploadFolder); // Ensure the folder exists
                }

                // Generate the file name with extension
                var fileExtension = Path.GetExtension(file.FileName);
                var fileName = $"ProcessChangeRequest_{model.Id}{fileExtension}";

                // Full physical path for saving the file
                filePath = Path.Combine(_uploadFolder, fileName);


                Console.WriteLine("Physical Path (for saving): " + filePath);

                try
                {
                    // Save the file to the physical location
                    using (var stream = new FileStream(filePath, FileMode.Create))
                    {
                        file.CopyTo(stream);
                    }

                    Console.WriteLine("File saved successfully at: " + filePath);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error saving file: {ex.Message}");
                    throw new Exception("File upload failed.");
                }
            }
            else
            {
                Console.WriteLine("No new file uploaded.");
            }

            // Update the database with the relative file path
            string query = @"UPDATE ProcessChangeRequest 
                     SET Part = @Part, Requester = @Requester, ReqDate = @ReqDate, 
                         Request = @Request, UpdatedBy = @UpdatedBy, 
                         UpdateResult = @UpdateResult, FileAddress = @FileAddress, 
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

                    // Save the relative path in the database
                    command.Parameters.AddWithValue("@FileAddress", string.IsNullOrEmpty(filePath) ? DBNull.Value : filePath);

                    command.Parameters.AddWithValue("@TestRequested",
                        string.IsNullOrWhiteSpace(model.TestRequested) ? DBNull.Value : int.Parse(model.TestRequested));

                    command.ExecuteNonQuery();
                }
            }

            Console.WriteLine("Database updated with relative path: " + filePath);
        }



        public void UpdateMediaLinkFile(int id, string fileAddressMediaLink)
        {
            
            Console.WriteLine($"Updating Request ID = {id}, FileAddressMediaLink = {fileAddressMediaLink}");

            string query = @"UPDATE ProcessChangeRequest SET FileAddressMediaLink = @FileAddressMediaLink WHERE Id = @Id";

            using (var connection = new MySqlConnection(_connectionString))
            {
                connection.Open();
                using (var command = new MySqlCommand(query, connection))
                {
                    command.Parameters.AddWithValue("@Id", id);
                    command.Parameters.AddWithValue("@FileAddressMediaLink", fileAddressMediaLink);

                    var rowsAffected = command.ExecuteNonQuery();
                    Console.WriteLine($"Rows Affected: {rowsAffected}");
                }
            }
        }



    }
}
