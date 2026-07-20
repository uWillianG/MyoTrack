using MyoTrack.Domain;
using MyoTrack.Domain.Services;

namespace MyoTrack.Tests;

public class ProgressionCalculatorTests
{
    [Fact]
    public void E1Rm_Epley()
    {
        // 100 * (1 + 8/30) = 126.666… → 126.7
        Assert.Equal(126.7m, ProgressionCalculator.EstimateOneRepMax(8, 100m));
    }

    [Fact]
    public void E1Rm_SingleRep_IsTheLoadItself()
    {
        Assert.Equal(100m, ProgressionCalculator.EstimateOneRepMax(1, 100m));
    }

    [Theory]
    [InlineData(13, 100)]
    [InlineData(0, 100)]
    [InlineData(8, 0)]
    public void E1Rm_OutOfRange_ReturnsNull(int reps, decimal load)
    {
        Assert.Null(ProgressionCalculator.EstimateOneRepMax(reps, load));
    }

    [Theory]
    [InlineData(MuscleGroup.Quadriceps, 5)]
    [InlineData(MuscleGroup.Glutes, 5)]
    [InlineData(MuscleGroup.LowerBack, 5)]
    [InlineData(MuscleGroup.Chest, 2.5)]
    [InlineData(MuscleGroup.Biceps, 2.5)]
    public void Increment_ByMuscleGroup(MuscleGroup group, decimal expected)
    {
        Assert.Equal(expected, ProgressionCalculator.IncrementFor(group));
    }

    [Fact]
    public void NoHistory_SuggestsStart()
    {
        var s = ProgressionCalculator.Suggest([], 8, 12, 2.5m);
        Assert.Equal(ProgressionAction.Start, s.Action);
        Assert.Null(s.NextLoadKg);
        Assert.Equal(8, s.TargetReps);
    }

    [Fact]
    public void AllSetsAtTopOfRange_SuggestsIncrease()
    {
        var sets = new[] { new SetPerformance(12, 40m), new(12, 40m), new(13, 40m) };
        var s = ProgressionCalculator.Suggest(sets, 8, 12, 2.5m);
        Assert.Equal(ProgressionAction.Increase, s.Action);
        Assert.Equal(42.5m, s.NextLoadKg);
        Assert.Equal(8, s.TargetReps); // volta ao piso da faixa com a carga nova
    }

    [Fact]
    public void WithinRange_SuggestsProgressReps()
    {
        var sets = new[] { new SetPerformance(12, 40m), new(10, 40m), new(9, 40m) };
        var s = ProgressionCalculator.Suggest(sets, 8, 12, 2.5m);
        Assert.Equal(ProgressionAction.ProgressReps, s.Action);
        Assert.Equal(40m, s.NextLoadKg);
        Assert.Equal(12, s.TargetReps);
    }

    [Fact]
    public void BelowMinimum_SuggestsConsolidate()
    {
        var sets = new[] { new SetPerformance(8, 40m), new(7, 40m), new(6, 40m) };
        var s = ProgressionCalculator.Suggest(sets, 8, 12, 2.5m);
        Assert.Equal(ProgressionAction.Consolidate, s.Action);
        Assert.Equal(40m, s.NextLoadKg);
        Assert.Equal(8, s.TargetReps);
    }

    [Fact]
    public void WarmupSets_DoNotBlockIncrease()
    {
        // Aquecimento leve com poucas reps não deve impedir a progressão:
        // só as séries na carga de trabalho (a maior) contam.
        var sets = new[] { new SetPerformance(15, 20m), new(12, 40m), new(12, 40m) };
        var s = ProgressionCalculator.Suggest(sets, 8, 12, 2.5m);
        Assert.Equal(ProgressionAction.Increase, s.Action);
        Assert.Equal(42.5m, s.NextLoadKg);
    }
}
