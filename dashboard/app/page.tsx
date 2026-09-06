import { redirect } from "next/navigation";

/** Step 1 of the four-step flow is where the product starts. */
export default function RootPage() {
  redirect("/conversation");
}
