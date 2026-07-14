interface Props {
  reviewStatus: string
  reviewNote: string | null
}

/** Badge de supervisão humana: mostra se um profissional já revisou o plano gerado por IA. */
export default function ReviewBadge({ reviewStatus, reviewNote }: Props) {
  if (reviewStatus === 'Approved') {
    return (
      <span className="inline-flex items-center gap-1 rounded-full bg-emerald-100 dark:bg-emerald-900/40 text-emerald-700 dark:text-emerald-300 px-2.5 py-0.5 text-xs font-medium">
        ✓ Revisado por profissional
      </span>
    )
  }
  if (reviewStatus === 'ChangesRequested') {
    return (
      <span
        className="inline-flex items-center gap-1 rounded-full bg-amber-100 dark:bg-amber-900/40 text-amber-700 dark:text-amber-300 px-2.5 py-0.5 text-xs font-medium"
        title={reviewNote ?? undefined}
      >
        ⚠ Ajustes sugeridos{reviewNote ? `: ${reviewNote}` : ''}
      </span>
    )
  }
  return (
    <span className="inline-flex items-center gap-1 rounded-full bg-slate-100 dark:bg-white/[0.06] text-slate-500 dark:text-slate-400 border border-slate-200/70 dark:border-white/10 px-2.5 py-0.5 text-xs font-medium">
      Gerado por IA — aguardando revisão
    </span>
  )
}
