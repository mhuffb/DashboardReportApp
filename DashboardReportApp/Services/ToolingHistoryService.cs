using DashboardReportApp.Models;
using MySql.Data.MySqlClient;

namespace DashboardReportApp.Services
{
    public class ToolingHistoryService
    {
        private readonly string _connectionString;

        public ToolingHistoryService(IConfiguration configuration)
        {
            _connectionString = configuration.GetConnectionString("MySQLConnection");
        }

        public List<ToolingHistoryModel> GetToolingHistories()
        {
            var toolingHistories = new List<ToolingHistoryModel>();
            string query = @"
        SELECT part, id, reason, toolvendor, dateinitiated, toolnumber, cost, revision, po, 
               toolworkhours, tooldesc, groupid, datedue, accountingcode,
               initiatedby, datereceived   -- NEW
        FROM toolinghistory 
        WHERE part IS NOT NULL";

            using (var connection = new MySqlConnection(_connectionString))
            {
                connection.Open();
                using (var command = new MySqlCommand(query, connection))
                using (var reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        var toolingHistory = new ToolingHistoryModel
                        {
                            Id = Convert.ToInt32(reader["Id"]),
                            Reason = reader["Reason"]?.ToString(),
                            ToolVendor = reader["ToolVendor"]?.ToString(),
                            DateInitiated = reader["DateInitiated"] != DBNull.Value ? Convert.ToDateTime(reader["DateInitiated"]) : DateTime.Today,
                            Part = reader["Part"]?.ToString(),
                            ToolNumber = reader["ToolNumber"]?.ToString(),
                            Cost = reader["Cost"] != DBNull.Value ? (decimal?)Convert.ToDecimal(reader["Cost"]) : null,
                            Revision = reader["Revision"] != DBNull.Value ? reader["Revision"]?.ToString() : null,
                            PO = reader["PO"] != DBNull.Value ? reader["PO"]?.ToString() : null,
                            ToolWorkHours = reader["ToolWorkHours"] != DBNull.Value ? (int?)Convert.ToInt32(reader["ToolWorkHours"]) : null,
                            ToolDesc = reader["ToolDesc"] != DBNull.Value ? reader["ToolDesc"]?.ToString() : null,
                            GroupID = Convert.ToInt32(reader["GroupID"]),
                            DateDue = reader["DateDue"] != DBNull.Value ? (DateTime?)Convert.ToDateTime(reader["DateDue"]) : DateTime.Today,
                            AccountingCode = reader["accountingCode"] != DBNull.Value ? (int?)Convert.ToInt32(reader["accountingCode"]) : null,

                            // NEW:
                            InitiatedBy = reader["initiatedby"] != DBNull.Value ? reader["initiatedby"]?.ToString() : null,
                            DateReceived = reader["datereceived"] != DBNull.Value ? (DateTime?)Convert.ToDateTime(reader["datereceived"]) : null
                        };

                        toolingHistories.Add(toolingHistory);
                    }
                }
            }

            return toolingHistories;
        }

        public int GetNextGroupID()
        {
            string query = "SELECT IFNULL(MAX(GroupID), 0) + 1 AS NextGroupID FROM toolinghistory";

            using (var connection = new MySqlConnection(_connectionString))
            {
                connection.Open();
                using (var command = new MySqlCommand(query, connection))
                {
                    var result = command.ExecuteScalar();
                    return Convert.ToInt32(result);
                }
            }
        }

        public ToolingHistoryModel? GetToolingHistoryById(int id)
        {
            const string q = @"
                SELECT part, id, reason, toolvendor, dateinitiated, toolnumber, cost, revision, po, 
                       toolworkhours, tooldesc, groupid, datedue, accountingcode
                FROM toolinghistory
                WHERE id = @Id
                LIMIT 1;";

            using var conn = new MySqlConnection(_connectionString);
            conn.Open();
            using var cmd = new MySqlCommand(q, conn);
            cmd.Parameters.AddWithValue("@Id", id);
            using var r = cmd.ExecuteReader();
            if (!r.Read()) return null;

            return new ToolingHistoryModel
            {
                Id = Convert.ToInt32(r["Id"]),
                Reason = r["Reason"]?.ToString(),
                ToolVendor = r["ToolVendor"]?.ToString(),
                DateInitiated = r["DateInitiated"] != DBNull.Value ? Convert.ToDateTime(r["DateInitiated"]) : DateTime.Today,
                Part = r["Part"]?.ToString(),
                ToolNumber = r["ToolNumber"]?.ToString(),
                Cost = r["Cost"] != DBNull.Value ? (decimal?)Convert.ToDecimal(r["Cost"]) : null,
                Revision = r["Revision"] != DBNull.Value ? r["Revision"]?.ToString() : null,
                PO = r["PO"] != DBNull.Value ? r["PO"]?.ToString() : null,
                ToolWorkHours = r["ToolWorkHours"] != DBNull.Value ? (int?)Convert.ToInt32(r["ToolWorkHours"]) : null,
                ToolDesc = r["ToolDesc"] != DBNull.Value ? r["ToolDesc"]?.ToString() : null,
                GroupID = Convert.ToInt32(r["GroupID"]),
                DateDue = r["DateDue"] != DBNull.Value ? (DateTime?)Convert.ToDateTime(r["DateDue"]) : null,
                AccountingCode = r["accountingCode"] != DBNull.Value ? (int?)Convert.ToInt32(r["accountingCode"]) : null
            };
        }

