using MySql.Data.MySqlClient;
using Microsoft.Extensions.Configuration;
using System;
using System.Collections.Generic;
using DashboardReportApp.Models;
using System.Data;

namespace DashboardReportApp.Services
{
    public class MaintenanceAdminService
    {
        private readonly string _connectionString;

        public MaintenanceAdminService(IConfiguration configuration)
        {
            _connectionString = configuration.GetConnectionString("MySQLConnection");
        }

        public List<MaintenanceRequestModel> GetAllRequests()
        {
            var requests = new List<MaintenanceRequestModel>();
            string query = "SELECT * FROM maintenance ORDER BY id DESC";

            using (var connection = new MySqlConnection(_connectionString))
            {
                connection.Open();
                using (var command = new MySqlCommand(query, connection))
                using (var reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        requests.Add(new MaintenanceRequestModel
                        {
                            Id = Convert.ToInt32(reader["Id"]),
                            Timestamp = reader["Timestamp"] == DBNull.Value ? null : Convert.ToDateTime(reader["Timestamp"]),
                            Equipment = reader["Equipment"]?.ToString(),
                            Requester = reader["Requester"]?.ToString(),
                            RequestedDate = reader["ReqDate"] == DBNull.Value ? null : Convert.ToDateTime(reader["ReqDate"]),
                            Problem = reader["Problem"]?.ToString(),
                            DownStartDateTime = reader["DownStartDateTime"] == DBNull.Value ? null : Convert.ToDateTime(reader["DownStartDateTime"]),
                            ClosedDateTime = reader["ClosedDateTime"] == DBNull.Value ? null : Convert.ToDateTime(reader["ClosedDateTime"]),
                            CloseBy = reader["CloseBy"]?.ToString(),
                            CloseResult = reader["CloseResult"]?.ToString(),
                            DownStatus = !reader.IsDBNull(reader.GetOrdinal("downStatus")) ? reader.GetBoolean("downStatus") : false,
                            HourMeter = !reader.IsDBNull(reader.GetOrdinal("hourMeter")) ? reader.GetDecimal("hourMeter") : (decimal?)null,
                            HoldStatus = reader["HoldStatus"] == DBNull.Value ? null : Convert.ToBoolean(reader["HoldStatus"]),
                            HoldReason = reader["HoldReason"]?.ToString(),
                            HoldResult = reader["HoldResult"]?.ToString(),
                            HoldBy = reader["HoldBy"]?.ToString(),
                            FileAddress = reader["FileAddress"]?.ToString(),
                            FileAddressMediaLink = reader["fileAddressImageLink"] == DBNull.Value ? null : reader["fileAddressImageLink"].ToString(),
                            StatusHistory = reader["StatusHistory"]?.ToString(),
                            CurrentStatusBy = reader["CurrentStatusBy"]?.ToString(),
                            Department = reader["Department"]?.ToString()
                        });
                    }
                }
            }

            return requests;
        }


        public bool UpdateRequest(MaintenanceRequestModel model, IFormFile? file)
        {
            Console.WriteLine($"[DEBUG] Updating request ID: {model.Id}");

            string query = @"
        UPDATE maintenance 
        SET 
            Equipment = @Equipment,
            Requester = @Requester,
            ReqDate = @RequestedDate,
            Problem = @Problem,
            ClosedDateTime = @ClosedDateTime,
            CloseBy = @CloseBy,
            CloseResult = @CloseResult,
            DownStatus = @DownStatus,
            HourMeter = @HourMeter,
            HoldStatus = @HoldStatus,
            HoldReason = @HoldReason,
            HoldResult = @HoldResult,
            HoldBy = @HoldBy,
            FileAddress = @FileAddress,
            StatusHistory = @StatusHistory,
            CurrentStatusBy = @CurrentStatusBy,
            Department = @Department
        WHERE Id = @Id";

            using (var connection = new MySqlConnection(_connectionString))
            {
                connection.Open();
                using (var command = new MySqlCommand(query, connection))
                {
                    command.Parameters.AddWithValue("@Id", model.Id);
                    command.Parameters.AddWithValue("@Equipment", model.Equipment);
                    command.Parameters.AddWithValue("@Requester", model.Requester);
                    command.Parameters.AddWithValue("@RequestedDate", model.RequestedDate ?? (object)DBNull.Value);
                    command.Parameters.AddWithValue("@Problem", model.Problem);
                    command.Parameters.AddWithValue("@ClosedDateTime", model.ClosedDateTime ?? (object)DBNull.Value);
                    command.Parameters.AddWithValue("@CloseBy", model.CloseBy ?? (object)DBNull.Value);
                    command.Parameters.AddWithValue("@CloseResult", model.CloseResult ?? (object)DBNull.Value);
                    command.Parameters.AddWithValue("@DownStatus", model.DownStatus ?? (object)DBNull.Value);
                    command.Parameters.AddWithValue("@HourMeter", model.HourMeter ?? (object)DBNull.Value);
                    command.Parameters.AddWithValue("@HoldStatus", model.HoldStatus ?? (object)DBNull.Value);
                    command.Parameters.AddWithValue("@HoldReason", model.HoldReason ?? (object)DBNull.Value);
                    command.Parameters.AddWithValue("@HoldResult", model.HoldResult ?? (object)DBNull.Value);
                    command.Parameters.AddWithValue("@HoldBy", model.HoldBy ?? (object)DBNull.Value);
                    command.Parameters.AddWithValue("@FileAddress", model.FileAddress ?? (object)DBNull.Value);
                    command.Parameters.AddWithValue("@StatusHistory", model.StatusHistory ?? (object)DBNull.Value);
                    command.Parameters.AddWithValue("@CurrentStatusBy", model.CurrentStatusBy ?? (object)DBNull.Value);
                    command.Parameters.AddWithValue("@Department", model.Department ?? (object)DBNull.Value);

                    int rowsAffected = command.ExecuteNonQuery();
                    Console.WriteLine($"[DEBUG] Rows affected: {rowsAffected}");

                    return rowsAffected > 0;
                }
            }
        }

    }
}
