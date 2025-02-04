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

        public MoldingModel GetFilteredData(string searchTerm, string sortColumn, bool sortDescending)
        {
            var pressRuns = GetPressRuns(searchTerm);
            var pressSetups = GetPressSetups(searchTerm);
            var pressLotChanges = GetPressLotChanges(searchTerm);

            // Apply sorting if a sort column is selected
            if (!string.IsNullOrEmpty(sortColumn))
            {
                pressRuns = SortData(pressRuns, sortColumn, sortDescending).ToList();
                pressSetups = SortData(pressSetups, sortColumn, sortDescending).ToList();
                pressLotChanges = SortData(pressLotChanges, sortColumn, sortDescending).ToList();
            }

            return new MoldingModel
            {
                PressRuns = pressRuns,
                PressSetups = pressSetups,
                PressLotChanges = pressLotChanges,
                SearchTerm = searchTerm,
                SortColumn = sortColumn,
                SortDescending = sortDescending
            };
        }
        private IEnumerable<T> SortData<T>(IEnumerable<T> data, string sortColumn, bool sortDescending)
        {
            var prop = typeof(T).GetProperty(sortColumn);
            if (prop == null) return data;

            return sortDescending ? data.OrderByDescending(x => prop.GetValue(x)) : data.OrderBy(x => prop.GetValue(x));
        }

        private List<PressRunLogFormModel> GetPressRuns(string searchTerm)
        {
            return QueryTable<PressRunLogFormModel>("pressrun", searchTerm);
        }

        private List<PressSetupModel> GetPressSetups(string searchTerm)
        {
            return QueryTable<PressSetupModel>("presssetup", searchTerm);
        }

        private List<PressMixBagChangeModel> GetPressLotChanges(string searchTerm)
        {
            return QueryTable<PressMixBagChangeModel>("presslotchange", searchTerm);
        }

        private List<T> QueryTable<T>(string tableName, string searchTerm) where T : new()
        {
            var results = new List<T>();
            bool isDateSearch = DateTime.TryParse(searchTerm, out DateTime searchDate);

            // Default query (adds "notes" column to search)
            string query = $"SELECT * FROM {tableName} WHERE operator LIKE @Search OR part LIKE @Search OR machine LIKE @Search OR notes LIKE @Search";

            // Handling Date Searches
            if (isDateSearch)
            {
                if (tableName == "pressrun" || tableName == "presssetup")
                {
                    query += " OR (startDateTime >= @SearchDate AND startDateTime < @SearchDateEnd) " +
                             " OR (endDateTime >= @SearchDate AND endDateTime < @SearchDateEnd)";
                }
                else if (tableName == "presslotchange")
                {
                    query += " OR (sentDateTime >= @SearchDate AND sentDateTime < @SearchDateEnd)";
                }
            }

            using (var connection = new MySqlConnection(_connectionString))
            {
                connection.Open();
                using (var command = new MySqlCommand(query, connection))
                {
                    command.Parameters.AddWithValue("@Search", $"%{searchTerm}%");

                    if (isDateSearch)
                    {
                        command.Parameters.AddWithValue("@SearchDate", searchDate);
                        command.Parameters.AddWithValue("@SearchDateEnd", searchDate.AddDays(1)); // Search full day
                    }

                    using (var reader = command.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            var obj = Activator.CreateInstance<T>();
                            foreach (var prop in typeof(T).GetProperties())
                            {
                                if (!reader.IsDBNull(reader.GetOrdinal(prop.Name)))
                                {
                                    object value = reader[prop.Name];

                                    if (value is long && prop.PropertyType == typeof(int))
                                    {
                                        value = Convert.ToInt32(value);
                                    }

                                    prop.SetValue(obj, value);
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
