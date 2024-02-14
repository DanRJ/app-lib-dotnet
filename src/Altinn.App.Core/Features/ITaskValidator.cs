using Altinn.App.Core.Models.Validation;
using Altinn.Platform.Storage.Interface.Models;
using Microsoft.Extensions.DependencyInjection;

namespace Altinn.App.Core.Features;

/// <summary>
/// Interface for handling validation of tasks.
/// </summary>
public interface ITaskValidator
{
    /// <summary>
    /// The task id this validator is for. Typically either hard coded by implementation or
    /// or set by constructor using a <see cref="ServiceKeyAttribute" /> and a keyed service.
    /// </summary>
    /// <example>
    /// <code>
    /// string TaskId { get; init; }
    /// // constructor
    /// public MyTaskValidator([ServiceKey] string taskId)
    /// {
    ///     TaskId = taskId;
    /// }
    /// </code>
    /// </example>
    string TaskId { get; }

    /// <summary>
    /// Returns the group id of the validator.
    /// The default is based on the FullName and TaskId fields, and should not need customization
    /// </summary>
    string ValidationSource => $"{this.GetType().FullName}-{TaskId}";

    /// <summary>
    /// Actual validation logic for the task
    /// </summary>
    /// <param name="instance">The instance to validate</param>
    /// <param name="taskId">current task to run validations for</param>
    /// <param name="language">The currently used language by the user (or null if not available)</param>
    /// <returns>List of validation issues to add to this task validation</returns>
    Task<List<ValidationIssue>> ValidateTask(Instance instance, string taskId, string? language);
}