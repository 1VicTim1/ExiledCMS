import { NextRequest } from "next/server";

import { authJsonRequest, jsonProblem, loginAndPersistSession, readProblem } from "@/lib/auth-proxy";

export const dynamic = "force-dynamic";

export async function POST(request: NextRequest) {
  let payload: { email?: string; displayName?: string; password?: string };

  try {
    payload = (await request.json()) as { email?: string; displayName?: string; password?: string };
  } catch {
    return jsonProblem({
      status: 400,
      title: "Invalid JSON",
      detail: "The submitted request body is not valid JSON.",
    }, 400);
  }

  const registerResponse = await authJsonRequest("/api/v1/auth/register", {
    method: "POST",
    headers: {
      "Content-Type": "application/json",
    },
    body: JSON.stringify({
      email: payload.email?.trim() ?? "",
      displayName: payload.displayName?.trim() ?? "",
      password: payload.password ?? "",
    }),
  });

  if (!registerResponse.ok) {
    return jsonProblem(await readProblem(registerResponse), registerResponse.status);
  }

  const login = await loginAndPersistSession(request, {
    email: payload.email?.trim() ?? "",
    password: payload.password ?? "",
  });

  return login.response;
}
