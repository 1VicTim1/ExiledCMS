import { logoutResponse } from "@/lib/auth-proxy";

export const dynamic = "force-dynamic";

export async function POST() {
  return logoutResponse();
}
