"use client";

import Link from "next/link";
import { useRouter, useSearchParams } from "next/navigation";
import { useState } from "react";

import { Button } from "@/components/ui/button";
import { authCardClassName, authInputClassName } from "@/components/auth/AuthFormShared";

export function LoginForm() {
  const router = useRouter();
  const searchParams = useSearchParams();
  const [email, setEmail] = useState("");
  const [password, setPassword] = useState("");
  const [totpCode, setTotpCode] = useState("");
  const [error, setError] = useState<string | null>(null);
  const [loading, setLoading] = useState(false);

  async function handleSubmit(event: React.FormEvent<HTMLFormElement>) {
    event.preventDefault();
    setLoading(true);
    setError(null);

    const response = await fetch("/api/auth/login", {
      method: "POST",
      headers: {
        "Content-Type": "application/json",
      },
      body: JSON.stringify({
        email,
        password,
        totpCode: totpCode.trim() || undefined,
      }),
    });

    if (!response.ok) {
      const payload = (await response.json().catch(() => null)) as { detail?: string } | null;
      setError(payload?.detail ?? "Не удалось выполнить вход.");
      setLoading(false);
      return;
    }

    router.push(searchParams.get("next") || "/account");
    router.refresh();
  }

  return (
    <div className={authCardClassName}>
      <p className="text-sm uppercase tracking-[0.24em] text-primary/80">Auth</p>
      <h1 className="mt-3 text-3xl font-semibold tracking-tight text-foreground">Вход в ExiledCMS</h1>
      <p className="mt-3 text-sm leading-6 text-muted-foreground">
        Войдите в аккаунт, чтобы открыть личный кабинет и работать с тикетами без dev-заглушек.
      </p>

      <form className="mt-8 space-y-4" onSubmit={handleSubmit}>
        <label className="block space-y-2">
          <span className="text-sm text-muted-foreground">Email</span>
          <input
            className={authInputClassName}
            type="email"
            autoComplete="email"
            value={email}
            onChange={(event) => setEmail(event.target.value)}
            required
          />
        </label>

        <label className="block space-y-2">
          <span className="text-sm text-muted-foreground">Пароль</span>
          <input
            className={authInputClassName}
            type="password"
            autoComplete="current-password"
            value={password}
            onChange={(event) => setPassword(event.target.value)}
            required
          />
        </label>

        <label className="block space-y-2">
          <span className="text-sm text-muted-foreground">Код 2FA</span>
          <input
            className={authInputClassName}
            type="text"
            inputMode="numeric"
            placeholder="Только если у вас включён TOTP"
            value={totpCode}
            onChange={(event) => setTotpCode(event.target.value)}
          />
        </label>

        {error ? <p className="text-sm text-rose-300">{error}</p> : null}

        <Button className="h-11 w-full" type="submit" disabled={loading}>
          {loading ? "Выполняется вход..." : "Войти"}
        </Button>
      </form>

      <p className="mt-6 text-sm text-muted-foreground">
        Нет аккаунта?{" "}
        <Link className="text-primary underline-offset-4 hover:underline" href="/auth/register">
          Зарегистрироваться
        </Link>
      </p>
    </div>
  );
}
