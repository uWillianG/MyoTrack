namespace MyoTrack.Domain.Entities;

/// <summary>
/// Resultado da análise de refeição por foto. Os itens ficam em JSONB
/// (ResultJson) para permitir edição manual sem migração de schema.
/// </summary>
public class MealPhotoAnalysis
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public Guid AnalysisJobId { get; set; }
    public string MediaKey { get; set; } = null!;

    /// <summary>Itens detectados: [{description, foodItemId?, quantityG, kcal, proteinG, carbsG, fatG}].</summary>
    public string ItemsJson { get; set; } = "[]";

    public decimal TotalKcal { get; set; }
    public decimal TotalProteinG { get; set; }
    public decimal TotalCarbsG { get; set; }
    public decimal TotalFatG { get; set; }

    /// <summary>True quando o usuário corrigiu a estimativa (sinal de qualidade para o futuro).</summary>
    public bool UserAdjusted { get; set; }

    /// <summary>
    /// Versão "ilustrada" da foto (IA anota itens e macros na própria imagem),
    /// gerada quando o usuário escolhe esse modo. Null = análise padrão ou
    /// geração indisponível/falhou (a análise continua válida sem ela).
    /// </summary>
    public string? IllustratedMediaKey { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>Quando a mídia foi apagada do storage pela política de retenção (LGPD).</summary>
    public DateTimeOffset? MediaExpiredAt { get; set; }
}
