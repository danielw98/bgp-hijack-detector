// A row of toggles with one panel open at a time under it: the labels never move, the
// open panel sits below the row, a click on the open toggle closes it. Each toggle names
// its panel in aria-controls.

export function wireDisclosures(row) {
  const toggles = [...row.querySelectorAll("[aria-controls]")];
  toggles.forEach((toggle) => toggle.addEventListener("click", () => {
    open(toggle.getAttribute("aria-expanded") === "true" ? null : toggle);
  }));

  function open(which) {
    toggles.forEach((toggle) => {
      const on = toggle === which;
      toggle.setAttribute("aria-expanded", String(on));
      const panel = document.getElementById(toggle.getAttribute("aria-controls"));
      if (panel) panel.hidden = !on;
    });
  }
}
