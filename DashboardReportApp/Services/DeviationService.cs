using DashboardReportApp.Models;
using iText.IO.Font.Constants;
using iText.Kernel.Font;
using iText.Kernel.Pdf;
using iText.Layout;
using iText.Layout.Borders;
using iText.Layout.Element;
using iText.Layout.Properties;
using MySql.Data.MySqlClient;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Diagnostics;

namespace DashboardReportApp.Services
{
    public class DeviationService
    {
        private readonly string _connectionString;

        public DeviationService(IConfiguration configuration)
        {
            _connectionString = configuration.GetConnectionString("MySQLConnection");
        }

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

        public void SaveDeviation(DeviationModel model)
        {
            string query = @"
                INSERT INTO deviation (part, sentDateTime, discrepancy, operator, commMethod, disposition, approvedBy, dateTimeCASTReview)
                VALUES (@part, @sentDateTime, @discrepancy, @operator, @commMethod, @disposition, @approvedBy, @dateTimeCASTReview)";

            using (var connection = new MySqlConnection(_connectionString))
            {
                connection.Open();
                using (var command = new MySqlCommand(query, connection))
                {
                    command.Parameters.AddWithValue("@part", model.Part?.ToUpper());
                    command.Parameters.AddWithValue("@sentDateTime", DateTime.Now);
                    command.Parameters.AddWithValue("@discrepancy", model.Discrepancy);
                    command.Parameters.AddWithValue("@operator", model.Operator);
                    command.Parameters.AddWithValue("@commMethod", model.CommMethod);
                    command.Parameters.AddWithValue("@disposition", (object)model.Disposition ?? DBNull.Value);
                    command.Parameters.AddWithValue("@approvedBy", (object)model.ApprovedBy ?? DBNull.Value);
                    command.Parameters.AddWithValue("@dateTimeCASTReview", (object)model.DateTimeCASTReview ?? DBNull.Value);

                    command.ExecuteNonQuery();
                }
            }
        }
public string GenerateAndPrintDeviationPdf(DeviationModel model)
    {
        string filePath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", $"Deviation_{model.Part}_{DateTime.Now:yyyyMMddHHmmss}.pdf");

        // Load fonts
        PdfFont boldFont = PdfFontFactory.CreateFont(StandardFonts.HELVETICA_BOLD);
        PdfFont normalFont = PdfFontFactory.CreateFont(StandardFonts.HELVETICA);

        using (var writer = new PdfWriter(filePath))
        using (var pdf = new PdfDocument(writer))
        using (var document = new Document(pdf))
        {
            // Title with Bottom Border
            Paragraph title = new Paragraph("IN-HOUSE TEMPORARY DEVIATION REQUEST")
                .SetFont(boldFont)
                .SetFontSize(18)
                .SetTextAlignment(TextAlignment.CENTER)
                .SetBorderBottom(new SolidBorder(1));
            document.Add(title);

            // Spacing
            document.Add(new Paragraph("\n"));

            // Add Part Information
            Table table = new Table(2).UseAllAvailableWidth();
            table.AddCell(new Cell().Add(new Paragraph("Part:").SetFont(boldFont)).SetBorderBottom(new SolidBorder(1)));
            table.AddCell(new Cell().Add(new Paragraph(model.Part).SetFont(normalFont)).SetBorderBottom(new SolidBorder(1)));
            table.AddCell(new Cell().Add(new Paragraph("Discrepancy:").SetFont(boldFont)).SetBorderBottom(new SolidBorder(1)));
            table.AddCell(new Cell().Add(new Paragraph(model.Discrepancy).SetFont(normalFont)).SetBorderBottom(new SolidBorder(1)));
            table.AddCell(new Cell().Add(new Paragraph("Operator:").SetFont(boldFont)).SetBorderBottom(new SolidBorder(1)));
            table.AddCell(new Cell().Add(new Paragraph(model.Operator).SetFont(normalFont)).SetBorderBottom(new SolidBorder(1)));
            table.AddCell(new Cell().Add(new Paragraph("Communication Method:").SetFont(boldFont)).SetBorderBottom(new SolidBorder(1)));
            table.AddCell(new Cell().Add(new Paragraph(model.CommMethod).SetFont(normalFont)).SetBorderBottom(new SolidBorder(1)));
            document.Add(table);

            // Add Fields for Disposition and Approved By
            document.Add(new Paragraph("\nDisposition: ________________________________").SetFont(normalFont).SetFontSize(12));
            document.Add(new Paragraph("\nApproved By: ________________________________").SetFont(normalFont).SetFontSize(12));

            // Footer
            document.Add(new Paragraph("\nPLACE ONE COPY OF DEVIATION WITH PAPERWORK AND PLACE ONE IN QUALITY CONTROL")
                .SetFont(boldFont)
                .SetTextAlignment(TextAlignment.CENTER));
            document.Add(new Paragraph("REVIEWED BY C.A.S.T")
                .SetFont(boldFont)
                .SetTextAlignment(TextAlignment.CENTER));
        }

        PrintPdfWithSumatraPDF(filePath);
        return filePath;
    }

    private void PrintPdfWithSumatraPDF(string pdfPath)
    {
        string sumatraPdfPath = @"C:\Tools\SumatraPDF\SumatraPDF.exe";
            string printerName = "QC1"; // Specify the target printer name
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
