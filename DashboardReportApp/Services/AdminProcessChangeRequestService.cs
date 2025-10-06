using DashboardReportApp.Models;
using Microsoft.Extensions.Options;
using MySql.Data.MySqlClient;
using System;
using System.Collections.Generic;
using System.IO;
using static Microsoft.EntityFrameworkCore.DbLoggerCategory.Database;
namespace DashboardReportApp.Services
{
    public class AdminProcessChangeRequestService
    {
        private readonly string _connectionString;
        private readonly string _baseFolder;

        public AdminProcessChangeRequestService(IConfiguration configuration, IOptionsMonitor<PathOptions> opts, IWebHostEnvironment env)
        {
            _connectionString = configuration.GetConnectionString("MySQLConnection")
                ?? throw new InvalidOperationException("MySQLConnection missing");

            // Prefer ProcessChangeUploads if set, else fall back to DeviationUploads
            var p = opts.CurrentValue;
            var configured = !string.IsNullOrWhiteSpace(p.ProcessChangeUploads)
                ? p.ProcessChangeUploads!
                : p.DeviationUploads;

            _baseFolder = Path.IsPathFullyQualified(configured)
                ? configured
                : Path.GetFullPath(Path.Combine(env.ContentRootPath, configured));

            Directory.CreateDirectory(_baseFolder);
        }

        public List<ProcessChangeRequestModel> GetAllRequests()
        {
            var requests = new List<ProcessChangeRequestModel>();
            const string query = "SELECT * FROM ProcessChangeRequest ORDER BY id DESC";

            using var connection = new MySqlConnection(_connectionString);
            connection.Open();
            using var command = new MySqlCommand(query, connection);
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                // normalize any legacy full paths -> filenames
                string fn1 = reader["FileAddress1"] == DBNull.Value ? "" : Path.GetFileName(reader["FileAddress1"].ToString()!);
                string fn2 = reader["FileAddress2"] == DBNull.Value ? "" : Path.GetFileName(reader["FileAddress2"].ToString()!);

                requests.Add(new ProcessChangeRequestModel
                {
                    Id = Convert.ToInt32(reader["Id"]),
                    Timestamp = Convert.ToDateTime(reader["Timestamp"]),
                    Part = reader["Part"]?.ToString(),
                    Requester = reader["Requester"]?.ToString(),
                    ReqDate = reader["ReqDate"] == DBNull.Value ? null : Convert.ToDateTime(reader["ReqDate"]),
                    Request = reader["Request"]?.ToString(),
                    UpdateDateTime = reader["UpdateDateTime"] == DBNull.Value ? null : Convert.ToDateTime(reader["UpdateDateTime"]),
                    UpdatedBy = reader["UpdatedBy"]?.ToString(),
                    UpdateResult = reader["UpdateResult"]?.ToString(),
                    FileAddress1 = string.IsNullOrEmpty(fn1) ? null : fn1,
                    FileAddress2 = string.IsNullOrEmpty(fn2) ? null : fn2,
                    TestRequested = reader["TestRequested"]?.ToString()
                });
            }
            return requests;
        }

        public List<string> GetOperators()
        {
            var names = new List<string>();
            const string query = "SELECT name FROM operators ORDER BY name";
            using var connection = new MySqlConnection(_connectionString);
            connection.Open();
            using var command = new MySqlCommand(query, connection);
            using var reader = command.ExecuteReader();
            while (reader.Read()) names.Add(reader.GetString(0));
            return names;
        }

        // Insert first, get new Id, then save file(s) using that Id, update filenames
        public void AddRequest(ProcessChangeRequestModel request, IFormFile? file)
        {
            const string insertSql = @"
INSERT INTO ProcessChangeRequest (Part, Requester, ReqDate, Request, FileAddress1, TestRequested)
VALUES (@Part, @Requester, @ReqDate, @Request, NULL, @TestRequested);";

            using var connection = new MySqlConnection(_connectionString);
            connection.Open();

            using (var cmd = new MySqlCommand(insertSql, connection))
            {
                cmd.Parameters.AddWithValue("@Part", request.Part);
                cmd.Parameters.AddWithValue("@Requester", request.Requester);
                cmd.Parameters.AddWithValue("@ReqDate", request.ReqDate ?? DateTime.Today);
                cmd.Parameters.AddWithValue("@Request", request.Request);
                cmd.Parameters.AddWithValue("@TestRequested",
                    string.IsNullOrWhiteSpace(request.TestRequested) ? DBNull.Value : int.Parse(request.TestRequested));

                cmd.ExecuteNonQuery();
                request.Id = (int)cmd.LastInsertedId;
            }

            // If there is a file, save it now using the new Id and update FileAddress1 with the filename
            if (file is { Length: > 0 })
            {
                var fileName = SaveWithPattern(file, $"ProcessChangeRequestFile1_{request.Id}");
                const string updateSql = "UPDATE ProcessChangeRequest SET FileAddress1=@f WHERE Id=@id;";
                using var up = new MySqlCommand(updateSql, connection);
                up.Parameters.AddWithValue("@f", fileName);
                up.Parameters.AddWithValue("@id", request.Id);
                up.ExecuteNonQuery();
            }
        }

