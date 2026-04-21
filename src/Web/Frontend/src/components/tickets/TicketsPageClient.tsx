"use client";

import Link from "next/link";
import { useCallback, useEffect, useMemo, useState } from "react";
import { useLocale, useTranslations } from "next-intl";
import {
  ArrowUpRight,
  BadgeCheck,
  Clock3,
  Filter,
  LifeBuoy,
  LoaderCircle,
  RefreshCw,
  ShieldAlert,
  Sparkles,
  Ticket,
  Upload,
} from "lucide-react";

import { LanguageSwitcher } from "@/components/lang/LanguageSwitcher";
import { Button, buttonVariants } from "@/components/ui/button";
import {
  Card,
  CardContent,
  CardDescription,
  CardFooter,
  CardHeader,
  CardTitle,
} from "@/components/ui/card";
import type {
  CreateTicketProxyRequest,
  PagedResponse,
  ProblemDetails,
  TicketCategory,
  TicketDetail,
  TicketMessage,
  TicketSummary,
} from "@/lib/tickets-types";

const inputClassName =
  "h-11 w-full rounded-xl border border-border/60 bg-background/60 px-4 text-sm text-foreground placeholder:text-muted-foreground/70 focus:border-primary/60 focus:outline-none focus:ring-2 focus:ring-primary/20";

const textareaClassName =
  "min-h-[136px] w-full rounded-xl border border-border/60 bg-background/60 px-4 py-3 text-sm text-foreground placeholder:text-muted-foreground/70 focus:border-primary/60 focus:outline-none focus:ring-2 focus:ring-primary/20";

type TicketFilter = "all" | "open" | "waiting" | "resolved";

type CreateFormState = {
  subject: string;
  categoryId: string;
  priority: string;
  message: string;
  actorDisplayName: string;
};

type TicketsPageClientProps = {
  session: {
    isAuthenticated: boolean;
    displayName?: string;
  };
};

const initialCreateFormState: CreateFormState = {
  subject: "",
  categoryId: "",
  priority: "medium",
  message: "",
  actorDisplayName: "",
};

function formatTicketId(id: string) {
  return `TK-${id.replace(/-/g, "").slice(0, 8).toUpperCase()}`;
}

function formatDateTime(value: string, locale: string) {
  return new Intl.DateTimeFormat(locale, {
    dateStyle: "medium",
    timeStyle: "short",
  }).format(new Date(value));
}

function formatRelativeMinutes(value: string, locale: string) {
  const diffMs = Date.now() - new Date(value).getTime();
  const diffMinutes = Math.max(1, Math.round(diffMs / 60000));

  if (diffMinutes < 60) {
    return new Intl.RelativeTimeFormat(locale, { numeric: "auto" }).format(-diffMinutes, "minute");
  }

  const diffHours = Math.max(1, Math.round(diffMinutes / 60));
  if (diffHours < 24) {
    return new Intl.RelativeTimeFormat(locale, { numeric: "auto" }).format(-diffHours, "hour");
  }

  const diffDays = Math.max(1, Math.round(diffHours / 24));
  return new Intl.RelativeTimeFormat(locale, { numeric: "auto" }).format(-diffDays, "day");
}

async function readResponseError(response: Response) {
  const text = await response.text();

  if (!text) {
    return `HTTP ${response.status}`;
  }

  try {
    const problem = JSON.parse(text) as ProblemDetails;
    return problem.detail || problem.title || `HTTP ${response.status}`;
  } catch {
    return text;
  }
}

