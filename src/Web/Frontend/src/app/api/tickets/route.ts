import { NextRequest } from "next/server";

import {
  invalidJsonResponse,
  parseOptionalDisplayName,
  proxyTicketsServiceRequest,
} from "@/lib/tickets-proxy";
import type { CreateTicketProxyRequest } from "@/lib/tickets-types";

export const dynamic = "force-dynamic";

export async function GET(request: NextRequest) {
  return proxyTicketsServiceRequest(request, "/api/v1/tickets", {
    forwardQuery: true,
  });
}

export async function POST(request: NextRequest) {
  let payload: Partial<CreateTicketProxyRequest>;

  try {
    payload = (await request.json()) as Partial<CreateTicketProxyRequest>;
  } catch {
    return invalidJsonResponse();
  }

  const actorDisplayName = parseOptionalDisplayName(payload.actorDisplayName);
  const ticketRequest = {
    subject: payload.subject,
    categoryId: payload.categoryId,
    priority: payload.priority,
    message: payload.message,
  };

  return proxyTicketsServiceRequest(request, "/api/v1/tickets", {
    method: "POST",
    body: ticketRequest,
    actorDisplayName,
  });
}
