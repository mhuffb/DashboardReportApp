using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Threading.Tasks;
using DashboardReportApp.Models;
using iText.Kernel.Pdf;
using iText.Layout;
using iText.Layout.Element;
using Microsoft.Extensions.Configuration;
using MySql.Data.MySqlClient;

namespace DashboardReportApp.Services
{
    public class HoldTagService
    {
        private readonly string _connectionString;

        public HoldTagService(IConfiguration configuration)
        {
            _connectionString = configuration.GetConnectionString("MySQLConnection");
        }

        public async Task<List<string>> GetOperatorsAsync()
        {
            var operators = new List<string>();
            string query = "SELECT DISTINCT name FROM operators ORDER BY name";

            using (var connection = new MySqlConnection(_connectionString))
            {
                await connection.OpenAsync();
                using (var command = new MySqlCommand(query, connection))
                using (var reader = await command.ExecuteReaderAsync())
                {
                    while (await reader.ReadAsync())
                    {
                        operators.Add(reader["name"].ToString());
                    }
                }
            }

            return operators;
        }

        public async Task AddHoldRecordAsync(HoldRecord record)
        {
            string query = @"INSERT INTO holdrecords 
                (part, discrepancy, date, issuedBy, disposition, dispositionBy, reworkInstr, reworkInstrBy, quantity, unit, pcsScrapped, dateCompleted, fileAddress)
                VALUES (@part, @discrepancy, @date, @issuedBy, @disposition, @dispositionBy, @reworkInstr, @reworkInstrBy, @quantity, @unit, @pcsScrapped, @dateCompleted, @fileAddress)";

            using (var connection = new MySqlConnection(_connectionString))
            {
                await connection.OpenAsync();
                using (var command = new MySqlCommand(query, connection))
                {
                    command.Parameters.AddWithValue("@part", record.Part);
                    command.Parameters.AddWithValue("@discrepancy", record.Discrepancy);
                    command.Parameters.AddWithValue("@date", record.Date);
                    command.Parameters.AddWithValue("@issuedBy", record.IssuedBy);
                    command.Parameters.AddWithValue("@disposition", (object)record.Disposition ?? DBNull.Value);

                    command.Parameters.AddWithValue("@dispositionBy", record.DispositionBy ?? (object)DBNull.Value);
                    command.Parameters.AddWithValue("@reworkInstr", record.ReworkInstr ?? (object)DBNull.Value);
                    command.Parameters.AddWithValue("@reworkInstrBy", record.ReworkInstrBy ?? (object)DBNull.Value);
                    command.Parameters.AddWithValue("@quantity", record.Quantity);
                    command.Parameters.AddWithValue("@unit", record.Unit);
                    command.Parameters.AddWithValue("@pcsScrapped", record.PcsScrapped ?? (object)DBNull.Value);
                    command.Parameters.AddWithValue("@dateCompleted", record.DateCompleted ?? (object)DBNull.Value);
                    command.Parameters.AddWithValue("@fileAddress", record.FileAddress ?? (object)DBNull.Value);

                    await command.ExecuteNonQueryAsync();
                }
            }
        }

        public string GeneratePdf(HoldRecord record)
        {
            string filePath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "HoldTagReport.pdf");

            using (PdfWriter writer = new PdfWriter(filePath))
            {
                using (PdfDocument pdf = new PdfDocument(writer))
                {
                    using (Document document = new Document(pdf))
                    {
                        document.Add(new Paragraph($"Part: {record.Part}"));
                        document.Add(new Paragraph($"Date: {record.Date:yyyy-MM-dd HH:mm:ss}"));
                        document.Add(new Paragraph($"Discrepancy: {record.Discrepancy}"));
                        document.Add(new Paragraph($"Disposition: {record.Disposition}"));
                        document.Add(new Paragraph($"Quantity: {record.Quantity} {record.Unit}"));
                        document.Add(new Paragraph($"Issued By: {record.IssuedBy}"));
                        document.Add(new Paragraph($"Date Completed: {record.DateCompleted?.ToString("yyyy-MM-dd") ?? "N/A"}"));

                    }
                }
            }

            return filePath;
        }
    }
}
