using SchoolAdmission.Api.Domain;
using Xunit;

namespace SchoolAdmission.Tests;

public class HoldStateMachineTests
{
    [Fact]
    public void IsTerminal_ReturnsTrueForTerminalStates()
    {
        Assert.True(HoldStateMachine.IsTerminal(HoldStatus.Confirmed));
        Assert.True(HoldStateMachine.IsTerminal(HoldStatus.Cancelled));
        Assert.True(HoldStateMachine.IsTerminal(HoldStatus.Expired));
        Assert.False(HoldStateMachine.IsTerminal(HoldStatus.Held));
    }

    [Fact]
    public void DeriveStatus_DerivesExpired_WhenHeldAndPastExpiry()
    {
        var now = DateTime.UtcNow;
        var past = now.AddSeconds(-1);

        var derived = HoldStateMachine.DeriveStatus(HoldStatus.Held, past, now);
        Assert.Equal(HoldStatus.Expired, derived);

        var future = now.AddSeconds(10);
        var active = HoldStateMachine.DeriveStatus(HoldStatus.Held, future, now);
        Assert.Equal(HoldStatus.Held, active);
    }

    [Fact]
    public void ValidateConfirmation_AllowsIdempotentConfirm()
    {
        var now = DateTime.UtcNow;
        var valid = HoldStateMachine.ValidateConfirmation(HoldStatus.Confirmed, now.AddMinutes(5), now, out var error);
        Assert.True(valid);
        Assert.Null(error);
    }

    [Fact]
    public void ValidateConfirmation_RejectsCancelledHold()
    {
        var now = DateTime.UtcNow;
        var valid = HoldStateMachine.ValidateConfirmation(HoldStatus.Cancelled, now.AddMinutes(5), now, out var error);
        Assert.False(valid);
        Assert.NotNull(error);
        Assert.Equal("HOLD_CANCELLED", error.Code);
    }

    [Fact]
    public void ValidateConfirmation_RejectsExpiredHold()
    {
        var now = DateTime.UtcNow;
        var valid = HoldStateMachine.ValidateConfirmation(HoldStatus.Held, now.AddSeconds(-1), now, out var error);
        Assert.False(valid);
        Assert.NotNull(error);
        Assert.Equal("HOLD_EXPIRED", error.Code);
    }

    [Fact]
    public void ValidateCancellation_AllowsIdempotentCancel()
    {
        var valid = HoldStateMachine.ValidateCancellation(HoldStatus.Cancelled, out var isAlreadyDone, out var error);
        Assert.True(valid);
        Assert.True(isAlreadyDone);
        Assert.Null(error);
    }

    [Fact]
    public void ValidateCancellation_RejectsConfirmedHold()
    {
        var valid = HoldStateMachine.ValidateCancellation(HoldStatus.Confirmed, out _, out var error);
        Assert.False(valid);
        Assert.NotNull(error);
        Assert.Equal("ALREADY_CONFIRMED", error.Code);
    }
}
