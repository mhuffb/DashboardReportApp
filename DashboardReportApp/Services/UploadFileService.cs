namespace DashboardReportApp.Services
{
    public class UploadFileService
    {
        private readonly ILogger<UploadFileService> _logger;

        // The UNC path where files will be saved.
        private readonly string _uploadDirectory = @"\\SINTERGYDC2024\Vol1\Visual Studio Programs\VSP\Uploads";

        public UploadFileService(ILogger<UploadFileService> logger)
        {
            _logger = logger;
        }

        /// <summary>
        /// Uploads a file to the UNC share using a custom filename pattern: {name}{id}{extension}.
        /// </summary>
        /// <param name="file">The file to upload (from an HTTP form, for example).</param>
        /// <param name="namePrefix">The name/prefix part of the file name, e.g. "MaintenanceRequest".</param>
        /// <param name="id">The numeric ID to append, e.g. 335.</param>
        /// <returns>Returns the full path of the uploaded file.</returns>
        /// <exception cref="ArgumentException">Thrown if file is null or has zero length.</exception>
        /// <exception cref="IOException">Thrown if there is an I/O problem during file copy.</exception>
        public async Task<string> UploadFileAsync(IFormFile file, string namePrefix, int id)
        {
            if (file == null || file.Length == 0)
            {
                throw new ArgumentException("The file is null or empty.", nameof(file));
            }

            // Extract the file extension from the original file.
            string extension = Path.GetExtension(file.FileName);

            // Build final file name, e.g. "MaintenanceRequest335.pdf"
            string finalFileName = $"{namePrefix}{id}{extension}";

            // Combine with the UNC directory path
            string fullPath = Path.Combine(_uploadDirectory, finalFileName);

            try
            {
                // Make sure the directory exists (optional if you know it's already there).
                if (!Directory.Exists(_uploadDirectory))
                {
                    Directory.CreateDirectory(_uploadDirectory);
                }

                // Copy the file to the destination
                using var stream = new FileStream(fullPath, FileMode.Create);
                await file.CopyToAsync(stream);

                _logger.LogInformation("File uploaded successfully to {Destination}", fullPath);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error uploading file {FileName} to {Destination}", file.FileName, fullPath);
                throw; // Re-throw so the caller can handle it (display error, etc.)
            }

            return fullPath;
        }
    }
}
