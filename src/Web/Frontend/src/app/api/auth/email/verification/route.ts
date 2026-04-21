import { NextRequest } from "next/server";

import { proxyAuthenticatedJson } from "@/lib/auth-proxy";

export const dynamic = "force-dynamic";

export async function POST(request: NextRequest) {
  return proxyAuthenticatedJson(request, "/api/v1/auth/email/verification", {
    method: "POST",
  });
}
