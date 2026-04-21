export interface ProblemDetails {
  title?: string;
  detail?: string;
  status?: number;
  errorCode?: string;
}

export interface PagedResponse<T> {
  items: T[];
  page: number;
  pageSize: number;
  totalCount: number;
}

export interface TicketCategoryReference {
  id: string;
  name: string;
  description?: string | null;
}

export interface ActorReference {
  userId: string;
  displayName: string;
  role?: string | null;
}

export interface TicketCategory {
  id: string;
  name: string;
  description?: string | null;
  isActive: boolean;
  displayOrder: number;
  createdAtUtc: string;
  updatedAtUtc: string;
}

export interface TicketSummary {
  id: string;
  subject: string;
  status: string;
  priority: string;
  category: TicketCategoryReference;
  createdBy: ActorReference;
  assignedTo?: ActorReference | null;
  createdAtUtc: string;
  updatedAtUtc: string;
  closedAtUtc?: string | null;
  lastMessageAtUtc: string;
  messageCount: number;
}

export interface TicketMessage {
  id: string;
  author: ActorReference;
  isStaffReply: boolean;
  body: string;
  createdAtUtc: string;
}

export interface TicketAssignment {
  id: string;
  assignedStaff: ActorReference;
  assignedBy: ActorReference;
  isActive: boolean;
  assignedAtUtc: string;
  unassignedAtUtc?: string | null;
}

export interface TicketInternalNote {
  id: string;
  author: ActorReference;
  body: string;
  createdAtUtc: string;
}

export interface TicketAuditLog {
  id: string;
  actor: ActorReference;
  actionType: string;
  isInternal: boolean;
  details: unknown;
  createdAtUtc: string;
}

export interface TicketDetail extends TicketSummary {
  messages: TicketMessage[];
  assignments: TicketAssignment[];
  internalNotes: TicketInternalNote[];
  auditTrail: TicketAuditLog[];
}

export interface CreateTicketProxyRequest {
  subject: string;
  categoryId: string;
  priority: string;
  message: string;
  actorDisplayName?: string;
}

export interface AddTicketMessageProxyRequest {
  body: string;
  actorDisplayName?: string;
}
