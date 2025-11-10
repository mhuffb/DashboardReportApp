using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Threading.Tasks;

using DashboardReportApp.Models;

using MailKit.Net.Smtp;
using MailKit.Security;

using Microsoft.Extensions.Options;

using MimeKit;

using MySql.Data.MySqlClient;

// iText 7
using iText.Kernel.Pdf;
using iText.Layout;
using iText.Layout.Element;
using iText.Layout.Properties;

namespace DashboardReportApp.Services
{
    public class MaintenanceRequestService
    {
        private readonly string _connectionString;
        private readonly PathOptions _paths;
        private readonly EmailOptions _email;
        private readonly PrinterOptions _printers;
        private readonly SharedService _sharedService;
        public PathOptions Paths => _paths;

        public MaintenanceRequestService(
            IConfiguration configuration,
            IOptions<PathOptions> paths,
            IOptions<EmailOptions> email,
            IOptions<PrinterOptions> printers,
            SharedService sharedService)
        {
            _connectionString = configuration.GetConnectionString("MySQLConnection");
            _paths = paths.Value;
            _email = email.Value;
            _printers = printers.Value;
            _sharedService = sharedService;
        }

        public async Task<int> AddRequestAsync(MaintenanceRequestModel request)
        {
            const string query = @"
INSERT INTO maintenance (
    equipment, requester, reqDate, problem, downStatus, hourMeter, 
    FileAddress1, department, downStartDateTime, status, SafetyConcern
) VALUES (
    @equipment, @requester, @reqDate, @problem, @downStatus, @hourMeter, 
    @FileAddress1, @department, @downStartDateTime, @status, @SafetyConcern
);
SELECT LAST_INSERT_ID();";

            using var connection = new MySqlConnection(_connectionString);
            await connection.OpenAsync();

            using var command = new MySqlCommand(query, connection);
            command.Parameters.AddWithValue("@equipment", request.Equipment);
            command.Parameters.AddWithValue("@requester", request.Requester);
            command.Parameters.AddWithValue("@reqDate", request.RequestedDate ?? DateTime.Now);
            command.Parameters.AddWithValue("@problem", request.Problem);
            command.Parameters.AddWithValue("@downStatus", (bool)request.DownStatus ? 1 : 0);
            command.Parameters.AddWithValue("@hourMeter", request.HourMeter ?? (object)DBNull.Value);
            // store filename only
            command.Parameters.AddWithValue("@FileAddress1", string.IsNullOrWhiteSpace(request.FileAddress1) ? (object)DBNull.Value : request.FileAddress1);
            command.Parameters.AddWithValue("@department", request.Department ?? (object)DBNull.Value);
            command.Parameters.AddWithValue("@downStartDateTime", request.DownStartDateTime ?? (object)DBNull.Value);
            command.Parameters.AddWithValue("@status", request.Status);
            command.Parameters.AddWithValue("@SafetyConcern", request.SafetyConcern);

            var result = await command.ExecuteScalarAsync();
            if (result == null || !int.TryParse(result.ToString(), out var insertedId))
                throw new InvalidOperationException("Failed to retrieve the inserted ID.");

            request.Id = insertedId;

            // Generate + email + print PDF
            var pdfPath = GeneratePdf(request);
            await SendEmailWithPdfAsync(pdfPath, request);

            // Use configured printer name
            _sharedService.PrintFileToSpecificPrinter(_printers.Maintenance, pdfPath, 1);

            return request.Id;
        }

        public string GeneratePdf(MaintenanceRequestModel request)
        {
            var inputFilePath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "workorderinput.pdf");
            if (!File.Exists(inputFilePath))
                throw new FileNotFoundException("The PDF template file was not found.", inputFilePath);

            Directory.CreateDirectory(_paths.MaintenanceExports);
            var outputFilePath = Path.Combine(_paths.MaintenanceExports, $"MaintenanceRequest_{request.Id}.pdf");

            using var writer = new PdfWriter(outputFilePath);
            using var pdfDoc = new PdfDocument(new PdfReader(inputFilePath), writer);
            using var document = new Document(pdfDoc);

            document.Add(new Paragraph(DateTime.Now.ToString()).SetFixedPosition(75, 615, 200));
            document.Add(new Paragraph(request.Equipment).SetFixedPosition(135, 575, 900));

            var problem = request.Problem ?? "";
            if (problem.Length < 180)
            {
                document.Add(new Paragraph(problem)
                    .SetTextAlignment(TextAlignment.CENTER)
                    .SetVerticalAlignment(VerticalAlignment.MIDDLE)
                    .SetFixedPosition(35, 425, 560));
            }
            else
            {
                document.Add(new Paragraph(problem)
                    .SetTextAlignment(TextAlignment.CENTER)
                    .SetVerticalAlignment(VerticalAlignment.MIDDLE)
                    .SetFixedPosition(35, 390, 560));
            }

