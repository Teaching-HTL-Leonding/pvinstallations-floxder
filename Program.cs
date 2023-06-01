using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using FluentValidation;
using FluentValidation.Results;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddDbContext<PvDbContext>(options => options.UseSqlServer(builder.Configuration["ConnectionStrings:DefaultConnection"]));
builder.Services.AddScoped<PostPvInstallationDtoValidator>();
builder.Services.AddScoped<PostProductionReportDtoValidator>();

var app = builder.Build();

app.MapGet("/ping", () => "pong");

app.MapPost("/installations", async (PostPvInstallationDto postPvInstallationDto, PvDbContext context, PostPvInstallationDtoValidator validator) =>
{
    ValidationResult validationResult = await validator.ValidateAsync(postPvInstallationDto);

    if (!validationResult.IsValid)
    {
        return Results.ValidationProblem(validationResult.ToDictionary());
    }

    var pvInstallation = new PvInstallation
    {
        Longitude = postPvInstallationDto.Longitude,
        Latitude = postPvInstallationDto.Latitude,
        Address = postPvInstallationDto.Address,
        OwnerName = postPvInstallationDto.OwnerName,
        IsActive = true,
        Comments = postPvInstallationDto.Comments,
    };

    await context.PvInstallations.AddAsync(pvInstallation);
    await context.SaveChangesAsync();

    await context.InstallationLogs.AddAsync(new()
    {
        Action = "created",
        Timestamp = DateTime.UtcNow,
        NextValue = pvInstallation.ToString(),
        PvInstallationID = pvInstallation.ID,
    });
    await context.SaveChangesAsync();

    return Results.Json(pvInstallation, statusCode: StatusCodes.Status201Created);
});

app.MapPost("/installations/{id}/deactivate", async (int id, PvDbContext context) =>
{
    var pvInstallation = await context.PvInstallations.FindAsync(id);

    if (pvInstallation == null)
    {
        return Results.BadRequest();
    }

    await context.InstallationLogs.AddAsync(new()
    {
        Action = "updated",
        Timestamp = DateTime.UtcNow,
        PreviousValue = pvInstallation.IsActive.ToString(),
        NextValue = true.ToString(),
        PvInstallationID = pvInstallation.ID,
    });

    pvInstallation.IsActive = false;
    await context.SaveChangesAsync();

    return Results.Ok(pvInstallation);
});

app.MapPost("/installations/{id}/reports", async (int id, PostProductionReportDto postProductionReportDto, PvDbContext context, PostProductionReportDtoValidator validator) =>
{
    ValidationResult validationResult = await validator.ValidateAsync(postProductionReportDto);

    if (!validationResult.IsValid)
    {
        return Results.ValidationProblem(validationResult.ToDictionary());
    }

    if (await context.PvInstallations.FindAsync(id) == null)
    {
        return Results.NotFound();
    }

    var productionReport = new ProductionReport()
    {
        BatteryWattage = postProductionReportDto.BatteryWattage,
        GridWattage = postProductionReportDto.GridWattage,
        HouseholdWattage = postProductionReportDto.HouseholdWattage,
        ProducedWattage = postProductionReportDto.ProducedWattage,
        PvInstallationID = id,
        Timestamp = DateTime.UtcNow,
    };

    await context.ProductionReports.AddAsync(productionReport);
    await context.SaveChangesAsync();

    return Results.Json(productionReport, statusCode: StatusCodes.Status201Created);
});

app.MapGet("/installations/{id}/reports", async (int id, DateTime timestamp, int duration, PvDbContext context) =>
{
    var to = timestamp.AddMinutes(duration);
    var pvInstallation = await context.PvInstallations.FirstAsync(v => v.ID == id);

    if (pvInstallation == null)
    {
        return Results.NotFound();
    }

    var totalProducedWattage = await context.ProductionReports.
        Where(v => v.PvInstallationID == id && v.Timestamp >= timestamp && v.Timestamp <= to).
        SumAsync(v => v.ProducedWattage);

    return Results.Ok(new { totalProducedWattage });
});

