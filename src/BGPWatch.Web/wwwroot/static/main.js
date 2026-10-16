// Wires whichever figures the page holds: the application and the guide share the
// modules and the element ids, so each module starts only where its elements exist.

import { post } from "./api.js";
import { t } from "./i18n.js";
import { renderGlossary, wirePopovers } from "./glossary.js";
import { mountSimulator } from "./simulator.js";
import { startIncidents } from "./incidents.js";
import { startIncident2008 } from "./timeline.js";
import { startPrefix } from "./prefix.js";
import { startLookup } from "./lookup.js";
import { wireDisclosures } from "./disclosures.js";

const $ = (id) => document.getElementById(id);

if ($("glossary")) renderGlossary($("glossary"));
wirePopovers();
document.querySelectorAll("[data-disclosures]").forEach(wireDisclosures);
document.querySelectorAll("[data-simulator]").forEach(mountSimulator);
if (document.querySelector("[data-incidents]")) startIncidents();

// The language toggle keeps the view or the section the reader is on.
document.querySelectorAll("a[data-lang]").forEach((link) => link.addEventListener("click", () => {
  link.href = link.href.split("#")[0] + location.hash;
}));
if ($("lanes")) startIncident2008();
if ($("prefix-choice")) startPrefix();
if ($("lookup-form")) startLookup();

if ($("btn-clear")) {
  $("btn-clear").addEventListener("click", async () => {
    if (!confirm(t("clear.confirm"))) return;
    const btn = $("btn-clear");
    const msg = $("clear-msg");
    btn.disabled = true;
    msg.textContent = t("clear.busy");
    try {
      const res = await post("/api/history/clear");
      msg.textContent = t("clear.done", { alerts: res.alerts, routes: res.routes });
    } catch (e) {
      msg.textContent = t("error", { message: e.message });
    } finally {
      btn.disabled = false;
    }
  });
}
