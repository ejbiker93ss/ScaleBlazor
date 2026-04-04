using Microsoft.JSInterop;

namespace ScaleBlazor.Client.Services;

public class JSInterop
{
    private readonly IJSRuntime _jsRuntime;

    public JSInterop(IJSRuntime jsRuntime)
    {
        _jsRuntime = jsRuntime;
    }

    public async Task PlaySound(string url)
    {
        await _jsRuntime.InvokeVoidAsync("JSInterop.playSound", url);
    }

    public async Task InitializeSound(string url)
    {
        await _jsRuntime.InvokeVoidAsync("JSInterop.initializeSound", url);
    }

    public async Task PrimeSound(string url)
    {
        await _jsRuntime.InvokeVoidAsync("JSInterop.primeSound", url);
    }

    public async Task ExitKioskMode()
    {
        await _jsRuntime.InvokeVoidAsync("JSInterop.exitKiosk");
    }

    public async Task UpdateDailyChart<T>(IEnumerable<T> data)
    {
        await _jsRuntime.InvokeVoidAsync("JSInterop.updateDailyChart", data);
    }

    public async Task UpdateTimelineChart(object data)
    {
        await _jsRuntime.InvokeVoidAsync("JSInterop.updateTimelineChart", data);
    }

    public async Task UpdateReportOverviewChart(object data)
    {
        try
        {
            await _jsRuntime.InvokeVoidAsync("updateReportOverviewChart", data);
        }
        catch (JSException)
        {
            await _jsRuntime.InvokeVoidAsync("JSInterop.updateReportOverviewChart", data);
        }
    }

    public async Task UpdateReportTrendsChart(object data)
    {
        try
        {
            await _jsRuntime.InvokeVoidAsync("updateReportTrendsChart", data);
        }
        catch (JSException)
        {
            await _jsRuntime.InvokeVoidAsync("JSInterop.updateReportTrendsChart", data);
        }
    }

    public async Task UpdateReportHourlyChart(object data)
    {
        try
        {
            await _jsRuntime.InvokeVoidAsync("updateReportHourlyChart", data);
        }
        catch (JSException)
        {
            await _jsRuntime.InvokeVoidAsync("JSInterop.updateReportHourlyChart", data);
        }
    }
}
