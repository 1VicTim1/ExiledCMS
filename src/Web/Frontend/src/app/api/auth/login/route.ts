import { NextRequest } from "next/server";

import { jsonProblem, loginAndPersistSession } from "@/lib/auth-proxy";

export const dynamic = "force-dynamic";

export async function POST(request: NextRequest) {
  let payload: { email?: string; password?: string; totpCode?: string };

  try {
    payload = (await request.json()) as { email?: string; password?: string; totpCode?: string };
  } catch {
    return jsonProblem({
      status: 400,
      title: "Invalid JSON",
      detail: "The submitted request body is not valid JSON.",
    }, 400);
  }

  try {
    const result = await loginAndPersistSession({
      email: payload.email?.trim() ?? "",
      password: payload.password ?? "",
      totpCode: payload.totpCode?.trim(),
    });

    return result.response;
  } catch (error) {
    if (error instanceof Response) {
      return error;
    }

    return jsonProblem({
      status: 503,
      title: "Auth service unavailable",
      detail: "The frontend could not reach auth-service.",
    }, 503);
  }
}
