using DashboardReportApp.Models;
using MySql.Data.MySqlClient;

namespace DashboardReportApp.Services
{
    public class MoldingService
    {
        private readonly string _connectionString;
        private readonly PressRunLogService _pressRunLogService; // We'll reuse its method

        public MoldingService(IConfiguration configuration, PressRunLogService pressRunLogService)
        {
            _connectionString = configuration.GetConnectionString("MySQLConnection");
            _pressRunLogService = pressRunLogService;
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
            string query = $"SELECT * FROM {tableName} ORDER BY ID DESC";

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
        /// <summary>
        /// Returns a dictionary of { "machineNumber": countValue } for all mapped machines.
        /// </summary>
        public async Task<Dictionary<string, int?>> GetAllMachineCountsAsync()
        {
            // Corrected machine list with proper commas and without duplicates
            var machineList = new List<string> {
        "1", "2", "41", "45", "50", "51", "57", "59", "70", "74",
        "92", "95", "102", "112", "124", "125", "154", "156", "175"
    };

            // Start all tasks concurrently
            var tasks = machineList.Select(async machine =>
            {
                int? count = await _pressRunLogService.TryGetDeviceCountOrNull(machine);
                return new { machine, count };
            });

            // Wait for all tasks to complete
            var results = await Task.WhenAll(tasks);

            // Build and return the dictionary from the results
            return results.ToDictionary(x => x.machine, x => x.count);
        }

    }
}
