namespace Altinn.App.Core.Helpers;

/// <summary>
/// Represents an error from the process "system".
/// </summary>
public class ProcessError
{
#nullable disable
    /// <summary>
    /// Gets or sets a machine readable error code or test resource key.
    /// </summary>
    public string Code { get; set; }

    /// <summary>
    /// Gets or sets a human readable error message.
    /// </summary>
    public string Text { get; set; }
#nullable restore
}
