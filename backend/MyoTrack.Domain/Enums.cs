namespace MyoTrack.Domain;

public enum Biotype
{
    Ectomorph = 1,
    Mesomorph = 2,
    Endomorph = 3
}

public enum ExperienceLevel
{
    Beginner = 1,
    Intermediate = 2,
    Advanced = 3
}

public enum FitnessGoal
{
    Hypertrophy = 1,
    WeightLoss = 2,
    Conditioning = 3,
    Aesthetics = 4
}

public enum CalorieGoal
{
    Deficit = 1,
    Maintenance = 2,
    Surplus = 3
}

public enum MuscleGroup
{
    Chest = 1,
    Back = 2,
    Shoulders = 3,
    Biceps = 4,
    Triceps = 5,
    Forearms = 6,
    Quadriceps = 7,
    Hamstrings = 8,
    Glutes = 9,
    Calves = 10,
    Abs = 11,
    LowerBack = 12,
    FullBody = 13,
    Cardio = 14
}

public enum Equipment
{
    None = 0,
    Barbell = 1,
    Dumbbell = 2,
    Machine = 3,
    Cable = 4,
    Kettlebell = 5,
    ResistanceBand = 6,
    Bodyweight = 7,
    Other = 99
}

public enum PlanStatus
{
    Draft = 0,
    Active = 1,
    Archived = 2
}

public enum ConsentType
{
    HealthData = 1,
    MediaAiAnalysis = 2,
    TermsOfService = 3,
    PrivacyPolicy = 4
}

public enum AnalysisJobType
{
    WorkoutGeneration = 1,
    DietGeneration = 2,
    MealPhoto = 3,
    ExerciseVideo = 4
}

public enum JobStatus
{
    Pending = 0,
    Processing = 1,
    Completed = 2,
    Failed = 3
}
