using API.Controllers;
using API.Models.StatusEffects;
using Core.Common;
using Core.StatusEffects;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace API.Tests.Controllers;

public class StatusEffectControllerTests
{
    private readonly Mock<IStatusEffectManager> _statusEffectManager;
    private readonly StatusEffectController _controller;
    private readonly Guid _combatId;
    private readonly Guid _targetId;

    public StatusEffectControllerTests()
    {
        _statusEffectManager = new Mock<IStatusEffectManager>();
        _controller = new StatusEffectController(_statusEffectManager.Object, Mock.Of<ILogger<StatusEffectController>>());
        _combatId = Guid.NewGuid();
        _targetId = Guid.NewGuid();
    }

    [Fact]
    public void ApplyStatus_WithValidRequest_ReturnsSuccess()
    {
        var request = new ApplyStatusRequest
        {
            StatusId = "burn",
            Stacks = 2,
            Duration = 3
        };
        var instance = StatusInstance("burn", request.Stacks, request.Duration!.Value);
        _statusEffectManager.Setup(m => m.ApplyStatus(
                _targetId,
                request.StatusId,
                request.Stacks,
                request.Duration,
                request.SourceId))
            .Returns(Result<StatusEffectInstance>.Success(instance));

        var result = _controller.ApplyStatusForEntity(_combatId, _targetId, request);

        var ok = Assert.IsType<OkObjectResult>(result);
        var response = Assert.IsType<StatusEffectResponse>(ok.Value);
        Assert.Equal("burn", response.StatusId);
        Assert.Equal(2, response.Stacks);
        Assert.Equal(3, response.Duration);
    }

    [Fact]
    public void ApplyStatus_WithInvalidStatusId_ReturnsBadRequest()
    {
        var request = new ApplyStatusRequest
        {
            StatusId = "",
            Stacks = 1,
            Duration = 3
        };
        _statusEffectManager.Setup(m => m.ApplyStatus(_targetId, "", 1, 3, null))
            .Returns(Result<StatusEffectInstance>.Failure("Status ID cannot be empty"));

        var result = _controller.ApplyStatusForEntity(_combatId, _targetId, request);

        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public void GetActiveStatus_ReturnsAllActiveEffects()
    {
        _statusEffectManager.Setup(m => m.GetActiveStatus(_targetId))
            .Returns(Result<List<StatusEffectInstance>>.Success(new List<StatusEffectInstance>
            {
                StatusInstance("burn", 2, 3),
                StatusInstance("shield", 1, 2)
            }));

        var result = _controller.GetActiveStatusForEntity(_combatId, _targetId);

        var ok = Assert.IsType<OkObjectResult>(result);
        var response = Assert.IsAssignableFrom<List<StatusEffectResponse>>(ok.Value);
        Assert.Equal(2, response.Count);
    }

    [Fact]
    public void RemoveStatus_WithValidInstanceId_ReturnsSuccess()
    {
        var instanceId = Guid.NewGuid();
        _statusEffectManager.Setup(m => m.RemoveStatus(_targetId, instanceId))
            .Returns(Result.Success());

        var result = _controller.RemoveStatusForEntity(_combatId, _targetId, instanceId);

        Assert.IsType<OkObjectResult>(result);
    }

    [Fact]
    public void AddStacks_WithValidRequest_ReturnsUpdatedStatus()
    {
        var instanceId = Guid.NewGuid();
        _statusEffectManager.Setup(m => m.AddStacks(_targetId, instanceId, 3))
            .Returns(Result<StatusEffectInstance>.Success(StatusInstance("burn", 5, 3, instanceId)));

        var result = _controller.AddStacksForEntity(
            _combatId,
            _targetId,
            instanceId,
            new ModifyStacksRequest { Stacks = 3 });

        var ok = Assert.IsType<OkObjectResult>(result);
        var response = Assert.IsType<StatusEffectResponse>(ok.Value);
        Assert.Equal(5, response.Stacks);
    }

    [Fact]
    public void RemoveStacks_WithValidRequest_ReturnsUpdatedStatus()
    {
        var instanceId = Guid.NewGuid();
        _statusEffectManager.Setup(m => m.RemoveStacks(_targetId, instanceId, 2))
            .Returns(Result<StatusEffectInstance?>.Success(StatusInstance("burn", 3, 3, instanceId)));

        var result = _controller.RemoveStacksForEntity(
            _combatId,
            _targetId,
            instanceId,
            new ModifyStacksRequest { Stacks = 2 });

        var ok = Assert.IsType<OkObjectResult>(result);
        var response = Assert.IsType<StatusEffectResponse>(ok.Value);
        Assert.Equal(3, response.Stacks);
    }

    [Fact]
    public void RefreshDuration_WithValidRequest_ReturnsUpdatedStatus()
    {
        var instanceId = Guid.NewGuid();
        _statusEffectManager.Setup(m => m.RefreshDuration(_targetId, instanceId, 5))
            .Returns(Result<StatusEffectInstance>.Success(StatusInstance("burn", 2, 5, instanceId)));

        var result = _controller.RefreshDurationForEntity(
            _combatId,
            _targetId,
            instanceId,
            new RefreshDurationRequest { Duration = 5 });

        var ok = Assert.IsType<OkObjectResult>(result);
        var response = Assert.IsType<StatusEffectResponse>(ok.Value);
        Assert.Equal(5, response.Duration);
    }

    [Fact]
    public void TickDurations_DecrementsDurations()
    {
        _statusEffectManager.Setup(m => m.TickDurations(_targetId))
            .Returns(Result.Success());
        _statusEffectManager.Setup(m => m.GetActiveStatus(_targetId))
            .Returns(Result<List<StatusEffectInstance>>.Success(new List<StatusEffectInstance>
            {
                StatusInstance("burn", 2, 2)
            }));

        var tickResult = _controller.TickDurationsForEntity(_combatId, _targetId);
        Assert.IsType<OkObjectResult>(tickResult);

        var result = _controller.GetActiveStatusForEntity(_combatId, _targetId);
        var ok = Assert.IsType<OkObjectResult>(result);
        var response = Assert.IsAssignableFrom<List<StatusEffectResponse>>(ok.Value);
        var status = Assert.Single(response);
        Assert.Equal(2, status.Duration);
    }

    [Fact]
    public void RemoveAllStatus_RemovesAllEffects()
    {
        _statusEffectManager.Setup(m => m.RemoveAllStatus(_targetId, null))
            .Returns(Result.Success());

        var result = _controller.RemoveAllStatusForEntity(_combatId, _targetId);

        Assert.IsType<OkObjectResult>(result);
    }

    private static StatusEffectInstance StatusInstance(
        string statusId,
        int stacks,
        int duration,
        Guid? instanceId = null) => new()
        {
            InstanceId = instanceId ?? Guid.NewGuid(),
            StatusId = statusId,
            Definition = new StatusEffectDefinition
            {
                StatusId = statusId,
                DisplayName = statusId,
                Type = StatusEffectType.WEAKNESS
            },
            TargetId = Guid.NewGuid(),
            Stacks = stacks,
            Duration = duration
        };
}
