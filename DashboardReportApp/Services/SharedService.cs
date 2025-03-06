using System.Diagnostics;
using System.Drawing.Printing;
using System.Net.Mail;
using System.Net;
using Microsoft.Data.SqlClient;
using MySql.Data.MySqlClient;
using System.Data.Odbc;

namespace DashboardReportApp.Services
{
    public class SharedService
    {
        private readonly string _connectionStringMySQL;
        private readonly string _connectionStringSQLExpress;
        private readonly string _connectionStringDataflex;
        private readonly string _uploadFolder;

        public SharedService(IConfiguration configuration)
        {
            _uploadFolder = @"\\SINTERGYDC2024\Vol1\VSP\Uploads";
            _connectionStringMySQL = configuration.GetConnectionString("MySQLConnection");
            _connectionStringSQLExpress = configuration.GetConnectionString("SQLExpressConnection");
            _connectionStringDataflex = configuration.GetConnectionString("DataflexConnection");
        }

        public SharedService()
        {
        }

        public void PrintFile(string printerName, string pdfPath)
        {
            if (string.IsNullOrWhiteSpace(pdfPath) || !File.Exists(pdfPath))
            {
                throw new FileNotFoundException("PDF file not found for printing.", pdfPath);
            }

            try
            {

                string sumatraPath = @"C:\Tools\SumatraPDF\SumatraPDF.exe"; // Path to SumatraPDF executable

                // Validate printer existence
                if (!PrinterSettings.InstalledPrinters.Cast<string>().Any(p => p.Equals(printerName, StringComparison.OrdinalIgnoreCase)))
                {
                    throw new Exception($"Printer '{printerName}' is not installed.");
                }

                // Validate SumatraPDF existence
                if (!File.Exists(sumatraPath))
                {
                    throw new FileNotFoundException("SumatraPDF executable not found.", sumatraPath);
                }

                // Set up the SumatraPDF command-line arguments
                string arguments = $"-print-to \"{printerName}\" \"{pdfPath}\"";

                // Start the process
                var process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = sumatraPath,
                        Arguments = arguments,
                        CreateNoWindow = true,
                        WindowStyle = ProcessWindowStyle.Hidden,
                    }
                };

                process.Start();
                process.WaitForExit();

