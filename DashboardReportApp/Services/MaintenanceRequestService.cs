namespace DashboardReportApp.Services
{
    using MySql.Data.MySqlClient;
    using Microsoft.Extensions.Configuration;
    using System;
    using System.Collections.Generic;
    using System.Threading.Tasks;
    using DashboardReportApp.Models;
    using System.Data;
    using System.Drawing.Imaging;
    using System.Drawing.Printing;
    using System.Reflection.Metadata;
    using iText.Kernel.Pdf;
    using iText.Layout;
    using iText.Layout.Element;
    using iText.Layout.Properties;
    using static Microsoft.EntityFrameworkCore.DbLoggerCategory.Database;
    using Microsoft.AspNetCore.Hosting;

    public class MaintenanceRequestService
    {
        private readonly string _connectionString;
        private readonly IWebHostEnvironment _webHostEnvironment;
        private readonly string _uploadFolder;
        public MaintenanceRequestService(IConfiguration configuration, IWebHostEnvironment webHostEnvironment)
        {
            _connectionString = configuration.GetConnectionString("MySQLConnection");
            _webHostEnvironment = webHostEnvironment;
            _uploadFolder = @"\\SINTERGYDC2024\Vol1\Visual Studio Programs\VSP\Uploads";
        }

        public async Task<IEnumerable<MaintenanceRequestModel>> GetOpenRequestsAsync()
        {
            var requests = new List<MaintenanceRequestModel>();
            string query = @"SELECT id, timestamp, equipment, requester, problem, downStatus, hourMeter, fileAddressImageLink 
                     FROM maintenance 
                     WHERE closedDateTime IS NULL";

            using (var connection = new MySqlConnection(_connectionString))
            {
                await connection.OpenAsync();

                using (var command = new MySqlCommand(query, connection))
                using (var reader = await command.ExecuteReaderAsync())
                {
                    while (await reader.ReadAsync())
                    {
                        var request = new MaintenanceRequestModel
                        {
                            Id = reader.GetInt32("id"),
                            Timestamp = reader.GetDateTime("timestamp"),
                            Equipment = reader.GetString("equipment"),
                            Requester = reader.GetString("requester"),
                            Problem = reader.GetString("problem"),
                            DownStatus = !reader.IsDBNull(reader.GetOrdinal("downStatus")) && reader.GetBoolean("downStatus"),
                            HourMeter = !reader.IsDBNull(reader.GetOrdinal("hourMeter")) ? reader.GetInt32("hourMeter") : (int?)null,
                            FileAddressMediaLink = !reader.IsDBNull(reader.GetOrdinal("fileAddressImageLink")) ? reader.GetString("fileAddressImageLink") : null,
                        };

                        requests.Add(request);
                    }
                }
            }

            return requests;
        }


        public async Task<bool> AddRequestAsync(MaintenanceRequestModel request)
        {
            string query = @"INSERT INTO maintenance (equipment, requester, reqDate, problem, downStatus, hourMeter, fileAddressImageLink, department) 
                     VALUES (@equipment, @requester, @reqDate, @problem, @downStatus, @hourMeter, @fileAddress, @department);
                     SELECT LAST_INSERT_ID();"; // Retrieve the last inserted ID

            using (var connection = new MySqlConnection(_connectionString))
            {
                await connection.OpenAsync();
                using (var command = new MySqlCommand(query, connection))
                {
                    command.Parameters.AddWithValue("@equipment", request.Equipment);
                    command.Parameters.AddWithValue("@requester", request.Requester);
                    command.Parameters.AddWithValue("@reqDate", request.RequestedDate ?? DateTime.Now);
                    command.Parameters.AddWithValue("@problem", request.Problem);
                    command.Parameters.AddWithValue("@downStatus", request.DownStatus ? 1 : 0);
                    command.Parameters.AddWithValue("@hourMeter", request.HourMeter ?? (object)DBNull.Value);
                    command.Parameters.AddWithValue("@fileAddress", request.FileAddressMediaLink ?? (object)DBNull.Value);
                    command.Parameters.AddWithValue("@department", request.Department ?? (object)DBNull.Value);

                    // Execute the query and retrieve the last inserted ID
                    object result = await command.ExecuteScalarAsync();
                    if (result != null && int.TryParse(result.ToString(), out int insertedId))
                    {
                        request.Id = insertedId; // Assign the generated ID to the request
                    }
                    else
                    {
                        throw new InvalidOperationException("Failed to retrieve the inserted ID.");
                    }

                    // Generate PDF for the request
                    string pdfPath = GeneratePdf(request);

                    // Email the PDF
                    await SendEmailWithPdfAsync(pdfPath, request);

                    return true;
                }
            }
        }


        public string GeneratePdf(MaintenanceRequestModel request)
        {
            // Path to the input PDF template in wwwroot
            string inputFilePath = Path.Combine(_webHostEnvironment.WebRootPath, "workorderinput.pdf");

            string outputFilePath = @$"\\SINTERGYDC2024\Vol1\VSP\Exports\MaintenanceRequest{request.Id}_{request.Equipment}.pdf";

            try
            {
                // Ensure the input template exists
                if (!File.Exists(inputFilePath))
                {
                    throw new FileNotFoundException("The PDF template file was not found.", inputFilePath);
                }

                using (PdfWriter writer = new PdfWriter(outputFilePath))
                {
                    using (PdfDocument pdfDoc = new PdfDocument(new PdfReader(inputFilePath), writer))
                    {
                        using (iText.Layout.Document document = new iText.Layout.Document(pdfDoc))
                        {
                            // Timestamp
                            document.Add(new Paragraph(DateTime.Now.ToString()).SetFixedPosition(75, 615, 200));

                            // Equipment
                            document.Add(new Paragraph(request.Equipment).SetFixedPosition(135, 575, 900));

                            // Problem
                            string problem = request.Problem;
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

                            // Request ID
                            document.Add(new Paragraph($"ID {request.Id}").SetFixedPosition(275, 660, 200));

                            // Request Date
                            document.Add(new Paragraph(request.RequestedDate?.ToShortDateString() ?? DateTime.Now.ToShortDateString())
                                .SetFixedPosition(400, 500, 200));

                            // Requester
                            document.Add(new Paragraph(request.Requester).SetFixedPosition(400, 560, 200));

                            // Department (if available)
                            if (!string.IsNullOrEmpty(request.Department))
                            {
                                document.Add(new Paragraph(request.Department).SetFixedPosition(250, 500, 200));
                            }

                            // Image Attached Note
                            if (!string.IsNullOrEmpty(request.FileAddressMediaLink))
                            {
                                document.Add(new Paragraph("Image Attached").SetFixedPosition(450, 460, 200));
                            }
                        }
                    }
                }

                return outputFilePath;
            }
            catch (Exception ex)
            {
                // Log the exception
                Console.WriteLine($"Error generating PDF: {ex.Message}");
                throw new ApplicationException("PDF generation failed.", ex);
            }
        }


        private async Task SendEmailWithPdfAsync(string pdfPath, MaintenanceRequestModel request)
        {
            // Define the mapping of departments to email recipients
            var departmentEmailRecipients = new Dictionary<string, List<string>>
    {
        { "Finishing", new List<string> { "ryoung@sintergy.net", "tgrieneisen@sintergy.net", "badamson@sintergy.net", "mzaffuto@sintergy.net", "rseltzer@sintergy.net" } },
        { "General", new List<string> { "ryoung@sintergy.net", "tgrieneisen@sintergy.net", "badamson@sintergy.net", "mzaffuto@sintergy.net", "jemery@sintergy.net", "rjones@sintergy.net" } },
        { "Maintenance", new List<string> { "ryoung@sintergy.net", "tgrieneisen@sintergy.net", "badamson@sintergy.net", "mzaffuto@sintergy.net", "jemery@sintergy.net", "rjones@sintergy.net" } },
        { "Molding", new List<string> { "ryoung@sintergy.net", "tgrieneisen@sintergy.net", "badamson@sintergy.net", "mzaffuto@sintergy.net", "jemery@sintergy.net", "bklebacha@sintergy.net" } },
        { "Packing", new List<string> { "ryoung@sintergy.net", "tgrieneisen@sintergy.net", "badamson@sintergy.net", "mzaffuto@sintergy.net", "shipping@sintergy.net", "dalmendarez@sintergy.net" } },
        { "Quality", new List<string> { "ryoung@sintergy.net", "tgrieneisen@sintergy.net", "dalmendarez@sintergy.net", "mhuff@sintergy.net", "jemery@sintergy.net" } },
        { "Secondary", new List<string> { "ryoung@sintergy.net", "tgrieneisen@sintergy.net", "badamson@sintergy.net", "mzaffuto@sintergy.net", "jkramer@sintergy.net" } },
        { "Sintering", new List<string> { "ryoung@sintergy.net", "tgrieneisen@sintergy.net", "badamson@sintergy.net", "mzaffuto@sintergy.net", "rseltzer@sintergy.net", "ameholick@sintergy.net" } },
        { "Tooling", new List<string> { "ryoung@sintergy.net", "tgrieneisen@sintergy.net", "badamson@sintergy.net", "mzaffuto@sintergy.net", "jemery@sintergy.net", "cschuckers@sintergy.net" } }
    };

            // Validate the department and get recipients
            if (!departmentEmailRecipients.ContainsKey(request.Department))
            {
                Console.WriteLine($"Invalid department: {request.Department}");
                return;
            }

            var recipients = departmentEmailRecipients[request.Department];

            // Add an additional recipient for all emails
            recipients.Add("mhuff@sintergy.net");

            // Send emails to all recipients
            foreach (var recipient in recipients)
            {
                try
                {
                    await SendIndividualEmailAsync(pdfPath, recipient, request);
                    Console.WriteLine($"Email sent to {recipient}");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error sending email to {recipient}: {ex.Message}");
                }
            }
        }

        private async Task SendIndividualEmailAsync(string pdfPath, string recipient, MaintenanceRequestModel request)
        {
            var email = new MimeKit.MimeMessage();
            email.From.Add(new MimeKit.MailboxAddress("Dashboard Report", "notifications@sintergy.net"));
            email.To.Add(new MimeKit.MailboxAddress(recipient, recipient));
            email.Subject = $"New Maintenance Request: {request.Id}";

            var builder = new MimeKit.BodyBuilder
            {
                TextBody = $@"A new maintenance request has been created.

Requester: {request.Requester}
Equipment: {request.Equipment}
Problem: {request.Problem}"
            };

            // Attach the PDF
            if (File.Exists(pdfPath))
            {
                builder.Attachments.Add(pdfPath);
            }
            else
            {
                Console.WriteLine($"PDF file not found at {pdfPath}");
            }

            email.Body = builder.ToMessageBody();

            using var smtp = new MailKit.Net.Smtp.SmtpClient();

            // SSL certificate bypass (not recommended for production)
            smtp.ServerCertificateValidationCallback = (sender, certificate, chain, sslPolicyErrors) => true;

            try
            {
                await smtp.ConnectAsync("smtp.sintergy.net", 587, MailKit.Security.SecureSocketOptions.StartTls);
                await smtp.AuthenticateAsync("notifications@sintergy.net", "$inT15851");
                await smtp.SendAsync(email);
            }
            catch (Exception ex)
            {
                // Log the exception
                Console.WriteLine($"Error sending email to {recipient}: {ex.Message}");
                throw;
            }
            finally
            {
                await smtp.DisconnectAsync(true);
            }
        }



        public async Task<List<string>> GetRequestersAsync()
        {
            var requesters = new List<string>();
            string query = "SELECT DISTINCT name FROM operators ORDER BY name";

            using (var connection = new MySqlConnection(_connectionString))
            {
                await connection.OpenAsync();
                using (var command = new MySqlCommand(query, connection))
                using (var reader = await command.ExecuteReaderAsync())
                {
                    while (await reader.ReadAsync())
                    {
                        requesters.Add(reader.GetString("name"));
                    }
                }
            }

            return requesters;
        }
        public async Task<List<string>> GetEquipmentListAsync()
        {
            var equipmentList = new List<string>();
            string query = @"SELECT equipment, name, brand, description 
                     FROM equipment 
                     WHERE status IS NULL OR status != 'obsolete' 
                     ORDER BY equipment";

            using (var connection = new MySqlConnection(_connectionString))
            {
                await connection.OpenAsync();
                using (var command = new MySqlCommand(query, connection))
                using (var reader = await command.ExecuteReaderAsync())
                {
                    while (await reader.ReadAsync())
                    {
                        // Combine equipment details into a single string
                        string equipment = reader["equipment"].ToString();
                        string name = reader["name"] != DBNull.Value ? reader["name"].ToString() : "N/A";
                        string brand = reader["brand"] != DBNull.Value ? reader["brand"].ToString() : "N/A";
                        string description = reader["description"] != DBNull.Value ? reader["description"].ToString() : "N/A";

                        // Format: "EquipmentNumber - Name (Brand: Description)"
                        equipmentList.Add($"{equipment} - {name} (Brand: {brand}, Description: {description})");
                    }
                }
            }

            return equipmentList;
        }

        public List<MaintenanceRequestModel> GetAllRequests()
        {
            var requests = new List<MaintenanceRequestModel>();
            string query = "SELECT * FROM maintenance ORDER BY id DESC";

            using (var connection = new MySqlConnection(_connectionString))
            {
                connection.Open();
                using (var command = new MySqlCommand(query, connection))
                using (var reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        requests.Add(new MaintenanceRequestModel
                        {
                            Id = Convert.ToInt32(reader["Id"]),
                            Timestamp = Convert.ToDateTime(reader["Timestamp"]),
                            Equipment = reader["Equipment"]?.ToString(),
                            Requester = reader["Requester"]?.ToString(),
                            RequestedDate = reader["RequestedDate"] == DBNull.Value ? null : Convert.ToDateTime(reader["RequestedDate"]),
                            Problem = reader["Problem"]?.ToString(),
                            DownStartDateTime = reader["DownStartDateTime"] == DBNull.Value ? null : Convert.ToDateTime(reader["DownStartDateTime"]),
                            ClosedDateTime = reader["ClosedDateTime"] == DBNull.Value ? null : Convert.ToDateTime(reader["ClosedDateTime"]),
                            CloseBy = reader["CloseBy"]?.ToString(),
                            CloseResult = reader["CloseResult"]?.ToString(),
                            DownStatus = Convert.ToBoolean(reader["DownStatus"]),
                            HourMeter = reader["HourMeter"] == DBNull.Value ? null : Convert.ToInt32(reader["HourMeter"]),
                            HoldStatus = Convert.ToBoolean(reader["HoldStatus"]),
                            HoldReason = reader["HoldReason"]?.ToString(),
                            HoldResult = reader["HoldResult"]?.ToString(),
                            HoldBy = reader["HoldBy"]?.ToString(),
                            FileAddress = reader["FileAddress"]?.ToString(),
                            FileAddressMediaLink = reader["FileAddressImageLink"]?.ToString(),
                            StatusHistory = reader["StatusHistory"]?.ToString(),
                            CurrentStatusBy = reader["CurrentStatusBy"]?.ToString(),
                            Department = reader["Department"]?.ToString()
                        });
                    }
                }
            }

            return requests;
        }

        public void UpdateRequest(MaintenanceRequestModel model, IFormFile? file)
        {
            string filePath = model.FileAddress; // Use the existing address if no new file is uploaded
            Console.WriteLine("Previous File Address: " + model.FileAddress);

            if (file != null && file.Length > 0)
            {
                if (!Directory.Exists(_uploadFolder))
                {
                    Directory.CreateDirectory(_uploadFolder); // Ensure the folder exists
                }

                // Generate the file name with extension
                var fileExtension = Path.GetExtension(file.FileName);
                var fileName = $"MaintenanceRequest_{model.Id}{fileExtension}";

                // Full physical path for saving the file
                filePath = Path.Combine(_uploadFolder, fileName);


                Console.WriteLine("Physical Path (for saving): " + filePath);

                try
                {
                    // Save the file to the physical location
                    using (var stream = new FileStream(filePath, FileMode.Create))
                    {
                        file.CopyTo(stream);
                    }

                    Console.WriteLine("File saved successfully at: " + filePath);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error saving file: {ex.Message}");
                    throw new Exception("File upload failed.");
                }
            }
            else
            {
                Console.WriteLine("No new file uploaded.");
            }

            // Update the database with the relative file path
            string query = @"UPDATE maintenance 
                 SET Equipment = @Equipment, 
                     Requester = @Requester, 
                     RequestedDate = @RequestedDate, 
                     Problem = @Problem, 
                     DownStartDateTime = @DownStartDateTime, 
                     ClosedDateTime = @ClosedDateTime, 
                     CloseBy = @CloseBy, 
                     CloseResult = @CloseResult, 
                     DownStatus = @DownStatus, 
                     HourMeter = @HourMeter, 
                     HoldStatus = @HoldStatus, 
                     HoldReason = @HoldReason, 
                     HoldResult = @HoldResult, 
                     HoldBy = @HoldBy, 
                     FileAddress = @FileAddress, 
                     FileAddressImageLink = @FileAddressImageLink, 
                     StatusHistory = @StatusHistory, 
                     CurrentStatusBy = @CurrentStatusBy, 
                     Department = @Department
                 WHERE Id = @Id";

            using (var connection = new MySqlConnection(_connectionString))
            {
                connection.Open();
                using (var command = new MySqlCommand(query, connection))
                {
                    // Bind parameters
                    command.Parameters.AddWithValue("@Id", model.Id);
                    command.Parameters.AddWithValue("@Equipment", model.Equipment);
                    command.Parameters.AddWithValue("@Requester", model.Requester);
                    command.Parameters.AddWithValue("@RequestedDate", model.RequestedDate);
                    command.Parameters.AddWithValue("@Problem", model.Problem);
                    command.Parameters.AddWithValue("@DownStartDateTime", model.DownStartDateTime);
                    command.Parameters.AddWithValue("@ClosedDateTime", model.ClosedDateTime);
                    command.Parameters.AddWithValue("@CloseBy", model.CloseBy);
                    command.Parameters.AddWithValue("@CloseResult", model.CloseResult);
                    command.Parameters.AddWithValue("@DownStatus", model.DownStatus);
                    command.Parameters.AddWithValue("@HourMeter", model.HourMeter);
                    command.Parameters.AddWithValue("@HoldStatus", model.HoldStatus);
                    command.Parameters.AddWithValue("@HoldReason", model.HoldReason);
                    command.Parameters.AddWithValue("@HoldResult", model.HoldResult);
                    command.Parameters.AddWithValue("@HoldBy", model.HoldBy);
                    command.Parameters.AddWithValue("@FileAddress", string.IsNullOrEmpty(model.FileAddress) ? DBNull.Value : model.FileAddress);
                    command.Parameters.AddWithValue("@FileAddressImageLink", string.IsNullOrEmpty(filePath) ? DBNull.Value : filePath);
                    command.Parameters.AddWithValue("@StatusHistory", string.IsNullOrEmpty(model.StatusHistory) ? DBNull.Value : model.StatusHistory);
                    command.Parameters.AddWithValue("@CurrentStatusBy", string.IsNullOrEmpty(model.CurrentStatusBy) ? DBNull.Value : model.CurrentStatusBy);
                    command.Parameters.AddWithValue("@Department", model.Department);

                    // Execute the query
                    command.ExecuteNonQuery();
                }
            }

            Console.WriteLine("Database updated with relative path: " + model.FileAddress);
        }


        public async Task<bool> UpdateMediaLinkFile(int id, string imagePath)
        {
            using (var connection = new MySqlConnection(_connectionString))
            {
                string query = "UPDATE maintenance SET FileAddressImageLink = @imagePath WHERE Id = @id";

                await connection.OpenAsync();
                using (var command = new MySqlCommand(query, connection))
                {
                    command.Parameters.AddWithValue("@imagePath", imagePath);
                    command.Parameters.AddWithValue("@id", id);

                    int rowsAffected = await command.ExecuteNonQueryAsync();
                    return rowsAffected > 0;
                }
            }
        }

    }

}
