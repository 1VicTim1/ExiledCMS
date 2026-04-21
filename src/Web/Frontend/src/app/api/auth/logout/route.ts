import { NextRequest } from "next/server";

import { logoutResponse } from "@/lib/auth-proxy";

export const dynamic = "force-dynamic";

export async function POST(request: NextRequest) {
  return logoutResponse(request);
}
