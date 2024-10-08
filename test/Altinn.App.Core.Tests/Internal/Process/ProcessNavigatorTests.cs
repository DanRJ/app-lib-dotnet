#nullable disable
using Altinn.App.Core.Features;
using Altinn.App.Core.Internal.Process;
using Altinn.App.Core.Internal.Process.Elements;
using Altinn.App.Core.Internal.Process.Elements.Base;
using Altinn.App.Core.Models.Process;
using Altinn.App.Core.Tests.Internal.Process.TestUtils;
using Altinn.App.PlatformServices.Tests.Internal.Process.StubGatewayFilters;
using Altinn.Platform.Storage.Interface.Models;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;

namespace Altinn.App.Core.Tests.Internal.Process;

public class ProcessNavigatorTests
{
    [Fact]
    public async Task GetNextTask_returns_next_element_if_no_gateway()
    {
        IProcessNavigator processNavigator = SetupProcessNavigator("simple-linear.bpmn", []);
        ProcessElement nextElements = await processNavigator.GetNextTask(new Instance(), "Task1", null);
        nextElements
            .Should()
            .BeEquivalentTo(
                new ProcessTask()
                {
                    Id = "Task2",
                    Name = "Bekreft skjemadata",
                    Incoming = new List<string> { "Flow2" },
                    Outgoing = new List<string> { "Flow3" },
                    ExtensionElements = new ExtensionElements()
                    {
                        TaskExtension = new() { TaskType = "confirmation", AltinnActions = new() },
                    }
                }
            );
    }

    [Fact]
    public async Task GetNextTask_single_sign_to_process_end()
    {
        var processFile = "with-double-sign.bpmn";
        IProcessNavigator processNavigator = SetupProcessNavigator(processFile, [new SingleSignGateway()]);
        ProcessElement nextElements = await processNavigator.GetNextTask(new Instance(), "Task_Sign1", "sign");
        nextElements.Id.Should().Be("EndEvent_1");
    }

    private sealed class SingleSignGateway : IProcessExclusiveGateway
    {
        public string GatewayId => "AltinnExpressionsExclusiveGateway";

        public Task<List<SequenceFlow>> FilterAsync(
            List<SequenceFlow> outgoingFlows,
            Instance instance,
            ProcessGatewayInformation processGatewayInformation
        )
        {
            return Task.FromResult(
                outgoingFlows.Where(f => f.Id == "Flow_sign1_sign" || f.Id == "Flow_SingleSign").ToList()
            );
        }
    }

    [Fact]
    public async Task GetNextTask_exclusive_gateway_zero_paths_should_fail()
    {
        var processFile = "with-double-sign.bpmn";
        IProcessNavigator processNavigator = SetupProcessNavigator(processFile, [new ZeroPathsGateway()]);
        await Assert.ThrowsAsync<ProcessException>(
            async () => await processNavigator.GetNextTask(new Instance(), "Task_Sign1", "sign")
        );
    }

    private sealed class ZeroPathsGateway : IProcessExclusiveGateway
    {
        public string GatewayId => "AltinnExpressionsExclusiveGateway";

        public Task<List<SequenceFlow>> FilterAsync(
            List<SequenceFlow> outgoingFlows,
            Instance instance,
            ProcessGatewayInformation processGatewayInformation
        )
        {
            return Task.FromResult(new List<SequenceFlow>());
        }
    }

    [Fact]
    public async Task NextFollowAndFilterGateways_returns_empty_list_if_no_outgoing_flows()
    {
        IProcessNavigator processNavigator = SetupProcessNavigator("simple-linear.bpmn", []);
        ProcessElement nextElements = await processNavigator.GetNextTask(new Instance(), "EndEvent", null);
        nextElements.Should().BeNull();
    }

    [Fact]
    public async Task GetNextTask_returns_default_if_no_filtering_is_implemented_and_default_set()
    {
        IProcessNavigator processNavigator = SetupProcessNavigator("simple-gateway-default.bpmn", []);
        ProcessElement nextElements = await processNavigator.GetNextTask(new Instance(), "Task1", null);
        nextElements
            .Should()
            .BeEquivalentTo(
                new ProcessTask()
                {
                    Id = "Task2",
                    Name = null!,
                    ExtensionElements = new()
                    {
                        TaskExtension = new()
                        {
                            TaskType = "confirm",
                            AltinnActions = new() { new("confirm"), new("reject") }
                        }
                    },
                    Incoming = new List<string> { "Flow3" },
                    Outgoing = new List<string> { "Flow5" }
                }
            );
    }

