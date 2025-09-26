using DashboardReportApp.Models;
using Microsoft.Extensions.Configuration;
using MySql.Data.MySqlClient;
using System;
using System.Collections.Generic;
using System.Data;
using System.Drawing;

namespace DashboardReportApp.Services
{
    public class ToolingHistoryService
    {
        private readonly string _connStr;

        public ToolingHistoryService(IConfiguration config)
        {
            _connStr = config.GetConnectionString("MySQLConnection")
                       ?? config["ConnectionStrings:MySQLConnection"];

            if (string.IsNullOrWhiteSpace(_connStr))
                throw new ArgumentException("ConnectionStrings:DefaultConnection is missing or empty.");

            var builder = new MySqlConnectionStringBuilder(_connStr);
            if (string.IsNullOrWhiteSpace(builder.Server))
                throw new ArgumentException("ConnectionStrings:DefaultConnection is missing Server/Host.");
        }

        private MySqlConnection OpenConnection()
        {
            var cn = new MySqlConnection(_connStr);
            cn.Open();
            return cn;
        }

        // ---------------- Tooling History ----------------

        public List<ToolingHistoryModel> GetAll()
        {
            var list = new List<ToolingHistoryModel>();
            const string sql = @"
                SELECT 
                  Id, GroupID, Part, ToolNumber, Revision, PO,
                  Reason, AccountingCode, ToolVendor, InitiatedBy,
                  DateInitiated, DateDue,
                  Cost, ToolWorkHours, ToolDesc
                FROM ToolingHistory
                ORDER BY Id DESC;";

            using var cn = OpenConnection();
            using var cmd = new MySqlCommand(sql, cn);
            using var rd = cmd.ExecuteReader();
            while (rd.Read())
            {
                list.Add(MapHistory(rd));
            }
            return list;
        }

        public ToolingHistoryModel GetById(int id)
        {
            const string sql = @"
                SELECT Id, GroupID, Part, ToolNumber, Revision, PO,
                       Reason, AccountingCode, ToolVendor, InitiatedBy,
                       DateInitiated, DateDue,
                       Cost, ToolWorkHours, ToolDesc
                FROM ToolingHistory
                WHERE Id = @Id;";

            using var cn = OpenConnection();
            using var cmd = new MySqlCommand(sql, cn);
            cmd.Parameters.AddWithValue("@Id", id);
            using var rd = cmd.ExecuteReader();
            return rd.Read() ? MapHistory(rd) : null;
        }

        public void Create(ToolingHistoryModel m)
        {
            const string sql = @"
        INSERT INTO ToolingHistory
        (GroupID, Part, ToolNumber, Revision, PO,
         Reason, AccountingCode, ToolVendor, InitiatedBy,
         DateInitiated, DateDue, DateReceived,
         Cost, ToolWorkHours, ToolDesc)
        VALUES
        (@GroupID, @Part, @ToolNumber, @Revision, @PO,
         @Reason, @AccountingCode, @ToolVendor, @InitiatedBy,
         @DateInitiated, @DateDue, @DateReceived,
         @Cost, @ToolWorkHours, @ToolDesc);
        SELECT LAST_INSERT_ID();";

            using var cn = OpenConnection();
            using var cmd = new MySqlCommand(sql, cn);
            AddHistoryParams(cmd, m);

            var newIdObj = cmd.ExecuteScalar();
            m.Id = Convert.ToInt32(newIdObj);

            // If no GroupID was provided, set it = Id so downstream tools have a valid key.
            if (m.GroupID <= 0)
            {
                using var upd = new MySqlCommand(
                    "UPDATE ToolingHistory SET GroupID = @Id WHERE Id = @Id;", cn);
                upd.Parameters.AddWithValue("@Id", m.Id);
                upd.ExecuteNonQuery();
                m.GroupID = m.Id;
            }
        }



