using DashboardReportApp.Services;
using Serilog;
using Microsoft.AspNetCore.Authentication.Negotiate;

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
         options.ListenAnyIP(5001, listenOptions => listenOptions.UseHttps());
    });

    // Add services to the container
    builder.Services.AddControllersWithViews();

   

    builder.Services.AddControllers();
    builder.Services.AddSingleton<IConfiguration>(builder.Configuration);
    builder.Services.AddScoped<PressMixBagChangeService>();
    builder.Services.AddScoped<PressRunLogService>();
    builder.Services.AddScoped<SinterRunLogService>();
    builder.Services.AddScoped<ScheduleService>();
    builder.Services.AddScoped<SecondaryRunLogService>();
    builder.Services.AddScoped<SecondarySetupLogService>();
    builder.Services.AddScoped<MaintenanceRequestService>();
    builder.Services.AddScoped<HoldTagService>();
    builder.Services.AddScoped<DeviationService>();
    builder.Services.AddScoped<QCSecondaryHoldReturnService>();
    builder.Services.AddScoped<ToolingHistoryService>();
    builder.Services.AddScoped<ProcessChangeRequestService>();
    builder.Services.AddScoped<MaintenanceAdminService>();
    builder.Services.AddScoped<AdminDeviationService>();
    builder.Services.AddScoped<PressSetupService>();
    builder.Services.AddScoped<AdminHoldTagService>();
    builder.Services.AddScoped<MoldingService>();
    builder.Services.AddScoped<AdminProcessChangeRequestService>();
    builder.Services.AddScoped<SharedService>();
    builder.Services.AddScoped<AssemblyService>();
    builder.Services.AddScoped<ProlinkService>();
    builder.Services.AddScoped<CalendarService>();
    builder.Services.AddHttpContextAccessor();

    // Add session services
    builder.Services.AddSession(options =>
    {
        options.IdleTimeout = TimeSpan.FromMinutes(20); // Set session timeout
        options.Cookie.HttpOnly = true; // Make the session cookie HTTP-only
        options.Cookie.IsEssential = true; // Mark the session cookie as essential
    });


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

    app.UseAuthentication();
    app.UseAuthorization();
    app.UseSession(); // Enable session middleware
    app.MapControllerRoute(
        name: "default",
        pattern: "{controller=Home}/{action=Index}/{id?}");


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