                if (process.ExitCode != 0)
                {
                    throw new Exception($"Printing process exited with code {process.ExitCode}.");
                }
            }
            catch (Exception ex)
            {
                throw new Exception($"Failed to print PDF: {ex.Message}");
            }
        }
        public string SaveFileToUploads(IFormFile file, string prefix)
        {
            //Prefixes HoldTagFile1, HoldTagFile2, 
            if (file == null || file.Length == 0)
            {
                throw new ArgumentException("File is null or empty.", nameof(file));
            }

            // Ensure the upload folder exists
            if (!Directory.Exists(_uploadFolder))
            {
                Directory.CreateDirectory(_uploadFolder);
            }

            // Create a unique filename: "HoldTagFile_637622183523457159.pdf", etc.
            var extension = Path.GetExtension(file.FileName);
            var uniqueName = "_" + DateTime.Now.Ticks + extension;
            var finalPath = Path.Combine(_uploadFolder, uniqueName);

            // Copy the file to disk
            using (var stream = new FileStream(finalPath, FileMode.Create))
            {
                file.CopyTo(stream);
            }

            // Return the path so we can save it in record.FileAddress1
            return finalPath;
        }
        public void SendEmailWithAttachment(string receiverEmail, string attachmentPath, string subject, string body)
        {
            string senderEmail = "notifications@sintergy.net";
            string senderPassword = "$inT15851";
            int smtpPort = 587;
            string smtpServer = "smtp.sintergy.net";
            // Create the email content

            MailMessage mail = new MailMessage(senderEmail, receiverEmail)
            {
                Subject = subject,
                Body = body
            };

            // Attach the PDF if it exists
            if (!string.IsNullOrEmpty(attachmentPath))
            {
                Attachment attachment = new Attachment(attachmentPath);
                mail.Attachments.Add(attachment);
            }

            // Configure SMTP client
            SmtpClient smtpClient = new SmtpClient(smtpServer)
            {
                Port = smtpPort,
                Credentials = new NetworkCredential(senderEmail, senderPassword),
                EnableSsl = false // Set this to true if your server supports SSL
            };

            try
            {
                smtpClient.Send(mail);
            }
            catch (Exception ex)
            {
                throw new Exception($"Failed to send email: {ex.Message}");
            }
            finally
            {
                mail.Dispose();
            }
        }
        public static string ParseProlinkPartNumber(string prolinkPartNumber)
        {
            string sintergyPartNumber = "";

            if (!string.IsNullOrEmpty(prolinkPartNumber))
            {
                // Split the string by '-'
                string[] parts = prolinkPartNumber.Split('-');

                // Check if there are at least two '-' in the string
                if (parts.Length > 2)
                {
                    // Concatenate the first two parts back with '-'
                    sintergyPartNumber = $"{parts[0]}-{parts[1]}";
                }
                else
                {
                    // If there are fewer than two '-', use the original string
                    sintergyPartNumber = prolinkPartNumber;
                }
            }

            return sintergyPartNumber;
        }

        public async Task<List<string>> GetActiveProlinkParts()
        {
            var parts = new List<string>();
            string query = @"
        SELECT DISTINCT
    qf.qcc_file_desc
FROM 
    part p
INNER JOIN 
    qcc_file qf ON p.qcc_file_id = qf.qcc_file_id
WHERE 
    p.measure_date >= DATEADD(HOUR, -12, GETDATE())
    AND qf.qcc_file_desc NOT LIKE '%CALIBRATION%'
ORDER BY 
    qf.qcc_file_desc";


            using (var connection = new SqlConnection(_connectionStringSQLExpress))
            {
                await connection.OpenAsync();
                using (var command = new SqlCommand(query, connection))
                using (var reader = await command.ExecuteReaderAsync())
                {
                    var uniqueParts = new HashSet<string>(); // Use HashSet to eliminate duplicates

                    while (await reader.ReadAsync())
                    {
                        string prolinkPartNumber = reader["qcc_file_desc"].ToString(); // Fetch the raw part number

                        string parsedPartNumber = SharedService.ParseProlinkPartNumber(prolinkPartNumber);


                        if (!string.IsNullOrEmpty(parsedPartNumber))
                        {
                            uniqueParts.Add(parsedPartNumber); // Ensure uniqueness
                        }
                    }

                    parts = uniqueParts.ToList(); // Convert HashSet to List
                }
            }

            return parts;
        }
        public async Task<List<string>> GetAllOperators()
        {
            var operators = new List<string>();
            string query = "SELECT DISTINCT name FROM operators ORDER BY name";

            using (var connection = new MySqlConnection(_connectionStringMySQL))
            {
                await connection.OpenAsync();
                using (var command = new MySqlCommand(query, connection))
                using (var reader = await command.ExecuteReaderAsync())
                {
                    while (await reader.ReadAsync())
                    {
                        operators.Add(reader["name"].ToString());
                    }
                }
            }

            return operators;
        }

        public List<string> GetOrderOfOps(string part)
        {
            List<string> result = new List<string>();

            string query = "SELECT mstep.master_id, pstep.\"desc\", mastersg.qc_prior, mastersg.qc_after " +
                           "FROM (mstep LEFT OUTER JOIN mastersg ON (mstep.master_id = mastersg.master_id AND mstep.pstep = mastersg.pstep)) " +
                           "LEFT OUTER JOIN pstep ON mstep.pstep = pstep.pcode " +
                           "WHERE mstep.omit = 0 AND mstep.master_id = ? " +
                           "AND pstep.\"desc\" IS NOT NULL " +
                           "AND (mastersg.omit = 0 OR mastersg.omit IS NULL) " +
                           "ORDER BY mstep.master_id";

            using (OdbcConnection conn = new OdbcConnection(_connectionStringDataflex))
            using (OdbcCommand cmd = new OdbcCommand(query, conn))
            {
                // Set the part number parameter
                cmd.Parameters.AddWithValue("?", part);
                conn.Open();
                using (OdbcDataReader reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        string desc = reader["desc"].ToString();

                        // Check if qc_prior equals 1 and add "QC Check" before the description if true.
                        bool qcPriorIsOne = reader["qc_prior"] != DBNull.Value && Convert.ToInt32(reader["qc_prior"]) == 1;
                        // Check if qc_after equals 1 and add "QC Check" after the description if true.
                        bool qcAfterIsOne = reader["qc_after"] != DBNull.Value && Convert.ToInt32(reader["qc_after"]) == 1;

                        if (qcPriorIsOne)
                        {
                            result.Add("QC Check");
                        }
                        result.Add(desc);
                        if (qcAfterIsOne)
                        {
                            result.Add("QC Check");
                        }
                    }
                }
            }
            return result;
        }
    }
}
