using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Threading.Tasks;
using DashboardReportApp.Models;
using iText.Kernel.Pdf;
using iText.Layout;
using iText.Layout.Element;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using MySql.Data.MySqlClient;
using iText.Kernel.Font;
using iText.Layout.Properties;
using iText.IO.Font.Constants;
using System.Net.Mail;
using System.Net;
using System.Diagnostics;
using System.Drawing.Printing;
using FastReport;
using Microsoft.AspNetCore.Http; // for IFormFile
using Microsoft.AspNetCore.Mvc;

namespace DashboardReportApp.Services
{
    public class AdminHoldTagService
    {
        private readonly string _connectionString;
        private readonly string _connectionStringSQLExpress;
        private readonly string _uploadFolder;

        public AdminHoldTagService(IConfiguration configuration)
        {
            _connectionString = configuration.GetConnectionString("MySQLConnection");
            _connectionStringSQLExpress = configuration.GetConnectionString("SQLExpressConnection");
            _uploadFolder = @"\\SINTERGYDC2024\Vol1\Visual Studio Programs\VSP\Uploads";
        }

        // 1. Get all HoldRecord rows
        public async Task<List<AdminHoldTagModel>> GetAllHoldRecordsAsync()
        {
            var records = new List<AdminHoldTagModel>();

            using var connection = new MySqlConnection(_connectionString);
            await connection.OpenAsync();

            string query = "SELECT * FROM HoldRecords ORDER BY id DESC";

            using var command = new MySqlCommand(query, connection);
            using var reader = await command.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                var record = new AdminHoldTagModel
                {
                    Id = reader.GetInt32("Id"),
                    Timestamp = reader.IsDBNull("Timestamp") ? null : reader.GetDateTime("Timestamp"),
                    Part = reader.IsDBNull("Part") ? null : reader.GetString("Part"),
                    Discrepancy = reader.IsDBNull("Discrepancy") ? null : reader.GetString("Discrepancy"),
                    Date = reader.IsDBNull("Date") ? null : reader.GetDateTime("Date"),
                    IssuedBy = reader.IsDBNull("IssuedBy") ? null : reader.GetString("IssuedBy"),
                    Disposition = reader.IsDBNull("Disposition") ? null : reader.GetString("Disposition"),
                    DispositionBy = reader.IsDBNull("DispositionBy") ? null : reader.GetString("DispositionBy"),
                    ReworkInstr = reader.IsDBNull("ReworkInstr") ? null : reader.GetString("ReworkInstr"),
                    ReworkInstrBy = reader.IsDBNull("ReworkInstrBy") ? null : reader.GetString("ReworkInstrBy"),
                    Quantity = reader.IsDBNull("Quantity") ? (int?)null : reader.GetInt32("Quantity"),
                    Unit = reader.IsDBNull("Unit") ? null : reader.GetString("Unit"),
                    PcsScrapped = reader.IsDBNull("PcsScrapped") ? (int?)null : reader.GetInt32("PcsScrapped"),
                    DateCompleted = reader.IsDBNull("DateCompleted") ? null : reader.GetDateTime("DateCompleted"),
                    FileAddress1 = reader.IsDBNull("FileAddress1") ? null : reader.GetString("FileAddress1"),
                    FileAddress2 = reader.IsDBNull("FileAddress2") ? null : reader.GetString("FileAddress2")
                };

                records.Add(record);
            }

            return records;
        }