        public void AddToolingHistory(ToolingHistoryModel model)
        {
            int? accountingCode = null;
            if (model.Reason == "New") accountingCode = 5030;
            else if (model.Reason == "Repair") accountingCode = 5045;
            else if (model.Reason == "Breakage") accountingCode = 5040;

            string query = @"
        INSERT INTO toolinghistory 
        (GroupID, Reason, ToolVendor, DateInitiated, DateDue, Part, ToolNumber, Cost, Revision, PO, 
         ToolWorkHours, ToolDesc, AccountingCode, InitiatedBy, DateReceived)
        VALUES 
        (@GroupID, @Reason, @ToolVendor, @DateInitiated, @DateDue, @Part, @ToolNumber, @Cost, @Revision, @PO, 
         @ToolWorkHours, @ToolDesc, @AccountingCode, @InitiatedBy, @DateReceived)";

            using (var connection = new MySqlConnection(_connectionString))
            {
                connection.Open();
                using (var command = new MySqlCommand(query, connection))
                {
                    command.Parameters.AddWithValue("@GroupID", model.GroupID);
                    command.Parameters.AddWithValue("@Reason", model.Reason);
                    command.Parameters.AddWithValue("@ToolVendor", model.ToolVendor);
                    command.Parameters.AddWithValue("@DateInitiated", model.DateInitiated);
                    command.Parameters.AddWithValue("@DateDue", model.DateDue ?? (object)DBNull.Value);
                    command.Parameters.AddWithValue("@Part", model.Part);
                    command.Parameters.AddWithValue("@ToolNumber", model.ToolNumber);
                    command.Parameters.AddWithValue("@Cost", model.Cost ?? (object)DBNull.Value);
                    command.Parameters.AddWithValue("@Revision", model.Revision ?? (object)DBNull.Value);
                    command.Parameters.AddWithValue("@PO", model.PO ?? (object)DBNull.Value);
                    command.Parameters.AddWithValue("@ToolWorkHours", model.ToolWorkHours ?? (object)DBNull.Value);
                    command.Parameters.AddWithValue("@ToolDesc", model.ToolDesc ?? (object)DBNull.Value);
                    command.Parameters.AddWithValue("@AccountingCode", accountingCode.HasValue ? (object)accountingCode.Value : DBNull.Value);

                    // NEW:
                    command.Parameters.AddWithValue("@InitiatedBy", string.IsNullOrWhiteSpace(model.InitiatedBy) ? "Emery, J" : model.InitiatedBy);
                    command.Parameters.AddWithValue("@DateReceived", model.DateReceived ?? (object)DBNull.Value);

                    command.ExecuteNonQuery();
                }
            }
        }


