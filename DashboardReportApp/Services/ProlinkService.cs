using iText.IO.Font.Constants;
using iText.Kernel.Colors;
using iText.Kernel.Font;
using iText.Kernel.Geom;
using iText.Kernel.Pdf;
using iText.Layout;
using iText.Layout.Borders;
using iText.Layout.Element;
using iText.Layout.Properties;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace DashboardReportApp.Services
{
    // Represents a raw measurement record returned from the database.
    public class MeasurementRecord
    {
        public int PartId { get; set; }
        public int RecordNumber { get; set; }
        public DateTime MeasureDate { get; set; }
        public string QccFileDesc { get; set; }
        public string Dimension { get; set; }
        public string MeasurementValue { get; set; }
        public string Factors { get; set; }

        // New properties for tolerance information.
        public double Nominal { get; set; }
        public double TolPlus { get; set; }
        public double TolMinus { get; set; }
    }

    // Represents a pivoted record per part.
    public class PartMeasurementPivot
    {
        public int PartId { get; set; }
        public int RecordNumber { get; set; }
        public DateTime MeasureDate { get; set; }
        public string QccFileDesc { get; set; }
        public Dictionary<string, string> Measurements { get; set; } = new Dictionary<string, string>();
        public Dictionary<string, string> FactorValues { get; set; } = new Dictionary<string, string>();
    }

    public class ProlinkService
    {
        private readonly string connectionString;
        // Define allowed factors.
        private readonly List<string> allowedFactors = new List<string> { "Operator", "Mix Lot #", "Mix No.", "Press", "Machine" };

        public ProlinkService(IConfiguration configuration)
        {
            connectionString = configuration.GetConnectionString("SQLExpressConnection");
        }

        // Retrieve raw measurement records including tolerance info.
        public List<MeasurementRecord> GetMeasurementRecords(string partString, string type, DateTime? startDate, DateTime? endDate)
        {
            List<MeasurementRecord> records = new List<MeasurementRecord>();

            using (var connection = new SqlConnection(connectionString))
            {
                connection.Open();
                string sql = @"
SELECT TOP 1000  
    p.part_id,
    p.record_number,
    p.measure_date,
    qf.qcc_file_desc,
    d.dim_desc AS Dimension,
    CAST(m.value AS VARCHAR(50)) AS MeasurementValue,
    d.nominal,
    d.tol_plus,
    d.tol_minus
FROM dbo.part p
JOIN dbo.qcc_file qf 
    ON p.qcc_file_id = qf.qcc_file_id
JOIN dbo.measurement m 
    ON p.part_id = m.part_id
JOIN dbo.dimension d 
    ON m.dim_id = d.dim_id
WHERE 
    qf.qcc_file_desc LIKE '%' + @PartString + '%'
    AND (
         (@Type IS NOT NULL AND qf.qcc_file_desc LIKE '%' + @Type + '%')
         OR (@Type IS NULL AND (
                qf.qcc_file_desc LIKE '%MOLD%' OR 
                qf.qcc_file_desc LIKE '%SINT%' OR 
                qf.qcc_file_desc LIKE '%MACHIN%'
             ))
        )
    AND m.deleted_flag = 0
    AND (@StartDate IS NULL OR p.measure_date >= @StartDate)
    AND (@EndDate IS NULL OR p.measure_date <= @EndDate)";

                using (var command = new SqlCommand(sql, connection))
                {
                    command.Parameters.AddWithValue("@PartString", partString);
                    command.Parameters.AddWithValue("@Type", string.IsNullOrEmpty(type) ? (object)DBNull.Value : type);
                    command.Parameters.AddWithValue("@StartDate", startDate.HasValue ? (object)startDate.Value : DBNull.Value);
                    command.Parameters.AddWithValue("@EndDate", endDate.HasValue ? (object)endDate.Value : DBNull.Value);

                    using (var reader = command.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            records.Add(new MeasurementRecord
                            {
                                PartId = reader.GetInt32(reader.GetOrdinal("part_id")),
                                RecordNumber = reader.GetInt32(reader.GetOrdinal("record_number")),
                                MeasureDate = reader.GetDateTime(reader.GetOrdinal("measure_date")),
                                QccFileDesc = reader.GetString(reader.GetOrdinal("qcc_file_desc")),
                                Dimension = reader.GetString(reader.GetOrdinal("Dimension")),
                                MeasurementValue = reader.GetString(reader.GetOrdinal("MeasurementValue")),
                                Nominal = reader.GetDouble(reader.GetOrdinal("nominal")),
                                TolPlus = reader.GetDouble(reader.GetOrdinal("tol_plus")),
                                TolMinus = reader.GetDouble(reader.GetOrdinal("tol_minus"))
                            });
                        }
                    }
                }
            }
            return records;
        }

        // Pivot the raw measurement records into one row per part.
        public List<PartMeasurementPivot> PivotMeasurements(List<MeasurementRecord> rawRecords)
        {
            var grouped = rawRecords.GroupBy(r => new { r.PartId, r.RecordNumber, r.MeasureDate, r.QccFileDesc });
            List<PartMeasurementPivot> pivotList = new List<PartMeasurementPivot>();

            foreach (var group in grouped)
            {
                PartMeasurementPivot pivot = new PartMeasurementPivot
                {
                    PartId = group.Key.PartId,
                    RecordNumber = group.Key.RecordNumber,
                    MeasureDate = group.Key.MeasureDate,
                    QccFileDesc = group.Key.QccFileDesc,
                };

                foreach (var rec in group)
                {
                    pivot.Measurements[rec.Dimension] = rec.MeasurementValue;
                }
                pivotList.Add(pivot);
            }
            return pivotList;
        }

        // Populate factor values for each pivoted part.
        public void PopulateFactorValues(List<PartMeasurementPivot> pivotRecords)
        {
            using (var connection = new SqlConnection(connectionString))
            {
                connection.Open();
                foreach (var pivot in pivotRecords)
                {
                    string factorSql = @"
SELECT f.factor_desc, pf.value 
FROM dbo.part_factor pf
JOIN dbo.factor f ON pf.factor_id = f.factor_id
WHERE pf.part_id = @PartId";

                    using (var command = new SqlCommand(factorSql, connection))
                    {
                        command.Parameters.AddWithValue("@PartId", pivot.PartId);
                        using (var reader = command.ExecuteReader())
                        {
                            while (reader.Read())
                            {
                                string factorDesc = reader.GetString(reader.GetOrdinal("factor_desc"));
                                if (!allowedFactors.Contains(factorDesc))
                                    continue;
                                string value = reader.IsDBNull(reader.GetOrdinal("value"))
                                    ? ""
                                    : reader.GetString(reader.GetOrdinal("value"));
                                pivot.FactorValues[factorDesc] = value;
                            }
                        }
                    }
                }
            }
        }

        // Generates a PDF report.
        public byte[] GeneratePdf(string partString, string type, DateTime? startDate, DateTime? endDate)
        {
            using (var ms = new MemoryStream())
            {
                PdfWriter writer = new PdfWriter(ms);
                PdfDocument pdfDoc = new PdfDocument(writer);
                Document document = new Document(pdfDoc, PageSize.A4.Rotate());

                PdfFont boldFont = PdfFontFactory.CreateFont(StandardFonts.HELVETICA_BOLD);
                PdfFont regularFont = PdfFontFactory.CreateFont(StandardFonts.HELVETICA);

                document.Add(new Paragraph("Prolink Report").SetFont(boldFont).SetFontSize(16));
                document.Add(new Paragraph("Part: " + partString).SetFont(regularFont));
                document.Add(new Paragraph("Date Range: " +
                    (startDate.HasValue ? startDate.Value.ToShortDateString() : "All") + " to " +
                    (endDate.HasValue ? endDate.Value.ToShortDateString() : "All")).SetFont(regularFont));
                document.Add(new Paragraph("\n"));

                List<string> departments;
                if (string.IsNullOrEmpty(type))
                    departments = new List<string> { "mold", "sint", "machin" };
                else
                    departments = new List<string> { type };

                foreach (var dept in departments)
                {
                    string deptDisplay = dept.Equals("mold", StringComparison.OrdinalIgnoreCase) ? "Molding" :
                                         dept.Equals("sint", StringComparison.OrdinalIgnoreCase) ? "Sintering" :
                                         dept.Equals("machin", StringComparison.OrdinalIgnoreCase) ? "Machining" : dept;
                    document.Add(new Paragraph("Department: " + deptDisplay).SetFont(boldFont).SetFontSize(14));
                    document.Add(new Paragraph("\n"));

                    var rawRecords = GetMeasurementRecords(partString, dept, startDate, endDate);
                    var pivotRecords = PivotMeasurements(rawRecords);
                    PopulateFactorValues(pivotRecords);

                    var measurementColumns = rawRecords.Select(r => r.Dimension)
                                                       .Distinct()
                                                       .OrderBy(d => d)
                                                       .Where(dim => pivotRecords.Any(p =>
                                                           p.Measurements.TryGetValue(dim, out var val) &&
                                                           !string.IsNullOrWhiteSpace(val)))
                                                       .ToList();

                    var factorColumns = allowedFactors.Where(factor => pivotRecords.Any(p =>
                        p.FactorValues.ContainsKey(factor) &&
                        !string.IsNullOrWhiteSpace(p.FactorValues[factor])))
                                                       .ToList();

                    // Compute maximum and minimum for each measurement dimension.
                    Dictionary<string, (double max, double min)> measurementStats = new Dictionary<string, (double, double)>();
                    foreach (var dim in measurementColumns)
                    {
                        var values = pivotRecords
                            .Select(pr =>
                            {
                                if (pr.Measurements.TryGetValue(dim, out var val) && double.TryParse(val, out double d))
                                    return (double?)d;
                                return null;
                            })
                            .Where(x => x.HasValue)
                            .Select(x => x.Value)
                            .ToList();
                        if (values.Any())
                        {
                            measurementStats[dim] = (values.Max(), values.Min());
                        }
                    }

                    // Map each measurement dimension to its tolerance information.
                    Dictionary<string, (double nominal, double tolPlus, double tolMinus)> dimensionTolerances = new Dictionary<string, (double, double, double)>();
                    foreach (var record in rawRecords)
                    {
                        var dim = record.Dimension;
                        if (!dimensionTolerances.ContainsKey(dim))
                        {
                            dimensionTolerances[dim] = (record.Nominal, record.TolPlus, record.TolMinus);
                        }
                    }

                    int commonColumns = 2; // Record and Date/Time.
                    int totalColumns = commonColumns + measurementColumns.Count + factorColumns.Count;
                    Table table = new Table(UnitValue.CreatePercentArray(totalColumns));
                    table.SetWidth(UnitValue.CreatePercentValue(100));
                    table.SetMarginTop(10);

                    // ----- Build top header row: Extra Info (Tolerance and Stats) -----
                    // For common columns, add empty cells.
                    for (int i = 0; i < commonColumns; i++)
                    {
                        table.AddHeaderCell(new Cell()
                            .Add(new Paragraph(""))
                            .SetBackgroundColor(ColorConstants.LIGHT_GRAY)
                            .SetTextAlignment(TextAlignment.CENTER)
                            .SetPadding(5)
                            .SetBorder(new SolidBorder(1)));
                    }
                    // For each measurement column, add extra info cell.
                    foreach (var dim in measurementColumns)
                    {
                        string extraText = "";
                        if (dimensionTolerances.ContainsKey(dim))
                        {
                            var tol = dimensionTolerances[dim];
                            double usl = tol.nominal + tol.tolPlus;
                            double lsl = tol.nominal + tol.tolMinus;
                            extraText = $"USL: {usl:F4}\nLSL: {lsl:F4}";

                        }
                        if (measurementStats.ContainsKey(dim))
                        {
                            var stats = measurementStats[dim];
                            extraText += $"\nMax: {stats.max:F4}\nMin: {stats.min:F4}";

                        }
                        table.AddHeaderCell(new Cell()
                            .Add(new Paragraph(extraText))
                            .SetBackgroundColor(ColorConstants.LIGHT_GRAY)
                            .SetTextAlignment(TextAlignment.CENTER)
                            .SetPadding(5)
                            .SetBorder(new SolidBorder(1)));
                    }
                    // For factor columns, add empty cells.
                    foreach (var factor in factorColumns)
                    {
                        table.AddHeaderCell(new Cell()
                            .Add(new Paragraph(""))
                            .SetBackgroundColor(ColorConstants.LIGHT_GRAY)
                            .SetTextAlignment(TextAlignment.CENTER)
                            .SetPadding(5)
                            .SetBorder(new SolidBorder(1)));
                    }

                    // ----- Build second header row: Column Labels -----
                    // Common column labels.
                    table.AddHeaderCell(new Cell()
                        .Add(new Paragraph("Record").SetFont(boldFont))
                        .SetBackgroundColor(ColorConstants.LIGHT_GRAY)
                        .SetTextAlignment(TextAlignment.CENTER)
                        .SetPadding(5)
                        .SetBorder(new SolidBorder(1)));
                    table.AddHeaderCell(new Cell()
                        .Add(new Paragraph("Date/Time").SetFont(boldFont))
                        .SetBackgroundColor(ColorConstants.LIGHT_GRAY)
                        .SetTextAlignment(TextAlignment.CENTER)
                        .SetPadding(5)
                        .SetBorder(new SolidBorder(1)));
                    // Measurement columns: dimension names.
                    foreach (var dim in measurementColumns)
                    {
                        table.AddHeaderCell(new Cell()
                            .Add(new Paragraph(dim).SetFont(boldFont))
                            .SetBackgroundColor(ColorConstants.LIGHT_GRAY)
                            .SetTextAlignment(TextAlignment.CENTER)
                            .SetPadding(5)
                            .SetBorder(new SolidBorder(1)));
                    }
                    // Factor columns: factor names.
                    foreach (var factor in factorColumns)
                    {
                        table.AddHeaderCell(new Cell()
                            .Add(new Paragraph(factor).SetFont(boldFont))
                            .SetBackgroundColor(ColorConstants.LIGHT_GRAY)
                            .SetTextAlignment(TextAlignment.CENTER)
                            .SetPadding(5)
                            .SetBorder(new SolidBorder(1)));
                    }

                    // ----- Add data rows -----
                    foreach (var pivot in pivotRecords)
                    {
                        table.AddCell(new Cell()
                            .Add(new Paragraph(pivot.RecordNumber.ToString()))
                            .SetPadding(5)
                            .SetBorder(new SolidBorder(1)));
                        table.AddCell(new Cell()
                            .Add(new Paragraph(pivot.MeasureDate.ToString("g")))
                            .SetPadding(5)
                            .SetTextAlignment(TextAlignment.CENTER)
                            .SetBorder(new SolidBorder(1)));

                        foreach (var dim in measurementColumns)
                        {
                            string measVal = pivot.Measurements.ContainsKey(dim) ? pivot.Measurements[dim] : "";
                            Paragraph p = new Paragraph(measVal);

                            // Check if the measurement value can be parsed and tolerance info exists.
                            if (!string.IsNullOrWhiteSpace(measVal) && double.TryParse(measVal, out double mValue))
                            {
                                if (dimensionTolerances.ContainsKey(dim))
                                {
                                    var tol = dimensionTolerances[dim];
                                    // Calculate USL and LSL.
                                    double usl = tol.nominal + tol.tolPlus;
                                    double lsl = tol.nominal + tol.tolMinus;
                                    // If value is outside tolerance, set text color to red.
                                    if (mValue > usl || mValue < lsl)
                                    {
                                        p.SetFontColor(ColorConstants.RED);
                                    }
                                }
                            }

                            table.AddCell(new Cell()
                                .Add(p)
                                .SetPadding(5)
                                .SetTextAlignment(TextAlignment.RIGHT)
                                .SetBorder(new SolidBorder(1)));
                        }

                        foreach (var factor in factorColumns)
                        {
                            string factVal = pivot.FactorValues.ContainsKey(factor) ? pivot.FactorValues[factor] : "";
                            table.AddCell(new Cell()
                                .Add(new Paragraph(factVal))
                                .SetPadding(5)
                                .SetTextAlignment(TextAlignment.RIGHT)
                                .SetBorder(new SolidBorder(1)));
                        }
                    }


                    document.Add(table);

                    if (departments.Count > 1 && dept != departments.Last())
                    {
                        document.Add(new AreaBreak(AreaBreakType.NEXT_PAGE));
                    }
                }

                document.Close();
                return ms.ToArray();
            }
        }
    }
}
