using DashboardReportApp.Models;
using MySql.Data.MySqlClient;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;

namespace DashboardReportApp.Services
{
    public class MoldingService
    {
        private readonly string _connectionString;
        private readonly SharedService _sharedService;
        private readonly ProlinkService _prolinkService;

        public MoldingService(IConfiguration configuration, SharedService sharedService, ProlinkService prolinkService)
        {
            _connectionString = configuration.GetConnectionString("MySQLConnection");
            _sharedService = sharedService;
            _prolinkService = prolinkService;
        }

        // Existing methods...
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
                                string matchingColumn = columnNames
                                    .FirstOrDefault(c => c.Equals(prop.Name.ToLower(), StringComparison.OrdinalIgnoreCase));

                                if (matchingColumn != null)
                                {
                                    int ordinal = reader.GetOrdinal(matchingColumn);
                                    if (!reader.IsDBNull(ordinal))
                                    {
                                        object value = reader[ordinal];
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

        public async Task<Dictionary<string, int?>> GetAllMachineCountsAsync()
        {
            var machineList = new List<string> {
                "1", "2", "41", "45", "50", "51", "57", "59", "70", "74",
                "92", "95", "102", "112", "124", "125", "154", "156", "175"
            };

            var tasks = machineList.Select(async machine =>
            {
                int? count = await TryGetDeviceCountOrNull(machine);
                return new { machine, count };
            });

            var results = await Task.WhenAll(tasks);
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
                using var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
                string url = $"http://{deviceIp}/api/picodata";
                HttpResponseMessage response = await httpClient.GetAsync(url);
                response.EnsureSuccessStatusCode();

                string json = (await response.Content.ReadAsStringAsync()).Trim();
                Console.WriteLine("Device JSON: " + json);

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

        #region Inspection Data for Molding Dashboard

        // New helper classes for inspection data.
        public class InspectionDataPoint
        {
            public DateTime X { get; set; }
            public double Y { get; set; }
        }

        public class InspectionDataset
        {
            public string Dimension { get; set; }
            public string Run { get; set; }
            public string MixNumber { get; set; }
            public List<InspectionDataPoint> DataPoints { get; set; } = new List<InspectionDataPoint>();
        }

        public class InspectionDataResult
        {
            public List<InspectionDataset> Datasets { get; set; } = new List<InspectionDataset>();
        }

        // Helper class to hold pressrun data for inspection.
        private class PressRunInspection
        {
            public DateTime StartDateTime { get; set; }
            public DateTime EndDateTime { get; set; }
            public string Run { get; set; }
            public string MixNumber { get; set; }
        }

        // New method that gathers inspection data:
        // 1. Query pressrun for start/end times and run (filtered by part number).
        // 2. For each run, join pressmixbagchange on run to get mixNumber.
        // 3. Get measurement records via ProlinkService for the given part.
        // 4. Filter measurements by the run’s time window and group them by dimension.
        public InspectionDataResult GetInspectionData(string partNumber)
        {
            var result = new InspectionDataResult();
            var runs = new List<PressRunInspection>();

            using (var connection = new MySqlConnection(_connectionString))
            {
                connection.Open();
                // Query pressrun table for the given part.
                string query = "SELECT startDateTime, endDateTime, run FROM pressrun WHERE part = @partNumber";
                using (var command = new MySqlCommand(query, connection))
                {
                    command.Parameters.AddWithValue("@partNumber", partNumber);
                    using (var reader = command.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            runs.Add(new PressRunInspection
                            {
                                StartDateTime = reader.GetDateTime(reader.GetOrdinal("startDateTime")),
                                EndDateTime = reader.GetDateTime(reader.GetOrdinal("endDateTime")),
                                Run = reader.GetString(reader.GetOrdinal("run"))
                            });
                        }
                    }
                }
                // For each pressrun, get mixNumber from pressmixbagchange.
                foreach (var runRec in runs)
                {
                    string mixQuery = "SELECT mixNumber FROM pressmixbagchange WHERE run = @run LIMIT 1";
                    using (var command = new MySqlCommand(mixQuery, connection))
                    {
                        command.Parameters.AddWithValue("@run", runRec.Run);
                        object mixObj = command.ExecuteScalar();
                        runRec.MixNumber = mixObj?.ToString() ?? "N/A";
                    }
                }
            }

            // Get measurement records via ProlinkService for the given part (for molding).
            var measurements = _prolinkService.GetMeasurementRecords(partNumber, "mold", null, null);

            // For each pressrun, filter measurements within the run's window and group by dimension.
            foreach (var runRec in runs)
            {
                var measurementsForRun = measurements
                    .Where(m => m.MeasureDate >= runRec.StartDateTime && m.MeasureDate <= runRec.EndDateTime)
                    .ToList();

                var groups = measurementsForRun.GroupBy(m => m.Dimension);
                foreach (var group in groups)
                {
                    var dataset = new InspectionDataset
                    {
                        Dimension = group.Key,
                        Run = runRec.Run,
                        MixNumber = runRec.MixNumber,
                        DataPoints = group.Select(m =>
                        {
                            double yVal = 0;
                            double.TryParse(m.MeasurementValue, NumberStyles.Any, CultureInfo.InvariantCulture, out yVal);
                            return new InspectionDataPoint
                            {
                                X = m.MeasureDate,
                                Y = yVal
                            };
                        }).OrderBy(dp => dp.X).ToList()
                    };
                    result.Datasets.Add(dataset);
                }
            }

            return result;
        }
        public async Task<object> GetMachineStatusAsync(string machine)
        {
            using var conn = new MySqlConnection(_connectionString);
            await conn.OpenAsync();

            // 1. Running
            using (var runCmd = new MySqlCommand(@"
        SELECT part, component, operator FROM pressrun 
        WHERE machine = @machine AND endDateTime IS NULL AND skidNumber > 0 
        ORDER BY id DESC LIMIT 1", conn))
            {
                runCmd.Parameters.AddWithValue("@machine", machine);
                using var reader = await runCmd.ExecuteReaderAsync();
                if (await reader.ReadAsync())
                {
                    string part = reader["part"]?.ToString();
                    string component = reader["component"]?.ToString();
                    string operatorName = reader["operator"]?.ToString();

                    return new
                    {
                        status = "running",
                        part = part,
                        component = component,
                        operatorName = operatorName
                    };
                }
            }

            // 2. Previously setup
            using (var availCmd = new MySqlCommand(@"
        SELECT part, component FROM presssetup 
        WHERE machine = @machine AND `open` = 1 
        ORDER BY id DESC LIMIT 1", conn))
            {
                availCmd.Parameters.AddWithValue("@machine", machine);
                using var reader = await availCmd.ExecuteReaderAsync();
                if (await reader.ReadAsync())
                {
                    string part = reader["part"]?.ToString();
                    string component = reader["component"]?.ToString();

                    return new
                    {
                        status = "available",
                        part = part,
                        component = component
                    };
                }
            }

            // 3. Setting up
            using (var setupCmd = new MySqlCommand(@"
        SELECT part, component FROM presssetup 
        WHERE machine = @machine AND endDateTime IS NULL 
        ORDER BY id DESC LIMIT 1", conn))
            {
                setupCmd.Parameters.AddWithValue("@machine", machine);
                using var reader = await setupCmd.ExecuteReaderAsync();
                if (await reader.ReadAsync())
                {
                    string part = reader["part"]?.ToString();
                    string component = reader["component"]?.ToString();

                    return new
                    {
                        status = "setup",
                        part = part,
                        component = component
                    };
                }
            }

            return new { status = "idle" };
        }

        public async Task<int?> GetScheduledQuantityForMachine(string machine)
        {
            using var conn = new MySqlConnection(_connectionString);
            await conn.OpenAsync();

            // 1. Try to find open pressrun
            string runQuery = @"
        SELECT s.quantity
        FROM schedule s
        JOIN pressrun r ON s.run = r.run
        WHERE r.machine = @machine AND r.endDateTime IS NULL
        ORDER BY r.id DESC LIMIT 1";

            using (var runCmd = new MySqlCommand(runQuery, conn))
            {
                runCmd.Parameters.AddWithValue("@machine", machine);
                var result = await runCmd.ExecuteScalarAsync();
                if (result != null && int.TryParse(result.ToString(), out int qty))
                    return qty;
            }

            // 2. Fallback: try presssetup
            string setupQuery = @"
        SELECT s.quantity
        FROM schedule s
        JOIN presssetup p ON s.run = p.run
        WHERE p.machine = @machine AND p.endDateTime IS NULL
        ORDER BY p.id DESC LIMIT 1";

            using (var setupCmd = new MySqlCommand(setupQuery, conn))
            {
                setupCmd.Parameters.AddWithValue("@machine", machine);
                var result = await setupCmd.ExecuteScalarAsync();
                if (result != null && int.TryParse(result.ToString(), out int qty))
                    return qty;
            }

            return null;
        }


        #endregion
    }
}
