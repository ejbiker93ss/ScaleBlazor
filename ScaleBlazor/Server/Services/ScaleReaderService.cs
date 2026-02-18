using System.Globalization;
using System.IO;
using System.IO.Ports;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Runtime.InteropServices;
using Microsoft.EntityFrameworkCore;
using ScaleBlazor.Server.Data;
using ScaleBlazor.Shared;

namespace ScaleBlazor.Server.Services;

public class ScaleReaderService : IDisposable
{
    private const int StableReadCount = 10;
    private const int HistoryReadCount = 10;        
    private const double ZeroThreshold = 0.01;
    private static readonly TimeSpan ReconnectDelay = TimeSpan.FromSeconds(5);

    private SerialPort? _serialPort;
    private readonly ILogger<ScaleReaderService> _logger;
    private readonly IConfiguration _configuration;
    private readonly IServiceScopeFactory _scopeFactory;
    private double _currentWeight = 0;
    private bool _isRunning = false;
    private Task? _readTask;
    private CancellationTokenSource _cancellationTokenSource;
    private readonly Queue<double> _recentWeights = new();
    private bool _autoReadLocked = true;
    private DateTime _lastSettingsRefresh = DateTime.MinValue;
    private DateTime _lastReconnectAttempt = DateTime.MinValue;
    private bool _autoCaptureEnabled;
    private double _autoCaptureThresholdPercent = 5.0;
    private readonly object _rawCaptureLock = new();
    private RawReadCapture? _rawCapture;

    public event EventHandler<WeightChangedEventArgs>? WeightChanged;

    public ScaleReaderService(
        ILogger<ScaleReaderService> logger,
        IConfiguration configuration,
        IServiceScopeFactory scopeFactory)
    {
        _logger = logger;
        _configuration = configuration;
        _scopeFactory = scopeFactory;
        _cancellationTokenSource = new CancellationTokenSource();
    }

    public bool IsConnected => _serialPort?.IsOpen ?? false;

    public double CurrentWeight => _currentWeight;

    public void Start()
    {
        if (_isRunning)
        {
            return;
        }

        _cancellationTokenSource.Dispose();
        _cancellationTokenSource = new CancellationTokenSource();

        if (!TryOpenPort())
        {
            throw new InvalidOperationException("Failed to connect to the scale. See logs for details.");
        }

        _isRunning = true;
        _readTask = Task.Run(() => ReadScaleData(_cancellationTokenSource.Token));
    }

    public void Restart()
    {
        Stop();
        Start();
    }

    public async Task<string?> AutoDetectPortAsync(TimeSpan? timeoutPerPort = null)
    {
        var ports = GetAvailablePortNames();
        if (ports.Length == 0)
        {
            return null;
        }

        var baudRate = _configuration.GetValue<int>("Scale:BaudRate", 9600);
        var dataBits = _configuration.GetValue<int>("Scale:DataBits", 8);
        var parity = _configuration.GetValue<Parity>("Scale:Parity", Parity.None);
        var stopBits = _configuration.GetValue<StopBits>("Scale:StopBits", StopBits.One);
        var timeout = timeoutPerPort ?? TimeSpan.FromSeconds(2);

        foreach (var port in ports)
        {
            if (_serialPort?.IsOpen == true && string.Equals(_serialPort.PortName, port, StringComparison.OrdinalIgnoreCase))
            {
                return port;
            }

            try
            {
                using var testPort = new SerialPort(port)
                {
                    BaudRate = baudRate,
                    DataBits = dataBits,
                    Parity = parity,
                    StopBits = stopBits,
                    Handshake = Handshake.None,
                    ReadTimeout = 250,
                    WriteTimeout = 250,
                    DtrEnable = true,
                    RtsEnable = true
                };

                testPort.Open();

                var buffer = new StringBuilder();
                var endTime = DateTime.UtcNow.Add(timeout);

                while (DateTime.UtcNow < endTime)
                {
                    var data = testPort.ReadExisting();
                    if (!string.IsNullOrWhiteSpace(data))
                    {
                        buffer.Append(data);
                        var bufferStr = buffer.ToString();
                        var lines = bufferStr.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                        foreach (var line in lines)
                        {
                            if (LooksLikeScaleLine(line))
                            {
                                await SaveDetectedPortAsync(port);
                                return port;
                            }
                        }
                    }

                    await Task.Delay(100);
                }
            }
            catch (Exception)
            {
            }
        }

        return null;
    }

