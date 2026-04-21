import "server-only";

import { NextRequest, NextResponse } from "next/server";

import {
  applyAuthSession,
  applyProfileCookies,
  clearAuthSession,
  readSessionFromRequest,
  type AuthLoginResponse,
  type AuthUserProfile,
} from "@/lib/auth-session";

type ProblemDetails = {
  status?: number;
  title?: string;
  detail?: string;
  code?: string;
};

export function getAuthServiceBaseUrl() {
  const configured = process.env.EXILEDCMS_AUTH_SERVICE_URL?.trim() || process.env.AUTH_SERVICE_URL?.trim();
  if (configured) {
    return configured.replace(/\/+$/, "");
  }

  return "http://localhost:8081";
}

export async function readProblem(response: Response) {
  const body = await response.text();
  if (!body) {
    return {
      status: response.status,
      title: "Request failed",
      detail: `HTTP ${response.status}`,
    } satisfies ProblemDetails;
  }

  try {
    return JSON.parse(body) as ProblemDetails;
  } catch {
    return {
      status: response.status,
      title: "Request failed",
      detail: body,
    } satisfies ProblemDetails;
  }
}

export function jsonProblem(problem: ProblemDetails, fallbackStatus = 500) {
  const status = problem.status ?? fallbackStatus;
  return NextResponse.json(
    {
      status,
      title: problem.title ?? "Request failed",
      detail: problem.detail ?? "The request could not be completed.",
      code: problem.code,
    },
    {
      status,
      headers: {
        "Cache-Control": "no-store",
      },
    },
  );
}

export async function authJsonRequest(path: string, init?: RequestInit) {
  return fetch(new URL(path, `${getAuthServiceBaseUrl()}/`), {
    ...init,
    headers: {
      Accept: "application/json",
      ...(init?.headers ?? {}),
    },
    cache: "no-store",
  });
}

export function getAuthorizationHeader(request: NextRequest) {
  const session = readSessionFromRequest(request);
  if (!session.accessToken) {
    return null;
  }

  return `Bearer ${session.accessToken}`;
}

export async function proxyAuthenticatedJson(
  request: NextRequest,
  path: string,
  init?: RequestInit,
) {
  const authorization = getAuthorizationHeader(request);
  if (!authorization) {
    return jsonProblem({
      status: 401,
      title: "Authentication required",
      detail: "You need to sign in before accessing this resource.",
    }, 401);
  }

  try {
    const upstream = await authJsonRequest(path, {
      ...init,
      headers: {
        Authorization: authorization,
        ...(init?.headers ?? {}),
      },
    });

    const body = await upstream.text();
    const response = new NextResponse(body || null, {
      status: upstream.status,
      headers: {
        "Cache-Control": "no-store",
        "Content-Type": upstream.headers.get("content-type") ?? "application/json; charset=utf-8",
      },
    });

    if (upstream.ok) {
      try {
        const json = JSON.parse(body) as AuthUserProfile;
        applyProfileCookies(response, json, undefined, { request });
      } catch {
        // Ignore non-profile responses.
      }
    }

    return response;
  } catch {
    return jsonProblem({
      status: 503,
      title: "Auth service unavailable",
      detail: "The frontend could not reach auth-service.",
    }, 503);
  }
}

export async function loginAndPersistSession(
  request: NextRequest,
  payload: { email: string; password: string; totpCode?: string },
) {
  const upstream = await authJsonRequest("/api/v1/auth/login", {
    method: "POST",
    headers: {
      "Content-Type": "application/json",
    },
    body: JSON.stringify(payload),
  });

  if (!upstream.ok) {
    return {
      ok: false as const,
      response: jsonProblem(await readProblem(upstream), upstream.status),
    };
  }

  const login = (await upstream.json()) as AuthLoginResponse;
  const response = NextResponse.json(login.user, {
    headers: {
      "Cache-Control": "no-store",
    },
  });

  applyAuthSession(response, login, { request });
  return {
    ok: true as const,
    response,
    login,
  };
}

export function logoutResponse(request: NextRequest) {
  return clearAuthSession(
    NextResponse.json(
      { success: true },
      {
        headers: {
          "Cache-Control": "no-store",
        },
      },
    ),
    { request },
  );
}
