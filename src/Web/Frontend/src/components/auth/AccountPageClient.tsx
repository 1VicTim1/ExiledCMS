"use client";

import { useEffect, useState } from "react";
import { useRouter } from "next/navigation";

import { Button } from "@/components/ui/button";
import { authInputClassName } from "@/components/auth/AuthFormShared";

type Profile = {
  id: string;
  email: string;
  displayName: string;
  emailVerified: boolean;
  totpEnabled: boolean;
  status: string;
  roles: string[];
  permissions: string[];
  lastLoginAtUtc?: string | null;
};

type VerificationResponse = {
  token: string;
  emailVerified: boolean;
};

type TotpSetup = {
  secret: string;
  manualEntryKey: string;
  otpAuthUrl: string;
};

export function AccountPageClient() {
  const router = useRouter();
  const [profile, setProfile] = useState<Profile | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [passwordState, setPasswordState] = useState({ currentPassword: "", newPassword: "" });
  const [emailToken, setEmailToken] = useState("");
  const [issuedToken, setIssuedToken] = useState<string | null>(null);
  const [totpSetup, setTotpSetup] = useState<TotpSetup | null>(null);
  const [totpCode, setTotpCode] = useState("");
  const [disableTotpPassword, setDisableTotpPassword] = useState("");
  const [message, setMessage] = useState<string | null>(null);

  async function loadProfile() {
    setLoading(true);
    setError(null);

    const response = await fetch("/api/auth/me", { cache: "no-store" });
    if (!response.ok) {
      const payload = (await response.json().catch(() => null)) as { detail?: string } | null;
      setError(payload?.detail ?? "Не удалось загрузить профиль.");
      setLoading(false);
      return;
    }

    setProfile((await response.json()) as Profile);
    setLoading(false);
  }

  useEffect(() => {
    queueMicrotask(() => {
      void loadProfile();
    });
  }, []);

  async function submitJson(path: string, body?: unknown) {
    const response = await fetch(path, {
      method: "POST",
      headers: body ? { "Content-Type": "application/json" } : undefined,
      body: body ? JSON.stringify(body) : undefined,
    });

    if (!response.ok) {
      const payload = (await response.json().catch(() => null)) as { detail?: string } | null;
      throw new Error(payload?.detail ?? "Операция завершилась ошибкой.");
    }

    return response;
  }

  async function handlePasswordChange(event: React.FormEvent<HTMLFormElement>) {
    event.preventDefault();
    setMessage(null);

    try {
      const response = await submitJson("/api/auth/password", passwordState);
      setProfile((await response.json()) as Profile);
      setPasswordState({ currentPassword: "", newPassword: "" });
      setMessage("Пароль успешно обновлён.");
    } catch (submitError) {
      setError(submitError instanceof Error ? submitError.message : "Не удалось сменить пароль.");
    }
  }

  async function handleIssueVerification() {
    setMessage(null);
    setError(null);

    try {
      const response = await submitJson("/api/auth/email/verification");
      const payload = (await response.json()) as VerificationResponse;
      setIssuedToken(payload.token || null);
      setMessage(payload.emailVerified ? "Почта уже подтверждена." : "Выдан новый verification token.");
      await loadProfile();
    } catch (submitError) {
      setError(submitError instanceof Error ? submitError.message : "Не удалось выдать verification token.");
    }
  }

  async function handleConfirmEmail(event: React.FormEvent<HTMLFormElement>) {
    event.preventDefault();
    setMessage(null);
    setError(null);

    try {
      const response = await submitJson("/api/auth/email/confirm", { token: emailToken });
      setProfile((await response.json()) as Profile);
      setEmailToken("");
      setIssuedToken(null);
      setMessage("Почта подтверждена.");
    } catch (submitError) {
      setError(submitError instanceof Error ? submitError.message : "Не удалось подтвердить почту.");
    }
  }

  async function handleTotpSetup() {
    setMessage(null);
    setError(null);

    try {
      const response = await submitJson("/api/auth/2fa/setup");
      setTotpSetup((await response.json()) as TotpSetup);
      setMessage("Секрет TOTP сгенерирован. Подтвердите код из приложения.");
    } catch (submitError) {
      setError(submitError instanceof Error ? submitError.message : "Не удалось начать настройку 2FA.");
    }
  }

  async function handleEnableTotp(event: React.FormEvent<HTMLFormElement>) {
    event.preventDefault();
    setMessage(null);
    setError(null);

    try {
      const response = await submitJson("/api/auth/2fa/enable", { code: totpCode });
      setProfile((await response.json()) as Profile);
      setTotpSetup(null);
      setTotpCode("");
      setMessage("2FA включена.");
    } catch (submitError) {
      setError(submitError instanceof Error ? submitError.message : "Не удалось включить 2FA.");
    }
  }

  async function handleDisableTotp(event: React.FormEvent<HTMLFormElement>) {
    event.preventDefault();
    setMessage(null);
    setError(null);

    try {
      const response = await submitJson("/api/auth/2fa/disable", { currentPassword: disableTotpPassword });
      setProfile((await response.json()) as Profile);
      setDisableTotpPassword("");
      setMessage("2FA отключена.");
    } catch (submitError) {
      setError(submitError instanceof Error ? submitError.message : "Не удалось отключить 2FA.");
    }
  }

  async function handleLogout() {
    await fetch("/api/auth/logout", { method: "POST" });
    router.push("/auth/login");
    router.refresh();
  }

  if (loading) {
    return <div className="text-sm text-muted-foreground">Загрузка профиля...</div>;
  }

  if (!profile) {
    return <div className="text-sm text-rose-300">{error ?? "Профиль не найден."}</div>;
  }

  return (
    <div className="mx-auto flex w-full max-w-5xl flex-col gap-6 px-6 py-16">
      <div className="rounded-3xl border border-border/60 bg-background/75 p-8 shadow-2xl backdrop-blur">
        <div className="flex flex-col gap-3 md:flex-row md:items-end md:justify-between">
          <div>
            <p className="text-sm uppercase tracking-[0.24em] text-primary/80">Account</p>
            <h1 className="mt-3 text-3xl font-semibold tracking-tight text-foreground">Личный кабинет</h1>
            <p className="mt-3 max-w-2xl text-sm leading-6 text-muted-foreground">
              Здесь можно управлять безопасностью аккаунта и проверять, достаточно ли прав для работы с тикетами и другими модулями.
            </p>
          </div>
          <Button variant="outline" onClick={handleLogout}>
            Выйти
          </Button>
        </div>

        {message ? <p className="mt-6 text-sm text-emerald-300">{message}</p> : null}
        {error ? <p className="mt-3 text-sm text-rose-300">{error}</p> : null}
      </div>

      <section className="grid gap-6 lg:grid-cols-2">
        <div className="rounded-3xl border border-border/60 bg-card/70 p-6">
          <h2 className="text-xl font-semibold text-foreground">Профиль</h2>
          <dl className="mt-5 space-y-3 text-sm text-muted-foreground">
            <div>
              <dt className="font-medium text-foreground">Email</dt>
              <dd>{profile.email}</dd>
            </div>
            <div>
              <dt className="font-medium text-foreground">Имя</dt>
              <dd>{profile.displayName}</dd>
            </div>
            <div>
              <dt className="font-medium text-foreground">Статус</dt>
              <dd>{profile.status}</dd>
            </div>
            <div>
              <dt className="font-medium text-foreground">Email confirmed</dt>
              <dd>{profile.emailVerified ? "Да" : "Нет"}</dd>
            </div>
            <div>
              <dt className="font-medium text-foreground">2FA</dt>
              <dd>{profile.totpEnabled ? "Включена" : "Выключена"}</dd>
            </div>
            <div>
              <dt className="font-medium text-foreground">Роли</dt>
              <dd>{profile.roles.join(", ") || "Нет"}</dd>
            </div>
            <div>
              <dt className="font-medium text-foreground">Права</dt>
              <dd className="break-words">{profile.permissions.join(", ") || "Нет"}</dd>
            </div>
          </dl>
        </div>

        <div className="rounded-3xl border border-border/60 bg-card/70 p-6">
          <h2 className="text-xl font-semibold text-foreground">Смена пароля</h2>
          <form className="mt-5 space-y-4" onSubmit={handlePasswordChange}>
            <input
              className={authInputClassName}
              type="password"
              placeholder="Текущий пароль"
              value={passwordState.currentPassword}
              onChange={(event) => setPasswordState((current) => ({ ...current, currentPassword: event.target.value }))}
              required
            />
            <input
              className={authInputClassName}
              type="password"
              placeholder="Новый пароль"
              value={passwordState.newPassword}
              onChange={(event) => setPasswordState((current) => ({ ...current, newPassword: event.target.value }))}
              required
            />
            <Button type="submit">Сменить пароль</Button>
          </form>
        </div>
      </section>

      <section className="grid gap-6 lg:grid-cols-2">
        <div className="rounded-3xl border border-border/60 bg-card/70 p-6">
          <h2 className="text-xl font-semibold text-foreground">Подтверждение почты</h2>
          <p className="mt-3 text-sm leading-6 text-muted-foreground">
            Создание тикетов доступно только после подтверждения почты. Пока SMTP/sendmail не подключён, verification token можно запросить вручную здесь.
          </p>

          <div className="mt-5 flex flex-wrap gap-3">
            <Button type="button" variant="outline" onClick={handleIssueVerification}>
              Выдать verification token
            </Button>
          </div>

          {issuedToken ? (
            <div className="mt-5 rounded-2xl border border-border/60 bg-background/60 p-4 text-sm">
              <p className="font-medium text-foreground">Verification token</p>
              <p className="mt-2 break-all text-muted-foreground">{issuedToken}</p>
            </div>
          ) : null}

          <form className="mt-5 space-y-4" onSubmit={handleConfirmEmail}>
            <input
              className={authInputClassName}
              type="text"
              placeholder="Вставьте verification token"
              value={emailToken}
              onChange={(event) => setEmailToken(event.target.value)}
              required
            />
            <Button type="submit">Подтвердить почту</Button>
          </form>
        </div>

        <div className="rounded-3xl border border-border/60 bg-card/70 p-6">
          <h2 className="text-xl font-semibold text-foreground">Two-factor authentication</h2>
          <p className="mt-3 text-sm leading-6 text-muted-foreground">
            Поддерживается TOTP. Секрет можно импортировать в Google Authenticator, Aegis, 1Password или любой другой совместимый клиент.
          </p>

          {!profile.totpEnabled ? (
            <>
              <div className="mt-5 flex flex-wrap gap-3">
                <Button type="button" variant="outline" onClick={handleTotpSetup}>
                  Сгенерировать секрет
                </Button>
              </div>

              {totpSetup ? (
                <div className="mt-5 rounded-2xl border border-border/60 bg-background/60 p-4 text-sm text-muted-foreground">
                  <p><span className="font-medium text-foreground">Secret:</span> {totpSetup.secret}</p>
                  <p className="mt-2 break-all"><span className="font-medium text-foreground">OTPAuth URI:</span> {totpSetup.otpAuthUrl}</p>
                </div>
              ) : null}

              <form className="mt-5 space-y-4" onSubmit={handleEnableTotp}>
                <input
                  className={authInputClassName}
                  type="text"
                  inputMode="numeric"
                  placeholder="Код из приложения"
                  value={totpCode}
                  onChange={(event) => setTotpCode(event.target.value)}
                  required
                />
                <Button type="submit">Включить 2FA</Button>
              </form>
            </>
          ) : (
            <form className="mt-5 space-y-4" onSubmit={handleDisableTotp}>
              <input
                className={authInputClassName}
                type="password"
                placeholder="Текущий пароль для отключения 2FA"
                value={disableTotpPassword}
                onChange={(event) => setDisableTotpPassword(event.target.value)}
                required
              />
              <Button type="submit" variant="outline">
                Отключить 2FA
              </Button>
            </form>
          )}
        </div>
      </section>
    </div>
  );
}
