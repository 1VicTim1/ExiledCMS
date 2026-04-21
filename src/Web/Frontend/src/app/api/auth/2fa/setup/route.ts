import { NextRequest } from "next/server";

import { proxyAuthenticatedJson } from "@/lib/auth-proxy";

export const dynamic = "force-dynamic";

export async function POST(request: NextRequest) {
  return proxyAuthenticatedJson(request, "/api/v1/auth/2fa/setup", {
    method: "POST",
  });
}
