using DashboardReportApp.Models;
using System;
using System.Collections.Generic;
using Microsoft.Extensions.Configuration;
using MySql.Data.MySqlClient;

namespace DashboardReportApp.Services
{
    public class AdminDeviationService
    {
        private readonly string _connectionString;

        public AdminDeviationService(IConfiguration configuration)
        {
            _connectionString = configuration.GetConnectionString("MySQLConnection");
        }

        public List<AdminDeviationModel> GetAllDeviations()
        {
            var deviations = new List<AdminDeviationModel>();

            using (MySqlConnection conn = new MySqlConnection(_connectionString))
            {
                conn.Open();
                using (MySqlCommand cmd = new MySqlCommand("SELECT * FROM Deviation order by id desc", conn))
                {
                    using (MySqlDataReader reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            deviations.Add(new AdminDeviationModel
                            {
                                Id = reader.GetInt32("Id"),
                                Timestamp = reader.GetDateTime("Timestamp"),
                                Part = reader.IsDBNull(reader.GetOrdinal("Part")) ? string.Empty : reader.GetString("Part"),
                                SentDateTime = reader.IsDBNull(reader.GetOrdinal("SentDateTime")) ? (DateTime?)null : reader.GetDateTime("SentDateTime"),
                                Discrepancy = reader.IsDBNull(reader.GetOrdinal("Discrepancy")) ? string.Empty : reader.GetString("Discrepancy"),
                                Operator = reader.IsDBNull(reader.GetOrdinal("Operator")) ? string.Empty : reader.GetString("Operator"),
                                CommMethod = reader.IsDBNull(reader.GetOrdinal("CommMethod")) ? string.Empty : reader.GetString("CommMethod"),
                                Disposition = reader.IsDBNull(reader.GetOrdinal("Disposition")) ? string.Empty : reader.GetString("Disposition"),
                                ApprovedBy = reader.IsDBNull(reader.GetOrdinal("ApprovedBy")) ? string.Empty : reader.GetString("ApprovedBy"),
                                DateTimeCASTReview = reader.IsDBNull(reader.GetOrdinal("DateTimeCASTReview")) ? (DateTime?)null : reader.GetDateTime("DateTimeCASTReview")
                            });
                        }
                    }
                }
            }
            return deviations;
        }

        public void UpdateDeviation(AdminDeviationModel deviation)
        {
            using (MySqlConnection conn = new MySqlConnection(_connectionString))
            {
                conn.Open();
                using (MySqlCommand cmd = new MySqlCommand(@"
                    UPDATE deviation 
                    SET  Part = @Part, SentDateTime = @SentDateTime, 
                         Discrepancy = @Discrepancy, Operator = @Operator, CommMethod = @CommMethod, 
                         Disposition = @Disposition, ApprovedBy = @ApprovedBy, DateTimeCASTReview = @DateTimeCASTReview 
                    WHERE Id = @Id", conn))
                {
                    cmd.Parameters.AddWithValue("@Id", deviation.Id);
                    cmd.Parameters.AddWithValue("@Part", deviation.Part);
                    cmd.Parameters.AddWithValue("@SentDateTime", deviation.SentDateTime.HasValue ? (object)deviation.SentDateTime.Value : DBNull.Value);
                    cmd.Parameters.AddWithValue("@Discrepancy", deviation.Discrepancy);
                    cmd.Parameters.AddWithValue("@Operator", deviation.Operator);
                    cmd.Parameters.AddWithValue("@CommMethod", deviation.CommMethod);
                    cmd.Parameters.AddWithValue("@Disposition", deviation.Disposition);
                    cmd.Parameters.AddWithValue("@ApprovedBy", deviation.ApprovedBy);
                    cmd.Parameters.AddWithValue("@DateTimeCASTReview", deviation.DateTimeCASTReview.HasValue ? (object)deviation.DateTimeCASTReview.Value : DBNull.Value);

                    cmd.ExecuteNonQuery();
                }
            }
        }

        // Returns all operator names (for the Operator dropdown)
        public List<string> GetAllOperatorNames()
        {
            var names = new List<string>();
            using (MySqlConnection conn = new MySqlConnection(_connectionString))
            {
                conn.Open();
                string query = "SELECT name FROM operators ORDER BY name";
                using (MySqlCommand cmd = new MySqlCommand(query, conn))
                {
                    using (MySqlDataReader reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            names.Add(reader.IsDBNull(0) ? string.Empty : reader.GetString("name"));
                        }
                    }
                }
            }
            return names;
        }

        // Returns names from operator table where allowApprDeviation = 1 (for the Approved By dropdown)
        public List<string> GetApprovedByOperators()
        {
            var names = new List<string>();
            using (MySqlConnection conn = new MySqlConnection(_connectionString))
            {
                conn.Open();
                string query = "SELECT name FROM operators WHERE allowApprDeviation = 1 ORDER BY name";
                using (MySqlCommand cmd = new MySqlCommand(query, conn))
                {
                    using (MySqlDataReader reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            names.Add(reader.IsDBNull(0) ? string.Empty : reader.GetString("name"));
                        }
                    }
                }
            }
            return names;
        }

    }
}
