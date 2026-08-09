using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Server.Data;

namespace Server.Controllers;

// Local-dev convenience only: pulls the live Azure database down over
// Kudu's VFS API and swaps it in for the local one, so an admin working on
// this machine can test against real data instead of the seeded dev set.
// Hard-gated on IsDevelopment() below so the route doesn't exist — let
// alone run — once actually deployed. Credentials are never stored: each
// call fetches fresh Kudu publish credentials from Azure via the `az` CLI,
// which must already be installed and logged in (`az login`) on this
// machine — see appsettings.Development.json for the target app/resource
// group (AzureSync section).
//
// AdminOrAbove rather than SaOnly: the button lives on the per-location
// admin settings page, which adminGuard restricts to Admin/Lead — Sa can
// never reach it (see client/src/app/core/guards.ts) — so SaOnly would
// make this permanently unreachable through the UI. The real access
// control here is IsDevelopment() plus the operator's own `az login`
// session, not the app role.
[ApiController]
[Route("api/dev-tools")]
[Authorize(Policy = "AdminOrAbove")]
public class DevToolsController(AppDbContext db, IConfiguration config, IWebHostEnvironment env, IHttpClientFactory httpClientFactory) : ControllerBase
{
    [HttpPost("sync-db-from-live")]
    public async Task<IActionResult> SyncDbFromLive(CancellationToken ct)
    {
        if (!env.IsDevelopment())
        {
            return NotFound();
        }

        var webAppName = config["AzureSync:WebAppName"];
        var resourceGroup = config["AzureSync:ResourceGroup"];
        var remoteDbPath = config["AzureSync:RemoteDbPath"] ?? "data/tat.db";
        if (string.IsNullOrWhiteSpace(webAppName) || string.IsNullOrWhiteSpace(resourceGroup))
        {
            return BadRequest("AzureSync:WebAppName / AzureSync:ResourceGroup aren't configured — see appsettings.Development.json.");
        }

        (string Username, string Password) credentials;
        try
        {
            credentials = await GetPublishingCredentialsAsync(webAppName, resourceGroup, ct);
        }
        catch (Exception ex)
        {
            return StatusCode(StatusCodes.Status502BadGateway,
                $"Couldn't fetch Azure publishing credentials via the 'az' CLI — is it installed and logged in (az login)? {ex.Message}");
        }

        byte[] downloaded;
        try
        {
            using var http = httpClientFactory.CreateClient();
            http.Timeout = TimeSpan.FromMinutes(2);
            http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
                "Basic", Convert.ToBase64String(Encoding.ASCII.GetBytes($"{credentials.Username}:{credentials.Password}")));

            var url = $"https://{webAppName}.scm.azurewebsites.net/api/vfs/{remoteDbPath}";
            using var response = await http.GetAsync(url, ct);
            if (!response.IsSuccessStatusCode)
            {
                return StatusCode(StatusCodes.Status502BadGateway, $"Kudu returned {(int)response.StatusCode} fetching /{remoteDbPath}.");
            }

            downloaded = await response.Content.ReadAsByteArrayAsync(ct);
        }
        catch (Exception ex)
        {
            return StatusCode(StatusCodes.Status502BadGateway, $"Failed to download the live database: {ex.Message}");
        }

        var localDbPath = Path.GetFullPath(new SqliteConnectionStringBuilder(db.Database.GetConnectionString()!).DataSource);

        // Release EF Core's pooled native handles on the outgoing file
        // before touching it on disk, and drop its WAL/SHM sidecars — they
        // describe un-checkpointed state for a file that's about to stop
        // existing, and SQLite would otherwise try to replay them against
        // the incoming one.
        await db.Database.CloseConnectionAsync();
        SqliteConnection.ClearAllPools();
        foreach (var suffix in new[] { "-wal", "-shm" })
        {
            var sidecar = localDbPath + suffix;
            if (System.IO.File.Exists(sidecar))
            {
                System.IO.File.Delete(sidecar);
            }
        }

        string? backupPath = null;
        if (System.IO.File.Exists(localDbPath))
        {
            backupPath = $"{localDbPath}.bak-{DateTime.Now:yyyyMMdd-HHmmss}";
            System.IO.File.Copy(localDbPath, backupPath, overwrite: true);
        }

        await System.IO.File.WriteAllBytesAsync(localDbPath, downloaded, ct);

        return Ok(new
        {
            message = "Local database replaced with the live copy. Restart the local server so it picks up the new file cleanly.",
            backupPath,
            bytesDownloaded = downloaded.Length,
        });
    }

    // Shells out to the Azure CLI rather than storing any credential
    // ourselves — relies entirely on the operator's own `az login` session.
    private static async Task<(string Username, string Password)> GetPublishingCredentialsAsync(
        string webAppName, string resourceGroup, CancellationToken ct)
    {
        var psi = new ProcessStartInfo("az")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (var arg in new[]
        {
            "webapp", "deployment", "list-publishing-credentials",
            "--name", webAppName,
            "--resource-group", resourceGroup,
            "--query", "{u:publishingUserName,p:publishingPassword}",
            "-o", "json",
        })
        {
            psi.ArgumentList.Add(arg);
        }

        using var process = Process.Start(psi) ?? throw new InvalidOperationException("Couldn't start the 'az' process — is it on PATH?");
        var stdoutTask = process.StandardOutput.ReadToEndAsync(ct);
        var stderrTask = process.StandardError.ReadToEndAsync(ct);
        await process.WaitForExitAsync(ct);
        var stdout = await stdoutTask;
        var stderr = await stderrTask;

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(stderr.Trim());
        }

        using var doc = JsonDocument.Parse(stdout);
        return (doc.RootElement.GetProperty("u").GetString()!, doc.RootElement.GetProperty("p").GetString()!);
    }
}
