// The references page: a link to a source, a stage or an RFC opens every closed block
// that holds its target, then scrolls the target into view.

function reveal() {
  const id = decodeURIComponent(location.hash.slice(1));
  const target = id && document.getElementById(id);
  if (!target) return;
  for (let block = target.closest("details"); block; block = block.parentElement.closest("details")) {
    block.open = true;
  }
  target.scrollIntoView({ block: "start" });
}

document.addEventListener("click", (event) => {
  const link = event.target.closest('a[href^="#"]');
  if (link && link.getAttribute("href") === location.hash) reveal();
});
window.addEventListener("hashchange", reveal);
reveal();
