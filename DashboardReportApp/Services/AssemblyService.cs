using DashboardReportApp.Models;
using DashboardReportApp.Services;
using iText.IO.Font.Constants;
using iText.Kernel.Font;
using iText.Kernel.Geom;
using iText.Kernel.Pdf;
using iText.Kernel.Pdf.Canvas;
using iText.Kernel.Pdf.Canvas.Draw;
using iText.Layout;
using iText.Layout.Borders;
using iText.Layout.Element;
using iText.Layout.Properties;
using iText.StyledXmlParser.Jsoup.Nodes;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using MySql.Data.MySqlClient;
using Mysqlx.Crud;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Odbc;
using System.IO;
using static iText.StyledXmlParser.Jsoup.Select.Evaluator;

public class AssemblyService
{
    private readonly string _connectionStringMySQL;
    private readonly SharedService _sharedService;
    private readonly string _exportsFolder;   // <- from config
    private readonly string datatable = "assembly";

    public AssemblyService(IConfiguration configuration, SharedService sharedService, IOptionsMonitor<PathOptions> opts, IWebHostEnvironment env)
    {
        _connectionStringMySQL = configuration.GetConnectionString("MySQLConnection");
        _sharedService = sharedService;

        var p = opts.CurrentValue;
        var configured = string.IsNullOrWhiteSpace(p.AssemblyExports) ? "App_Data/Exports" : p.AssemblyExports;

        _exportsFolder = System.IO.Path.IsPathFullyQualified(configured)
            ? configured
            : System.IO.Path.GetFullPath(System.IO.Path.Combine(env.ContentRootPath, configured));

        Directory.CreateDirectory(_exportsFolder);

    }
        // Rebuild absolute path when we only have filename
    public string GetExportAbsolutePath(string? name)
    {
        var fn = string.IsNullOrWhiteSpace(name) ? "" : System.IO.Path.GetFileName(name);
        return string.IsNullOrWhiteSpace(fn) ? "" : System.IO.Path.Combine(_exportsFolder, fn);
    }
    public async Task<List<AssemblyModel>> GetAllRunsAsync()
{
    var allRuns = new List<AssemblyModel>();

    string query = @"
            SELECT id, timestamp, operator, prodNumber, part, endDateTime, notes, open, skidNumber, pcs
            FROM " + datatable +
            " ORDER BY id DESC";

    await using var connection = new MySqlConnection(_connectionStringMySQL);
    await connection.OpenAsync();
    await using var command = new MySqlCommand(query, connection);
    await using var reader = await command.ExecuteReaderAsync();

    while (await reader.ReadAsync())
    {
        // Use IsDBNull to check for null values before calling GetDateTime
        DateTime timestamp = !reader.IsDBNull(reader.GetOrdinal("timestamp"))
                             ? reader.GetDateTime("timestamp")
                             : DateTime.MinValue; // or choose a default value

        DateTime endDateTime = !reader.IsDBNull(reader.GetOrdinal("endDateTime"))
                                 ? reader.GetDateTime("endDateTime")
                                 : DateTime.MinValue; // adjust default as needed


            allRuns.Add(new AssemblyModel
            {
                Id = reader.GetInt32("Id"),
                Operator = reader["operator"]?.ToString(),
                ProdNumber = reader["prodNumber"]?.ToString() ?? "N/A",
                Part = reader["part"]?.ToString() ?? "N/A",
                EndDateTime = endDateTime,
                Notes = reader["notes"]?.ToString(),
                Open = reader["open"] != DBNull.Value ? Convert.ToSByte(reader["open"]) : (sbyte)0,
                SkidNumber = reader["skidNumber"] != DBNull.Value ? reader.GetInt32("skidNumber") : 0,
                Pcs = !reader.IsDBNull(reader.GetOrdinal("pcs")) ? reader.GetInt32("pcs") : 0


            });
    }

    return allRuns;
}
    public async Task<List<PressRunLogModel>> GetOpenGreenSkidsAsync()
    {
        var openGreenSkids = new List<PressRunLogModel>();

        string query = @"
SELECT 
    MIN(id)                  AS id,
    MIN(timestamp)           AS timestamp,
    prodNumber,
    GROUP_CONCAT(DISTINCT run)        AS run,
    part,
    GROUP_CONCAT(DISTINCT component)  AS component,
    MAX(endDateTime)         AS endDateTime,
    GROUP_CONCAT(DISTINCT operator)   AS operator,
    GROUP_CONCAT(DISTINCT machine)    AS machine,
    MIN(pcsStart)            AS pcsStart,
    MAX(pcsEnd)              AS pcsEnd,
    ''                       AS notes,
    0                        AS skidNumber,
    GROUP_CONCAT(DISTINCT lotNumber)      AS lotNumber,
    GROUP_CONCAT(DISTINCT materialCode)   AS materialCode
FROM pressrun
WHERE open = 1
  AND skidNumber > 0
  AND component LIKE '%C%'   -- assembly parent skids come from C components
GROUP BY prodNumber, part
ORDER BY part;";

        await using var connection = new MySqlConnection(_connectionStringMySQL);
        await connection.OpenAsync();
        await using var command = new MySqlCommand(query, connection);
        await using var reader = await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            openGreenSkids.Add(new PressRunLogModel
            {
                Id = reader.GetInt32("id"),
                Timestamp = reader.GetDateTime("timestamp"),
                ProdNumber = reader["prodNumber"]?.ToString() ?? "N/A",
                Run = reader["run"]?.ToString() ?? "",
                Part = reader["part"]?.ToString() ?? "N/A",
                Component = reader["component"]?.ToString() ?? "",
                EndDateTime = reader.IsDBNull(reader.GetOrdinal("endDateTime"))
                                ? (DateTime?)null
                                : reader.GetDateTime("endDateTime"),
                Operator = reader["operator"]?.ToString() ?? "",
                SkidNumber = 0,
                Machine = reader["machine"]?.ToString() ?? "",
                PcsStart = reader.IsDBNull(reader.GetOrdinal("pcsStart"))
                                ? 0
                                : Convert.ToInt32(reader["pcsStart"]),
                PcsEnd = reader.IsDBNull(reader.GetOrdinal("pcsEnd"))
                                ? 0
                                : Convert.ToInt32(reader["pcsEnd"]),
                Notes = reader["notes"]?.ToString() ?? "",
                LotNumber = reader["lotNumber"]?.ToString() ?? "",
                MaterialCode = reader["materialCode"]?.ToString() ?? ""
            });
        }

