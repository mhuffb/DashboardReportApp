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

        private const string SelectCols = @"
SELECT Id, AssemblyNumber, ToolNumber, Location, Category,
       `Condition`, `Status`, UnavailableReason, DateUnavailable, EstimatedAvailableDate
FROM tooling_inventory";

        public async Task<List<ToolItemModel>> GetAllAsync()
        {
            var list = new List<ToolItemModel>();
            const string sql = SelectCols + " ORDER BY Id DESC;";

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

        public async Task<int> CreateAsync(ToolItemModel m)
        {
            const string sql = @"
INSERT INTO tooling_inventory
(AssemblyNumber, ToolNumber, Location, Category, `Condition`, `Status`,
 UnavailableReason, DateUnavailable, EstimatedAvailableDate)
VALUES
(@AssemblyNumber, @ToolNumber, @Location, @Category, @Condition, @Status,
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
 Category=@Category,
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
            cmd.Parameters.AddWithValue("@Category", (int)m.Category);
            cmd.Parameters.AddWithValue("@Condition", (int)m.Condition);
            cmd.Parameters.AddWithValue("@Status", (int)m.Status);
            cmd.Parameters.AddWithValue("@UnavailableReason", (object?)m.UnavailableReason ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@DateUnavailable", (object?)m.DateUnavailable ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@EstimatedAvailableDate", (object?)m.EstimatedAvailableDate ?? DBNull.Value);
        }

        private static ToolItemModel Map(MySqlDataReader r) => new()
        {
            Id = r.GetInt32("Id"),
            AssemblyNumber = r.GetString("AssemblyNumber"),
            ToolNumber = r.GetString("ToolNumber"),
            Location = r["Location"] as string,
            Category = (ToolCategory)r.GetInt32("Category"),
            Condition = (ToolCondition)r.GetInt32("Condition"),
            Status = (ToolStatus)r.GetInt32("Status"),
            UnavailableReason = r["UnavailableReason"] as string,
            DateUnavailable = r["DateUnavailable"] == DBNull.Value ? null : r.GetDateTime("DateUnavailable"),
            EstimatedAvailableDate = r["EstimatedAvailableDate"] == DBNull.Value ? null : r.GetDateTime("EstimatedAvailableDate")
        };
    }
}
