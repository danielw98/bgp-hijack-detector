// Wires whichever figures the page holds: the application and the guide share the
// modules and the element ids, so each module starts only where its elements exist.

import { post } from "./api.js";
import { renderGlossary, wirePopovers } from "./glossary.js";
import { mountSimulator } from "./simulator.js";
import { startIncidents } from "./incidents.js";
import { startIncident2008 } from "./timeline.js";
import { startPrefix } from "./prefix.js";
import { startLookup } from "./lookup.js";

const $ = (id) => document.getElementById(id);

if ($("glossary")) renderGlossary($("glossary"));
wirePopovers();
document.querySelectorAll("[data-simulator]").forEach(mountSimulator);
if ($("incidents")) startIncidents();
if ($("lanes")) startIncident2008();
if ($("prefix-choice")) startPrefix();
if ($("lookup-form")) startLookup();

if ($("btn-clear")) {
  $("btn-clear").addEventListener("click", async () => {
    if (!confirm("Ștergi alertele și istoricul de rute ale simulatorului? Incidentul din 2008 și ruterele rămân neschimbate.")) return;
    const btn = $("btn-clear");
    const msg = $("clear-msg");
    btn.disabled = true;
    msg.textContent = "se golește...";
    try {
      const res = await post("/api/history/clear");
      msg.textContent = `Șterse: ${res.alerts} alerte, ${res.routes} rute. Ruterele își retrimit tabelele.`;
    } catch (e) {
      msg.textContent = "Eroare: " + e.message;
    } finally {
      btn.disabled = false;
    }
  });
}
