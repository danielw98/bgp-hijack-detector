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
