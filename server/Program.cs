using System.Text;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Server.Data;
using Server.Hubs;
using Server.Models;
using Server.Security;

var builder = WebApplication.CreateBuilder(args);

const string AngularDevClient = "AngularDevClient";

// Add services to the container.

builder.Services.AddControllers()
    .AddJsonOptions(options => options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();
builder.Services.AddCors(options =>
{
    options.AddPolicy(AngularDevClient, policy =>
        // :4300 is the client-verify config in .claude/launch.json, used to
        // run a second local session side by side with :4200 for manual
        // testing (e.g. two roles at once).
        policy.WithOrigins("http://localhost:4200", "http://127.0.0.1:4200", "http://localhost:4300", "http://127.0.0.1:4300")
              .AllowAnyHeader()
              .AllowAnyMethod());
});

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("Default")));

builder.Services.AddScoped<TokenService>();

builder.Services.AddSignalR();
builder.Services.AddSingleton<IScheduleNotifier, ScheduleNotifier>();

var jwtSection = builder.Configuration.GetSection("Jwt");
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = jwtSection["Issuer"],
            ValidateAudience = true,
            ValidAudience = jwtSection["Audience"],
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSection["Key"]!)),
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromMinutes(1),
        };
    });

builder.Services.AddAuthorizationBuilder()
    .AddPolicy("SaOnly", policy => policy.RequireRole(nameof(AccountRole.Sa)))
    .AddPolicy("AdminOrAbove", policy => policy.RequireRole(nameof(AccountRole.Sa), nameof(AccountRole.Admin)))
    .AddPolicy("LeadOrAbove", policy => policy.RequireRole(nameof(AccountRole.Sa), nameof(AccountRole.Admin), nameof(AccountRole.Lead)));

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.Migrate();
    SeedData.EnsureSaAccount(db);
}

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.UseCors(AngularDevClient);
}

app.UseHttpsRedirection();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapHub<ScheduleHub>("/hubs/schedule");

app.Run();
