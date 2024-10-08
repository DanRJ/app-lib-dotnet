using System.Security.Claims;
using Altinn.App.Core.Configuration;
using Altinn.App.Core.Extensions;
using Altinn.App.Core.Features;
using Altinn.App.Core.Helpers.Extensions;
using Altinn.App.Core.Internal.App;
using Altinn.App.Core.Internal.Data;
using Altinn.App.Core.Internal.Language;
using Altinn.App.Core.Internal.Profile;
using Altinn.App.Core.Models;
using Altinn.Platform.Profile.Models;
using Altinn.Platform.Storage.Interface.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;

namespace Altinn.App.Core.Internal.Pdf;

/// <summary>
/// Service for handling the creation and storage of receipt Pdf.
/// </summary>
public class PdfService : IPdfService
{
    private readonly IAppResources _resourceService;
    private readonly IDataClient _dataClient;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly IProfileClient _profileClient;

    private readonly IPdfGeneratorClient _pdfGeneratorClient;
    private readonly PdfGeneratorSettings _pdfGeneratorSettings;
    private readonly GeneralSettings _generalSettings;
    private readonly Telemetry? _telemetry;
    private const string PdfElementType = "ref-data-as-pdf";
    private const string PdfContentType = "application/pdf";

    /// <summary>
    /// Initializes a new instance of the <see cref="PdfService"/> class.
    /// </summary>
    /// <param name="appResources">The service giving access to local resources.</param>
    /// <param name="dataClient">The data client.</param>
    /// <param name="httpContextAccessor">The httpContextAccessor</param>
    /// <param name="profileClient">The profile client</param>
    /// <param name="pdfGeneratorClient">PDF generator client for the experimental PDF generator service</param>
    /// <param name="pdfGeneratorSettings">PDF generator related settings.</param>
    /// <param name="generalSettings">The app general settings.</param>
    /// <param name="telemetry">Telemetry for metrics and traces.</param>
    public PdfService(
        IAppResources appResources,
        IDataClient dataClient,
        IHttpContextAccessor httpContextAccessor,
        IProfileClient profileClient,
        IPdfGeneratorClient pdfGeneratorClient,
        IOptions<PdfGeneratorSettings> pdfGeneratorSettings,
        IOptions<GeneralSettings> generalSettings,
        Telemetry? telemetry = null
    )
    {
        _resourceService = appResources;
        _dataClient = dataClient;
        _httpContextAccessor = httpContextAccessor;
        _profileClient = profileClient;
        _pdfGeneratorClient = pdfGeneratorClient;
        _pdfGeneratorSettings = pdfGeneratorSettings.Value;
        _generalSettings = generalSettings.Value;
        _telemetry = telemetry;
    }

    /// <inheritdoc/>
    public async Task GenerateAndStorePdf(Instance instance, string taskId, CancellationToken ct)
    {
        using var activity = _telemetry?.StartGenerateAndStorePdfActivity(instance, taskId);

        HttpContext? httpContext = _httpContextAccessor.HttpContext;
        var queries = httpContext?.Request.Query;
        var user = httpContext?.User;

        var language = GetOverriddenLanguage(queries) ?? await GetLanguage(user);

        var pdfContent = await GeneratePdfContent(instance, taskId, language, ct);

        var appIdentifier = new AppIdentifier(instance);

        TextResource? textResource = await GetTextResource(appIdentifier.App, appIdentifier.Org, language);
        string fileName = GetFileName(instance, textResource);
        await _dataClient.InsertBinaryData(instance.Id, PdfElementType, PdfContentType, fileName, pdfContent, taskId);
    }

    /// <inheritdoc/>
    public async Task<Stream> GeneratePdf(Instance instance, string taskId, CancellationToken ct)
    {
        using var activity = _telemetry?.StartGeneratePdfActivity(instance, taskId);

        HttpContext? httpContext = _httpContextAccessor.HttpContext;
        var queries = httpContext?.Request.Query;
        var user = httpContext?.User;

        var language = GetOverriddenLanguage(queries) ?? await GetLanguage(user);

        return await GeneratePdfContent(instance, taskId, language, ct);
    }

    private async Task<Stream> GeneratePdfContent(
        Instance instance,
        string taskId,
        string language,
        CancellationToken ct
    )
    {
        var baseUrl = _generalSettings.FormattedExternalAppBaseUrl(new AppIdentifier(instance));
        var pagePath = _pdfGeneratorSettings
            .AppPdfPagePathTemplate.ToLowerInvariant()
            .Replace("{instanceid}", instance.Id);

        Uri uri = BuildUri(baseUrl, pagePath, language);

        Stream pdfContent = await _pdfGeneratorClient.GeneratePdf(uri, ct);

        return pdfContent;
    }

    private static Uri BuildUri(string baseUrl, string pagePath, string language)
    {
        // Uses string manipulation instead of UriBuilder, since UriBuilder messes up
        // query parameters in combination with hash fragments in the url.
        string url = baseUrl + pagePath;
        if (url.Contains('?'))
        {
            url += $"&lang={language}";
        }
        else
        {
            url += $"?lang={language}";
        }

        return new Uri(url);
    }

    internal async Task<string> GetLanguage(ClaimsPrincipal? user)
    {
        string language = LanguageConst.Nb;

        if (user is null)
        {
            return language;
        }

        int? userId = user.GetUserIdAsInt();

        if (userId is not null)
        {
            UserProfile userProfile =
                await _profileClient.GetUserProfile((int)userId)
                ?? throw new Exception("Could not get user profile while getting language");

            if (!string.IsNullOrEmpty(userProfile.ProfileSettingPreference?.Language))
            {
                language = userProfile.ProfileSettingPreference.Language;
            }
        }

        return language;
    }

    internal static string? GetOverriddenLanguage(IQueryCollection? queries)
    {
        if (queries is null)
        {
            return null;
        }

        if (
            queries.TryGetValue("language", out StringValues queryLanguage)
            || queries.TryGetValue("lang", out queryLanguage)
        )
        {
            return queryLanguage.ToString();
        }

        return null;
    }

    private async Task<TextResource?> GetTextResource(string app, string org, string language)
    {
        TextResource? textResource = await _resourceService.GetTexts(org, app, language);

        if (textResource == null && language != LanguageConst.Nb)
        {
            // fallback to norwegian if texts does not exist
            textResource = await _resourceService.GetTexts(org, app, LanguageConst.Nb);
        }

        return textResource;
    }

    private static string GetFileName(Instance instance, TextResource? textResource)
    {
        string? fileName = null;
        string app = instance.AppId.Split("/")[1];

        fileName = $"{app}.pdf";

        if (textResource is null)
        {
            return GetValidFileName(fileName);
        }

        TextResourceElement? titleText =
            textResource.Resources.Find(textResourceElement =>
                textResourceElement.Id.Equals("appName", StringComparison.Ordinal)
            )
            ?? textResource.Resources.Find(textResourceElement =>
                textResourceElement.Id.Equals("ServiceName", StringComparison.Ordinal)
            );

        if (titleText is not null && !string.IsNullOrEmpty(titleText.Value))
        {
            fileName = titleText.Value + ".pdf";
        }

        return GetValidFileName(fileName);
    }

    private static string GetValidFileName(string fileName)
    {
        fileName = Uri.EscapeDataString(fileName.AsFileName(false));
        return fileName;
    }
}
