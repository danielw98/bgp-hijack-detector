// The status items: lab reachable, route feed answering, last update. The word carries the state.

import { LOCALE, t } from "./i18n.js";

const $ = (id) => document.getElementById(id);

export function setLab(up) {
  $("dot-lab").className = "dot " + (up ? "up" : "down");
  $("lab-state").textContent = up ? t("status.lab.up") : t("status.lab.down");
}

export function setData(up) {
  $("dot-data").className = "dot " + (up ? "up" : "down");
  $("data-state").textContent = up ? t("status.data.up") : t("status.data.down");
  if (up) $("updated").textContent = t("status.updated", { time: new Date().toLocaleTimeString(LOCALE) });
}

// The status line of a page without the simulator: the lab and the route feed, polled.
export function watchStatus(get, everyMs = 15000) {
  const check = async () => {
    try {
      const lab = await get("/api/lab");
      setLab(lab.routers.every((r) => r.reachable));
    } catch {
      setLab(false);
    }
    try {
      await get("/api/stats");
      setData(true);
    } catch {
      setData(false);
    }
  };
  check();
  setInterval(check, everyMs);
}
