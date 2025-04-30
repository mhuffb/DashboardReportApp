using DashboardReportApp.Models;
using MySql.Data.MySqlClient;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace DashboardReportApp.Services
{
    public class QCSecondaryHoldReturnService
    {
        private readonly string _connectionString;

        public QCSecondaryHoldReturnService(IConfiguration configuration)
        {
            _connectionString = configuration.GetConnectionString("MySQLConnection");
        }

        public async Task<List<string>> GetOperatorsAsync()
        {
            var operators = new List<string>();
            string query = "SELECT name FROM operators WHERE dept = 'qc' ORDER BY name";

            using (var connection = new MySqlConnection(_connectionString))
            {
                await connection.OpenAsync();
                using (var command = new MySqlCommand(query, connection))
                using (var reader = await command.ExecuteReaderAsync())
                {
                    while (await reader.ReadAsync())
                    {
                        operators.Add(reader["name"].ToString());
                    }
                }
            }

            return operators;
        }

        public async Task AddHoldReturnAsync(QCSecondaryHoldReturnModel record)
        {
            string query = @"
INSERT INTO qcsecondaryholdreturn
    (operator, prodNumber, op, run,
     qtyreturned_machined, qtyreturned_nonmachined, notes, timestamp)
VALUES
    (@operator, @prodNumber, @op, @run,
     @qtyreturned_machined, @qtyreturned_nonmachined, @notes, @timestamp)";


            using (var connection = new MySqlConnection(_connectionString))
            {
                await connection.OpenAsync();
                using (var command = new MySqlCommand(query, connection))
                {
                    command.Parameters.AddWithValue("@operator", record.Operator);
                    command.Parameters.AddWithValue("@run", record.Run);
                    command.Parameters.AddWithValue("@qtyreturned_machined", record.QtyReturnedMachined);
                    command.Parameters.AddWithValue("@qtyreturned_nonmachined", record.QtyReturnedNonMachined);
                    command.Parameters.AddWithValue("@notes", record.Notes ?? (object)DBNull.Value);
                    command.Parameters.AddWithValue("@prodNumber", record.ProdNumber);   
                    command.Parameters.AddWithValue("@op", record.Op);
                    command.Parameters.AddWithValue("@timestamp", DateTime.Now);

                    await command.ExecuteNonQueryAsync();
                }
            }
        }

    }
}
