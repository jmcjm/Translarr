# Multi-LLM Support Design

## Context

Translarr currently uses `Mscc.GenerativeAI` (Gemini-specific SDK) for subtitle translation. This locks the app to a single provider. Google Gemini, OpenAI, Anthropic Claude, xAI Grok, DeepSeek, and Ollama all expose OpenAI-compatible chat completion APIs. Switching to the OpenAI SDK with a configurable base URL supports all of them with one client.

POC confirmed: OpenAI SDK with Gemini's compatibility layer (`https://generativelanguage.googleapis.com/v1beta/openai/`) produces identical results to the native Gemini SDK for subtitle translation.

## Architecture

### Single SDK, Configurable Provider

Replace `Mscc.GenerativeAI` with `OpenAI` NuGet package. One `OpenAIClient` instance configured with user-provided base URL + API key. No provider-specific code, no adapter pattern, no factory.

API stores `LlmBaseUrl`, `LlmApiKey`, `LlmModel`, `Temperature`, `SystemPrompt`. API does not know or care about provider names.

Frontend has a hardcoded dictionary mapping provider names to base URLs. Dropdown selects provider → auto-fills base URL (readonly). "Custom" option makes base URL editable. On settings load, frontend matches stored `LlmBaseUrl` against dictionary to set dropdown state.

## Data Layer (Settings)

### Setting Key Renames

| Old Key | New Key |
|---|---|
| `GeminiApiKey` | `LlmApiKey` |
| `GeminiModel` | `LlmModel` |
| `Temperature` | `Temperature` (unchanged) |
| `SystemPrompt` | `SystemPrompt` (unchanged) |

### New Setting

| Key | Default Value |
|---|---|
| `LlmBaseUrl` | `https://generativelanguage.googleapis.com/v1beta/openai/` |

### Migration Strategy

`TranslarrDatabaseInitializer` checks for old key names on startup. If found, renames them (UPDATE key, not delete+create) to preserve user values. New installs get new names directly.

### DTO Rename

`GeminiSettingsDto` → `LlmSettingsDto` with fields: `ApiKey`, `BaseUrl`, `Model`, `Temperature`, `SystemPrompt`.

## Infrastructure - OpenAI SDK

### Remove

- `Mscc.GenerativeAI` package
- `Core/Infrastructure/Services/GeminiClient.cs`
- `Core/Application/Abstractions/Services/IGeminiClient.cs`
- `Core/Application/Models/GeminiSettingsDto.cs`

### Add

- `OpenAI` package
- `Core/Application/Abstractions/Services/ISubtitleTranslator.cs`
- `Core/Application/Models/LlmSettingsDto.cs`
- `Core/Infrastructure/Services/OpenAiSubtitleTranslator.cs`

### ISubtitleTranslator Interface

```csharp
public interface ISubtitleTranslator
{
    Task<string> TranslateSubtitlesAsync(string content, LlmSettingsDto settings);
}
```

### OpenAiSubtitleTranslator Implementation

- Creates `OpenAIClient` with `settings.BaseUrl` + `settings.ApiKey`
- Gets `ChatClient` for `settings.Model`
- Sends system prompt + user content via `CompleteChatAsync`
- Checks `FinishReason` for `ContentFilter` → throws error
- Checks empty response → throws error
- Returns response text
- Strips markdown code blocks from response (existing regex logic)

### DI Registration

`services.AddScoped<ISubtitleTranslator, OpenAiSubtitleTranslator>()` replaces `IGeminiClient, GeminiClient`.

## Application Layer Changes

### SubtitleTranslationService

- Constructor: `IGeminiClient geminiClient` → `ISubtitleTranslator subtitleTranslator`
- `geminiClient.TranslateSubtitlesAsync(content, settings)` → `subtitleTranslator.TranslateSubtitlesAsync(content, settings)`
- `settingsService.GetGeminiSettingsAsync()` → `settingsService.GetLlmSettingsAsync()`

### ISettingsService

- `GetGeminiSettingsAsync()` → `GetLlmSettingsAsync()` returning `LlmSettingsDto`

## Frontend - Provider Dropdown

### Provider Dictionary (frontend-only, hardcoded)

| Provider | Base URL | Suggested Models |
|---|---|---|
| Google Gemini | `https://generativelanguage.googleapis.com/v1beta/openai/` | `gemini-2.5-flash`, `gemini-2.5-pro` |
| OpenAI | `https://api.openai.com/v1/` | `gpt-4o`, `gpt-4o-mini` |
| Anthropic Claude | `https://api.anthropic.com/v1/` | `claude-sonnet-4-6`, `claude-haiku-4-5` |
| Grok (xAI) | `https://api.x.ai/v1/` | `grok-3`, `grok-3-mini` |
| DeepSeek | `https://api.deepseek.com/v1/` | `deepseek-chat`, `deepseek-reasoner` |
| Custom | (user edits) | (user edits) |

### Settings Page UI

Section renamed from "Gemini API Configuration" to "LLM Configuration".

Fields:
1. **Provider** - dropdown with provider names from dictionary
2. **Base URL** - text field, readonly when predefined provider selected, editable when Custom
3. **API Key** - password field (unchanged behavior)
4. **Model** - dropdown with suggested models for selected provider + "Custom" option (then text input). Changing provider resets model to first suggested.
5. **Temperature** - range slider (unchanged behavior)
6. **System Prompt** - textarea (unchanged behavior)
7. **Test API Connection** - button, sends test request to configured provider

### UI Flow

1. On load: GET settings → match `LlmBaseUrl` against dictionary → set dropdown (or Custom if no match)
2. User changes dropdown → auto-fill base URL, set readonly
3. User selects Custom → base URL becomes editable
4. Save → POST all settings including `LlmBaseUrl`

## Safety/Error Handling

- Check `FinishReason == ChatFinishReason.ContentFilter` → throw with descriptive error
- Check null/empty response text → throw "empty response" error
- Both checks (belt and suspenders approach)

## Packages

### Remove from Directory.Packages.props

- `Mscc.GenerativeAI`

### Add to Directory.Packages.props

- `OpenAI` (latest stable)

### Infrastructure.csproj

- Remove: `<PackageReference Include="Mscc.GenerativeAI" />`
- Add: `<PackageReference Include="OpenAI" />`

## Files Changed/Created

### New Files
- `Core/Application/Abstractions/Services/ISubtitleTranslator.cs`
- `Core/Application/Models/LlmSettingsDto.cs`
- `Core/Infrastructure/Services/OpenAiSubtitleTranslator.cs`

### Deleted Files
- `Core/Infrastructure/Services/GeminiClient.cs`
- `Core/Application/Abstractions/Services/IGeminiClient.cs`
- `Core/Application/Models/GeminiSettingsDto.cs`

### Modified Files
- `Directory.Packages.props` - swap packages
- `Core/Infrastructure/Infrastructure.csproj` - swap package reference
- `Core/Infrastructure/DependencyInjection.cs` - swap DI registration
- `Core/Infrastructure/Services/TranslarrDatabaseInitializer.cs` - rename keys, add LlmBaseUrl
- `Core/Application/Services/SubtitleTranslationService.cs` - use ISubtitleTranslator + LlmSettingsDto
- `Core/Application/Abstractions/Services/ISettingsService.cs` - rename method
- `Core/Application/Services/SettingsService.cs` - rename method + new key names
- `Frontend/HavitWebApp/Components/Pages/Settings.razor` - provider dropdown, base URL field, renamed labels
