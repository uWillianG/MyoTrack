using MyoTrack.Domain;
using MyoTrack.Domain.Entities;

namespace MyoTrack.Infrastructure.Seed;

/// <summary>
/// Catálogo inicial de exercícios. Expandir para ~150 na Fase 1;
/// tags de contraindicação usam o vocabulário: knee, lower-back, shoulder, elbow, wrist, hip, neck.
/// </summary>
public static class ExerciseSeed
{
    private static Exercise Ex(string name, MuscleGroup primary, Equipment eq, bool compound,
        MuscleGroup[]? secondary = null, string[]? contra = null) => new()
    {
        Name = name,
        PrimaryMuscleGroup = primary,
        Equipment = eq,
        IsCompound = compound,
        SecondaryMuscleGroups = secondary?.ToList() ?? [],
        ContraindicationTags = contra?.ToList() ?? [],
    };

    public static readonly List<Exercise> Items =
    [
        // Peito
        Ex("Supino reto com barra", MuscleGroup.Chest, Equipment.Barbell, true, [MuscleGroup.Triceps, MuscleGroup.Shoulders], ["shoulder"]),
        Ex("Supino inclinado com halteres", MuscleGroup.Chest, Equipment.Dumbbell, true, [MuscleGroup.Triceps, MuscleGroup.Shoulders], ["shoulder"]),
        Ex("Crucifixo na máquina (peck deck)", MuscleGroup.Chest, Equipment.Machine, false, null, ["shoulder"]),
        Ex("Crossover na polia", MuscleGroup.Chest, Equipment.Cable, false),
        Ex("Flexão de braço", MuscleGroup.Chest, Equipment.Bodyweight, true, [MuscleGroup.Triceps, MuscleGroup.Shoulders], ["wrist"]),

        // Costas
        Ex("Levantamento terra", MuscleGroup.Back, Equipment.Barbell, true, [MuscleGroup.Hamstrings, MuscleGroup.Glutes, MuscleGroup.LowerBack], ["lower-back", "knee"]),
        Ex("Barra fixa (pull-up)", MuscleGroup.Back, Equipment.Bodyweight, true, [MuscleGroup.Biceps], ["shoulder", "elbow"]),
        Ex("Puxada alta na polia (pulldown)", MuscleGroup.Back, Equipment.Cable, true, [MuscleGroup.Biceps], ["shoulder"]),
        Ex("Remada curvada com barra", MuscleGroup.Back, Equipment.Barbell, true, [MuscleGroup.Biceps, MuscleGroup.LowerBack], ["lower-back"]),
        Ex("Remada baixa sentada", MuscleGroup.Back, Equipment.Cable, true, [MuscleGroup.Biceps]),
        Ex("Remada unilateral com halter (serrote)", MuscleGroup.Back, Equipment.Dumbbell, true, [MuscleGroup.Biceps]),
        Ex("Barra fixa pegada supinada (chin-up)", MuscleGroup.Back, Equipment.Bodyweight, true, [MuscleGroup.Biceps], ["shoulder", "elbow"]),
        Ex("Remada cavalinho (T-bar)", MuscleGroup.Back, Equipment.Barbell, true, [MuscleGroup.Biceps, MuscleGroup.LowerBack], ["lower-back"]),
        Ex("Remada na máquina", MuscleGroup.Back, Equipment.Machine, true, [MuscleGroup.Biceps]),
        Ex("Puxada na polia com pegada fechada (triângulo)", MuscleGroup.Back, Equipment.Cable, true, [MuscleGroup.Biceps], ["shoulder"]),
        Ex("Pullover na polia com braços estendidos", MuscleGroup.Back, Equipment.Cable, false, null, ["shoulder"]),
        Ex("Encolhimento com halteres (trapézio)", MuscleGroup.Back, Equipment.Dumbbell, false, null, ["neck"]),

        // Ombros
        Ex("Desenvolvimento militar com barra", MuscleGroup.Shoulders, Equipment.Barbell, true, [MuscleGroup.Triceps], ["shoulder", "lower-back"]),
        Ex("Desenvolvimento com halteres sentado", MuscleGroup.Shoulders, Equipment.Dumbbell, true, [MuscleGroup.Triceps], ["shoulder"]),
        Ex("Elevação lateral com halteres", MuscleGroup.Shoulders, Equipment.Dumbbell, false, null, ["shoulder"]),
        Ex("Elevação frontal com halteres", MuscleGroup.Shoulders, Equipment.Dumbbell, false, null, ["shoulder"]),
        Ex("Crucifixo inverso na máquina", MuscleGroup.Shoulders, Equipment.Machine, false),

        // Bíceps
        Ex("Rosca direta com barra", MuscleGroup.Biceps, Equipment.Barbell, false, null, ["elbow", "wrist"]),
        Ex("Rosca alternada com halteres", MuscleGroup.Biceps, Equipment.Dumbbell, false, null, ["elbow"]),
        Ex("Rosca martelo", MuscleGroup.Biceps, Equipment.Dumbbell, false, [MuscleGroup.Forearms]),
        Ex("Rosca Scott na máquina", MuscleGroup.Biceps, Equipment.Machine, false, null, ["elbow"]),
        Ex("Rosca concentrada", MuscleGroup.Biceps, Equipment.Dumbbell, false, null, ["elbow"]),
        Ex("Rosca na polia baixa", MuscleGroup.Biceps, Equipment.Cable, false, null, ["elbow"]),
        Ex("Rosca martelo no cabo (corda)", MuscleGroup.Biceps, Equipment.Cable, false, [MuscleGroup.Forearms]),
        Ex("Rosca inversa com barra W", MuscleGroup.Biceps, Equipment.Barbell, false, [MuscleGroup.Forearms], ["wrist", "elbow"]),

        // Tríceps
        Ex("Tríceps na polia (pushdown)", MuscleGroup.Triceps, Equipment.Cable, false, null, ["elbow"]),
        Ex("Tríceps testa com barra W", MuscleGroup.Triceps, Equipment.Barbell, false, null, ["elbow"]),
        Ex("Tríceps francês com halter", MuscleGroup.Triceps, Equipment.Dumbbell, false, null, ["elbow", "shoulder"]),
        Ex("Mergulho em paralelas (dips)", MuscleGroup.Triceps, Equipment.Bodyweight, true, [MuscleGroup.Chest, MuscleGroup.Shoulders], ["shoulder", "elbow"]),

        // Quadríceps
        Ex("Agachamento livre com barra", MuscleGroup.Quadriceps, Equipment.Barbell, true, [MuscleGroup.Glutes, MuscleGroup.Hamstrings, MuscleGroup.LowerBack], ["knee", "lower-back", "hip"]),
        Ex("Leg press 45°", MuscleGroup.Quadriceps, Equipment.Machine, true, [MuscleGroup.Glutes], ["knee"]),
        Ex("Cadeira extensora", MuscleGroup.Quadriceps, Equipment.Machine, false, null, ["knee"]),
        Ex("Afundo com halteres", MuscleGroup.Quadriceps, Equipment.Dumbbell, true, [MuscleGroup.Glutes], ["knee"]),
        Ex("Agachamento búlgaro", MuscleGroup.Quadriceps, Equipment.Dumbbell, true, [MuscleGroup.Glutes], ["knee"]),
        Ex("Agachamento no hack", MuscleGroup.Quadriceps, Equipment.Machine, true, [MuscleGroup.Glutes], ["knee"]),

        // Posteriores
        Ex("Stiff com barra", MuscleGroup.Hamstrings, Equipment.Barbell, true, [MuscleGroup.Glutes, MuscleGroup.LowerBack], ["lower-back"]),
        Ex("Mesa flexora", MuscleGroup.Hamstrings, Equipment.Machine, false, null, ["knee"]),
        Ex("Cadeira flexora", MuscleGroup.Hamstrings, Equipment.Machine, false, null, ["knee"]),

        // Glúteos
        Ex("Elevação pélvica (hip thrust)", MuscleGroup.Glutes, Equipment.Barbell, true, [MuscleGroup.Hamstrings], ["hip"]),
        Ex("Cadeira abdutora", MuscleGroup.Glutes, Equipment.Machine, false),
        Ex("Coice na polia (glúteo)", MuscleGroup.Glutes, Equipment.Cable, false),

        // Panturrilhas
        Ex("Panturrilha em pé na máquina", MuscleGroup.Calves, Equipment.Machine, false),
        Ex("Panturrilha sentado", MuscleGroup.Calves, Equipment.Machine, false),

        // Abdômen
        Ex("Prancha abdominal", MuscleGroup.Abs, Equipment.Bodyweight, false, null, ["lower-back", "shoulder"]),
        Ex("Abdominal supra no solo", MuscleGroup.Abs, Equipment.Bodyweight, false, null, ["neck", "lower-back"]),
        Ex("Abdominal na polia alta (crunch)", MuscleGroup.Abs, Equipment.Cable, false),
        Ex("Elevação de pernas suspenso", MuscleGroup.Abs, Equipment.Bodyweight, false, null, ["lower-back", "shoulder"]),

        // Cardio / condicionamento
        Ex("Esteira — corrida contínua", MuscleGroup.Cardio, Equipment.Machine, false, null, ["knee", "hip"]),
        Ex("Bicicleta ergométrica", MuscleGroup.Cardio, Equipment.Machine, false),
        Ex("Remo ergômetro", MuscleGroup.Cardio, Equipment.Machine, true, [MuscleGroup.Back], ["lower-back"]),
        Ex("Burpee", MuscleGroup.FullBody, Equipment.Bodyweight, true, null, ["knee", "wrist", "lower-back"]),
        Ex("Kettlebell swing", MuscleGroup.FullBody, Equipment.Kettlebell, true, [MuscleGroup.Glutes, MuscleGroup.LowerBack], ["lower-back"]),
    ];
}
