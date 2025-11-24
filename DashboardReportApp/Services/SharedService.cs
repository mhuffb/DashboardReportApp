using DashboardReportApp.Models;
using iText.Kernel.Pdf;
using iText.Layout;
using iText.Layout.Element;
using iText.StyledXmlParser.Jsoup.Select;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using MySql.Data.MySqlClient;
using Mysqlx.Crud;
using System.Data;
using System.Diagnostics;
using System.Drawing.Printing;
using System.Net;
using System.Net.Mail;

namespace DashboardReportApp.Services
{
    public class SharedService
    {
        private readonly string _connectionStringMySQL;
        private readonly string _connectionStringSQLExpress;
        private readonly string _connectionStringSinTSQL;
        private readonly string _uploadFolder;
        private readonly IHttpContextAccessor _httpContextAccessor;
        private readonly PrinterOptions _printing;
        private readonly string _sumatraExePath;

        private readonly string _clientHostLogPath;
        public SharedService(IConfiguration configuration, IHttpContextAccessor httpContextAccessor, IOptions<PrinterOptions> printingOptions)
        {
            _connectionStringMySQL = configuration.GetConnectionString("MySQLConnection");
            _connectionStringSQLExpress = configuration.GetConnectionString("SQLExpressConnection");
            _connectionStringSinTSQL = configuration.GetConnectionString("SqlServerConnectionsinTSQL");
            _httpContextAccessor = httpContextAccessor;
            _printing = printingOptions.Value;
            _sumatraExePath = configuration["Printing:SumatraExePath"]
                      ?? @"C:\Users\OFFICE_01\AppData\Local\SumatraPDF\SumatraPDF.exe"; // optional fallback
            _clientHostLogPath = configuration["Paths:ClientHostLog"]
                     ?? @"\\Sintergydc2024\vol1\vsp\testcomputername.txt";
        }

        public SharedService()
        {
        }

       

       
        public void SendEmailWithAttachment2(string receiverEmail, string attachmentPath, string attachmentPath2, string subject, string body)
        {
        }
        public void SendEmailWithAttachment(string receiverEmail, string attachmentPath, string attachmentPath2, string subject, string body)
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
            // Attach the PDF if it exists
            if (!string.IsNullOrEmpty(attachmentPath2))
            {
                Attachment attachment2 = new Attachment(attachmentPath2);
                mail.Attachments.Add(attachment2);
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

                if (prolinkPartNumber.Contains("B"))
                {
                    // If there are at least three parts, join the first three parts
                    if (parts.Length > 2)
                    {
                        sintergyPartNumber = $"{parts[0]}-{parts[1]}-{parts[2]}-{parts[3]}";
                    }
                    else
                    {
                        // If not enough parts, fallback to the original string
                        sintergyPartNumber = prolinkPartNumber;
                    }
                }
                // Check if the string contains "SL"
                else if (prolinkPartNumber.Contains("SL"))
                {
                    // If there are at least three parts, join the first three parts
                    if (parts.Length > 2)
                    {
                        sintergyPartNumber = $"{parts[0]}-{parts[1]}-{parts[2]}";
                    }
                    else
                    {
                        // If not enough parts, fallback to the original string
                        sintergyPartNumber = prolinkPartNumber;
                    }
                }
                else
                {
                    // Original logic: if there are at least two '-' (i.e. three parts) then join the first two parts
                    if (parts.Length > 2)
                    {
                        sintergyPartNumber = $"{parts[0]}-{parts[1]}";
                    }
                    else
                    {
                        sintergyPartNumber = prolinkPartNumber;
                    }
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
            var result = new List<string>();

            const string sql = @"
SELECT  m.master_id,
        ps.[desc]     AS step_desc,
        ms.qc_prior,
        ms.qc_after
FROM    dbo.mstep      AS m
LEFT JOIN dbo.mastersg AS ms
       ON m.master_id = ms.master_id
      AND m.pstep     = ms.pstep
LEFT JOIN dbo.pstep    AS ps
       ON m.pstep     = ps.pcode
WHERE   m.omit = 0
  AND   m.master_id = @part
  AND   ps.[desc] IS NOT NULL
  AND   (ms.omit = 0 OR ms.omit IS NULL)
ORDER BY m.master_id;  -- change to m.seq if you have a sequence column
";

            using var conn = new SqlConnection(_connectionStringSinTSQL);
            using var cmd = new SqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@part", part ?? (object)DBNull.Value);

            conn.Open();
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                string desc = reader["step_desc"]?.ToString() ?? string.Empty;

                bool qcPriorIsOne = reader["qc_prior"] != DBNull.Value && Convert.ToInt32(reader["qc_prior"]) == 1;
                bool qcAfterIsOne = reader["qc_after"] != DBNull.Value && Convert.ToInt32(reader["qc_after"]) == 1;

                if (qcPriorIsOne) result.Add("QC Check");
                if (!string.IsNullOrWhiteSpace(desc)) result.Add(desc);
                if (qcAfterIsOne) result.Add("QC Check");
            }

            Console.WriteLine(string.Join(", ", result));
            return result;
        }


