namespace MyoTrack.Infrastructure.Ai;

/// <summary>
/// Falha transiente de IA (rate limit, indisponibilidade, resposta truncada).
/// Diferente de InvalidOperationException (erro de negócio, sem retry), esta
/// exceção faz o JobPollerService devolver o job à fila até MaxAttempts.
/// </summary>
public class TransientAiException(string message) : Exception(message);