        public void UpdateToolingHistory(ToolingHistoryModel model)
        {
            string query = @"
        UPDATE toolinghistory 
        SET 
            Reason = @Reason,
            ToolVendor = @ToolVendor,
            DateInitiated = @DateInitiated,
            Part = @Part,
            ToolNumber = @ToolNumber,
            Cost = @Cost,
            Revision = @Revision,
            PO = @PO,
            ToolWorkHours = @ToolWorkHours,
            ToolDesc = @ToolDesc,
            DateDue = @DateDue,
            InitiatedBy = @InitiatedBy,      -- NEW
            DateReceived = @DateReceived     -- NEW
        WHERE Id = @Id";

            using (var connection = new MySqlConnection(_connectionString))
            {
                connection.Open();
                using (var command = new MySqlCommand(query, connection))
                {
                    command.Parameters.AddWithValue("@Id", model.Id);
                    command.Parameters.AddWithValue("@Reason", model.Reason ?? (object)DBNull.Value);
                    command.Parameters.AddWithValue("@ToolVendor", model.ToolVendor ?? (object)DBNull.Value);
                    command.Parameters.AddWithValue("@DateInitiated", model.DateInitiated);
                    command.Parameters.AddWithValue("@Part", model.Part ?? (object)DBNull.Value);
                    command.Parameters.AddWithValue("@ToolNumber", model.ToolNumber ?? (object)DBNull.Value);
                    command.Parameters.AddWithValue("@Cost", model.Cost ?? (object)DBNull.Value);
                    command.Parameters.AddWithValue("@Revision", model.Revision ?? (object)DBNull.Value);
                    command.Parameters.AddWithValue("@PO", model.PO ?? (object)DBNull.Value);
                    command.Parameters.AddWithValue("@ToolWorkHours", model.ToolWorkHours ?? (object)DBNull.Value);
                    command.Parameters.AddWithValue("@ToolDesc", model.ToolDesc ?? (object)DBNull.Value);
                    command.Parameters.AddWithValue("@DateDue", model.DateDue ?? (object)DBNull.Value);

                    // NEW:
                    command.Parameters.AddWithValue("@InitiatedBy", string.IsNullOrWhiteSpace(model.InitiatedBy) ? (object)DBNull.Value : model.InitiatedBy);
                    command.Parameters.AddWithValue("@DateReceived", model.DateReceived ?? (object)DBNull.Value);

                    command.ExecuteNonQuery();
                }
            }
        }

        public List<ToolItemViewModel> GetToolItemsByGroupID(int groupID)
        {
            var toolItems = new List<ToolItemViewModel>();

            // "Real" tool items are those that have *any* of the item-level signals present.
            // This reliably excludes the tooling history header row, which typically has Action/ToolItem/etc. NULL.
            string query = @"
SELECT *
FROM toolinghistory
WHERE GroupID = @GroupID
  AND (
        Action IS NOT NULL
     OR ToolItem IS NOT NULL
     OR Quantity IS NOT NULL
     OR ReceivedBy IS NOT NULL
     OR FittedBy IS NOT NULL
     OR DateFitted IS NOT NULL
     )
ORDER BY Id ASC;";

            using (var connection = new MySqlConnection(_connectionString))
            {
                connection.Open();
                using (var command = new MySqlCommand(query, connection))
                {
                    command.Parameters.AddWithValue("@GroupID", groupID);

                    using (var reader = command.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            var toolItem = new ToolItemViewModel
                            {
                                Id = Convert.ToInt32(reader["Id"]),
                                GroupID = groupID,
                                ToolItem = reader["ToolItem"]?.ToString(),
                                ToolNumber = reader["ToolNumber"]?.ToString(),
                                ToolDesc = reader["ToolDesc"]?.ToString(),
                                Cost = reader["Cost"] != DBNull.Value ? (decimal?)Convert.ToDecimal(reader["Cost"]) : null,
                                Revision = reader["Revision"]?.ToString(),
                                Quantity = reader["Quantity"] != DBNull.Value ? (int?)Convert.ToInt32(reader["Quantity"]) : null,
                                ToolWorkHours = reader["ToolWorkHours"] != DBNull.Value ? (int?)Convert.ToInt32(reader["ToolWorkHours"]) : null,
                                DateDue = reader["DateDue"] != DBNull.Value ? (DateTime?)Convert.ToDateTime(reader["DateDue"]) : null,
                                DateFitted = reader["DateFitted"] != DBNull.Value ? (DateTime?)Convert.ToDateTime(reader["DateFitted"]) : null,
                                ReceivedBy = reader["ReceivedBy"]?.ToString(),
                                FittedBy = reader["FittedBy"]?.ToString(),
                                Action = reader["Action"]?.ToString()
                            };

                            toolItems.Add(toolItem);
                        }
                    }
                }
            }

