// The quiet status line under the title: lab reachable, route feed answering, last update.

const $ = (id) => document.getElementById(id);

export function setLab(up) {
  $("dot-lab").className = "dot " + (up ? "up" : "down");
  $("lab-state").textContent = up ? "pornit" : "oprit";
}

export function setData(up) {
  $("dot-data").className = "dot " + (up ? "up" : "down");
  $("data-state").textContent = up ? "activ" : "întrerupt";
  if (up) $("updated").textContent = "Actualizat " + new Date().toLocaleTimeString("ro-RO") + ".";
}
