using DashboardReportApp.Models;
using iText.IO.Font.Constants;
using iText.Kernel.Colors;
using iText.Kernel.Font;
using iText.Kernel.Geom;
using iText.Kernel.Pdf;
using iText.Layout;
using iText.Layout.Element;
using iText.Layout.Properties;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using MySql.Data.MySqlClient;
using Serilog;
using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Globalization;
using Path = System.IO.Path;

namespace DashboardReportApp.Services
{
    public interface ITestBakeService
    {
        Task<TestBakeViewModel> LoadInitialAsync(
            string productionNumber,
            string runNumber,
            string testType,
            string reason,
            DateTime? testBakeStartTime,
    DateTime? testBakeEndTime,
            int? headerId);

        Task LoginAsync(string @operator);
        Task PlaceTestBakeAsync(TestBakeLoginRow login);  // NEW
        Task<List<TestBakeLoginRow>> GetActiveLoginsAsync();
        Task<TestBakeLoginRow?> GetLoginByIdAsync(int id);
        Task<int> FinishTestBakeAsync(int loginId);



        Task<int> CreateTestBakeHeaderAsync(TestBakeHeaderRow header);
        Task<TestBakeHeaderRow?> GetHeaderByIdAsync(int id);

        // NEW: for PDF generation (used later)
        Task<byte[]> GenerateTestBakePdfAsync(TestBakeViewModel vm);

        Task<int> GetHeaderCountAsync();
        Task<List<TestBakeHeaderRow>> GetRecentHeadersAsync(int page, int pageSize);

        Task<byte[]> GetPdfFromDiskAsync(int headerId);
    }




    public class TestBakeService : ITestBakeService
    {
        private readonly string _prolinkConnStr;   // SQLExpressConnection
        private readonly string _mastersConnStr;   // SqlServerConnectionsinTSQL
        private readonly string _mysqlConnStr;     // MySQLConnection

        private readonly string _uploadPath;
        // Adjust these to match whatever factor_desc names you use in Prolink


        private const string FactorDescTestBake = "TestBake"; // new; value = 'Yes'

        public TestBakeService(IConfiguration config)
        {
            _prolinkConnStr = config.GetConnectionString("SQLExpressConnection");
            _mastersConnStr = config.GetConnectionString("SqlServerConnectionsinTSQL");
            _mysqlConnStr = config.GetConnectionString("MySQLConnection");


            _uploadPath = config.GetValue<string>("Paths:TestBakeUploads")
                         ?? Path.Combine(AppContext.BaseDirectory, "Uploads", "TestBake");

            Directory.CreateDirectory(_uploadPath);
        }


        public async Task<TestBakeViewModel> LoadInitialAsync(
       string productionNumber,
       string runNumber,
       string testType,
       string reason,
       DateTime? testBakeStartTime,
       DateTime? testBakeEndTime,
       int? headerId)
        {
            var vm = new TestBakeViewModel
            {
                SearchProductionNumber = productionNumber ?? "",
                SearchRunNumber = runNumber ?? "",
                SearchTestType = testType ?? "",
                SearchReason = reason ?? "",
                TestBakeStartTime = testBakeStartTime,
                TestBakeEndTime =testBakeEndTime,
                HeaderId = headerId
            };

            try
            {
                // 1) If we have a saved header row, load it first
                if (headerId.HasValue)
                {
                    var storedHeader = await GetHeaderByIdAsync(headerId.Value);
                    if (storedHeader != null)
                    {
                        vm.HeaderId = storedHeader.Id;

                        // This is the Prolink part we want to drive all lookups (e.g., SG-1104)
                        vm.Header.Part = storedHeader.ProlinkPart;
                        vm.Header.Component = null; // or storedHeader.Component if you add that field
                        vm.Header.ProductionNumber = storedHeader.ProductionNumber;
                        vm.Header.RunNumber = storedHeader.RunNumber;
                        vm.Header.TestType = storedHeader.TestType;
                        vm.Header.Reason = storedHeader.Reason;
                        vm.TestBakeStartTime = storedHeader.TestBakeStartTime;
                        vm.TestBakeEndTime = storedHeader.TestBakeEndTime;
                        vm.Header.TestedBy = storedHeader.Operator;
                    }
                }
                else
                {
                    // no header row yet: just carry through what was passed in
                    vm.TestBakeStartTime = testBakeStartTime;
                    vm.TestBakeEndTime = testBakeEndTime;
                }
                // 2) If nothing came from stored header (Initial scan path), fall back to search fields
                if (string.IsNullOrWhiteSpace(vm.Header.ProductionNumber) &&
                    string.IsNullOrWhiteSpace(vm.Header.RunNumber) &&
                    string.IsNullOrWhiteSpace(vm.Header.TestType) &&
                    string.IsNullOrWhiteSpace(vm.Header.Reason))
                {
                    vm.Header.ProductionNumber = vm.SearchProductionNumber;
                    vm.Header.RunNumber = vm.SearchRunNumber;
                    vm.Header.TestType = vm.SearchTestType;
                    vm.Header.Reason = vm.SearchReason;
                    // Part will be filled by LoadHeaderFromProlinkAsync if still empty
                }

                // 3) Get Prolink header info (Date, Machine, Material, Lot, etc.)
                await LoadHeaderFromProlinkAsync(vm);

                await LoadMachineMaterialLotFromRunsAsync(vm);

                // 4) Tool numbers from tooling_inventory using vm.Header.Part
                await LoadToolNumbersAndDetailsAsync(vm);

                // 5) Master limits from masterm using header Part + Component
                var masterRows = await LoadMasterDimensionsAsync(vm.Header.Part, vm.Header.Component);

                // 6) Prolink MOLD + SINTER dims using header Part
                var moldDims = await LoadProlinkDimsByLocationAsync(vm, "MOLDING");
                var sinterDims = await LoadProlinkDimsByLocationAsync(vm, "SINTERING");

                // 7) Merge
                vm.Dimensions = MergeDimensions(masterRows, moldDims, sinterDims);

                vm.HasResults = true;
            }
            catch (Exception ex)
            {
                vm.ErrorMessage = ex.Message;
                vm.HasResults = false;
            }


            return vm;
        }