        return openGreenSkids;
    }


    // Get a list of operators from MySQL
    public List<string> GetOperators()
    {
        var operators = new List<string>();
        string query = "SELECT name FROM operators WHERE dept = 'greenAssembly' ORDER BY name";

        using (var connection = new MySqlConnection(_connectionStringMySQL))
        {
            connection.Open();
            using (var command = new MySqlCommand(query, connection))
            {
                using (var reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        operators.Add(reader["name"].ToString());
                    }
                }
            }
        }

        return operators;
    }

    /// <summary>
    /// Inserts a new skid record into the assembly table and then generates a PDF report 
    /// using the submitted AssemblyModel information.
    /// </summary>
    /// <param name="model">The AssemblyModel with the submitted info.</param>
    /// <returns>The file path to the generated PDF report.</returns>
    public async Task LogSkidAsync(AssemblyModel model)
    {
        int nextSkidNumber = 1;
        string selectQuery = "SELECT IFNULL(MAX(skidNumber), 0) FROM " + datatable + " WHERE prodNumber = @prodNumber AND part = @part";

        using (var connection = new MySqlConnection(_connectionStringMySQL))
        {
            connection.Open();

            // Determine the next skid number.
            using (var selectCommand = new MySqlCommand(selectQuery, connection))
            {
                selectCommand.Parameters.AddWithValue("@prodNumber", model.ProdNumber);
                selectCommand.Parameters.AddWithValue("@part", model.Part.ToUpper());
                var result = selectCommand.ExecuteScalar();
                if (result != null)
                {
                    int currentMax = Convert.ToInt32(result);
                    nextSkidNumber = currentMax + 1;
                }
            }

            // Insert the new skid record with open = 1.
            string insertQuery = @"
            INSERT INTO " + datatable + @" 
                (prodNumber, part, endDateTime, operator, notes, skidNumber, pcs, open) 
            VALUES 
                (@prodNumber, @part, @endDateTime, @operator, @notes, @skidNumber, @pcs, 1)";

            using (var command = new MySqlCommand(insertQuery, connection))
            {
                command.Parameters.AddWithValue("@prodNumber", model.ProdNumber);
                command.Parameters.AddWithValue("@part", model.Part.ToUpper());
                command.Parameters.AddWithValue("@endDateTime", DateTime.Now);
                command.Parameters.AddWithValue("@operator", model.Operator);
                command.Parameters.AddWithValue("@notes", string.IsNullOrEmpty(model.Notes) ? DBNull.Value : (object)model.Notes);
                command.Parameters.AddWithValue("@skidNumber", nextSkidNumber);
                command.Parameters.AddWithValue("@pcs", model.Pcs);

                command.ExecuteNonQuery();

                // Retrieve the last inserted ID and update the model.
                long insertedId = command.LastInsertedId;
                model.Id = Convert.ToInt32(insertedId);
            }

            // Update the model with the newly determined skid number.
            model.SkidNumber = nextSkidNumber;
        }

        
    }


    /// <summary>
    /// Generates a PDF report for the given AssemblyModel and returns the file path.
    /// </summary>
    public async Task<string> GenerateAssemblyReportAsync(AssemblyModel model)
    {
        // 🔴 ADD THIS LINE: make sure the model has runs, components, lots, materials, machines
        await PopulateAssemblyMetadataFromPressrunAsync(model);

        var fileName = $"AssemblyTag_{model.Id}.pdf";
        var filePath = System.IO.Path.Combine(_exportsFolder, fileName);

        List<string> result = _sharedService.GetOrderOfOps(model.Part);

        PdfFont boldFont = PdfFontFactory.CreateFont(StandardFonts.HELVETICA_BOLD);
        PdfFont normalFont = PdfFontFactory.CreateFont(StandardFonts.HELVETICA);

        PdfWriter writer = new PdfWriter(filePath);
        PdfDocument pdf = new PdfDocument(writer);

        using (var document = new iText.Layout.Document(pdf))
        {
            document.SetMargins(20, 20, 40, 20);

            // Title
            document.Add(new Paragraph("Assembly Tag")
                .SetFont(boldFont)
                .SetFontSize(18)
                .SetTextAlignment(TextAlignment.CENTER));

            document.Add(new LineSeparator(new SolidLine()).SetMarginBottom(10));

            // Order of Operations header
            document.Add(new Paragraph("Order of Operations:")
                .SetFont(boldFont)
                .SetFontSize(14)
                .SetTextAlignment(TextAlignment.CENTER)
                .SetMarginBottom(2));

            document.Add(new Paragraph("Assembly")
                .SetFont(boldFont)
                .SetFontSize(12)
                .SetTextAlignment(TextAlignment.CENTER)
                .SetMarginBottom(10));

            // ==== CORE ASSEMBLY INFO (existing, plus run) ====
            document.Add(new Paragraph($"Assembly Id: {model.Id}")
                .SetFont(normalFont).SetFontSize(12).SetMarginBottom(2));

            document.Add(new Paragraph($"Production Number: {model.ProdNumber}")
                .SetFont(normalFont).SetFontSize(12).SetMarginBottom(2));

            document.Add(new Paragraph($"Part: {model.Part}")
                .SetFont(normalFont).SetFontSize(12).SetMarginBottom(2));

            document.Add(new Paragraph($"End DateTime: {DateTime.Now}")
                .SetFont(normalFont).SetFontSize(12).SetMarginBottom(2));

            document.Add(new Paragraph($"Operator: {model.Operator}")
                .SetFont(normalFont).SetFontSize(12).SetMarginBottom(2));

            document.Add(new Paragraph($"Pcs: {model.Pcs}")
                .SetFont(normalFont).SetFontSize(12).SetMarginBottom(2));

            document.Add(new Paragraph($"Skid Number: {model.SkidNumber}")
                .SetFont(normalFont).SetFontSize(12).SetMarginBottom(2));

            if (!string.IsNullOrWhiteSpace(model.Notes))
            {
                document.Add(new Paragraph($"Notes: {model.Notes}")
                    .SetFont(normalFont).SetFontSize(12).SetMarginBottom(6));
            }

            // ==== NEW BLOCK: PRESS / MATERIAL INFO ====
            if (!string.IsNullOrWhiteSpace(model.Components) ||
                !string.IsNullOrWhiteSpace(model.LotNumbers) ||
                !string.IsNullOrWhiteSpace(model.MaterialCodes) ||
                !string.IsNullOrWhiteSpace(model.Machines))
            {
                document.Add(new LineSeparator(new SolidLine())
                    .SetMarginTop(5).SetMarginBottom(5));

                document.Add(new Paragraph("Source Press / Material Information")
                    .SetFont(boldFont)
                    .SetFontSize(13)
                    .SetMarginBottom(4));

                if (!string.IsNullOrWhiteSpace(model.Run))
                {
                    document.Add(new Paragraph($"Run: {model.Run}")
                        .SetFont(normalFont).SetFontSize(12).SetMarginBottom(2));
                }

                if (!string.IsNullOrWhiteSpace(model.Components))
                {
                    document.Add(new Paragraph($"Components: {model.Components}")
                        .SetFont(normalFont).SetFontSize(12).SetMarginBottom(2));
                }
                if (!string.IsNullOrWhiteSpace(model.LotNumbers))
                {
                    document.Add(new Paragraph($"Lot Number(s): {model.LotNumbers}")
                        .SetFont(normalFont).SetFontSize(12).SetMarginBottom(2));
                }
                if (!string.IsNullOrWhiteSpace(model.MaterialCodes))
                {
                    document.Add(new Paragraph($"Material Code(s): {model.MaterialCodes}")
                        .SetFont(normalFont).SetFontSize(12).SetMarginBottom(2));
                }
                if (!string.IsNullOrWhiteSpace(model.Machines))
                {
                    document.Add(new Paragraph($"Press Machine(s): {model.Machines}")
                        .SetFont(normalFont).SetFontSize(12).SetMarginBottom(6));
                }
            }

            // ==== ORDER OF OPS (unchanged) ====
            foreach (var item in result)
            {
                document.Add(new Paragraph(_sharedService.processDesc(item))
                    .SetFont(boldFont)
                    .SetFontSize(12)
                    .SetTextAlignment(TextAlignment.CENTER)
                    .SetMarginBottom(2));

                if (item.ToLower().Contains("machin"))
                {
                    document.Add(new Paragraph("Machine #: ____________ Signature: __________________________   Date: __________________")
                        .SetFont(normalFont).SetFontSize(12)
                        .SetTextAlignment(TextAlignment.CENTER)
                        .SetMarginBottom(5));
                }
                else if (item.ToLower().Contains("sinter"))
                {
                    document.Add(new Paragraph("Loaded By: __________________   Unloaded By: __________________   Date: __________________")
                        .SetFont(normalFont).SetFontSize(12)
                        .SetTextAlignment(TextAlignment.CENTER)
                        .SetMarginBottom(5));
                }
                else
                {
                    document.Add(new Paragraph("Signature: __________________________   Date: __________________")
                        .SetFont(normalFont).SetFontSize(12)
                        .SetTextAlignment(TextAlignment.CENTER)
                        .SetMarginBottom(5));
                }

                // Footer on each page
                int pageCount = pdf.GetNumberOfPages();
                for (int i = 1; i <= pageCount; i++)
                {
                    PdfPage page = pdf.GetPage(i);
                    Rectangle pageSize = page.GetPageSize();
                    PdfCanvas pdfCanvas = new PdfCanvas(page);
                    Canvas footerCanvas = new Canvas(pdfCanvas, pageSize);
                    footerCanvas.ShowTextAligned(
                        new Paragraph(model.Part + " Skid # " + model.SkidNumber)
                            .SetFont(boldFont)
                            .SetFontSize(25),
                        pageSize.GetWidth() / 2,
                        pageSize.GetBottom() + 33,
                        TextAlignment.CENTER);
                    footerCanvas.Close();
                }
            }

            pdf.Close();
            return filePath;
        }
    }


    public void EndProduction(string part, string prodNumber)
    {
        string updateQuery1 = "UPDATE pressrun " +
                       "SET open = 0 " +
                       "WHERE prodNumber = @prodNumber AND part = @part";

        using (var connection = new MySqlConnection(_connectionStringMySQL))
        {
            connection.Open();
            using (var updateCommand = new MySqlCommand(updateQuery1, connection))
            {
                updateCommand.Parameters.AddWithValue("@part", part);
                updateCommand.Parameters.AddWithValue("@prodNumber", prodNumber);

                int rowsAffected = updateCommand.ExecuteNonQuery();
                Console.WriteLine($"✅ Rows Updated: {rowsAffected}");
            }
        }

    }

    public async Task<(List<AssemblyModel> Items, int TotalCount)> GetPagedRunsAsync(
     int page, int pageSize, string sort, string dir, string search)
    {
        var items = new List<AssemblyModel>();
        int total = 0;

        var validSort = sort switch
        {
            "endDateTime" => "a.endDateTime",
            "prodNumber" => "a.prodNumber",
            "part" => "a.part",
            "operator" => "a.operator",
            _ => "a.id"
        };
        var validDir = dir?.ToUpper() == "ASC" ? "ASC" : "DESC";

        string whereClause = "";
        if (!string.IsNullOrWhiteSpace(search))
        {
            whereClause = @"WHERE a.prodNumber LIKE @search
                     OR a.part      LIKE @search
                     OR a.operator  LIKE @search";
        }

        string countSql = $"SELECT COUNT(*) FROM {datatable} a {whereClause}";

        string dataSql = $@"
        SELECT 
            a.id,
            a.timestamp,
            a.operator,
            a.prodNumber,
            a.part,
            a.endDateTime,
            a.notes,
            a.open,
            a.skidNumber,
            a.pcs,
            prAgg.runs,
            prAgg.lotNumbers,
            prAgg.materialCodes,
            prAgg.components,
            prAgg.machines
        FROM {datatable} a
        LEFT JOIN (
            SELECT 
                prodNumber,
                part,
                GROUP_CONCAT(DISTINCT run)        AS runs,
                GROUP_CONCAT(DISTINCT lotNumber)  AS lotNumbers,
                GROUP_CONCAT(DISTINCT materialCode) AS materialCodes,
                GROUP_CONCAT(DISTINCT component)  AS components,
                GROUP_CONCAT(DISTINCT machine)    AS machines
            FROM pressrun
            GROUP BY prodNumber, part
        ) prAgg
          ON prAgg.prodNumber = a.prodNumber
         AND prAgg.part       = a.part
        {whereClause}
        ORDER BY {validSort} {validDir}
        LIMIT @pageSize OFFSET @offset";

        await using var conn = new MySqlConnection(_connectionStringMySQL);
        await conn.OpenAsync();

        // total
        await using (var countCmd = new MySqlCommand(countSql, conn))
        {
            if (!string.IsNullOrWhiteSpace(search))
                countCmd.Parameters.AddWithValue("@search", $"%{search}%");
            total = Convert.ToInt32(await countCmd.ExecuteScalarAsync());
        }

        // page data
        await using (var dataCmd = new MySqlCommand(dataSql, conn))
        {
            dataCmd.Parameters.AddWithValue("@pageSize", pageSize);
            dataCmd.Parameters.AddWithValue("@offset", (page - 1) * pageSize);
            if (!string.IsNullOrWhiteSpace(search))
                dataCmd.Parameters.AddWithValue("@search", $"%{search}%");

            await using var reader = await dataCmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                items.Add(new AssemblyModel
                {
                    Id = reader.GetInt32("id"),
                    Operator = reader["operator"]?.ToString(),
                    ProdNumber = reader["prodNumber"]?.ToString() ?? "N/A",
                    Part = reader["part"]?.ToString() ?? "N/A",
                    EndDateTime = reader.IsDBNull(reader.GetOrdinal("endDateTime"))
                                    ? DateTime.MinValue
                                    : reader.GetDateTime("endDateTime"),
                    Notes = reader["notes"]?.ToString(),
                    Open = reader["open"] != DBNull.Value ? Convert.ToSByte(reader["open"]) : (sbyte)0,
                    SkidNumber = reader["skidNumber"] != DBNull.Value ? reader.GetInt32("skidNumber") : 0,
                    Pcs = !reader.IsDBNull(reader.GetOrdinal("pcs")) ? reader.GetInt32("pcs") : 0,

                    Run = reader["runs"]?.ToString() ?? "",
                    LotNumbers = reader["lotNumbers"]?.ToString() ?? "",
                    MaterialCodes = reader["materialCodes"]?.ToString() ?? "",
                    Components = reader["components"]?.ToString() ?? "",
                    Machines = reader["machines"]?.ToString() ?? ""
                });
            }
        }

        return (items, total);
    }

    private async Task PopulateAssemblyMetadataFromPressrunAsync(AssemblyModel model)
    {
        const string sql = @"
        SELECT 
            GROUP_CONCAT(DISTINCT run)          AS runs,
            GROUP_CONCAT(DISTINCT component)    AS components,
            GROUP_CONCAT(DISTINCT lotNumber)    AS lotNumbers,
            GROUP_CONCAT(DISTINCT materialCode) AS materialCodes,
            GROUP_CONCAT(DISTINCT machine)      AS machines
        FROM pressrun
        WHERE prodNumber = @prodNumber
          AND part       = @part";

        await using var conn = new MySqlConnection(_connectionStringMySQL);
        await conn.OpenAsync();
        await using var cmd = new MySqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@prodNumber", model.ProdNumber);
        cmd.Parameters.AddWithValue("@part", model.Part);

        await using var reader = await cmd.ExecuteReaderAsync();
        if (await reader.ReadAsync())
        {
            model.Run = reader["runs"]?.ToString() ?? "";
            model.Components = reader["components"]?.ToString() ?? "";
            model.LotNumbers = reader["lotNumbers"]?.ToString() ?? "";
            model.MaterialCodes = reader["materialCodes"]?.ToString() ?? "";
            model.Machines = reader["machines"]?.ToString() ?? "";
        }
    }


}



