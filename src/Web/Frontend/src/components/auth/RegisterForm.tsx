"use client";

import Link from "next/link";
import { useRouter } from "next/navigation";
import { useState } from "react";

import { Button } from "@/components/ui/button";
import { authCardClassName, authInputClassName } from "@/components/auth/AuthFormShared";

export function RegisterForm() {
  const router = useRouter();
  const [email, setEmail] = useState("");
  const [displayName, setDisplayName] = useState("");
  const [password, setPassword] = useState("");
  const [error, setError] = useState<string | null>(null);
  const [loading, setLoading] = useState(false);

  async function handleSubmit(event: React.FormEvent<HTMLFormElement>) {
    event.preventDefault();
    setLoading(true);
    setError(null);

    const response = await fetch("/api/auth/register", {
      method: "POST",
      headers: {
        "Content-Type": "application/json",
      },
      body: JSON.stringify({
        email,
        displayName,
        password,
      }),
    });

    if (!response.ok) {
      const payload = (await response.json().catch(() => null)) as { detail?: string } | null;
      setError(payload?.detail ?? "Не удалось зарегистрировать аккаунт.");
      setLoading(false);
      return;
    }

    router.push("/account");
    router.refresh();
  }

  return (
    <div className={authCardClassName}>
      <p className="text-sm uppercase tracking-[0.24em] text-primary/80">Auth</p>
      <h1 className="mt-3 text-3xl font-semibold tracking-tight text-foreground">Регистрация</h1>
      <p className="mt-3 text-sm leading-6 text-muted-foreground">
        После регистрации вы сразу попадёте в кабинет, где можно подтвердить почту, включить 2FA и сменить пароль.
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
          <span className="text-sm text-muted-foreground">Отображаемое имя</span>
          <input
            className={authInputClassName}
            type="text"
            autoComplete="nickname"
            value={displayName}
            onChange={(event) => setDisplayName(event.target.value)}
            required
          />
        </label>

        <label className="block space-y-2">
          <span className="text-sm text-muted-foreground">Пароль</span>
          <input
            className={authInputClassName}
            type="password"
            autoComplete="new-password"
            minLength={8}
            value={password}
            onChange={(event) => setPassword(event.target.value)}
            required
          />
        </label>

        {error ? <p className="text-sm text-rose-300">{error}</p> : null}

        <Button className="h-11 w-full" type="submit" disabled={loading}>
          {loading ? "Создаём аккаунт..." : "Создать аккаунт"}
        </Button>
      </form>

      <p className="mt-6 text-sm text-muted-foreground">
        Уже зарегистрированы?{" "}
        <Link className="text-primary underline-offset-4 hover:underline" href="/auth/login">
          Войти
        </Link>
      </p>
    </div>
  );
}
