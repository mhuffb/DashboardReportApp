using DashboardReportApp.Models;
using MySql.Data.MySqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.AspNetCore.Http;
using iText.IO.Font.Constants;
using iText.Kernel.Font;
using iText.Kernel.Pdf;
using iText.Layout;
using iText.Layout.Borders;
using iText.Layout.Element;
using iText.Layout.Properties;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Data;

namespace DashboardReportApp.Services
{
    public class DeviationService
    {
        private readonly string _connectionString;
        private readonly string _uploadFolder;
        private readonly SharedService _sharedService;
        public DeviationService(IConfiguration configuration, SharedService sharedService)
        {
            _connectionString = configuration.GetConnectionString("MySQLConnection");
            _sharedService = sharedService;
            // For example, save deviation files in wwwroot\DeviationUploads
            _uploadFolder = @"\\SINTERGYDC2024\Vol1\VSP\Uploads";
            if (!Directory.Exists(_uploadFolder))
            {
                Directory.CreateDirectory(_uploadFolder);
            }
        }

        // Retrieves a list of operators for the dropdown.
        public List<string> GetOperators()
        {
            var operators = new List<string>();
            string query = "SELECT DISTINCT name FROM operators ORDER BY name";

            using (var connection = new MySqlConnection(_connectionString))
            {
                connection.Open();
                using (var command = new MySqlCommand(query, connection))
                using (var reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        operators.Add(reader["name"].ToString());
                    }
                }
            }
            return operators;
        }

        // Saves a new deviation record.
        // If a file is uploaded, saves it and stores its path in FileAddress1.
        public async Task SaveDeviationAsync(DeviationModel model, IFormFile? file)
        {
            if (file != null && file.Length > 0)
            {
                // Use the same file-saving logic as the hold tag.
                string filePath = SaveDeviationFile(file);
                model.FileAddress1 = filePath;
            }
            // FileAddress2 remains untouched (read-only)

            string query = @"
                INSERT INTO deviation 
                (part, sentDateTime, discrepancy, operator, commMethod, disposition, approvedBy, dateTimeCASTReview, FileAddress1, FileAddress2)
                VALUES (@part, @sentDateTime, @discrepancy, @operator, @commMethod, @disposition, @approvedBy, @dateTimeCASTReview, @FileAddress1, @FileAddress2)";

            using (var connection = new MySqlConnection(_connectionString))
            {
                await connection.OpenAsync();
                using (var command = new MySqlCommand(query, connection))
                {
                    command.Parameters.AddWithValue("@part", model.Part?.ToUpper());
                    command.Parameters.AddWithValue("@sentDateTime", DateTime.Now);
                    command.Parameters.AddWithValue("@discrepancy", model.Discrepancy);
                    command.Parameters.AddWithValue("@operator", model.Operator);
                    command.Parameters.AddWithValue("@commMethod", model.CommMethod);
                    command.Parameters.AddWithValue("@disposition", model.Disposition ?? (object)DBNull.Value);
                    command.Parameters.AddWithValue("@approvedBy", model.ApprovedBy ?? (object)DBNull.Value);
                    command.Parameters.AddWithValue("@dateTimeCASTReview", model.DateTimeCASTReview ?? (object)DBNull.Value);
                    command.Parameters.AddWithValue("@FileAddress1", model.FileAddress1 ?? (object)DBNull.Value);
                    command.Parameters.AddWithValue("@FileAddress2", model.FileAddress2 ?? (object)DBNull.Value);

                    await command.ExecuteNonQueryAsync();
                }
            }
        }

        // Synchronous file-saving method modeled after the hold tag version.
        public string SaveDeviationFile(IFormFile file)
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



        // Retrieves all deviation records.
        public async Task<List<DeviationModel>> GetAllDeviationsAsync()
        {
            var records = new List<DeviationModel>();
            string query = "SELECT * FROM deviation ORDER BY id DESC";

            using (var connection = new MySqlConnection(_connectionString))
            {
                await connection.OpenAsync();
                using (var command = new MySqlCommand(query, connection))
                using (var reader = await command.ExecuteReaderAsync())
                {
                    while (await reader.ReadAsync())
                    {
                        var record = new DeviationModel
                        {
                            Id = reader.GetInt32("id"),
                            Part = reader.IsDBNull(reader.GetOrdinal("part")) ? null : reader.GetString("part"),
                            SentDateTime = reader.IsDBNull(reader.GetOrdinal("sentDateTime")) ? (DateTime?)null : reader.GetDateTime("sentDateTime"),
                            Discrepancy = reader.IsDBNull(reader.GetOrdinal("discrepancy")) ? null : reader.GetString("discrepancy"),
                            Operator = reader.IsDBNull(reader.GetOrdinal("operator")) ? null : reader.GetString("operator"),
                            CommMethod = reader.IsDBNull(reader.GetOrdinal("commMethod")) ? null : reader.GetString("commMethod"),
                            Disposition = reader.IsDBNull(reader.GetOrdinal("disposition")) ? null : reader.GetString("disposition"),
                            ApprovedBy = reader.IsDBNull(reader.GetOrdinal("approvedBy")) ? null : reader.GetString("approvedBy"),
                            DateTimeCASTReview = reader.IsDBNull(reader.GetOrdinal("dateTimeCASTReview")) ? (DateTime?)null : reader.GetDateTime("dateTimeCASTReview"),
                            FileAddress1 = reader.IsDBNull(reader.GetOrdinal("FileAddress1")) ? null : reader.GetString("FileAddress1"),
                            FileAddress2 = reader.IsDBNull(reader.GetOrdinal("FileAddress2")) ? null : reader.GetString("FileAddress2")
                        };
                        records.Add(record);
                    }
                }
            }
            return records;
        }

