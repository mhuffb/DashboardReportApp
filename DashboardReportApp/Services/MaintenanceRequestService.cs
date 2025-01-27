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
        public MaintenanceRequestService(IConfiguration configuration, IWebHostEnvironment webHostEnvironment)
        {
            _connectionString = configuration.GetConnectionString("MySQLConnection");
            _webHostEnvironment = webHostEnvironment;
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
                            FileAddressImageLink = !reader.IsDBNull(reader.GetOrdinal("fileAddressImageLink")) ? reader.GetString("fileAddressImageLink") : null,
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
                    command.Parameters.AddWithValue("@fileAddress", request.FileAddressImageLink ?? (object)DBNull.Value);
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

            string outputFilePath = @$"\\SINTERGYDC2024\Vol1\Visual Studio Programs\VSApps\MaintenanceComplete\pdf\{request.Id}.pdf";

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
                            if (!string.IsNullOrEmpty(request.FileAddressImageLink))
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

        public async Task<bool> UpdateImagePathAsync(int id, string imagePath)
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
