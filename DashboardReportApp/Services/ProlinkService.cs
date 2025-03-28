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
    // New model for corrective actions
    public class CorrectiveAction
    {
        public int ActionId { get; set; }
        public string ActionRef { get; set; }
        public string ActionDesc { get; set; }
    }

    // Represents a raw measurement record.
    public class MeasurementRecord
    {
        public int PartId { get; set; }
        public int RecordNumber { get; set; }
        public DateTime MeasureDate { get; set; }
        public string QccFileDesc { get; set; }
        public string Dimension { get; set; }
        public string MeasurementValue { get; set; }
        public string Factors { get; set; }
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
        // Corrective actions for this record.
        public List<CorrectiveAction> CorrectiveActions { get; set; } = new List<CorrectiveAction>();
    }

    // For returning dimension-level stats.
    public class DimensionStats
    {
        public double Usl { get; set; }
        public double Lsl { get; set; }
        public double Max { get; set; }
        public double Min { get; set; }
    }

    // Container grouping a department's data.
    public class PivotedDepartmentResult
    {
        // DepartmentName is already mapped to a full name (e.g., "Molding")
        public string DepartmentName { get; set; }
        public List<string> DimensionColumns { get; set; } = new List<string>();
        public List<string> FactorColumns { get; set; } = new List<string>();
        public List<PartMeasurementPivot> Rows { get; set; } = new List<PartMeasurementPivot>();
        public Dictionary<string, DimensionStats> DimensionStats { get; set; } = new Dictionary<string, DimensionStats>();
    }

    public class ProlinkService
    {
        private readonly string connectionString;
        private readonly List<string> allowedFactors = new List<string>
        {
            "Operator", "Mix Lot #", "Mix No.", "Press", "Machine", "Oven"
        };

        public ProlinkService(IConfiguration configuration)
        {
            connectionString = configuration.GetConnectionString("SQLExpressConnection");
        }

        // 1) Retrieve raw measurement records.
        public List<MeasurementRecord> GetMeasurementRecords(
            string partString,
            string type,
            DateTime? startDate,
            DateTime? endDate)
        {
            List<MeasurementRecord> records = new List<MeasurementRecord>();
            using (var connection = new SqlConnection(connectionString))
            {
                connection.Open();
                string sql = @"
SELECT   
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
JOIN dbo.qcc_file qf ON p.qcc_file_id = qf.qcc_file_id
JOIN dbo.measurement m ON p.part_id = m.part_id
JOIN dbo.dimension d ON m.dim_id = d.dim_id
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
    AND (@EndDate IS NULL OR p.measure_date <= @EndDate)
ORDER BY p.measure_date desc";
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

        // 2) Pivot measurements.
        public List<PartMeasurementPivot> PivotMeasurements(List<MeasurementRecord> rawRecords)
        {
            var grouped = rawRecords.GroupBy(r => new
            {
                r.PartId,
                r.RecordNumber,
                r.MeasureDate,
                r.QccFileDesc
            });
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

        // 3) Populate factor values.
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

        // 4) Populate corrective actions via the stored procedure.
        public void PopulateCorrectiveActions(List<PartMeasurementPivot> pivotRecords, DateTime? startDate, DateTime? endDate, string department)
        {
            using (var connection = new SqlConnection(connectionString))
            {
                connection.Open();
                foreach (var pivot in pivotRecords)
                {
                    string sql = "EXEC dbo.GetCorrectiveActionsForRecordOrDepartment @recordNumber, @department, @startDate, @endDate";
                    using (var command = new SqlCommand(sql, connection))
                    {
                        command.Parameters.AddWithValue("@recordNumber", pivot.RecordNumber);
                        command.Parameters.AddWithValue("@department", string.IsNullOrEmpty(department) ? DBNull.Value : (object)department);
                        command.Parameters.AddWithValue("@startDate", startDate.HasValue ? (object)startDate.Value : DBNull.Value);
                        command.Parameters.AddWithValue("@endDate", endDate.HasValue ? (object)endDate.Value : DBNull.Value);
                        using (var reader = command.ExecuteReader())
                        {
                            while (reader.Read())
                            {
                                pivot.CorrectiveActions.Add(new CorrectiveAction
                                {
                                    ActionId = reader.GetInt32(0),
                                    ActionRef = reader.IsDBNull(1) ? "" : reader.GetString(1),
                                    ActionDesc = reader.GetString(2)
                                });
                            }
                        }
                    }
                }
            }
        }

        // 5) Get pivoted data for departments (with department name mapping).
        public List<PivotedDepartmentResult> GetPivotedData(
            string partString,
            string departmentParam,
            DateTime? startDate,
            DateTime? endDate,
            bool onlyOutOfSpec,
            bool includeCorrectiveActions)
        {
            List<string> departments;
            if (string.IsNullOrEmpty(departmentParam))
                departments = new List<string> { "mold", "sint", "machin" };
            else
                departments = new List<string> { departmentParam };

            var results = new List<PivotedDepartmentResult>();

            foreach (string dept in departments)
            {
                // Map department code to full name.
                string displayDept = dept.Equals("mold", StringComparison.OrdinalIgnoreCase) ? "Molding" :
                                     dept.Equals("sint", StringComparison.OrdinalIgnoreCase) ? "Sintering" :
                                     dept.Equals("machin", StringComparison.OrdinalIgnoreCase) ? "Machining" : dept;

                var rawRecords = GetMeasurementRecords(partString, dept, startDate, endDate);
                if (onlyOutOfSpec)
                {
                    var grouped = rawRecords.GroupBy(r => new { r.PartId, r.RecordNumber });
                    var filteredGroups = grouped.Where(g =>
                        g.Any(r =>
                        {
                            if (double.TryParse(r.MeasurementValue, out double meas))
                            {
                                meas = Math.Round(meas, 4);
                                double nom = Math.Round(r.Nominal, 4);
                                double plus = Math.Round(r.TolPlus, 4);
                                double minus = Math.Round(r.TolMinus, 4);
                                double usl = nom + plus;
                                double lsl = nom + minus;
                                return (meas > usl || meas < lsl);
                            }
                            return false;
                        })
                    );
                    rawRecords = filteredGroups.SelectMany(x => x).ToList();
                }

                if (!rawRecords.Any())
                {
                    results.Add(new PivotedDepartmentResult
                    {
                        DepartmentName = displayDept,
                        DimensionColumns = new List<string>(),
                        FactorColumns = new List<string>(),
                        Rows = new List<PartMeasurementPivot>(),
                        DimensionStats = new Dictionary<string, DimensionStats>()
                    });
                    continue;
                }

                var pivotRows = PivotMeasurements(rawRecords);
                PopulateFactorValues(pivotRows);
                if (includeCorrectiveActions)
                {
                    PopulateCorrectiveActions(pivotRows, startDate, endDate, dept);
                }
                var dimensionColumns = pivotRows
                    .SelectMany(p => p.Measurements.Keys)
                    .Distinct()
                    .OrderBy(d => d)
                    .ToList();

                var factorColumns = allowedFactors
                    .Where(f => pivotRows.Any(p =>
                        p.FactorValues.ContainsKey(f) &&
                        !string.IsNullOrWhiteSpace(p.FactorValues[f])
                    ))
                    .ToList();

                var dimTolerances = new Dictionary<string, (double nom, double plus, double minus)>();
                foreach (var rr in rawRecords)
                {
                    if (!dimTolerances.ContainsKey(rr.Dimension))
                    {
                        dimTolerances[rr.Dimension] = (rr.Nominal, rr.TolPlus, rr.TolMinus);
                    }
                }

                var dimensionStats = new Dictionary<string, DimensionStats>();
                foreach (var dim in dimensionColumns)
                {
                    double? maxVal = null;
                    double? minVal = null;
                    foreach (var pr in pivotRows)
                    {
                        if (pr.Measurements.TryGetValue(dim, out string strVal) &&
                            double.TryParse(strVal, out double dblVal))
                        {
                            if (!maxVal.HasValue || dblVal > maxVal.Value)
                                maxVal = dblVal;
                            if (!minVal.HasValue || dblVal < minVal.Value)
                                minVal = dblVal;
                        }
                    }
                    double usl = 0.0, lsl = 0.0;
                    if (dimTolerances.TryGetValue(dim, out var t))
                    {
                        usl = t.nom + t.plus;
                        lsl = t.nom + t.minus;
                    }
                    dimensionStats[dim] = new DimensionStats
                    {
                        Usl = usl,
                        Lsl = lsl,
                        Max = maxVal ?? 0.0,
                        Min = minVal ?? 0.0
                    };
                }

                var deptResult = new PivotedDepartmentResult
                {
                    DepartmentName = displayDept,
                    DimensionColumns = dimensionColumns,
                    FactorColumns = factorColumns,
                    Rows = pivotRows,
                    DimensionStats = dimensionStats
                };

                results.Add(deptResult);
            }
            return results;
        }

        // 6) Generate PDF.
        public byte[] GeneratePdf(
            string partString,
            string type,
            DateTime? startDate,
            DateTime? endDate,
            bool onlyOutOfSpec,
            bool includeCorrectiveActions)
        {
            using (var ms = new MemoryStream())
            {
                var writer = new PdfWriter(ms);
                var pdfDoc = new PdfDocument(writer);
                var document = new Document(pdfDoc, PageSize.A4.Rotate());

                PdfFont boldFont = PdfFontFactory.CreateFont(StandardFonts.HELVETICA_BOLD);
                PdfFont regularFont = PdfFontFactory.CreateFont(StandardFonts.HELVETICA);
                float smallFontSize = 8f;

                document.Add(new Paragraph("Prolink Report")
                    .SetFont(boldFont)
                    .SetFontSize(12));

                document.Add(new Paragraph($"Part: {(partString ?? "").ToUpper()}")
                    .SetFont(regularFont)
                    .SetFontSize(smallFontSize));

                string dateRange =
                    $"{(startDate.HasValue ? startDate.Value.ToShortDateString() : "All")} to " +
                    $"{(endDate.HasValue ? endDate.Value.ToShortDateString() : "All")}";
                document.Add(new Paragraph($"Date Range: {dateRange}")
                    .SetFont(regularFont)
                    .SetFontSize(smallFontSize));

                document.Add(new Paragraph($"Only Out-of-Spec: {(onlyOutOfSpec ? "Yes" : "No")}")
                    .SetFont(regularFont)
                    .SetFontSize(smallFontSize));

                document.Add(new Paragraph("\n"));

                List<string> departments;
                if (string.IsNullOrEmpty(type))
                {
                    departments = new List<string> { "mold", "sint", "machin" };
                }
                else
                {
                    departments = new List<string> { type };
                }

                bool firstDept = true;
                foreach (string dept in departments)
                {
                    if (!firstDept)
                    {
                        document.Add(new AreaBreak(AreaBreakType.NEXT_PAGE));
                    }
                    firstDept = false;

                    // Map department code to full name.
                    string deptDisplay = dept.Equals("mold", StringComparison.OrdinalIgnoreCase)
                        ? "Molding"
                        : dept.Equals("sint", StringComparison.OrdinalIgnoreCase)
                            ? "Sintering"
                            : dept.Equals("machin", StringComparison.OrdinalIgnoreCase)
                                ? "Machining"
                                : dept;
                    document.Add(new Paragraph($"Department: {deptDisplay}")
                        .SetFont(boldFont)
                        .SetFontSize(smallFontSize));

                    var rawRecords = GetMeasurementRecords(partString, dept, startDate, endDate);
                    if (onlyOutOfSpec)
                    {
                        var grouped = rawRecords.GroupBy(r => new { r.PartId, r.RecordNumber });
                        var filteredGroups = grouped.Where(grp =>
                        {
                            return grp.Any(r =>
                            {
                                if (double.TryParse(r.MeasurementValue, out double meas))
                                {
                                    meas = Math.Round(meas, 4);
                                    double nom = Math.Round(r.Nominal, 4);
                                    double plus = Math.Round(r.TolPlus, 4);
                                    double minus = Math.Round(r.TolMinus, 4);
                                    double usl = nom + plus;
                                    double lsl = nom + minus;
                                    return (meas > usl || meas < lsl);
                                }
                                return false;
                            });
                        });
                        rawRecords = filteredGroups.SelectMany(g => g).ToList();
                    }

                    if (!rawRecords.Any())
                    {
                        document.Add(new Paragraph("No records found for this department.")
                            .SetFont(regularFont)
                            .SetFontSize(smallFontSize));
                        continue;
                    }

                    var pivotRecords = PivotMeasurements(rawRecords);
                    PopulateFactorValues(pivotRecords);
                    if (includeCorrectiveActions)
                    {
                        PopulateCorrectiveActions(pivotRecords, startDate, endDate, dept);
                    }

                    if (!pivotRecords.Any())
                    {
                        document.Add(new Paragraph("No records found for this department.")
                            .SetFont(regularFont)
                            .SetFontSize(smallFontSize));
                        continue;
                    }

                    var measurementColumns = pivotRecords
                        .SelectMany(p => p.Measurements.Keys)
                        .Distinct()
                        .OrderBy(d => d)
                        .ToList();

                    var factorColumns = allowedFactors
                        .Where(f => pivotRecords.Any(p =>
                            p.FactorValues.ContainsKey(f) &&
                            !string.IsNullOrWhiteSpace(p.FactorValues[f])))
                        .ToList();

                    var measurementStats = new Dictionary<string, (double max, double min)>();
                    foreach (var dim in measurementColumns)
                    {
                        var vals = pivotRecords
                            .Select(p =>
                            {
                                if (p.Measurements.TryGetValue(dim, out string s) &&
                                    double.TryParse(s, out double d))
                                    return (double?)d;
                                return null;
                            })
                            .Where(x => x.HasValue)
                            .Select(x => x.Value)
                            .ToList();

                        if (vals.Any())
                            measurementStats[dim] = (vals.Max(), vals.Min());
                    }

                    var dimensionTolerances = new Dictionary<string, (double nominal, double plus, double minus)>();
                    foreach (var rec in rawRecords)
                    {
                        var dim = rec.Dimension;
                        if (!dimensionTolerances.ContainsKey(dim))
                        {
                            dimensionTolerances[dim] = (rec.Nominal, rec.TolPlus, rec.TolMinus);
                        }
                    }

                    BuildDepartmentTable(
                        document,
                        pivotRecords,
                        measurementColumns,
                        factorColumns,
                        measurementStats,
                        dimensionTolerances,
                        boldFont,
                        regularFont,
                        smallFontSize
                    );
                }

                document.Close();
                return ms.ToArray();
            }
        }

        // 7) BuildDepartmentTable: constructs the PDF table and appends a corrective actions row after each record row.
        private void BuildDepartmentTable(
     Document document,
     List<PartMeasurementPivot> pivotRecords,
     List<string> measurementColumns,
     List<string> factorColumns,
     Dictionary<string, (double max, double min)> measurementStats,
     Dictionary<string, (double nominal, double plus, double minus)> dimensionTolerances,
     PdfFont boldFont,
     PdfFont regularFont,
     float smallFontSize)
        {
            int commonCols = 2;
            int measCount = measurementColumns.Count;
            int factorCount = factorColumns.Count;
            int totalCols = commonCols + measCount + factorCount;
            float[] colWidths = new float[totalCols];
            colWidths[0] = 1f;
            colWidths[1] = 3f;
            for (int i = 2; i < totalCols; i++)
                colWidths[i] = 1f;

            var table = new Table(colWidths)
                .SetWidth(UnitValue.CreatePercentValue(100))
                .SetMarginTop(10);

            string[] rowLabels = { "USL", "LSL", "Max", "Min" };
            foreach (var label in rowLabels)
            {
                table.AddHeaderCell(new Cell()
                    .Add(new Paragraph(label).SetFont(boldFont).SetFontSize(smallFontSize))
                    .SetBackgroundColor(ColorConstants.LIGHT_GRAY)
                    .SetTextAlignment(TextAlignment.CENTER)
                    .SetPadding(3)
                    .SetBorder(new SolidBorder(1)));
                table.AddHeaderCell(new Cell()
                    .Add(new Paragraph("").SetFont(regularFont).SetFontSize(smallFontSize))
                    .SetBackgroundColor(ColorConstants.LIGHT_GRAY)
                    .SetTextAlignment(TextAlignment.CENTER)
                    .SetPadding(3)
                    .SetBorder(new SolidBorder(1)));
                foreach (var dim in measurementColumns)
                {
                    string cellText = "";
                    if (dimensionTolerances.TryGetValue(dim, out var tol))
                    {
                        double nom = tol.nominal;
                        double plus = tol.plus;
                        double minus = tol.minus;
                        double usl = nom + plus;
                        double lsl = nom + minus;

                        if (label == "USL")
                            cellText = usl.ToString("F4");
                        else if (label == "LSL")
                            cellText = lsl.ToString("F4");
                        else if (label == "Max" && measurementStats.ContainsKey(dim))
                            cellText = measurementStats[dim].max.ToString("F4");
                        else if (label == "Min" && measurementStats.ContainsKey(dim))
                            cellText = measurementStats[dim].min.ToString("F4");
                    }
                    table.AddHeaderCell(new Cell()
                        .Add(new Paragraph(cellText).SetFont(regularFont).SetFontSize(smallFontSize))
                        .SetBackgroundColor(ColorConstants.LIGHT_GRAY)
                        .SetTextAlignment(TextAlignment.CENTER)
                        .SetPadding(3)
                        .SetBorder(new SolidBorder(1)));
                }
                for (int i = 0; i < factorCount; i++)
                {
                    table.AddHeaderCell(new Cell()
                        .Add(new Paragraph("").SetFont(regularFont).SetFontSize(smallFontSize))
                        .SetBackgroundColor(ColorConstants.LIGHT_GRAY)
                        .SetTextAlignment(TextAlignment.CENTER)
                        .SetPadding(3)
                        .SetBorder(new SolidBorder(1)));
                }
            }

            // Main header row: "Record", "Date/Time", then dimension and factor columns.
            string[] commonHeaders = { "Record", "Date/Time" };
            for (int i = 0; i < totalCols; i++)
            {
                string text;
                if (i < commonCols)
                    text = commonHeaders[i];
                else if (i < commonCols + measCount)
                    text = measurementColumns[i - commonCols];
                else
                    text = factorColumns[i - commonCols - measCount];

                table.AddHeaderCell(new Cell()
                    .Add(new Paragraph(text).SetFont(boldFont).SetFontSize(smallFontSize))
                    .SetBackgroundColor(ColorConstants.LIGHT_GRAY)
                    .SetTextAlignment(TextAlignment.CENTER)
                    .SetPadding(3)
                    .SetBorder(new SolidBorder(1)));
            }

            // Data rows with corrective actions row appended per record if available.
            foreach (var pivot in pivotRecords)
            {
                table.AddCell(new Cell()
                    .Add(new Paragraph(pivot.RecordNumber.ToString()).SetFont(regularFont).SetFontSize(smallFontSize))
                    .SetPadding(3)
                    .SetBorder(new SolidBorder(1)));
                table.AddCell(new Cell()
                    .Add(new Paragraph(pivot.MeasureDate.ToString("g")).SetFont(regularFont).SetFontSize(smallFontSize))
                    .SetTextAlignment(TextAlignment.CENTER)
                    .SetPadding(3)
                    .SetBorder(new SolidBorder(1)));
                foreach (var dim in measurementColumns)
                {
                    pivot.Measurements.TryGetValue(dim, out var val);
                    if (string.IsNullOrWhiteSpace(val)) val = "";
                    var paragraph = new Paragraph(val).SetFont(regularFont).SetFontSize(smallFontSize);
                    if (dimensionTolerances.ContainsKey(dim) && double.TryParse(val, out double doubleVal))
                    {
                        var (nom, plus, minus) = dimensionTolerances[dim];
                        double usl = nom + plus;
                        double lsl = nom + minus;
                        if (doubleVal > usl || doubleVal < lsl)
                        {
                            paragraph.SetFontColor(ColorConstants.RED);
                        }
                    }
                    table.AddCell(new Cell()
                        .Add(paragraph)
                        .SetTextAlignment(TextAlignment.RIGHT)
                        .SetPadding(3)
                        .SetBorder(new SolidBorder(1)));
                }
                foreach (var factor in factorColumns)
                {
                    pivot.FactorValues.TryGetValue(factor, out var fVal);
                    if (string.IsNullOrWhiteSpace(fVal)) fVal = "";
                    table.AddCell(new Cell()
                        .Add(new Paragraph(fVal).SetFont(regularFont).SetFontSize(smallFontSize))
                        .SetTextAlignment(TextAlignment.RIGHT)
                        .SetPadding(3)
                        .SetBorder(new SolidBorder(1)));
                }
                // Append corrective actions row if available.
                if (pivot.CorrectiveActions != null && pivot.CorrectiveActions.Any())
                {
                    string caText = "Corrective Actions: " + string.Join("; ",
                        pivot.CorrectiveActions.Select(a => a.ActionRef + ": " + a.ActionDesc));
                    // Note: new Cell(1, totalCols) creates a cell that spans 1 row and totalCols columns.
                    table.AddCell(new Cell(1, totalCols)
                        .Add(new Paragraph(caText).SetFont(regularFont).SetFontSize(smallFontSize))
                        .SetTextAlignment(TextAlignment.LEFT)
                        .SetBackgroundColor(ColorConstants.YELLOW)
                        .SetBorder(new SolidBorder(1)));
                }
            }

            document.Add(table);
        }

    }
}