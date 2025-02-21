using DashboardReportApp.Models;
using MySql.Data.MySqlClient;
using Microsoft.Extensions.Configuration;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using iText.IO.Font.Constants;
using iText.Kernel.Font;
using iText.Kernel.Pdf;
using iText.Layout;
using iText.Layout.Borders;
using iText.Layout.Element;
using iText.Layout.Properties;
using System.Diagnostics;
using System.Data;

namespace DashboardReportApp.Services
{
    public class DeviationService
    {
        private readonly string _connectionString;

        public DeviationService(IConfiguration configuration)
        {
            _connectionString = configuration.GetConnectionString("MySQLConnection");
        }

        // Retrieves a list of operators (for the dropdown)
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

        // Saves a new deviation record asynchronously
        public async Task SaveDeviationAsync(DeviationModel model)
        {
            string query = @"
                INSERT INTO deviation 
                (part, sentDateTime, discrepancy, operator, commMethod, disposition, approvedBy, dateTimeCASTReview)
                VALUES (@part, @sentDateTime, @discrepancy, @operator, @commMethod, @disposition, @approvedBy, @dateTimeCASTReview)";

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

                    await command.ExecuteNonQueryAsync();
                }
            }
        }

        // Retrieves all deviation records asynchronously
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
                            DateTimeCASTReview = reader.IsDBNull(reader.GetOrdinal("dateTimeCASTReview")) ? (DateTime?)null : reader.GetDateTime("dateTimeCASTReview")
                        };
                        records.Add(record);
                    }
                }
            }

            return records;
        }

        // Generates a deviation PDF, prints it, and returns the PDF file path.
        public string GenerateAndPrintDeviationPdf(DeviationModel model)
        {
            // Create a file path for the PDF (e.g., under wwwroot)
            string filePath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", $"Deviation_{model.Part}_{DateTime.Now:yyyyMMddHHmmss}.pdf");

            // Load fonts
            PdfFont boldFont = PdfFontFactory.CreateFont(StandardFonts.HELVETICA_BOLD);
            PdfFont normalFont = PdfFontFactory.CreateFont(StandardFonts.HELVETICA);

            using (var writer = new PdfWriter(filePath))
            using (var pdf = new PdfDocument(writer))
            using (var document = new Document(pdf))
            {
                // Title with bottom border
                Paragraph title = new Paragraph("IN-HOUSE TEMPORARY DEVIATION REQUEST")
                    .SetFont(boldFont)
                    .SetFontSize(18)
                    .SetTextAlignment(TextAlignment.CENTER)
                    .SetBorderBottom(new SolidBorder(1));
                document.Add(title);
                document.Add(new Paragraph("\n"));

                // Create table for key details
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

            // Print the PDF using SumatraPDF
            PrintPdfWithSumatraPDF(filePath);
            return filePath;
        }

        private void PrintPdfWithSumatraPDF(string pdfPath)
        {
            string sumatraPdfPath = @"C:\Tools\SumatraPDF\SumatraPDF.exe";
            string printerName = "QC1"; // Change to your target printer
            if (!File.Exists(sumatraPdfPath))
            {
                throw new FileNotFoundException("SumatraPDF.exe not found. Please provide the correct path.");
            }

            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = sumatraPdfPath,
                    Arguments = $"-print-to \"{printerName}\" \"{pdfPath}\"",
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };

            try
            {
                process.Start();
                process.WaitForExit();
            }
            catch (Exception ex)
            {
                throw new Exception($"Failed to print PDF: {ex.Message}");
            }
        }
    }
}
