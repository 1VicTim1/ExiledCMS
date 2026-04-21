import { NextRequest } from "next/server";

import { jsonProblem, proxyAuthenticatedJson } from "@/lib/auth-proxy";

export const dynamic = "force-dynamic";

export async function POST(request: NextRequest) {
  let payload: unknown;

  try {
    payload = await request.json();
  } catch {
    return jsonProblem({
      status: 400,
      title: "Invalid JSON",
      detail: "The submitted request body is not valid JSON.",
    }, 400);
  }

  return proxyAuthenticatedJson(request, "/api/v1/auth/2fa/enable", {
    method: "POST",
    headers: {
      "Content-Type": "application/json",
    },
    body: JSON.stringify(payload),
  });
}
