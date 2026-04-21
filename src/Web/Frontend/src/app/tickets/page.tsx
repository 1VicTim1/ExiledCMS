import { cookies } from "next/headers";

import { ModuleUnavailableView } from "@/components/ModuleUnavailableView";
import TicketsPageClient from "@/components/tickets/TicketsPageClient";
import { readSessionFromCookieSource } from "@/lib/auth-session";
import { getModuleAvailability } from "@/lib/platform-server";

export default async function TicketsPage() {
  const cookieStore = await cookies();
  const session = readSessionFromCookieSource((name) => cookieStore.get(name)?.value);
  const availability = await getModuleAvailability("tickets-service");
  if (!availability.available) {
    const isAdmin =
      session.role?.toLowerCase() === "admin" ||
      session.permissions.includes("platform.registry.view") ||
      session.permissions.includes("auth.users.list");

    return (
      <ModuleUnavailableView
        title="Модуль тикетов недоступен"
        description="Для обычного пользователя страница скрыта как недоступная. Если вы администратор, ниже показана диагностика из platform-core."
        availability={availability}
        isAdmin={isAdmin}
      />
    );
  }

  return (
    <TicketsPageClient
      session={{
        isAuthenticated: session.isAuthenticated,
        displayName: session.displayName,
      }}
    />
  );
}
