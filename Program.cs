using Microsoft.EntityFrameworkCore;
using Microsoft.OpenApi.Models;

var builder = WebApplication.CreateBuilder(args);

// Port binding for Kestrel on AWS
builder.WebHost.UseUrls("http://0.0.0.0:8080");

// 1. Add Services
builder.Services.AddDbContext<AppDbContext>(options => 
    options.UseSqlite("Data Source=Tasks.db"));
    var connectionString = builder.Configuration.GetConnectionString("MyDbConnection");
builder.Services.AddDbContext<AppDbContext>(options => 
    options.UseNpgsql(connectionString));

builder.Services.AddEndpointsApiExplorer();

// Configure Swagger to handle our API Key
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo { Title = "My Secure AWS API", Version = "v1" });
    c.AddSecurityDefinition("ApiKey", new OpenApiSecurityScheme
    {
        Description = "Enter your API Key (e.g., MySecretKey123)",
        Name = "X-Api-Key",
        In = ParameterLocation.Header,
        Type = SecuritySchemeType.ApiKey
    });

    c.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "ApiKey" }
            },
            new string[] { }
        }
    });
});

var app = builder.Build();

// 2. Auto-Create Database on Startup
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.EnsureCreated();
}

// 3. SECURE MIDDLEWARE (Reading from Configuration)
app.Use(async (context, next) =>
{
    // Bypass security for Swagger and Root
    if (context.Request.Path.StartsWithSegments("/swagger") || context.Request.Path == "/")
    {
        await next();
        return;
    }

    // GET THE KEY FROM appsettings.json
    var apiKeyFromConfig = app.Configuration.GetValue<string>("SecuritySettings:ApiKey");

    if (!context.Request.Headers.TryGetValue("X-Api-Key", out var extractedApiKey) || 
        apiKeyFromConfig != extractedApiKey)
    {
        context.Response.StatusCode = 401;
        await context.Response.WriteAsync("Unauthorized: Invalid or Missing API Key.");
        return;
    }

    await next();
});

// 4. Standard Middlewares
app.UseSwagger();
app.UseSwaggerUI();

// 5. Endpoints
app.MapGet("/", () => "Tasks API is Live and Secure!");
app.MapGet("/tasks", async (AppDbContext db) => await db.Tasks.ToListAsync());
app.MapPost("/tasks", async (AppDbContext db, TodoTask task) => {
    db.Tasks.Add(task);
    await db.SaveChangesAsync();
    return Results.Created($"/tasks/{task.Id}", task);
});

app.MapPut("/tasks/{id}", async (int id, TodoTask inputTask, AppDbContext db) =>
{
    var task = await db.Tasks.FindAsync(id);
    if (task is null) return Results.NotFound();
    task.Title = inputTask.Title;
    task.IsCompleted = inputTask.IsCompleted;
    await db.SaveChangesAsync();
    return Results.NoContent();
});

app.MapDelete("/tasks/{id}", async (int id, AppDbContext db) =>
{
    if (await db.Tasks.FindAsync(id) is TodoTask task)
    {
        db.Tasks.Remove(task);
        await db.SaveChangesAsync();
        return Results.Ok(task);
    }
    return Results.NotFound();
});

app.Run();

// 6. Data Classes
public class TodoTask {
    public int Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public bool IsCompleted { get; set; }
}

public class AppDbContext : DbContext {
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }
    public DbSet<TodoTask> Tasks => Set<TodoTask>();
}