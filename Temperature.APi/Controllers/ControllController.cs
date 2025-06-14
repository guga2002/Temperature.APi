using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Diagnostics;

namespace Temperature.Api.Controllers;

[Route("api/[controller]")]
[ApiController]
public class ControllController : ControllerBase
{
    private static DateTime _lastBeat = DateTime.MinValue;
    private static readonly object _beatLock = new();
    private readonly ILogger<ControllController> _logger;

    private const string ProcessName = "Natia.UI";
    private const string ExePath = @"C:\Users\MONITORING PC\source\repos\Natia.UI\Natia.UI\bin\Release\net8.0\publish\Natia.UI.exe";

    public ControllController(ILogger<ControllController> logger)
    {
        _logger = logger;
    }

    [HttpGet("heartbeat/{fromRobot}")]
    [AllowAnonymous]
    public async Task<DateTime> HeartBeat(bool fromRobot)
    {
        await Task.Yield();

        var now = DateTime.UtcNow;

        if (fromRobot)
        {
            lock (_beatLock)
            {
                _lastBeat = now;
            }
            _logger.LogInformation("✅ Heartbeat received from robot at {Time}", now);
        }

        return _lastBeat;
    }

    [HttpGet("checkrobot")]
    [AllowAnonymous]
    public async Task<string> CheckRobotHealth()
    {
        if (!IsProcessRunning(ProcessName))
        {
            _logger.LogWarning("⚠️ Robot process not running. Attempting restart.");
            StartProcess(ExePath);
        }

        var now = DateTime.UtcNow;
        DateTime lastBeat;
        lock (_beatLock)
        {
            lastBeat = _lastBeat;
        }

        if ((now - lastBeat).TotalMinutes > 3)
        {
            _logger.LogWarning("⚠️ Robot heartbeat stale (last: {LastBeat}). Reloading...", lastBeat);
            return await Reload();
        }

        _logger.LogInformation("✅ Robot is healthy. Last beat: {LastBeat}", lastBeat);
        return "Robot is healthy";
    }

    [HttpGet("reload")]
    [AllowAnonymous]
    public async Task<string> Reload()
    {
        _logger.LogInformation("🔄 Reloading Natia robot process...");

        StopProcess(ProcessName);
        await Task.Delay(2000);
        StartProcess(ExePath);

        _logger.LogInformation("✅ Robot successfully reloaded.");
        return "Successfully Reloaded";
    }


    private bool IsProcessRunning(string processName)
    {
        return Process.GetProcessesByName(processName).Any();
    }

    private void StopProcess(string processName)
    {
        var processes = Process.GetProcessesByName(processName);
        if (processes.Length == 0)
        {
            _logger.LogWarning("No running process found: {ProcessName}", processName);
            return;
        }

        foreach (var process in processes)
        {
            try
            {
                _logger.LogInformation("Stopping process {Name} (PID: {PID})", process.ProcessName, process.Id);
                process.Kill();
                process.WaitForExit();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error stopping process {Name}", process.ProcessName);
            }
        }
    }


    private void StartProcess(string exePath)
    {
        try
        {
            if (IsProcessRunning(ProcessName))
            {
                _logger.LogInformation("Process already running: {ProcessName}", ProcessName);
                return;
            }

            var startInfo = new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = $"/c start \"\" \"{exePath}\"",
                CreateNoWindow = true,
                UseShellExecute = false
            };

            Process.Start(startInfo);
            _logger.LogInformation("Started process from path: {ExePath}", exePath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error starting process at {ExePath}", exePath);
        }
    }
}