            document.Add(new Paragraph($"ID {request.Id}").SetFixedPosition(275, 660, 200));
            document.Add(new Paragraph(request.RequestedDate?.ToShortDateString() ?? DateTime.Now.ToShortDateString())
                .SetFixedPosition(400, 500, 200));
            document.Add(new Paragraph(request.Requester).SetFixedPosition(400, 560, 200));

            if (!string.IsNullOrEmpty(request.Department))
                document.Add(new Paragraph(request.Department).SetFixedPosition(250, 500, 200));

            if (!string.IsNullOrEmpty(request.FileAddress1))
                document.Add(new Paragraph("Image Attached").SetFixedPosition(450, 460, 200));
            else
                document.Add(new Paragraph("No Image Attached").SetFixedPosition(450, 460, 200));

            return outputFilePath;
        }

        private async Task SendEmailWithPdfAsync(string pdfPath, MaintenanceRequestModel request)
        {
            var recipients = ResolveRecipientAddresses(request);

            try
            {
               
                // send to normal recipients 
                await SendIndividualEmailAsync(pdfPath, recipients, request);

               
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error sending email: {ex.Message}");
            }
        }

       

        private async Task SendIndividualEmailAsync(
            string pdfPath,
            IEnumerable<string> recipients,
            MaintenanceRequestModel request)
        {
            var email = new MimeMessage();
            email.From.Add(new MailboxAddress(_email.FromName, _email.FromAddress));

            foreach (var r in recipients)
                email.To.Add(new MailboxAddress(r, r));

            email.Subject = $"New Maintenance Request: {request.Id}";

            var eq = await GetEquipmentDetailsAsync(request.Equipment);
            string equipmentBlock = eq is null
                ? "Equipment Details: (no match found)"
                : $@"Equipment Details:
  Dept: {eq.Value.Department}
  Name: {eq.Value.Name}
  Brand: {eq.Value.Brand}
  Model: {eq.Value.Model}
  Serial: {eq.Value.Serial}
  Description: {eq.Value.Description}";

            var builder = new BodyBuilder
            {
                TextBody = $@"A new maintenance request has been created.

Requester: {request.Requester}
Equipment: {request.Equipment}
Problem: {request.Problem}

{equipmentBlock}"
            };

            if (!string.IsNullOrWhiteSpace(pdfPath) && File.Exists(pdfPath))
                builder.Attachments.Add(pdfPath);

            // Attach uploaded image if filename exists (we reconstruct full path)
            if (!string.IsNullOrWhiteSpace(request.FileAddress1))
            {
                var fullUpload = Path.Combine(_paths.MaintenanceUploads, request.FileAddress1);
                if (File.Exists(fullUpload))
                    builder.Attachments.Add(fullUpload);
            }

            email.Body = builder.ToMessageBody();

            using var smtp = new MailKit.Net.Smtp.SmtpClient();
            smtp.ServerCertificateValidationCallback = (sender, certificate, chain, sslPolicyErrors) => true;

            try
            {
                if (_email.UseStartTls)
                    await smtp.ConnectAsync(_email.SmtpHost, _email.SmtpPort, SecureSocketOptions.StartTls);
                else if (_email.UseSsl)
                    await smtp.ConnectAsync(_email.SmtpHost, _email.SmtpPort, SecureSocketOptions.SslOnConnect);
                else
                    await smtp.ConnectAsync(_email.SmtpHost, _email.SmtpPort, SecureSocketOptions.None);

                if (!string.IsNullOrWhiteSpace(_email.Username))
                    await smtp.AuthenticateAsync(_email.Username, _email.Password);

                await smtp.SendAsync(email);
            }
            finally
            {
                await smtp.DisconnectAsync(true);
            }
        }

        public async Task<List<string>> GetRequestersAsync()
        {
            var requesters = new List<string>();
            const string query = "SELECT DISTINCT name FROM operators ORDER BY name";

            using var connection = new MySqlConnection(_connectionString);
            await connection.OpenAsync();

            using var command = new MySqlCommand(query, connection);
            using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                requesters.Add(reader.GetString("name"));
            }

