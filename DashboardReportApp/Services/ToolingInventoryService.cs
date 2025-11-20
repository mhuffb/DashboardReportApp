using System;
using System.Collections.Generic;
using System.Data;
using System.Threading.Tasks;
using DashboardReportApp.Models;
using MySql.Data.MySqlClient;

namespace DashboardReportApp.Services
{
    public class ToolingInventoryService
    {
        private readonly string _conn;

        public ToolingInventoryService(IConfiguration configuration)
        {
            _conn = configuration.GetConnectionString("MySQLConnection");
        }

       

        public async Task<List<ToolItemModel>> GetAllAsync()
        {
            var list = new List<ToolItemModel>();
            const string sql = SelectCols + " ORDER BY Id ASC;";

            await using var conn = new MySqlConnection(_conn);
            await conn.OpenAsync();

            await using var cmd = new MySqlCommand(sql, conn);
            await using var r = await cmd.ExecuteReaderAsync(CommandBehavior.CloseConnection);

            while (await r.ReadAsync())
            {
                list.Add(Map(r));
            }

            return list;
        }

        public async Task<ToolItemModel?> GetByIdAsync(int id)
        {
            const string sql = SelectCols + " WHERE Id=@id;";

            await using var conn = new MySqlConnection(_conn);
            await conn.OpenAsync();

            await using var cmd = new MySqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@id", id);

            await using var r = await cmd.ExecuteReaderAsync(CommandBehavior.CloseConnection);
            if (await r.ReadAsync())
                return Map(r);

            return null;
        }

        // Services/ToolingInventoryService.cs

        private const string SelectCols = @"
SELECT Id, AssemblyNumber, ToolNumber, Location, ToolItem,
       `Condition`, `Status`, UnavailableReason, DateUnavailable, EstimatedAvailableDate
FROM tooling_inventory";

        public async Task<int> CreateAsync(ToolItemModel m)
        {
            const string sql = @"
INSERT INTO tooling_inventory
(AssemblyNumber, ToolNumber, Location, ToolItem, `Condition`, `Status`,
 UnavailableReason, DateUnavailable, EstimatedAvailableDate)
VALUES
(@AssemblyNumber, @ToolNumber, @Location, @ToolItem, @Condition, @Status,
 @UnavailableReason, @DateUnavailable, @EstimatedAvailableDate);
SELECT LAST_INSERT_ID();";

            await using var conn = new MySqlConnection(_conn);
            await conn.OpenAsync();

            await using var cmd = new MySqlCommand(sql, conn);
            Bind(cmd, m);

            var idObj = await cmd.ExecuteScalarAsync();
            return idObj is null ? 0 : Convert.ToInt32(idObj);
        }

        public async Task UpdateAsync(ToolItemModel m)
        {
            const string sql = @"
UPDATE tooling_inventory SET
 AssemblyNumber=@AssemblyNumber,
 ToolNumber=@ToolNumber,
 Location=@Location,
 ToolItem=@ToolItem,
 `Condition`=@Condition,
 `Status`=@Status,
 UnavailableReason=@UnavailableReason,
 DateUnavailable=@DateUnavailable,
 EstimatedAvailableDate=@EstimatedAvailableDate
WHERE Id=@Id;";

            await using var conn = new MySqlConnection(_conn);
            await conn.OpenAsync();

            await using var cmd = new MySqlCommand(sql, conn);
            Bind(cmd, m);
            cmd.Parameters.AddWithValue("@Id", m.Id);

            await cmd.ExecuteNonQueryAsync();
        }