        public string GetDeviceIp(string machine)
        {
            // If the machine already looks like an IP, return it directly.
            if (machine.Contains("."))
                return machine;

            var deviceIPs = new Dictionary<string, string>
            {
                { "1", "192.168.1.30" },
                { "2", "192.168.1.31" },
                { "41", "192.168.1.32" },
                { "45", "192.168.1.33" },
                { "50", "192.168.1.34" },
                { "51", "192.168.1.35" },
                { "57", "192.168.1.36" },
                { "59", "192.168.1.37" },
                { "70", "192.168.1.38" },
                { "74", "192.168.1.39" },
                { "92", "192.168.1.40" },
                { "95", "192.168.1.41" },
                { "102", "192.168.1.42" },
                { "112", "192.168.1.43" },
                { "124", "192.168.1.44" },
                { "125", "192.168.1.45" },
                { "154", "192.168.1.46" },
                { "156", "192.168.1.47" },
                { "175", "192.168.1.48" }
            };

            if (deviceIPs.TryGetValue(machine, out var ip))
            {
                return ip;
            }
            throw new Exception($"No device found for machine: {machine}");
        }

        public async Task<DataTable> GetLatestPartFactorDetailsAsync(string partNumber, DateTime? startDate, DateTime? endDate)
        {
            DataTable dt = new DataTable();
            using (var connection = new SqlConnection(_connectionStringSQLExpress))
            {
                await connection.OpenAsync();
                using (var command = new SqlCommand("GetLatestPartFactorDetails", connection))
                {
                    command.CommandType = CommandType.StoredProcedure;
                    // Pass the parameters to the stored procedure.
                    command.Parameters.AddWithValue("@qcc_file_desc", partNumber);
                    command.Parameters.AddWithValue("@start_date", startDate);
                    command.Parameters.AddWithValue("@end_date", endDate);
                    using (var reader = await command.ExecuteReaderAsync())
                    {
                        dt.Load(reader);
                    }
                }
            }
            return dt;
        }


        public async Task<DataTable> GetStatisticsAsync(string part, DateTime? startDate)
        {
            DataTable dt = new DataTable();
            using (var connection = new SqlConnection(_connectionStringSQLExpress))
            {
                await connection.OpenAsync();
                using (var command = new SqlCommand("GetStatistics", connection))
                {
                    command.CommandType = CommandType.StoredProcedure;
                    // Pass the part and start date parameters
                    command.Parameters.AddWithValue("@qcc_file_desc", part);
                    command.Parameters.AddWithValue("@startDate", startDate);
                    using (var reader = await command.ExecuteReaderAsync())
                    {
                        dt.Load(reader);
                    }
                }
            }
            return dt;
        }