        // Generates a PDF for the deviation, prints it, and returns its file path.
        public string GenerateAndPrintDeviationPdf(DeviationModel model)
        {
            string filePath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", $"Deviation_{model.Part}_{DateTime.Now:yyyyMMddHHmmss}.pdf");

            PdfFont boldFont = PdfFontFactory.CreateFont(StandardFonts.HELVETICA_BOLD);
            PdfFont normalFont = PdfFontFactory.CreateFont(StandardFonts.HELVETICA);

            using (var writer = new PdfWriter(filePath))
            using (var pdf = new PdfDocument(writer))
            using (var document = new Document(pdf))
            {
                Paragraph title = new Paragraph("IN-HOUSE TEMPORARY DEVIATION REQUEST")
                    .SetFont(boldFont)
                    .SetFontSize(18)
                    .SetTextAlignment(TextAlignment.CENTER)
                    .SetBorderBottom(new SolidBorder(1));
                document.Add(title);
                document.Add(new Paragraph("\n"));

                Table table = new Table(2).UseAllAvailableWidth();
                table.AddCell(new Cell().Add(new Paragraph("Part:").SetFont(boldFont)).SetBorderBottom(new SolidBorder(1)));
                table.AddCell(new Cell().Add(new Paragraph(model.Part).SetFont(normalFont)).SetBorderBottom(new SolidBorder(1)));
                table.AddCell(new Cell().Add(new Paragraph("Discrepancy:").SetFont(boldFont)).SetBorderBottom(new SolidBorder(1)));
                table.AddCell(new Cell().Add(new Paragraph(model.Discrepancy).SetFont(normalFont)).SetBorderBottom(new SolidBorder(1)));
                table.AddCell(new Cell().Add(new Paragraph("Operator:").SetFont(boldFont)).SetBorderBottom(new SolidBorder(1)));
                table.AddCell(new Cell().Add(new Paragraph(model.Operator).SetFont(normalFont)).SetBorderBottom(new SolidBorder(1)));
                table.AddCell(new Cell().Add(new Paragraph("Comm Method:").SetFont(boldFont)).SetBorderBottom(new SolidBorder(1)));
                table.AddCell(new Cell().Add(new Paragraph(model.CommMethod).SetFont(normalFont)).SetBorderBottom(new SolidBorder(1)));
                document.Add(table);

                document.Add(new Paragraph("\nDisposition: ________________________________").SetFont(normalFont).SetFontSize(12));
                document.Add(new Paragraph("\nApproved By: ________________________________").SetFont(normalFont).SetFontSize(12));

                document.Add(new Paragraph("\nPLACE ONE COPY OF DEVIATION WITH PAPERWORK AND PLACE ONE IN QUALITY CONTROL")
                    .SetFont(boldFont)
                    .SetTextAlignment(TextAlignment.CENTER));
                document.Add(new Paragraph("REVIEWED BY C.A.S.T")
                    .SetFont(boldFont)
                    .SetTextAlignment(TextAlignment.CENTER));
            }
            Console.WriteLine("Printing file: " + filePath);
            _sharedService.PrintFileToSpecificPrinter("QC1", filePath);
            return filePath;
        }

       
        // Updates FileAddress1 for an existing deviation record.
        // Uses the same file-saving logic as in Create.
        public async Task<bool> UpdateFileAddress1Async(int id, IFormFile file)
        {
            // Save the new file (same as with Hold Tag)
            string filePath = SaveDeviationFile(file);

            using (var connection = new MySqlConnection(_connectionString))
            {
                await connection.OpenAsync();
                string query = "UPDATE deviation SET FileAddress1 = @FileAddress1 WHERE id = @id";
                using (var command = new MySqlCommand(query, connection))
                {
                    command.Parameters.AddWithValue("@FileAddress1", filePath);
                    command.Parameters.AddWithValue("@id", id);
                    int rowsAffected = await command.ExecuteNonQueryAsync();
                    return rowsAffected > 0;
                }
            }
        }
    }
}
