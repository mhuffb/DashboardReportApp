using DashboardReportApp.Models;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using MySql.Data.MySqlClient;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading.Tasks;

namespace DashboardReportApp.Services
{
    public interface ITestBakeService
    {
        Task<TestBakeViewModel> LoadInitialAsync(
            string productionNumber,
            string runNumber,
            string department,
            string testType,
            string reason);
    }

    public class TestBakeService : ITestBakeService
    {
        private readonly string _prolinkConnStr;   // SQLExpressConnection
        private readonly string _mastersConnStr;   // SqlServerConnectionsinTSQL
        private readonly string _mysqlConnStr;     // MySQLConnection

        // Adjust these to match whatever factor_desc names you use in Prolink
        private const string FactorDescProdNumber = "Prod Number";
        private const string FactorDescRunNumber = "Run Number";
        private const string FactorDescTBType = "TB Type";   // values: "mtb", "stb"

        public TestBakeService(IConfiguration config)
        {
            _prolinkConnStr = config.GetConnectionString("SQLExpressConnection");
            _mastersConnStr = config.GetConnectionString("SqlServerConnectionsinTSQL");
            _mysqlConnStr = config.GetConnectionString("MySQLConnection");
        }

        public async Task<TestBakeViewModel> LoadInitialAsync(
            string productionNumber,
            string runNumber,
            string department,
            string testType,
            string reason)
        {
            var vm = new TestBakeViewModel
            {
                SearchProductionNumber = productionNumber ?? "",
                SearchRunNumber = runNumber ?? "",
                SearchDepartment = department ?? "",
                SearchTestType = testType ?? "",
                SearchReason = reason ?? ""
            };

            try
            {
                // 1) Header (part/component/date) from Prolink via factors
                await LoadHeaderFromProlinkAsync(vm);

                // copy search info into header
                vm.Header.ProductionNumber = vm.SearchProductionNumber;
                vm.Header.RunNumber = vm.SearchRunNumber;
                vm.Header.Department = vm.SearchDepartment;
                vm.Header.TestType = vm.SearchTestType;
                vm.Header.Reason = vm.SearchReason;

                // 2) Tool numbers + punches / testedBy from toolinginventory (MySQL)
                await LoadToolNumbersAndDetailsAsync(vm);

                // 3) Master limits from masterm (SinTSQL)
                var masterRows = await LoadMasterDimensionsAsync(vm.Header.Part, vm.Header.Component);

                // 4) Prolink MOLD + SINTER dimensions using mtb / stb
                var moldDims = await LoadProlinkDimsAsync(vm, "mtb"); // molding test bake
                var sinterDims = await LoadProlinkDimsAsync(vm, "stb"); // sintering test bake

                // 5) Merge master + Prolink using fuzzy name match
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

            // This query assumes you've added part_factor rows for:
            // - Prod Number
            // - Run Number
            // - TB Type (mtb/stb)
            // We just need one matching record to get part & component & date.
            // You can refine this later if you have a dedicated test-bake qcc_file_desc pattern.

            const string sql = @"
WITH PartWithFactors AS (
    SELECT
        p.part_id,
        p.record_number,
        p.measure_date,
        qf.qcc_file_desc,
        MAX(CASE WHEN f.factor_desc = @FactorProd THEN pf.value END) AS ProdNumber,
        MAX(CASE WHEN f.factor_desc = @FactorRun  THEN pf.value END) AS RunNumber
        -- TB Type factor isn't needed for header
    FROM dbo.part p
    JOIN dbo.qcc_file qf    ON p.qcc_file_id = qf.qcc_file_id
    LEFT JOIN dbo.part_factor pf ON p.part_id = pf.part_id
    LEFT JOIN dbo.factor f       ON pf.factor_id = f.factor_id
    WHERE (@ProdNumber IS NULL OR @ProdNumber = '' OR pf.value = @ProdNumber OR 1 = 1)
    GROUP BY p.part_id, p.record_number, p.measure_date, qf.qcc_file_desc
)
SELECT TOP 1
    part_id,
    record_number,
    measure_date,
    qcc_file_desc,
    ProdNumber,
    RunNumber
FROM PartWithFactors
WHERE (@ProdNumber IS NULL OR ProdNumber = @ProdNumber)
  AND (@RunNumber  IS NULL OR RunNumber  = @RunNumber)
ORDER BY measure_date DESC;";

            using var cmd = new SqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@FactorProd", FactorDescProdNumber);
            cmd.Parameters.AddWithValue("@FactorRun", FactorDescRunNumber);

            cmd.Parameters.AddWithValue("@ProdNumber",
                string.IsNullOrWhiteSpace(vm.SearchProductionNumber) ? (object)DBNull.Value : vm.SearchProductionNumber);
            cmd.Parameters.AddWithValue("@RunNumber",
                string.IsNullOrWhiteSpace(vm.SearchRunNumber) ? (object)DBNull.Value : vm.SearchRunNumber);

            using var reader = await cmd.ExecuteReaderAsync();
            if (!await reader.ReadAsync())
            {
                throw new Exception("No matching Prolink record found for that production/run number.");
            }

            int partId = reader.GetInt32(reader.GetOrdinal("part_id"));
            vm.SearchProductionNumber = reader["ProdNumber"]?.ToString() ?? vm.SearchProductionNumber;
            vm.SearchRunNumber = reader["RunNumber"]?.ToString() ?? vm.SearchRunNumber;
            vm.Header.Date = reader["measure_date"] as DateTime?;
            string qccDesc = reader["qcc_file_desc"]?.ToString() ?? "";

            // For now, treat qcc_file_desc as the raw "part string" (Prolink style).
            // If you want to parse it to Sintergy part/component, you can use your SharedService.ParseProlinkPartNumber, or
            // look it up in your own parts table. Here we just put it in Part.
            vm.SearchPart = qccDesc;
            vm.Header.Part = qccDesc;
            vm.Header.Component = "";  // you can later map to your component if needed

            // TODO: if you store machine/material/lot as factors too,
            // you can call another helper to read those from part_factor here.
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
FROM toolinginventory
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
    finish_dim1,   -- sintering low
    finish_dim2    -- sintering high
FROM masterm
WHERE master_id = @masterId
  AND ([desc] IS NOT NULL)
ORDER BY [desc];";

            using var cmd = new SqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@masterId", masterId);

            using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var lowMold = reader["dim1"] == DBNull.Value ? (decimal?)null : Convert.ToDecimal(reader["dim1"]);
                var highMold = reader["dim2"] == DBNull.Value ? (decimal?)null : Convert.ToDecimal(reader["dim2"]);
                var lowSinter = reader["finish_dim1"] == DBNull.Value ? (decimal?)null : Convert.ToDecimal(reader["finish_dim1"]);
                var highSinter = reader["finish_dim2"] == DBNull.Value ? (decimal?)null : Convert.ToDecimal(reader["finish_dim2"]);

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

        private async Task<List<ProlinkDimRow>> LoadProlinkDimsAsync(TestBakeViewModel vm, string tbTypeValue)
        {
            var list = new List<ProlinkDimRow>();

            using var conn = new SqlConnection(_prolinkConnStr);
            await conn.OpenAsync();

            // This query assumes:
            // - Prod Number & Run Number are stored as factors
            // - TB Type factor holds "mtb" or "stb"
            // Adjust factor_desc names & table aliases as needed.

            const string sql = @"
WITH PartWithFactors AS (
    SELECT
        p.part_id,
        p.record_number,
        p.measure_date,
        qf.qcc_file_desc,
        MAX(CASE WHEN f.factor_desc = @FactorProd   THEN pf.value END) AS ProdNumber,
        MAX(CASE WHEN f.factor_desc = @FactorRun    THEN pf.value END) AS RunNumber,
        MAX(CASE WHEN f.factor_desc = @FactorTBType THEN pf.value END) AS TBType
    FROM dbo.part p
    JOIN dbo.qcc_file qf    ON p.qcc_file_id = qf.qcc_file_id
    LEFT JOIN dbo.part_factor pf ON p.part_id = pf.part_id
    LEFT JOIN dbo.factor f       ON pf.factor_id = f.factor_id
    GROUP BY p.part_id, p.record_number, p.measure_date, qf.qcc_file_desc
)
SELECT
    d.dim_desc AS DimName,
    d.nominal  AS Nominal,
    d.tol_minus AS TolMinus,
    d.tol_plus  AS TolPlus,
    CAST(m.value AS DECIMAL(18,5)) AS MeasuredValue
FROM PartWithFactors pw
JOIN dbo.measurement m ON pw.part_id = m.part_id
JOIN dbo.dimension   d ON m.dim_id  = d.dim_id
WHERE
    m.deleted_flag = 0
    AND pw.ProdNumber = @ProdNumber
    AND pw.RunNumber  = @RunNumber
    AND pw.TBType     = @TBType
ORDER BY d.dim_desc;";

            using var cmd = new SqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@FactorProd", FactorDescProdNumber);
            cmd.Parameters.AddWithValue("@FactorRun", FactorDescRunNumber);
            cmd.Parameters.AddWithValue("@FactorTBType", FactorDescTBType);

            cmd.Parameters.AddWithValue("@ProdNumber", vm.Header.ProductionNumber);
            cmd.Parameters.AddWithValue("@RunNumber", vm.Header.RunNumber);
            cmd.Parameters.AddWithValue("@TBType", tbTypeValue);

            using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                list.Add(new ProlinkDimRow
                {
                    Name = reader["DimName"]?.ToString() ?? "",
                    Nominal = reader["Nominal"] == DBNull.Value ? (decimal?)null : Convert.ToDecimal(reader["Nominal"]),
                    TolMinus = reader["TolMinus"] == DBNull.Value ? (decimal?)null : Convert.ToDecimal(reader["TolMinus"]),
                    TolPlus = reader["TolPlus"] == DBNull.Value ? (decimal?)null : Convert.ToDecimal(reader["TolPlus"]),
                    Measurement = reader["MeasuredValue"] == DBNull.Value ? (decimal?)null : Convert.ToDecimal(reader["MeasuredValue"])
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

        #endregion
    }
}
