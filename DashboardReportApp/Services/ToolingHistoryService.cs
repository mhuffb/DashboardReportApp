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
            var list = new List<ToolingHistoryModel>();
            const string sql = @"
SELECT Id, GroupID, Part, ToolNumber, Revision, PO, Reason, ToolVendor, DateInitiated, DateDue,
       Cost, ToolWorkHours, ToolDesc, AccountingCode, InitiatedBy, DateReceived
FROM tooling_history_header
ORDER BY Id DESC;";

            using var conn = new MySqlConnection(_connectionString);
            conn.Open();
            using var cmd = new MySqlCommand(sql, conn);
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                list.Add(new ToolingHistoryModel
                {
                    Id = Convert.ToInt32(r["Id"]),
                    GroupID = Convert.ToInt32(r["GroupID"]),
                    Part = r["Part"]?.ToString(),
                    ToolNumber = r["ToolNumber"]?.ToString(),
                    Revision = r["Revision"]?.ToString(),
                    PO = r["PO"]?.ToString(),
                    Reason = r["Reason"]?.ToString(),
                    ToolVendor = r["ToolVendor"]?.ToString(),
                    DateInitiated = Convert.ToDateTime(r["DateInitiated"]),
                    DateDue = r["DateDue"] == DBNull.Value ? null : (DateTime?)Convert.ToDateTime(r["DateDue"]),
                    Cost = r["Cost"] == DBNull.Value ? null : (decimal?)Convert.ToDecimal(r["Cost"]),
                    ToolWorkHours = r["ToolWorkHours"] == DBNull.Value ? null : (int?)Convert.ToInt32(r["ToolWorkHours"]),
                    ToolDesc = r["ToolDesc"]?.ToString(),
                    AccountingCode = r["AccountingCode"] == DBNull.Value ? null : (int?)Convert.ToInt32(r["AccountingCode"]),
                    InitiatedBy = r["InitiatedBy"]?.ToString(),
                    DateReceived = r["DateReceived"] == DBNull.Value ? null : (DateTime?)Convert.ToDateTime(r["DateReceived"])
                });
            }
            return list;
        }


        public int GetNextGroupID()
        {
            const string sql = "SELECT IFNULL(MAX(GroupID), 0) + 1 FROM tooling_history_header;";
            using var conn = new MySqlConnection(_connectionString);
            conn.Open();
            using var cmd = new MySqlCommand(sql, conn);
            return Convert.ToInt32(cmd.ExecuteScalar());
        }


        public ToolingHistoryModel? GetToolingHistoryById(int id)
        {
            const string sql = @"
SELECT Id, GroupID, Part, ToolNumber, Revision, PO, Reason, ToolVendor, DateInitiated, DateDue,
       Cost, ToolWorkHours, ToolDesc, AccountingCode, InitiatedBy, DateReceived
FROM tooling_history_header
WHERE Id = @Id
LIMIT 1;";

            using var conn = new MySqlConnection(_connectionString);
            conn.Open();
            using var cmd = new MySqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@Id", id);
            using var r = cmd.ExecuteReader();
            if (!r.Read()) return null;

            return new ToolingHistoryModel
            {
                Id = Convert.ToInt32(r["Id"]),
                GroupID = Convert.ToInt32(r["GroupID"]),
                Part = r["Part"]?.ToString(),
                ToolNumber = r["ToolNumber"]?.ToString(),
                Revision = r["Revision"]?.ToString(),
                PO = r["PO"]?.ToString(),
                Reason = r["Reason"]?.ToString(),
                ToolVendor = r["ToolVendor"]?.ToString(),
                DateInitiated = Convert.ToDateTime(r["DateInitiated"]),
                DateDue = r["DateDue"] == DBNull.Value ? null : (DateTime?)Convert.ToDateTime(r["DateDue"]),
                Cost = r["Cost"] == DBNull.Value ? null : (decimal?)Convert.ToDecimal(r["Cost"]),
                ToolWorkHours = r["ToolWorkHours"] == DBNull.Value ? null : (int?)Convert.ToInt32(r["ToolWorkHours"]),
                ToolDesc = r["ToolDesc"]?.ToString(),
                AccountingCode = r["AccountingCode"] == DBNull.Value ? null : (int?)Convert.ToInt32(r["AccountingCode"]),
                InitiatedBy = r["InitiatedBy"]?.ToString(),
                DateReceived = r["DateReceived"] == DBNull.Value ? null : (DateTime?)Convert.ToDateTime(r["DateReceived"])
            };
        }
        private int GetHeaderIdByGroup(MySqlConnection conn, MySqlTransaction? tx, int groupId)
        {
            const string sql = "SELECT Id FROM tooling_history_header WHERE GroupID=@G LIMIT 1;";
            using var cmd = new MySqlCommand(sql, conn, tx);
            cmd.Parameters.AddWithValue("@G", groupId);
            var o = cmd.ExecuteScalar();
            if (o == null || o == DBNull.Value) throw new Exception($"No header found for GroupID {groupId}.");
            return Convert.ToInt32(o);
        }


        public void AddToolItem(ToolItemViewModel m)
        {
            const string sql = @"
INSERT INTO tooling_history_item
 (HeaderId, GroupID, Action, ToolItem, ToolNumber, ToolDesc, Revision, Quantity, Cost, ToolWorkHours,
  DateDue, DateFitted, DateReceived, ReceivedBy, FittedBy)
VALUES
 (@HeaderId, @GroupID, @Action, @ToolItem, @ToolNumber, @ToolDesc, @Revision, @Quantity, @Cost, @ToolWorkHours,
  @DateDue, @DateFitted, @DateReceived, @ReceivedBy, @FittedBy);";

            using var conn = new MySqlConnection(_connectionString);
            conn.Open();
            using var tx = conn.BeginTransaction();
            var headerId = GetHeaderIdByGroup(conn, tx, m.GroupID);

            using var cmd = new MySqlCommand(sql, conn, tx);
            cmd.Parameters.AddWithValue("@HeaderId", headerId);
            cmd.Parameters.AddWithValue("@GroupID", m.GroupID);
            cmd.Parameters.AddWithValue("@Action", (object?)m.Action ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@ToolItem", (object?)m.ToolItem ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@ToolNumber", (object?)m.ToolNumber ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@ToolDesc", (object?)m.ToolDesc ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@Revision", (object?)m.Revision ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@Quantity", (object?)m.Quantity ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@Cost", (object?)m.Cost ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@ToolWorkHours", (object?)m.ToolWorkHours ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@DateDue", (object?)m.DateDue ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@DateFitted", (object?)m.DateFitted ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@DateReceived", (object?)m.DateReceived ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@ReceivedBy", (object?)m.ReceivedBy ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@FittedBy", (object?)m.FittedBy ?? DBNull.Value);
            cmd.ExecuteNonQuery();
            tx.Commit();
        }

        public void UpdateToolItem(ToolItemViewModel m)
        {
            const string sql = @"
UPDATE tooling_history_item SET
  Action=@Action, ToolItem=@ToolItem, ToolNumber=@ToolNumber, ToolDesc=@ToolDesc, Revision=@Revision,
  Quantity=@Quantity, Cost=@Cost, ToolWorkHours=@ToolWorkHours, DateDue=@DateDue, DateFitted=@DateFitted,
  DateReceived=@DateReceived, ReceivedBy=@ReceivedBy, FittedBy=@FittedBy
WHERE Id=@Id;";

            using var conn = new MySqlConnection(_connectionString);
            conn.Open();
            using var cmd = new MySqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@Id", m.Id);
            cmd.Parameters.AddWithValue("@Action", (object?)m.Action ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@ToolItem", (object?)m.ToolItem ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@ToolNumber", (object?)m.ToolNumber ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@ToolDesc", (object?)m.ToolDesc ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@Revision", (object?)m.Revision ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@Quantity", (object?)m.Quantity ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@Cost", (object?)m.Cost ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@ToolWorkHours", (object?)m.ToolWorkHours ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@DateDue", (object?)m.DateDue ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@DateFitted", (object?)m.DateFitted ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@DateReceived", (object?)m.DateReceived ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@ReceivedBy", (object?)m.ReceivedBy ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@FittedBy", (object?)m.FittedBy ?? DBNull.Value);
            cmd.ExecuteNonQuery();
        }
        public void AddToolingHistory(ToolingHistoryModel m)
        {
            using var conn = new MySqlConnection(_connectionString);
            conn.Open();

            // ensure unique GroupID
            using (var check = new MySqlCommand(
                "SELECT COUNT(*) FROM tooling_history_header WHERE GroupID=@G;", conn))
            {
                check.Parameters.AddWithValue("@G", m.GroupID);
                var exists = Convert.ToInt32(check.ExecuteScalar()) > 0;
                if (exists)
                {
                    using var maxCmd = new MySqlCommand("SELECT IFNULL(MAX(GroupID),0)+1 FROM tooling_history_header;", conn);
                    m.GroupID = Convert.ToInt32(maxCmd.ExecuteScalar());
                }
            }

            int? accountingCode = m.Reason switch
            {
                "New" => 5030,
                "Repair" => 5045,
                "Breakage" => 5040,
                _ => null
            };

            const string sql = @"
INSERT INTO tooling_history_header
 (GroupID, Part, ToolNumber, Revision, PO, Reason, ToolVendor, DateInitiated, DateDue, Cost,
  ToolWorkHours, ToolDesc, AccountingCode, InitiatedBy, DateReceived)
VALUES
 (@GroupID, @Part, @ToolNumber, @Revision, @PO, @Reason, @ToolVendor, @DateInitiated, @DateDue, @Cost,
  @ToolWorkHours, @ToolDesc, @AccountingCode, @InitiatedBy, @DateReceived);";

            using var cmd = new MySqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@GroupID", m.GroupID);
            cmd.Parameters.AddWithValue("@Part", (object?)m.Part ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@ToolNumber", (object?)m.ToolNumber ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@Revision", (object?)m.Revision ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@PO", (object?)m.PO ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@Reason", (object?)m.Reason ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@ToolVendor", (object?)m.ToolVendor ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@DateInitiated", m.DateInitiated);
            cmd.Parameters.AddWithValue("@DateDue", (object?)m.DateDue ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@Cost", (object?)m.Cost ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@ToolWorkHours", (object?)m.ToolWorkHours ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@ToolDesc", (object?)m.ToolDesc ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@AccountingCode", (object?)accountingCode ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@InitiatedBy", string.IsNullOrWhiteSpace(m.InitiatedBy) ? "Emery, J" : m.InitiatedBy);
            cmd.Parameters.AddWithValue("@DateReceived", (object?)m.DateReceived ?? DBNull.Value);
            cmd.ExecuteNonQuery();
        }


        public void UpdateToolingHistory(ToolingHistoryModel m)
        {
            const string sql = @"
UPDATE tooling_history_header SET
  Part=@Part, ToolNumber=@ToolNumber, Revision=@Revision, PO=@PO, Reason=@Reason, ToolVendor=@ToolVendor,
  DateInitiated=@DateInitiated, DateDue=@DateDue, Cost=@Cost, ToolWorkHours=@ToolWorkHours, ToolDesc=@ToolDesc,
  InitiatedBy=@InitiatedBy, DateReceived=@DateReceived
WHERE Id=@Id;";

            using var conn = new MySqlConnection(_connectionString);
            conn.Open();
            using var cmd = new MySqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@Id", m.Id);
            cmd.Parameters.AddWithValue("@Part", (object?)m.Part ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@ToolNumber", (object?)m.ToolNumber ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@Revision", (object?)m.Revision ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@PO", (object?)m.PO ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@Reason", (object?)m.Reason ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@ToolVendor", (object?)m.ToolVendor ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@DateInitiated", m.DateInitiated);
            cmd.Parameters.AddWithValue("@DateDue", (object?)m.DateDue ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@Cost", (object?)m.Cost ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@ToolWorkHours", (object?)m.ToolWorkHours ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@ToolDesc", (object?)m.ToolDesc ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@InitiatedBy", string.IsNullOrWhiteSpace(m.InitiatedBy) ? (object)DBNull.Value : m.InitiatedBy);
            cmd.Parameters.AddWithValue("@DateReceived", (object?)m.DateReceived ?? DBNull.Value);
            cmd.ExecuteNonQuery();
        }





        public List<ToolItemViewModel> GetToolItemsByGroupID(int groupID)
        {
            var list = new List<ToolItemViewModel>();
            const string sql = @"
SELECT i.Id, i.GroupID, i.Action, i.ToolItem, i.ToolNumber, i.ToolDesc, i.Revision,
       i.Quantity, i.Cost, i.ToolWorkHours, i.DateDue, i.DateFitted,
       i.DateReceived, i.ReceivedBy, i.FittedBy
FROM tooling_history_item i
WHERE i.GroupID = @GroupID
ORDER BY i.Id ASC;";

            using var conn = new MySqlConnection(_connectionString);
            conn.Open();
            using var cmd = new MySqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@GroupID", groupID);
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                list.Add(new ToolItemViewModel
                {
                    Id = Convert.ToInt32(r["Id"]),
                    GroupID = Convert.ToInt32(r["GroupID"]),
                    Action = r["Action"]?.ToString(),
                    ToolItem = r["ToolItem"]?.ToString(),
                    ToolNumber = r["ToolNumber"]?.ToString(),
                    ToolDesc = r["ToolDesc"]?.ToString(),
                    Revision = r["Revision"]?.ToString(),
                    Quantity = r["Quantity"] == DBNull.Value ? 0 : Convert.ToInt32(r["Quantity"]),

                    Cost = r["Cost"] == DBNull.Value ? null : (decimal?)Convert.ToDecimal(r["Cost"]),
                    ToolWorkHours = r["ToolWorkHours"] == DBNull.Value ? null : (int?)Convert.ToInt32(r["ToolWorkHours"]),
                    DateDue = r["DateDue"] == DBNull.Value ? null : (DateTime?)Convert.ToDateTime(r["DateDue"]),
                    DateFitted = r["DateFitted"] == DBNull.Value ? null : (DateTime?)Convert.ToDateTime(r["DateFitted"]),
                    DateReceived = r["DateReceived"] == DBNull.Value ? null : (DateTime?)Convert.ToDateTime(r["DateReceived"]),
                    ReceivedBy = r["ReceivedBy"]?.ToString(),
                    FittedBy = r["FittedBy"]?.ToString()
                });
            }
            return list;
        }



      
     

       
        public void ReceiveAllInGroup(int groupID, string receivedBy, DateTime receivedDate, bool alsoFit)
        {
            using var conn = new MySqlConnection(_connectionString);
            conn.Open();
            using var tx = conn.BeginTransaction();

            const string receiveSql = @"
UPDATE tooling_history_item
SET ReceivedBy=@ReceivedBy, DateReceived=@DateReceived
WHERE GroupID=@GroupID;";

            using (var cmd = new MySqlCommand(receiveSql, conn, tx))
            {
                cmd.Parameters.AddWithValue("@GroupID", groupID);
                cmd.Parameters.AddWithValue("@ReceivedBy", receivedBy);
                cmd.Parameters.AddWithValue("@DateReceived", receivedDate);
                cmd.ExecuteNonQuery();
            }

            if (alsoFit)
            {
                const string fitSql = @"
UPDATE tooling_history_item
SET FittedBy=@FittedBy, DateFitted=@DateFitted
WHERE GroupID=@GroupID;";

                using var cmd2 = new MySqlCommand(fitSql, conn, tx);
                cmd2.Parameters.AddWithValue("@GroupID", groupID);
                cmd2.Parameters.AddWithValue("@FittedBy", receivedBy);
                cmd2.Parameters.AddWithValue("@DateFitted", receivedDate);
                cmd2.ExecuteNonQuery();
            }

            tx.Commit();
        }

        public void FitAllInGroup(int groupID, string fittedBy, DateTime fittedDate)
        {
            const string sql = @"
UPDATE tooling_history_item
SET FittedBy=@FittedBy, DateFitted=@DateFitted
WHERE GroupID=@GroupID;";

            using var conn = new MySqlConnection(_connectionString);
            conn.Open();
            using var cmd = new MySqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@GroupID", groupID);
            cmd.Parameters.AddWithValue("@FittedBy", fittedBy);
            cmd.Parameters.AddWithValue("@DateFitted", fittedDate);
            cmd.ExecuteNonQuery();
        }

        public ToolingHistoryModel? GetHeaderByGroupID(int groupID)
        {
            const string sql = @"
        SELECT
            Id,
            GroupID,
            Part,
            PO,
            Reason,
            ToolVendor,
            DateInitiated,
            DateDue,
            AccountingCode,
            ToolNumber,
            Revision,
            Cost,
            ToolWorkHours,
            ToolDesc,
            InitiatedBy,
            DateReceived
        FROM tooling_history_header
        WHERE GroupID = @GroupID
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
                GroupID = Convert.ToInt32(r["GroupID"]),
                Part = r["Part"]?.ToString(),
                PO = r["PO"]?.ToString(),
                Reason = r["Reason"]?.ToString(),
                ToolVendor = r["ToolVendor"]?.ToString(),
                DateInitiated = r["DateInitiated"] != DBNull.Value ? Convert.ToDateTime(r["DateInitiated"]) : DateTime.Today,
                DateDue = r["DateDue"] != DBNull.Value ? (DateTime?)Convert.ToDateTime(r["DateDue"]) : null,
                AccountingCode = r["AccountingCode"] != DBNull.Value ? (int?)Convert.ToInt32(r["AccountingCode"]) : null,
                ToolNumber = r["ToolNumber"]?.ToString(),
                Revision = r["Revision"]?.ToString(),
                Cost = r["Cost"] != DBNull.Value ? (decimal?)Convert.ToDecimal(r["Cost"]) : null,
                ToolWorkHours = r["ToolWorkHours"] != DBNull.Value ? (int?)Convert.ToInt32(r["ToolWorkHours"]) : null,
                ToolDesc = r["ToolDesc"]?.ToString(),
                InitiatedBy = r["InitiatedBy"] != DBNull.Value ? r["InitiatedBy"]?.ToString() : null,
                DateReceived = r["DateReceived"] != DBNull.Value ? (DateTime?)Convert.ToDateTime(r["DateReceived"]) : null
            };
        }
    }
}
