using DashboardReportApp.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using MySql.Data.MySqlClient;
using System;
using System.Collections.Generic;
using System.IO;

namespace DashboardReportApp.Services
{
    public class ProcessChangeRequestService
    {
        private readonly string _connectionString;
        private readonly string _baseFolder;

        public ProcessChangeRequestService(
        IConfiguration configuration,
        IOptionsMonitor<PathOptions> opts,
        IWebHostEnvironment env)
        {
            _connectionString = configuration.GetConnectionString("MySQLConnection")
             ?? throw new InvalidOperationException("MySQLConnection missing");

            var p = opts.CurrentValue;
            var configured = !string.IsNullOrWhiteSpace(p.ProcessChangeUploads)
                ? p.ProcessChangeUploads!
                : p.DeviationUploads;

            _baseFolder = Path.IsPathFullyQualified(configured)
                ? configured
                : Path.GetFullPath(Path.Combine(env.ContentRootPath, configured));

            Directory.CreateDirectory(_baseFolder);
        }

        // Gets all ProcessChangeRequest rows
        public List<ProcessChangeRequestModel> GetAllRequests()
        {
            var requests = new List<ProcessChangeRequestModel>();
            string query = "SELECT * FROM ProcessChangeRequest ORDER BY Id DESC";

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
                            // The DB columns might differ. 
                            // Use the correct column names for file addresses if not "FileAddress1" or "FileAddress"
                            FileAddress1 = reader["FileAddress1"]?.ToString(),
                            FileAddress2 = reader["FileAddress2"]?.ToString(),
                            TestRequested = reader["TestRequested"]?.ToString()
                        });
                    }
                }
            }
            return requests;
        }
        public List<string> GetOperators()
        {
            var operatorNames = new List<string>();
            string query = "SELECT DISTINCT name FROM operators ORDER BY name";

            using (var connection = new MySqlConnection(_connectionString))
            {
                connection.Open();
                using (var command = new MySqlCommand(query, connection))
                using (var reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        // Assuming column 0 contains the 'name' string
                        operatorNames.Add(reader.GetString(0));
                    }
                }
            }

            return operatorNames;
        }

        // Add a new request + optional single file
        public int AddRequest(ProcessChangeRequestModel request)
        {
            int newId;
            const string insertQuery = @"
                INSERT INTO ProcessChangeRequest 
                (Part, Requester, ReqDate, Request, FileAddress1, TestRequested)
                VALUES (@Part, @Requester, @ReqDate, @Request, @FileAddress1, @TestRequested)";

            using (var connection = new MySqlConnection(_connectionString))
            {
                connection.Open();
                using (var command = new MySqlCommand(insertQuery, connection))
                {
                    command.Parameters.AddWithValue("@Part", request.Part);
                    command.Parameters.AddWithValue("@Requester", request.Requester);
                    command.Parameters.AddWithValue("@ReqDate", request.ReqDate ?? DateTime.Today);
                    command.Parameters.AddWithValue("@Request", request.Request);

                    // We haven't saved any file yet, so set FileAddress1 to NULL
                    command.Parameters.AddWithValue("@FileAddress1", DBNull.Value);

                    // Convert "1"/"0" or null string to int or DB null
                    command.Parameters.AddWithValue("@TestRequested",
                        string.IsNullOrWhiteSpace(request.TestRequested)
                            ? (object)DBNull.Value
                            : int.Parse(request.TestRequested));

                    command.ExecuteNonQuery();
                    newId = (int)command.LastInsertedId;
                }
            }

            Console.WriteLine($"[AddRequest] Inserted new ProcessChangeRequest with Id = {newId}");
            return newId;
        }

        // -------------------------------------------------
        // (B) UpdateFileAddress1: Upload the file + Update DB
        //     If you want to reuse this for "AddRequest",
        //     just call it once you have the new Id.
        // -------------------------------------------------
        // Resolve filename -> absolute path
        public string GetAbsolutePath(string? stored)
        {
            var name = string.IsNullOrWhiteSpace(stored) ? "" : Path.GetFileName(stored);
            return string.IsNullOrWhiteSpace(name) ? "" : Path.Combine(_baseFolder, name);
        }

        public string UpdateFileAddress1(int id, IFormFile file)
        {
            if (file == null || file.Length == 0)
                throw new ArgumentException("File is missing or empty.", nameof(file));

            Directory.CreateDirectory(_baseFolder);
            var ext = Path.GetExtension(file.FileName);
            var fileName = $"ProcessChangeRequestFile1_{id}{ext}";
            var path = Path.Combine(_baseFolder, fileName);

            using (var fs = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None))
                file.CopyTo(fs);

            const string sql = "UPDATE ProcessChangeRequest SET FileAddress1=@f WHERE Id=@id;";
            using var conn = new MySqlConnection(_connectionString);
            conn.Open();
            using var cmd = new MySqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@f", fileName); // store filename only
            cmd.Parameters.AddWithValue("@id", id);
            cmd.ExecuteNonQuery();

            return fileName;
        }
    }
}
