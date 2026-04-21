import { redirect } from "next/navigation";

import { getPlatformCorePublicUrl } from "@/lib/platform-server";

export default function SwaggerPage() {
  redirect(`${getPlatformCorePublicUrl()}/swagger`);
}
