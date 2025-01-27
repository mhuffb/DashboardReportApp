using DashboardReportApp.Models;
using MySql.Data.MySqlClient;
using System;
using System.Collections.Generic;
using static Microsoft.EntityFrameworkCore.DbLoggerCategory.Database;

namespace DashboardReportApp.Services
{
    public class ToolingHistoryService
    {
        private readonly string _connectionString;

        public ToolingHistoryService(IConfiguration configuration)
        {
            _connectionString = configuration.GetConnectionString("MySQLConnection");
        }

        public List<ToolingHistory> GetToolingHistories()
        {
            var toolingHistories = new List<ToolingHistory>();
            string query = "SELECT part, id, reason, toolvendor, dateinitiated, toolnumber, cost, revision, po, toolworkhours, tooldesc, groupid, datedue, accountingcode FROM toolinghistory where part is not null";

            using (var connection = new MySqlConnection(_connectionString))
            {
                connection.Open();
                using (var command = new MySqlCommand(query, connection))
                using (var reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        var toolingHistory = new ToolingHistory
                        {
                            Id = Convert.ToInt32(reader["Id"]),
                            Reason = reader["Reason"].ToString(),
                            ToolVendor = reader["ToolVendor"].ToString(),
                            DateInitiated = reader["DateInitiated"] != DBNull.Value ? Convert.ToDateTime(reader["DateInitiated"]) : DateTime.Today,
                            Part = reader["Part"].ToString(),
                            ToolNumber = reader["ToolNumber"].ToString(),
                            Cost = reader["Cost"] != DBNull.Value ? (decimal?)Convert.ToDecimal(reader["Cost"]) : null,
                            Revision = reader["Revision"] != DBNull.Value ? reader["Revision"].ToString() : null,
                            PO = reader["PO"] != DBNull.Value ? reader["PO"].ToString() : null,
                            ToolWorkHours = reader["ToolWorkHours"] != DBNull.Value ? (int?)Convert.ToInt32(reader["ToolWorkHours"]) : null,
                            ToolDesc = reader["ToolDesc"] != DBNull.Value ? reader["ToolDesc"].ToString() : null,
                            GroupID = Convert.ToInt32(reader["GroupID"]),
                            DateDue = reader["DateDue"] != DBNull.Value ? (DateTime?)Convert.ToDateTime(reader["DateDue"]) : DateTime.Today,
                            // Read the accountingCode column if it exists
                            AccountingCode = reader["accountingCode"] != DBNull.Value
                                ? (int?)Convert.ToInt32(reader["accountingCode"])
                                : null
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
                    return Convert.ToInt32(result); // Returns the next GroupID
                }
            }
        }


        public void AddToolingHistory(ToolingHistory model)
        {
            // Get the next GroupID
            model.GroupID = GetNextGroupID();
            // 1. Determine the accountingCode from model.Reason
            int? accountingCode = null;
            if (model.Reason == "New")
                accountingCode = 5030;
            else if (model.Reason == "Repair")
                accountingCode = 5045;
            else if (model.Reason == "Breakage")
                accountingCode = 5040;
            else if (model.Reason == "Fitting")
                accountingCode = null;  // or pick a code if desired
            else
                accountingCode = null;
            string query = @"
        INSERT INTO toolinghistory 
        (GroupID, Reason, ToolVendor, DateInitiated, DateDue,  Part, ToolNumber, Cost, Revision, PO, ToolWorkHours, ToolDesc) 
        VALUES 
        (@GroupID, @Reason, @ToolVendor, @DateInitiated, @DateDue, @Part, @ToolNumber, @Cost, @Revision, @PO, @ToolWorkHours, @ToolDesc)";

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
                    // 2. Add the accountingCode parameter
                    command.Parameters.AddWithValue("@AccountingCode",
                        accountingCode.HasValue ? (object)accountingCode.Value : DBNull.Value);

                    command.ExecuteNonQuery();
                }
            }
        }

        public void UpdateToolingHistory(ToolingHistory model)
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
                    ToolDesc = @ToolDesc
                WHERE Id = @Id";

