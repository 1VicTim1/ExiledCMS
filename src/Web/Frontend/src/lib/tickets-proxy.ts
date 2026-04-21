import "server-only";

import { NextRequest, NextResponse } from "next/server";

import { readSessionFromRequest } from "@/lib/auth-session";

const devActorDefaults = {
  userId: process.env.EXILEDCMS_DEV_USER_ID?.trim() || "11111111-1111-1111-1111-111111111111",
  displayName: process.env.EXILEDCMS_DEV_USER_NAME?.trim() || "Demo Player",
  role: process.env.EXILEDCMS_DEV_USER_ROLE?.trim() || "user",
  permissions: process.env.EXILEDCMS_DEV_USER_PERMISSIONS?.trim() || "ticket.create ticket.read.own ticket.reply.own",
};

class ActorResolutionError extends Error {
  constructor(message: string) {
    super(message);
    this.name = "ActorResolutionError";
  }
}

function getTicketsServiceBaseUrl(): string {
  const configured = process.env.EXILEDCMS_TICKETS_SERVICE_URL?.trim() || process.env.TICKETS_SERVICE_URL?.trim();
  if (configured) {
    return configured.replace(/\/+$/, "");
  }

  return "http://localhost:8080";
}

function shouldAllowDevActor(): boolean {
  const configured = process.env.EXILEDCMS_ALLOW_DEV_ACTOR?.trim().toLowerCase();
  if (configured === "true") {
    return true;
  }

  if (configured === "false") {
    return false;
  }

  return false;
}

function resolveActor(request: NextRequest, displayNameOverride?: string) {
  const session = readSessionFromRequest(request);
  const allowDevActor = shouldAllowDevActor();
  const userId = session.userId;
  const displayName = displayNameOverride?.trim() || session.displayName?.trim();
  const role = session.role?.trim();
  const permissions = session.permissions.join(" ");

  if (!userId && !allowDevActor) {
    throw new ActorResolutionError("Authentication is required before calling tickets-service.");
  }

  return {
    userId: userId || devActorDefaults.userId,
    displayName: displayName || devActorDefaults.displayName,
    role: role || devActorDefaults.role,
    permissions: permissions || devActorDefaults.permissions,
    emailVerified: session.emailVerified,
  };
}

function createProblemResponse(status: number, title: string, detail: string) {
  return NextResponse.json(
    {
      status,
      title,
      detail,
    },
    {
      status,
      headers: {
        "Cache-Control": "no-store",
      },
    },
  );
}

async function toProxyResponse(upstream: Response, actorDisplayName?: string) {
  const body = await upstream.text();
  const headers = new Headers();
  const contentType = upstream.headers.get("content-type");
  const location = upstream.headers.get("location");

  if (contentType) {
    headers.set("Content-Type", contentType);
  }

  if (location) {
    headers.set("Location", location);
  }

  headers.set("Cache-Control", "no-store");

  const response = new NextResponse(body || null, {
    status: upstream.status,
    headers,
  });

  if (actorDisplayName?.trim()) {
    response.cookies.set("exiledcms_user_name", actorDisplayName.trim(), {
      path: "/",
      maxAge: 60 * 60 * 24 * 365,
      sameSite: "lax",
      httpOnly: true,
    });
  }

  return response;
}

export async function proxyTicketsServiceRequest(
  request: NextRequest,
  path: string,
  options?: {
    method?: string;
    body?: unknown;
    actorDisplayName?: string;
    forwardQuery?: boolean;
  },
) {
  let actor: ReturnType<typeof resolveActor>;

  try {
    actor = resolveActor(request, options?.actorDisplayName);
  } catch (error) {
    if (error instanceof ActorResolutionError) {
      return createProblemResponse(401, "Authentication required", error.message);
    }

    return createProblemResponse(500, "Actor resolution failed", "The current request actor could not be resolved.");
  }

  const url = new URL(path, `${getTicketsServiceBaseUrl()}/`);
  if (options?.forwardQuery) {
    url.search = request.nextUrl.search;
  }

  const method = options?.method || request.method;

  if (method === "POST" && !actor.emailVerified) {
    return createProblemResponse(
      403,
      "Email verification required",
      "Only users with a verified email address can create tickets.",
    );
  }

  const headers = new Headers({
    Accept: "application/json",
    "X-User-Id": actor.userId,
    "X-User-Name": actor.displayName,
    "X-User-Role": actor.role,
    "X-User-Permissions": actor.permissions,
    "X-Correlation-Id": request.headers.get("X-Correlation-Id")?.trim() || crypto.randomUUID(),
  });

  let body: string | undefined;

  if (typeof options?.body !== "undefined") {
    headers.set("Content-Type", "application/json");
    body = JSON.stringify(options.body);
  }

  try {
    const upstream = await fetch(url, {
      method,
      headers,
      body,
      cache: "no-store",
    });

    return await toProxyResponse(upstream, options?.actorDisplayName);
  } catch {
    return createProblemResponse(
      503,
      "Tickets service unavailable",
      "The frontend could not reach tickets-service. Verify that the service is running and its base URL is configured correctly.",
    );
  }
}

export function parseOptionalDisplayName(value: unknown): string | undefined {
  if (typeof value !== "string") {
    return undefined;
  }

  const normalized = value.trim();
  return normalized ? normalized : undefined;
}

export function invalidJsonResponse() {
  return createProblemResponse(400, "Invalid JSON", "The submitted request body is not valid JSON.");
}
