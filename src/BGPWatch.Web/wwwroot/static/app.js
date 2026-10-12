// The application's views: one at a time, chosen from the top bar or the URL hash,
// so a link from the guide opens straight on a view.

const views = [...document.querySelectorAll(".view")];
const links = [...document.querySelectorAll("nav.views a[data-view]")];
const OLD_NAMES = { lab: "simulator", prefixe: "prefixes", verifica: "lookup" };

function show(name) {
  views.forEach((view) => { view.hidden = view.id !== `view-${name}`; });
  links.forEach((link) => {
    link.classList.toggle("current", link.dataset.view === name);
    if (link.dataset.view === name) link.setAttribute("aria-current", "page"); else link.removeAttribute("aria-current");
  });
  window.scrollTo({ top: 0 });
}

function fromHash() {
  const hash = location.hash.replace("#", "");
  const name = OLD_NAMES[hash] || hash || "simulator";
  show(views.some((view) => view.id === `view-${name}`) ? name : "simulator");
}

window.addEventListener("hashchange", fromHash);
fromHash();
