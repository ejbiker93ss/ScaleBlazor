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
    private const double ZeroThreshold = 0.01;
    private const int DefaultExactReadingHoldMilliseconds = 850;
    private const int ExactReadingDecimals = 2;
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
    private readonly Queue<double> _recentSavedWeights = new();
    private bool _autoReadLocked = true;
    private DateTime _lastSettingsRefresh = DateTime.MinValue;
    private DateTime _lastReconnectAttempt = DateTime.MinValue;
    private bool _autoCaptureEnabled;
    private double _autoCaptureThresholdPercent = 5.0;
    private TimeSpan _exactReadingHoldTime = TimeSpan.FromMilliseconds(DefaultExactReadingHoldMilliseconds);
    private bool _seenZeroWhileLocked;
    private bool _savedWeightsInitialized;
    private double _savedBaselineAverage;
    private double _savedBaselinePercentDiff;
    private bool _passSavedThresholdCheck = true;
    private readonly object _stateLock = new();
    private readonly object _rawCaptureLock = new();
    private RawReadCapture? _rawCapture;
    private double _lastExactWeight = -1;
    private DateTime _exactWeightSince = DateTime.MinValue;

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
                if (parts.Length >= 3)
                {
                    var weightStr = parts[2].Trim();
                    weight = ParseWeightString(weightStr);
                }
            }
            // Format 2: Just the weight value
            else
            {
                weight = ParseWeightString(line);
            }

            if (!double.IsNaN(weight))
            {
                // Double the weight to get correct case weight
                weight *= 2;

                await RefreshSettingsAsync();

                var roundedWeight = Math.Round(weight, ExactReadingDecimals, MidpointRounding.AwayFromZero);
                lock (_stateLock)
                {
                    if (roundedWeight != _lastExactWeight)
                    {
                        _lastExactWeight = roundedWeight;
                        _exactWeightSince = DateTime.UtcNow;
                    }
                }

                if (weight <= ZeroThreshold)
                {
                    lock (_stateLock)
                    {
                        if (_autoReadLocked)
                        {
                            _seenZeroWhileLocked = true;
                        }

                        _recentWeights.Clear();
                    }

                    UpdateCurrentWeight(weight);
                    return;
                }

                var isLocked = false;
                lock (_stateLock)
                {
                    if (_autoReadLocked && _seenZeroWhileLocked)
                    {
                        _autoReadLocked = false;
                        _seenZeroWhileLocked = false;
                        _recentWeights.Clear();
                    }

                    isLocked = _autoReadLocked;
                }

                if (isLocked)
                {
                    UpdateCurrentWeight(weight);
                    return;
                }

                AddReading(weight);

                if (_autoCaptureEnabled)
                {
                    var (shouldAutoCapture, stableWeight) = await ShouldAutoCaptureAsync();
                    if (!shouldAutoCapture)
                    {
                        UpdateCurrentWeight(weight);
                        return;
                    }

                    lock (_stateLock)
                    {
                        _autoReadLocked = true;
                        _seenZeroWhileLocked = false;
                        _recentWeights.Clear();
                    }

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

    public async Task<string> RunReadingSpeedTestAsync(int readingsToTest = 20)
    {
        if (!_isRunning || _serialPort?.IsOpen != true)
        {
            return "Scale is not connected or running.";
        }

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        var lines = await CaptureRawLinesAsync(readingsToTest, TimeSpan.FromSeconds(10));
        stopwatch.Stop();

        if (lines.Count == 0)
        {
            return "No readings received within the timeout period.";
        }

        var avgTimePerReading = stopwatch.ElapsedMilliseconds / (double)lines.Count;
        var readingsPerSecond = lines.Count / stopwatch.Elapsed.TotalSeconds;
        var timeToStabilize = avgTimePerReading * StableReadCount;

        var report = new StringBuilder();
        report.AppendLine($"--- Scale Reading Speed Test ---");
        report.AppendLine($"Total Readings Captured: {lines.Count}");
        report.AppendLine($"Total Time Elapsed: {stopwatch.ElapsedMilliseconds} ms");
        report.AppendLine($"Average Time Per Reading: {avgTimePerReading:F2} ms");
        report.AppendLine($"Readings Per Second: {readingsPerSecond:F2}");
        report.AppendLine($"Estimated Time to Collect {StableReadCount} Readings (Stabilization Window): {timeToStabilize:F2} ms");

        if (timeToStabilize < 1000)
        {
            report.AppendLine();
            report.AppendLine("Note: Your stabilization window is under 1 second. This might cause the system to capture a reading before the physical scale has fully settled.");
            report.AppendLine("Consider increasing 'StableReadCount' (currently 10) or lowering 'AutoCaptureThresholdPercent' (currently 5%).");
        }

        return report.ToString();
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
        lock (_stateLock)
        {
            _recentWeights.Enqueue(weight);
            while (_recentWeights.Count > StableReadCount)
            {
                _recentWeights.Dequeue();
            }
        }
    }

    private async Task<(bool ShouldAutoCapture, double StableWeight)> ShouldAutoCaptureAsync()
    {
        var stableWeight = 0d;

        double[] weights;
        DateTime exactWeightSince;
        lock (_stateLock)
        {
            weights = _recentWeights.ToArray();
            exactWeightSince = _exactWeightSince;
        }

        if (weights.Length < StableReadCount)
        {
            return (false, stableWeight);
        }

        var lastReadings = weights[^StableReadCount..];
        var avgLast = lastReadings.Average();

        if (avgLast <= 0)
        {
            return (false, stableWeight);
        }

        var currentWeight = lastReadings[^1];
        var diff = currentWeight >= avgLast ? currentWeight - avgLast : avgLast - currentWeight;
        var percentDiff = 0.0;

        if (diff > 0 && avgLast > 0)
        {
            percentDiff = diff / avgLast * 100.0;
        }

        var minWeight = lastReadings.Min();
        var maxWeight = lastReadings.Max();
        var rangePercent = 0.0;

        if ((maxWeight - minWeight) > 0 && avgLast > 0)
        {
            rangePercent = (maxWeight - minWeight) / avgLast * 100.0;
        }

        if (percentDiff > _autoCaptureThresholdPercent || rangePercent > _autoCaptureThresholdPercent)
        {
            return (false, stableWeight);
        }

        if (DateTime.UtcNow - exactWeightSince < _exactReadingHoldTime)
        {
            return (false, stableWeight);
        }

        var savedReadings = await GetRecentSavedReadingsAsync();
        if (savedReadings.Count > 0)
        {
            var savedAverage = savedReadings.Average();
            var savedBaseline = savedAverage >= 0 ? savedAverage : -savedAverage;

            if (savedBaseline > ZeroThreshold)
            {
                var savedDiff = avgLast >= savedAverage ? avgLast - savedAverage : savedAverage - avgLast;
                var savedPercentDiff = savedDiff / savedBaseline * 100.0;
                var passSavedThresholdCheck = savedPercentDiff <= _autoCaptureThresholdPercent;

                lock (_stateLock)
                {
                    _savedBaselineAverage = savedAverage;
                    _savedBaselinePercentDiff = savedPercentDiff;
                    _passSavedThresholdCheck = passSavedThresholdCheck;
                }

                if (!passSavedThresholdCheck)
                {
                    return (false, stableWeight);
                }
            }
        }
        else
        {
            lock (_stateLock)
            {
                _savedBaselineAverage = 0;
                _savedBaselinePercentDiff = 0;
                _passSavedThresholdCheck = true;
            }
        }

        stableWeight = avgLast;
        return (true, stableWeight);
    }

    private async Task<IReadOnlyList<double>> GetRecentSavedReadingsAsync()
    {
        lock (_stateLock)
        {
            if (_recentSavedWeights.Count > 0)
            {
                return _recentSavedWeights.ToArray();
            }

            if (_savedWeightsInitialized)
            {
                return Array.Empty<double>();
            }
        }

        using var scope = _scopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ScaleDbContext>();
        var recentSaved = await context.ScaleReadings
            .AsNoTracking()
            .OrderByDescending(r => r.Timestamp)
            .Take(StableReadCount)
            .Select(r => r.Weight)
            .ToListAsync();

        lock (_stateLock)
        {
            if (_recentSavedWeights.Count == 0 && recentSaved.Count > 0)
            {
                for (int i = recentSaved.Count - 1; i >= 0; i--)
                {
                    _recentSavedWeights.Enqueue(recentSaved[i]);
                }
            }

            _savedWeightsInitialized = true;
            return _recentSavedWeights.ToArray();
        }
    }

    private void AddSavedReading(double weight)
    {
        lock (_stateLock)
        {
            _recentSavedWeights.Enqueue(weight);
            while (_recentSavedWeights.Count > StableReadCount)
            {
                _recentSavedWeights.Dequeue();
            }

            _savedWeightsInitialized = true;
        }
    }

    private void UpdateCurrentWeight(double weight)
    {
        double oldWeight;
        lock (_stateLock)
        {
            oldWeight = _currentWeight;
            _currentWeight = weight;
        }

        if (oldWeight - weight > 0.01)
        {
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

        var holdMs = _configuration.GetValue<int>("Scale:ExactReadingHoldMilliseconds", DefaultExactReadingHoldMilliseconds);
        if (holdMs < 100)
        {
            holdMs = 100;
        }
        _exactReadingHoldTime = TimeSpan.FromMilliseconds(holdMs);

        _lastSettingsRefresh = DateTime.UtcNow;
    }

    public Task<ScaleDebugState> GetDebugStateAsync()
    {
        double currentWeight;
        double lastExactWeight;
        DateTime exactWeightSince;
        bool autoReadLocked;
        bool seenZeroWhileLocked;
        bool autoCaptureEnabled;
        double autoCaptureThresholdPercent;
        TimeSpan exactReadingHoldTime;
        double[] weights;

        lock (_stateLock)
        {
            currentWeight = _currentWeight;
            lastExactWeight = _lastExactWeight;
            exactWeightSince = _exactWeightSince;
            autoReadLocked = _autoReadLocked;
            seenZeroWhileLocked = _seenZeroWhileLocked;
            autoCaptureEnabled = _autoCaptureEnabled;
            autoCaptureThresholdPercent = _autoCaptureThresholdPercent;
            exactReadingHoldTime = _exactReadingHoldTime;
            weights = _recentWeights.ToArray();
        }

        var debug = new ScaleDebugState
        {
            AutoCaptureEnabled = autoCaptureEnabled,
            AutoReadLocked = autoReadLocked,
            StableReadCount = StableReadCount,
            RecentCount = weights.Length,
            CurrentWeight = currentWeight,
            LastExactWeight = lastExactWeight,
            SeenZeroWhileLocked = seenZeroWhileLocked,
            AutoCaptureThresholdPercent = autoCaptureThresholdPercent,
            ExactHoldElapsedMs = (DateTime.UtcNow - exactWeightSince).TotalMilliseconds,
            ExactHoldRequiredMs = exactReadingHoldTime.TotalMilliseconds,
            PassStabilityCheck = false
        };

        if (weights.Length > 0)
        {
            debug.RecentAverage = weights.Average();
            debug.RecentMin = weights.Min();
            debug.RecentMax = weights.Max();
        }

        if (weights.Length >= StableReadCount)
        {
            var lastReadings = weights[^StableReadCount..];
            var avgLast = lastReadings.Average();
            var current = lastReadings[^1];
            var diff = current >= avgLast ? current - avgLast : avgLast - current;

            debug.RecentAverage = avgLast;
            debug.RecentMin = lastReadings.Min();
            debug.RecentMax = lastReadings.Max();
            debug.PercentDiff = avgLast == 0 ? 0 : diff / avgLast * 100.0;
            debug.RangePercent = avgLast == 0 ? 0 : (debug.RecentMax - debug.RecentMin) / avgLast * 100.0;
            debug.PassStabilityCheck = debug.PercentDiff <= autoCaptureThresholdPercent && debug.RangePercent <= autoCaptureThresholdPercent;
        }

        debug.PassHoldCheck = debug.ExactHoldElapsedMs >= debug.ExactHoldRequiredMs;

        debug.ReadyToAutoCapture = debug.AutoCaptureEnabled
            && !debug.AutoReadLocked
            && debug.PassStabilityCheck
            && debug.PassHoldCheck;

        return Task.FromResult(debug);
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
        AddSavedReading(weight);
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
                        _logger.LogError("Port {PortName} not found. Available ports: {AvailablePorts}", portName, string.Join(", ", availablePorts));
                        return false;
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
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error closing scale connection");
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
