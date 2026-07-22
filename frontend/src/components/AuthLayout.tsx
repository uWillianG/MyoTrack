/** Moldura comum das telas fora do app (login, recuperação de senha). */
export default function AuthLayout({
  subtitle,
  children,
}: {
  subtitle: string
  children: React.ReactNode
}) {
  return (
    <div className="min-h-screen flex items-center justify-center px-4">
      <div className="w-full max-w-sm card p-8">
        <div className="space-y-3 pb-6 text-center">
          <span className="mx-auto flex h-12 w-12 items-center justify-center rounded-2xl bg-linear-to-br from-emerald-400 to-teal-600 text-xl font-bold text-white shadow-lg shadow-emerald-600/30">
            M
          </span>
          <h1 className="font-display text-3xl font-bold tracking-tight text-slate-900 dark:text-white">
            Myo<span className="text-emerald-500">Track</span>
          </h1>
          <p className="text-sm text-slate-500 dark:text-slate-400">{subtitle}</p>
        </div>
        {children}
      </div>
    </div>
  )
}
