const rules: { label: string; test: (password: string) => boolean }[] = [
  { label: 'pelo menos 8 caracteres', test: (p) => p.length >= 8 },
  { label: 'uma letra maiúscula', test: (p) => /[A-ZÀ-Þ]/.test(p) },
  { label: 'uma letra minúscula', test: (p) => /[a-zß-ÿ]/.test(p) },
  { label: 'um número', test: (p) => /\d/.test(p) },
  { label: 'um símbolo (!, @, #…)', test: (p) => /[^A-Za-zÀ-ÿ0-9]/.test(p) },
]

/**
 * Regras verificáveis no browser. As outras duas — senha muito comum e senha
 * parecida com o e-mail/nome — dependem do servidor e chegam como erro.
 */
export default function PasswordRules({ password }: { password: string }) {
  return (
    <ul className="space-y-0.5 text-xs">
      {rules.map((rule) => {
        const ok = rule.test(password)
        return (
          <li
            key={rule.label}
            className={ok ? 'text-emerald-600 dark:text-emerald-400' : 'text-slate-400'}
          >
            <span aria-hidden="true" className="mr-1.5">
              {ok ? '✓' : '•'}
            </span>
            {rule.label}
          </li>
        )
      })}
    </ul>
  )
}
