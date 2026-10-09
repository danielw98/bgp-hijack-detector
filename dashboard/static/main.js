// Wires the page: glossary popovers, the five figures, and the history control.

import { post } from "./api.js";
import { renderGlossary, wirePopovers } from "./glossary.js";
import { startLab } from "./lab.js";
import { startIncidents } from "./incidents.js";
import { startIncident2008 } from "./timeline.js";
import { startPrefix } from "./prefix.js";
import { startLookup } from "./lookup.js";

const $ = (id) => document.getElementById(id);

renderGlossary($("glossary"));
wirePopovers();
startLab();
startIncidents();
startIncident2008();
startPrefix();
startLookup();

$("btn-clear").addEventListener("click", async () => {
  if (!confirm("Ștergi alertele și istoricul de rute ale laboratorului? Incidentul din 2008 și laboratorul rămân neschimbate.")) return;
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