            using (var connection = new MySqlConnection(_connectionString))
            {
                connection.Open();
                using (var command = new MySqlCommand(query, connection))
                {
                    command.Parameters.AddWithValue("@Id", model.Id);
                    command.Parameters.AddWithValue("@Reason", model.Reason);
                    command.Parameters.AddWithValue("@ToolVendor", model.ToolVendor);
                    command.Parameters.AddWithValue("@DateInitiated", model.DateInitiated);
                    command.Parameters.AddWithValue("@Part", model.Part);
                    command.Parameters.AddWithValue("@ToolNumber", model.ToolNumber);
                    command.Parameters.AddWithValue("@Cost", model.Cost);
                    command.Parameters.AddWithValue("@Revision", model.Revision);
                    command.Parameters.AddWithValue("@PO", model.PO);
                    command.Parameters.AddWithValue("@ToolWorkHours", model.ToolWorkHours);
                    command.Parameters.AddWithValue("@ToolDesc", model.ToolDesc);

                    command.ExecuteNonQuery();
                }
            }
        }
        public List<ToolItemViewModel> GetToolItemsByGroupID(int groupID)
        {
            var toolItems = new List<ToolItemViewModel>();
            string query = "SELECT * FROM toolinghistory WHERE GroupID = @GroupID";

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
                                GroupID = groupID,
                                ToolItem = reader["toolItem"] != DBNull.Value
                            ? reader["toolItem"].ToString()
                            : null,
                                ToolNumber = reader["ToolNumber"].ToString(),
                                ToolDesc = reader["ToolDesc"].ToString(),
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

        public void AddOrUpdateToolItem(ToolItemViewModel toolItem)
        {
            var query = @"
        INSERT INTO toolinghistory 
        (toolItem, GroupID, ToolNumber, ToolDesc, Cost, Revision, Quantity, ToolWorkHours, 
         DateDue, DateFitted, ReceivedBy, FittedBy, Action)
        VALUES
        (@ToolItem, @GroupID, @ToolNumber, @ToolDesc, @Cost, @Revision, @Quantity, @ToolWorkHours,
         @DateDue, @DateFitted, @ReceivedBy, @FittedBy, @Action);
    ";

            using (var conn = new MySqlConnection(_connectionString))
            {
                conn.Open();
                using (var cmd = new MySqlCommand(query, conn))
                {
                    cmd.Parameters.AddWithValue("@ToolItem", toolItem.ToolItem ?? (object)DBNull.Value);

                    cmd.Parameters.AddWithValue("@GroupID", toolItem.GroupID);
                    cmd.Parameters.AddWithValue("@ToolNumber", toolItem.ToolNumber);
                    cmd.Parameters.AddWithValue("@ToolDesc", toolItem.ToolDesc);
                    cmd.Parameters.AddWithValue("@Cost", toolItem.Cost ?? (object)DBNull.Value);
                    cmd.Parameters.AddWithValue("@Revision", toolItem.Revision ?? (object)DBNull.Value);
                    cmd.Parameters.AddWithValue("@Quantity", toolItem.Quantity ?? (object)DBNull.Value);
                    cmd.Parameters.AddWithValue("@ToolWorkHours", toolItem.ToolWorkHours ?? (object)DBNull.Value);
                    cmd.Parameters.AddWithValue("@DateDue", toolItem.DateDue ?? (object)DBNull.Value);
                    cmd.Parameters.AddWithValue("@DateFitted", toolItem.DateFitted ?? (object)DBNull.Value);
                    cmd.Parameters.AddWithValue("@ReceivedBy", toolItem.ReceivedBy ?? (object)DBNull.Value);
                    cmd.Parameters.AddWithValue("@FittedBy", toolItem.FittedBy ?? (object)DBNull.Value);
                    cmd.Parameters.AddWithValue("@Action", toolItem.Action ?? (object)DBNull.Value);

                    cmd.ExecuteNonQuery();
                    //var rowsAffected = cmd.ExecuteNonQuery();
                    //Console.WriteLine($"Rows affected: {rowsAffected}");

                }
            }
        }


    }
}
