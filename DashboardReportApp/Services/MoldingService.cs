using DashboardReportApp.Models;
using MySql.Data.MySqlClient;

namespace DashboardReportApp.Services
{
    public class MoldingService
    {
        private readonly string _connectionString;

        public MoldingService(IConfiguration configuration)
        {
            _connectionString = configuration.GetConnectionString("MySQLConnection");
        }

        // Now returns all data without applying filtering/sorting on the server
        public MoldingModel GetData()
        {
            var pressRuns = GetPressRuns();
            var pressSetups = GetPressSetups();
            var pressLotChanges = GetPressLotChanges();

            return new MoldingModel
            {
                PressRuns = pressRuns,
                PressSetups = pressSetups,
                PressLotChanges = pressLotChanges
            };
        }

        private List<PressRunLogModel> GetPressRuns()
        {
            return QueryTable<PressRunLogModel>("pressrun");
        }

        private List<PressSetupModel> GetPressSetups()
        {
            return QueryTable<PressSetupModel>("presssetup");
        }

        private List<PressMixBagChangeModel> GetPressLotChanges()
        {
            return QueryTable<PressMixBagChangeModel>("presslotchange");
        }

        private List<T> QueryTable<T>(string tableName) where T : new()
        {
            var results = new List<T>();
            string query = $"SELECT * FROM {tableName}";

            using (var connection = new MySqlConnection(_connectionString))
            {
                connection.Open();
                using (var command = new MySqlCommand(query, connection))
                {
                    using (var reader = command.ExecuteReader())
                    {
                        // Build a list of column names available in the result set (using lower-case for comparison)
                        var columnNames = new List<string>();
                        for (int i = 0; i < reader.FieldCount; i++)
                        {
                            columnNames.Add(reader.GetName(i).ToLower());
                        }

                        while (reader.Read())
                        {
                            var obj = Activator.CreateInstance<T>();
                            foreach (var prop in typeof(T).GetProperties())
                            {
                                // Use case-insensitive comparison: check if any column matches the property name
                                string matchingColumn = columnNames
                                    .FirstOrDefault(c => c.Equals(prop.Name.ToLower(), StringComparison.OrdinalIgnoreCase));

                                if (matchingColumn != null)
                                {
                                    int ordinal = reader.GetOrdinal(matchingColumn);
                                    if (!reader.IsDBNull(ordinal))
                                    {
                                        object value = reader[ordinal];

                                        // Convert long to int if needed
                                        if (value is long && prop.PropertyType == typeof(int))
                                        {
                                            value = Convert.ToInt32(value);
                                        }

                                        prop.SetValue(obj, value);
                                    }
                                }
                            }
                            results.Add(obj);
                        }
                    }
                }
            }
            return results;
        }

    }
}
