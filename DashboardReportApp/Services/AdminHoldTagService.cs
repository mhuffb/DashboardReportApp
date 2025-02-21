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

            string query = "SELECT * FROM HoldRecords order by id desc"; // or your actual table name

            using var command = new MySqlCommand(query, connection);
            using var reader = await command.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                // Use 'IsDBNull' checks for nullable columns
                var record = new AdminHoldTagModel
                {
                    Id = reader.GetInt32(reader.GetOrdinal("Id")),
                    Timestamp = reader.IsDBNull(reader.GetOrdinal("Timestamp"))
                        ? null
                        : reader.GetDateTime(reader.GetOrdinal("Timestamp")),
                    Part = reader.IsDBNull(reader.GetOrdinal("Part"))
                        ? null
                        : reader.GetString(reader.GetOrdinal("Part")),
                    Discrepancy = reader.IsDBNull(reader.GetOrdinal("Discrepancy"))
                        ? null
                        : reader.GetString(reader.GetOrdinal("Discrepancy")),
                    Date = reader.IsDBNull(reader.GetOrdinal("Date"))
                        ? null
                        : reader.GetDateTime(reader.GetOrdinal("Date")),
                    IssuedBy = reader.IsDBNull(reader.GetOrdinal("IssuedBy"))
                        ? null
                        : reader.GetString(reader.GetOrdinal("IssuedBy")),
                    Disposition = reader.IsDBNull(reader.GetOrdinal("Disposition"))
                        ? null
                        : reader.GetString(reader.GetOrdinal("Disposition")),
                    DispositionBy = reader.IsDBNull(reader.GetOrdinal("DispositionBy"))
                        ? null
                        : reader.GetString(reader.GetOrdinal("DispositionBy")),
                    ReworkInstr = reader.IsDBNull(reader.GetOrdinal("ReworkInstr"))
                        ? null
                        : reader.GetString(reader.GetOrdinal("ReworkInstr")),
                    ReworkInstrBy = reader.IsDBNull(reader.GetOrdinal("ReworkInstrBy"))
                        ? null
                        : reader.GetString(reader.GetOrdinal("ReworkInstrBy")),

                    // ✅ Fix for integer column 'Quantity'
                    Quantity = reader.IsDBNull(reader.GetOrdinal("Quantity"))
                        ? (int?)null
                        : reader.GetInt32(reader.GetOrdinal("Quantity")),

                    Unit = reader.IsDBNull(reader.GetOrdinal("Unit"))
                        ? null
                        : reader.GetString(reader.GetOrdinal("Unit")),

                    // ✅ Already int? in the model
                    PcsScrapped = reader.IsDBNull(reader.GetOrdinal("PcsScrapped"))
                        ? (int?)null
                        : reader.GetInt32(reader.GetOrdinal("PcsScrapped")),

                    DateCompleted = reader.IsDBNull(reader.GetOrdinal("DateCompleted"))
                        ? null
                        : reader.GetDateTime(reader.GetOrdinal("DateCompleted")),
                    FileAddress = reader.IsDBNull(reader.GetOrdinal("FileAddress"))
                        ? null
                        : reader.GetString(reader.GetOrdinal("FileAddress"))
                };

                records.Add(record);
            }

            return records;
        }

        // 2. Update an existing HoldRecord row
        public async Task<bool> UpdateRequest(AdminHoldTagModel model, IFormFile? file)
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
                var fileName = $"HoldTag{model.Id}{fileExtension}";

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
                    FileAddress = @FileAddress
                WHERE Id = @Id";

            using var command = new MySqlCommand(query, connection);

            // Safely handle null vs DB null
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
            command.Parameters.AddWithValue("@FileAddress", filePath ?? (object)DBNull.Value);

            int rowsAffected = await command.ExecuteNonQueryAsync();
            return rowsAffected > 0; // true if at least one row was updated
        }

        public async Task<List<string>> GetIssuedByOperatorsAsync()
        {
            var operators = new List<string>();
            using var connection = new MySqlConnection(_connectionString);
            await connection.OpenAsync();
            string query = "SELECT name FROM operators"; // Returns all names for Issued By
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