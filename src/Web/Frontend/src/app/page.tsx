import Link from "next/link";
import { cookies } from "next/headers";
import { getTranslations } from "next-intl/server";

import { buttonVariants } from "@/components/ui/button";
import { LanguageSwitcher } from "@/components/lang/LanguageSwitcher";
import { readSessionFromCookieSource } from "@/lib/auth-session";
import { getModuleAvailability } from "@/lib/platform-server";

export default async function Home() {
  const t = await getTranslations("Home");
  const cookieStore = await cookies();
  const session = readSessionFromCookieSource((name) => cookieStore.get(name)?.value);
  const ticketsAvailability = await getModuleAvailability("tickets-service");

  return (
    <div className="relative z-10 flex min-h-screen flex-col">
      <header className="sticky top-0 z-50 flex h-20 items-center justify-between border-b border-border/40 bg-background/50 px-6 backdrop-blur-md">
        <div className="flex items-center gap-3">
          <div className="flex h-9 w-9 items-center justify-center rounded-xl bg-primary font-bold text-primary-foreground">
            EX
          </div>
          <div>
            <p className="text-sm uppercase tracking-[0.18em] text-primary/80">ExiledCMS</p>
            <p className="text-lg font-semibold tracking-tight text-foreground">ExiledProject</p>
          </div>
        </div>

        <nav className="hidden items-center gap-6 text-sm font-medium text-muted-foreground md:flex">
          <Link href="/" className="text-foreground transition-colors hover:text-foreground">
            {t("home")}
          </Link>
          {ticketsAvailability.available ? (
            <Link href="/tickets" className="transition-colors hover:text-foreground">
              {t("support")}
            </Link>
          ) : null}
          <Link href="/swagger" className="transition-colors hover:text-foreground">
            Swagger
          </Link>
          {session.isAuthenticated ? (
            <Link href="/account" className="transition-colors hover:text-foreground">
              Кабинет
            </Link>
          ) : null}
        </nav>

        <div className="flex items-center gap-3">
          <LanguageSwitcher />
          {session.isAuthenticated ? (
            <Link href="/account" className={buttonVariants({ variant: "ghost" })}>
              {session.displayName || "Кабинет"}
            </Link>
          ) : (
            <>
              <Link href="/auth/login" className={buttonVariants({ variant: "ghost" })}>
                Войти
              </Link>
              <Link href="/auth/register" className={buttonVariants({ variant: "default" })}>
                Регистрация
              </Link>
            </>
          )}
        </div>
      </header>

      <main className="flex flex-1 items-center px-6 py-16">
        <div className="mx-auto grid w-full max-w-6xl gap-10 lg:grid-cols-[1.3fr_0.7fr]">
          <section className="space-y-8">
            <div className="inline-flex items-center rounded-full border border-primary/30 bg-primary/10 px-3 py-1 text-sm font-medium text-primary">
              {t("seasonLive")}
            </div>

            <div className="space-y-5">
              <h1 className="max-w-4xl text-5xl font-bold tracking-tighter md:text-7xl">
                {t("title", { name: "ExiledProject" }).split("ExiledProject").map((part, index, parts) => (
                  <span key={index}>
                    {part}
                    {index < parts.length - 1 ? (
                      <span className="bg-gradient-to-r from-primary to-amber-300 bg-clip-text text-transparent">
                        ExiledProject
                      </span>
                    ) : null}
                  </span>
                ))}
              </h1>
              <p className="max-w-2xl text-lg leading-8 text-muted-foreground">{t("description")}</p>
            </div>

            <div className="flex flex-wrap gap-4">
              <Link
                href={session.isAuthenticated ? "/account" : "/auth/register"}
                className={buttonVariants({ size: "lg", variant: "default", className: "h-12 px-8 text-base" })}
              >
                {session.isAuthenticated ? "Открыть кабинет" : "Создать аккаунт"}
              </Link>
              {ticketsAvailability.available ? (
                <Link
                  href="/tickets"
                  className={buttonVariants({ size: "lg", variant: "outline", className: "h-12 px-8 text-base" })}
                >
                  Открыть тикеты
                </Link>
              ) : (
                <Link
                  href="/swagger"
                  className={buttonVariants({ size: "lg", variant: "outline", className: "h-12 px-8 text-base" })}
                >
                  Посмотреть Swagger hub
                </Link>
              )}
            </div>
          </section>

          <section className="grid gap-4">
            <div className="rounded-3xl border border-border/40 bg-card/60 p-6 backdrop-blur-sm">
              <p className="text-sm uppercase tracking-[0.18em] text-primary/80">Auth</p>
              <h2 className="mt-3 text-2xl font-semibold text-foreground">Рабочая связка входа и кабинета</h2>
              <p className="mt-3 text-sm leading-6 text-muted-foreground">
                Логин и регистрация переведены на реальные API-роуты, а кабинет умеет менять пароль, подтверждать почту и настраивать TOTP.
              </p>
            </div>

            <div className="rounded-3xl border border-border/40 bg-card/60 p-6 backdrop-blur-sm">
              <p className="text-sm uppercase tracking-[0.18em] text-primary/80">Tickets</p>
              <h2 className="mt-3 text-2xl font-semibold text-foreground">Модульные страницы больше не 404</h2>
              <p className="mt-3 text-sm leading-6 text-muted-foreground">
                Если модуль отсутствует в registry ядра, обычный пользователь получает понятный запрет, а администратор — диагностическую информацию.
              </p>
            </div>

            <div className="grid grid-cols-2 gap-4">
              <div className="rounded-2xl border border-border/40 bg-card/50 p-5 text-center">
                <span className="text-3xl font-bold text-foreground">1.2k+</span>
                <p className="mt-1 text-sm text-muted-foreground">{t("stats.players")}</p>
              </div>
              <div className="rounded-2xl border border-border/40 bg-card/50 p-5 text-center">
                <span className="text-3xl font-bold text-foreground">99.9%</span>
                <p className="mt-1 text-sm text-muted-foreground">{t("stats.uptime")}</p>
              </div>
              <div className="rounded-2xl border border-border/40 bg-card/50 p-5 text-center">
                <span className="text-3xl font-bold text-foreground">2FA</span>
                <p className="mt-1 text-sm text-muted-foreground">TOTP и email verification</p>
              </div>
              <div className="rounded-2xl border border-border/40 bg-card/50 p-5 text-center">
                <span className="text-3xl font-bold text-foreground">Core</span>
                <p className="mt-1 text-sm text-muted-foreground">Swagger hub и registry</p>
              </div>
            </div>
          </section>
        </div>
      </main>

      <footer className="border-t border-border/40 bg-background/50 py-8 text-center text-sm text-muted-foreground backdrop-blur-md">
        <p>{t("footer", { year: new Date().getFullYear() })}</p>
      </footer>
    </div>
  );
}