        // 2. Update an existing HoldRecord row, handling up to 2 file uploads
        public async Task<bool> UpdateRequest(AdminHoldTagModel model, IFormFile? file1, IFormFile? file2)
        {
            // Preserve existing file paths, unless we upload a new file
            string filePath1 = model.FileAddress1;
            string filePath2 = model.FileAddress2;

            // 1) If FileUpload1 was provided, save/replace FileAddress1
            if (file1 != null && file1.Length > 0)
            {
                filePath1 = await SaveFileForHoldTagAsync(model.Id, file1, "File1");
            }

            // 2) If FileUpload2 was provided, save/replace FileAddress2
            if (file2 != null && file2.Length > 0)
            {
                filePath2 = await SaveFileForHoldTagAsync(model.Id, file2, "File2");
            }

            // Now update DB record
            using var connection = new MySqlConnection(_connectionString);
            await connection.OpenAsync();

            string query = @"
                UPDATE HoldRecords
                SET 
                    Part = @Part,
                    Discrepancy = @Discrepancy,
                    Date = @Date,
                    IssuedBy = @IssuedBy,
                    Disposition = @Disposition,
                    DispositionBy = @DispositionBy,
                    ReworkInstr = @ReworkInstr,
                    ReworkInstrBy = @ReworkInstrBy,
                    Quantity = @Quantity,
                    Unit = @Unit,
                    PcsScrapped = @PcsScrapped,
                    DateCompleted = @DateCompleted,
                    FileAddress1 = @FileAddress1,
                    FileAddress2 = @FileAddress2
                WHERE Id = @Id";

            using var command = new MySqlCommand(query, connection);

            command.Parameters.AddWithValue("@Id", model.Id);
            command.Parameters.AddWithValue("@Part", model.Part ?? (object)DBNull.Value);
            command.Parameters.AddWithValue("@Discrepancy", model.Discrepancy ?? (object)DBNull.Value);
            command.Parameters.AddWithValue("@Date", model.Date ?? (object)DBNull.Value);
            command.Parameters.AddWithValue("@IssuedBy", model.IssuedBy ?? (object)DBNull.Value);
            command.Parameters.AddWithValue("@Disposition", model.Disposition ?? (object)DBNull.Value);
            command.Parameters.AddWithValue("@DispositionBy", model.DispositionBy ?? (object)DBNull.Value);
            command.Parameters.AddWithValue("@ReworkInstr", model.ReworkInstr ?? (object)DBNull.Value);
            command.Parameters.AddWithValue("@ReworkInstrBy", model.ReworkInstrBy ?? (object)DBNull.Value);
            command.Parameters.AddWithValue("@Quantity", model.Quantity ?? (object)DBNull.Value);
            command.Parameters.AddWithValue("@Unit", model.Unit ?? (object)DBNull.Value);
            command.Parameters.AddWithValue("@PcsScrapped", model.PcsScrapped ?? (object)DBNull.Value);
            command.Parameters.AddWithValue("@DateCompleted", model.DateCompleted ?? (object)DBNull.Value);
            command.Parameters.AddWithValue("@FileAddress1", filePath1 ?? (object)DBNull.Value);
            command.Parameters.AddWithValue("@FileAddress2", filePath2 ?? (object)DBNull.Value);

            int rowsAffected = await command.ExecuteNonQueryAsync();
            return (rowsAffected > 0);
        }

        /// <summary>
        /// Saves a file to disk for the given HoldTag (by ID).
        /// We append a suffix for clarity (e.g. "File1" or "File2").
        /// </summary>
        private async Task<string> SaveFileForHoldTagAsync(int holdTagId, IFormFile file, string suffix)
        {
            if (!Directory.Exists(_uploadFolder))
            {
                Directory.CreateDirectory(_uploadFolder);
            }

            // Example: "HoldTag_123_File1.pdf"
            var extension = Path.GetExtension(file.FileName);
            var fileName = "";
            if(suffix == "File1")
            {
                fileName = $"HoldTag1_{holdTagId}_{extension}";
            }
            else if (suffix == "File2")
            {
                fileName = $"HoldTag2_{holdTagId}_{extension}";
            }
            var finalPath = Path.Combine(_uploadFolder, fileName);

            using (var stream = new FileStream(finalPath, FileMode.Create))
            {
                await file.CopyToAsync(stream);
            }

            return finalPath;
        }
       
        // -- Additional queries for operator dropdowns
        public async Task<List<string>> GetIssuedByOperatorsAsync()
        {
            var operators = new List<string>();
            using var connection = new MySqlConnection(_connectionString);
            await connection.OpenAsync();
            string query = "SELECT name FROM operators"; // all
            using var command = new MySqlCommand(query, connection);
            using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                operators.Add(reader.IsDBNull(0) ? "" : reader.GetString(0));
            }
            return operators;
        }

        public async Task<List<string>> GetDispositionOperatorsAsync()
        {
            var operators = new List<string>();
            using var connection = new MySqlConnection(_connectionString);
            await connection.OpenAsync();
            string query = "SELECT name FROM operators WHERE allowHoldDisp = 1";
            using var command = new MySqlCommand(query, connection);
            using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                operators.Add(reader.IsDBNull(0) ? "" : reader.GetString(0));
            }
            return operators;
        }

        public async Task<List<string>> GetReworkOperatorsAsync()
        {
            var operators = new List<string>();
            using var connection = new MySqlConnection(_connectionString);
            await connection.OpenAsync();
            string query = "SELECT name FROM operators WHERE allowHoldRework = 1";
            using var command = new MySqlCommand(query, connection);
            using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                operators.Add(reader.IsDBNull(0) ? "" : reader.GetString(0));
            }
            return operators;
        }
    }
}
