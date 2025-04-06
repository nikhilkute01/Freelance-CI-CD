using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json;
using Serilog;
using VSRAdminAPI.Services;
using VSRAdminAPI.Model;
using VSRAdminAPI.Model.Common;
using VSRAdminAPI.Repository;
using VSRAdminAPI.Middleware;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using System.Text.Json;

// Configure Serilog logger
Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Debug()
    .WriteTo.Console()
    .WriteTo.File("logs/VSRAdminAPI-.log", rollingInterval: RollingInterval.Day)
    .CreateLogger();

try
{
    Log.Information("Starting web host");

    var builder = WebApplication.CreateBuilder(args);

    // 1. Fix for Azure App Service port configuration
    builder.WebHost.ConfigureKestrel(serverOptions =>
    {
        serverOptions.ListenAnyIP(80); // Azure Linux uses port 8080
    });

    // Configuration
    builder.Configuration.AddJsonFile("appsettings.json", optional: false, reloadOnChange: true);
    builder.Configuration.AddEnvironmentVariables();

    // Logging
    builder.Host.UseSerilog();

    // 2. Add Health Checks for Azure probes
    builder.Services.AddHealthChecks()
        .AddCheck("self", () => HealthCheckResult.Healthy());

    // CORS
    builder.Services.AddCors(options =>
    {
        options.AddPolicy("AllowAll", builder =>
        {
            builder.AllowAnyOrigin()
                   .AllowAnyMethod()
                   .AllowAnyHeader();
        });
    });

    // Swagger
    builder.Services.AddEndpointsApiExplorer();
    builder.Services.AddSwaggerGen(c =>
    {
        c.SwaggerDoc("v1", new() { Title = "VSRAdmin API", Version = "v1" });
    });

    // Application Services
    builder.Services.AddScoped<ICompanyService, CompanyService>();
    builder.Services.AddScoped<ICompanyRepository, CompanyRepository>();
    builder.Services.AddScoped<ICustomerService, CustomerService>();
    builder.Services.AddScoped<ICustomerRepository, CustomerRepository>();
    builder.Services.AddScoped<IRestaurantInstructionService, RestaurantInstructionService>();
    builder.Services.AddScoped<IRestaurantInstructions, RestaurantInstructions>();

    var app = builder.Build();

    // Middleware Pipeline
    app.UseSerilogRequestLogging();
    app.UseCors("AllowAll");

    // 3. Fix routing configuration - this must come before endpoint mapping
    app.UseRouting();

    if (app.Environment.IsDevelopment())
    {
        app.UseDeveloperExceptionPage();
        app.UseSwagger();
        app.UseSwaggerUI(c =>
        {
            c.SwaggerEndpoint("/swagger/v1/swagger.json", "VSRAdmin API v1");
            c.RoutePrefix = "swagger";
        });
    }
    else
    {
        app.UseExceptionHandler("/error");
        app.UseHsts();
    }

    // Only use HTTPS redirection in production
    if (!app.Environment.IsDevelopment())
    {
        app.UseHttpsRedirection();
    }

    app.UseMiddleware<ErrorHandlerMiddleware>();

    // 4. Health check endpoints for Azure probes
    app.MapHealthChecks("/health", new HealthCheckOptions
    {
        ResponseWriter = async (context, report) =>
        {
            context.Response.ContentType = "application/json";
            var response = new
            {
                Status = report.Status.ToString(),
                Checks = report.Entries.Select(e => new
                {
                    Name = e.Key,
                    Status = e.Value.Status.ToString(),
                    Description = e.Value.Description
                }),
                Duration = report.TotalDuration
            };
            await context.Response.WriteAsync(JsonSerializer.Serialize(response));
        }
    });

    app.MapGet("/ping", () => "pong");

    // 5. Fix root endpoint with explicit routing
    app.MapGet("/", () => new
    {
        Status = "API is running",
        Environment = app.Environment.EnvironmentName,
        DateTime = DateTime.UtcNow,
        Version = "1.0"
    }).WithTags("Health").Produces(200);

    // 6. Fix Restaurant API with default parameter values
    app.MapGet("/api/Restaurant", (
        [FromQuery] string? search = "", 
        [FromQuery] int pageno = 1,
        [FromServices] ICompanyService companyService,
        [FromServices] ILogger<Program> logger) =>
    {
        try
        {
            if (pageno <= 0)
            {
                return Results.BadRequest("Page number must be greater than 0");
            }

            var companySearch = new CompanySearch
            {
                Search = search ?? string.Empty,
                Pageno = pageno
            };

            var genericResponse = companyService.LoadCompany(companySearch);
            return Results.Ok(genericResponse);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error in LoadCompany");
            return Results.Problem($"An error occurred: {ex.Message}", statusCode: 500);
        }
    })
    .WithTags("Restaurant")
    .Produces<GenericResponse>(200)
    .Produces(400)
    .Produces(500);

    // [Keep all your other endpoints exactly as they were]

    // 7. Add endpoint for Azure's health probe
    app.MapGet("/admin/host/status", () => new { Status = "Healthy" });

    // Global error handler
    app.Map("/error", () => Results.Problem("An error occurred", statusCode: 500));

    // 8. Important: This must come after all endpoint mappings
    app.UseEndpoints(endpoints => { });

    Log.Information("Application starting up...");
    Log.Information("Environment: {Environment}", app.Environment.EnvironmentName);
    Log.Information("Listening on port 8080");

    app.Run();
}
catch (Exception ex)
{
    Log.Fatal(ex, "Application terminated unexpectedly");
}
finally
{
    Log.CloseAndFlush();
}