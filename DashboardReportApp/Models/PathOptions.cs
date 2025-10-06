using Microsoft.AspNetCore.Mvc;

namespace DashboardReportApp.Models
{
    // Models/PathOptions.cs
    public sealed class PathOptions
    {
        public string DeviationUploads { get; set; } = "";
        public string? HoldTagUploads { get; set; }
        public string? ProcessChangeUploads { get; set; }
        public string AssemblyExports { get; set; } = "";
        public string? HoldTagExports { get; set; }
        public string MaintenanceUploads { get; set; } = "";
        public string MaintenanceExports { get; set; } = "";
    }

    public sealed class EmailOptions
    {
        // From
        public string FromName { get; set; } = "Dashboard Report";
        public string FromAddress { get; set; } = "notifications@sintergy.net";

        // SMTP
        public string SmtpHost { get; set; } = "smtp.sintergy.net";
        public int SmtpPort { get; set; } = 587;
        public bool UseStartTls { get; set; } = true;   // STARTTLS
        public bool UseSsl { get; set; } = false;       // SMTPS (usually false if STARTTLS true)
        public string Username { get; set; } = "notifications@sintergy.net";
        public string Password { get; set; } = "$inT15851";

        // Where to send by department (we build dept@DefaultRecipientDomain)
        public string DefaultRecipientDomain { get; set; } = "sintergy.net";
        public string? OverrideAllTo { get; set; }
    }

    public sealed class PrinterOptions
    {
        // Add more named printers later as needed
        public string Maintenance { get; set; } = "Maintenance";
        /// <summary>
        /// Full path to SumatraPDF.exe
        /// e.g., C:\Program Files\SumatraPDF\SumatraPDF.exe
        /// </summary>
        public string SumatraExePath { get; set; } = "";

        /// <summary>
        /// If true, the app will fail fast at startup when Sumatra cannot be found.
        /// </summary>
        public bool ValidateOnStart { get; set; } = true;
    }
}