    private async Task ReadScaleData(CancellationToken cancellationToken)
    {
        var buffer = new StringBuilder();

        while (_isRunning && !cancellationToken.IsCancellationRequested)
        {
            try
            {
                if (_serialPort?.IsOpen != true)
                {
                    if (DateTime.UtcNow - _lastReconnectAttempt >= ReconnectDelay)
                    {
                        _lastReconnectAttempt = DateTime.UtcNow;
                        TryOpenPort();
                    }

                    await Task.Delay(1000, cancellationToken);
                    continue;
                }

                if (_serialPort?.IsOpen == true && _serialPort.BytesToRead > 0)
                {
                    // Read available data
                    var data = _serialPort.ReadExisting();
                    buffer.Append(data);

                    // Process complete lines (ending with CR or LF)
                    var bufferStr = buffer.ToString();
                    var lines = bufferStr.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);

                    if (bufferStr.EndsWith('\r') || bufferStr.EndsWith('\n'))
                    {
                        // Process all complete lines
                        foreach (var line in lines)
                        {
                            await ProcessScaleLineAsync(line.Trim());
                        }
                        buffer.Clear();
                    }
                    else if (lines.Length > 1)
                    {
                        // Process all but the last incomplete line
                        for (int i = 0; i < lines.Length - 1; i++)
                        {
                            await ProcessScaleLineAsync(lines[i].Trim());
                        }
                        // Keep the incomplete line in the buffer
                        buffer.Clear();
                        buffer.Append(lines[^1]);
                    }
                }

                await Task.Delay(100, cancellationToken); // Poll every 100ms
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (TimeoutException)
            {
                // Normal timeout, continue
            }
            catch (IOException ex)
            {
                _logger.LogWarning(ex, "Scale connection lost. Attempting to reconnect.");
                ClosePort();
                await Task.Delay(1000, cancellationToken);
            }
            catch (InvalidOperationException ex)
            {
                _logger.LogWarning(ex, "Scale port not ready. Attempting to reconnect.");
                ClosePort();
                await Task.Delay(1000, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error reading from scale");
                await Task.Delay(1000, cancellationToken); // Wait before retrying
            }
        }
    }

    private async Task ProcessScaleLineAsync(string line)
    {
        try
        {
            // CAS PD-2Z protocol typically sends data in format:
            // ST,GS,+00000lb,CR (Stable, Gross, weight in pounds)
            // or ST,NT,+00000lb,CR (Stable, Net, weight in pounds)
            // Format may vary: Check actual protocol from manufacturer

            _logger.LogInformation($"Raw scale data: {line}"); // Changed to Information so it's always visible

            RecordRawLine(line);

            if (!line.StartsWith("WGT", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            // Parse different possible formats
            double weight = 0;

            // Format 1: "ST,GS,+00000lb" or similar
            if (line.Contains(','))
            {
                var parts = line.Split(',');
                _logger.LogInformation($"Comma-separated format detected. Parts: {string.Join(" | ", parts)}");
                if (parts.Length >= 3)
                {
                    var weightStr = parts[2].Trim();
                    weight = ParseWeightString(weightStr);
                }
            }
            // Format 2: Just the weight value
            else
            {
                _logger.LogInformation($"Simple format detected, parsing: {line}");
                weight = ParseWeightString(line);
            }

            if (!double.IsNaN(weight))
            {
                // Double the weight to get correct case weight
                var originalWeight = weight;
                weight *= 2;

                _logger.LogInformation($"Parsed weight: {originalWeight:F2} lbs → Case weight: {weight:F2} lbs");

                await RefreshSettingsAsync();

                if (weight <= ZeroThreshold)
                {
                    if (_autoReadLocked)
                    {
                        _autoReadLocked = false;
                    }

                    _recentWeights.Clear();
                    UpdateCurrentWeight(0);
                    return;
                }

                if (_autoReadLocked)
                {
                    UpdateCurrentWeight(weight);
                    return;
                }

                AddReading(weight);

                if (_autoCaptureEnabled && ShouldAutoCapture(out var stableWeight))
                {
                    _autoReadLocked = true;
                    _recentWeights.Clear();
                    await AutoCaptureReadingAsync(stableWeight);
                    UpdateCurrentWeight(stableWeight);
                    return;
                }

                UpdateCurrentWeight(weight);
            }
            else
            {
                _logger.LogWarning($"Failed to parse weight from: {line}");
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, $"Failed to parse scale data: {line}");
        }
    }

    public async Task<IReadOnlyList<string>> CaptureRawLinesAsync(int count, TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        if (_serialPort?.IsOpen != true)
        {
            return Array.Empty<string>();
        }

        RawReadCapture capture;

        lock (_rawCaptureLock)
        {
            if (_rawCapture != null)
            {
                throw new InvalidOperationException("Raw capture already in progress.");
            }

            capture = new RawReadCapture(Math.Max(1, count));
            _rawCapture = capture;
        }

        var timeoutTask = Task.Delay(timeout, cancellationToken);
        var completed = await Task.WhenAny(capture.Completion.Task, timeoutTask);

        if (completed != capture.Completion.Task)
        {
            lock (_rawCaptureLock)
            {
                if (_rawCapture == capture)
                {
                    _rawCapture = null;
                }
            }

            capture.Completion.TrySetResult(capture.Lines.ToList());
        }

        return await capture.Completion.Task;
    }

    private void RecordRawLine(string line)
    {
        RawReadCapture? capture;

        lock (_rawCaptureLock)
        {
            capture = _rawCapture;
            if (capture == null)
            {
                return;
            }

            capture.Lines.Add(line);

            if (capture.Lines.Count >= capture.TargetCount)
            {
                _rawCapture = null;
                capture.Completion.TrySetResult(capture.Lines.ToList());
            }
        }
    }

    private double ParseWeightString(string weightStr)
    {
        // Extract numeric tokens from strings like "WGT:1  2.90P  0.00"
        var matches = Regex.Matches(weightStr, @"[-+]?\d*\.?\d+");

        if (matches.Count == 0)
        {
            return double.NaN;
        }

        // For WGT format, the weight is typically the second number
        var valueToParse = weightStr.StartsWith("WGT", StringComparison.OrdinalIgnoreCase) && matches.Count > 1
            ? matches[1].Value
            : matches[0].Value;

        if (double.TryParse(valueToParse, NumberStyles.Float, CultureInfo.InvariantCulture, out var weight))
        {
            return weight;
        }

        return double.NaN;
    }

    private void AddReading(double weight)
    {
        _recentWeights.Enqueue(weight);
        while (_recentWeights.Count > StableReadCount)
        {
            _recentWeights.Dequeue();
        }
    }

    private bool ShouldAutoCapture(out double stableWeight)
    {
        stableWeight = 0;

        if (_recentWeights.Count < StableReadCount)
        {
            return false;
        }

        var weights = _recentWeights.ToArray();
        var lastReadings = weights[^StableReadCount..];
        var avgLast = lastReadings.Average();

        if (avgLast <= 0)
        {
            return false;
        }

        var currentWeight = lastReadings[^1];
        var percentDiff = Math.Abs(currentWeight - avgLast) / avgLast * 100.0;

        if (percentDiff > _autoCaptureThresholdPercent)
        {
            return false;
        }

        stableWeight = avgLast;
        return true;
    }

    private void UpdateCurrentWeight(double weight)
    {
        var oldWeight = _currentWeight;
        _currentWeight = weight;

        if (Math.Abs(oldWeight - weight) > 0.01)
        {
            _logger.LogInformation($"Weight changed: {weight:F2} lbs");
            WeightChanged?.Invoke(this, new WeightChangedEventArgs(weight));
        }
    }

    private async Task RefreshSettingsAsync()
    {
        if (DateTime.UtcNow - _lastSettingsRefresh < TimeSpan.FromSeconds(2))
        {
            return;
        }

        using var scope = _scopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ScaleDbContext>();
        var settings = await context.Settings.AsNoTracking().FirstOrDefaultAsync();

        _autoCaptureEnabled = settings?.AutoCaptureEnabled ?? false;
        _autoCaptureThresholdPercent = settings?.AutoCaptureThresholdPercent ?? 5.0;
        _lastSettingsRefresh = DateTime.UtcNow;
    }

    private string GetConfiguredPortName()
    {
        using var scope = _scopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ScaleDbContext>();
        var settings = context.Settings.AsNoTracking().FirstOrDefault();
        var configuredPort = settings?.ScalePortName
            ?? _configuration["Scale:PortName"]
            ?? "COM4";

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            var availablePorts = GetAvailablePortNames();
            if (!availablePorts.Contains(configuredPort, StringComparer.OrdinalIgnoreCase))
            {
                var linuxPort = availablePorts.FirstOrDefault(port =>
                    port.StartsWith("/dev/ttyUSB", StringComparison.OrdinalIgnoreCase)
                    || port.StartsWith("/dev/ttyACM", StringComparison.OrdinalIgnoreCase));

                if (!string.IsNullOrWhiteSpace(linuxPort))
                {
                    return linuxPort;
                }
            }
        }

        return configuredPort;
    }

    private static bool LooksLikeScaleLine(string line)
    {
        return Regex.IsMatch(line, @"[-+]?\d*\.?\d+");
    }

    private async Task AutoCaptureReadingAsync(double weight)
    {
        using var scope = _scopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ScaleDbContext>();
        var settings = await context.Settings.FirstOrDefaultAsync();
        var readingsPerPallet = settings?.ReadingsPerPallet ?? 10;

        var activePallet = await context.Pallets
            .Where(p => !p.IsCompleted)
            .OrderByDescending(p => p.CreatedAt)
            .FirstOrDefaultAsync();

        var reading = new ScaleReading
        {
            Weight = weight,
            Timestamp = DateTime.Now
        };

        if (activePallet != null)
        {
            reading.PalletId = activePallet.PalletId;
            activePallet.ReadingCount++;

            var palletReadings = await context.ScaleReadings
                .Where(r => r.PalletId == activePallet.PalletId)
                .ToListAsync();
            palletReadings.Add(reading);
            activePallet.TotalWeight = palletReadings.Average(r => r.Weight);

            if (activePallet.ReadingCount >= readingsPerPallet)
            {
                activePallet.IsCompleted = true;

                var nextPalletNumber = await context.Pallets.CountAsync() + 1;
                var newPallet = new Pallet
                {
                    PalletId = $"P{nextPalletNumber:D3}",
                    CreatedAt = DateTime.Now,
                    IsCompleted = false,
                    ReadingCount = 0,
                    TotalWeight = 0
                };
                context.Pallets.Add(newPallet);
            }
        }

        context.ScaleReadings.Add(reading);
        await context.SaveChangesAsync();
    }

    private async Task SaveDetectedPortAsync(string portName)
    {
        using var scope = _scopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ScaleDbContext>();
        var settings = await context.Settings.FirstOrDefaultAsync();

        if (settings == null)
        {
            settings = new AppSettings
            {
                ReadingsPerPallet = 10,
                ScalePortName = portName
            };
            context.Settings.Add(settings);
        }
        else
        {
            settings.ScalePortName = portName;
        }

        await context.SaveChangesAsync();
    }

    public void Stop()
    {
        _isRunning = false;
        _cancellationTokenSource.Cancel();

        try
        {
            _readTask?.Wait(TimeSpan.FromSeconds(2));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error waiting for read task to complete");
        }

        ClosePort();
    }

    public List<string> GetAvailablePorts()
    {
        return GetAvailablePortNames().ToList();
    }

    private static string[] GetAvailablePortNames()
    {
        var ports = SerialPort.GetPortNames().ToList();

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            ports.AddRange(GetDeviceMatches("/dev/ttyUSB*"));
            ports.AddRange(GetDeviceMatches("/dev/ttyACM*"));
        }

        return ports
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static IEnumerable<string> GetDeviceMatches(string pattern)
    {
        try
        {
            return Directory.GetFiles("/dev", Path.GetFileName(pattern));
        }
        catch
        {
            return Array.Empty<string>();
        }
    }

    private static bool IsDevicePortName(string portName)
    {
        return portName.StartsWith("/dev/", StringComparison.OrdinalIgnoreCase);
    }

    public void Dispose()
    {
        Stop();
        _serialPort?.Dispose();
        _cancellationTokenSource.Dispose();
    }

    private bool TryOpenPort()
    {
        var portName = GetConfiguredPortName();
        var baudRate = _configuration.GetValue<int>("Scale:BaudRate", 9600);
        var dataBits = _configuration.GetValue<int>("Scale:DataBits", 8);
        var parity = _configuration.GetValue<Parity>("Scale:Parity", Parity.None);
        var stopBits = _configuration.GetValue<StopBits>("Scale:StopBits", StopBits.One);

        var availablePorts = GetAvailablePortNames();

        try
        {
            if (!availablePorts.Contains(portName, StringComparer.OrdinalIgnoreCase))
            {
                if (!IsDevicePortName(portName) || !File.Exists(portName))
                {
                    var detectedPort = AutoDetectPortAsync(TimeSpan.FromSeconds(2)).GetAwaiter().GetResult();
                    if (!string.IsNullOrWhiteSpace(detectedPort))
                    {
                        portName = detectedPort;
                    }
                    else
                    {
                        throw new Exception($"Port {portName} not found. Available ports: {string.Join(", ", availablePorts)}");
                    }
                }
            }

            var serialPort = new SerialPort(portName)
            {
                BaudRate = baudRate,
                DataBits = dataBits,
                Parity = parity,
                StopBits = stopBits,
                Handshake = Handshake.None,
                ReadTimeout = 500,
                WriteTimeout = 500,
                DtrEnable = true,
                RtsEnable = true
            };

            serialPort.Open();
            _serialPort?.Dispose();
            _serialPort = serialPort;
            _logger.LogInformation("Scale connected on {PortName}", portName);
            return true;
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.LogError(ex, "Access denied to {PortName}. Port may be in use by another application.", portName);
        }
        catch (IOException ex)
        {
            _logger.LogError(ex, "Device communication error on {PortName}. Check that the scale is powered and cable is connected.", portName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to connect to scale on {PortName}", portName);
        }

        return false;
    }

    private void ClosePort()
    {
        try
        {
            if (_serialPort?.IsOpen == true)
            {
                _serialPort.Close();
                _logger.LogInformation("Scale connection closed");
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error closing scale connection");
        }
    }

    private sealed class RawReadCapture
    {
        public RawReadCapture(int targetCount)
        {
            TargetCount = targetCount;
        }

        public int TargetCount { get; }
        public List<string> Lines { get; } = new();
        public TaskCompletionSource<List<string>> Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
}

public class WeightChangedEventArgs : EventArgs
{
    public double Weight { get; }
    public DateTime Timestamp { get; }

    public WeightChangedEventArgs(double weight)
    {
        Weight = weight;
        Timestamp = DateTime.Now;
    }
}
