using Newtonsoft.Json;

namespace Altinn.App.Core.Models;

/// <summary>
/// Query response object
/// </summary>
public class QueryResponse<T>
{
    /// <summary>
    /// The number of items in this response.
    /// </summary>
    [JsonProperty(PropertyName = "count")]
    public long Count { get; set; }

    /// <summary>
    /// The current query.
    /// </summary>
    [JsonProperty(PropertyName = "self")]
#nullable disable
    public string Self { get; set; }

    /// <summary>
    /// A link to the next page.
    /// </summary>
    [JsonProperty(PropertyName = "next")]
    public string Next { get; set; }

    /// <summary>
    /// The metadata.
    /// </summary>
    [JsonProperty(PropertyName = "instances")]
    public List<T> Instances { get; set; }
#nullable restore
}
