// Where a packet ends up: two destinations of the victim's block (one inside the /25 the
// attacker announces, one outside it) by the three routers it could leave from, each cell
// the outcome of the walk on the current tables (simulate.js). A click on a cell runs that
// walk on the map.

import { esc } from "./format.js";
import { simulate } from "./simulate.js";
import { t } from "./i18n.js";

const FROM = ["victim", "transit", "attacker"];
const CELL = {
  delivered: { cls: "delivered", key: "grid.cell.delivered" },
  dropped: { cls: "dropped", key: "grid.cell.dropped" },
  unreachable: { cls: "none", key: "grid.cell.unreachable" },
};

export function createGrid(container, destinations, onPick) {
  container.addEventListener("click", (e) => {
    const cell = e.target.closest("button[data-from]");
    if (cell && onPick) onPick(cell.dataset.from, cell.dataset.to);
  });

  function render(lab, names) {
    if (!lab.routers.length) { container.innerHTML = ""; return; }
    const head = FROM.map((role) => `<th scope="col">${esc(t("grid.from", { name: names[role] }))}</th>`).join("");
    const rows = destinations.map(([address, where]) => {
      const cells = FROM.map((role) => {
        const outcome = simulate(lab, role, address).outcome;
        const cell = CELL[outcome] || { cls: "none", key: "grid.cell.other" };
        return `<td><button type="button" class="cell ${cell.cls}" data-from="${role}" data-to="${esc(address)}" title="${esc(t("grid.hint"))}">${esc(t(cell.key))}</button></td>`;
      }).join("");
      return `<tr><th scope="row"><span class="mono">${esc(address)}</span> <span class="where">${esc(where)}</span></th>${cells}</tr>`;
    }).join("");
    container.innerHTML = `<table class="grid"><caption>${esc(t("grid.title"))}</caption><thead><tr><th></th>${head}</tr></thead><tbody>${rows}</tbody></table>`;
  }

  return { render };
}
