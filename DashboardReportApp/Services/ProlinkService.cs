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

    // Represents a pivoted record per part (one row per PartId/RecordNumber).
    public class PartMeasurementPivot
    {
        public int PartId { get; set; }
        public int RecordNumber { get; set; }
        public DateTime MeasureDate { get; set; }
        public string QccFileDesc { get; set; }

        // dimension -> measurement value
        public Dictionary<string, string> Measurements { get; set; } = new Dictionary<string, string>();

        // factor -> factor value
        public Dictionary<string, string> FactorValues { get; set; } = new Dictionary<string, string>();
    }

    // For returning dimension-level stats to match your PDF row approach.
    public class DimensionStats
    {
        public double Usl { get; set; }
        public double Lsl { get; set; }
        public double Max { get; set; }
        public double Min { get; set; }
    }

    // For convenience, a container that groups everything about a single department's data.
    public class PivotedDepartmentResult
    {
        public string DepartmentName { get; set; }
        public List<string> DimensionColumns { get; set; } = new List<string>();
        public List<string> FactorColumns { get; set; } = new List<string>();
        public List<PartMeasurementPivot> Rows { get; set; } = new List<PartMeasurementPivot>();

        // dimension -> (usl, lsl, max, min)
        public Dictionary<string, DimensionStats> DimensionStats { get; set; }
            = new Dictionary<string, DimensionStats>();
    }

    public class ProlinkService
    {
        private readonly string connectionString;

        // Define allowed factors (only these factor_desc values will appear as columns).
        private readonly List<string> allowedFactors = new List<string>
        {
            "Operator", "Mix Lot #", "Mix No.", "Press", "Machine", "Oven"
        };

        public ProlinkService(IConfiguration configuration)
        {
            connectionString = configuration.GetConnectionString("SQLExpressConnection");
        }

        // 1) Retrieve raw measurement records including tolerance info.
        public List<MeasurementRecord> GetMeasurementRecords(
            string partString,
            string type,
            DateTime? startDate,
            DateTime? endDate
        )
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

        // 2) Group raw measurement records into pivoted structures.
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

                // Accumulate dimension -> measurement for that part
                foreach (var rec in group)
                {
                    pivot.Measurements[rec.Dimension] = rec.MeasurementValue;
                }

                pivotList.Add(pivot);
            }
            return pivotList;
        }

        // 3) Populate factor values for each pivoted part.
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

        // 4) MAIN UTILITY:
        //    Return pivoted data with dimension stats for each relevant department,
        //    i.e. EXACTLY the data structure you'd want to replicate the PDF format in JSON.
        public List<PivotedDepartmentResult> GetPivotedData(
            string partString,
            string departmentParam,
            DateTime? startDate,
            DateTime? endDate,
            bool onlyOutOfSpec
        )
        {
            // If user didn't specify department, do "mold, sint, machin"
            List<string> departments;
            if (string.IsNullOrEmpty(departmentParam))
                departments = new List<string> { "mold", "sint", "machin" };
            else
                departments = new List<string> { departmentParam };

            var results = new List<PivotedDepartmentResult>();

            foreach (string dept in departments)
            {
                // 1) Get raw records
                var rawRecords = GetMeasurementRecords(partString, dept, startDate, endDate);

                // 2) If onlyOutOfSpec is requested, keep records in any group that has an OOS measurement
                if (onlyOutOfSpec)
                {
                    // group by (partId, recordNumber)
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

                // If no raw records remain, we can just note it & continue
                if (!rawRecords.Any())
                {
                    results.Add(new PivotedDepartmentResult
                    {
                        DepartmentName = dept,
                        DimensionColumns = new List<string>(),
                        FactorColumns = new List<string>(),
                        Rows = new List<PartMeasurementPivot>(),
                        DimensionStats = new Dictionary<string, DimensionStats>()
                    });
                    continue;
                }

                // 3) Pivot
                var pivotRows = PivotMeasurements(rawRecords);

                // 4) Factor values
                PopulateFactorValues(pivotRows);

                // 5) Build dimension & factor columns
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

                // 6) Compute dimension-level stats (USL, LSL, Max, Min)
                //    First gather dimension tolerances from the raw data
                var dimTolerances = new Dictionary<string, (double nom, double plus, double minus)>();
                foreach (var rr in rawRecords)
                {
                    if (!dimTolerances.ContainsKey(rr.Dimension))
                    {
                        dimTolerances[rr.Dimension] = (rr.Nominal, rr.TolPlus, rr.TolMinus);
                    }
                }

                // Now compute max/min from pivoted rows
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

                // 7) Build the final pivoted result object
                var deptResult = new PivotedDepartmentResult
                {
                    DepartmentName = dept,
                    DimensionColumns = dimensionColumns,
                    FactorColumns = factorColumns,
                    Rows = pivotRows,
                    DimensionStats = dimensionStats
                };

                results.Add(deptResult);
            }

            return results;
        }

        // 5) Generate PDF exactly as you had before (unchanged).
        public byte[] GeneratePdf(
            string partString,
            string type,
            DateTime? startDate,
            DateTime? endDate,
            bool onlyOutOfSpec)
        {
            using (var ms = new MemoryStream())
            {
                var writer = new PdfWriter(ms);
                var pdfDoc = new PdfDocument(writer);
                var document = new Document(pdfDoc, PageSize.A4.Rotate());

                PdfFont boldFont = PdfFontFactory.CreateFont(StandardFonts.HELVETICA_BOLD);
                PdfFont regularFont = PdfFontFactory.CreateFont(StandardFonts.HELVETICA);
                float smallFontSize = 8f;

                // Overall report title
                document.Add(new Paragraph("Prolink Report")
                    .SetFont(boldFont)
                    .SetFontSize(12));

                // Basic filter info
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

                document.Add(new Paragraph("\n")); // extra blank line

                // 1) Determine which departments to process
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
                    // Page break between departments (but not before the first).
                    if (!firstDept)
                    {
                        document.Add(new AreaBreak(AreaBreakType.NEXT_PAGE));
                    }
                    firstDept = false;

                    // Show a department header
                    string deptDisplay = dept.Equals("mold", StringComparison.OrdinalIgnoreCase)
                        ? "Molding"
                        : dept.Equals("sint", StringComparison.OrdinalIgnoreCase)
                            ? "Sintering"
                            : dept.Equals("machin", StringComparison.OrdinalIgnoreCase)
                                ? "Machining"
                                : dept;  // fallback if user typed something else
                    document.Add(new Paragraph($"Department: {deptDisplay}")
                        .SetFont(boldFont)
                        .SetFontSize(smallFontSize));

                    // 2) Get all raw measurement rows
                    var rawRecords = GetMeasurementRecords(partString, dept, startDate, endDate);

                    // 3) If onlyOutOfSpec, keep entire records where ANY dimension is out-of-spec
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

                    // If no measurements remain
                    if (!rawRecords.Any())
                    {
                        document.Add(new Paragraph("No records found for this department.")
                            .SetFont(regularFont)
                            .SetFontSize(smallFontSize));
                        continue;
                    }

                    // 4) Pivot & factor
                    var pivotRecords = PivotMeasurements(rawRecords);
                    PopulateFactorValues(pivotRecords);

                    if (!pivotRecords.Any())
                    {
                        document.Add(new Paragraph("No records found for this department.")
                            .SetFont(regularFont)
                            .SetFontSize(smallFontSize));
                        continue;
                    }

                    // Identify dimension columns
                    var measurementColumns = pivotRecords
                        .SelectMany(p => p.Measurements.Keys)
                        .Distinct()
                        .OrderBy(d => d)
                        .ToList();

                    // Factor columns actually used
                    var factorColumns = allowedFactors
                        .Where(f => pivotRecords.Any(p =>
                            p.FactorValues.ContainsKey(f) &&
                            !string.IsNullOrWhiteSpace(p.FactorValues[f])))
                        .ToList();

                    // Stats for Max/Min
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

                    // Collect tolerances for highlighting & USL/LSL rows
                    var dimensionTolerances = new Dictionary<string, (double nominal, double plus, double minus)>();
                    foreach (var rec in rawRecords)
                    {
                        var dim = rec.Dimension;
                        if (!dimensionTolerances.ContainsKey(dim))
                        {
                            dimensionTolerances[dim] = (rec.Nominal, rec.TolPlus, rec.TolMinus);
                        }
                    }

                    // 5) Build the PDF table
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

                // Done with all departments
                document.Close();
                return ms.ToArray();
            }
        }

        // 6) BuildDepartmentTable - unchanged from your code
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
            // Common columns: (Record, Date/Time)
            int commonCols = 2;
            int measCount = measurementColumns.Count;
            int factorCount = factorColumns.Count;
            int totalCols = commonCols + measCount + factorCount;

            // Column widths: Record=1, Date=3, everything else=1
            float[] colWidths = new float[totalCols];
            colWidths[0] = 1f;  // record
            colWidths[1] = 3f;  // date/time
            for (int i = 2; i < totalCols; i++)
                colWidths[i] = 1f;

            var table = new Table(colWidths)
                .SetWidth(UnitValue.CreatePercentValue(100))
                .SetMarginTop(10);

            // 1) Tolerance/stat rows: USL, LSL, Max, Min
            string[] rowLabels = { "USL", "LSL", "Max", "Min" };
            foreach (var label in rowLabels)
            {
                // first cell = label
                table.AddHeaderCell(new Cell()
                    .Add(new Paragraph(label).SetFont(boldFont).SetFontSize(smallFontSize))
                    .SetBackgroundColor(ColorConstants.LIGHT_GRAY)
                    .SetTextAlignment(TextAlignment.CENTER)
                    .SetPadding(3)
                    .SetBorder(new SolidBorder(1)));

                // second cell = blank
                table.AddHeaderCell(new Cell()
                    .Add(new Paragraph("").SetFont(regularFont).SetFontSize(smallFontSize))
                    .SetBackgroundColor(ColorConstants.LIGHT_GRAY)
                    .SetTextAlignment(TextAlignment.CENTER)
                    .SetPadding(3)
                    .SetBorder(new SolidBorder(1)));

                // one cell per measurement dimension
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

                // factor columns are blank in these stat rows
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

            // 2) Main header row: "Record", "Date/Time", then dims, then factors
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

            // 3) Data rows
            foreach (var pivot in pivotRecords)
            {
                // Record Number
                table.AddCell(new Cell()
                    .Add(new Paragraph(pivot.RecordNumber.ToString())
                        .SetFont(regularFont)
                        .SetFontSize(smallFontSize))
                    .SetPadding(3)
                    .SetBorder(new SolidBorder(1)));

                // Date/Time
                table.AddCell(new Cell()
                    .Add(new Paragraph(pivot.MeasureDate.ToString("g"))
                        .SetFont(regularFont)
                        .SetFontSize(smallFontSize))
                    .SetTextAlignment(TextAlignment.CENTER)
                    .SetPadding(3)
                    .SetBorder(new SolidBorder(1)));

                // Measurement columns
                foreach (var dim in measurementColumns)
                {
                    pivot.Measurements.TryGetValue(dim, out var val);
                    if (string.IsNullOrWhiteSpace(val)) val = "";

                    var paragraph = new Paragraph(val)
                        .SetFont(regularFont)
                        .SetFontSize(smallFontSize);

                    // Highlight out-of-spec in red
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

                // Factor columns
                foreach (var factor in factorColumns)
                {
                    pivot.FactorValues.TryGetValue(factor, out var fVal);
                    if (string.IsNullOrWhiteSpace(fVal)) fVal = "";

                    table.AddCell(new Cell()
                        .Add(new Paragraph(fVal)
                            .SetFont(regularFont)
                            .SetFontSize(smallFontSize))
                        .SetTextAlignment(TextAlignment.RIGHT)
                        .SetPadding(3)
                        .SetBorder(new SolidBorder(1)));
                }
            }

            // Finally, add the table to the document
            document.Add(table);
        }
    }
}
