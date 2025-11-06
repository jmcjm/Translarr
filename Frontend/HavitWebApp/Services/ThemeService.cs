using Microsoft.JSInterop;

namespace Translarr.Frontend.HavitWebApp.Services;

public class ThemeService
{
    private readonly IJSRuntime _jsRuntime;
    private string _currentTheme = ThemeMode.Light;

    public event Action? OnThemeChanged;

    public ThemeService(IJSRuntime jsRuntime)
    {
        _jsRuntime = jsRuntime;
    }

    public string CurrentTheme => _currentTheme;

    public async Task InitializeAsync()
    {
        try
        {
            var savedTheme = await _jsRuntime.InvokeAsync<string>("localStorage.getItem", "theme");
            _currentTheme = string.IsNullOrEmpty(savedTheme) ? ThemeMode.Light : savedTheme;
            await ApplyThemeAsync();
        }
        catch
        {
            _currentTheme = ThemeMode.Light;
        }
    }

    public async Task SetThemeAsync(string theme)
    {
        _currentTheme = theme;
        await ApplyThemeAsync();
        OnThemeChanged?.Invoke();
    }

    private async Task ApplyThemeAsync()
    {
        try
        {
            // Dracula używa custom data-theme attribute
            if (_currentTheme == ThemeMode.Dracula)
            {
                await _jsRuntime.InvokeVoidAsync("eval",
                    "document.documentElement.setAttribute('data-theme', 'dracula'); document.documentElement.removeAttribute('data-bs-theme')");
            }
            else
            {
                // Light/Dark używają Bootstrap data-bs-theme
                await _jsRuntime.InvokeVoidAsync("eval",
                    $"document.documentElement.setAttribute('data-bs-theme', '{_currentTheme}'); document.documentElement.removeAttribute('data-theme')");
            }

            await _jsRuntime.InvokeVoidAsync("localStorage.setItem", "theme", _currentTheme);
        }
        catch
        {
            // JS interop may not be available during prerendering
        }
    }
}

public static class ThemeMode
{
    public const string Light = "light";
    public const string Dark = "dark";
    public const string Dracula = "dracula";
}

