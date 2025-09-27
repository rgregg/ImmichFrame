using System.Collections.Generic;

namespace ImmichFrame.Core.Events;

public class FrameEventSecurity
{
    public string? Origin { get; init; }
    public IReadOnlyList<string> Sandbox { get; init; } = new List<string>();
    public string? Signature { get; init; }
}
