using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using PanasonicPrototype.Api.Data;
using PanasonicPrototype.Api.Services;

var builder = WebApplication.CreateBuilder(args);

// Render (and most container hosts) assign a dynamic port via $PORT and expect
// the app to bind to 0.0.0.0 on it - there's no fixed port to hardcode.
var port = Environment.GetEnvironmentVariable("PORT");
if (!string.IsNullOrEmpty(port))
{
    builder.WebHost.UseUrls($"http://0.0.0.0:{port}");
}

// Add services to the container.
// Every endpoint requires authentication by default; use [AllowAnonymous] to opt out (login only).
builder.Services.AddControllers(options =>
{
    options.Filters.Add(new AuthorizeFilter());
});
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddHttpContextAccessor();

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("Default") ?? "Data Source=panasonic-prototype.db"));

builder.Services.AddScoped<BudgetApprovalService>();
builder.Services.AddScoped<DiscountApprovalService>();
builder.Services.AddScoped<BudgetPlanningService>();
builder.Services.AddScoped<GlobalPlanningService>();
builder.Services.AddScoped<DashboardService>();
builder.Services.AddScoped<ReportExportService>();
builder.Services.AddScoped<JwtTokenService>();
builder.Services.AddScoped<CurrentUserContext>();

var jwtKey = builder.Configuration["Jwt:Key"]!;
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = builder.Configuration["Jwt:Issuer"],
            ValidAudience = builder.Configuration["Jwt:Audience"],
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey))
        };
    });
builder.Services.AddAuthorization();

// Comma-separated list of additional allowed origins (e.g. the deployed
// Vercel frontend URL) - set via the ALLOWED_ORIGINS env var on the host.
// localhost:4200 is always allowed for local development.
var extraOrigins = Environment.GetEnvironmentVariable("ALLOWED_ORIGINS")
    ?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
    ?? Array.Empty<string>();
var allowedOrigins = new[] { "http://localhost:4200" }.Concat(extraOrigins).ToArray();

const string AngularDevClient = "AngularDevClient";
builder.Services.AddCors(options =>
{
    options.AddPolicy(AngularDevClient, policy =>
        policy.WithOrigins(allowedOrigins)
            .AllowAnyHeader()
            .AllowAnyMethod());
});

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseCors(AngularDevClient);
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    var approvalService = scope.ServiceProvider.GetRequiredService<BudgetApprovalService>();
    var budgetPlanningService = scope.ServiceProvider.GetRequiredService<BudgetPlanningService>();
    db.Database.EnsureCreated();
    await DbSeeder.SeedAsync(db, approvalService, budgetPlanningService);
}

app.Run();