app.MapGet("/installations/{id}/timeline", async (int id, DateTime startTimestamp, int duration, int page, PvDbContext context) =>
{
    if (page < 1) { return Results.BadRequest("`page` number must be greater than 0"); }
    if (duration < 1) { return Results.BadRequest("`duration` must be greater than 0"); }

    // number of elements in the current page
    var nrOfElements = Math.Min(60, duration - ((page - 1) * 60));

    if (nrOfElements <= 0)
    {
        return Results.BadRequest("this page contains no elements");
    }

    DateTime fromTimestamp = startTimestamp.AddHours(page - 1);
    DateTime toTimestamp = fromTimestamp.AddMinutes(nrOfElements);
    var reportsPerMinute = await context.ProductionReports.
        Where(v => v.PvInstallationID == id && v.Timestamp >= fromTimestamp && v.Timestamp < toTimestamp).
        OrderBy(v => v.Timestamp).
        ToListAsync();

    var timeline = new TimelineItem[nrOfElements];

    // initialize array
    for (int i = 0; i < timeline.Length; i++)
    {
        timeline[i] = new();
    }

    foreach (var item in reportsPerMinute)
    {
        var totalMinutes = (int)(item.Timestamp - fromTimestamp).TotalMinutes;
        timeline[totalMinutes].BatteryWattage += item.BatteryWattage;
        timeline[totalMinutes].GridWattage += item.GridWattage;
        timeline[totalMinutes].HouseholdWattage += item.HouseholdWattage;
        timeline[totalMinutes].ProducedWattage += item.ProducedWattage;
    }

    return Results.Ok(timeline);
});

app.Run();

record PostPvInstallationDto(float Longitude, float Latitude, string Address, string OwnerName, string? Comments);
record PostProductionReportDto(float ProducedWattage, float HouseholdWattage, float BatteryWattage, float GridWattage);

class TimelineItem
{
    public float ProducedWattage { get; set; }
    public float HouseholdWattage { get; set; }
    public float BatteryWattage { get; set; }
    public float GridWattage { get; set; }
}

class PvDbContext : DbContext
{
    public PvDbContext(DbContextOptions<PvDbContext> options) : base(options) { }

    public DbSet<PvInstallation> PvInstallations => Set<PvInstallation>();
    public DbSet<ProductionReport> ProductionReports => Set<ProductionReport>();
    public DbSet<InstallationLog> InstallationLogs => Set<InstallationLog>();
}

public class PvInstallation
{
    public int ID { get; set; }
    public float Longitude { get; set; }
    public float Latitude { get; set; }
    public string Address { get; set; } = "";
    public string OwnerName { get; set; } = "";
    public bool IsActive { get; set; }
    public string? Comments { get; set; } = "";

    [JsonIgnore]
    public List<ProductionReport> ProductionReports { get; set; } = new();

    public override string ToString()
    {
        return string.Format("PvInstallation: {0}/{1}, {2}, {3}, {4}, {5}", Longitude, Latitude, Address, OwnerName, IsActive, Comments);
    }
}

public class ProductionReport
{
    public int ID { get; set; }  // automatically generated when named ID
    public DateTime Timestamp { get; set; }
    public float ProducedWattage { get; set; }
    public float HouseholdWattage { get; set; }
    public float BatteryWattage { get; set; }
    public float GridWattage { get; set; }
    public int PvInstallationID { get; set; }  // FK

    [JsonIgnore]
    public PvInstallation? PvInstallation { get; set; }  // object (from FK)
}

class InstallationLog
{
    public int ID { get; set; }
    public DateTime Timestamp { get; set; }
    public string Action { get; set; } = "";
    public string PreviousValue { get; set; } = "";
    public string NextValue { get; set; } = "";
    public int PvInstallationID { get; set; }

    [JsonIgnore]
    public PvInstallation? PvInstallation { get; set; }

}

class PostPvInstallationDtoValidator : AbstractValidator<PostPvInstallationDto>
{
    public PostPvInstallationDtoValidator()
    {
        RuleFor(v => v.Longitude).InclusiveBetween(-180, 180);
        RuleFor(v => v.Latitude).InclusiveBetween(-90, 90);
        RuleFor(v => v.Address).NotEmpty().MaximumLength(1024);
        RuleFor(v => v.OwnerName).NotEmpty().MaximumLength(512);
        RuleFor(v => v.Comments).MaximumLength(1024);
    }
}

class PostProductionReportDtoValidator : AbstractValidator<PostProductionReportDto>
{
    public PostProductionReportDtoValidator()
    {
        RuleFor(v => v.ProducedWattage).GreaterThanOrEqualTo(0f);
        RuleFor(v => v.HouseholdWattage).GreaterThanOrEqualTo(0f);
        RuleFor(v => v.BatteryWattage).GreaterThanOrEqualTo(0f);
        RuleFor(v => v.GridWattage).GreaterThanOrEqualTo(0f);
    }
}
