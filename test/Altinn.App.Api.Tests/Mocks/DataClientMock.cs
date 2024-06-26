using System.Text.Json;
using Altinn.App.Api.Tests.Data;
using Altinn.App.Core.Extensions;
using Altinn.App.Core.Helpers.Serialization;
using Altinn.App.Core.Infrastructure.Clients.Storage;
using Altinn.App.Core.Internal.App;
using Altinn.App.Core.Internal.Data;
using Altinn.App.Core.Models;
using Altinn.Platform.Storage.Interface.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using DataElement = Altinn.Platform.Storage.Interface.Models.DataElement;

namespace App.IntegrationTests.Mocks.Services;

public class DataClientMock : IDataClient
{
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly IAppMetadata _appMetadata;

    private static readonly JsonSerializerOptions _jsonSerializerOptions =
        new() { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    private readonly ILogger<DataClientMock> _logger;

    public DataClientMock(
        IAppMetadata appMetadata,
        IHttpContextAccessor httpContextAccessor,
        ILogger<DataClientMock> logger
    )
    {
        _httpContextAccessor = httpContextAccessor;
        _logger = logger;
        _appMetadata = appMetadata;
    }

    public async Task<bool> DeleteBinaryData(
        string org,
        string app,
        int instanceOwnerPartyId,
        Guid instanceGuid,
        Guid dataGuid
    )
    {
        return await DeleteData(org, app, instanceOwnerPartyId, instanceGuid, dataGuid, false);
    }

    public async Task<bool> DeleteData(
        string org,
        string app,
        int instanceOwnerPartyId,
        Guid instanceGuid,
        Guid dataGuid,
        bool delay
    )
    {
        await Task.CompletedTask;
        string dataElementPath = TestData.GetDataElementPath(org, app, instanceOwnerPartyId, instanceGuid, dataGuid);

        if (delay)
        {
            string fileContent = await File.ReadAllTextAsync(dataElementPath);

            if (fileContent == null)
            {
                return false;
            }

            if (
                JsonSerializer.Deserialize<DataElement>(fileContent, _jsonSerializerOptions)
                is not DataElement dataElement
            )
            {
                throw new Exception(
                    $"Unable to deserialize data element for org: {org}/{app} party: {instanceOwnerPartyId} instance: {instanceGuid} data: {dataGuid}. Tried path: {dataElementPath}"
                );
            }

            dataElement.DeleteStatus = new() { IsHardDeleted = true, HardDeleted = DateTime.UtcNow };

            WriteDataElementToFile(dataElement, org, app, instanceOwnerPartyId);

            return true;
        }
        else
        {
            string dataBlobPath = TestData.GetDataBlobPath(org, app, instanceOwnerPartyId, instanceGuid, dataGuid);

            if (File.Exists(dataElementPath))
            {
                File.Delete(dataElementPath);
            }

            if (File.Exists(dataBlobPath))
            {
                File.Delete(dataBlobPath);
            }

            return true;
        }
    }

    public Task<Stream> GetBinaryData(string org, string app, int instanceOwnerPartyId, Guid instanceGuid, Guid dataId)
    {
        string dataPath = TestData.GetDataBlobPath(org, app, instanceOwnerPartyId, instanceGuid, dataId);

        Stream ms = new MemoryStream();
        using (FileStream file = new(dataPath, FileMode.Open, FileAccess.Read))
        {
            file.CopyTo(ms);
        }

        return Task.FromResult(ms);
    }

#pragma warning disable CS1998 // Async method lacks 'await' operators and will run synchronously
    public async Task<List<AttachmentList>> GetBinaryDataList(
        string org,
        string app,
        int instanceOwnerPartyId,
        Guid instanceGuid
    )
#pragma warning restore CS1998 // Async method lacks 'await' operators and will run synchronously
    {
        var dataElements = GetDataElements(org, app, instanceOwnerPartyId, instanceGuid);
        List<AttachmentList> list = new();
        foreach (DataElement dataElement in dataElements)
        {
            AttachmentList al =
                new()
                {
                    Type = dataElement.DataType,
                    Attachments = new List<Attachment>()
                    {
                        new Attachment()
                        {
                            Id = dataElement.Id,
                            Name = dataElement.Filename,
                            Size = dataElement.Size
                        }
                    }
                };
            list.Add(al);
        }

        return list;
    }

    public async Task<object> GetFormData(
        Guid instanceGuid,
        Type type,
        string org,
        string app,
        int instanceOwnerPartyId,
        Guid dataId
    )
    {
        string dataPath = TestData.GetDataBlobPath(org, app, instanceOwnerPartyId, instanceGuid, dataId);
        using var sourceStream = File.Open(dataPath, FileMode.OpenOrCreate);

        ModelDeserializer deserializer = new(_logger, type);
        var formData = await deserializer.DeserializeAsync(sourceStream, "application/xml");

        // var formData = serializer.Deserialize(sourceStream);
        return formData ?? throw new Exception("Unable to deserialize form data");
    }

    public async Task<DataElement> InsertFormData<T>(Instance instance, string dataType, T dataToSerialize, Type type)
    {
        Guid instanceGuid = Guid.Parse(instance.Id.Split("/")[1]);
        string app = instance.AppId.Split("/")[1];
        string org = instance.Org;
        int instanceOwnerId = int.Parse(instance.InstanceOwner.PartyId);

        return await InsertFormData(dataToSerialize, instanceGuid, type, org, app, instanceOwnerId, dataType);
    }

    public Task<DataElement> InsertFormData<T>(
        T dataToSerialize,
        Guid instanceGuid,
        Type type,
        string org,
        string app,
        int instanceOwnerPartyId,
        string dataType
    )
    {
        Guid dataGuid = Guid.NewGuid();
        string dataPath = TestData.GetDataDirectory(org, app, instanceOwnerPartyId, instanceGuid);

        DataElement dataElement =
            new()
            {
                Id = dataGuid.ToString(),
                InstanceGuid = instanceGuid.ToString(),
                DataType = dataType,
                ContentType = "application/xml",
            };

        try
        {
            Directory.CreateDirectory(dataPath + @"blob");

            using (Stream stream = File.Open(dataPath + @"blob/" + dataGuid, FileMode.Create, FileAccess.ReadWrite))
            {
                DataClient.Serialize(dataToSerialize, type, stream);
            }

            WriteDataElementToFile(dataElement, org, app, instanceOwnerPartyId);
        }
        catch
#pragma warning disable S108 // Nested blocks of code should not be left empty
        { }
#pragma warning restore S108 // Nested blocks of code should not be left empty

        return Task.FromResult(dataElement);
    }

    public Task<DataElement> UpdateData<T>(
        T dataToSerialize,
        Guid instanceGuid,
        Type type,
        string org,
        string app,
        int instanceOwnerPartyId,
        Guid dataGuid
    )
    {
        ArgumentNullException.ThrowIfNull(dataToSerialize);
        string dataPath = TestData.GetDataDirectory(org, app, instanceOwnerPartyId, instanceGuid);

        DataElement? dataElement = GetDataElements(org, app, instanceOwnerPartyId, instanceGuid)
            .FirstOrDefault(de => de.Id == dataGuid.ToString());

        if (dataElement == null)
        {
            throw new Exception(
                $"Unable to find data element for org: {org}/{app} party: {instanceOwnerPartyId} instance: {instanceGuid} data: {dataGuid}"
            );
        }

        Directory.CreateDirectory(dataPath + @"blob");

        using (
            Stream stream = File.Open(
                dataPath + $@"blob{Path.DirectorySeparatorChar}" + dataGuid,
                FileMode.Create,
                FileAccess.ReadWrite
            )
        )
        {
            DataClient.Serialize(dataToSerialize, type, stream);
        }

        dataElement.LastChanged = DateTime.UtcNow;
        WriteDataElementToFile(dataElement, org, app, instanceOwnerPartyId);

        return Task.FromResult(dataElement);
    }

    public async Task<DataElement> InsertBinaryData(
        string org,
        string app,
        int instanceOwnerPartyId,
        Guid instanceGuid,
        string dataType,
        HttpRequest request
    )
    {
        Guid dataGuid = Guid.NewGuid();
        string dataPath = TestData.GetDataDirectory(org, app, instanceOwnerPartyId, instanceGuid);
        DataElement dataElement =
            new()
            {
                Id = dataGuid.ToString(),
                InstanceGuid = instanceGuid.ToString(),
                DataType = dataType,
                ContentType = request.ContentType
            };

        if (!Directory.Exists(Path.GetDirectoryName(dataPath)))
        {
            var directory =
                Path.GetDirectoryName(dataPath)
                ?? throw new Exception($"Unable to get directory name from path {dataPath}");

            Directory.CreateDirectory(directory);
        }

        Directory.CreateDirectory(dataPath + @"blob");

        long filesize;

        using (
            Stream streamToWriteTo = File.Open(
                dataPath + @"blob/" + dataGuid,
                FileMode.OpenOrCreate,
                FileAccess.ReadWrite,
                FileShare.ReadWrite
            )
        )
        {
            await request.Body.CopyToAsync(streamToWriteTo);
            streamToWriteTo.Flush();
            filesize = streamToWriteTo.Length;
            streamToWriteTo.Close();
        }

        dataElement.Size = filesize;

        WriteDataElementToFile(dataElement, org, app, instanceOwnerPartyId);

        return dataElement;
    }

    public async Task<DataElement> InsertBinaryData(
        string instanceId,
        string dataType,
        string contentType,
        string filename,
        Stream stream
    )
    {
        Application application = await _appMetadata.GetApplicationMetadata();
        var instanceIdParts = instanceId.Split("/");

        Guid dataGuid = Guid.NewGuid();

        string org = application.Org;
        string app = application.Id.Split("/")[1];
        int instanceOwnerId = int.Parse(instanceIdParts[0]);
        Guid instanceGuid = Guid.Parse(instanceIdParts[1]);

        string dataPath = TestData.GetDataDirectory(org, app, instanceOwnerId, instanceGuid);

        DataElement dataElement =
            new()
            {
                Id = dataGuid.ToString(),
                InstanceGuid = instanceGuid.ToString(),
                DataType = dataType,
                ContentType = contentType,
            };

        if (!Directory.Exists(Path.GetDirectoryName(dataPath)))
        {
            var directory = Path.GetDirectoryName(dataPath);
            if (directory != null)
                Directory.CreateDirectory(directory);
        }

        Directory.CreateDirectory(dataPath + @"blob");

        long filesize;

        using (
            Stream streamToWriteTo = File.Open(
                dataPath + @"blob/" + dataGuid,
                FileMode.OpenOrCreate,
                FileAccess.ReadWrite,
                FileShare.ReadWrite
            )
        )
        {
            stream.Seek(0, SeekOrigin.Begin);
            await stream.CopyToAsync(streamToWriteTo);
            streamToWriteTo.Flush();
            filesize = streamToWriteTo.Length;
        }

        dataElement.Size = filesize;
        WriteDataElementToFile(dataElement, org, app, instanceOwnerId);

        return dataElement;
    }

    public Task<DataElement> UpdateBinaryData(
        InstanceIdentifier instanceIdentifier,
        string? contentType,
        string filename,
        Guid dataGuid,
        Stream stream
    )
    {
        throw new NotImplementedException();
    }

    public async Task<DataElement> InsertBinaryData(
        string instanceId,
        string dataType,
        string contentType,
        string? filename,
        Stream stream,
        string? generatedFromTask = null
    )
    {
        Application application = await _appMetadata.GetApplicationMetadata();
        var instanceIdParts = instanceId.Split("/");

        Guid dataGuid = Guid.NewGuid();

        string org = application.Org;
        string app = application.Id.Split("/")[1];
        int instanceOwnerId = int.Parse(instanceIdParts[0]);
        Guid instanceGuid = Guid.Parse(instanceIdParts[1]);

        string dataPath = TestData.GetDataDirectory(org, app, instanceOwnerId, instanceGuid);

        DataElement dataElement =
            new()
            {
                Id = dataGuid.ToString(),
                InstanceGuid = instanceGuid.ToString(),
                DataType = dataType,
                ContentType = contentType,
            };

        if (!Directory.Exists(Path.GetDirectoryName(dataPath)))
        {
            var directory = Path.GetDirectoryName(dataPath);
            if (directory != null)
                Directory.CreateDirectory(directory);
        }

        Directory.CreateDirectory(dataPath + @"blob");

        long filesize;

        using (
            Stream streamToWriteTo = File.Open(
                dataPath + @"blob/" + dataGuid,
                FileMode.OpenOrCreate,
                FileAccess.ReadWrite,
                FileShare.ReadWrite
            )
        )
        {
            stream.Seek(0, SeekOrigin.Begin);
            await stream.CopyToAsync(streamToWriteTo);
            streamToWriteTo.Flush();
            filesize = streamToWriteTo.Length;
        }

        dataElement.Size = filesize;
        WriteDataElementToFile(dataElement, org, app, instanceOwnerId);

        return dataElement;
    }

    public Task<DataElement> UpdateBinaryData(
        string org,
        string app,
        int instanceOwnerPartyId,
        Guid instanceGuid,
        Guid dataGuid,
        HttpRequest request
    )
    {
        throw new NotImplementedException();
    }

    public Task<DataElement> Update(Instance instance, DataElement dataElement)
    {
        string org = instance.Org;
        string app = instance.AppId.Split("/")[1];
        int instanceOwnerId = int.Parse(instance.InstanceOwner.PartyId);

        WriteDataElementToFile(dataElement, org, app, instanceOwnerId);

        return Task.FromResult(dataElement);
    }

    public Task<DataElement> LockDataElement(InstanceIdentifier instanceIdentifier, Guid dataGuid)
    {
        // 🤬The signature does not take org/app,
        // but our test data is organized by org/app.
        (string org, string app) = TestData.GetInstanceOrgApp(instanceIdentifier);
        List<DataElement> dataElement = GetDataElements(
            org,
            app,
            instanceIdentifier.InstanceOwnerPartyId,
            instanceIdentifier.InstanceGuid
        );
        DataElement? element = dataElement.FirstOrDefault(d => d.Id == dataGuid.ToString());
        if (element is null)
        {
            throw new Exception("Data element not found.");
        }
        element.Locked = true;
        WriteDataElementToFile(element, org, app, instanceIdentifier.InstanceOwnerPartyId);
        return Task.FromResult(element);
    }

    public Task<DataElement> UnlockDataElement(InstanceIdentifier instanceIdentifier, Guid dataGuid)
    {
        // 🤬The signature does not take org/app,
        // but our test data is organized by org/app.
        (string org, string app) = TestData.GetInstanceOrgApp(instanceIdentifier);
        List<DataElement> dataElement = GetDataElements(
            org,
            app,
            instanceIdentifier.InstanceOwnerPartyId,
            instanceIdentifier.InstanceGuid
        );
        DataElement? element = dataElement.FirstOrDefault(d => d.Id == dataGuid.ToString());
        if (element is null)
        {
            throw new Exception("Data element not found.");
        }
        element.Locked = false;
        WriteDataElementToFile(element, org, app, instanceIdentifier.InstanceOwnerPartyId);
        return Task.FromResult(element);
    }

    private static void WriteDataElementToFile(
        DataElement dataElement,
        string org,
        string app,
        int instanceOwnerPartyId
    )
    {
        string dataElementPath = TestData.GetDataElementPath(
            org,
            app,
            instanceOwnerPartyId,
            Guid.Parse(dataElement.InstanceGuid),
            Guid.Parse(dataElement.Id)
        );

        string jsonData = JsonSerializer.Serialize(dataElement, _jsonSerializerOptions);

        using StreamWriter sw = new(dataElementPath);

        sw.Write(jsonData.ToString());
        sw.Close();
    }

    private List<DataElement> GetDataElements(string org, string app, int instanceOwnerId, Guid instanceId)
    {
        string path = TestData.GetDataDirectory(org, app, instanceOwnerId, instanceId);
        List<DataElement> dataElements = new();

        if (!Directory.Exists(path))
        {
            return new List<DataElement>();
        }

        string[] files = Directory.GetFiles(path);

        foreach (string file in files)
        {
            string content = File.ReadAllText(Path.Combine(path, file));
            DataElement? dataElement = JsonSerializer.Deserialize<DataElement>(content, _jsonSerializerOptions);

            if (dataElement != null)
            {
                if (
                    dataElement.DeleteStatus?.IsHardDeleted == true
                    && string.IsNullOrEmpty(_httpContextAccessor.HttpContext?.User.GetOrg())
                )
                {
                    continue;
                }

                dataElements.Add(dataElement);
            }
        }

        return dataElements;
    }
}
