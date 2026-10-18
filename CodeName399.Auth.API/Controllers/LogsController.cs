using CodeName399.Auth.API.Constants;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.IO.Compression;

namespace CodeName399.Auth.API.Controllers;

[Authorize(Policy = PolicyConstant.AuthenticationAdmin)]
public class LogsController : ControllerBase
{
    private readonly IWebHostEnvironment _environment;
    private readonly IConfiguration _configuration;
    private readonly ILogger<LogsController> _logger;

    public LogsController(
        IWebHostEnvironment environment,
        IConfiguration configuration,
        ILogger<LogsController> logger)
    {
        _environment = environment;
        _configuration = configuration;
        _logger = logger;
    }

    private string LogsPath
    {
        get
        {
            var configuredPath =
               Environment.GetEnvironmentVariable("CODENAME399_LOG_DIRECTORY") ?? _configuration["Logging:FileDirectory"];

            var path = string.IsNullOrWhiteSpace(configuredPath)
                ? Path.Combine(_environment.ContentRootPath, "Logs")
                : configuredPath;

            return Path.GetFullPath(path);
        }
    }

    [HttpGet("logs")]
    public IActionResult GetLogs()
    {
        try
        {
            var path = LogsPath;

            _logger.LogDebug(
                "Getting available log files from {LogsPath}. Exists={Exists}",
                path,
                Directory.Exists(path));

            if (!Directory.Exists(path))
            {
                return Ok(new
                {
                    LogDirectory = path,
                    DirectoryExists = false,
                    Files = Array.Empty<object>()
                });
            }

            var logs = Directory
                .EnumerateFiles(path, "log-*.txt", SearchOption.TopDirectoryOnly)
                .Select(file =>
                {
                    var info = new FileInfo(file);

                    return new
                    {
                        Name = info.Name,
                        Size = info.Length,
                        Created = info.CreationTimeUtc,
                        Modified = info.LastWriteTimeUtc
                    };
                })
                .OrderByDescending(x => x.Modified)
                .ToArray();

            _logger.LogDebug(
                "Found {Count} log files in {LogsPath}.",
                logs.Length,
                path);

            return Ok(new
            {
                LogDirectory = path,
                DirectoryExists = true,
                Files = logs
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to retrieve log files.");

            return StatusCode(
                StatusCodes.Status500InternalServerError,
                "An error occurred while retrieving log files.");
        }
    }

    [HttpGet("download/{fileName}")]
    public IActionResult Download(string fileName)
    {
        try
        {
            fileName = Path.GetFileName(fileName);

            if (string.IsNullOrWhiteSpace(fileName) ||
                !fileName.StartsWith("log-", StringComparison.OrdinalIgnoreCase) ||
                !fileName.EndsWith(".txt", StringComparison.OrdinalIgnoreCase))
            {
                return BadRequest("Invalid log file name.");
            }

            var filePath = Path.Combine(LogsPath, fileName);

            if (!System.IO.File.Exists(filePath))
            {
                _logger.LogDebug(
                    "Requested log file not found: {FilePath}",
                    filePath);

                return NotFound();
            }

            // Explicit System.IO.FileStream avoids any ambiguity with
            // ControllerBase.File(...).
            var stream = new System.IO.FileStream(
                filePath,
                System.IO.FileMode.Open,
                System.IO.FileAccess.Read,
                System.IO.FileShare.ReadWrite | System.IO.FileShare.Delete);

            return File(stream, "text/plain", fileName);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to download log file {FileName}",
                fileName);

            return StatusCode(
                StatusCodes.Status500InternalServerError,
                "An error occurred while downloading the log file.");
        }
    }

    [HttpGet("download-all")]
    public IActionResult DownloadAll()
    {
        try
        {
            var path = LogsPath;

            if (!Directory.Exists(path))
            {
                return NotFound($"Log directory not found: {path}");
            }

            var logFiles = Directory
                .EnumerateFiles(path, "log-*.txt", SearchOption.TopDirectoryOnly)
                .OrderByDescending(System.IO.File.GetLastWriteTimeUtc)
                .ToArray();

            if (logFiles.Length == 0)
            {
                return NotFound($"No log files found in: {path}");
            }

            using var memory = new MemoryStream();

            using (var archive = new ZipArchive(
                       memory,
                       ZipArchiveMode.Create,
                       leaveOpen: true))
            {
                foreach (var file in logFiles)
                {
                    var entry = archive.CreateEntry(
                        Path.GetFileName(file));

                    using var entryStream = entry.Open();

                    using var fileStream = new System.IO.FileStream(
                        file,
                        System.IO.FileMode.Open,
                        System.IO.FileAccess.Read,
                        System.IO.FileShare.ReadWrite |
                        System.IO.FileShare.Delete);

                    fileStream.CopyTo(entryStream);
                }
            }

            memory.Position = 0;

            return File(
                memory,
                "application/zip",
                $"Logs_{DateTime.UtcNow:yyyyMMdd_HHmmss}.zip");
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to create log archive.");

            return StatusCode(
                StatusCodes.Status500InternalServerError,
                "An error occurred while creating the log archive.");
        }
    }
}
