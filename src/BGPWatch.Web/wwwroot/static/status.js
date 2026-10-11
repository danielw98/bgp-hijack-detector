// The status items: lab reachable, route feed answering, last update. The word carries the state.

const $ = (id) => document.getElementById(id);

export function setLab(up) {
  $("dot-lab").className = "dot " + (up ? "up" : "down");
  $("lab-state").textContent = up ? "Rutere pornite" : "Rutere oprite";
}

export function setData(up) {
  $("dot-data").className = "dot " + (up ? "up" : "down");
  $("data-state").textContent = up ? "Flux activ" : "Flux oprit";
  if (up) $("updated").textContent = "actualizat " + new Date().toLocaleTimeString("ro-RO");
}
