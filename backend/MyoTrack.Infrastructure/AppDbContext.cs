using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using MyoTrack.Domain.Entities;
using MyoTrack.Infrastructure.Identity;

namespace MyoTrack.Infrastructure;

public class AppDbContext(DbContextOptions<AppDbContext> options)
    : IdentityDbContext<ApplicationUser, IdentityRole<Guid>, Guid>(options)
{
    public DbSet<UserProfile> UserProfiles => Set<UserProfile>();
    public DbSet<ConsentRecord> ConsentRecords => Set<ConsentRecord>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();

    public DbSet<Exercise> Exercises => Set<Exercise>();
    public DbSet<WorkoutPlan> WorkoutPlans => Set<WorkoutPlan>();
    public DbSet<WorkoutDay> WorkoutDays => Set<WorkoutDay>();
    public DbSet<WorkoutExercise> WorkoutExercises => Set<WorkoutExercise>();
    public DbSet<WorkoutSession> WorkoutSessions => Set<WorkoutSession>();
    public DbSet<SetLog> SetLogs => Set<SetLog>();

    public DbSet<FoodItem> FoodItems => Set<FoodItem>();
    public DbSet<DietPlan> DietPlans => Set<DietPlan>();
    public DbSet<Meal> Meals => Set<Meal>();
    public DbSet<MealItem> MealItems => Set<MealItem>();

    public DbSet<BodyMeasurement> BodyMeasurements => Set<BodyMeasurement>();
    public DbSet<AnalysisJob> AnalysisJobs => Set<AnalysisJob>();
    public DbSet<MealPhotoAnalysis> MealPhotoAnalyses => Set<MealPhotoAnalysis>();
    public DbSet<ExerciseVideoAnalysis> ExerciseVideoAnalyses => Set<ExerciseVideoAnalysis>();
    public DbSet<AiUsageLog> AiUsageLogs => Set<AiUsageLog>();
    public DbSet<UserSubscription> UserSubscriptions => Set<UserSubscription>();
    public DbSet<StripeEventLog> StripeEventLogs => Set<StripeEventLog>();
    public DbSet<CoachMessage> CoachMessages => Set<CoachMessage>();
    public DbSet<WeeklyReport> WeeklyReports => Set<WeeklyReport>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<UserProfile>(e =>
        {
            e.HasIndex(p => p.UserId).IsUnique();
        });

        builder.Entity<ConsentRecord>(e =>
        {
            e.HasIndex(c => new { c.UserId, c.Type });
        });

        builder.Entity<RefreshToken>(e =>
        {
            e.HasIndex(t => t.TokenHash).IsUnique();
            e.HasIndex(t => t.UserId);
        });

        builder.Entity<Exercise>(e =>
        {
            e.HasIndex(x => x.Name).IsUnique();
            e.Property(x => x.Name).HasMaxLength(200);
            e.Property(x => x.TutorialVideoUrl).HasMaxLength(500);
        });

        builder.Entity<FoodItem>(e =>
        {
            e.HasIndex(x => x.Name);
            e.Property(x => x.Name).HasMaxLength(300);
        });

        builder.Entity<WorkoutPlan>(e =>
        {
            e.HasIndex(p => new { p.UserId, p.Status });
            e.Property(p => p.GenerationInputJson).HasColumnType("jsonb");
            e.Property(p => p.RawLlmOutputJson).HasColumnType("jsonb");
        });

        builder.Entity<DietPlan>(e =>
        {
            e.HasIndex(p => new { p.UserId, p.Status });
            e.Property(p => p.GenerationInputJson).HasColumnType("jsonb");
            e.Property(p => p.RawLlmOutputJson).HasColumnType("jsonb");
        });

        builder.Entity<WorkoutSession>(e =>
        {
            e.HasIndex(s => new { s.UserId, s.Date });
        });

        builder.Entity<SetLog>(e =>
        {
            e.HasIndex(s => s.ExerciseId);
        });

        builder.Entity<BodyMeasurement>(e =>
        {
            e.HasIndex(m => new { m.UserId, m.Date });
        });

        builder.Entity<MealPhotoAnalysis>(e =>
        {
            e.HasIndex(a => new { a.UserId, a.CreatedAt });
            e.HasIndex(a => a.AnalysisJobId).IsUnique();
            e.Property(a => a.ItemsJson).HasColumnType("jsonb");
        });

        builder.Entity<ExerciseVideoAnalysis>(e =>
        {
            e.HasIndex(a => new { a.UserId, a.CreatedAt });
            e.HasIndex(a => a.AnalysisJobId).IsUnique();
            e.Property(a => a.AnalyzedExercise).HasMaxLength(50);
            e.Property(a => a.ResultJson).HasColumnType("jsonb");
        });

        builder.Entity<AiUsageLog>(e =>
        {
            e.HasIndex(l => new { l.UserId, l.CreatedAt });
        });

        builder.Entity<UserSubscription>(e =>
        {
            e.HasIndex(s => s.UserId).IsUnique();
            e.HasIndex(s => s.StripeCustomerId);
            e.HasIndex(s => s.StripeSubscriptionId);
            e.Property(s => s.StripeStatus).HasMaxLength(50);
        });

        builder.Entity<StripeEventLog>(e =>
        {
            e.HasKey(l => l.Id);
            e.Property(l => l.Id).HasMaxLength(255);
            e.Property(l => l.Type).HasMaxLength(100);
        });

        builder.Entity<AnalysisJob>(e =>
        {
            // Índice parcial para o worker buscar jobs pendentes com eficiência.
            e.HasIndex(j => new { j.Status, j.CreatedAt });
            e.Property(j => j.InputJson).HasColumnType("jsonb");
            e.Property(j => j.ResultJson).HasColumnType("jsonb");
        });

        builder.Entity<CoachMessage>(e =>
        {
            e.HasIndex(m => new { m.UserId, m.CreatedAt });
            e.Property(m => m.Content).HasMaxLength(4000);
        });

        builder.Entity<WeeklyReport>(e =>
        {
            e.HasIndex(r => new { r.UserId, r.WeekStart }).IsUnique();
            e.Property(r => r.MetricsJson).HasColumnType("jsonb");
            e.Property(r => r.NarrativeJson).HasColumnType("jsonb");
        });
    }
}
