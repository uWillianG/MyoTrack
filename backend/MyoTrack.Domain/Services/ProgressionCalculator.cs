namespace MyoTrack.Domain.Services;

/// <summary>O que fazer com a carga na próxima sessão de um exercício.</summary>
public enum ProgressionAction
{
    /// <summary>Sem histórico — começar com a carga sugerida do plano.</summary>
    Start,
    /// <summary>Fechou todas as séries no teto de repetições — subir a carga.</summary>
    Increase,
    /// <summary>Dentro da faixa — manter a carga e buscar o teto de repetições.</summary>
    ProgressReps,
    /// <summary>Alguma série abaixo do mínimo — manter a carga e consolidar.</summary>
    Consolidate,
}

/// <summary>Uma série da última sessão do exercício.</summary>
public record SetPerformance(int Reps, decimal LoadKg);

/// <summary>Sugestão para a próxima sessão: ação, carga alvo e repetições alvo.</summary>
public record ProgressionSuggestion(ProgressionAction Action, decimal? NextLoadKg, int TargetReps);

/// <summary>
/// Progressão de carga por dupla progressão, calculada em código (nunca LLM):
/// primeiro progridem as repetições dentro da faixa do plano; ao fechar todas
/// as séries no teto, sobe a carga um incremento e volta ao piso de repetições.
/// </summary>
public static class ProgressionCalculator
{
    /// <summary>
    /// 1RM estimado (fórmula de Epley). Null acima de 12 repetições — a
    /// estimativa perde sentido em séries longas.
    /// </summary>
    public static decimal? EstimateOneRepMax(int reps, decimal loadKg)
    {
        if (reps is < 1 or > 12 || loadKg <= 0)
            return null;
        // Uma repetição única já É o 1RM — Epley superestimaria (carga × 1,033).
        if (reps == 1)
            return loadKg;
        return Math.Round(loadKg * (1 + reps / 30m), 1);
    }

    /// <summary>
    /// Incremento conservador por grupo muscular: membros inferiores e levantamentos
    /// de corpo inteiro toleram saltos maiores que os pequenos grupos do tronco.
    /// </summary>
    public static decimal IncrementFor(MuscleGroup group) => group switch
    {
        MuscleGroup.Quadriceps or MuscleGroup.Hamstrings or MuscleGroup.Glutes
            or MuscleGroup.Calves or MuscleGroup.LowerBack or MuscleGroup.FullBody => 5m,
        _ => 2.5m,
    };

    public static ProgressionSuggestion Suggest(
        IReadOnlyList<SetPerformance> lastSets, int repsMin, int repsMax, decimal incrementKg)
    {
        if (lastSets.Count == 0)
            return new(ProgressionAction.Start, null, repsMin);

        // Carga de trabalho da sessão: a maior usada (séries de aquecimento não atrapalham).
        var load = lastSets.Max(s => s.LoadKg);
        var workSets = lastSets.Where(s => s.LoadKg == load).ToList();

        if (workSets.All(s => s.Reps >= repsMax))
            return new(ProgressionAction.Increase, load + incrementKg, repsMin);
        if (workSets.All(s => s.Reps >= repsMin))
            return new(ProgressionAction.ProgressReps, load, repsMax);
        return new(ProgressionAction.Consolidate, load, repsMin);
    }
}
