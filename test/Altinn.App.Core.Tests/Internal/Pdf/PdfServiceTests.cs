using System.Net;
using Altinn.App.Common.Tests;
using Altinn.App.Core.Configuration;
using Altinn.App.Core.Infrastructure.Clients.Pdf;
using Altinn.App.Core.Internal.App;
using Altinn.App.Core.Internal.Auth;
using Altinn.App.Core.Internal.Data;
using Altinn.App.Core.Internal.Pdf;
using Altinn.App.Core.Internal.Profile;
using Altinn.App.PlatformServices.Tests.Helpers;
using Altinn.App.PlatformServices.Tests.Mocks;
using Altinn.Platform.Storage.Interface.Models;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using Moq;

namespace Altinn.App.PlatformServices.Tests.Internal.Pdf;

public class PdfServiceTests
{
    private const string HostName = "at22.altinn.cloud";

    private readonly Mock<IAppResources> _appResources = new();
    private readonly Mock<IDataClient> _dataClient = new();
    private readonly Mock<IHttpContextAccessor> _httpContextAccessor = new();
    private readonly Mock<IPdfGeneratorClient> _pdfGeneratorClient = new();
    private readonly Mock<IProfileClient> _profile = new();
    private readonly IOptions<PdfGeneratorSettings> _pdfGeneratorSettingsOptions =
        Microsoft.Extensions.Options.Options.Create<PdfGeneratorSettings>(new() { });

    private readonly IOptions<GeneralSettings> _generalSettingsOptions =
        Microsoft.Extensions.Options.Options.Create<GeneralSettings>(new() { HostName = HostName });

    private readonly IOptions<PlatformSettings> _platformSettingsOptions =
        Microsoft.Extensions.Options.Options.Create<PlatformSettings>(new() { });

    private readonly Mock<IUserTokenProvider> _userTokenProvider;

