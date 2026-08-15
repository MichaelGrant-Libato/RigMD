using Microsoft.AspNetCore.Mvc;
using Npgsql;

namespace RigMD.Api.Controllers;

[ApiController]
[Route("api/database")]
public class DatabaseController : ControllerBase
{
    private readonly IConfiguration _configuration;

    public DatabaseController(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    private string GetConnectionString()
    {
        var databaseUrl = _configuration["DATABASE_URL"];

        if (string.IsNullOrWhiteSpace(databaseUrl))
            throw new InvalidOperationException("DATABASE_URL is not configured.");

        var uri = new Uri(databaseUrl);
        var userInfo = uri.UserInfo.Split(':', 2);

        if (userInfo.Length != 2)
            throw new InvalidOperationException("DATABASE_URL contains invalid credentials.");

        var builder = new NpgsqlConnectionStringBuilder
        {
            Host = uri.Host,
            Port = uri.Port,
            Database = uri.AbsolutePath.TrimStart('/'),
            Username = Uri.UnescapeDataString(userInfo[0]),
            Password = Uri.UnescapeDataString(userInfo[1]),
            SslMode = SslMode.Require,
            Pooling = true
        };

        return builder.ConnectionString;
    }

    [HttpGet("health")]
    public async Task<IActionResult> Health()
    {
        try
        {
            await using var connection = new NpgsqlConnection(GetConnectionString());
            await connection.OpenAsync();

            await using var command =
                new NpgsqlCommand("SELECT current_database(), current_timestamp;", connection);

            await using var reader = await command.ExecuteReaderAsync();

            await reader.ReadAsync();

            return Ok(new
            {
                status = "connected",
                database = reader.GetString(0),
                timestamp = reader.GetDateTime(1)
            });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new
            {
                status = "error",
                error = ex.Message
            });
        }
    }
}