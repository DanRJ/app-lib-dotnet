using Altinn.App.Core.Helpers;
using Altinn.App.Core.Internal.App;
using Altinn.App.Core.Internal.Instances;
using Altinn.App.Core.Internal.Validation;
using Altinn.App.Core.Models.Validation;
using Altinn.Platform.Storage.Interface.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Altinn.App.Api.Controllers;

/// <summary>
/// Represents all actions related to validation of data and instances
/// </summary>
[Authorize]
[ApiController]
public class ValidateController : ControllerBase
{
    private readonly IInstanceClient _instanceClient;
    private readonly IAppMetadata _appMetadata;
    private readonly IValidationService _validationService;

    /// <summary>
    /// Initialises a new instance of the <see cref="ValidateController"/> class
    /// </summary>
    public ValidateController(
        IInstanceClient instanceClient,
        IValidationService validationService,
        IAppMetadata appMetadata
    )
    {
        _instanceClient = instanceClient;
        _validationService = validationService;
        _appMetadata = appMetadata;
    }

    /// <summary>
    /// Validate an app instance. This will validate all individual data elements, both the binary elements and the elements bound
    /// to a model, and then finally the state of the instance.
    /// </summary>
    /// <param name="org">Unique identifier of the organisation responsible for the app.</param>
    /// <param name="app">Application identifier which is unique within an organisation</param>
    /// <param name="instanceOwnerPartyId">Unique id of the party that is the owner of the instance.</param>
    /// <param name="instanceGuid">Unique id to identify the instance</param>
    /// <param name="language">The currently used language by the user (or null if not available)</param>
    [HttpGet]
    [Route("{org}/{app}/instances/{instanceOwnerPartyId:int}/{instanceGuid:guid}/validate")]
    public async Task<IActionResult> ValidateInstance(
        [FromRoute] string org,
        [FromRoute] string app,
        [FromRoute] int instanceOwnerPartyId,
        [FromRoute] Guid instanceGuid,
        [FromQuery] string? language = null
    )
    {
        Instance? instance = await _instanceClient.GetInstance(app, org, instanceOwnerPartyId, instanceGuid);
        if (instance == null)
        {
            return NotFound();
        }

        string? taskId = instance.Process?.CurrentTask?.ElementId;
        if (taskId == null)
        {
            throw new ValidationException("Unable to validate instance without a started process.");
        }

        try
        {
            List<ValidationIssue> messages = await _validationService.ValidateInstanceAtTask(
                instance,
                taskId,
                language
            );
            return Ok(messages);
        }
        catch (PlatformHttpException exception)
        {
            if (exception.Response.StatusCode == System.Net.HttpStatusCode.Forbidden)
            {
                return StatusCode(403);
            }

            throw;
        }
    }

    /// <summary>
    /// Validate an app instance. This will validate a single data element
    /// </summary>
    /// <param name="org">Unique identifier of the organisation responsible for the app.</param>
    /// <param name="app">Application identifier which is unique within an organisation</param>
    /// <param name="instanceOwnerId">Unique id of the party that is the owner of the instance.</param>
    /// <param name="instanceId">Unique id to identify the instance</param>
    /// <param name="dataGuid">Unique id identifying specific data element</param>
    /// <param name="language">The currently used language by the user (or null if not available)</param>
    [HttpGet]
    [Route("{org}/{app}/instances/{instanceOwnerId:int}/{instanceId:guid}/data/{dataGuid:guid}/validate")]
    public async Task<IActionResult> ValidateData(
        [FromRoute] string org,
        [FromRoute] string app,
        [FromRoute] int instanceOwnerId,
        [FromRoute] Guid instanceId,
        [FromRoute] Guid dataGuid,
        [FromQuery] string? language = null
    )
    {
        Instance? instance = await _instanceClient.GetInstance(app, org, instanceOwnerId, instanceId);
        if (instance == null)
        {
            return NotFound();
        }

        if (instance.Process?.CurrentTask?.ElementId == null)
        {
            throw new ValidationException("Unable to validate instance without a started process.");
        }

        List<ValidationIssue> messages = new List<ValidationIssue>();

        DataElement? element = instance.Data.FirstOrDefault(d => d.Id == dataGuid.ToString());

        if (element == null)
        {
            throw new ValidationException("Unable to validate data element.");
        }

        Application application = await _appMetadata.GetApplicationMetadata();

        DataType? dataType = application.DataTypes.FirstOrDefault(et => et.Id == element.DataType);

        if (dataType == null)
        {
            throw new ValidationException("Unknown element type.");
        }

        messages.AddRange(await _validationService.ValidateDataElement(instance, element, dataType, language));

        string taskId = instance.Process.CurrentTask.ElementId;

        // Should this be a BadRequest instead?
        if (!dataType.TaskId.Equals(taskId, StringComparison.OrdinalIgnoreCase))
        {
            ValidationIssue message = new ValidationIssue
            {
                Code = ValidationIssueCodes.DataElementCodes.DataElementValidatedAtWrongTask,
                Severity = ValidationIssueSeverity.Warning,
                DataElementId = element.Id,
                Description = $"Data element for task {dataType.TaskId} validated while currentTask is {taskId}",
                CustomTextKey = ValidationIssueCodes.DataElementCodes.DataElementValidatedAtWrongTask,
                CustomTextParams = new List<string>() { dataType.TaskId, taskId },
            };
            messages.Add(message);
        }

        return Ok(messages);
    }
}
