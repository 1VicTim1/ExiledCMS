import { NextRequest } from "next/server";

import { proxyTicketsServiceRequest } from "@/lib/tickets-proxy";

export const dynamic = "force-dynamic";

export async function GET(
  request: NextRequest,
  context: { params: Promise<{ id: string }> },
) {
  const { id } = await context.params;

  return proxyTicketsServiceRequest(request, `/api/v1/tickets/${id}`, {
    forwardQuery: true,
  });
}
