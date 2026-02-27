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

    public async Task UpdateTimelineChart<T>(IEnumerable<T> data)
    {
        await _jsRuntime.InvokeVoidAsync("JSInterop.updateTimelineChart", data);
    }
}
