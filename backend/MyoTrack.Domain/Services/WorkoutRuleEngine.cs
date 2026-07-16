using MyoTrack.Domain.Entities;

namespace MyoTrack.Domain.Services;

public record WorkoutGenerationInput(
    FitnessGoal Goal,
    ExperienceLevel Level,
    int DaysPerWeek,
    List<MuscleGroup> PriorityMuscleGroups,
    List<string> InjuryTags,
    List<Equipment> AvailableEquipment);

public record GeneratedExercise(int ExerciseId, string Name, int Sets, int RepsMin, int RepsMax, int RestSeconds, string? Notes);
public record GeneratedDay(int Order, string Label, List<GeneratedExercise> Exercises);
public record GeneratedWorkout(string Split, List<GeneratedDay> Days);

/// <summary>
/// Gera o esqueleto de treino por regras determinísticas: split conforme dias/semana,
/// volume conforme nível, filtro de contraindicações e equipamento.
/// O LLM apenas personaliza/anota dentro deste esqueleto — nunca cria exercícios.
/// </summary>
public static class WorkoutRuleEngine
{
    private sealed record DayTemplate(string Label, MuscleGroup[] Groups);

    private static (string Split, DayTemplate[] Days) SplitFor(int daysPerWeek) => daysPerWeek switch
    {
        <= 2 => ("FullBody", [
            new("A — Corpo inteiro", [MuscleGroup.Quadriceps, MuscleGroup.Chest, MuscleGroup.Back, MuscleGroup.Shoulders, MuscleGroup.Abs]),
            new("B — Corpo inteiro", [MuscleGroup.Hamstrings, MuscleGroup.Glutes, MuscleGroup.Back, MuscleGroup.Chest, MuscleGroup.Biceps, MuscleGroup.Triceps, MuscleGroup.Forearms]),
        ]),
        3 => ("ABC", [
            new("A — Peito/Ombros/Tríceps", [MuscleGroup.Chest, MuscleGroup.Shoulders, MuscleGroup.Triceps]),
            new("B — Costas/Bíceps/Antebraços", [MuscleGroup.Back, MuscleGroup.Biceps, MuscleGroup.Forearms, MuscleGroup.Abs]),
            new("C — Pernas", [MuscleGroup.Quadriceps, MuscleGroup.Hamstrings, MuscleGroup.Glutes, MuscleGroup.Calves]),
        ]),
        4 => ("ABCD", [
            new("A — Peito/Tríceps", [MuscleGroup.Chest, MuscleGroup.Triceps]),
            new("B — Costas/Bíceps/Antebraços", [MuscleGroup.Back, MuscleGroup.Biceps, MuscleGroup.Forearms]),
            new("C — Pernas", [MuscleGroup.Quadriceps, MuscleGroup.Hamstrings, MuscleGroup.Glutes, MuscleGroup.Calves]),
            new("D — Ombros/Abdômen", [MuscleGroup.Shoulders, MuscleGroup.Abs]),
        ]),
        _ => ("PPL", [
            new("A — Push (Peito/Ombros/Tríceps)", [MuscleGroup.Chest, MuscleGroup.Shoulders, MuscleGroup.Triceps]),
            new("B — Pull (Costas/Bíceps)", [MuscleGroup.Back, MuscleGroup.Biceps]),
            new("C — Legs (Pernas)", [MuscleGroup.Quadriceps, MuscleGroup.Hamstrings, MuscleGroup.Glutes, MuscleGroup.Calves]),
            new("D — Push (variação)", [MuscleGroup.Chest, MuscleGroup.Shoulders, MuscleGroup.Triceps]),
            new("E — Pull + Abdômen", [MuscleGroup.Back, MuscleGroup.Biceps, MuscleGroup.Forearms, MuscleGroup.Abs]),
        ]),
    };

    private static (int RepsMin, int RepsMax, int RestSeconds) PrescriptionFor(FitnessGoal goal) => goal switch
    {
        FitnessGoal.Hypertrophy => (8, 12, 90),
        FitnessGoal.WeightLoss => (12, 15, 60),
        FitnessGoal.Conditioning => (15, 20, 45),
        _ => (10, 15, 75), // Aesthetics
    };

    private static int SetsFor(ExperienceLevel level) => level switch
    {
        ExperienceLevel.Beginner => 3,
        ExperienceLevel.Intermediate => 3,
        _ => 4,
    };

    private static int ExercisesPerGroup(ExperienceLevel level, bool isPriority) =>
        (level == ExperienceLevel.Beginner ? 1 : 2) + (isPriority ? 1 : 0);

    public static GeneratedWorkout Generate(WorkoutGenerationInput input, IReadOnlyList<Exercise> catalog)
    {
        var eligible = catalog
            .Where(e => !e.ContraindicationTags.Intersect(input.InjuryTags, StringComparer.OrdinalIgnoreCase).Any())
            .Where(e => input.AvailableEquipment.Count == 0
                || e.Equipment == Equipment.Bodyweight
                || e.Equipment == Equipment.None
                || input.AvailableEquipment.Contains(e.Equipment))
            .ToList();

        var (split, dayTemplates) = SplitFor(input.DaysPerWeek);
        var (repsMin, repsMax, rest) = PrescriptionFor(input.Goal);
        var sets = SetsFor(input.Level);

        var days = new List<GeneratedDay>();
        foreach (var (template, dayIndex) in dayTemplates.Select((t, i) => (t, i)))
        {
            var used = new HashSet<int>();
            var exercises = new List<GeneratedExercise>();

            foreach (var group in template.Groups)
            {
                var isPriority = input.PriorityMuscleGroups.Contains(group);
                var candidates = eligible
                    .Where(e => e.PrimaryMuscleGroup == group && !used.Contains(e.Id))
                    .OrderByDescending(e => e.IsCompound)
                    .ThenBy(e => e.Id)
                    .Take(ExercisesPerGroup(input.Level, isPriority))
                    .ToList();

                foreach (var exercise in candidates)
                {
                    used.Add(exercise.Id);
                    exercises.Add(new GeneratedExercise(
                        exercise.Id, exercise.Name, sets, repsMin, repsMax, rest,
                        isPriority ? "Grupo priorizado" : null));
                }
            }

            days.Add(new GeneratedDay(dayIndex + 1, template.Label, exercises));
        }

        return new GeneratedWorkout(split, days);
    }
}
