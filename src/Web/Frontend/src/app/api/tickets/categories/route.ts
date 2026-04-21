import { NextRequest } from "next/server";

import { proxyTicketsServiceRequest } from "@/lib/tickets-proxy";

export const dynamic = "force-dynamic";

export async function GET(request: NextRequest) {
  return proxyTicketsServiceRequest(request, "/api/v1/ticket-categories", {
    forwardQuery: true,
  });
}
