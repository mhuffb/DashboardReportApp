using DashboardReportApp.Services;
using Serilog;

Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Debug() // Capture detailed logs
    .WriteTo.Console() // Logs to the console
    .WriteTo.File("Logs/service-log-.txt", rollingInterval: RollingInterval.Day) // Logs to file
    .CreateLogger();

try
{
    Log.Information("Starting up the application...");

    var builder = WebApplication.CreateBuilder(args);

    builder.WebHost.ConfigureKestrel(options =>
    {
        options.ListenAnyIP(5000); // HTTP
        // HTTPS can be re-enabled if needed:
        // options.ListenAnyIP(5001, listenOptions => listenOptions.UseHttps());
    });

    // Add services to the container
    builder.Services.AddControllersWithViews();
    builder.Services.AddControllers();
    builder.Services.AddScoped<PressMixBagChangeService>();
    // Register the PressRunLogService
    builder.Services.AddScoped<PressRunLogService>();
    // Register SinteringService
    builder.Services.AddTransient<SinterRunLogService>();
    builder.Services.AddSingleton<IConfiguration>(builder.Configuration);
    builder.Services.AddTransient<ScheduleService>();
    builder.Services.AddScoped<ISecondaryRunLogService, SecondaryRunLogService>();
    builder.Services.AddScoped<ISecondarySetupLogService, SecondarySetupLogService>();
    builder.Services.AddScoped<MaintenanceRequestService>();
    builder.Services.AddScoped<EmailAttachmentService>();
    //builder.Services.AddHostedService<EmailProcessingBackgroundService>();

    builder.Services.AddScoped<DashboardReportApp.Services.HoldTagService>();





    // Use Serilog for logging
    builder.Host.UseSerilog();

    var app = builder.Build();

    // Configure the HTTP request pipeline
    if (!app.Environment.IsDevelopment())
    {
        app.UseExceptionHandler("/Home/Error");
        app.UseHsts();
    }

    app.UseHttpsRedirection();
    app.UseStaticFiles();

    app.UseRouting();
    app.UseAuthorization();

    app.MapControllerRoute(
        name: "default",
        pattern: "{controller=Home}/{action=Index}/{id?}");

    Log.Information("Application startup completed successfully.");

    app.UseEndpoints(endpoints =>
    {
        endpoints.MapControllerRoute(
            name: "default",
            pattern: "{controller=Home}/{action=Index}/{id?}");
    });

    app.Run();
}
catch (Exception ex)
{
    Log.Fatal(ex, "Application terminated unexpectedly during startup.");
}
finally
{
    Log.CloseAndFlush(); // Ensure all logs are written to the file before exiting
}
