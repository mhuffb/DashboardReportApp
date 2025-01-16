using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using MySql.Data.MySqlClient;
using System;
using System.Threading.Tasks;

namespace DashboardReportApp.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class PicoDataController : ControllerBase
    {
        private readonly ILogger<PicoDataController> _logger;

        public PicoDataController(ILogger<PicoDataController> logger)
        {
            _logger = logger;
        }

        // Handle PUT requests to /api/picodata
        [HttpPut]
        public async Task<IActionResult> HandlePut([FromBody] PicoDataModel data)
        {
            if (data == null || string.IsNullOrWhiteSpace(data.PressValue) || string.IsNullOrWhiteSpace(data.CountValue))
            {
                _logger.LogError("Invalid data format received: {Data}", data);
                return BadRequest(new { error = "Invalid data format. Expected JSON with 'press_value' and 'count_value'." });
            }

            try
            {
                // MySQL connection string
                string connectionString = "Server=192.168.1.6;Database=sintergy;Uid=admin;Pwd=N0mad2019;";
                await using var connection = new MySqlConnection(connectionString);

                await connection.OpenAsync();
                _logger.LogInformation("Connected to MySQL database successfully.");

                // Insert data into the table
                string query = "INSERT INTO presscount (press, count) VALUES (@press, @count)";
                await using var command = new MySqlCommand(query, connection);
                command.Parameters.AddWithValue("@press", data.PressValue);
                command.Parameters.AddWithValue("@count", data.CountValue);

                await command.ExecuteNonQueryAsync();
                _logger.LogInformation("Data inserted successfully: press={Press}, count={Count}", data.PressValue, data.CountValue);

                return Ok(new { message = "Data inserted successfully." });
            }
            catch (MySqlException ex)
            {
                _logger.LogError("Error inserting data into database: {Error}", ex.Message);
                return StatusCode(500, new { error = "Database error occurred." });
            }
            catch (Exception ex)
            {
                _logger.LogError("Unexpected error occurred: {Error}", ex.Message);
                return StatusCode(500, new { error = "Internal server error." });
            }
        }

        // Handle GET requests to /api/picodata/press
        [HttpGet("press")]
        public IActionResult GetPressLink()
        {
            string responseHtml = "<a href=\"http://192.168.1.254\">Press 124</a>";
            return Content(responseHtml, "text/html");
        }
    }

    // Model for the incoming data
    public class PicoDataModel
    {
        public string PressValue { get; set; }
        public string CountValue { get; set; }
    }
}