            return toolItems;
        }


        public void AddToolItem(ToolItemViewModel toolItem)
        {
            string query = @"
INSERT INTO toolinghistory
(
    GroupID, ToolNumber, ToolDesc, Cost, Revision, Quantity, ToolWorkHours,
    DateDue, DateFitted, ReceivedBy, FittedBy, Action, ToolItem,
    Part, PO, Reason, AccountingCode, ToolVendor, DateInitiated, InitiatedBy  -- NEW
)
SELECT
    @GroupID, @ToolNumber, @ToolDesc, @Cost, @Revision, @Quantity, @ToolWorkHours,
    @DateDue, @DateFitted, @ReceivedBy, @FittedBy, @Action, @ToolItem,
    h.Part, h.PO, h.Reason, h.AccountingCode, h.ToolVendor, h.DateInitiated, h.InitiatedBy   -- NEW
FROM toolinghistory h
WHERE h.GroupID = @GroupID
  AND h.Action IS NULL AND h.ToolItem IS NULL
ORDER BY h.Id ASC
LIMIT 1;
";

            using (var conn = new MySqlConnection(_connectionString))
            {
                conn.Open();
                using (var cmd = new MySqlCommand(query, conn))
                {
                    cmd.Parameters.AddWithValue("@GroupID", toolItem.GroupID);
                    cmd.Parameters.AddWithValue("@ToolNumber", toolItem.ToolNumber ?? (object)DBNull.Value);
                    cmd.Parameters.AddWithValue("@ToolDesc", toolItem.ToolDesc ?? (object)DBNull.Value);
                    cmd.Parameters.AddWithValue("@Cost", toolItem.Cost ?? (object)DBNull.Value);
                    cmd.Parameters.AddWithValue("@Revision", toolItem.Revision ?? (object)DBNull.Value);
                    cmd.Parameters.AddWithValue("@Quantity", toolItem.Quantity ?? (object)DBNull.Value);
                    cmd.Parameters.AddWithValue("@ToolWorkHours", toolItem.ToolWorkHours ?? (object)DBNull.Value);
                    cmd.Parameters.AddWithValue("@DateDue", toolItem.DateDue ?? (object)DBNull.Value);
                    cmd.Parameters.AddWithValue("@DateFitted", toolItem.DateFitted ?? (object)DBNull.Value);
                    cmd.Parameters.AddWithValue("@ReceivedBy", toolItem.ReceivedBy ?? (object)DBNull.Value);
                    cmd.Parameters.AddWithValue("@FittedBy", toolItem.FittedBy ?? (object)DBNull.Value);
                    cmd.Parameters.AddWithValue("@Action", toolItem.Action ?? (object)DBNull.Value);
                    cmd.Parameters.AddWithValue("@ToolItem", toolItem.ToolItem ?? (object)DBNull.Value);

                    cmd.ExecuteNonQuery();
                }
            }
        }

        public ToolingHistoryModel GetHeaderByGroupID(int groupID)
        {
            const string sql = @"
SELECT *
FROM toolinghistory
WHERE GroupID = @GroupID
  AND Action IS NULL
  AND ToolItem IS NULL
ORDER BY Id ASC
LIMIT 1;";

            using var conn = new MySqlConnection(_connectionString);
            conn.Open();
            using var cmd = new MySqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@GroupID", groupID);
            using var r = cmd.ExecuteReader();
            if (!r.Read()) return null;

            return new ToolingHistoryModel
            {
                Id = Convert.ToInt32(r["Id"]),
                GroupID = groupID,
                Part = r["Part"]?.ToString(),
                PO = r["PO"]?.ToString(),
                Reason = r["Reason"]?.ToString(),
                ToolVendor = r["ToolVendor"]?.ToString(),
                DateInitiated = r["DateInitiated"] != DBNull.Value ? Convert.ToDateTime(r["DateInitiated"]) : DateTime.Today,
                DateDue = r["DateDue"] != DBNull.Value ? (DateTime?)Convert.ToDateTime(r["DateDue"]) : null,
                AccountingCode = r["AccountingCode"] != DBNull.Value ? (int?)Convert.ToInt32(r["AccountingCode"]) : null
                // Add InitiatedBy if you have that column.
            };
        }

        public void UpdateToolItem(ToolItemViewModel item)
        {
            string query = @"
                UPDATE toolinghistory 
                SET 
                    ToolNumber = @ToolNumber,
                    Action = @Action,
                    ToolItem = @ToolItem,
                    ToolDesc = @ToolDesc,
                    Cost = @Cost,
                    Revision = @Revision,
                    Quantity = @Quantity,
                    ToolWorkHours = @ToolWorkHours,
                    DateDue = @DateDue,
                    DateFitted = @DateFitted,
                    ReceivedBy = @ReceivedBy,
                    FittedBy = @FittedBy
                WHERE Id = @Id
            ";

            using (var conn = new MySqlConnection(_connectionString))
            {
                conn.Open();
                using (var cmd = new MySqlCommand(query, conn))
                {
                    cmd.Parameters.AddWithValue("@Id", item.Id);
                    cmd.Parameters.AddWithValue("@ToolNumber", item.ToolNumber ?? (object)DBNull.Value);
                    cmd.Parameters.AddWithValue("@Action", item.Action ?? (object)DBNull.Value);
                    cmd.Parameters.AddWithValue("@ToolItem", item.ToolItem ?? (object)DBNull.Value);
                    cmd.Parameters.AddWithValue("@ToolDesc", item.ToolDesc ?? (object)DBNull.Value);
                    cmd.Parameters.AddWithValue("@Cost", item.Cost ?? (object)DBNull.Value);
                    cmd.Parameters.AddWithValue("@Revision", item.Revision ?? (object)DBNull.Value);
                    cmd.Parameters.AddWithValue("@Quantity", item.Quantity ?? (object)DBNull.Value);
                    cmd.Parameters.AddWithValue("@ToolWorkHours", item.ToolWorkHours ?? (object)DBNull.Value);
                    cmd.Parameters.AddWithValue("@DateDue", item.DateDue ?? (object)DBNull.Value);
                    cmd.Parameters.AddWithValue("@DateFitted", item.DateFitted ?? (object)DBNull.Value);
                    cmd.Parameters.AddWithValue("@ReceivedBy", item.ReceivedBy ?? (object)DBNull.Value);
                    cmd.Parameters.AddWithValue("@FittedBy", item.FittedBy ?? (object)DBNull.Value);

                    var rowsAffected = cmd.ExecuteNonQuery();
                    Console.WriteLine($"UpdateToolItem => Rows updated: {rowsAffected}");
                }
            }
        }
        public void ReceiveAllInGroup(int groupID, string receivedBy, DateTime receivedDate, bool alsoFit)
        {
            using (var conn = new MySqlConnection(_connectionString))
            {
                conn.Open();
                using (var tx = conn.BeginTransaction())
                {
                    // 1) mark all real items as received
                    using (var cmd = new MySqlCommand(@"
                UPDATE toolinghistory
                SET ReceivedBy = @ReceivedBy
                WHERE GroupID = @GroupID
                  AND (Action IS NOT NULL OR ToolItem IS NOT NULL OR ToolNumber IS NOT NULL OR ToolDesc IS NOT NULL);
            ", conn, tx))
                    {
                        cmd.Parameters.AddWithValue("@GroupID", groupID);
                        cmd.Parameters.AddWithValue("@ReceivedBy", receivedBy);
                        var n = cmd.ExecuteNonQuery();
                        Console.WriteLine($"[ReceiveAllInGroup] ReceivedBy updated rows: {n}");
                    }

                    // 2) optionally fit ALL items with the same name
                    if (alsoFit)
                    {
                        using (var cmd2 = new MySqlCommand(@"
                    UPDATE toolinghistory
                    SET FittedBy = @FittedBy,
                        DateFitted = @DateFitted
                    WHERE GroupID = @GroupID
                      AND (Action IS NOT NULL OR ToolItem IS NOT NULL OR ToolNumber IS NOT NULL OR ToolDesc IS NOT NULL);
                ", conn, tx))
                        {
                            cmd2.Parameters.AddWithValue("@GroupID", groupID);
                            cmd2.Parameters.AddWithValue("@FittedBy", receivedBy);
                            cmd2.Parameters.AddWithValue("@DateFitted", receivedDate);
                            var m = cmd2.ExecuteNonQuery();
                            Console.WriteLine($"[ReceiveAllInGroup] FittedBy updated rows: {m}");
                        }
                    }

                    tx.Commit();
                }
            }
        }



        public void FitAllInGroup(int groupID, string fittedBy, DateTime fittedDate)
        {
            var sql = @"
        UPDATE toolinghistory
        SET FittedBy = @FittedBy, DateFitted = @DateFitted
        WHERE GroupID = @GroupID AND Action IS NOT NULL;
    ";

            using (var conn = new MySqlConnection(_connectionString))
            {
                conn.Open();
                using (var cmd = new MySqlCommand(sql, conn))
                {
                    cmd.Parameters.AddWithValue("@GroupID", groupID);
                    cmd.Parameters.AddWithValue("@FittedBy", fittedBy);
                    cmd.Parameters.AddWithValue("@DateFitted", fittedDate);
                    cmd.ExecuteNonQuery();
                }
            }
        }

    }
}
