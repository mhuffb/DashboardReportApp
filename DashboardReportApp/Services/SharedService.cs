using System.Diagnostics;
using System.Drawing.Printing;
using System.Net.Mail;
using System.Net;
using Microsoft.Data.SqlClient;
using MySql.Data.MySqlClient;
using System.Data;
using iText.StyledXmlParser.Jsoup.Select;
using Mysqlx.Crud;
using iText.Layout;
using iText.Kernel.Pdf;
using iText.Layout.Element;

namespace DashboardReportApp.Services
{
    public class SharedService
    {
        private readonly string _connectionStringMySQL;
        private readonly string _connectionStringSQLExpress;
        private readonly string _connectionStringSinTSQL;
        private readonly string _uploadFolder;
        private readonly IHttpContextAccessor _httpContextAccessor;

        public SharedService(IConfiguration configuration, IHttpContextAccessor httpContextAccessor)
        {
            _uploadFolder = @"\\SINTERGYDC2024\Vol1\VSP\Uploads";
            _connectionStringMySQL = configuration.GetConnectionString("MySQLConnection");
            _connectionStringSQLExpress = configuration.GetConnectionString("SQLExpressConnection");
            _connectionStringSinTSQL = configuration.GetConnectionString("SqlServerConnectionsinTSQL");
            _httpContextAccessor = httpContextAccessor;
        }

        public SharedService()
        {
        }

       

        public string SaveFileToUploads(IFormFile file, string prefix, int id)
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
            //var uniqueName = "_" + DateTime.Now.Ticks + extension;
            var finalPath = Path.Combine(_uploadFolder, prefix + "_" + id + extension);

            // Copy the file to disk
            using (var stream = new FileStream(finalPath, FileMode.Create))
            {
                file.CopyTo(stream);
            }

            // Return the path so we can save it in record.FileAddress1
            return finalPath;
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
            // Example mapping of users (or locations) to printers
            var userPrinterMappings = new Dictionary<string, string>
    {
        { @"Office01.sintergyinc.local", "None" },
        { @"mold02", "Mold02" },
        { @"MOLD03", "Mold03" },
        { @"MOLD04-PC", "Mold004" },
        // Add additional mappings as needed
    };

            // Try to get the printer name from the dictionary
            if (userPrinterMappings.TryGetValue(userName, out string printerName))
            {
                return printerName;
            }

            // Fallback option if not found
            return "None";
        }
        public void PrintFileToSpecificPrinter(string printerName, string pdfPath, int copies)
        {
            if (string.IsNullOrWhiteSpace(pdfPath) || !File.Exists(pdfPath))
            {
                throw new FileNotFoundException("PDF file not found for printing.", pdfPath);
            }

            if (copies < 1)
            {
                throw new ArgumentException("Number of copies must be at least 1.", nameof(copies));
            }
            if (printerName != "None")
            {

                try
                {
                    string sumatraPath = @"C:\Tools\SumatraPDF\SumatraPDF.exe"; // Path to SumatraPDF executable

                    // Validate printer existence
                    if (!PrinterSettings.InstalledPrinters.Cast<string>()
                        .Any(p => p.Equals(printerName, StringComparison.OrdinalIgnoreCase)))
                    {
                        throw new Exception($"Printer '{printerName}' is not installed.");
                    }

                    // Validate SumatraPDF existence
                    if (!File.Exists(sumatraPath))
                    {
                        throw new FileNotFoundException("SumatraPDF executable not found.", sumatraPath);
                    }

                    // Build command arguments
                    // Only add the copies setting if more than one copy is requested.
                    string copyArgs = copies > 1 ? $" -print-settings \"copies={copies}\"" : "";
                    string arguments = $"-print-to \"{printerName}\"{copyArgs} \"{pdfPath}\"";

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
            string sumatra = @"C:\Tools\SumatraPDF\SumatraPDF.exe";
            if (!File.Exists(sumatra))
                throw new FileNotFoundException("SumatraPDF executable not found.", sumatra);

            string copyArg = copies > 1 ? $" -print-settings \"copies={copies}\"" : "";
            string args = $"-print-to \"{printerName}\"{copyArg} \"{pdfPath}\"";

            var p = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = sumatra,
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
