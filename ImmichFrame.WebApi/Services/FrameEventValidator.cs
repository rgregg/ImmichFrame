using System;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using ImmichFrame.Core.Events;
using ImmichFrame.Core.Interfaces;
using ImmichFrame.WebApi.Models.Events;

namespace ImmichFrame.WebApi.Services;

public class FrameEventValidator
{
    private readonly IGeneralSettings _settings;

    public FrameEventValidator(IGeneralSettings settings)
    {
        _settings = settings;
    }

    public FrameEvent Validate(FrameEventRequestDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.DeviceId))
        {
            throw new ValidationException("deviceId is required");
        }

        if (string.IsNullOrWhiteSpace(dto.Id))
        {
            throw new ValidationException("id is required");
        }

        if (string.IsNullOrWhiteSpace(dto.Type))
        {
            throw new ValidationException("type is required");
        }

        if (!dto.Type.StartsWith("frame.ui.", StringComparison.OrdinalIgnoreCase))
        {
            throw new ValidationException("type must start with 'frame.ui.'");
        }

        var timeoutMs = dto.TimeoutMs ?? _settings.EventDefaultTimeoutMs;
        if (timeoutMs < 0)
        {
            throw new ValidationException("timeoutMs must be >= 0");
        }

        string? normalizedUrl = null;
        string? normalizedOrigin = dto.Security?.Origin;

        switch (dto.Mode)
        {
            case FrameEventMode.Popup:
            case FrameEventMode.Cover:
                if (string.IsNullOrWhiteSpace(dto.Url))
                {
                    throw new ValidationException("url is required for popup and cover modes");
                }

                if (!Uri.TryCreate(dto.Url, UriKind.Absolute, out var parsedUrl) ||
                    (parsedUrl.Scheme != Uri.UriSchemeHttps && parsedUrl.Scheme != Uri.UriSchemeHttp))
                {
                    throw new ValidationException("url must be an absolute HTTP(S) URL");
                }

                normalizedUrl = parsedUrl.ToString();
                normalizedOrigin ??= parsedUrl.GetLeftPart(UriPartial.Authority);

                if (_settings.EventAllowedOrigins.Count > 0)
                {
                    if (string.IsNullOrWhiteSpace(normalizedOrigin))
                    {
                        throw new ValidationException("security.origin is required when origin allowlist is configured");
                    }

                    if (!_settings.EventAllowedOrigins.Contains(normalizedOrigin, StringComparer.OrdinalIgnoreCase))
                    {
                        throw new ValidationException($"Origin '{normalizedOrigin}' is not allowed");
                    }
                }
                break;

            case FrameEventMode.PopupText:
                if (string.IsNullOrWhiteSpace(dto.Message))
                {
                    throw new ValidationException("message is required for popupText mode");
                }
                break;

            case FrameEventMode.Close:
                // no additional validation
                break;

            default:
                throw new ValidationException("mode is not supported");
        }

        var sandbox = dto.Security?.Sandbox?.Count > 0
            ? dto.Security!.Sandbox
            : _settings.EventDefaultSandbox.ToList();

        IReadOnlyList<FrameEventAction> actions = dto.Actions?.Count > 0
            ? dto.Actions.ConvertAll(a => a.ToDomain())
            : Array.Empty<FrameEventAction>();

        var input = dto.Input?.ToDomain() ?? new FrameEventInput();

        return new FrameEvent
        {
            DeviceId = dto.DeviceId,
            Id = dto.Id,
            Type = dto.Type,
            Mode = dto.Mode,
            Url = normalizedUrl ?? dto.Url,
            Message = dto.Message,
            TimeoutMs = timeoutMs,
            Priority = dto.Priority,
            Category = string.IsNullOrWhiteSpace(dto.Category) ? null : dto.Category,
            Title = dto.Title,
            Meta = dto.Meta,
            Actions = actions,
            Input = input,
            Security = new FrameEventSecurity
            {
                Origin = normalizedOrigin,
                Sandbox = sandbox,
                Signature = dto.Security?.Signature
            },
            PostedAt = dto.PostedAt?.ToUniversalTime() ?? DateTime.UtcNow
        };
    }
}