    public PdfServiceTests()
    {
        var resource = new TextResource()
        {
            Id = "digdir-not-really-an-app-nb",
            Language = "nb",
            Org = "digdir",
            Resources = new List<TextResourceElement>()
        };
        _appResources
            .Setup(s => s.GetTexts(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(resource);

        DefaultHttpContext httpContext = new();
        httpContext.Request.Protocol = "https";
        httpContext.Request.Host = new(HostName);
        _httpContextAccessor.Setup(s => s.HttpContext!).Returns(httpContext);

        _userTokenProvider = new Mock<IUserTokenProvider>();
        _userTokenProvider.Setup(s => s.GetUserToken()).Returns("usertoken");
    }

    [Fact]
    public async Task ValidRequest_ShouldReturnPdf()
    {
        DelegatingHandlerStub delegatingHandler =
            new(
                async (HttpRequestMessage request, CancellationToken token) =>
                {
                    await Task.CompletedTask;
                    return new HttpResponseMessage()
                    {
                        Content = new StreamContent(
                            EmbeddedResource.LoadDataAsStream("Altinn.App.Core.Tests.Internal.Pdf.TestData.example.pdf")
                        )
                    };
                }
            );

        var httpClient = new HttpClient(delegatingHandler);
        var pdfGeneratorClient = new PdfGeneratorClient(
            httpClient,
            _pdfGeneratorSettingsOptions,
            _platformSettingsOptions,
            _userTokenProvider.Object,
            _httpContextAccessor.Object
        );

        Stream pdf = await pdfGeneratorClient.GeneratePdf(
            new Uri(@"https://org.apps.hostName/appId/#/instance/instanceId"),
            CancellationToken.None
        );

        pdf.Length.Should().Be(17814L);
    }

    [Fact]
    public async Task ValidRequest_PdfGenerationFails_ShouldThrowException()
    {
        DelegatingHandlerStub delegatingHandler =
            new(
                async (HttpRequestMessage request, CancellationToken token) =>
                {
                    await Task.CompletedTask;
                    return new HttpResponseMessage() { StatusCode = HttpStatusCode.RequestTimeout };
                }
            );

        var httpClient = new HttpClient(delegatingHandler);
        var pdfGeneratorClient = new PdfGeneratorClient(
            httpClient,
            _pdfGeneratorSettingsOptions,
            _platformSettingsOptions,
            _userTokenProvider.Object,
            _httpContextAccessor.Object
        );

        var func = async () =>
            await pdfGeneratorClient.GeneratePdf(
                new Uri(@"https://org.apps.hostName/appId/#/instance/instanceId"),
                CancellationToken.None
            );

        await func.Should().ThrowAsync<PdfGenerationException>();
    }

    [Fact]
    public async Task GenerateAndStorePdf()
    {
        // Arrange
        TelemetrySink telemetrySink = new();
        _pdfGeneratorClient.Setup(s => s.GeneratePdf(It.IsAny<Uri>(), It.IsAny<CancellationToken>()));
        _generalSettingsOptions.Value.ExternalAppBaseUrl = "https://{org}.apps.{hostName}/{org}/{app}";

        var target = new PdfService(
            _appResources.Object,
            _dataClient.Object,
            _httpContextAccessor.Object,
            _profile.Object,
            _pdfGeneratorClient.Object,
            _pdfGeneratorSettingsOptions,
            _generalSettingsOptions,
            telemetrySink.Object
        );

        Instance instance =
            new()
            {
                Id = $"509378/{Guid.NewGuid()}",
                AppId = "digdir/not-really-an-app",
                Org = "digdir"
            };

        // Act
        await target.GenerateAndStorePdf(instance, "Task_1", CancellationToken.None);

        // Asserts
        _pdfGeneratorClient.Verify(
            s =>
                s.GeneratePdf(
                    It.Is<Uri>(u =>
                        u.Scheme == "https"
                        && u.Host == $"{instance.Org}.apps.{HostName}"
                        && u.AbsoluteUri.Contains(instance.AppId)
                        && u.AbsoluteUri.Contains(instance.Id)
                    ),
                    It.IsAny<CancellationToken>()
                ),
            Times.Once
        );

        _dataClient.Verify(
            s =>
                s.InsertBinaryData(
                    It.Is<string>(s => s == instance.Id),
                    It.Is<string>(s => s == "ref-data-as-pdf"),
                    It.Is<string>(s => s == "application/pdf"),
                    It.Is<string>(s => s == "not-really-an-app.pdf"),
                    It.IsAny<Stream>(),
                    It.Is<string>(s => s == "Task_1")
                ),
            Times.Once
        );

        await Verify(telemetrySink.GetSnapshot());
    }

    [Fact]
    public async Task GenerateAndStorePdf_with_generatedFrom()
    {
        // Arrange
        _pdfGeneratorClient.Setup(s => s.GeneratePdf(It.IsAny<Uri>(), It.IsAny<CancellationToken>()));

        _generalSettingsOptions.Value.ExternalAppBaseUrl = "https://{org}.apps.{hostName}/{org}/{app}";

        var target = new PdfService(
            _appResources.Object,
            _dataClient.Object,
            _httpContextAccessor.Object,
            _profile.Object,
            _pdfGeneratorClient.Object,
            _pdfGeneratorSettingsOptions,
            _generalSettingsOptions
        );

        var dataModelId = Guid.NewGuid();
        var attachmentId = Guid.NewGuid();

        Instance instance =
            new()
            {
                Id = $"509378/{Guid.NewGuid()}",
                AppId = "digdir/not-really-an-app",
                Org = "digdir",
                Process = new() { CurrentTask = new() { ElementId = "Task_1" } },
                Data = new()
                {
                    new() { Id = dataModelId.ToString(), DataType = "Model" },
                    new() { Id = attachmentId.ToString(), DataType = "attachment" }
                }
            };

        // Act
        await target.GenerateAndStorePdf(instance, "Task_1", CancellationToken.None);

        // Asserts
        _pdfGeneratorClient.Verify(
            s =>
                s.GeneratePdf(
                    It.Is<Uri>(u =>
                        u.Scheme == "https"
                        && u.Host == $"{instance.Org}.apps.{HostName}"
                        && u.AbsoluteUri.Contains(instance.AppId)
                        && u.AbsoluteUri.Contains(instance.Id)
                    ),
                    It.IsAny<CancellationToken>()
                ),
            Times.Once
        );

        _dataClient.Verify(
            s =>
                s.InsertBinaryData(
                    It.Is<string>(s => s == instance.Id),
                    It.Is<string>(s => s == "ref-data-as-pdf"),
                    It.Is<string>(s => s == "application/pdf"),
                    It.Is<string>(s => s == "not-really-an-app.pdf"),
                    It.IsAny<Stream>(),
                    It.Is<string>(s => s == "Task_1")
                ),
            Times.Once
        );
    }
}
