using System.Diagnostics;
using Altinn.Platform.Storage.Interface.Models;
using static Altinn.App.Core.Features.Telemetry.Validation;

namespace Altinn.App.Core.Features;

partial class Telemetry
{
    private void InitValidation(InitContext context) { }

    internal Activity? StartValidateInstanceAtTaskActivity(Instance instance, string taskId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(taskId);

        var activity = ActivitySource.StartActivity($"{Prefix}.ValidateInstanceAtTask");
        activity?.SetTaskId(taskId);
        activity?.SetInstanceId(instance);
        return activity;
    }

    internal Activity? StartRunTaskValidatorActivity(ITaskValidator validator)
    {
        var activity = ActivitySource.StartActivity($"{Prefix}.RunTaskValidator");

        activity?.SetTag(InternalLabels.ValidatorType, validator.GetType().Name);
        activity?.SetTag(InternalLabels.ValidatorSource, validator.ValidationSource);

        return activity;
    }

    internal Activity? StartValidateDataElementActivity(Instance instance, DataElement dataElement)
    {
        var activity = ActivitySource.StartActivity($"{Prefix}.ValidateDataElement");
        activity?.SetInstanceId(instance);
        activity?.SetDataElementId(dataElement);
        return activity;
    }

    internal Activity? StartRunDataElementValidatorActivity(IDataElementValidator validator)
    {
        var activity = ActivitySource.StartActivity($"{Prefix}.RunDataElementValidator");

        activity?.SetTag(InternalLabels.ValidatorType, validator.GetType().Name);
        activity?.SetTag(InternalLabels.ValidatorSource, validator.ValidationSource);

        return activity;
    }

    internal Activity? StartValidateFormDataActivity(Instance instance, DataElement dataElement)
    {
        var activity = ActivitySource.StartActivity($"{Prefix}.ValidateFormData");

        activity?.SetInstanceId(instance);
        activity?.SetDataElementId(dataElement);
        return activity;
    }

    internal Activity? StartRunFormDataValidatorActivity(IFormDataValidator validator)
    {
        var activity = ActivitySource.StartActivity($"{Prefix}.RunFormDataValidator");

        activity?.SetTag(InternalLabels.ValidatorType, validator.GetType().Name);
        activity?.SetTag(InternalLabels.ValidatorSource, validator.ValidationSource);

        return activity;
    }

    internal static class Validation
    {
        internal const string Prefix = "Validation";
    }
}
