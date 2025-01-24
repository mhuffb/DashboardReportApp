using DashboardReportApp.Models;
using MySql.Data.MySqlClient;
using System;
using System.Collections.Generic;
using System.Configuration;

namespace DashboardReportApp.Services
{
    public class DeviationService
    {
        private readonly string _connectionString;

        public DeviationService(IConfiguration configuration)
        {
            _connectionString = configuration.GetConnectionString("MySQLConnection");
        }

        public List<string> GetOperators()
        {
            var operators = new List<string>();
            string query = "SELECT DISTINCT name FROM operators ORDER BY name";

            using (var connection = new MySqlConnection(_connectionString))
            {
                connection.Open();
                using (var command = new MySqlCommand(query, connection))
                using (var reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        operators.Add(reader["name"].ToString());
                    }
                }
            }

            return operators;
        }

        public void SaveDeviation(DeviationViewModel model)
        {
            string query = @"
                INSERT INTO deviation (part, sentDateTime, discrepancy, operator, commMethod, disposition, approvedBy, dateTimeCASTReview)
                VALUES (@part, @sentDateTime, @discrepancy, @operator, @commMethod, @disposition, @approvedBy, @dateTimeCASTReview)";

            using (var connection = new MySqlConnection(_connectionString))
            {
                connection.Open();
                using (var command = new MySqlCommand(query, connection))
                {
                    command.Parameters.AddWithValue("@part", model.Part?.ToUpper());
                    command.Parameters.AddWithValue("@sentDateTime", DateTime.Now);
                    command.Parameters.AddWithValue("@discrepancy", model.Discrepancy);
                    command.Parameters.AddWithValue("@operator", model.Operator);
                    command.Parameters.AddWithValue("@commMethod", model.CommMethod);
                    command.Parameters.AddWithValue("@disposition", (object)model.Disposition ?? DBNull.Value);
                    command.Parameters.AddWithValue("@approvedBy", (object)model.ApprovedBy ?? DBNull.Value);
                    command.Parameters.AddWithValue("@dateTimeCASTReview", (object)model.DateTimeCASTReview ?? DBNull.Value);

                    command.ExecuteNonQuery();
                }
            }
        }
    }
}
