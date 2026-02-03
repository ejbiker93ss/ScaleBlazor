using Microsoft.JSInterop;
using ScaleBlazor.Shared;

namespace ScaleBlazor.Client.Services;

public class JSInterop
{
    private readonly IJSRuntime _jsRuntime;

    public JSInterop(IJSRuntime jsRuntime)
    {
        _jsRuntime = jsRuntime;
    }

    public async Task UpdateDailyChart(List<DailyAverage> data)
    {
        await _jsRuntime.InvokeVoidAsync("JSInterop.updateDailyChart", data);
    }

    public async Task UpdateTimelineChart(List<ScaleReading> data)
    {
        await _jsRuntime.InvokeVoidAsync("JSInterop.updateTimelineChart", data);
    }
}