        private static void Bind(MySqlCommand cmd, ToolItemModel m)
        {
            cmd.Parameters.AddWithValue("@AssemblyNumber", m.AssemblyNumber);
            cmd.Parameters.AddWithValue("@ToolNumber", m.ToolNumber);
            cmd.Parameters.AddWithValue("@Location", (object?)m.Location ?? DBNull.Value);

            cmd.Parameters.AddWithValue("@ToolItem", m.ToolItem); // required string

            cmd.Parameters.AddWithValue("@Condition", (object?)m.Condition?.ToString() ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@Status", (object?)m.Status?.ToString() ?? DBNull.Value);

            cmd.Parameters.AddWithValue("@UnavailableReason", (object?)m.UnavailableReason ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@DateUnavailable", (object?)m.DateUnavailable ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@EstimatedAvailableDate", (object?)m.EstimatedAvailableDate ?? DBNull.Value);
        }

        private static ToolItemModel Map(MySqlDataReader r)
        {
            static string? S(MySqlDataReader rr, string col) =>
                rr[col] == DBNull.Value ? null : rr.GetString(col);

            var condStr = S(r, "Condition");
            var statStr = S(r, "Status");

            ToolCondition? cond = null;
            if (!string.IsNullOrWhiteSpace(condStr) &&
                Enum.TryParse<ToolCondition>(condStr, true, out var condVal))
                cond = condVal;

            ToolStatus? stat = null;
            if (!string.IsNullOrWhiteSpace(statStr) &&
                Enum.TryParse<ToolStatus>(statStr, true, out var statVal))
                stat = statVal;

            return new ToolItemModel
            {
                Id = r.GetInt32("Id"),
                AssemblyNumber = r.GetString("AssemblyNumber"), 
                ToolNumber = r.GetString("ToolNumber"),
                ToolItem = r.GetString("ToolItem"),

                Location = S(r, "Location"),
                Condition = cond,
                Status = stat,

                UnavailableReason = S(r, "UnavailableReason"),
                DateUnavailable = r["DateUnavailable"] == DBNull.Value ? null : r.GetDateTime("DateUnavailable"),
                EstimatedAvailableDate = r["EstimatedAvailableDate"] == DBNull.Value ? null : r.GetDateTime("EstimatedAvailableDate")
            };
        }


        // Services/ToolingInventoryService.cs  (new helper)
        public async Task<List<string>> GetDistinctToolItemsAsync()
        {
            var list = new List<string>();
            await using var conn = new MySqlConnection(_conn);
            await conn.OpenAsync();

            // Does tooling_history_items exist?
            const string existsSql = @"
        SELECT COUNT(*) FROM information_schema.tables
        WHERE table_schema = DATABASE() AND table_name = 'tooling_history_item';";
            int hasHistory;
            await using (var existsCmd = new MySqlCommand(existsSql, conn))
                hasHistory = Convert.ToInt32(await existsCmd.ExecuteScalarAsync());

            var sql = @"
        SELECT DISTINCT ToolItem
        FROM tooling_inventory
        WHERE ToolItem IS NOT NULL AND ToolItem <> ''" +
                (hasHistory > 0 ? @"
        UNION
        SELECT DISTINCT ToolItem
        FROM tooling_history_item
        WHERE ToolItem IS NOT NULL AND ToolItem <> ''" : "") + @"
        ORDER BY 1;";

            await using var cmd = new MySqlCommand(sql, conn);
            await using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync())
                list.Add(r.GetString(0));

            return list;
        }

        public async Task<ToolItemModel?> FindByKeyAsync(string assembly, string toolItem, string toolNumber)
        {
            const string sql = SelectCols + @"
    WHERE AssemblyNumber=@asm AND ToolItem=@ti AND ToolNumber=@tn
    LIMIT 1;";
            await using var conn = new MySqlConnection(_conn);
            await conn.OpenAsync();
            await using var cmd = new MySqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@asm", assembly);
            cmd.Parameters.AddWithValue("@ti", toolItem);
            cmd.Parameters.AddWithValue("@tn", toolNumber);
            await using var r = await cmd.ExecuteReaderAsync(CommandBehavior.CloseConnection);
            return await r.ReadAsync() ? Map(r) : null;
        }

        // quick create with sane defaults
        public async Task<int> CreateBasicAsync(string assembly, string toolItem, string toolNumber)
        {
            var m = new ToolItemModel
            {
                AssemblyNumber = assembly.Trim(),
                ToolItem = toolItem.Trim(),
                ToolNumber = toolNumber.Trim(),
                Condition = ToolCondition.ReadyForProduction,
                Status = ToolStatus.Available,
                Location = null,
                UnavailableReason = null,
                DateUnavailable = null,
                EstimatedAvailableDate = null
            };
            return await CreateAsync(m);
        }

        public async Task MarkUnavailableAsync(
            string assembly, string toolItem, string toolNumber,
            string reason, DateTime dateUnavailable, DateTime? eta)
        {
            // ensure a row exists; if not, create it
            var existing = await FindByKeyAsync(assembly, toolItem, toolNumber);
            if (existing == null)
            {
                var id = await CreateBasicAsync(assembly, toolItem, toolNumber);
                existing = await GetByIdAsync(id);
            }

            existing!.Status = ToolStatus.Unavailable;
            existing.UnavailableReason = reason;
            existing.DateUnavailable = dateUnavailable;
            existing.EstimatedAvailableDate = eta;

            await UpdateAsync(existing);
        }
        // in ToolingInventoryService
        public Task MarkAvailableAsync(string asm, string toolItem, string toolNumber)
        {
            const string sql = @"
        UPDATE tooling_inventory
        SET Status = @status,
            UnavailableReason = NULL,
            DateUnavailable = NULL,
            EstimatedAvailableDate = NULL
        WHERE AssemblyNumber = @asm
          AND ToolItem = @toolItem
          AND ToolNumber = @toolNumber;";

            // assuming ToolStatus.Available = 1 – adjust if needed
            using var conn = new MySqlConnection(_conn);
            conn.Open();
            using var cmd = new MySqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@status", (int)ToolStatus.Available);
            cmd.Parameters.AddWithValue("@asm", asm);
            cmd.Parameters.AddWithValue("@toolItem", toolItem);
            cmd.Parameters.AddWithValue("@toolNumber", toolNumber);
            cmd.ExecuteNonQuery();

            return Task.CompletedTask;
        }
    }
}
