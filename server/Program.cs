using System.Text;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Server.Data;
using Server.Hubs;
using Server.Models;
using Server.Security;
using Server.Services;

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
        // Dev-only: employees reach the Angular dev server from many
        // different LAN devices/hostnames on port 4200 (or 4300, used by
        // .claude/launch.json for a second side-by-side session), so the
        // origin can't be pinned to a fixed list. Auth is a bearer token
        // (not cookies), so allowing any origin here doesn't expose
        // credentialed requests.
        policy.SetIsOriginAllowed(origin =>
                  Uri.TryCreate(origin, UriKind.Absolute, out var uri) &&
                  (uri.Port == 4200 || uri.Port == 4300))
              .AllowAnyHeader()
              .AllowAnyMethod());
});

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("Default")));

builder.Services.AddScoped<TokenService>();

// Keys live outside publish/wwwroot (both get rm -rf'd on every
// publish-win.sh rebuild — see that script) so encrypted SSNs stay readable
// across redeploys. LocalApplicationData is writable without elevation and
// is per-machine, which matches this app's single-process LAN deployment.
var dataProtectionKeysPath = builder.Configuration["Storage:DataProtectionKeysPath"]
    ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TAT", "keys");
builder.Services.AddDataProtection()
    .PersistKeysToFileSystem(new DirectoryInfo(dataProtectionKeysPath))
    .SetApplicationName("TAT");
builder.Services.AddSingleton<SsnProtector>();

builder.Services.AddSignalR();
builder.Services.AddSingleton<IScheduleNotifier, ScheduleNotifier>();
builder.Services.AddSingleton<IEmailSender, SmtpEmailSender>();

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

// No app.UseHttpsRedirection(): this deploys over plain HTTP on the
// internal LAN only, with no certificate configured, so that middleware
// would just fail to find an HTTPS port and warn on every request.

// Serves the built Angular app (client/dist/client/browser, copied into
// wwwroot at publish time — see publish-win.sh) so a single process/port
// can host both the API and the UI for LAN deployment. In local dev,
// wwwroot doesn't exist, so these no-op and the Angular dev server on
// :4200 is used instead.
app.UseDefaultFiles();
app.UseStaticFiles();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapHub<ScheduleHub>("/hubs/schedule");
app.MapFallbackToFile("index.html");

app.Run();
