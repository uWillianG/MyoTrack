using MyoTrack.Domain.Entities;

namespace MyoTrack.Infrastructure.Seed;

/// <summary>
/// Alimentos básicos da dieta brasileira, valores por 100 g (base TACO 4ª ed., arredondados).
/// A importação completa da TACO/TBCA (~600 itens) entra na Fase 1.
/// </summary>
public static class FoodSeed
{
    private static FoodItem F(string name, decimal kcal, decimal p, decimal c, decimal g, decimal? fiber = null) => new()
    {
        Name = name,
        KcalPer100g = kcal,
        ProteinPer100g = p,
        CarbsPer100g = c,
        FatPer100g = g,
        FiberPer100g = fiber,
        Source = "TACO",
    };

    public static readonly List<FoodItem> Items =
    [
        // Cereais e derivados
        F("Arroz branco cozido", 128m, 2.5m, 28.1m, 0.2m, 1.6m),
        F("Arroz integral cozido", 124m, 2.6m, 25.8m, 1.0m, 2.7m),
        F("Aveia em flocos crua", 394m, 13.9m, 66.6m, 8.5m, 9.1m),
        F("Pão francês", 300m, 8.0m, 58.6m, 3.1m, 2.3m),
        F("Pão integral de trigo", 253m, 9.4m, 49.9m, 3.7m, 6.9m),
        F("Macarrão cozido", 122m, 3.9m, 24.5m, 1.3m, 1.5m),
        F("Tapioca (goma hidratada)", 240m, 0m, 60.0m, 0m, 0m),
        F("Batata inglesa cozida", 52m, 1.2m, 11.9m, 0m, 1.3m),
        F("Batata-doce cozida", 77m, 0.6m, 18.4m, 0.1m, 2.2m),
        F("Mandioca cozida", 125m, 0.6m, 30.1m, 0.3m, 1.6m),

        // Leguminosas
        F("Feijão carioca cozido", 76m, 4.8m, 13.6m, 0.5m, 8.5m),
        F("Feijão preto cozido", 77m, 4.5m, 14.0m, 0.5m, 8.4m),
        F("Lentilha cozida", 93m, 6.3m, 16.3m, 0.5m, 7.9m),
        F("Grão-de-bico cozido", 164m, 8.9m, 27.4m, 2.6m, 7.6m),

        // Carnes e ovos
        F("Peito de frango grelhado (sem pele)", 159m, 32.0m, 0m, 2.5m),
        F("Coxa de frango assada (sem pele)", 167m, 26.9m, 0m, 5.8m),
        F("Carne bovina — patinho grelhado", 219m, 35.9m, 0m, 7.3m),
        F("Carne bovina — acém moído cozido", 212m, 26.7m, 0m, 10.9m),
        F("Carne suína — lombo assado", 210m, 35.7m, 0m, 6.4m),
        F("Tilápia grelhada", 128m, 26.0m, 0m, 2.0m),
        F("Sardinha assada", 164m, 32.2m, 0m, 3.9m),
        F("Atum em conserva (água)", 108m, 24.0m, 0m, 1.0m),
        F("Ovo de galinha cozido", 146m, 13.3m, 0.6m, 9.5m),
        F("Clara de ovo cozida", 59m, 13.4m, 0m, 0.1m),

        // Laticínios
        F("Leite integral", 61m, 3.2m, 4.6m, 3.3m),
        F("Leite desnatado", 35m, 3.4m, 5.0m, 0.2m),
        F("Iogurte natural integral", 51m, 4.1m, 1.9m, 3.0m),
        F("Iogurte natural desnatado", 42m, 3.8m, 5.8m, 0.3m),
        F("Queijo minas frescal", 264m, 17.4m, 3.2m, 20.2m),
        F("Queijo muçarela", 330m, 22.6m, 3.0m, 25.2m),
        F("Requeijão cremoso", 257m, 9.6m, 2.4m, 23.4m),

        // Frutas
        F("Banana prata", 98m, 1.3m, 26.0m, 0.1m, 2.0m),
        F("Maçã com casca", 56m, 0.3m, 15.2m, 0m, 1.3m),
        F("Laranja pera", 37m, 1.0m, 8.9m, 0.1m, 0.8m),
        F("Mamão papaia", 40m, 0.5m, 10.4m, 0.1m, 1.0m),
        F("Abacate", 96m, 1.2m, 6.0m, 8.4m, 6.3m),
        F("Morango", 30m, 0.9m, 6.8m, 0.3m, 1.7m),
        F("Manga palmer", 72m, 0.4m, 19.4m, 0.2m, 1.6m),

        // Verduras e legumes
        F("Brócolis cozido", 25m, 2.1m, 4.4m, 0.5m, 3.4m),
        F("Cenoura crua", 34m, 1.3m, 7.7m, 0.2m, 3.2m),
        F("Tomate cru", 15m, 1.1m, 3.1m, 0.2m, 1.2m),
        F("Alface crespa", 11m, 1.3m, 1.7m, 0.2m, 1.8m),
        F("Abobrinha cozida", 15m, 1.1m, 3.0m, 0.2m, 1.6m),
        F("Couve refogada", 90m, 3.1m, 8.7m, 5.5m, 5.7m),

        // Gorduras e oleaginosas
        F("Azeite de oliva", 884m, 0m, 0m, 100m),
        F("Pasta de amendoim integral", 589m, 22.5m, 21.6m, 46.5m, 8.0m),
        F("Castanha-de-caju torrada", 570m, 18.5m, 29.1m, 46.3m, 3.7m),
        F("Castanha-do-pará", 643m, 14.5m, 15.1m, 63.5m, 7.9m),
        F("Amêndoa torrada", 581m, 18.6m, 29.5m, 47.3m, 11.6m),

        // Suplementos comuns
        F("Whey protein concentrado (pó)", 400m, 80.0m, 8.0m, 6.0m, 0m),
        F("Creatina (pó)", 0m, 0m, 0m, 0m),
    ];
}
