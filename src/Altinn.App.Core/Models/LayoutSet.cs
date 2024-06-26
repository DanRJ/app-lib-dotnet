namespace Altinn.App.Core.Models;

/// <summary>
/// A specific layoutset
/// </summary>
public class LayoutSet
{
    /// <summary>
    /// LayoutsetId for layout. This is the foldername
    /// </summary>
#nullable disable
    public string Id { get; set; }

    /// <summary>
    /// DataType for layout
    /// </summary>
    public string DataType { get; set; }

    /// <summary>
    /// List of tasks where layuout should be used
    /// </summary>
    public List<string> Tasks { get; set; }
#nullable restore
}