        public void Update(ToolingHistoryModel m)
        {
            const string sql = @"
                UPDATE ToolingHistory SET
                  GroupID=@GroupID, Part=@Part, ToolNumber=@ToolNumber, Revision=@Revision, PO=@PO,
                  Reason=@Reason, AccountingCode=@AccountingCode, ToolVendor=@ToolVendor,
                  InitiatedBy=@InitiatedBy, DateInitiated=@DateInitiated, DateDue=@DateDue,
                  Cost=@Cost, ToolWorkHours=@ToolWorkHours, ToolDesc=@ToolDesc
                WHERE Id=@Id;";

            using var cn = OpenConnection();
            using var cmd = new MySqlCommand(sql, cn);

            cmd.Parameters.AddWithValue("@GroupID", m.GroupID > 0 ? m.GroupID : (object)DBNull.Value);
            AddHistoryParams(cmd, m);
            cmd.Parameters.AddWithValue("@Id", m.Id);
            cmd.ExecuteNonQuery();

            // Keep invariant: if missing GroupID, backfill to Id
            if (m.GroupID <= 0)
            {
                using var cmdUpd = new MySqlCommand(@"UPDATE ToolingHistory SET GroupID=@Id WHERE Id=@Id;", cn);
                cmdUpd.Parameters.AddWithValue("@Id", m.Id);
                cmdUpd.ExecuteNonQuery();
                m.GroupID = m.Id;
            }
        }