        #region Prolink header

        private async Task LoadHeaderFromProlinkAsync(TestBakeViewModel vm)
        {
            using var conn = new SqlConnection(_prolinkConnStr);
            await conn.OpenAsync();

            const string sql = @"
WITH TestBakeParts AS (
    SELECT
        p.part_id,
        p.record_number,
        p.measure_date,
        qf.qcc_file_desc
    FROM dbo.part p
    JOIN dbo.qcc_file qf     ON p.qcc_file_id = qf.qcc_file_id
    LEFT JOIN dbo.part_factor pf ON p.part_id = pf.part_id
    LEFT JOIN dbo.factor f       ON pf.factor_id = f.factor_id
    WHERE
    f.factor_desc = @FactorTestBake
    AND pf.value = 'Yes'
    AND (@StartTime IS NULL OR p.measure_date >= @StartTime)
    AND (@EndTime   IS NULL OR p.measure_date <= @EndTime)

)
SELECT TOP 1
    part_id,
    record_number,
    measure_date,
    qcc_file_desc
FROM TestBakeParts
ORDER BY measure_date ASC;";

            using var cmd = new SqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@FactorTestBake", FactorDescTestBake);
            cmd.Parameters.AddWithValue("@StartTime",
    vm.TestBakeStartTime.HasValue ? vm.TestBakeStartTime.Value : (object)DBNull.Value);
            cmd.Parameters.AddWithValue("@EndTime",
                vm.TestBakeEndTime.HasValue ? vm.TestBakeEndTime.Value : (object)DBNull.Value);


            using var reader = await cmd.ExecuteReaderAsync();
            if (!await reader.ReadAsync())
            {
                throw new Exception("No Prolink TestBake records found at or after the login time.");
            }

            int partId = reader.GetInt32(reader.GetOrdinal("part_id"));
            vm.Header.Date = (DateTime?)reader["measure_date"];
            string qccDesc = reader["qcc_file_desc"]?.ToString() ?? "";

            // 🔹 Only set Part from Prolink if it wasn't already supplied (e.g., from MySQL header)
            if (string.IsNullOrWhiteSpace(vm.Header.Part))
            {
                vm.Header.Part = qccDesc;
            }

            await LoadBasicFactorsForHeaderAsync(vm, conn, partId);
        }


        private async Task LoadBasicFactorsForHeaderAsync(TestBakeViewModel vm, SqlConnection conn, int partId)
        {
            // OPTIONAL: if you store Machine/Lot/Material etc. as Prolink factors,
            // you can pull them here. For now, this just looks for some common ones.

            const string sql = @"
SELECT f.factor_desc, pf.value
FROM dbo.part_factor pf
JOIN dbo.factor f ON pf.factor_id = f.factor_id
WHERE pf.part_id = @PartId;";

            using var cmd = new SqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@PartId", partId);

            using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                string desc = reader["factor_desc"]?.ToString() ?? "";
                string val = reader["value"]?.ToString() ?? "";

                if (desc.Contains("Machine", StringComparison.OrdinalIgnoreCase) ||
                    desc.Contains("Press", StringComparison.OrdinalIgnoreCase))
                    vm.Header.MachineNumber = val;

                if (desc.Contains("Mix Lot", StringComparison.OrdinalIgnoreCase) ||
                    desc.Contains("Lot", StringComparison.OrdinalIgnoreCase))
                    vm.Header.LotNumber = val;

                if (desc.Contains("Material", StringComparison.OrdinalIgnoreCase))
                    vm.Header.Material = val;
            }
        }

        #endregion

        #region Tooling inventory (MySQLConnection)

        private async Task LoadToolNumbersAndDetailsAsync(TestBakeViewModel vm)
        {
            using var conn = new MySqlConnection(_mysqlConnStr);
            await conn.OpenAsync();

            // Get distinct tool numbers for this part (AssemblyNumber)
            const string sql = @"
SELECT DISTINCT ToolNumber
FROM tooling_inventory
WHERE AssemblyNumber = @part
  AND ToolNumber IS NOT NULL
ORDER BY ToolNumber;";

            using var cmd = new MySqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@part", vm.Header.Part);

            using var reader = await cmd.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                string toolNumber = reader["ToolNumber"]?.ToString() ?? "";
                if (!string.IsNullOrWhiteSpace(toolNumber))
                    vm.ToolNumbers.Add(toolNumber);
            }

            // We DO NOT set TestedBy / TopPunch / BottomPunch / Die / Pin here,
            // since in your schema:
            // - TestedBy is whoever fills out the form (input on page)
            // - ToolItem identifies Top Punch / Bottom Punch / etc. on separate rows.
            // You can later add logic to query ToolItem-specific rows if you want
            // dropdowns per punch/pin.
        }


        #endregion

        #region Master dimensions – masterm (SqlServerConnectionsinTSQL)

        private async Task<List<CombinedDimensionRow>> LoadMasterDimensionsAsync(string part, string component)
        {
            var list = new List<CombinedDimensionRow>();

            string masterId = ResolveMasterId(part, component);

            using var conn = new SqlConnection(_mastersConnStr);
            await conn.OpenAsync();

            const string sql = @"
SELECT 
    master_id,
    [desc],        -- molding dimension name
    dim1,          -- molding low
    dim2,          -- molding high
    s_dim1,   -- sintering low
    s_dim2    -- sintering high
FROM masterm
WHERE master_id = @masterId
  AND ([desc] IS NOT NULL)
ORDER BY [desc];";

            using var cmd = new SqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@masterId", masterId);

            using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var lowMold = SafeDecimal(reader["dim1"]);
                var highMold = SafeDecimal(reader["dim2"]);
                var lowSinter = SafeDecimal(reader["s_dim1"]);
                var highSinter = SafeDecimal(reader["s_dim2"]);

                var row = new CombinedDimensionRow
                {
                    MasterMoldName = reader["desc"]?.ToString() ?? "",
                    MasterMoldLow = lowMold,
                    MasterMoldHigh = highMold,
                    MasterSinterLow = lowSinter,
                    MasterSinterHigh = highSinter
                };

                if (lowMold.HasValue && highMold.HasValue)
                {
                    row.MasterMoldNominal = (lowMold.Value + highMold.Value) / 2m;
                    row.MasterMoldTolMinus = lowMold.Value - row.MasterMoldNominal;
                    row.MasterMoldTolPlus = highMold.Value - row.MasterMoldNominal;
                }

                if (lowSinter.HasValue && highSinter.HasValue)
                {
                    row.MasterSinterNominal = (lowSinter.Value + highSinter.Value) / 2m;
                    row.MasterSinterTolMinus = lowSinter.Value - row.MasterSinterNominal;
                    row.MasterSinterTolPlus = highSinter.Value - row.MasterSinterNominal;
                }

                list.Add(row);
            }

            return list;
        }

       

