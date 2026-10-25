// Glossary terms: every <dfn class="term" data-term="..."> opens a popover with its
// definition; the glossary section lists them all. Definitions live in the strings files.

import { esc } from "./format.js";
import { t } from "./i18n.js";

const TERM_KEYS = ["as", "prefix", "bgp", "update", "as_path", "origin", "announcement", "subprefix", "hijack", "rpki", "vantage", "collector", "mrt", "ris", "next_hop", "blackhole"];

const TERMS = Object.fromEntries(TERM_KEYS.map((key) => [key, [t(`term.${key}.title`), t(`term.${key}.body`)]]));

export function renderGlossary(container) {
  container.innerHTML = Object.entries(TERMS)
    .map(([key, [title, body]]) => `<dt id="g-${esc(key)}">${esc(title)}</dt><dd>${esc(body)}</dd>`)
    .join("");
}

export function wirePopovers(root = document) {
  let pop = null;
  const close = () => { if (pop) { pop.remove(); pop = null; } };
  const open = (term) => {
    close();
    const [title, body] = TERMS[term.dataset.term] || ["", ""];
    if (!title) return;
    pop = document.createElement("div");
    pop.className = "pop";
    pop.setAttribute("role", "dialog");
    pop.innerHTML = `<b>${esc(title)}.</b> ${esc(body)}`;
    document.body.appendChild(pop);
    const r = term.getBoundingClientRect();
    const width = Math.min(360, window.innerWidth - 32);
    pop.style.width = width + "px";
    pop.style.left = Math.max(16, Math.min(r.left + window.scrollX, window.innerWidth - width - 16 + window.scrollX)) + "px";
    pop.style.top = (r.bottom + window.scrollY + 8) + "px";
  };
  root.addEventListener("click", (e) => {
    const term = e.target.closest("dfn.term");
    if (term) { e.preventDefault(); pop && pop.dataset.for === term.dataset.term ? close() : open(term); if (pop) pop.dataset.for = term.dataset.term; return; }
    if (!e.target.closest(".pop")) close();
  });
  root.addEventListener("keydown", (e) => {
    if (e.key === "Escape") close();
    const term = e.target.closest && e.target.closest("dfn.term");
    if (term && (e.key === "Enter" || e.key === " ")) { e.preventDefault(); open(term); pop.dataset.for = term.dataset.term; }
  });
  window.addEventListener("resize", close);
}
