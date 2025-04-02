using DashboardReportApp.Models;
using MySql.Data.MySqlClient;
using System.Text.Json;

namespace DashboardReportApp.Services
{
    public class MoldingService
    {
        private readonly string _connectionString;
        private readonly SharedService _sharedService;

        public MoldingService(IConfiguration configuration, SharedService sharedService)
        {
            _connectionString = configuration.GetConnectionString("MySQLConnection");
            _sharedService = sharedService;
        }


        // Now returns all data without applying filtering/sorting on the server
        public MoldingModel GetData()
        {
            var pressRuns = GetPressRuns();
            var pressSetups = GetPressSetups();
            var pressLotChanges = GetPressMixBagChanges();

            return new MoldingModel
            {
                PressRuns = pressRuns,
                PressSetups = pressSetups,
                PressLotChanges = pressLotChanges
            };
        }

        public List<PressRunLogModel> GetPressRuns()
        {
            return QueryTable<PressRunLogModel>("pressruntime");
        }

        public List<PressSetupModel> GetPressSetups()
        {
            return QueryTable<PressSetupModel>("presssetuptime");
        }

        private List<PressMixBagChangeModel> GetPressMixBagChanges()
        {
            return QueryTable<PressMixBagChangeModel>("pressmixbagchange");
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
                int? count = await TryGetDeviceCountOrNull(machine);
                return new { machine, count };
            });

            // Wait for all tasks to complete
            var results = await Task.WhenAll(tasks);

            // Build and return the dictionary from the results
            return results.ToDictionary(x => x.machine, x => x.count);
        }

        private string MapMachineToIp(string machine)
        {
            return _sharedService.GetDeviceIp(machine);
        }

        public async Task<int?> TryGetDeviceCountOrNull(string machine)
        {
            string deviceIp;
            try
            {
                deviceIp = MapMachineToIp(machine);
            }
            catch
            {
                return null;
            }

            try
            {
                // Configure a 5-second timeout (adjust as needed)
                using var httpClient = new HttpClient
                {
                    Timeout = TimeSpan.FromSeconds(5)
                };

                string url = $"http://{deviceIp}/api/picodata";
                HttpResponseMessage response = await httpClient.GetAsync(url);
                response.EnsureSuccessStatusCode();

                string json = (await response.Content.ReadAsStringAsync()).Trim();
                Console.WriteLine("Device JSON: " + json);

                // Attempt to parse "count_value" from a JSON object
                if (json.StartsWith("{"))
                {
                    var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                    using var doc = JsonDocument.Parse(json);
                    JsonElement root = doc.RootElement;
                    if (root.TryGetProperty("count_value", out JsonElement countElement))
                    {
                        if (countElement.ValueKind == JsonValueKind.Number && countElement.TryGetInt32(out int deviceCount))
                        {
                            return deviceCount;
                        }
                        else if (countElement.ValueKind == JsonValueKind.String)
                        {
                            string countStr = countElement.GetString();
                            if (int.TryParse(countStr, out int parsedCount))
                            {
                                return parsedCount;
                            }
                        }
                    }
                }

                // Fallback: check if JSON is just a plain integer
                if (int.TryParse(json, out int plainCount))
                {
                    return plainCount;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("Error in TryGetDeviceCountOrNull: " + ex.Message);
            }
            return null;
        }

    }
}
