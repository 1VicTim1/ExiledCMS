import "server-only";

import { NextRequest, NextResponse } from "next/server";

export const authCookieNames = {
  accessToken: "exiledcms_access_token",
  userId: "exiledcms_user_id",
  displayName: "exiledcms_user_name",
  role: "exiledcms_user_role",
  permissions: "exiledcms_user_permissions",
  email: "exiledcms_user_email",
  emailVerified: "exiledcms_user_email_verified",
  totpEnabled: "exiledcms_user_totp_enabled",
} as const;

export type AuthUserProfile = {
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

export type AuthLoginResponse = {
  accessToken: string;
  expiresAtUtc: string;
  user: AuthUserProfile;
};

export type AuthSessionSnapshot = {
  accessToken?: string;
  userId?: string;
  displayName?: string;
  role?: string;
  permissions: string[];
  email?: string;
  emailVerified: boolean;
  totpEnabled: boolean;
  isAuthenticated: boolean;
};

export function readSessionFromRequest(request: NextRequest): AuthSessionSnapshot {
  return readSessionFromCookieSource((name) => request.cookies.get(name)?.value);
}

export function readSessionFromCookieSource(getCookieValue: (name: string) => string | undefined): AuthSessionSnapshot {
  const permissions = splitPermissions(getCookieValue(authCookieNames.permissions));
  const accessToken = getCookieValue(authCookieNames.accessToken)?.trim();
  const userId = getCookieValue(authCookieNames.userId)?.trim();

  return {
    accessToken,
    userId,
    displayName: getCookieValue(authCookieNames.displayName)?.trim(),
    role: getCookieValue(authCookieNames.role)?.trim(),
    permissions,
    email: getCookieValue(authCookieNames.email)?.trim(),
    emailVerified: parseBooleanCookie(getCookieValue(authCookieNames.emailVerified)),
    totpEnabled: parseBooleanCookie(getCookieValue(authCookieNames.totpEnabled)),
    isAuthenticated: Boolean(accessToken && userId),
  };
}

export function applyAuthSession(response: NextResponse, login: AuthLoginResponse) {
  const expires = new Date(login.expiresAtUtc);
  const user = login.user;

  response.cookies.set(authCookieNames.accessToken, login.accessToken, {
    httpOnly: true,
    sameSite: "lax",
    secure: process.env.NODE_ENV === "production",
    path: "/",
    expires,
  });

  applyProfileCookies(response, user, expires);
  return response;
}

export function applyProfileCookies(response: NextResponse, user: AuthUserProfile, expires?: Date) {
  const primaryRole = user.roles.find((role) => role.toLowerCase() === "admin") || user.roles[0] || "user";
  const cookieOptions = {
    httpOnly: true,
    sameSite: "lax" as const,
    secure: process.env.NODE_ENV === "production",
    path: "/",
    expires,
  };

  response.cookies.set(authCookieNames.userId, user.id, cookieOptions);
  response.cookies.set(authCookieNames.displayName, user.displayName, cookieOptions);
  response.cookies.set(authCookieNames.role, primaryRole, cookieOptions);
  response.cookies.set(authCookieNames.permissions, user.permissions.join(" "), cookieOptions);
  response.cookies.set(authCookieNames.email, user.email, cookieOptions);
  response.cookies.set(authCookieNames.emailVerified, String(user.emailVerified), cookieOptions);
  response.cookies.set(authCookieNames.totpEnabled, String(user.totpEnabled), cookieOptions);
}

export function clearAuthSession(response: NextResponse) {
  for (const cookieName of Object.values(authCookieNames)) {
    response.cookies.set(cookieName, "", {
      httpOnly: true,
      sameSite: "lax",
      secure: process.env.NODE_ENV === "production",
      path: "/",
      expires: new Date(0),
    });
  }

  return response;
}

export function splitPermissions(rawValue?: string) {
  return (rawValue ?? "")
    .split(/[ ,;]+/)
    .map((value) => value.trim())
    .filter(Boolean);
}

export function parseBooleanCookie(value?: string) {
  return value?.trim().toLowerCase() === "true";
}