        public void UpdateRequest(ProcessChangeRequestModel model, IFormFile? file1, IFormFile? file2)
        {
            // keep existing filenames unless replaced
            string fileName1 = Path.GetFileName(model.FileAddress1 ?? "");
            string fileName2 = Path.GetFileName(model.FileAddress2 ?? "");

            if (file1 is { Length: > 0 })
                fileName1 = SaveWithPattern(file1, $"ProcessChangeRequestFile1_{model.Id}");
            if (file2 is { Length: > 0 })
                fileName2 = SaveWithPattern(file2, $"ProcessChangeRequestFile2_{model.Id}");

            const string sql = @"
UPDATE ProcessChangeRequest
SET Part=@Part, Requester=@Requester, ReqDate=@ReqDate,
    Request=@Request, UpdatedBy=@UpdatedBy, UpdateResult=@UpdateResult,
    FileAddress1=@FileAddress1, FileAddress2=@FileAddress2,
    TestRequested=@TestRequested
WHERE Id=@Id;";

            using var connection = new MySqlConnection(_connectionString);
            connection.Open();
            using var cmd = new MySqlCommand(sql, connection);
            cmd.Parameters.AddWithValue("@Id", model.Id);
            cmd.Parameters.AddWithValue("@Part", model.Part);
            cmd.Parameters.AddWithValue("@Requester", model.Requester);
            cmd.Parameters.AddWithValue("@ReqDate", (object?)model.ReqDate ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@Request", model.Request);
            cmd.Parameters.AddWithValue("@UpdatedBy", (object?)model.UpdatedBy ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@UpdateResult", (object?)model.UpdateResult ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@FileAddress1", string.IsNullOrWhiteSpace(fileName1) ? (object)DBNull.Value : fileName1);
            cmd.Parameters.AddWithValue("@FileAddress2", string.IsNullOrWhiteSpace(fileName2) ? (object)DBNull.Value : fileName2);
            cmd.Parameters.AddWithValue("@TestRequested",
                string.IsNullOrWhiteSpace(model.TestRequested) ? DBNull.Value : int.Parse(model.TestRequested));
            cmd.ExecuteNonQuery();
        }

        public void UpdateFileAddress(int id, string fileAddress1)
        {
            // Treat input as filename (defensive)
            var name = Path.GetFileName(fileAddress1);
            const string sql = @"UPDATE ProcessChangeRequest SET FileAddress1=@f WHERE Id=@id;";
            using var connection = new MySqlConnection(_connectionString);
            connection.Open();
            using var cmd = new MySqlCommand(sql, connection);
            cmd.Parameters.AddWithValue("@id", id);
            cmd.Parameters.AddWithValue("@f", name);
            cmd.ExecuteNonQuery();
        }

        public void UpdateFileAddress2(int id, string fileAddress2)
        {
            var name = Path.GetFileName(fileAddress2);
            const string sql = @"UPDATE ProcessChangeRequest SET FileAddress2=@f WHERE Id=@id;";
            using var connection = new MySqlConnection(_connectionString);
            connection.Open();
            using var cmd = new MySqlCommand(sql, connection);
            cmd.Parameters.AddWithValue("@id", id);
            cmd.Parameters.AddWithValue("@f", name);
            cmd.ExecuteNonQuery();
        }

        // === Helpers ===
        private string SaveWithPattern(IFormFile file, string baseNameNoExt)
        {
            Directory.CreateDirectory(_baseFolder);
            var ext = Path.GetExtension(file.FileName); // includes dot
            var fileName = $"{baseNameNoExt}{ext}";
            var finalPath = Path.Combine(_baseFolder, fileName);
            using var fs = new FileStream(finalPath, FileMode.Create, FileAccess.Write, FileShare.None);
            file.CopyTo(fs);
            return fileName; // store just filename
        }

        public string GetAbsolutePath(string? stored)
        {
            var name = string.IsNullOrWhiteSpace(stored) ? "" : Path.GetFileName(stored);
            return string.IsNullOrWhiteSpace(name) ? "" : Path.Combine(_baseFolder, name);
        }
    }
}