            return requesters;
        }

        public async Task<List<string>> GetEquipmentListAsync()
        {
            var equipmentList = new List<string>();
            const string query = @"
SELECT equipment, name, brand, description 
FROM equipment 
WHERE status IS NULL OR status != 'obsolete' 
ORDER BY equipment";

            using var connection = new MySqlConnection(_connectionString);
            await connection.OpenAsync();

            using var command = new MySqlCommand(query, connection);
            using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var equipment = reader["equipment"].ToString();
                var name = reader["name"] == DBNull.Value ? "N/A" : reader["name"].ToString();
                var brand = reader["brand"] == DBNull.Value ? "N/A" : reader["brand"].ToString();
                var description = reader["description"] == DBNull.Value ? "N/A" : reader["description"].ToString();

                equipmentList.Add($"{equipment} - {name} (Brand: {brand}, Description: {description})");
            }

            return equipmentList;
        }

        public List<MaintenanceRequestModel> GetAllRequests()
        {
            var requests = new List<MaintenanceRequestModel>();
            const string query = "SELECT * FROM maintenance ORDER BY id DESC";

            using var connection = new MySqlConnection(_connectionString);
            connection.Open();

            using var command = new MySqlCommand(query, connection);
            using var reader = command.ExecuteReader();
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
                    DownStatus = !reader.IsDBNull(reader.GetOrdinal("downStatus")) && reader.GetBoolean("downStatus"),
                    HourMeter = !reader.IsDBNull(reader.GetOrdinal("hourMeter")) ? reader.GetDecimal("hourMeter") : (decimal?)null,
                    HoldStatus = reader["HoldStatus"] == DBNull.Value ? null : Convert.ToBoolean(reader["HoldStatus"]),
                    HoldReason = reader["HoldReason"]?.ToString(),
                    HoldResult = reader["HoldResult"]?.ToString(),
                    HoldBy = reader["HoldBy"]?.ToString(),
                    FileAddress = reader["FileAddress"]?.ToString(),
                    FileAddress1 = reader["FileAddress1"] == DBNull.Value ? null : reader["FileAddress1"].ToString(), // filename
                    FileAddress2 = reader["FileAddress2"] == DBNull.Value ? null : reader["FileAddress2"].ToString(),
                    StatusHistory = reader["StatusHistory"]?.ToString(),
                    CurrentStatusBy = reader["CurrentStatusBy"]?.ToString(),
                    Department = reader["Department"]?.ToString(),
                    Status = reader["Status"]?.ToString(),
                    StatusDesc = reader["StatusDesc"]?.ToString(),
                    SafetyConcern = !reader.IsDBNull(reader.GetOrdinal("SafetyConcern")) && Convert.ToBoolean(reader["SafetyConcern"])
                });
            }

            return requests;
        }

        public async Task<string> UpdateFile1Link(int id, string fileName)
        {
            using var connection = new MySqlConnection(_connectionString);
            const string sql = "UPDATE maintenance SET FileAddress1 = @FileAddress1 WHERE Id = @id";

            await connection.OpenAsync();

            using var command = new MySqlCommand(sql, connection);
            command.Parameters.AddWithValue("@FileAddress1", string.IsNullOrEmpty(fileName) ? DBNull.Value : (object)fileName);
            command.Parameters.AddWithValue("@id", id);

            await command.ExecuteNonQueryAsync();
            return fileName;
        }

        public bool UpdateRequest(MaintenanceRequestModel model, Microsoft.AspNetCore.Http.IFormFile? file)
        {
            const string query = @"
UPDATE maintenance 
SET 
    Timestamp = @Timestamp, 
    Equipment = @Equipment, 
    Requester = @Requester, 
    Problem = @Problem, 
    DownStatus = @DownStatus, 
    HourMeter = @HourMeter, 
    Department = @Department
WHERE Id = @Id";

            using var connection = new MySqlConnection(_connectionString);
            connection.Open();

            using var command = new MySqlCommand(query, connection);
            command.Parameters.AddWithValue("@Id", model.Id);
            command.Parameters.AddWithValue("@Timestamp", model.Timestamp ?? (object)DBNull.Value);
            command.Parameters.AddWithValue("@Equipment", model.Equipment ?? (object)DBNull.Value);
            command.Parameters.AddWithValue("@Requester", model.Requester ?? (object)DBNull.Value);
            command.Parameters.AddWithValue("@Problem", model.Problem ?? (object)DBNull.Value);
            command.Parameters.AddWithValue("@DownStatus", model.DownStatus ?? (object)DBNull.Value);
            command.Parameters.AddWithValue("@HourMeter", model.HourMeter ?? (object)DBNull.Value);
            command.Parameters.AddWithValue("@Department", model.Department ?? (object)DBNull.Value);

            var rowsAffected = command.ExecuteNonQuery();
            return rowsAffected > 0;
        }

        public async Task<int> GetOpenMaintenanceCount(string equipment)
        {
            const string query = "SELECT COUNT(*) FROM maintenance WHERE equipment = @equipment AND LOWER(status) = 'open'";

            using var connection = new MySqlConnection(_connectionString);
            await connection.OpenAsync();

            using var command = new MySqlCommand(query, connection);
            command.Parameters.AddWithValue("@equipment", equipment);

            var count = Convert.ToInt32(await command.ExecuteScalarAsync());
            return count;
        }

        public async Task<List<MaintenanceRequestModel>> GetAllOpenRequestsAsync()
        {
            var requests = new List<MaintenanceRequestModel>();
            const string query = "SELECT * FROM maintenance WHERE LOWER(status) = 'open'";

            using var connection = new MySqlConnection(_connectionString);
            await connection.OpenAsync();

            using var command = new MySqlCommand(query, connection);
            using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                requests.Add(new MaintenanceRequestModel
                {
                    Id = Convert.ToInt32(reader["id"]),
                    Equipment = reader["Equipment"]?.ToString(),
                    Requester = reader["Requester"]?.ToString(),
                    RequestedDate = reader["ReqDate"] == DBNull.Value ? null : Convert.ToDateTime(reader["ReqDate"]),
                    Problem = reader["Problem"]?.ToString(),
                    SafetyConcern = !reader.IsDBNull(reader.GetOrdinal("SafetyConcern")) && Convert.ToBoolean(reader["SafetyConcern"])
                });
            }

            return requests;
        }

        private async Task<(string Department, string Name, string Brand, string Description, string Model, string Serial)?> GetEquipmentDetailsAsync(string equipmentNo)
        {
            if (string.IsNullOrWhiteSpace(equipmentNo)) return null;

            const string sql = @"
SELECT department, name, brand, description, model, serial
FROM equipment
WHERE equipment = @equipment
LIMIT 1;";

            using var conn = new MySqlConnection(_connectionString);
            await conn.OpenAsync();

            using var cmd = new MySqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@equipment", equipmentNo);

            using var reader = await cmd.ExecuteReaderAsync(CommandBehavior.SingleRow);
            if (await reader.ReadAsync())
            {
                string GetStr(string col) => reader[col] == DBNull.Value ? "" : reader[col].ToString();

                return (
                    Department: GetStr("department"),
                    Name: GetStr("name"),
                    Brand: GetStr("brand"),
                    Description: GetStr("description"),
                    Model: GetStr("model"),
                    Serial: GetStr("serial")
                );
            }

            return null;
        }

        private IEnumerable<string> ResolveRecipientAddresses(MaintenanceRequestModel request)
        {
            // If OverrideAllTo is configured, always use it (dev mode)
            if (!string.IsNullOrWhiteSpace(_email.OverrideAllTo))
            {
                return _email.OverrideAllTo
                    .Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            }

            // Otherwise, route to department@DefaultRecipientDomain (prod mode)
            var dept = string.IsNullOrWhiteSpace(request.Department) ? "maintenance" : request.Department;
            return new[] { $"{dept}@{_email.DefaultRecipientDomain}" };
        }
        // in MaintenanceRequestService
        public bool UpdateRequestAdmin(MaintenanceRequestModel model)
        {
            const string query = @"
        UPDATE maintenance 
        SET 
            Equipment = @Equipment,
            Requester = @Requester,
            ReqDate = @RequestedDate,
            Problem = @Problem,
            ClosedDateTime = @ClosedDateTime,
            HourMeter = @HourMeter,
            FileAddress1 = @FileAddress1,
            FileAddress2 = @FileAddress2,
            Department = @Department,
            StatusDesc = @StatusDesc, 
            Status = @Status,
            SafetyConcern = @SafetyConcern
        WHERE Id = @Id";

            using var connection = new MySqlConnection(_connectionString);
            connection.Open();

            using var command = new MySqlCommand(query, connection);
            command.Parameters.AddWithValue("@Id", model.Id);
            command.Parameters.AddWithValue("@Equipment", model.Equipment);
            command.Parameters.AddWithValue("@Requester", model.Requester);
            command.Parameters.AddWithValue("@RequestedDate", model.RequestedDate ?? (object)DBNull.Value);
            command.Parameters.AddWithValue("@Problem", model.Problem);
            command.Parameters.AddWithValue("@ClosedDateTime", model.ClosedDateTime ?? (object)DBNull.Value);
            command.Parameters.AddWithValue("@FileAddress1", model.FileAddress1 ?? (object)DBNull.Value);
            command.Parameters.AddWithValue("@FileAddress2", model.FileAddress2 ?? (object)DBNull.Value);
            command.Parameters.AddWithValue("@Department", model.Department ?? (object)DBNull.Value);
            command.Parameters.AddWithValue("@StatusDesc", model.StatusDesc);
            command.Parameters.AddWithValue("@Status", model.Status);
            command.Parameters.AddWithValue("@HourMeter", model.HourMeter ?? (object)DBNull.Value);
            command.Parameters.AddWithValue("@SafetyConcern", model.SafetyConcern);

            return command.ExecuteNonQuery() > 0;
        }

    }
}