        public async Task<string> GetMostCurrentProlinkPart(string searchString)
        {
            Console.WriteLine("searchstring: " + searchString);
            string partResult = null;
            string query;
            if(searchString.Contains("SL"))
            {
                query = @"SELECT TOP 1 qf.qcc_file_desc
FROM part p
INNER JOIN qcc_file qf ON p.qcc_file_id = qf.qcc_file_id
WHERE qf.qcc_file_desc LIKE @searchString + '-%'
  AND qf.qcc_file_desc LIKE '%MOLD%'
  AND qf.qcc_file_desc LIKE '%SL%'
  AND qf.qcc_file_desc NOT LIKE '%IM%'
  AND qf.qcc_file_desc NOT LIKE '%SETUP%'
  AND qf.qcc_file_desc NOT LIKE '%CALIBRATION%'
ORDER BY p.measure_date DESC"
;
            }
            else
            {
                query = @"
       SELECT TOP 1 qf.qcc_file_desc
FROM part p
INNER JOIN qcc_file qf ON p.qcc_file_id = qf.qcc_file_id
WHERE qf.qcc_file_desc LIKE @searchString + '-%'
  AND qf.qcc_file_desc LIKE '%MOLD%'
  AND qf.qcc_file_desc NOT LIKE '%SL%'
  AND qf.qcc_file_desc NOT LIKE '%IM%'
  AND qf.qcc_file_desc NOT LIKE '%SETUP%'
  AND qf.qcc_file_desc NOT LIKE '%CALIBRATION%'
ORDER BY p.measure_date DESC


";
            }
           

            string prolinkPartNumber = "";

            using (var connection = new SqlConnection(_connectionStringSQLExpress))
            {
                await connection.OpenAsync();
                using (var command = new SqlCommand(query, connection))
                {
                    command.Parameters.AddWithValue("@searchString", searchString);
                    using (var reader = await command.ExecuteReaderAsync())
                    {
                        if (await reader.ReadAsync())
                        {
                            prolinkPartNumber = reader["qcc_file_desc"].ToString();
                            
                        }
                    }
                }
            }

            return prolinkPartNumber;
        }
        public string processDesc(string input)
        {
            switch (input)
            {
                case string a when a.Contains("HEAT TREAT-AHT") || a.Contains("HEAT TREAT - US HEAT") || a.Contains("HEAT TREAT - ELK COUNTY"): return "Heat Treat";

                case string st when st.Contains("Steam Treat") || st.Contains("STEAM TREAT"): return "Steam Treat";

                case string lsp when lsp.Contains("LS&PLATE") || lsp.Contains("LS & PLATE") || lsp.Contains("LS & Plate"): return "LS & Plate";

                case string Gr when Gr.Contains("Grind") || Gr.Contains("GRIND"): return "Grind";

                case string mc when mc.Contains("Machin"): return "Machine";

                case string et when et.Contains("Etching") || et.Contains("ETCHING"): return "Etching";

                case string hn when hn.Contains("Honing") || hn.Contains("HONING"): return "Honing";

                case string pl when pl.Contains("Plating") || pl.Contains("PLATING"): return "Plating";

                case string bo when bo.Contains("Black Oxide") || bo.Contains("BLACK OXIDE"): return "Black Oxide";

                case string mg when mg.Contains("Magni-Coat") || mg.Contains("MAGNI-COAT"): return "Magni-Coat";

                case string ls when ls.Contains("Loctite Seal") || ls.Contains("LOCTITE SEAL"): return "Loctite Seal";

                case string tb when tb.Contains("Tumbl") || tb.Contains("TUMBL"): return "Tumble";

            }
            return input;
        }

        public async Task<string> GetLatestProlinkPartForMachineAsync(string machine)
        {
            string partName = null;
            string query = @"
        SELECT TOP 1 qf.qcc_file_desc
        FROM dbo.part p
        INNER JOIN dbo.qcc_file qf ON p.qcc_file_id = qf.qcc_file_id
        INNER JOIN dbo.part_factor pf ON p.part_id = pf.part_id
        INNER JOIN dbo.factor f ON pf.factor_id = f.factor_id
        WHERE p.measure_date >= DATEADD(HOUR, -1, GETDATE())
          AND (f.factor_desc LIKE '%Machine%' OR f.factor_desc LIKE '%Press%')
          AND pf.value = @machine
          AND qf.qcc_file_desc NOT LIKE '%CALIBRATION%'
        ORDER BY p.measure_date DESC;";

            using (var connection = new SqlConnection(_connectionStringSQLExpress))
            {
                await connection.OpenAsync();
                using (var command = new SqlCommand(query, connection))
                {
                    command.Parameters.AddWithValue("@machine", machine);
                    using (var reader = await command.ExecuteReaderAsync())
                    {
                        if (await reader.ReadAsync())
                        {
                            string prolinkPartNumber = reader["qcc_file_desc"].ToString();
                            // Use your helper method to parse the part number.
                            partName = ParseProlinkPartNumber(prolinkPartNumber);
                        }
                    }
                }
            }
            return partName;
        }

        public void PrintFileToClosestPrinter(string pdfPath, int copies)
        {

            var context = _httpContextAccessor.HttpContext;
            string clientHostName = "Unknown";
            if (context != null)
            {
                string clientIp = context.Connection.RemoteIpAddress?.ToString();

                // Reverse DNS (optional, and may be slow if DNS is not set up or unresponsive)

                try
                {
                    if (!string.IsNullOrEmpty(clientIp))
                    {
                        var entry = Dns.GetHostEntry(clientIp);
                        clientHostName = entry.HostName;
                    }
                }
                catch (System.Net.Sockets.SocketException)
                {
                    // Reverse DNS failed or not possible
                }

                // Prepare the log message. Adjust the text if needed.
                string textToWrite = $"{DateTime.Now}: User name is {clientHostName} Ip is {clientIp}";

                // Define the shared file path
                string filePath = @"\\Sintergydc2024\vol1\vsp\testcomputername.txt";

                try
                {
                    // Append the text to the file (creates the file if it doesn't exist)
                    System.IO.File.AppendAllText(filePath, textToWrite + Environment.NewLine);
                }
                catch (Exception ex)
                {
                    // Consider proper logging or error handling here
                    // For example, log the error to a logging service or a local file if file access fails
                    throw new Exception("Error writing to log file", ex);
                }
            }
            else
            {
                Console.WriteLine("No active HttpContext available.");
            }

            // Get the corresponding printer based on the user
            string printerName = GetPrinterForUser(clientHostName);

            PrintFileToSpecificPrinter(printerName, pdfPath, copies);

        }

