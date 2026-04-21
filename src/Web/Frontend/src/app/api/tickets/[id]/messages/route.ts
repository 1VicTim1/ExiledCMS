import { NextRequest } from "next/server";

import {
  invalidJsonResponse,
  parseOptionalDisplayName,
  proxyTicketsServiceRequest,
} from "@/lib/tickets-proxy";
import type { AddTicketMessageProxyRequest } from "@/lib/tickets-types";

export const dynamic = "force-dynamic";

export async function POST(
  request: NextRequest,
  context: { params: Promise<{ id: string }> },
) {
  let payload: Partial<AddTicketMessageProxyRequest>;

  try {
    payload = (await request.json()) as Partial<AddTicketMessageProxyRequest>;
  } catch {
    return invalidJsonResponse();
  }

  const { id } = await context.params;
  const actorDisplayName = parseOptionalDisplayName(payload.actorDisplayName);
  const messageRequest = {
    body: payload.body,
  };

  return proxyTicketsServiceRequest(request, `/api/v1/tickets/${id}/messages`, {
    method: "POST",
    body: messageRequest,
    actorDisplayName,
  });
}
