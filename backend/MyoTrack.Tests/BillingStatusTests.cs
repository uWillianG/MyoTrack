using MyoTrack.Api.Controllers;

namespace MyoTrack.Tests;

public class BillingStatusTests
{
    [Theory]
    [InlineData("active", true)]
    [InlineData("trialing", true)]
    // past_due = período de graça: o Stripe ainda está tentando cobrar.
    [InlineData("past_due", true)]
    [InlineData("canceled", false)]
    [InlineData("unpaid", false)]
    [InlineData("incomplete", false)]
    [InlineData("incomplete_expired", false)]
    [InlineData("paused", false)]
    [InlineData(null, false)]
    [InlineData("", false)]
    public void IsEntitledStatus_MapsStripeStatusToAccess(string? status, bool expected)
    {
        Assert.Equal(expected, BillingController.IsEntitledStatus(status));
    }
}
