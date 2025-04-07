using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json;
using Serilog;
using VSRAdminAPI.Middleware;
using VSRAdminAPI.Model;
using VSRAdminAPI.Model.Common;
using VSRAdminAPI.Repository;
using VSRAdminAPI.Services;

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

    // Use Serilog
    builder.Host.UseSerilog();

    // Configuration
    builder.Configuration.AddJsonFile("appsettings.json", optional: false, reloadOnChange: true);
    builder.Configuration.AddEnvironmentVariables();

    // Services
    builder.Services.AddControllers();
    builder.Services.AddSingleton<IConfiguration>(builder.Configuration);

    builder.Services.AddScoped<ICompanyService, CompanyService>();
    builder.Services.AddScoped<ICompanyRepository, CompanyRepository>();
    builder.Services.AddScoped<ICustomerService, CustomerService>();
    builder.Services.AddScoped<ICustomerRepository, CustomerRepository>();
    builder.Services.AddScoped<IRestaurantInstructionService, RestaurantInstructionService>();
    builder.Services.AddScoped<IRestaurantInstructions, RestaurantInstructions>();

    // CORS
    builder.Services.AddCors(options =>
    {
        options.AddPolicy("AllowAll", policy =>
        {
            policy.AllowAnyOrigin()
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

    // ✅ PORT binding via env/config
    string port = Environment.GetEnvironmentVariable("PORT") ?? builder.Configuration["AppPort"] ?? "8080";

    builder.WebHost.ConfigureKestrel(options =>
    {
        options.ListenAnyIP(int.Parse(port));
    });

    var app = builder.Build();

    // Middleware
    app.UseSerilogRequestLogging();
    app.UseCors("AllowAll");
    app.UseMiddleware<ErrorHandlerMiddleware>();

    app.UseHttpsRedirection();
    app.UseAuthorization();

    if (app.Environment.IsDevelopment())
    {
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

    // Health check
    app.MapGet("/", () => Results.Ok("Hello from VSRAdminAPI!"));

    // Login
    app.MapPost("/api/ValidateLogin", ([FromBody] LoginValues loginvalues, [FromServices] ICompanyService companyService) =>
    {
        if (loginvalues == null)
        {
            Log.Warning("Empty login values received");
            return Results.BadRequest("Login values are required");
        }

        var genericResponse = companyService.ValidateLogin(loginvalues);
        return Results.Ok(genericResponse);
    }).WithTags("Login").Produces<GenericResponse>(200).Produces(400);

    // Add Restaurant
    app.MapPost("/api/Restaurant", async (HttpRequest request,
        [FromServices] ICompanyService companyService,
        [FromServices] ILogger<Program> logger) =>
    {
        try
        {
            var form = await request.ReadFormAsync();
            var customerdata = form["customerdata"].FirstOrDefault();
            var file = form.Files.FirstOrDefault();

            if (string.IsNullOrWhiteSpace(customerdata))
            {
                logger.LogWarning("Customer data is empty");
                return Results.BadRequest(new { Message = "Customer data is required." });
            }

            MasterCustomer objContact = JsonConvert.DeserializeObject<MasterCustomer>(customerdata)
                ?? throw new InvalidOperationException("Deserialization returned null");

            if (file != null)
            {
                var directory = "/app/restaurantlogo";
                Directory.CreateDirectory(directory);

                var filePath = Path.Combine(directory, $"{objContact.DID}.jpg");
                await using var fileStream = new FileStream(filePath, FileMode.Create);
                await file.CopyToAsync(fileStream);
            }

            var customerFileData = new CustomerFileData
            {
                Customerdata = customerdata,
                Filename = file
            };

            GenericResponse response = companyService.AddCompany(customerFileData);
            return Results.Ok(response);
        }
        catch (JsonException ex)
        {
            logger.LogError(ex, "Invalid JSON format");
            return Results.BadRequest(new { Message = "Invalid customer data format." });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unhandled error in AddCompany");
            return Results.Problem($"An unexpected error occurred: {ex.Message}", statusCode: 500);
        }
    }).WithTags("Restaurant")
      .Accepts<CustomerFileData>("multipart/form-data")
      .Produces<GenericResponse>(200)
      .Produces(400)
      .Produces(500);

    // Load Restaurant
    app.MapGet("/api/Restaurant", ([FromQuery] string? search, [FromQuery] int pageno,
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
    }).WithTags("Restaurant")
      .Produces<GenericResponse>(200)
      .Produces(400)
      .Produces(500);

    // Add Instruction
    app.MapPost("/api/Instruction", ([FromBody] ReqInput reqInput,
        [FromServices] IRestaurantInstructionService restaurantInstructionService) =>
    {
        if (reqInput == null)
        {
            return Results.BadRequest("Request input is required");
        }

        var genericResponse = restaurantInstructionService.AddInstruction(reqInput);
        return Results.Ok(genericResponse);
    }).WithTags("Restaurant").Produces<GenericResponse>(200).Produces(400);

    // Load Instruction
    app.MapGet("/api/Instruction", (int customerid,
        [FromServices] IRestaurantInstructionService restaurantInstructionService) =>
    {
        if (customerid <= 0)
        {
            return Results.BadRequest("Valid customer ID is required");
        }

        var genericResponse = restaurantInstructionService.LoadInstruction(customerid);
        return Results.Ok(genericResponse);
    }).WithTags("Restaurant").Produces<GenericResponse>(200).Produces(400);

    // Customer Info
    app.MapPost("/api/CustomerInfo", ([FromBody] CustomerInfo addcustomerinfo,
        [FromServices] ICustomerService customerService,
        [FromServices] ILogger<Program> logger) =>
    {
        try
        {
            if (addcustomerinfo == null)
            {
                return Results.BadRequest("Customer info is required");
            }

            GenericResponse genericResponse = customerService.Customerinfo(addcustomerinfo);
            return Results.Ok(genericResponse);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error in CustomerInfo");
            return Results.Problem($"An error occurred: {ex.Message}", statusCode: 500);
        }
    }).WithTags("CustomerInfo").Produces<GenericResponse>(200).Produces(400).Produces(500);

    // Global Error Fallback
    app.Map("/error", () => Results.Problem("An error occurred", statusCode: 500));

    Log.Information("Environment: {Environment}", app.Environment.EnvironmentName);
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
