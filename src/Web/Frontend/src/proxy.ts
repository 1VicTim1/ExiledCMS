import { NextResponse } from "next/server";
import type { NextRequest } from "next/server";

import { authCookieNames } from "@/lib/auth-session";

const locales = ["ru", "en"];
const defaultLocale = "ru";

function resolveLocale(request: NextRequest) {
  const cookieLocale = request.cookies.get("NEXT_LOCALE")?.value;
  if (cookieLocale && locales.includes(cookieLocale)) {
    return cookieLocale;
  }

  const acceptLang = request.headers.get("accept-language") || "";
  if (acceptLang.includes("ru")) {
    return "ru";
  }

  if (acceptLang.includes("en")) {
    return "en";
  }

  return defaultLocale;
}

export function proxy(request: NextRequest) {
  const locale = resolveLocale(request);
  const response = NextResponse.next();
  response.cookies.set("NEXT_LOCALE", locale, {
    path: "/",
    maxAge: 60 * 60 * 24 * 365,
  });

  const isAuthenticated = Boolean(request.cookies.get(authCookieNames.accessToken)?.value);
  const pathname = request.nextUrl.pathname;

  if ((pathname.startsWith("/account") || pathname.startsWith("/admin")) && !isAuthenticated) {
    const loginUrl = new URL("/auth/login", request.url);
    loginUrl.searchParams.set("next", pathname);
    return NextResponse.redirect(loginUrl);
  }

  if (isAuthenticated && (pathname === "/auth/login" || pathname === "/auth/register")) {
    return NextResponse.redirect(new URL("/account", request.url));
  }

  return response;
}

export const config = {
  matcher: ["/((?!api|_next/static|_next/image|favicon.ico|robots.txt|grid.svg|.*\\..*).*)"],
};