export default function TicketsPageClient({ session }: TicketsPageClientProps) {
  const locale = useLocale();
  const homeT = useTranslations("Home");
  const t = useTranslations("Tickets");

  const [categories, setCategories] = useState<TicketCategory[]>([]);
  const [tickets, setTickets] = useState<TicketSummary[]>([]);
  const [selectedTicketId, setSelectedTicketId] = useState<string | null>(null);
  const [selectedTicket, setSelectedTicket] = useState<TicketDetail | null>(null);
  const [activeFilter, setActiveFilter] = useState<TicketFilter>("all");
  const [createForm, setCreateForm] = useState<CreateFormState>(initialCreateFormState);
  const [replyBody, setReplyBody] = useState("");
  const [loadingTickets, setLoadingTickets] = useState(true);
  const [loadingCategories, setLoadingCategories] = useState(true);
  const [loadingDetail, setLoadingDetail] = useState(false);
  const [submittingTicket, setSubmittingTicket] = useState(false);
  const [submittingReply, setSubmittingReply] = useState(false);
  const [ticketsError, setTicketsError] = useState<string | null>(null);
  const [categoriesError, setCategoriesError] = useState<string | null>(null);
  const [detailError, setDetailError] = useState<string | null>(null);
  const [formError, setFormError] = useState<string | null>(null);
  const [replyError, setReplyError] = useState<string | null>(null);
  const [successMessage, setSuccessMessage] = useState<string | null>(null);

  const priorityOptions = useMemo(
    () => [
      { value: "low", label: t("form.priorities.low") },
      { value: "medium", label: t("form.priorities.normal") },
      { value: "high", label: t("form.priorities.high") },
      { value: "urgent", label: t("form.priorities.urgent") },
    ],
    [t],
  );

  const filterOptions = useMemo(
    () => [
      { value: "all" as const, label: t("queue.filters.all") },
      { value: "open" as const, label: t("queue.filters.open") },
      { value: "waiting" as const, label: t("queue.filters.waiting") },
      { value: "resolved" as const, label: t("queue.filters.resolved") },
    ],
    [t],
  );

  const getStatusLabel = useCallback(
    (status: string) => {
      switch (status) {
        case "open":
          return t("statuses.open");
        case "in_progress":
          return t("statuses.inProgress");
        case "waiting_user":
          return t("statuses.waitingPlayer");
        case "resolved":
          return t("statuses.resolved");
        case "closed":
          return t("statuses.closed");
        default:
          return status;
      }
    },
    [t],
  );

  const getPriorityLabel = useCallback(
    (priority: string) => {
      switch (priority) {
        case "low":
          return t("priorities.low");
        case "medium":
          return t("priorities.medium");
        case "high":
          return t("priorities.high");
        case "urgent":
          return t("priorities.urgent");
        default:
          return priority;
      }
    },
    [t],
  );

  const getStatusClassName = useCallback((status: string) => {
    switch (status) {
      case "open":
        return "border-sky-500/30 bg-sky-500/10 text-sky-300";
      case "in_progress":
        return "border-primary/30 bg-primary/10 text-primary";
      case "waiting_user":
        return "border-amber-500/30 bg-amber-500/10 text-amber-300";
      case "resolved":
      case "closed":
        return "border-emerald-500/30 bg-emerald-500/10 text-emerald-300";
      default:
        return "border-border/50 bg-background/60 text-muted-foreground";
    }
  }, []);

  const getPriorityClassName = useCallback((priority: string) => {
    switch (priority) {
      case "urgent":
        return "border-fuchsia-500/30 bg-fuchsia-500/10 text-fuchsia-300";
      case "high":
        return "border-rose-500/30 bg-rose-500/10 text-rose-300";
      case "medium":
        return "border-sky-500/30 bg-sky-500/10 text-sky-300";
      case "low":
        return "border-zinc-500/30 bg-zinc-500/10 text-zinc-300";
      default:
        return "border-border/50 bg-background/60 text-muted-foreground";
    }
  }, []);

  const filteredTickets = useMemo(() => {
    return tickets.filter((ticket) => {
      switch (activeFilter) {
        case "open":
          return ticket.status === "open" || ticket.status === "in_progress";
        case "waiting":
          return ticket.status === "waiting_user";
        case "resolved":
          return ticket.status === "resolved" || ticket.status === "closed";
        default:
          return true;
      }
    });
  }, [activeFilter, tickets]);

  const openTicketsCount = useMemo(
    () => tickets.filter((ticket) => !["resolved", "closed"].includes(ticket.status)).length,
    [tickets],
  );

  const resolvedTicketsCount = useMemo(
    () => tickets.filter((ticket) => ["resolved", "closed"].includes(ticket.status)).length,
    [tickets],
  );

  const loadCategories = useCallback(async () => {
    setLoadingCategories(true);
    setCategoriesError(null);

    try {
      const response = await fetch("/api/tickets/categories", {
        cache: "no-store",
      });

      if (!response.ok) {
        throw new Error(await readResponseError(response));
      }

      const items = (await response.json()) as TicketCategory[];
      const activeItems = items.filter((item) => item.isActive);
      setCategories(activeItems);
      setCreateForm((current) => {
        if (current.categoryId || activeItems.length === 0) {
          return current;
        }

        return {
          ...current,
          categoryId: activeItems[0].id,
        };
      });
    } catch (error) {
      setCategories([]);
      setCategoriesError(error instanceof Error ? error.message : t("errors.categories"));
    } finally {
      setLoadingCategories(false);
    }
  }, [t]);

  const loadTickets = useCallback(
    async (preferredTicketId?: string | null) => {
      setLoadingTickets(true);
      setTicketsError(null);

      try {
        const response = await fetch("/api/tickets?page=1&pageSize=50", {
          cache: "no-store",
        });

        if (!response.ok) {
          throw new Error(await readResponseError(response));
        }

        const payload = (await response.json()) as PagedResponse<TicketSummary>;
        const items = payload.items || [];
        setTickets(items);
        setSelectedTicketId((currentSelected) => {
          if (preferredTicketId && items.some((item) => item.id === preferredTicketId)) {
            return preferredTicketId;
          }

          if (currentSelected && items.some((item) => item.id === currentSelected)) {
            return currentSelected;
          }

          return items[0]?.id ?? null;
        });
      } catch (error) {
        setTickets([]);
        setSelectedTicketId(null);
        setSelectedTicket(null);
        setTicketsError(error instanceof Error ? error.message : t("errors.tickets"));
      } finally {
        setLoadingTickets(false);
      }
    },
    [t],
  );

  const loadTicketDetail = useCallback(
    async (ticketId: string) => {
      setLoadingDetail(true);
      setDetailError(null);

      try {
        const response = await fetch(`/api/tickets/${ticketId}`, {
          cache: "no-store",
        });

        if (!response.ok) {
          throw new Error(await readResponseError(response));
        }

        const detail = (await response.json()) as TicketDetail;
        setSelectedTicket(detail);
      } catch (error) {
        setSelectedTicket(null);
        setDetailError(error instanceof Error ? error.message : t("errors.detail"));
      } finally {
        setLoadingDetail(false);
      }
    },
    [t],
  );

  useEffect(() => {
    queueMicrotask(() => {
      void loadCategories();
      void loadTickets();
    });
  }, [loadCategories, loadTickets]);

  useEffect(() => {
    if (!selectedTicketId) {
      queueMicrotask(() => {
        setSelectedTicket(null);
        setDetailError(null);
      });
      return;
    }

    queueMicrotask(() => {
      void loadTicketDetail(selectedTicketId);
    });
  }, [loadTicketDetail, selectedTicketId]);

  const handleCreateTicket = useCallback(
    async (event: React.FormEvent<HTMLFormElement>) => {
      event.preventDefault();
      setFormError(null);
      setSuccessMessage(null);
      setSubmittingTicket(true);

      try {
        const payload: CreateTicketProxyRequest = {
          subject: createForm.subject,
          categoryId: createForm.categoryId,
          priority: createForm.priority,
          message: createForm.message,
          actorDisplayName: createForm.actorDisplayName,
        };

        const response = await fetch("/api/tickets", {
          method: "POST",
          headers: {
            "Content-Type": "application/json",
          },
          body: JSON.stringify(payload),
        });

        if (!response.ok) {
          throw new Error(await readResponseError(response));
        }

        const created = (await response.json()) as TicketDetail;
        setCreateForm((current) => ({
          ...initialCreateFormState,
          actorDisplayName: current.actorDisplayName,
          categoryId: current.categoryId || categories[0]?.id || "",
        }));
        setSelectedTicketId(created.id);
        setSelectedTicket(created);
        setReplyBody("");
        setSuccessMessage(t("form.success"));
        await loadTickets(created.id);
      } catch (error) {
        setFormError(error instanceof Error ? error.message : t("errors.create"));
      } finally {
        setSubmittingTicket(false);
      }
    },
    [categories, createForm, loadTickets, t],
  );

  const handleReply = useCallback(
    async (event: React.FormEvent<HTMLFormElement>) => {
      event.preventDefault();
      if (!selectedTicketId) {
        return;
      }

      setReplyError(null);
      setSuccessMessage(null);
      setSubmittingReply(true);

      try {
        const response = await fetch(`/api/tickets/${selectedTicketId}/messages`, {
          method: "POST",
          headers: {
            "Content-Type": "application/json",
          },
          body: JSON.stringify({ body: replyBody }),
        });

        if (!response.ok) {
          throw new Error(await readResponseError(response));
        }

        setReplyBody("");
        setSuccessMessage(t("composer.success"));
        await loadTicketDetail(selectedTicketId);
        await loadTickets(selectedTicketId);
      } catch (error) {
        setReplyError(error instanceof Error ? error.message : t("errors.reply"));
      } finally {
        setSubmittingReply(false);
      }
    },
    [loadTicketDetail, loadTickets, replyBody, selectedTicketId, t],
  );

  const handleRefresh = useCallback(async () => {
    setSuccessMessage(null);
    await Promise.all([loadCategories(), loadTickets(selectedTicketId)]);
  }, [loadCategories, loadTickets, selectedTicketId]);

  return (
    <div className="relative z-10 flex min-h-screen flex-col">
      <header className="sticky top-0 z-50 flex h-20 items-center justify-between border-b border-border/40 bg-background/50 px-6 backdrop-blur-md">
        <div className="flex items-center gap-2">
          <div className="flex h-8 w-8 items-center justify-center rounded-lg bg-primary font-bold text-primary-foreground">
            EP
          </div>
          <span className="text-lg font-semibold tracking-tight">ExiledProject</span>
        </div>
        <nav className="hidden gap-6 text-sm font-medium text-muted-foreground md:flex">
          <Link href="/" className="transition-colors hover:text-foreground">
            {homeT("home")}
          </Link>
          <Link href="/tickets" className="text-foreground transition-colors hover:text-foreground">
            {homeT("support")}
          </Link>
          <Link href="/swagger" className="transition-colors hover:text-foreground">
            Swagger
          </Link>
          {session.isAuthenticated ? (
            <Link href="/account" className="transition-colors hover:text-foreground">
              Кабинет
            </Link>
          ) : null}
        </nav>
        <div className="flex items-center gap-4">
          <LanguageSwitcher />
          {session.isAuthenticated ? (
            <Link href="/account" className={buttonVariants({ variant: "ghost" })}>
              {session.displayName || "Кабинет"}
            </Link>
          ) : (
            <>
              <Link href="/auth/login" className={buttonVariants({ variant: "ghost" })}>
                {homeT("login")}
              </Link>
              <Link href="/auth/register" className={buttonVariants({ variant: "default" })}>
                Регистрация
              </Link>
            </>
          )}
        </div>
      </header>

      <main className="flex-1 px-6 py-8 md:px-8">
        <div className="mx-auto flex w-full max-w-7xl flex-col gap-6">
          <section className="grid gap-6 xl:grid-cols-[1.25fr_0.75fr]">
            <Card className="border-border/40 bg-card/50 backdrop-blur-sm">
              <CardHeader className="gap-4">
                <div className="inline-flex w-fit items-center gap-2 rounded-full border border-primary/30 bg-primary/10 px-3 py-1 text-sm font-medium text-primary">
                  <LifeBuoy className="h-4 w-4" />
                  {t("badge")}
                </div>
                <div className="space-y-3">
                  <CardTitle className="text-3xl md:text-5xl">{t("title")}</CardTitle>
                  <CardDescription className="max-w-2xl text-base leading-7 text-muted-foreground">
                    {t("description")}
                  </CardDescription>
                </div>
              </CardHeader>
              <CardContent className="space-y-5">
                <div className="flex flex-col gap-3 sm:flex-row">
                  <Button
                    size="lg"
                    className="h-12 px-6 text-base sm:w-auto"
                    onClick={() => document.getElementById("ticket-subject")?.focus()}
                  >
                    <Ticket className="h-4 w-4" />
                    {t("primaryAction")}
                  </Button>
                  <Button size="lg" variant="outline" className="h-12 px-6 text-base sm:w-auto" onClick={handleRefresh}>
                    <RefreshCw className="h-4 w-4" />
                    {t("secondaryAction")}
                  </Button>
                </div>
                {successMessage ? (
                  <div className="rounded-2xl border border-emerald-500/30 bg-emerald-500/10 px-4 py-3 text-sm text-emerald-200">
                    {successMessage}
                  </div>
                ) : null}
                <div className="grid gap-3 sm:grid-cols-2 xl:grid-cols-3">
                  <div className="rounded-2xl border border-border/40 bg-background/40 p-4">
                    <div className="flex items-center gap-2 text-muted-foreground">
                      <Ticket className="h-4 w-4" />
                      <span className="text-sm">{t("stats.open.label")}</span>
                    </div>
                    <div className="mt-3 text-3xl font-semibold tracking-tight">{loadingTickets ? "…" : openTicketsCount}</div>
                  </div>
                  <div className="rounded-2xl border border-border/40 bg-background/40 p-4">
                    <div className="flex items-center gap-2 text-muted-foreground">
                      <Clock3 className="h-4 w-4" />
                      <span className="text-sm">{t("stats.reply.label")}</span>
                    </div>
                    <div className="mt-3 text-3xl font-semibold tracking-tight">{t("stats.reply.value")}</div>
                  </div>
                  <div className="rounded-2xl border border-border/40 bg-background/40 p-4 sm:col-span-2 xl:col-span-1">
                    <div className="flex items-center gap-2 text-muted-foreground">
                      <BadgeCheck className="h-4 w-4" />
                      <span className="text-sm">{t("stats.resolved.label")}</span>
                    </div>
                    <div className="mt-3 text-3xl font-semibold tracking-tight">{loadingTickets ? "…" : resolvedTicketsCount}</div>
                  </div>
                </div>
              </CardContent>
            </Card>

            <div className="grid gap-4 sm:grid-cols-3 xl:grid-cols-1">
              <Card className="border-border/40 bg-card/50 backdrop-blur-sm">
                <CardHeader>
                  <CardTitle className="flex items-center gap-2 text-base">
                    <Sparkles className="h-4 w-4 text-primary" />
                    {t("highlights.fastReplies.title")}
                  </CardTitle>
                  <CardDescription>{t("highlights.fastReplies.description")}</CardDescription>
                </CardHeader>
              </Card>
              <Card className="border-border/40 bg-card/50 backdrop-blur-sm">
                <CardHeader>
                  <CardTitle className="flex items-center gap-2 text-base">
                    <ShieldAlert className="h-4 w-4 text-primary" />
                    {t("highlights.appeals.title")}
                  </CardTitle>
                  <CardDescription>{t("highlights.appeals.description")}</CardDescription>
                </CardHeader>
              </Card>
              <Card className="border-border/40 bg-card/50 backdrop-blur-sm">
                <CardHeader>
                  <CardTitle className="flex items-center gap-2 text-base">
                    <ArrowUpRight className="h-4 w-4 text-primary" />
                    {t("highlights.tracking.title")}
                  </CardTitle>
                  <CardDescription>{t("highlights.tracking.description")}</CardDescription>
                </CardHeader>
              </Card>
            </div>
          </section>

          <section className="grid gap-6 xl:grid-cols-[1.15fr_0.85fr]">
            <Card className="border-border/40 bg-card/50 backdrop-blur-sm">
              <CardHeader className="gap-4 border-b border-border/40">
                <div className="flex flex-col gap-3 lg:flex-row lg:items-center lg:justify-between">
                  <div>
                    <CardTitle>{t("queue.title")}</CardTitle>
                    <CardDescription>{t("queue.description")}</CardDescription>
                  </div>
                  <div className="flex flex-wrap gap-2 text-xs">
                    {filterOptions.map((filter) => (
                      <button
                        key={filter.value}
                        type="button"
                        onClick={() => setActiveFilter(filter.value)}
                        className={`inline-flex items-center gap-1 rounded-full border px-3 py-1.5 transition-colors ${
                          activeFilter === filter.value
                            ? "border-primary/30 bg-primary/10 text-primary"
                            : "border-border/50 bg-background/50 text-muted-foreground hover:text-foreground"
                        }`}
                      >
                        {filter.value === "all" ? <Filter className="h-3.5 w-3.5" /> : null}
                        {filter.label}
                      </button>
                    ))}
                  </div>
                </div>
                {ticketsError ? (
                  <div className="rounded-2xl border border-destructive/30 bg-destructive/10 px-4 py-3 text-sm text-destructive">
                    {ticketsError}
                  </div>
                ) : null}
              </CardHeader>
              <CardContent className="space-y-4 pt-6">
                {loadingTickets ? (
                  <div className="flex items-center gap-2 rounded-2xl border border-border/40 bg-background/40 px-4 py-5 text-sm text-muted-foreground">
                    <LoaderCircle className="h-4 w-4 animate-spin" />
                    {t("queue.loading")}
                  </div>
                ) : null}

                {!loadingTickets && filteredTickets.length === 0 ? (
                  <div className="rounded-2xl border border-dashed border-border/50 bg-background/30 p-6 text-sm text-muted-foreground">
                    <p className="font-medium text-foreground">{t("queue.emptyTitle")}</p>
                    <p className="mt-2 leading-6">{t("queue.emptyDescription")}</p>
                  </div>
                ) : null}

                {!loadingTickets
                  ? filteredTickets.map((ticketItem) => {
                      const metaParts = [
                        ticketItem.category.name,
                        t("queue.updated", { value: formatRelativeMinutes(ticketItem.updatedAtUtc, locale) }),
                      ];

                      if (ticketItem.assignedTo?.displayName) {
                        metaParts.push(t("queue.assigned", { value: ticketItem.assignedTo.displayName }));
                      }

                      return (
                        <button
                          key={ticketItem.id}
                          type="button"
                          onClick={() => setSelectedTicketId(ticketItem.id)}
                          className={`w-full rounded-2xl border bg-background/40 p-4 text-left transition-colors hover:border-primary/30 hover:bg-background/60 ${
                            selectedTicketId === ticketItem.id ? "border-primary/40" : "border-border/40"
                          }`}
                        >
                          <div className="flex flex-col gap-3 md:flex-row md:items-start md:justify-between">
                            <div className="space-y-2">
                              <div className="flex flex-wrap items-center gap-2">
                                <span className="rounded-full border border-border/50 bg-background/60 px-2.5 py-1 text-xs font-medium text-muted-foreground">
                                  {formatTicketId(ticketItem.id)}
                                </span>
                                <span className={`rounded-full border px-2.5 py-1 text-xs font-medium ${getStatusClassName(ticketItem.status)}`}>
                                  {getStatusLabel(ticketItem.status)}
                                </span>
                                <span className={`rounded-full border px-2.5 py-1 text-xs font-medium ${getPriorityClassName(ticketItem.priority)}`}>
                                  {getPriorityLabel(ticketItem.priority)}
                                </span>
                              </div>
                              <div>
                                <h3 className="text-base font-semibold">{ticketItem.subject}</h3>
                                <p className="mt-1 text-sm text-muted-foreground">{metaParts.join(" • ")}</p>
                              </div>
                            </div>
                            <div className="inline-flex items-center gap-1 text-sm text-primary">
                              {t("queue.openTicket")}
                              <ArrowUpRight className="h-4 w-4" />
                            </div>
                          </div>
                          <p className="mt-4 text-sm leading-6 text-muted-foreground">
                            {t("queue.messageCount", { count: ticketItem.messageCount })}
                          </p>
                        </button>
                      );
                    })
                  : null}
              </CardContent>
            </Card>

            <Card className="border-border/40 bg-card/50 backdrop-blur-sm">
              <CardHeader>
                <CardTitle>{t("create.title")}</CardTitle>
                <CardDescription>{t("create.description")}</CardDescription>
              </CardHeader>
              <form onSubmit={handleCreateTicket}>
                <CardContent className="space-y-4">
                  {formError ? (
                    <div className="rounded-2xl border border-destructive/30 bg-destructive/10 px-4 py-3 text-sm text-destructive">
                      {formError}
                    </div>
                  ) : null}
                  {categoriesError ? (
                    <div className="rounded-2xl border border-destructive/30 bg-destructive/10 px-4 py-3 text-sm text-destructive">
                      {categoriesError}
                    </div>
                  ) : null}
                  <div className="space-y-2">
                    <label className="text-sm font-medium" htmlFor="ticket-subject">
                      {t("form.subject")}
                    </label>
                    <input
                      id="ticket-subject"
                      className={inputClassName}
                      placeholder={t("form.subjectPlaceholder")}
                      value={createForm.subject}
                      onChange={(event) => setCreateForm((current) => ({ ...current, subject: event.target.value }))}
                    />
                  </div>

                  <div className="grid gap-4 md:grid-cols-2">
                    <div className="space-y-2">
                      <label className="text-sm font-medium" htmlFor="ticket-category">
                        {t("form.category")}
                      </label>
                      <select
                        id="ticket-category"
                        className={inputClassName}
                        value={createForm.categoryId}
                        onChange={(event) => setCreateForm((current) => ({ ...current, categoryId: event.target.value }))}
                        disabled={loadingCategories || categories.length === 0}
                      >
                        {categories.length === 0 ? (
                          <option value="">{loadingCategories ? t("form.categoriesLoading") : t("form.noCategories")}</option>
                        ) : null}
                        {categories.map((category) => (
                          <option key={category.id} value={category.id}>
                            {category.name}
                          </option>
                        ))}
                      </select>
                    </div>

                    <div className="space-y-2">
                      <label className="text-sm font-medium" htmlFor="ticket-priority">
                        {t("form.priority")}
                      </label>
                      <select
                        id="ticket-priority"
                        className={inputClassName}
                        value={createForm.priority}
                        onChange={(event) => setCreateForm((current) => ({ ...current, priority: event.target.value }))}
                      >
                        {priorityOptions.map((priority) => (
                          <option key={priority.value} value={priority.value}>
                            {priority.label}
                          </option>
                        ))}
                      </select>
                    </div>
                  </div>

                  <div className="space-y-2">
                    <label className="text-sm font-medium" htmlFor="ticket-nickname">
                      {t("form.nickname")}
                    </label>
                    <input
                      id="ticket-nickname"
                      className={inputClassName}
                      placeholder={t("form.nicknamePlaceholder")}
                      value={createForm.actorDisplayName}
                      onChange={(event) => setCreateForm((current) => ({ ...current, actorDisplayName: event.target.value }))}
                    />
                  </div>

                  <div className="space-y-2">
                    <label className="text-sm font-medium" htmlFor="ticket-description">
                      {t("form.description")}
                    </label>
                    <textarea
                      id="ticket-description"
                      className={textareaClassName}
                      placeholder={t("form.descriptionPlaceholder")}
                      value={createForm.message}
                      onChange={(event) => setCreateForm((current) => ({ ...current, message: event.target.value }))}
                    />
                  </div>
                </CardContent>
                <CardFooter className="flex flex-col items-stretch gap-3 sm:flex-row sm:justify-between">
                  <Button variant="outline" className="sm:w-auto" type="button" disabled>
                    <Upload className="h-4 w-4" />
                    {t("form.attach")}
                  </Button>
                  <div className="flex flex-col gap-3 sm:flex-row">
                    <Button
                      variant="ghost"
                      type="button"
                      onClick={() => setCreateForm((current) => ({ ...initialCreateFormState, actorDisplayName: current.actorDisplayName, categoryId: categories[0]?.id || "" }))}
                    >
                      {t("form.reset")}
                    </Button>
                    <Button type="submit" disabled={submittingTicket || categories.length === 0}>
                      {submittingTicket ? <LoaderCircle className="h-4 w-4 animate-spin" /> : null}
                      {submittingTicket ? t("form.submitting") : t("form.submit")}
                    </Button>
                  </div>
                </CardFooter>
              </form>
            </Card>
          </section>

          <section className="grid gap-6 xl:grid-cols-[1.15fr_0.85fr]">
            <Card className="border-border/40 bg-card/50 backdrop-blur-sm">
              <CardHeader className="border-b border-border/40">
                <div className="flex flex-col gap-4 lg:flex-row lg:items-start lg:justify-between">
                  <div>
                    <CardTitle>{t("detail.title")}</CardTitle>
                    <CardDescription>{t("detail.description")}</CardDescription>
                  </div>
                  {selectedTicket ? (
                    <span className={`w-fit rounded-full border px-3 py-1 text-xs font-medium ${getStatusClassName(selectedTicket.status)}`}>
                      {getStatusLabel(selectedTicket.status)}
                    </span>
                  ) : null}
                </div>
                {detailError ? (
                  <div className="rounded-2xl border border-destructive/30 bg-destructive/10 px-4 py-3 text-sm text-destructive">
                    {detailError}
                  </div>
                ) : null}
              </CardHeader>
              <CardContent className="space-y-6 pt-6">
                {loadingDetail ? (
                  <div className="flex items-center gap-2 rounded-2xl border border-border/40 bg-background/40 px-4 py-5 text-sm text-muted-foreground">
                    <LoaderCircle className="h-4 w-4 animate-spin" />
                    {t("detail.loading")}
                  </div>
                ) : null}

                {!loadingDetail && !selectedTicket ? (
                  <div className="rounded-2xl border border-dashed border-border/50 bg-background/30 p-6 text-sm text-muted-foreground">
                    <p className="font-medium text-foreground">{t("detail.emptyTitle")}</p>
                    <p className="mt-2 leading-6">{t("detail.emptyDescription")}</p>
                  </div>
                ) : null}

                {!loadingDetail && selectedTicket ? (
                  <>
                    <div className="grid gap-4 md:grid-cols-2">
                      <div className="rounded-2xl border border-border/40 bg-background/40 p-4">
                        <p className="text-xs uppercase tracking-[0.2em] text-muted-foreground">{t("detail.subjectLabel")}</p>
                        <p className="mt-2 text-sm font-medium">{selectedTicket.subject}</p>
                      </div>
                      <div className="rounded-2xl border border-border/40 bg-background/40 p-4">
                        <p className="text-xs uppercase tracking-[0.2em] text-muted-foreground">{t("detail.categoryLabel")}</p>
                        <p className="mt-2 text-sm font-medium">{selectedTicket.category.name}</p>
                      </div>
                      <div className="rounded-2xl border border-border/40 bg-background/40 p-4">
                        <p className="text-xs uppercase tracking-[0.2em] text-muted-foreground">{t("detail.priorityLabel")}</p>
                        <p className="mt-2 text-sm font-medium">{getPriorityLabel(selectedTicket.priority)}</p>
                      </div>
                      <div className="rounded-2xl border border-border/40 bg-background/40 p-4">
                        <p className="text-xs uppercase tracking-[0.2em] text-muted-foreground">{t("detail.assigneeLabel")}</p>
                        <p className="mt-2 text-sm font-medium">{selectedTicket.assignedTo?.displayName || t("detail.unassigned")}</p>
                      </div>
                      <div className="rounded-2xl border border-border/40 bg-background/40 p-4">
                        <p className="text-xs uppercase tracking-[0.2em] text-muted-foreground">{t("detail.authorLabel")}</p>
                        <p className="mt-2 text-sm font-medium">{selectedTicket.createdBy.displayName}</p>
                      </div>
                      <div className="rounded-2xl border border-border/40 bg-background/40 p-4">
                        <p className="text-xs uppercase tracking-[0.2em] text-muted-foreground">{t("detail.lastUpdateLabel")}</p>
                        <p className="mt-2 text-sm font-medium">{formatDateTime(selectedTicket.updatedAtUtc, locale)}</p>
                      </div>
                    </div>

                    <div className="space-y-4">
                      <div>
                        <h3 className="text-base font-semibold">{t("conversation.title")}</h3>
                        <p className="mt-1 text-sm text-muted-foreground">{t("conversation.description")}</p>
                      </div>

                      <div className="space-y-4">
                        {selectedTicket.messages.map((message: TicketMessage) => {
                          const isOwnMessage = message.author.userId === selectedTicket.createdBy.userId && !message.isStaffReply;

                          return (
                            <div key={message.id} className={`flex ${isOwnMessage ? "justify-end" : "justify-start"}`}>
                              <div
                                className={`max-w-[85%] rounded-2xl border p-4 ${
                                  isOwnMessage
                                    ? "border-primary/30 bg-primary/10"
                                    : "border-border/40 bg-background/50"
                                }`}
                              >
                                <div className="flex flex-wrap items-center gap-2 text-xs text-muted-foreground">
                                  <span className="font-medium text-foreground">{message.author.displayName}</span>
                                  {message.author.role ? (
                                    <>
                                      <span>•</span>
                                      <span>{message.author.role}</span>
                                    </>
                                  ) : null}
                                  <span>•</span>
                                  <span>{formatDateTime(message.createdAtUtc, locale)}</span>
                                </div>
                                <p className="mt-3 whitespace-pre-wrap text-sm leading-6 text-foreground">{message.body}</p>
                              </div>
                            </div>
                          );
                        })}
                      </div>

                      {replyError ? (
                        <div className="rounded-2xl border border-destructive/30 bg-destructive/10 px-4 py-3 text-sm text-destructive">
                          {replyError}
                        </div>
                      ) : null}

                      <form className="rounded-2xl border border-dashed border-border/50 bg-background/30 p-4" onSubmit={handleReply}>
                        <div className="space-y-3">
                          <label className="text-sm font-medium" htmlFor="ticket-reply">
                            {t("composer.label")}
                          </label>
                          <textarea
                            id="ticket-reply"
                            className={textareaClassName}
                            placeholder={t("composer.placeholder")}
                            value={replyBody}
                            onChange={(event) => setReplyBody(event.target.value)}
                            disabled={submittingReply || !selectedTicket}
                          />
                        </div>
                        <div className="mt-4 flex flex-col gap-3 sm:flex-row sm:justify-between">
                          <Button variant="outline" type="button" disabled>
                            <Upload className="h-4 w-4" />
                            {t("composer.attach")}
                          </Button>
                          <Button type="submit" disabled={submittingReply || !selectedTicket || !replyBody.trim()}>
                            {submittingReply ? <LoaderCircle className="h-4 w-4 animate-spin" /> : null}
                            {submittingReply ? t("composer.sending") : t("composer.send")}
                          </Button>
                        </div>
                      </form>
                    </div>
                  </>
                ) : null}
              </CardContent>
            </Card>

            <div className="grid gap-6">
              <Card className="border-border/40 bg-card/50 backdrop-blur-sm">
                <CardHeader>
                  <CardTitle>{t("sla.title")}</CardTitle>
                  <CardDescription>{t("sla.description")}</CardDescription>
                </CardHeader>
                <CardContent className="space-y-4">
                  <div className="rounded-2xl border border-border/40 bg-background/40 p-4">
                    <p className="text-sm text-muted-foreground">{t("sla.items.reply.label")}</p>
                    <p className="mt-2 text-xl font-semibold">{t("sla.items.reply.value")}</p>
                  </div>
                  <div className="rounded-2xl border border-border/40 bg-background/40 p-4">
                    <p className="text-sm text-muted-foreground">{t("sla.items.review.label")}</p>
                    <p className="mt-2 text-xl font-semibold">{t("sla.items.review.value")}</p>
                  </div>
                  <div className="rounded-2xl border border-border/40 bg-background/40 p-4">
                    <p className="text-sm text-muted-foreground">{t("sla.items.escalation.label")}</p>
                    <p className="mt-2 text-xl font-semibold">{t("sla.items.escalation.value")}</p>
                  </div>
                </CardContent>
              </Card>

              <Card className="border-border/40 bg-card/50 backdrop-blur-sm">
                <CardHeader>
                  <CardTitle>{t("knowledge.title")}</CardTitle>
                  <CardDescription>{t("knowledge.description")}</CardDescription>
                </CardHeader>
                <CardContent className="space-y-3">
                  <div className="rounded-2xl border border-border/40 bg-background/40 p-4">
                    <div className="flex items-start gap-3">
                      <BadgeCheck className="mt-0.5 h-4 w-4 text-primary" />
                      <div>
                        <h3 className="text-sm font-semibold">{t("knowledge.items.payments.title")}</h3>
                        <p className="mt-1 text-sm leading-6 text-muted-foreground">{t("knowledge.items.payments.description")}</p>
                      </div>
                    </div>
                  </div>
                  <div className="rounded-2xl border border-border/40 bg-background/40 p-4">
                    <div className="flex items-start gap-3">
                      <ShieldAlert className="mt-0.5 h-4 w-4 text-primary" />
                      <div>
                        <h3 className="text-sm font-semibold">{t("knowledge.items.security.title")}</h3>
                        <p className="mt-1 text-sm leading-6 text-muted-foreground">{t("knowledge.items.security.description")}</p>
                      </div>
                    </div>
                  </div>
                  <div className="rounded-2xl border border-border/40 bg-background/40 p-4">
                    <div className="flex items-start gap-3">
                      <Sparkles className="mt-0.5 h-4 w-4 text-primary" />
                      <div>
                        <h3 className="text-sm font-semibold">{t("knowledge.items.rules.title")}</h3>
                        <p className="mt-1 text-sm leading-6 text-muted-foreground">{t("knowledge.items.rules.description")}</p>
                      </div>
                    </div>
                  </div>
                </CardContent>
              </Card>
            </div>
          </section>
        </div>
      </main>

      <footer className="border-t border-border/40 bg-background/50 py-8 text-center text-sm text-muted-foreground backdrop-blur-md">
        <p>{homeT("footer", { year: new Date().getFullYear() })}</p>
      </footer>
    </div>
  );
}
