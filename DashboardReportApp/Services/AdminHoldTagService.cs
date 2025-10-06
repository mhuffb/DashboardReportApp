using DashboardReportApp.Models;
using FastReport;
using iText.IO.Font.Constants;
using iText.Kernel.Font;
using iText.Kernel.Pdf;
using iText.Layout;
using iText.Layout.Element;
using iText.Layout.Properties;
using Microsoft.AspNetCore.Http; // for IFormFile
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using MySql.Data.MySqlClient;
using System;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics;
using System.Drawing.Printing;
using System.IO;
using System.Net;
using System.Net.Mail;
using System.Threading.Tasks;

namespace DashboardReportApp.Services
{
    public class AdminHoldTagService
    {
        private readonly string _mysqlConn;
        private readonly string _sqlExpressConn;
        private readonly string _baseFolder;

        public AdminHoldTagService(IConfiguration configuration, IOptionsMonitor<PathOptions> pathOptions, IWebHostEnvironment env)
        {
            _mysqlConn = configuration.GetConnectionString("MySQLConnection")
                ?? throw new InvalidOperationException("MySQLConnection missing");
            _sqlExpressConn = configuration.GetConnectionString("SQLExpressConnection")
                ?? throw new InvalidOperationException("SQLExpressConnection missing");

            // prefer HoldTagUploads if present; otherwise use DeviationUploads
            var p = pathOptions.CurrentValue;
            var configured = !string.IsNullOrWhiteSpace(p.HoldTagUploads) ? p.HoldTagUploads! : p.DeviationUploads;

            _baseFolder = Path.IsPathFullyQualified(configured)
                ? configured
                : Path.GetFullPath(Path.Combine(env.ContentRootPath, configured));

            Directory.CreateDirectory(_baseFolder); // idempotent
        }

        // 1. Get all HoldRecord rows
        public async Task<List<AdminHoldTagModel>> GetAllHoldRecordsAsync()
        {
            var records = new List<AdminHoldTagModel>();
            using var connection = new MySqlConnection(_mysqlConn);
            await connection.OpenAsync();

            const string query = "SELECT * FROM HoldRecords ORDER BY id DESC";
            using var command = new MySqlCommand(query, connection);
            using var reader = await command.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                // Strip any legacy full paths to just the filename
                string fn1 = reader.IsDBNull("FileAddress1") ? "" : Path.GetFileName(reader.GetString("FileAddress1"));
                string fn2 = reader.IsDBNull("FileAddress2") ? "" : Path.GetFileName(reader.GetString("FileAddress2"));

                records.Add(new AdminHoldTagModel
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
                    FileAddress1 = string.IsNullOrEmpty(fn1) ? null : fn1,
                    FileAddress2 = string.IsNullOrEmpty(fn2) ? null : fn2
                });
            }

            return records;
        }

        public async Task<bool> UpdateRequest(AdminHoldTagModel model, IFormFile? file1, IFormFile? file2)
        {
            // Preserve existing filenames if no new upload
            string fileName1 = Path.GetFileName(model.FileAddress1 ?? "");
            string fileName2 = Path.GetFileName(model.FileAddress2 ?? "");

            if (file1 is { Length: > 0 })
                fileName1 = await SaveFileForHoldTagAsync(model.Id, file1, suffix: "1");

            if (file2 is { Length: > 0 })
                fileName2 = await SaveFileForHoldTagAsync(model.Id, file2, suffix: "2");

            using var connection = new MySqlConnection(_mysqlConn);
            await connection.OpenAsync();

            const string query = @"
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
            command.Parameters.AddWithValue("@Part", (object?)model.Part ?? DBNull.Value);
            command.Parameters.AddWithValue("@Discrepancy", (object?)model.Discrepancy ?? DBNull.Value);
            command.Parameters.AddWithValue("@Date", (object?)model.Date ?? DBNull.Value);
            command.Parameters.AddWithValue("@IssuedBy", (object?)model.IssuedBy ?? DBNull.Value);
            command.Parameters.AddWithValue("@Disposition", (object?)model.Disposition ?? DBNull.Value);
            command.Parameters.AddWithValue("@DispositionBy", (object?)model.DispositionBy ?? DBNull.Value);
            command.Parameters.AddWithValue("@ReworkInstr", (object?)model.ReworkInstr ?? DBNull.Value);
            command.Parameters.AddWithValue("@ReworkInstrBy", (object?)model.ReworkInstrBy ?? DBNull.Value);
            command.Parameters.AddWithValue("@Quantity", (object?)model.Quantity ?? DBNull.Value);
            command.Parameters.AddWithValue("@Unit", (object?)model.Unit ?? DBNull.Value);
            command.Parameters.AddWithValue("@PcsScrapped", (object?)model.PcsScrapped ?? DBNull.Value);
            command.Parameters.AddWithValue("@DateCompleted", (object?)model.DateCompleted ?? DBNull.Value);
            command.Parameters.AddWithValue("@FileAddress1", string.IsNullOrWhiteSpace(fileName1) ? (object)DBNull.Value : fileName1);
            command.Parameters.AddWithValue("@FileAddress2", string.IsNullOrWhiteSpace(fileName2) ? (object)DBNull.Value : fileName2);

            var rows = await command.ExecuteNonQueryAsync();
            return rows > 0;
        }

        private async Task<string> SaveFileForHoldTagAsync(int holdTagId, IFormFile file, string suffix)
        {
            Directory.CreateDirectory(_baseFolder);

            // Good, predictable names: HoldTag{suffix}_{id}{.ext}
            var ext = Path.GetExtension(file.FileName); // includes leading dot
            var fileName = $"HoldTag{suffix}_{holdTagId}{ext}";

            var finalPath = Path.Combine(_baseFolder, fileName);
            using var fs = new FileStream(finalPath, FileMode.Create, FileAccess.Write, FileShare.None);
            await file.CopyToAsync(fs);

            // Return just the filename (what we store in DB)
            return fileName;
        }

        // Rebuild absolute path from stored filename
        public string GetAbsolutePath(string? stored)
        {
            var name = string.IsNullOrWhiteSpace(stored) ? "" : Path.GetFileName(stored);
            return string.IsNullOrWhiteSpace(name) ? "" : Path.Combine(_baseFolder, name);
        }
    
       
        // -- Additional queries for operator dropdowns
        public async Task<List<string>> GetIssuedByOperatorsAsync()
        {
            var operators = new List<string>();
            using var connection = new MySqlConnection(_mysqlConn);
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
            using var connection = new MySqlConnection(_mysqlConn);
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
            using var connection = new MySqlConnection(_mysqlConn);
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
