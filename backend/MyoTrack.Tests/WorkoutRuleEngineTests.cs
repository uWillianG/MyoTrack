using MyoTrack.Domain;
using MyoTrack.Domain.Entities;
using MyoTrack.Domain.Services;
using MyoTrack.Infrastructure.Seed;

namespace MyoTrack.Tests;

public class WorkoutRuleEngineTests
{
    private static IReadOnlyList<Exercise> Catalog()
    {
        // O seed não define Ids (banco gera); atribui sequencial para os testes.
        var items = ExerciseSeed.Items.Select((e, i) => new Exercise
        {
            Id = i + 1,
            Name = e.Name,
            PrimaryMuscleGroup = e.PrimaryMuscleGroup,
            SecondaryMuscleGroups = e.SecondaryMuscleGroups,
            Equipment = e.Equipment,
            IsCompound = e.IsCompound,
            ContraindicationTags = e.ContraindicationTags,
        }).ToList();
        return items;
    }

    private static WorkoutGenerationInput Input(
        int days = 3,
        ExperienceLevel level = ExperienceLevel.Intermediate,
        FitnessGoal goal = FitnessGoal.Hypertrophy,
        List<string>? injuries = null,
        List<Equipment>? equipment = null,
        List<MuscleGroup>? priorities = null) =>
        new(goal, level, days, priorities ?? [], injuries ?? [], equipment ?? []);

    [Theory]
    [InlineData(2, "FullBody", 2)]
    [InlineData(3, "ABC", 3)]
    [InlineData(4, "ABCD", 4)]
    [InlineData(5, "PPL", 5)]
    [InlineData(6, "PPL", 5)]
    public void Split_MatchesDaysPerWeek(int days, string expectedSplit, int expectedDayCount)
    {
        var plan = WorkoutRuleEngine.Generate(Input(days: days), Catalog());
        Assert.Equal(expectedSplit, plan.Split);
        Assert.Equal(expectedDayCount, plan.Days.Count);
    }

    [Fact]
    public void InjuryTags_ExcludeContraindicatedExercises()
    {
        var plan = WorkoutRuleEngine.Generate(Input(days: 5, injuries: ["knee"]), Catalog());
        var catalog = Catalog().ToDictionary(e => e.Id);

        var exercisesWithKneeRisk = plan.Days
            .SelectMany(d => d.Exercises)
            .Where(e => catalog[e.ExerciseId].ContraindicationTags.Contains("knee"))
            .ToList();

        Assert.Empty(exercisesWithKneeRisk);
        // Ainda deve haver treino de pernas com opções seguras.
        Assert.Contains(plan.Days, d => d.Exercises.Count > 0 && d.Label.Contains("Legs"));
    }

    [Fact]
    public void EquipmentFilter_OnlyAllowedEquipmentOrBodyweight()
    {
        var plan = WorkoutRuleEngine.Generate(
            Input(equipment: [Equipment.Dumbbell]), Catalog());
        var catalog = Catalog().ToDictionary(e => e.Id);

        foreach (var e in plan.Days.SelectMany(d => d.Exercises))
        {
            var eq = catalog[e.ExerciseId].Equipment;
            Assert.True(eq is Equipment.Dumbbell or Equipment.Bodyweight or Equipment.None,
                $"{e.Name} usa {eq}, não permitido");
        }
    }

    [Fact]
    public void Beginner_HasFewerExercisesThanAdvanced()
    {
        var beginner = WorkoutRuleEngine.Generate(Input(level: ExperienceLevel.Beginner), Catalog());
        var advanced = WorkoutRuleEngine.Generate(Input(level: ExperienceLevel.Advanced), Catalog());

        Assert.True(
            beginner.Days.Sum(d => d.Exercises.Count) < advanced.Days.Sum(d => d.Exercises.Count));
        Assert.All(beginner.Days.SelectMany(d => d.Exercises), e => Assert.Equal(3, e.Sets));
        Assert.All(advanced.Days.SelectMany(d => d.Exercises), e => Assert.Equal(4, e.Sets));
    }

    [Fact]
    public void Goal_DefinesRepRangeAndRest()
    {
        var hypertrophy = WorkoutRuleEngine.Generate(Input(goal: FitnessGoal.Hypertrophy), Catalog());
        var first = hypertrophy.Days[0].Exercises[0];
        Assert.Equal((8, 12, 90), (first.RepsMin, first.RepsMax, first.RestSeconds));

        var conditioning = WorkoutRuleEngine.Generate(Input(goal: FitnessGoal.Conditioning), Catalog());
        var c = conditioning.Days[0].Exercises[0];
        Assert.Equal((15, 20, 45), (c.RepsMin, c.RepsMax, c.RestSeconds));
    }

    [Fact]
    public void PriorityGroup_GetsExtraExercise()
    {
        var without = WorkoutRuleEngine.Generate(Input(), Catalog());
        var with = WorkoutRuleEngine.Generate(Input(priorities: [MuscleGroup.Chest]), Catalog());

        int ChestCount(GeneratedWorkout w) => w.Days
            .SelectMany(d => d.Exercises)
            .Count(e => Catalog().First(c => c.Id == e.ExerciseId).PrimaryMuscleGroup == MuscleGroup.Chest);

        Assert.True(ChestCount(with) > ChestCount(without));
    }

    [Theory]
    [InlineData(MuscleGroup.Forearms)]
    [InlineData(MuscleGroup.Traps)]
    [InlineData(MuscleGroup.Calves)]
    public void SmallGroups_AppearInAllSplits(MuscleGroup group)
    {
        var catalog = Catalog().ToDictionary(e => e.Id);
        foreach (var days in new[] { 2, 3, 4, 5 })
        {
            var plan = WorkoutRuleEngine.Generate(Input(days: days), catalog.Values.ToList());
            Assert.Contains(plan.Days.SelectMany(d => d.Exercises),
                e => catalog[e.ExerciseId].PrimaryMuscleGroup == group);
        }
    }

    [Fact]
    public void NoDuplicateExerciseWithinSameDay()
    {
        var plan = WorkoutRuleEngine.Generate(Input(days: 5, level: ExperienceLevel.Advanced), Catalog());
        foreach (var day in plan.Days)
        {
            var ids = day.Exercises.Select(e => e.ExerciseId).ToList();
            Assert.Equal(ids.Count, ids.Distinct().Count());
        }
    }
}
