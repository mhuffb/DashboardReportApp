﻿using System;
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
    public class HoldTagService
    {
        private readonly string _connectionString;
        private readonly string _connectionStringSQLExpress;
        private readonly string _uploadFolder;
        public HoldTagService(IConfiguration configuration)
        {
            _connectionString = configuration.GetConnectionString("MySQLConnection");
            _connectionStringSQLExpress = configuration.GetConnectionString("SQLExpressConnection");
            _uploadFolder = @"\\SINTERGYDC2024\Vol1\VSP\Uploads";
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

        public async Task AddHoldRecordAsync(HoldTagModel record)
        {
            string query = @"INSERT INTO holdrecords 
                (part, discrepancy, date, issuedBy, disposition, dispositionBy, reworkInstr, reworkInstrBy, quantity, unit, pcsScrapped, dateCompleted, fileAddress1, fileAddress2)
                VALUES (@part, @discrepancy, @date, @issuedBy, @disposition, @dispositionBy, @reworkInstr, @reworkInstrBy, @quantity, @unit, @pcsScrapped, @dateCompleted, @fileAddress1, @fileAddress2)";

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
                    command.Parameters.AddWithValue("@fileAddress1", record.FileAddress1 ?? (object)DBNull.Value);
                    command.Parameters.AddWithValue("@fileAddress2", record.FileAddress2 ?? (object)DBNull.Value);

                    await command.ExecuteNonQueryAsync();
                }
            }
        }

        public string GenerateHoldTagPdf(HoldTagModel record)
        {
            string filePath = @"\\SINTERGYDC2024\Vol1\Visual Studio Programs\reports\HoldTag_" + record.Id + ".pdf";

            // Use predefined fonts
            PdfFont boldFont = PdfFontFactory.CreateFont(StandardFonts.HELVETICA_BOLD);
            PdfFont normalFont = PdfFontFactory.CreateFont(StandardFonts.HELVETICA);

            using (var writer = new PdfWriter(filePath))
            using (var pdf = new PdfDocument(writer))
            using (var document = new Document(pdf))
            {
                // Title
                document.Add(new Paragraph("Hold").SetFont(boldFont).SetFontSize(18).SetTextAlignment(TextAlignment.CENTER));
                document.Add(new Paragraph("\n")); // Spacing

                // Part Number
                string partNumber = string.IsNullOrWhiteSpace(record.Part) ? "N/A" : record.Part;
                document.Add(new Paragraph("Part Number:").SetFont(boldFont).SetFontSize(12));
                document.Add(new Paragraph(partNumber).SetFont(normalFont).SetFontSize(12));
                document.Add(new Paragraph("\n")); // Spacing

                // Discrepancy
                document.Add(new Paragraph("Discrepancy:").SetFont(boldFont).SetFontSize(12));
                document.Add(new Paragraph(record.Discrepancy ?? "N/A").SetFont(normalFont).SetFontSize(12));
                document.Add(new Paragraph("\n")); // Spacing

                // Issued By and Issued Date
                string issuedBy = string.IsNullOrWhiteSpace(record.IssuedBy) ? "Unknown" : record.IssuedBy;
                document.Add(new Paragraph("Issued By:").SetFont(boldFont).SetFontSize(12));
                document.Add(new Paragraph(issuedBy).SetFont(normalFont).SetFontSize(12));
                document.Add(new Paragraph("Issued Date:").SetFont(boldFont).SetFontSize(12));
                document.Add(new Paragraph($"{record.Date:MM/dd/yyyy}").SetFont(normalFont).SetFontSize(12));
                document.Add(new Paragraph("\n")); // Spacing

                // Corrective Action Needed
                document.Add(new Paragraph("Corrective Action Needed: Yes ☐  No ☐").SetFont(boldFont).SetFontSize(12));
                document.Add(new Paragraph("\n")); // Spacing

                // Quantity and Unit
                string quantityAndUnit = $"{record.Quantity} {record.Unit}".Trim();
                document.Add(new Paragraph("Amount:").SetFont(boldFont).SetFontSize(12));
                document.Add(new Paragraph(string.IsNullOrWhiteSpace(quantityAndUnit) ? "N/A" : quantityAndUnit).SetFont(normalFont).SetFontSize(12));
                document.Add(new Paragraph("\n")); // Spacing

                // Footer Instructions
                document.Add(new Paragraph("Return Form To QA Manager Once Completed")
                    .SetFont(boldFont)
                    .SetFontSize(12)
                    .SetTextAlignment(TextAlignment.CENTER));
            }

            return filePath; // Return the generated PDF file path
        }


        public async Task<List<string>> GetPartsAsync()
        {
            var parts = new List<string>();
            string query = @"
        SELECT DISTINCT
    qf.qcc_file_desc
FROM 
    part p
INNER JOIN 
    qcc_file qf ON p.qcc_file_id = qf.qcc_file_id
WHERE 
    p.measure_date >= DATEADD(HOUR, -12, GETDATE())
    AND qf.qcc_file_desc NOT LIKE '%CALIBRATION%'
ORDER BY 
    qf.qcc_file_desc";


            using (var connection = new SqlConnection(_connectionStringSQLExpress))
            {
                await connection.OpenAsync();
                using (var command = new SqlCommand(query, connection))
                using (var reader = await command.ExecuteReaderAsync())
                {
                    var uniqueParts = new HashSet<string>(); // Use HashSet to eliminate duplicates

                    while (await reader.ReadAsync())
                    {
                        string prolinkPartNumber = reader["qcc_file_desc"].ToString(); // Fetch the raw part number
                        string parsedPartNumber = parseProlinkPartNumber(prolinkPartNumber); // Process part number

                        if (!string.IsNullOrEmpty(parsedPartNumber))
                        {
                            uniqueParts.Add(parsedPartNumber); // Ensure uniqueness
                        }
                    }

                    parts = uniqueParts.ToList(); // Convert HashSet to List
                }
            }

            return parts;
        }

        private string parseProlinkPartNumber(string prolinkPartNumber)
        {
            string sintergyPartNumber = "";

            if (!string.IsNullOrEmpty(prolinkPartNumber))
            {
                // Split the string by '-'
                string[] parts = prolinkPartNumber.Split('-');

                // Check if there are at least two '-' in the string
                if (parts.Length > 2)
                {
                    // Concatenate the first two parts back with '-'
                    sintergyPartNumber = $"{parts[0]}-{parts[1]}";
                }
                else
                {
                    // If there are fewer than two '-', use the original string
                    sintergyPartNumber = prolinkPartNumber;
                }
            }

            return sintergyPartNumber;
        }
        public void SendEmailWithAttachment(string senderEmail, string senderPassword, string receiverEmail, string smtpServer, string attachmentPath, HoldTagModel record)
        {
            int smtpPort = 587;

            // Create the email content
            string subject = $"{record.Part} Placed on Hold By: {record.IssuedBy}";
            string body = $"Discrepancy: {record.Discrepancy}\n" +
                          $"Quantity: {record.Quantity} {record.Unit}\n" +
                          $"Issued By: {record.IssuedBy}\n" +
                          $"Issued Date: {record.Date:MM/dd/yyyy}";

            MailMessage mail = new MailMessage(senderEmail, receiverEmail)
            {
                Subject = subject,
                Body = body
            };

            // Attach the PDF if it exists
            if (!string.IsNullOrEmpty(attachmentPath))
            {
                Attachment attachment = new Attachment(attachmentPath);
                mail.Attachments.Add(attachment);
            }

            // Configure SMTP client
            SmtpClient smtpClient = new SmtpClient(smtpServer)
            {
                Port = smtpPort,
                Credentials = new NetworkCredential(senderEmail, senderPassword),
                EnableSsl = false // Set this to true if your server supports SSL
            };

            try
            {
                smtpClient.Send(mail);
            }
            catch (Exception ex)
            {
                throw new Exception($"Failed to send email: {ex.Message}");
            }
            finally
            {
                mail.Dispose();
            }
        }
        public void PrintPdf(string pdfPath)
        {
            if (string.IsNullOrWhiteSpace(pdfPath) || !File.Exists(pdfPath))
            {
                throw new FileNotFoundException("PDF file not found for printing.", pdfPath);
            }

            try
            {
                string printerName = "QAHoldTags"; // Set the specific printer name
                string sumatraPath = @"C:\Tools\SumatraPDF\SumatraPDF.exe"; // Path to SumatraPDF executable

                // Validate printer existence
                if (!PrinterSettings.InstalledPrinters.Cast<string>().Any(p => p.Equals(printerName, StringComparison.OrdinalIgnoreCase)))
                {
                    throw new Exception($"Printer '{printerName}' is not installed.");
                }

                // Validate SumatraPDF existence
                if (!File.Exists(sumatraPath))
                {
                    throw new FileNotFoundException("SumatraPDF executable not found.", sumatraPath);
                }

                // Set up the SumatraPDF command-line arguments
                string arguments = $"-print-to \"{printerName}\" \"{pdfPath}\"";

                // Start the process
                var process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = sumatraPath,
                        Arguments = arguments,
                        CreateNoWindow = true,
                        WindowStyle = ProcessWindowStyle.Hidden,
                    }
                };

                process.Start();
                process.WaitForExit();

                if (process.ExitCode != 0)
                {
                    throw new Exception($"Printing process exited with code {process.ExitCode}.");
                }
            }
            catch (Exception ex)
            {
                throw new Exception($"Failed to print PDF: {ex.Message}");
            }
        }

        // 1. Get all HoldRecord rows
        public async Task<List<HoldTagModel>> GetAllHoldRecordsAsync()
        {
            var records = new List<HoldTagModel>();

            using var connection = new MySqlConnection(_connectionString);
            await connection.OpenAsync();

            string query = "SELECT * FROM HoldRecords order by id desc"; // or your actual table name

            using var command = new MySqlCommand(query, connection);
            using var reader = await command.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                // Use 'IsDBNull' checks for nullable columns
                var record = new HoldTagModel
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
                    FileAddress1 = reader.IsDBNull(reader.GetOrdinal("FileAddress1"))
                        ? null
                        : reader.GetString(reader.GetOrdinal("FileAddress1")),
                    FileAddress2 = reader.IsDBNull(reader.GetOrdinal("FileAddress2"))
                        ? null
                        : reader.GetString(reader.GetOrdinal("FileAddress2"))

                };

                records.Add(record);
            }

            return records;
        }

        public string SaveHoldTagFile(IFormFile file)
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
            var uniqueName = "HoldTagFile1_" + DateTime.Now.Ticks + extension;
            var finalPath = Path.Combine(_uploadFolder, uniqueName);

            // Copy the file to disk
            using (var stream = new FileStream(finalPath, FileMode.Create))
            {
                file.CopyTo(stream);
            }

            // Return the path so we can save it in record.FileAddress1
            return finalPath;
        }
        public async Task UpdateFileAddress1Async(int id, string filePath)
        {
            string query = @"UPDATE holdrecords
                     SET FileAddress1 = @FileAddress1
                     WHERE Id = @Id";

            using (var connection = new MySqlConnection(_connectionString))
            {
                await connection.OpenAsync();
                using (var command = new MySqlCommand(query, connection))
                {
                    command.Parameters.AddWithValue("@Id", id);
                    command.Parameters.AddWithValue("@FileAddress1", filePath);
                    await command.ExecuteNonQueryAsync();
                }
            }
        }

    }
}