        private string GetPrinterForUser(string userName)
        {
            // No host? Just use default
            if (string.IsNullOrWhiteSpace(userName))
                return _printing.Default ?? "Microsoft Print to PDF";

            // Exact key match (case-sensitive)
            if (_printing.HostMappings != null &&
                _printing.HostMappings.TryGetValue(userName, out var printerFromExact))
            {
                return printerFromExact;
            }

            // Case-insensitive fallback
            if (_printing.HostMappings != null)
            {
                foreach (var kvp in _printing.HostMappings)
                {
                    if (kvp.Key.Equals(userName, StringComparison.OrdinalIgnoreCase))
                    {
                        return kvp.Value;
                    }
                }
            }

            // Final fallback
            return _printing.Default ?? "Microsoft Print to PDF";
        }


        public void PrintFileToSpecificPrinter(string printerName, string filePath, int copies = 1)
        {
            if (string.IsNullOrWhiteSpace(_printing.SumatraExePath) || !File.Exists(_printing.SumatraExePath))
                throw new FileNotFoundException(
                    "SumatraPDF.exe not found. Install SumatraPDF or set Printing:SumatraExePath in appsettings.json.",
                    _printing.SumatraExePath ?? "<unset>");

            if (string.IsNullOrWhiteSpace(printerName))
                throw new ArgumentException("Printer name is required.", nameof(printerName));

            if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
                throw new FileNotFoundException("File to print not found.", filePath);

            if (copies < 1) copies = 1;

            // Sumatra accepts -print-to, and will respect copies via -print-settings "copies=N"
            // (If your driver ignores that, we can loop N times instead.)
            string args = $"-silent -exit-on-print -print-to \"{printerName}\" -print-settings \"copies={copies}\" \"{filePath}\"";

            var psi = new ProcessStartInfo
            {
                FileName = _printing.SumatraExePath,
                Arguments = args,
                CreateNoWindow = true,
                UseShellExecute = false,
                WindowStyle = ProcessWindowStyle.Hidden
            };

            using var proc = Process.Start(psi);
            if (proc == null)
                throw new InvalidOperationException("Failed to start SumatraPDF printing process.");

            // Optional: wait for completion & error check
            proc.WaitForExit(15_000); // 15s timeout; adjust if needed
            if (proc.ExitCode != 0)
                throw new InvalidOperationException($"SumatraPDF exited with code {proc.ExitCode} while printing.");
        }
        public void PrintPlainText(string printerName, string text, int copies = 1)
        {
            if (copies < 1) throw new ArgumentException("Copies must be ≥1.", nameof(copies));
            if (string.IsNullOrWhiteSpace(printerName) || printerName.Equals("None", StringComparison.OrdinalIgnoreCase))
                return;

            if (!PrinterSettings.InstalledPrinters.Cast<string>()
                .Any(p => p.Equals(printerName, StringComparison.OrdinalIgnoreCase)))
                throw new Exception($"Printer '{printerName}' is not installed.");

            /* --- create temp PDF with iText 7 --- */
            string pdfPath = Path.Combine(Path.GetTempPath(), $"txt_{Guid.NewGuid():N}.pdf");

            using (var writer = new PdfWriter(pdfPath))
            using (var pdf = new PdfDocument(writer))
            using (var doc = new Document(pdf))
            {
                doc.SetMargins(40, 40, 40, 40);          // 40 pt ≈ 0.55 in
                doc.Add(new Paragraph(text ?? string.Empty).SetFontSize(11));
            }

            /* --- silent SumatraPDF print --- */
            if (!File.Exists(_sumatraExePath))
                throw new FileNotFoundException("SumatraPDF executable not found.", _sumatraExePath);

            string copyArg = copies > 1 ? $" -print-settings \"copies={copies}\"" : "";
            string args = $"-print-to \"{printerName}\"{copyArg} \"{pdfPath}\"";

            var p = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = _sumatraExePath,
                    Arguments = args,
                    CreateNoWindow = true,
                    WindowStyle = ProcessWindowStyle.Hidden,
                    UseShellExecute = false
                }
            };

            p.Start();
            p.WaitForExit();
            if (p.ExitCode != 0) throw new Exception($"Printing exited with code {p.ExitCode}.");

            try { File.Delete(pdfPath); } catch { /* ignore */ }
        }
    }
}
