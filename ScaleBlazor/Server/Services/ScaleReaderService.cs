using System.IO.Ports;
using System.Text;

namespace ScaleBlazor.Server.Services;

public class ScaleReaderService : IDisposable
{
    private SerialPort? _serialPort;
    private readonly ILogger<ScaleReaderService> _logger;
    private readonly IConfiguration _configuration;
    private double _currentWeight = 0;
    private bool _isRunning = false;
    private Task? _readTask;
    private readonly CancellationTokenSource _cancellationTokenSource;

    public event EventHandler<WeightChangedEventArgs>? WeightChanged;

    public ScaleReaderService(ILogger<ScaleReaderService> logger, IConfiguration configuration)
    {
        _logger = logger;
        _configuration = configuration;
        _cancellationTokenSource = new CancellationTokenSource();
    }

    public bool IsConnected => _serialPort?.IsOpen ?? false;

    public double CurrentWeight => _currentWeight;

    public void Start()
    {
        if (_isRunning)
        {
            _logger.LogWarning("Scale reader is already running");
            return;
        }

        var portName = _configuration["Scale:PortName"] ?? "COM3";
        var baudRate = _configuration.GetValue<int>("Scale:BaudRate", 9600);
        var dataBits = _configuration.GetValue<int>("Scale:DataBits", 8);
        var parityString = _configuration["Scale:Parity"] ?? "None";
        var stopBitsString = _configuration["Scale:StopBits"] ?? "One";

        try
        {
            _serialPort = new SerialPort(portName)
            {
                BaudRate = baudRate,
                DataBits = dataBits,
                Parity = Enum.Parse<Parity>(parityString),
                StopBits = Enum.Parse<StopBits>(stopBitsString),
                Handshake = Handshake.None,
                ReadTimeout = 500,
                WriteTimeout = 500
            };

            _serialPort.Open();
            _logger.LogInformation($"Successfully connected to scale on {portName} at {baudRate} baud");

            _isRunning = true;
            _readTask = Task.Run(() => ReadScaleData(_cancellationTokenSource.Token));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to connect to scale");
            throw;
        }
    }

    private async Task ReadScaleData(CancellationToken cancellationToken)
    {
        var buffer = new StringBuilder();

        while (_isRunning && !cancellationToken.IsCancellationRequested)
        {
            try
            {
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
                            ProcessScaleLine(line.Trim());
                        }
                        buffer.Clear();
                    }
                    else if (lines.Length > 1)
                    {
                        // Process all but the last incomplete line
                        for (int i = 0; i < lines.Length - 1; i++)
                        {
                            ProcessScaleLine(lines[i].Trim());
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
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error reading from scale");
                await Task.Delay(1000, cancellationToken); // Wait before retrying
            }
        }
    }

    private void ProcessScaleLine(string line)
    {
        try
        {
            // CAS PD-2Z protocol typically sends data in format:
            // ST,GS,+00000lb,CR (Stable, Gross, weight in pounds)
            // or ST,NT,+00000lb,CR (Stable, Net, weight in pounds)
            // Format may vary: Check actual protocol from manufacturer

            _logger.LogDebug($"Raw scale data: {line}");

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

            if (weight >= 0)
            {
                var oldWeight = _currentWeight;
                _currentWeight = weight;

                if (Math.Abs(oldWeight - weight) > 0.01) // Only notify if changed significantly
                {
                    _logger.LogDebug($"Weight changed: {weight:F2} lbs");
                    WeightChanged?.Invoke(this, new WeightChangedEventArgs(weight));
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, $"Failed to parse scale data: {line}");
        }
    }

    private double ParseWeightString(string weightStr)
    {
        // Remove common characters: spaces, 'lb', 'lbs', 'kg', etc.
        var cleaned = weightStr
            .Replace("lb", "", StringComparison.OrdinalIgnoreCase)
            .Replace("lbs", "", StringComparison.OrdinalIgnoreCase)
            .Replace("kg", "", StringComparison.OrdinalIgnoreCase)
            .Replace(" ", "")
            .Trim();

        if (double.TryParse(cleaned, out var weight))
        {
            return Math.Abs(weight); // Return absolute value
        }

        return 0;
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

        if (_serialPort?.IsOpen == true)
        {
            _serialPort.Close();
            _logger.LogInformation("Scale connection closed");
        }
    }

    public List<string> GetAvailablePorts()
    {
        return SerialPort.GetPortNames().ToList();
    }

    public void Dispose()
    {
        Stop();
        _serialPort?.Dispose();
        _cancellationTokenSource.Dispose();
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
