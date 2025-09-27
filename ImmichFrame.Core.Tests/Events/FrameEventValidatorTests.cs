using System.ComponentModel.DataAnnotations;
using ImmichFrame.Core.Events;
using ImmichFrame.Core.Interfaces;
using ImmichFrame.WebApi.Models.Events;
using ImmichFrame.WebApi.Services;
using Moq;
using NUnit.Framework;

namespace ImmichFrame.Core.Tests.Events;

[TestFixture]
public class FrameEventValidatorTests
{
    private static FrameEventValidator CreateValidator(
        IEnumerable<string>? allowedOrigins = null,
        IEnumerable<string>? defaultSandbox = null,
        int defaultTimeoutMs = 15000)
    {
        var generalSettings = new Mock<IGeneralSettings>();
        generalSettings.SetupGet(g => g.EventAllowedOrigins)
            .Returns((allowedOrigins ?? Array.Empty<string>()).ToList());
        generalSettings.SetupGet(g => g.EventDefaultSandbox)
            .Returns((defaultSandbox ?? new[] { "allow-scripts", "allow-same-origin" }).ToList());
        generalSettings.SetupGet(g => g.EventDefaultTimeoutMs).Returns(defaultTimeoutMs);

        return new FrameEventValidator(generalSettings.Object);
    }

    private static FrameEventRequestDto CreateBaseDto() => new()
    {
        DeviceId = "device-1",
        Id = "evt-1",
        Type = "frame.ui.v1",
        Mode = FrameEventMode.Popup,
        Url = "https://app.example.com/overlay"
    };

    [Test]
    public void Validate_Throws_WhenRequiredFieldsMissing()
    {
        var validator = CreateValidator();
        var dto = new FrameEventRequestDto();

        Assert.That(() => validator.Validate(dto), Throws.InstanceOf<ValidationException>());
    }

    [Test]
    public void Validate_Throws_WhenUrlMissingForPopup()
    {
        var validator = CreateValidator();
        var dto = CreateBaseDto();
        dto.Url = null;

        Assert.That(() => validator.Validate(dto), Throws.InstanceOf<ValidationException>());
    }

    [Test]
    public void Validate_Throws_WhenUrlInvalid()
    {
        var validator = CreateValidator();
        var dto = CreateBaseDto();
        dto.Url = "ftp://example.com";

        Assert.That(() => validator.Validate(dto), Throws.InstanceOf<ValidationException>());
    }

    [Test]
    public void Validate_AllowsPopupText_WhenMessageProvided()
    {
        var validator = CreateValidator();
        var dto = CreateBaseDto();
        dto.Mode = FrameEventMode.PopupText;
        dto.Url = null;
        dto.Message = "Hello";

        var result = validator.Validate(dto);

        Assert.Multiple(() =>
        {
            Assert.That(result.Mode, Is.EqualTo(FrameEventMode.PopupText));
            Assert.That(result.Message, Is.EqualTo("Hello"));
        });
    }

    [Test]
    public void Validate_Throws_ForPopupTextWithoutMessage()
    {
        var validator = CreateValidator();
        var dto = CreateBaseDto();
        dto.Mode = FrameEventMode.PopupText;
        dto.Url = null;
        dto.Message = null;

        Assert.That(() => validator.Validate(dto), Throws.InstanceOf<ValidationException>());
    }

    [Test]
    public void Validate_UsesDefaultTimeout_WhenNotProvided()
    {
        var validator = CreateValidator(defaultTimeoutMs: 5000);
        var dto = CreateBaseDto();
        dto.TimeoutMs = null;

        var result = validator.Validate(dto);

        Assert.That(result.TimeoutMs, Is.EqualTo(5000));
    }

    [Test]
    public void Validate_EnforcesAllowedOrigins()
    {
        var validator = CreateValidator(allowedOrigins: new[] { "https://trusted.example" });
        var dto = CreateBaseDto();
        dto.Url = "https://trusted.example/app";

        Assert.DoesNotThrow(() => validator.Validate(dto));

        dto.Url = "https://untrusted.example/app";

        Assert.That(() => validator.Validate(dto), Throws.InstanceOf<ValidationException>());
    }

    [Test]
    public void Validate_DefaultsSandboxWhenMissing()
    {
        var validator = CreateValidator(defaultSandbox: new[] { "allow-scripts" });
        var dto = CreateBaseDto();
        dto.Security = new FrameEventSecurityDto();

        var result = validator.Validate(dto);

        Assert.That(result.Security.Sandbox, Is.EquivalentTo(new[] { "allow-scripts" }));
    }

    [Test]
    public void Validate_PreservesProvidedSandbox()
    {
        var validator = CreateValidator(defaultSandbox: new[] { "allow-scripts" });
        var dto = CreateBaseDto();
        dto.Security = new FrameEventSecurityDto
        {
            Sandbox = new List<string> { "allow-popups" }
        };

        var result = validator.Validate(dto);

        Assert.That(result.Security.Sandbox, Is.EquivalentTo(new[] { "allow-popups" }));
    }

    [Test]
    public void Validate_NormalizesOrigin()
    {
        var validator = CreateValidator();
        var dto = CreateBaseDto();
        dto.Url = "https://trusted.example/app";
        dto.Security = new FrameEventSecurityDto { Origin = "https://trusted.example" };

        var result = validator.Validate(dto);

        Assert.That(result.Security.Origin, Is.EqualTo("https://trusted.example"));
    }
}
