using MailKit.Net.Pop3;
using MimeKit;
using MySql.Data.MySqlClient;
using System.IO;
using System.Threading.Tasks;
using System.Text.RegularExpressions;
using System.Configuration;
using System;

namespace DashboardReportApp.Services
{
    public class EmailAttachmentService
    {
        private readonly string _connectionString;
        private readonly string _emailServer;
        private readonly string _emailAddress;
        private readonly string _emailPassword;
        private readonly string _attachmentSavePath;

        public EmailAttachmentService(IConfiguration configuration)
        {
            _connectionString = configuration.GetConnectionString("MySQLConnection");
            _emailServer = configuration["Email:Server"];
            _emailAddress = configuration["Email:Address"];
            _emailPassword = configuration["Email:Password"];
            // _attachmentSavePath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot/uploads");
        }




        public async Task ProcessIncomingEmailsAsync()
        {
            try
            {
                using (var client = new Pop3Client())
                {
                    client.ServerCertificateValidationCallback = (sender, certificate, chain, sslPolicyErrors) => true;

                    await client.ConnectAsync("pop.sintergy.net", 995, true);
                    await client.AuthenticateAsync("fixit@sintergy.net", "$inT15851");

                    for (int i = 0; i < client.Count; i++)
                    {
                        var message = client.GetMessage(i);
                        Console.WriteLine($"Processing email: {message.Subject ?? "(No Subject)"}");

                        string orderId = ExtractOrderId(message.Subject);
                        Console.WriteLine($"Extracted Order ID: {orderId}");

                        if (string.IsNullOrEmpty(orderId))
                        {
                            Console.WriteLine("Invalid or missing order ID. Skipping email.");
                            continue;
                        }

                        var attachmentsFound = false;

                        // Process attachments
                        foreach (var attachment in message.BodyParts)
                        {
                            if (attachment is MimePart mimePart)
                            {
                                var disposition = mimePart.ContentDisposition?.Disposition;

                                // Handle both attachments and inline images
                                if (disposition == ContentDisposition.Attachment || disposition == ContentDisposition.Inline)
                                {
                                    string fileName = "MaintenanceRequest" + orderId + "_" + (mimePart.FileName ?? $"embedded_{Guid.NewGuid()}.bin");

                                    //string attachmentSavePath = Path.Combine("wwwroot/uploads", fileName);
                                    string attachmentSavePath = Path.Combine(@"\\SINTERGYDC2024\\Vol1\\Visual Studio Programs\images", fileName);
                                    
                                    using (var stream = File.Create(attachmentSavePath))
                                    {
                                        mimePart.Content.DecodeTo(stream);
                                    }

                                    await SaveMediaToOrder(orderId, attachmentSavePath);
                                    Console.WriteLine($"Attachment saved to {attachmentSavePath}.");
                                    attachmentsFound = true;
                                }
                            }
                        }

                        if (!attachmentsFound)
                        {
                            Console.WriteLine("No attachments or embedded images found. Skipping email.");
                        }

                        client.DeleteMessage(i);
                    }

                    await client.DisconnectAsync(true);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error processing emails: {ex.Message}");
            }
        }


        private string ExtractOrderId(string subject)
        {
            if (string.IsNullOrWhiteSpace(subject))
            {
                Console.WriteLine("Email subject is null or empty.");
                return null;
            }

            var match = System.Text.RegularExpressions.Regex.Match(subject, @"#(\d+)");
            return match.Success ? match.Groups[1].Value : null;
        }

        private async Task SaveMediaToOrder(string orderId, string filePath)
        {
            // Save the media file path in your database associated with the order
            using (var connection = new MySqlConnection(_connectionString))
            {
                string query = "UPDATE maintenance SET fileAddressimagelink = @filePath WHERE id = @orderId";

                await connection.OpenAsync();
                using (var command = new MySqlCommand(query, connection))
                {
                    command.Parameters.AddWithValue("@filePath", filePath);
                    command.Parameters.AddWithValue("@orderId", orderId);
                    await command.ExecuteNonQueryAsync();
                }
            }
        }



    }


}
