namespace MyoTrack.Domain.Services;

public record MacroTargets(decimal Kcal, decimal ProteinG, decimal CarbsG, decimal FatG);

/// <summary>
/// Cálculo determinístico de gasto energético e macros — nunca delegado ao LLM.
/// TMB por Mifflin-St Jeor; guard-rail: a meta calórica nunca fica abaixo da TMB.
/// </summary>
public static class TdeeCalculator
{
    public static decimal CalculateBmr(string sex, decimal weightKg, decimal heightCm, int ageYears)
    {
        var baseValue = 10m * weightKg + 6.25m * heightCm - 5m * ageYears;
        return sex.Equals("F", StringComparison.OrdinalIgnoreCase) ? baseValue - 161m : baseValue + 5m;
    }

    /// <summary>Fator de atividade aproximado a partir dos dias de treino por semana.</summary>
    public static decimal ActivityFactor(int trainingDaysPerWeek) => trainingDaysPerWeek switch
    {
        <= 1 => 1.2m,
        2 or 3 => 1.375m,
        4 or 5 => 1.55m,
        _ => 1.725m,
    };

    public static decimal CalculateTdee(string sex, decimal weightKg, decimal heightCm, int ageYears, int trainingDaysPerWeek) =>
        CalculateBmr(sex, weightKg, heightCm, ageYears) * ActivityFactor(trainingDaysPerWeek);

    public static MacroTargets CalculateTargets(
        string sex, decimal weightKg, decimal heightCm, int ageYears, int trainingDaysPerWeek, CalorieGoal goal)
    {
        var bmr = CalculateBmr(sex, weightKg, heightCm, ageYears);
        var tdee = bmr * ActivityFactor(trainingDaysPerWeek);

        var kcal = goal switch
        {
            CalorieGoal.Deficit => tdee * 0.80m,
            CalorieGoal.Surplus => tdee * 1.10m,
            _ => tdee,
        };

        // Guard-rail de segurança: nunca prescrever abaixo da TMB.
        kcal = Math.Max(kcal, bmr);

        // Proteína 2 g/kg (déficit) ou 1.8 g/kg; gordura 25% das kcal; carbo fecha o restante.
        var proteinG = weightKg * (goal == CalorieGoal.Deficit ? 2.0m : 1.8m);
        var fatG = kcal * 0.25m / 9m;
        var carbsG = Math.Max(0m, (kcal - proteinG * 4m - fatG * 9m) / 4m);

        return new MacroTargets(
            Math.Round(kcal, 0),
            Math.Round(proteinG, 0),
            Math.Round(carbsG, 0),
            Math.Round(fatG, 0));
    }

    public static int CalculateAge(DateOnly birthDate, DateOnly today)
    {
        var age = today.Year - birthDate.Year;
        if (today < birthDate.AddYears(age)) age--;
        return age;
    }
}
