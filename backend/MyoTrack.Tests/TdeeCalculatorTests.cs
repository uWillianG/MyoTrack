using MyoTrack.Domain;
using MyoTrack.Domain.Services;

namespace MyoTrack.Tests;

public class TdeeCalculatorTests
{
    [Fact]
    public void Bmr_MifflinStJeor_Male()
    {
        // 10*80 + 6.25*180 - 5*30 + 5 = 800 + 1125 - 150 + 5
        var bmr = TdeeCalculator.CalculateBmr("M", 80m, 180m, 30);
        Assert.Equal(1780m, bmr);
    }

    [Fact]
    public void Bmr_MifflinStJeor_Female()
    {
        // 10*60 + 6.25*165 - 5*25 - 161
        var bmr = TdeeCalculator.CalculateBmr("F", 60m, 165m, 25);
        Assert.Equal(1345.25m, bmr);
    }

    [Theory]
    [InlineData(0, 1.2)]
    [InlineData(3, 1.375)]
    [InlineData(5, 1.55)]
    [InlineData(6, 1.725)]
    public void ActivityFactor_ByTrainingDays(int days, decimal expected)
    {
        Assert.Equal(expected, TdeeCalculator.ActivityFactor(days));
    }

    [Fact]
    public void Deficit_Reduces20Percent()
    {
        var targets = TdeeCalculator.CalculateTargets("M", 80m, 180m, 30, 4, CalorieGoal.Deficit);
        var tdee = TdeeCalculator.CalculateTdee("M", 80m, 180m, 30, 4);
        Assert.Equal(Math.Round(tdee * 0.80m), targets.Kcal);
    }

    [Fact]
    public void Deficit_NeverGoesBelowBmr()
    {
        // Pessoa leve e sedentária: 80% do TDEE ficaria abaixo da TMB sem o guard-rail.
        var targets = TdeeCalculator.CalculateTargets("F", 50m, 160m, 40, 0, CalorieGoal.Deficit);
        var bmr = TdeeCalculator.CalculateBmr("F", 50m, 160m, 40);
        Assert.True(targets.Kcal >= Math.Round(bmr) - 1);
    }

    [Fact]
    public void Deficit_ProteinIs2gPerKg()
    {
        var targets = TdeeCalculator.CalculateTargets("M", 80m, 180m, 30, 4, CalorieGoal.Deficit);
        Assert.Equal(160m, targets.ProteinG);
    }

    [Fact]
    public void Macros_SumApproximatesKcal()
    {
        var t = TdeeCalculator.CalculateTargets("M", 80m, 180m, 30, 4, CalorieGoal.Maintenance);
        var kcalFromMacros = t.ProteinG * 4m + t.CarbsG * 4m + t.FatG * 9m;
        Assert.InRange(kcalFromMacros, t.Kcal * 0.97m, t.Kcal * 1.03m);
    }

    [Fact]
    public void Age_RespectsBirthdayNotYetReached()
    {
        Assert.Equal(29, TdeeCalculator.CalculateAge(new DateOnly(1996, 12, 1), new DateOnly(2026, 7, 12)));
        Assert.Equal(30, TdeeCalculator.CalculateAge(new DateOnly(1996, 7, 12), new DateOnly(2026, 7, 12)));
    }
}
