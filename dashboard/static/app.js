// The application's views: one at a time, chosen from the top bar or the URL hash,
// so a link from the guide opens straight on a view.

const views = [...document.querySelectorAll(".view")];
const links = [...document.querySelectorAll("nav.views a[data-view]")];

function show(name) {
  views.forEach((view) => { view.hidden = view.id !== `view-${name}`; });
  links.forEach((link) => link.classList.toggle("current", link.dataset.view === name));
  window.scrollTo({ top: 0 });
}

function fromHash() {
  const name = location.hash.replace("#", "") || "lab";
  show(views.some((view) => view.id === `view-${name}`) ? name : "lab");
}

window.addEventListener("hashchange", fromHash);
fromHash();
