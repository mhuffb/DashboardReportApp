using System.Diagnostics;
using System.Drawing.Printing;
using System.Net.Mail;
using System.Net;
using Microsoft.Data.SqlClient;
using MySql.Data.MySqlClient;
using System.Data.Odbc;
using System.Data;

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

        public void PrintFile(string printerName, string pdfPath, int copies = 1)
        {
            if (string.IsNullOrWhiteSpace(pdfPath) || !File.Exists(pdfPath))
            {
                throw new FileNotFoundException("PDF file not found for printing.", pdfPath);
            }

            if (copies < 1)
            {
                throw new ArgumentException("Number of copies must be at least 1.", nameof(copies));
            }

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
           // Console.WriteLine(result.ToArray());
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
            string partResult = null;
            string query = @"
        SELECT TOP 1 qf.qcc_file_desc
        FROM part p
        INNER JOIN qcc_file qf ON p.qcc_file_id = qf.qcc_file_id
        WHERE qf.qcc_file_desc LIKE '%' + @searchString + '%'
          AND qf.qcc_file_desc LIKE '%MOLD%'
          AND qf.qcc_file_desc NOT LIKE '%IM%'
          AND qf.qcc_file_desc NOT LIKE '%SETUP%'
          AND qf.qcc_file_desc NOT LIKE '%CALIBRATION%'
        ORDER BY p.measure_date DESC";

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

    }
}
