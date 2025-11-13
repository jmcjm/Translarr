using Microsoft.JSInterop;
using MudBlazor;

namespace Translarr.Frontend.MudBlazorWebApp.Services;

public class ThemeService
{
    private readonly IJSRuntime _jsRuntime;
    private MudTheme _currentTheme;
    private bool _isDarkMode;
    private string _currentThemeName = "default";

    public event Action? OnThemeChanged;

    public ThemeService(IJSRuntime jsRuntime)
    {
        _jsRuntime = jsRuntime;
        _currentTheme = GetDefaultTheme();
    }

    public MudTheme CurrentTheme => _currentTheme;
    public bool IsDarkMode => _isDarkMode;
    public string CurrentThemeName => _currentThemeName;

    public async Task InitializeAsync()
    {
        try
        {
            var savedTheme = await _jsRuntime.InvokeAsync<string>("localStorage.getItem", "selectedTheme");
            var savedMode = await _jsRuntime.InvokeAsync<string>("localStorage.getItem", "darkMode");

            if (!string.IsNullOrEmpty(savedTheme))
            {
                _currentThemeName = savedTheme;
                _currentTheme = GetThemeByName(savedTheme);
            }

            if (!string.IsNullOrEmpty(savedMode))
            {
                _isDarkMode = bool.Parse(savedMode);
            }
        }
        catch
        {
            // Ignore errors during initialization
        }
    }

    public async Task SetThemeAsync(string themeName, bool isDark)
    {
        _currentThemeName = themeName;
        _isDarkMode = isDark;
        _currentTheme = GetThemeByName(themeName);

        try
        {
            await _jsRuntime.InvokeVoidAsync("localStorage.setItem", "selectedTheme", themeName);
            await _jsRuntime.InvokeVoidAsync("localStorage.setItem", "darkMode", isDark.ToString());
        }
        catch
        {
            // Ignore storage errors
        }

        OnThemeChanged?.Invoke();
    }

    public List<ThemeOption> GetAvailableThemes()
    {
        return
        [
            new ThemeOption { Name = "default", DisplayName = "Default", SupportsDark = true },
            new ThemeOption { Name = "dracula", DisplayName = "Dracula", SupportsDark = false }
        ];
    }

    private MudTheme GetThemeByName(string themeName)
    {
        return themeName.ToLower() switch
        {
            "dracula" => GetDraculaTheme(),
            _ => GetDefaultTheme()
        };
    }

    private MudTheme GetDefaultTheme()
    {
        return new MudTheme
        {
            PaletteLight = new PaletteLight
            {
                Primary = Colors.Blue.Default,
                Secondary = Colors.Purple.Default,
                AppbarBackground = Colors.Blue.Default,
                Background = Colors.Gray.Lighten5,
                DrawerBackground = "#FFF",
                DrawerText = "rgba(0,0,0, 0.7)",
                Success = Colors.Green.Accent4
            },
            PaletteDark = new PaletteDark
            {
                Primary = Colors.Blue.Lighten1,
                Secondary = Colors.Purple.Lighten1,
                AppbarBackground = "#1E1E1E",
                Background = "#121212",
                Surface = "#1E1E1E",
                DrawerBackground = "#1E1E1E",
                DrawerText = "rgba(255,255,255, 0.7)",
                Success = Colors.Green.Accent3,
                Black = "#27272f",
                White = "rgba(255,255,255, 0.9)"
            },
            LayoutProperties = new LayoutProperties
            {
                DrawerWidthLeft = "260px",
                DrawerWidthRight = "300px"
            }
        };
    }

    private MudTheme GetDraculaTheme()
    {
        // Dracula color scheme: https://draculatheme.com/contribute
        return new MudTheme
        {
            PaletteDark = new PaletteDark
            {
                Primary = "#bd93f9",        // Purple
                Secondary = "#ff79c6",      // Pink
                Tertiary = "#8be9fd",       // Cyan
                AppbarBackground = "#282a36",
                Background = "#282a36",     // Background
                Surface = "#44475a",        // Current Line
                DrawerBackground = "#282a36",
                DrawerText = "#f8f8f2",     // Foreground
                Success = "#50fa7b",        // Green
                Info = "#8be9fd",           // Cyan
                Warning = "#ffb86c",        // Orange
                Error = "#ff5555",          // Red
                TextPrimary = "#f8f8f2",    // Foreground
                TextSecondary = "#6272a4",  // Comment
                Black = "#21222c",
                White = "#f8f8f2",
                ActionDefault = "#f8f8f2",
                ActionDisabled = "#6272a4",
                Divider = "#44475a",
                LinesDefault = "#44475a"
            },
            LayoutProperties = new LayoutProperties
            {
                DrawerWidthLeft = "260px",
                DrawerWidthRight = "300px"
            }
        };
    }
}

public class ThemeOption
{
    public required string Name { get; set; }
    public required string DisplayName { get; set; }
    public bool SupportsDark { get; set; }
}