private static decimal? SafeDecimal(object dbValue)
    {
        if (dbValue == null || dbValue == DBNull.Value)
            return null;

        var s = dbValue.ToString().Trim();
        if (string.IsNullOrEmpty(s))
            return null;

        // Remove any non-numeric / non-sign / non-decimal characters (like '#')
        var filtered = new string(s.Where(c =>
            char.IsDigit(c) || c == '.' || c == '-' || c == '+'
        ).ToArray());

        if (string.IsNullOrWhiteSpace(filtered))
            return null;

        // Try invariant culture first
        if (decimal.TryParse(filtered,
            NumberStyles.Any,
            CultureInfo.InvariantCulture,
            out var val))
        {
            return val;
        }

        // As a fallback, try current culture
        if (decimal.TryParse(filtered,
            NumberStyles.Any,
            CultureInfo.CurrentCulture,
            out val))
        {
            return val;
        }

        // If it still won't parse, just ignore it instead of blowing up
        // or you can log it somewhere if you want
        return null;
        // If you *really* want to see the bad value, you could instead:
        // throw new FormatException($"The input string '{s}' was not in a correct decimal format.");
    }

    private static string ResolveMasterId(string part, string component)
        {
            // Your rule:
            // - if component has a "C" in the component name, use component
            // - if component is null OR does not contain C, use part
            if (!string.IsNullOrWhiteSpace(component) &&
                component.IndexOf('C', StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return component;
            }
            return part;
        }

        #endregion

        #region Prolink dimensions for MTB/STB

        private class ProlinkDimRow
        {
            public string Name { get; set; } = "";
            public decimal? Nominal { get; set; }
            public decimal? TolMinus { get; set; }
            public decimal? TolPlus { get; set; }
            public decimal? Measurement { get; set; }
        }

        private async Task<List<ProlinkDimRow>> LoadProlinkDimsByLocationAsync(
      TestBakeViewModel vm,
      string location)
        {
            var list = new List<ProlinkDimRow>();

            using var conn = new SqlConnection(_prolinkConnStr);
            await conn.OpenAsync();

            const string sql = @"
WITH PartWithFactors AS (
    SELECT
        p.part_id,
        p.record_number,
        p.measure_date,
        qf.qcc_file_desc,
        MAX(CASE WHEN f.factor_desc = @FactorTestBake THEN pf.value END) AS TestBake
    FROM dbo.part p
    JOIN dbo.qcc_file qf      ON p.qcc_file_id = qf.qcc_file_id
    LEFT JOIN dbo.part_factor pf ON p.part_id = pf.part_id
    LEFT JOIN dbo.factor f       ON pf.factor_id = f.factor_id
    WHERE
        -- Department/location filter (MOLDING, SINTERING, etc.)
        qf.edl_desc = @Location
        
        -- 🔍 NEW: filter Prolink parts that CONTAIN the header part string
        AND (
            @PartDesc IS NULL
            OR @PartDesc = ''
            OR qf.qcc_file_desc LIKE '%' + @PartDesc + '%'
        )
    GROUP BY p.part_id, p.record_number, p.measure_date, qf.qcc_file_desc
)
SELECT
    d.dim_desc AS DimName,
    d.nominal   AS Nominal,
    d.tol_minus AS TolMinus,
    d.tol_plus  AS TolPlus,
    CAST(m.value AS DECIMAL(18,5)) AS MeasuredValue
FROM PartWithFactors pw
JOIN dbo.measurement m ON pw.part_id = m.part_id
JOIN dbo.dimension   d ON m.dim_id  = d.dim_id
WHERE
    m.deleted_flag = 0
    AND pw.TestBake = 'Yes'
    AND (@StartTime IS NULL OR pw.measure_date >= @StartTime)
    AND (@EndTime   IS NULL OR pw.measure_date <= @EndTime)
ORDER BY d.dim_desc;
";

            using var cmd = new SqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@FactorTestBake", FactorDescTestBake);

            // MOLDING / SINTERING etc. (matches qf.edl_desc)
            cmd.Parameters.AddWithValue("@Location", location);

            // ✅ use the header part as the substring to search in qcc_file_desc
            cmd.Parameters.AddWithValue("@PartDesc",
                string.IsNullOrWhiteSpace(vm.Header.Part)
                    ? (object)DBNull.Value
                    : vm.Header.Part);

            cmd.Parameters.AddWithValue("@StartTime",
    vm.TestBakeStartTime.HasValue ? vm.TestBakeStartTime.Value : (object)DBNull.Value);
            cmd.Parameters.AddWithValue("@EndTime",
                vm.TestBakeEndTime.HasValue ? vm.TestBakeEndTime.Value : (object)DBNull.Value);


            using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                list.Add(new ProlinkDimRow
                {
                    Name = reader["DimName"]?.ToString() ?? "",
                    Nominal = SafeDecimal(reader["Nominal"]),
                    TolMinus = SafeDecimal(reader["TolMinus"]),
                    TolPlus = SafeDecimal(reader["TolPlus"]),
                    Measurement = SafeDecimal(reader["MeasuredValue"])
                });
            }


            return list;
        }

        #endregion

        #region Merge + fuzzy matching

        private List<CombinedDimensionRow> MergeDimensions(
            List<CombinedDimensionRow> masterRows,
            List<ProlinkDimRow> moldDims,
            List<ProlinkDimRow> sinterDims)
        {
            var result = new List<CombinedDimensionRow>();

            var moldList = moldDims.ToList();
            var sinterList = sinterDims.ToList();

            // 1) Start with master rows
            foreach (var master in masterRows)
            {
                var row = new CombinedDimensionRow
                {
                    MasterMoldName = master.MasterMoldName,
                    MasterMoldLow = master.MasterMoldLow,
                    MasterMoldHigh = master.MasterMoldHigh,
                    MasterMoldNominal = master.MasterMoldNominal,
                    MasterMoldTolMinus = master.MasterMoldTolMinus,
                    MasterMoldTolPlus = master.MasterMoldTolPlus,
                    MasterSinterLow = master.MasterSinterLow,
                    MasterSinterHigh = master.MasterSinterHigh,
                    MasterSinterNominal = master.MasterSinterNominal,
                    MasterSinterTolMinus = master.MasterSinterTolMinus,
                    MasterSinterTolPlus = master.MasterSinterTolPlus
                };

                var bestMold = FindBestMatch(master.MasterMoldName, moldList);
                var bestSinter = FindBestMatch(master.MasterMoldName, sinterList);

                if (bestMold != null)
                {
                    row.MoldName = bestMold.Name;
                    row.MoldNominal = bestMold.Nominal;
                    row.MoldTolMinus = bestMold.TolMinus;
                    row.MoldTolPlus = bestMold.TolPlus;
                    row.MoldMeasurement = bestMold.Measurement;
                    moldList.Remove(bestMold);
                }
                if (bestSinter != null)
                {
                    row.SinterName = bestSinter.Name;
                    row.SinterNominal = bestSinter.Nominal;
                    row.SinterTolMinus = bestSinter.TolMinus;
                    row.SinterTolPlus = bestSinter.TolPlus;
                    row.SinterMeasurement = bestSinter.Measurement;
                    sinterList.Remove(bestSinter);
                }

                result.Add(row);
            }

            // 2) Any leftover mold-only dims
            foreach (var m in moldList)
            {
                result.Add(new CombinedDimensionRow
                {
                    MoldName = m.Name,
                    MoldNominal = m.Nominal,
                    MoldTolMinus = m.TolMinus,
                    MoldTolPlus = m.TolPlus,
                    MoldMeasurement = m.Measurement
                });
            }

            // 3) Any leftover sinter-only dims
            foreach (var s in sinterList)
            {
                result.Add(new CombinedDimensionRow
                {
                    SinterName = s.Name,
                    SinterNominal = s.Nominal,
                    SinterTolMinus = s.TolMinus,
                    SinterTolPlus = s.TolPlus,
                    SinterMeasurement = s.Measurement
                });
            }

            return result
                .OrderBy(r => (r.MasterMoldName ?? "") + (r.MoldName ?? "") + (r.SinterName ?? ""))
                .ToList();
        }

        private ProlinkDimRow? FindBestMatch(string masterName, List<ProlinkDimRow> candidates)
        {
            if (string.IsNullOrWhiteSpace(masterName) || candidates.Count == 0)
                return null;

            double bestScore = 0.0;
            ProlinkDimRow? best = null;

            foreach (var c in candidates)
            {
                var score = NameSimilarity(masterName, c.Name);
                if (score > bestScore)
                {
                    bestScore = score;
                    best = c;
                }
            }

            // 0.5 is a decent starting threshold, tweak if needed
            return bestScore >= 0.5 ? best : null;
        }

        private static string NormalizeName(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return "";
            var chars = s.ToUpperInvariant()
                         .Where(char.IsLetterOrDigit)
                         .ToArray();
            return new string(chars);
        }

        private double NameSimilarity(string a, string b)
        {
            var na = NormalizeName(a);
            var nb = NormalizeName(b);
            if (na.Length == 0 || nb.Length == 0) return 0.0;

            int len = Math.Min(na.Length, nb.Length);
            int same = 0;
            for (int i = 0; i < len; i++)
            {
                if (na[i] == nb[i]) same++;
            }
            return (double)same / Math.Max(na.Length, nb.Length);
        }
        public async Task LoginAsync(string @operator)
        {
            using var conn = new MySqlConnection(_mysqlConnStr);
            await conn.OpenAsync();

            const string sql = @"
INSERT INTO testbake_login (Operator, StartTime, IsActive)
VALUES (@op, @start, 1);";

            using var cmd = new MySqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@op", @operator);
            cmd.Parameters.AddWithValue("@start", DateTime.Now);
            await cmd.ExecuteNonQueryAsync();
        }

        public async Task<List<TestBakeLoginRow>> GetActiveLoginsAsync()
        {
            var list = new List<TestBakeLoginRow>();

            using var conn = new MySqlConnection(_mysqlConnStr);
            await conn.OpenAsync();

            const string sql = @"
SELECT Id, Operator, StartTime,
       Furnace, ProductionNumber, RunNumber,
       Part, Component, TestType, Reason
FROM testbake_login
WHERE IsActive = 1
ORDER BY StartTime DESC;";

            using var cmd = new MySqlCommand(sql, conn);
            using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                list.Add(new TestBakeLoginRow
                {
                    Id = reader.GetInt32("Id"),
                    Operator = reader["Operator"]?.ToString() ?? "",
                    StartTime = reader.GetDateTime("StartTime"),
                    Furnace = reader["Furnace"]?.ToString() ?? "",
                    ProductionNumber = reader["ProductionNumber"]?.ToString(),
                    RunNumber = reader["RunNumber"]?.ToString(),
                    Part = reader["Part"]?.ToString(),
                    Component = reader["Component"]?.ToString(),
                    TestType = reader["TestType"]?.ToString(),
                    Reason = reader["Reason"]?.ToString()
                });
            }

            return list;
        }

        public async Task<TestBakeLoginRow?> GetLoginByIdAsync(int id)
        {
            using var conn = new MySqlConnection(_mysqlConnStr);
            await conn.OpenAsync();

            const string sql = @"
SELECT Id, Operator, StartTime,
       Furnace, ProductionNumber, RunNumber,
       Part, Component, TestType, Reason
FROM testbake_login
WHERE Id = @id;";

            using var cmd = new MySqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@id", id);

            using var reader = await cmd.ExecuteReaderAsync();
            if (!await reader.ReadAsync())
                return null;

            return new TestBakeLoginRow
            {
                Id = reader.GetInt32("Id"),
                Operator = reader["Operator"]?.ToString() ?? "",
                StartTime = reader.GetDateTime("StartTime"),
                Furnace = reader["Furnace"]?.ToString() ?? "",
                ProductionNumber = reader["ProductionNumber"]?.ToString(),
                RunNumber = reader["RunNumber"]?.ToString(),
                Part = reader["Part"]?.ToString(),
                Component = reader["Component"]?.ToString(),
                TestType = reader["TestType"]?.ToString(),
                Reason = reader["Reason"]?.ToString()
            };
        }


        #endregion

        public async Task<int> CreateTestBakeHeaderAsync(TestBakeHeaderRow header)
        {
            using var conn = new MySqlConnection(_mysqlConnStr);
            await conn.OpenAsync();

            const string sqlInsert = @"
INSERT INTO testbake_header
    (LoginId, Operator, ProductionNumber, RunNumber,
     TestType, Reason, ProlinkPart,
     TestBakeStartTime, TestBakeEndTime,
     OutcomeStatus, OutcomeNotes, OutcomeBy, OutcomeDate,
     CreatedAt, UpdatedAt)
VALUES
    (@LoginId, @Operator, @Prod, @Run,
     @TestType, @Reason, @ProlinkPart,
     @StartTime, @EndTime,
     @OutcomeStatus, @OutcomeNotes, @OutcomeBy, @OutcomeDate,
     @CreatedAt, @UpdatedAt);
SELECT LAST_INSERT_ID();";


            using var cmd = new MySqlCommand(sqlInsert, conn);
            cmd.Parameters.AddWithValue("@LoginId", header.LoginId);
            cmd.Parameters.AddWithValue("@Operator", header.Operator);
            cmd.Parameters.AddWithValue("@Prod", (object?)header.ProductionNumber ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@Run", (object?)header.RunNumber ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@TestType", (object?)header.TestType ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@Reason", (object?)header.Reason ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@ProlinkPart", (object?)header.ProlinkPart ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@StartTime", header.TestBakeStartTime);

            cmd.Parameters.AddWithValue("@EndTime", (object?)header.TestBakeEndTime ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@OutcomeStatus", (object?)header.OutcomeStatus ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@OutcomeNotes", (object?)header.OutcomeNotes ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@OutcomeBy", (object?)header.OutcomeBy ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@OutcomeDate", (object?)header.OutcomeDate ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@CreatedAt", header.CreatedAt);
            cmd.Parameters.AddWithValue("@UpdatedAt", (object?)header.UpdatedAt ?? DBNull.Value);

            var idObj = await cmd.ExecuteScalarAsync();
            var id = Convert.ToInt32(idObj);

            // compute filename (matches controller download name)
            var fileName = $"TestBake_{id}.pdf";

            const string sqlUpdate = @"
UPDATE testbake_header
SET FileName = @FileName
WHERE Id = @Id;";

            using (var cmd2 = new MySqlCommand(sqlUpdate, conn))
            {
                cmd2.Parameters.AddWithValue("@FileName", fileName);
                cmd2.Parameters.AddWithValue("@Id", id);
                await cmd2.ExecuteNonQueryAsync();
            }

            return id;
        }

        public async Task<TestBakeHeaderRow?> GetHeaderByIdAsync(int id)
        {
            using var conn = new MySqlConnection(_mysqlConnStr);
            await conn.OpenAsync();

            const string sql = @"SELECT * FROM testbake_header WHERE Id = @Id;";

            using var cmd = new MySqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@Id", id);

            using var reader = await cmd.ExecuteReaderAsync();
            if (!await reader.ReadAsync())
                return null;

            return new TestBakeHeaderRow
            {
                Id = reader.GetInt32("Id"),
                LoginId = reader.GetInt32("LoginId"),
                Operator = reader["Operator"]?.ToString() ?? "",
                ProductionNumber = reader["ProductionNumber"]?.ToString(),
                RunNumber = reader["RunNumber"]?.ToString(),
                TestType = reader["TestType"]?.ToString(),
                Reason = reader["Reason"]?.ToString(),
                ProlinkPart = reader["ProlinkPart"]?.ToString(),
                TestBakeStartTime = reader.GetDateTime("TestBakeStartTime"),

                TestBakeEndTime = reader["TestBakeEndTime"] as DateTime?,
                OutcomeStatus = reader["OutcomeStatus"]?.ToString(),
                OutcomeNotes = reader["OutcomeNotes"]?.ToString(),
                OutcomeBy = reader["OutcomeBy"]?.ToString(),
                OutcomeDate = reader["OutcomeDate"] as DateTime?,
                FileName = reader["FileName"]?.ToString(),     // <-- NEW
                CreatedAt = reader.GetDateTime("CreatedAt"),
                UpdatedAt = reader["UpdatedAt"] as DateTime?
            };
        }

        public async Task<byte[]> GenerateTestBakePdfAsync(TestBakeViewModel vm)
        {
            // 🔹 If dimensions are empty, reload the full VM using the header id
            if ((vm.Dimensions == null || vm.Dimensions.Count == 0) && vm.HeaderId.HasValue)
            {
                vm = await LoadInitialAsync(
                    productionNumber: vm.Header.ProductionNumber ?? "",
                    runNumber: vm.Header.RunNumber ?? "",
                    testType: vm.Header.TestType ?? "",
                    reason: vm.Header.Reason ?? "",
                    testBakeStartTime: vm.TestBakeStartTime,
                    testBakeEndTime: vm.TestBakeEndTime,
                    headerId: vm.HeaderId
                );
            }

            using var ms = new MemoryStream();

            var writer = new PdfWriter(ms);
            var pdf = new PdfDocument(writer);
            var doc = new Document(pdf, PageSize.A4.Rotate());
            doc.SetMargins(20, 20, 20, 20);

            PdfFont font = PdfFontFactory.CreateFont(StandardFonts.HELVETICA);
            PdfFont fontBold = PdfFontFactory.CreateFont(StandardFonts.HELVETICA_BOLD);

            doc.Add(new Paragraph("Initial Testbake Sheet")
                .SetFont(fontBold)
                .SetFontSize(16)
                .SetTextAlignment(TextAlignment.CENTER));

            doc.Add(new Paragraph($"Date: {vm.Header.Date:g}")
                .SetFont(font)
                .SetFontSize(10));
            doc.Add(new Paragraph($"Part: {vm.Header.Part}    Component: {vm.Header.Component}")
                .SetFontSize(10));
            doc.Add(new Paragraph($"Prod #: {vm.Header.ProductionNumber}    Run #: {vm.Header.RunNumber}")
                .SetFontSize(10));
            doc.Add(new Paragraph($"Machine: {vm.Header.MachineNumber}    Material: {vm.Header.Material}    Lot: {vm.Header.LotNumber}")
                .SetFontSize(10));
            doc.Add(new Paragraph($"Test Type: {vm.Header.TestType}    Reason: {vm.Header.Reason}")
                .SetFontSize(10));
            doc.Add(new Paragraph($"Tested By: {vm.Header.TestedBy}    Tool #: {vm.Header.ToolNumber}")
                .SetFontSize(10));

            doc.Add(new Paragraph(" ")); // spacer

            // === DIMENSION TABLE (limits only) ===
            var table = new Table(new float[]
            {
        3, 2, 2, 2, 2,       // Master name + Master Mold L/H + Master Sinter L/H
        3, 2, 2, 2,          // Mold section
        3, 2, 2, 2           // Sinter section
            });
            table.SetWidth(UnitValue.CreatePercentValue(100));

            string[] headers = new[]
            {
        "Master Name",
        "Master Mold Low", "Master Mold High",
        "Master Sinter Low", "Master Sinter High",
        "Mold Name", "Mold Low", "Mold High", "Mold Meas",
        "Sinter Name", "Sinter Low", "Sinter High", "Sinter Meas"
    };

            foreach (var h in headers)
            {
                table.AddHeaderCell(
                    new Cell().Add(
                        new Paragraph(h)
                            .SetFont(fontBold)
                            .SetFontSize(8)
                    )
                );
            }

            // If STILL no dimensions, put a single row to make it obvious
            if (vm.Dimensions == null || vm.Dimensions.Count == 0)
            {
                var cell = new Cell(1, headers.Length)
                    .Add(new Paragraph("No dimensions found for this test bake.")
                        .SetFontSize(8));
                table.AddCell(cell);
            }
            else
            {
                foreach (var row in vm.Dimensions)
                {
                    decimal? moldLow = null, moldHigh = null;
                    decimal? sinterLow = null, sinterHigh = null;

                    if (row.MoldNominal.HasValue && row.MoldTolMinus.HasValue)
                        moldLow = row.MoldNominal.Value + row.MoldTolMinus.Value;
                    if (row.MoldNominal.HasValue && row.MoldTolPlus.HasValue)
                        moldHigh = row.MoldNominal.Value + row.MoldTolPlus.Value;

                    if (row.SinterNominal.HasValue && row.SinterTolMinus.HasValue)
                        sinterLow = row.SinterNominal.Value + row.SinterTolMinus.Value;
                    if (row.SinterNominal.HasValue && row.SinterTolPlus.HasValue)
                        sinterHigh = row.SinterNominal.Value + row.SinterTolPlus.Value;

                    // Master
                    table.AddCell(new Paragraph(row.MasterMoldName ?? "").SetFontSize(8));

                    table.AddCell(new Paragraph(row.MasterMoldLow?.ToString("0.0000") ?? "")
                        .SetFontSize(8));
                    table.AddCell(new Paragraph(row.MasterMoldHigh?.ToString("0.0000") ?? "")
                        .SetFontSize(8));

                    table.AddCell(new Paragraph(row.MasterSinterLow?.ToString("0.0000") ?? "")
                        .SetFontSize(8));
                    table.AddCell(new Paragraph(row.MasterSinterHigh?.ToString("0.0000") ?? "")
                        .SetFontSize(8));

                    // Mold (Prolink)
                    table.AddCell(new Paragraph(row.MoldName ?? "").SetFontSize(8));
                    table.AddCell(new Paragraph(moldLow?.ToString("0.0000") ?? "").SetFontSize(8));
                    table.AddCell(new Paragraph(moldHigh?.ToString("0.0000") ?? "").SetFontSize(8));
                    table.AddCell(new Paragraph(row.MoldMeasurement?.ToString("0.0000") ?? "")
                        .SetFontSize(8));

                    // Sinter (Prolink)
                    table.AddCell(new Paragraph(row.SinterName ?? "").SetFontSize(8));
                    table.AddCell(new Paragraph(sinterLow?.ToString("0.0000") ?? "").SetFontSize(8));
                    table.AddCell(new Paragraph(sinterHigh?.ToString("0.0000") ?? "").SetFontSize(8));
                    table.AddCell(new Paragraph(row.SinterMeasurement?.ToString("0.0000") ?? "")
                        .SetFontSize(8));
                }
            }

            doc.Add(table);
            doc.Close();

            return ms.ToArray();
        }

        public async Task<int> GetHeaderCountAsync()
        {
            using var conn = new MySqlConnection(_mysqlConnStr);
            await conn.OpenAsync();

            const string sql = "SELECT COUNT(*) FROM testbake_header;";
            using var cmd = new MySqlCommand(sql, conn);
            var obj = await cmd.ExecuteScalarAsync();
            return Convert.ToInt32(obj);
        }

        public async Task<List<TestBakeHeaderRow>> GetRecentHeadersAsync(int page, int pageSize)
        {
            var list = new List<TestBakeHeaderRow>();
            int offset = (page - 1) * pageSize;

            using var conn = new MySqlConnection(_mysqlConnStr);
            await conn.OpenAsync();

            const string sql = @"
SELECT *
FROM testbake_header
ORDER BY CreatedAt DESC
LIMIT @Offset, @PageSize;";

            using var cmd = new MySqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@Offset", offset);
            cmd.Parameters.AddWithValue("@PageSize", pageSize);

            using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                list.Add(new TestBakeHeaderRow
                {
                    Id = reader.GetInt32("Id"),
                    LoginId = reader.GetInt32("LoginId"),
                    Operator = reader["Operator"]?.ToString() ?? "",
                    ProductionNumber = reader["ProductionNumber"]?.ToString(),
                    RunNumber = reader["RunNumber"]?.ToString(),
                    TestType = reader["TestType"]?.ToString(),
                    Reason = reader["Reason"]?.ToString(),
                    ProlinkPart = reader["ProlinkPart"]?.ToString(),
                    TestBakeStartTime = reader.GetDateTime("TestBakeStartTime"),
                    OutcomeStatus = reader["OutcomeStatus"]?.ToString(),
                    OutcomeNotes = reader["OutcomeNotes"]?.ToString(),
                    OutcomeBy = reader["OutcomeBy"]?.ToString(),
                    OutcomeDate = reader["OutcomeDate"] as DateTime?,
                    FileName = reader["FileName"]?.ToString(),
                    CreatedAt = reader.GetDateTime("CreatedAt"),
                    UpdatedAt = reader["UpdatedAt"] as DateTime?
                });
            }

            return list;
        }

        private async Task LoadMachineMaterialLotFromRunsAsync(TestBakeViewModel vm)
        {
            // Need at least Prod or Run to search
            if (string.IsNullOrWhiteSpace(vm.Header.ProductionNumber) &&
                string.IsNullOrWhiteSpace(vm.Header.RunNumber))
                return;

            string? prod = string.IsNullOrWhiteSpace(vm.Header.ProductionNumber)
                ? null
                : vm.Header.ProductionNumber;
            string? run = string.IsNullOrWhiteSpace(vm.Header.RunNumber)
                ? null
                : vm.Header.RunNumber;

            using var conn = new MySqlConnection(_mysqlConnStr);
            await conn.OpenAsync();

            // ------------------ PRESS SIDE (pressrun THEN presssetup) ------------------
            string? pressMachine = null;
            string? pressMaterial = null;
            string? pressLot = null;

            bool foundPressRun = false;

            const string sqlPress = @"
SELECT Machine, MaterialCode, LotNumber
FROM pressrun
WHERE (@Prod IS NULL OR ProdNumber = @Prod)
  AND (@Run  IS NULL OR Run        = @Run)
ORDER BY StartDateTime DESC
LIMIT 1;";

            using (var cmdPress = new MySqlCommand(sqlPress, conn))
            {
                cmdPress.Parameters.AddWithValue("@Prod", (object?)prod ?? DBNull.Value);
                cmdPress.Parameters.AddWithValue("@Run", (object?)run ?? DBNull.Value);

                using var r = await cmdPress.ExecuteReaderAsync();
                if (await r.ReadAsync())
                {
                    foundPressRun = true;
                    pressMachine = r["Machine"]?.ToString();
                    pressMaterial = r["MaterialCode"]?.ToString();
                    pressLot = r["LotNumber"]?.ToString();
                }
            }

            // 🔁 FALLBACK: if nothing found in pressrun, look in presssetup
            if (!foundPressRun)
            {
                const string sqlPressSetup = @"
SELECT machine, materialCode, lotNumber
FROM presssetup
WHERE (@Prod IS NULL OR prodNumber = @Prod)
  AND (@Run  IS NULL OR run        = @Run)
ORDER BY startDateTime DESC
LIMIT 1;";

                using (var cmdSetup = new MySqlCommand(sqlPressSetup, conn))
                {
                    cmdSetup.Parameters.AddWithValue("@Prod", (object?)prod ?? DBNull.Value);
                    cmdSetup.Parameters.AddWithValue("@Run", (object?)run ?? DBNull.Value);

                    using var rs = await cmdSetup.ExecuteReaderAsync();
                    if (await rs.ReadAsync())
                    {
                        pressMachine = rs["machine"]?.ToString();
                        pressMaterial = rs["materialCode"]?.ToString();
                        pressLot = rs["lotNumber"]?.ToString();
                    }
                }
            }

            // ------------------ SINTER SIDE (unchanged) ------------------
            string? sinterMachine = null;
            string? sinterMaterial = null;
            string? sinterLot = null;

            const string sqlSinter = @"
SELECT Oven, MaterialCode, LotNumber
FROM sinterrun
WHERE (@Prod IS NULL OR ProdNumber = @Prod)
  AND (@Run  IS NULL OR Run        = @Run)
ORDER BY StartDateTime DESC
LIMIT 1;";

            using (var cmdSinter = new MySqlCommand(sqlSinter, conn))
            {
                cmdSinter.Parameters.AddWithValue("@Prod", (object?)prod ?? DBNull.Value);
                cmdSinter.Parameters.AddWithValue("@Run", (object?)run ?? DBNull.Value);

                using var r = await cmdSinter.ExecuteReaderAsync();
                if (await r.ReadAsync())
                {
                    sinterMachine = r["Oven"]?.ToString();
                    sinterMaterial = r["MaterialCode"]?.ToString();
                    sinterLot = r["LotNumber"]?.ToString();
                }
            }

            // ------------------ COMBINE INTO HEADER ------------------

            // Machine = BOTH press + sinter, comma separated (only non-empty)
            var machines = new List<string>();
            if (!string.IsNullOrWhiteSpace(pressMachine)) machines.Add(pressMachine);
            if (!string.IsNullOrWhiteSpace(sinterMachine)) machines.Add(sinterMachine);
            if (machines.Any())
                vm.Header.MachineNumber = string.Join(", ", machines);

            // Material, Lot: prefer sinterrun if present, else press side (pressrun or presssetup)
            var material = !string.IsNullOrWhiteSpace(sinterMaterial) ? sinterMaterial : pressMaterial;
            var lot = !string.IsNullOrWhiteSpace(sinterLot) ? sinterLot : pressLot;

            if (!string.IsNullOrWhiteSpace(material))
                vm.Header.Material = material;
            if (!string.IsNullOrWhiteSpace(lot))
                vm.Header.LotNumber = lot;
        }

        public async Task PlaceTestBakeAsync(TestBakeLoginRow login)
        {
            using var conn = new MySqlConnection(_mysqlConnStr);
            await conn.OpenAsync();

            const string sql = @"
INSERT INTO testbake_login
    (Operator, StartTime, IsActive,
     Furnace, ProductionNumber, RunNumber,
     Part, Component, TestType, Reason)
VALUES
    (@Operator, @StartTime, 1,
     @Furnace, @Prod, @Run,
     @Part, @Component, @TestType, @Reason);";

            using var cmd = new MySqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@Operator", login.Operator);
            cmd.Parameters.AddWithValue("@StartTime", login.StartTime);
            cmd.Parameters.AddWithValue("@Furnace", (object?)login.Furnace ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@Prod", (object?)login.ProductionNumber ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@Run", (object?)login.RunNumber ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@Part", (object?)login.Part ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@Component", (object?)login.Component ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@TestType", (object?)login.TestType ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@Reason", (object?)login.Reason ?? DBNull.Value);

            await cmd.ExecuteNonQueryAsync();
        }
        public async Task<int> FinishTestBakeAsync(int loginId)
        {
            // 1) Get login
            var login = await GetLoginByIdAsync(loginId);
            if (login == null)
                throw new InvalidOperationException($"No active test bake found for Id {loginId}.");


            var now = DateTime.Now;

            // 2) Build header row using login info
            var headerRow = new TestBakeHeaderRow
            {
                LoginId = login.Id,
                Operator = login.Operator,
                ProductionNumber = login.ProductionNumber,
                RunNumber = login.RunNumber,
                TestType = login.TestType,
                Reason = login.Reason,
                ProlinkPart = login.Part,
                TestBakeStartTime = login.StartTime,
                TestBakeEndTime = now,
                OutcomeStatus = null,
                OutcomeNotes = null,
                OutcomeBy = null,
                OutcomeDate = null,
                CreatedAt = DateTime.Now,
                UpdatedAt = null
            };

            // 3) Create header in MySQL (also sets FileName)
            var headerId = await CreateTestBakeHeaderAsync(headerRow);

            // 4) Load full VM (Prolink + masterm)
            var vm = await LoadInitialAsync(
                productionNumber: headerRow.ProductionNumber ?? "",
                runNumber: headerRow.RunNumber ?? "",
                testType: headerRow.TestType ?? "",
                reason: headerRow.Reason ?? "",
                testBakeStartTime: headerRow.TestBakeStartTime,
                testBakeEndTime: headerRow.TestBakeEndTime,
                headerId: headerId);

            // Ensure TestedBy is set (operator)
            vm.Header.TestedBy = headerRow.Operator;

            // 5) Generate PDF bytes
            var pdfBytes = await GenerateTestBakePdfAsync(vm);

            // 6) Save PDF to uploads folder
            var fileName = $"TestBake_{headerId}.pdf";
            var fullPath = System.IO.Path.Combine(_uploadPath, fileName);
            await File.WriteAllBytesAsync(fullPath, pdfBytes);

            // 7) Mark login inactive
            using (var conn = new MySqlConnection(_mysqlConnStr))
            {
                await conn.OpenAsync();
                const string sql = @"UPDATE testbake_login SET IsActive = 0 WHERE Id = @Id;";
                using var cmd = new MySqlCommand(sql, conn);
                cmd.Parameters.AddWithValue("@Id", loginId);
                await cmd.ExecuteNonQueryAsync();
            }

            return headerId;
        }
        public async Task<byte[]> GetPdfFromDiskAsync(int headerId)
        {
            // 1) Load header to know the file name and core fields
            var headerRow = await GetHeaderByIdAsync(headerId);
            if (headerRow == null)
                throw new InvalidOperationException($"TestBake header {headerId} not found.");

            // 2) Determine file name
            var fileName = headerRow.FileName;
            if (string.IsNullOrWhiteSpace(fileName))
            {
                // fall back to convention
                fileName = $"TestBake_{headerId}.pdf";

                // optionally update DB once so future calls have FileName set
                using var conn = new MySqlConnection(_mysqlConnStr);
                await conn.OpenAsync();
                const string sqlUpdate = @"UPDATE testbake_header SET FileName = @FileName WHERE Id = @Id;";
                using var cmd = new MySqlCommand(sqlUpdate, conn);
                cmd.Parameters.AddWithValue("@FileName", fileName);
                cmd.Parameters.AddWithValue("@Id", headerId);
                await cmd.ExecuteNonQueryAsync();
            }

            var fullPath = Path.Combine(_uploadPath, fileName);

            // 3) If the file exists on the share, return it
            if (System.IO.File.Exists(fullPath))
            {
                return await System.IO.File.ReadAllBytesAsync(fullPath);
            }

            // 4) Fallback: regenerate from Prolink + masters + runs,
            //    save it into the uploads folder, then return bytes.
            var vm = await LoadInitialAsync(
                productionNumber: headerRow.ProductionNumber ?? "",
                runNumber: headerRow.RunNumber ?? "",
                testType: headerRow.TestType ?? "",
                reason: headerRow.Reason ?? "",
                testBakeStartTime: headerRow.TestBakeStartTime,
                testBakeEndTime: headerRow.TestBakeEndTime,
                headerId: headerRow.Id);

            var pdfBytes = await GenerateTestBakePdfAsync(vm);

            Directory.CreateDirectory(_uploadPath); // just in case
            await System.IO.File.WriteAllBytesAsync(fullPath, pdfBytes);

            return pdfBytes;
        }

    }
}