    [Fact]
    public async Task GetNextTask_runs_custom_filter_and_returns_result()
    {
        IProcessNavigator processNavigator = SetupProcessNavigator(
            "simple-gateway-with-join-gateway.bpmn",
            [new DataValuesFilter("Gateway1", "choose")]
        );
        Instance i = new Instance() { DataValues = new Dictionary<string, string>() { { "choose", "Flow3" } } };

        ProcessElement nextElements = await processNavigator.GetNextTask(i, "Task1", null);
        nextElements
            .Should()
            .BeEquivalentTo(
                new ProcessTask()
                {
                    Id = "Task2",
                    Name = null!,
                    ExtensionElements = new()
                    {
                        TaskExtension = new()
                        {
                            TaskType = "data",
                            AltinnActions = new() { new("submit") }
                        }
                    },
                    Incoming = new List<string> { "Flow3" },
                    Outgoing = new List<string> { "Flow5" }
                }
            );
    }

    [Fact]
    public async Task GetNextTask_throws_ProcessException_if_multiple_targets_found()
    {
        IProcessNavigator processNavigator = SetupProcessNavigator(
            "simple-gateway-with-join-gateway.bpmn",
            new List<IProcessExclusiveGateway>() { new DataValuesFilter("Foobar", "choose") }
        );
        Instance i = new Instance() { DataValues = new Dictionary<string, string>() { { "choose", "Flow3" } } };

        var result = await Assert.ThrowsAsync<ProcessException>(
            async () => await processNavigator.GetNextTask(i, "Task1", null)
        );
        result
            .Message.Should()
            .Be("Multiple next elements found from Task1. Please supply action and filters or define a default flow.");
    }

    [Fact]
    public async Task GetNextTask_follows_downstream_gateways()
    {
        IProcessNavigator processNavigator = SetupProcessNavigator(
            "simple-gateway-with-join-gateway.bpmn",
            new List<IProcessExclusiveGateway>() { new DataValuesFilter("Gateway1", "choose1") }
        );
        Instance i = new Instance() { DataValues = new Dictionary<string, string>() { { "choose1", "Flow4" } } };
        ProcessElement nextElements = await processNavigator.GetNextTask(i, "Task1", null);
        nextElements
            .Should()
            .BeEquivalentTo(
                new EndEvent()
                {
                    Id = "EndEvent",
                    Name = null!,
                    Incoming = new List<string> { "Flow6" },
                    Outgoing = new List<string>()
                }
            );
    }

    [Fact]
    public async Task GetNextTask_runs_custom_filter_and_throws_exception_when_no_paths_are_found()
    {
        IProcessNavigator processNavigator = SetupProcessNavigator(
            "simple-gateway-with-join-gateway.bpmn",
            new List<IProcessExclusiveGateway>()
            {
                new DataValuesFilter("Gateway1", "choose1"),
                new DataValuesFilter("Gateway2", "choose2")
            }
        );
        Instance i = new Instance()
        {
            DataValues = new Dictionary<string, string>() { { "choose1", "Flow4" }, { "choose2", "Bar" } }
        };

        await Assert.ThrowsAsync<ProcessException>(async () => await processNavigator.GetNextTask(i, "Task1", null));
    }

    [Fact]
    public async Task GetNextTask_returns_empty_list_if_element_has_no_next()
    {
        IProcessNavigator processNavigator = SetupProcessNavigator(
            "simple-gateway-with-join-gateway.bpmn",
            new List<IProcessExclusiveGateway>()
        );
        Instance i = new Instance();

        ProcessElement nextElements = await processNavigator.GetNextTask(i, "EndEvent", null);
        nextElements.Should().BeNull();
    }

    private static IProcessNavigator SetupProcessNavigator(
        string bpmnfile,
        IEnumerable<IProcessExclusiveGateway> gatewayFilters
    )
    {
        ProcessReader pr = ProcessTestUtils.SetupProcessReader(bpmnfile);
        return new ProcessNavigator(
            pr,
            new ExclusiveGatewayFactory(gatewayFilters),
            new NullLogger<ProcessNavigator>()
        );
    }
}
