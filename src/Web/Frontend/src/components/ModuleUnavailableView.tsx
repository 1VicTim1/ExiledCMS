import Link from "next/link";

import { buttonVariants } from "@/components/ui/button";
import type { PlatformModuleAvailability } from "@/lib/platform-server";

type Props = {
  title: string;
  description: string;
  availability: PlatformModuleAvailability;
  isAdmin: boolean;
};

export function ModuleUnavailableView({ title, description, availability, isAdmin }: Props) {
  return (
    <main className="mx-auto flex min-h-screen w-full max-w-5xl flex-col justify-center px-6 py-16">
      <div className="rounded-3xl border border-border/60 bg-background/70 p-8 shadow-2xl backdrop-blur">
        <p className="mb-3 text-sm uppercase tracking-[0.24em] text-amber-300">403</p>
        <h1 className="text-3xl font-semibold tracking-tight text-foreground">{title}</h1>
        <p className="mt-4 max-w-2xl text-base leading-7 text-muted-foreground">{description}</p>

        <div className="mt-8 flex flex-wrap gap-3">
          <Link href="/" className={buttonVariants({ variant: "default" })}>
            На главную
          </Link>
          <Link href="/swagger" className={buttonVariants({ variant: "outline" })}>
            Central Swagger
          </Link>
        </div>

        {isAdmin ? (
          <div className="mt-10 rounded-2xl border border-border/60 bg-card/70 p-5">
            <h2 className="text-sm font-semibold uppercase tracking-[0.18em] text-muted-foreground">Debug</h2>
            <dl className="mt-4 grid gap-3 text-sm text-muted-foreground">
              <div>
                <dt className="font-medium text-foreground">Module</dt>
                <dd>{availability.moduleId}</dd>
              </div>
              <div>
                <dt className="font-medium text-foreground">Registry URL</dt>
                <dd className="break-all">{availability.requestedUrl}</dd>
              </div>
              <div>
                <dt className="font-medium text-foreground">Reason</dt>
                <dd>{availability.error ?? "Дополнительная диагностика не была передана."}</dd>
              </div>
              {availability.module ? (
                <>
                  <div>
                    <dt className="font-medium text-foreground">Base URL</dt>
                    <dd>{availability.module.baseUrl ?? "Не указан"}</dd>
                  </div>
                  <div>
                    <dt className="font-medium text-foreground">Health URL</dt>
                    <dd>{availability.module.healthUrl ?? "Не указан"}</dd>
                  </div>
                  <div>
                    <dt className="font-medium text-foreground">OpenAPI</dt>
                    <dd>{availability.module.openApiUrl ?? "Не указан"}</dd>
                  </div>
                </>
              ) : null}
            </dl>
          </div>
        ) : null}
      </div>
    </main>
  );
}