        private static void AddHistoryParams(MySqlCommand cmd, ToolingHistoryModel m)
        {
            cmd.Parameters.AddWithValue("@Part", (object?)m.Part ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@ToolNumber", (object?)m.ToolNumber ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@Revision", (object?)m.Revision ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@PO", (object?)m.PO ?? DBNull.Value);

            cmd.Parameters.AddWithValue("@Reason", (object?)m.Reason ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@ToolVendor", (object?)m.ToolVendor ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@InitiatedBy", (object?)m.InitiatedBy ?? DBNull.Value);

            var pInit = cmd.Parameters.Add("@DateInitiated", MySqlDbType.Date);
            pInit.Value = (object?)m.DateInitiated ?? DBNull.Value;

            var pDue = cmd.Parameters.Add("@DateDue", MySqlDbType.Date);
            pDue.Value = (object?)m.DateDue ?? DBNull.Value;

            cmd.Parameters.AddWithValue("@Cost", (object?)m.Cost ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@ToolWorkHours", (object?)m.ToolWorkHours ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@ToolDesc", (object?)m.ToolDesc ?? DBNull.Value);
        }

        private static ToolingHistoryModel MapHistory(MySqlDataReader rd)
        {
            return new ToolingHistoryModel
            {
                Id = GetInt(rd, "Id"),
                GroupID = GetInt(rd, "GroupID"),
                Part = GetString(rd, "Part"),
                ToolNumber = GetString(rd, "ToolNumber"),
                Revision = GetString(rd, "Revision"),
                PO = GetString(rd, "PO"),
                Reason = GetString(rd, "Reason"),
                ToolVendor = GetString(rd, "ToolVendor"),
                InitiatedBy = GetString(rd, "InitiatedBy"),
                DateInitiated = GetNullableDate(rd, "DateInitiated"),
                DateDue = GetNullableDate(rd, "DateDue"),
                Cost = GetNullableDecimal(rd, "Cost"),
                ToolWorkHours = GetNullableDecimal(rd, "ToolWorkHours"),
                ToolDesc = GetString(rd, "ToolDesc")
            };
        }

        // ---------------- Tool Items ----------------

        public GroupDetailsViewModel GetGroupDetails(int groupId)
        {
            var vm = new GroupDetailsViewModel { GroupID = groupId };

            const string sql = @"
                SELECT Id, GroupID, Action, ToolItem, ToolNumber, ToolDesc, Revision,
                       Quantity, Cost, ToolWorkHours, DateDue, DateFitted, DateReceived,
                       ReceivedBy, FittedBy
                FROM ToolItems
                WHERE GroupID = @GroupID
                ORDER BY Id DESC;";

            using var cn = OpenConnection();
            using var cmd = new MySqlCommand(sql, cn);
            cmd.Parameters.AddWithValue("@GroupID", groupId);

            using var rd = cmd.ExecuteReader();
            while (rd.Read())
            {
                vm.ToolItems.Add(new GroupToolItem
                {
                    Id = GetInt(rd, "Id"),
                    GroupID = GetInt(rd, "GroupID"),
                    Action = GetString(rd, "Action"),
                    ToolItem = GetString(rd, "ToolItem"),
                    ToolNumber = GetString(rd, "ToolNumber"),
                    ToolDesc = GetString(rd, "ToolDesc"),
                    Revision = GetString(rd, "Revision"),
                    Quantity = GetNullableInt(rd, "Quantity"),
                    Cost = GetNullableDecimal(rd, "Cost"),
                    ToolWorkHours = GetNullableDecimal(rd, "ToolWorkHours"),
                    DateDue = GetNullableDate(rd, "DateDue"),
                    DateFitted = GetNullableDate(rd, "DateFitted"),
                    DateReceived = GetNullableDate(rd, "DateReceived"),
                    ReceivedBy = GetString(rd, "ReceivedBy"),
                    FittedBy = GetString(rd, "FittedBy")
                });
            }
            return vm;
        }

        public void AddToolItem(GroupToolItem m)
        {
            const string sql = @"
                INSERT INTO ToolItems
                (GroupID, Action, ToolItem, ToolNumber, ToolDesc, Revision,
                 Quantity, Cost, ToolWorkHours, DateDue, DateFitted, DateReceived, ReceivedBy, FittedBy)
                VALUES
                (@GroupID, @Action, @ToolItem, @ToolNumber, @ToolDesc, @Revision,
                 @Quantity, @Cost, @ToolWorkHours, @DateDue, @DateFitted, @DateReceived, @ReceivedBy, @FittedBy);";

            using var cn = OpenConnection();
            using var cmd = new MySqlCommand(sql, cn);

            cmd.Parameters.AddWithValue("@GroupID", m.GroupID);
            cmd.Parameters.AddWithValue("@Action", (object?)m.Action ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@ToolItem", (object?)m.ToolItem ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@ToolNumber", (object?)m.ToolNumber ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@ToolDesc", (object?)m.ToolDesc ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@Revision", (object?)m.Revision ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@Quantity", (object?)m.Quantity ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@Cost", (object?)m.Cost ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@ToolWorkHours", (object?)m.ToolWorkHours ?? DBNull.Value);

            var pDue = cmd.Parameters.Add("@DateDue", MySqlDbType.Date);
            pDue.Value = (object?)m.DateDue ?? DBNull.Value;

            var pFit = cmd.Parameters.Add("@DateFitted", MySqlDbType.Date);
            pFit.Value = (object?)m.DateFitted ?? DBNull.Value;

            var pRecv = cmd.Parameters.Add("@DateReceived", MySqlDbType.Date);
            pRecv.Value = (object?)m.DateReceived ?? DBNull.Value;

            cmd.Parameters.AddWithValue("@ReceivedBy", (object?)m.ReceivedBy ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@FittedBy", (object?)m.FittedBy ?? DBNull.Value);

            cmd.ExecuteNonQuery();
        }

        public void SaveAllToolItems(GroupDetailsViewModel vm)
        {
            if (vm?.ToolItems == null || vm.ToolItems.Count == 0) return;

            const string sql = @"
                UPDATE ToolItems SET
                  Action=@Action, ToolItem=@ToolItem, ToolNumber=@ToolNumber,
                  ToolDesc=@ToolDesc, Revision=@Revision,
                  Quantity=@Quantity, Cost=@Cost, ToolWorkHours=@ToolWorkHours,
                  DateDue=@DateDue, DateFitted=@DateFitted, DateReceived=@DateReceived,
                  ReceivedBy=@ReceivedBy, FittedBy=@FittedBy
                WHERE Id=@Id AND GroupID=@GroupID;";

            using var cn = OpenConnection();

            foreach (var t in vm.ToolItems)
            {
                using var cmd = new MySqlCommand(sql, cn);
                cmd.Parameters.AddWithValue("@Id", t.Id);
                cmd.Parameters.AddWithValue("@GroupID", vm.GroupID);
                cmd.Parameters.AddWithValue("@Action", (object?)t.Action ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@ToolItem", (object?)t.ToolItem ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@ToolNumber", (object?)t.ToolNumber ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@ToolDesc", (object?)t.ToolDesc ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@Revision", (object?)t.Revision ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@Quantity", (object?)t.Quantity ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@Cost", (object?)t.Cost ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@ToolWorkHours", (object?)t.ToolWorkHours ?? DBNull.Value);

                var pDue = cmd.Parameters.Add("@DateDue", MySqlDbType.Date);
                pDue.Value = (object?)t.DateDue ?? DBNull.Value;

                var pFit = cmd.Parameters.Add("@DateFitted", MySqlDbType.Date);
                pFit.Value = (object?)t.DateFitted ?? DBNull.Value;

                var pRecv = cmd.Parameters.Add("@DateReceived", MySqlDbType.Date);
                pRecv.Value = (object?)t.DateReceived ?? DBNull.Value;

                cmd.Parameters.AddWithValue("@ReceivedBy", (object?)t.ReceivedBy ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@FittedBy", (object?)t.FittedBy ?? DBNull.Value);

                cmd.ExecuteNonQuery();
            }
        }

        // ---------------- Helpers ----------------

        private static int GetInt(MySqlDataReader rd, string name)
        {
            var ordinal = rd.GetOrdinal(name);
            return rd.IsDBNull(ordinal) ? 0 : Convert.ToInt32(rd.GetValue(ordinal));
        }

        private static int? GetNullableInt(MySqlDataReader rd, string name)
        {
            var ordinal = rd.GetOrdinal(name);
            return rd.IsDBNull(ordinal) ? (int?)null : Convert.ToInt32(rd.GetValue(ordinal));
        }

        private static decimal? GetNullableDecimal(MySqlDataReader rd, string name)
        {
            var ordinal = rd.GetOrdinal(name);
            return rd.IsDBNull(ordinal) ? (decimal?)null : Convert.ToDecimal(rd.GetValue(ordinal));
        }

        private static DateTime? GetNullableDate(MySqlDataReader rd, string name)
        {
            var ordinal = rd.GetOrdinal(name);
            return rd.IsDBNull(ordinal) ? (DateTime?)null : Convert.ToDateTime(rd.GetValue(ordinal));
        }

        private static string GetString(MySqlDataReader rd, string name)
        {
            var ordinal = rd.GetOrdinal(name);
            if (rd.IsDBNull(ordinal)) return null;
            var val = rd.GetValue(ordinal);
            return val?.ToString();
        }
       
        public int SaveToolingHistory(ToolingHistoryModel m)
        {
            using var conn = new MySqlConnection(_connStr);
            conn.Open();

            using var tx = conn.BeginTransaction();

            try
            {
                if (m.Id == 0)
                {
                    // INSERT
                    using (var cmd = new MySqlCommand(@"
INSERT INTO toolinghistory
(Part, ToolNumber, Revision, PO, Reason, ToolVendor, InitiatedBy, DateInitiated, DateDue, Cost, ToolWorkHours, ToolDesc, GroupID)
VALUES (@Part, @ToolNumber, @Revision, @PO, @Reason, @ToolVendor, @InitiatedBy, @DateInitiated, @DateDue, @Cost, @ToolWorkHours, @ToolDesc, 0);
SELECT LAST_INSERT_ID();", conn, tx))
                    {
                        cmd.Parameters.AddWithValue("@Part", (object?)m.Part ?? DBNull.Value);
                        cmd.Parameters.AddWithValue("@ToolNumber", (object?)m.ToolNumber ?? DBNull.Value);
                        cmd.Parameters.AddWithValue("@Revision", (object?)m.Revision ?? DBNull.Value);
                        cmd.Parameters.AddWithValue("@PO", (object?)m.PO ?? DBNull.Value);
                        cmd.Parameters.AddWithValue("@Reason", (object?)m.Reason ?? DBNull.Value);
                        cmd.Parameters.AddWithValue("@ToolVendor", (object?)m.ToolVendor ?? DBNull.Value);
                        cmd.Parameters.AddWithValue("@InitiatedBy", (object?)m.InitiatedBy ?? DBNull.Value);
                        cmd.Parameters.AddWithValue("@DateInitiated", (object?)m.DateInitiated ?? DBNull.Value);
                        cmd.Parameters.AddWithValue("@DateDue", (object?)m.DateDue ?? DBNull.Value);
                        cmd.Parameters.AddWithValue("@Cost", (object?)m.Cost ?? DBNull.Value);
                        cmd.Parameters.AddWithValue("@ToolWorkHours", (object?)m.ToolWorkHours ?? DBNull.Value);
                        cmd.Parameters.AddWithValue("@ToolDesc", (object?)m.ToolDesc ?? DBNull.Value);

                        var newId = Convert.ToInt32(cmd.ExecuteScalar());
                        m.Id = newId;

                        // Ensure GroupID equals Id
                        using var cmd2 = new MySqlCommand(@"UPDATE toolinghistory SET GroupID = @gid WHERE Id = @id;", conn, tx);
                        cmd2.Parameters.AddWithValue("@gid", newId);
                        cmd2.Parameters.AddWithValue("@id", newId);
                        cmd2.ExecuteNonQuery();

                        tx.Commit();
                        return newId;
                    }
                }
                else
                {
                    // UPDATE
                    using (var cmd = new MySqlCommand(@"
UPDATE toolinghistory
SET Part=@Part, ToolNumber=@ToolNumber, Revision=@Revision, PO=@PO, Reason=@Reason, ToolVendor=@ToolVendor, 
    InitiatedBy=@InitiatedBy, DateInitiated=@DateInitiated, DateDue=@DateDue, Cost=@Cost, ToolWorkHours=@ToolWorkHours, ToolDesc=@ToolDesc
WHERE Id=@Id;", conn, tx))
                    {
                        cmd.Parameters.AddWithValue("@Part", (object?)m.Part ?? DBNull.Value);
                        cmd.Parameters.AddWithValue("@ToolNumber", (object?)m.ToolNumber ?? DBNull.Value);
                        cmd.Parameters.AddWithValue("@Revision", (object?)m.Revision ?? DBNull.Value);
                        cmd.Parameters.AddWithValue("@PO", (object?)m.PO ?? DBNull.Value);
                        cmd.Parameters.AddWithValue("@Reason", (object?)m.Reason ?? DBNull.Value);
                        cmd.Parameters.AddWithValue("@ToolVendor", (object?)m.ToolVendor ?? DBNull.Value);
                        cmd.Parameters.AddWithValue("@InitiatedBy", (object?)m.InitiatedBy ?? DBNull.Value);
                        cmd.Parameters.AddWithValue("@DateInitiated", (object?)m.DateInitiated ?? DBNull.Value);
                        cmd.Parameters.AddWithValue("@DateDue", (object?)m.DateDue ?? DBNull.Value);
                        cmd.Parameters.AddWithValue("@Cost", (object?)m.Cost ?? DBNull.Value);
                        cmd.Parameters.AddWithValue("@ToolWorkHours", (object?)m.ToolWorkHours ?? DBNull.Value);
                        cmd.Parameters.AddWithValue("@ToolDesc", (object?)m.ToolDesc ?? DBNull.Value);
                        cmd.Parameters.AddWithValue("@Id", m.Id);
                        cmd.ExecuteNonQuery();
                    }

                    // If GroupID ended up 0 for some reason, fix it
                    if (m.GroupID == 0)
                    {
                        using var cmd2 = new MySqlCommand(@"UPDATE toolinghistory SET GroupID = Id WHERE Id = @id;", conn, tx);
                        cmd2.Parameters.AddWithValue("@id", m.Id);
                        cmd2.ExecuteNonQuery();
                        m.GroupID = m.Id;
                    }

                    tx.Commit();
                    return m.GroupID == 0 ? m.Id : m.GroupID;
                }
            }
            catch
            {
                tx.Rollback();
                throw;
            }
        }
    }
}
