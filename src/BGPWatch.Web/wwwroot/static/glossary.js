// Glossary terms: every <dfn class="term" data-term="..."> opens a popover with its
// definition; the glossary section lists them all. Definitions live here, once.

import { esc } from "./format.js";

export const TERMS = {
  as: ["Sistem autonom (AS)", "O rețea administrată de o singură organizație, identificată printr-un număr: AS3333 este RIPE NCC, AS65001 este victima din laborator. Internetul este format din zeci de mii de astfel de rețele care își anunță una alteia ce adrese dețin."],
  prefix: ["Prefix", "Un bloc de adrese IP scris ca adresă și lungime: 203.0.113.0/24 înseamnă cele 256 de adrese care încep cu 203.0.113. Cu cât lungimea este mai mare, cu atât blocul este mai mic și mai specific."],
  bgp: ["BGP", "Border Gateway Protocol, protocolul prin care sistemele autonome își anunță prefixele și află pe unde să trimită traficul. Un ruter BGP nu verifică dacă vecinul are dreptul să anunțe ce anunță."],
  update: ["Mesaj UPDATE", "Mesajul BGP care anunță sau retrage prefixe. Poartă prefixele și atributele căii, între care lista sistemelor autonome traversate."],
  as_path: ["Calea AS", "Lista sistemelor autonome prin care a trecut un anunț. Fiecare AS își adaugă numărul la început când transmite anunțul mai departe, deci ultimul număr este originea."],
  origin: ["Origine", "AS-ul care a pornit anunțul unui prefix, adică ultimul din calea AS. O origine falsă este un AS care anunță un prefix ce nu îi aparține."],
  announcement: ["Anunț", "Afirmația unui AS, transmisă prin BGP, că poate livra trafic către un prefix. Vecinii o cred și o transmit mai departe, dacă politica lor nu o respinge."],
  subprefix: ["Sub-prefix", "Un prefix mai specific, inclus în altul: 203.0.113.0/25 este jumătate din 203.0.113.0/24. Ruterele trimit pachetele pe prefixul cel mai specific, așa că un sub-prefix anunțat de un atacator câștigă oriunde ajunge."],
  hijack: ["Deturnare de rută", "Un AS anunță un prefix care nu îi aparține, sau un sub-prefix al lui, și atrage traficul destinat proprietarului. Poate fi un atac sau o greșeală de configurare; ruterele nu fac diferența."],
  rpki: ["RPKI și ROA", "Resource Public Key Infrastructure: deținătorul unui bloc de adrese semnează o autorizație (ROA) care spune ce AS poate anunța prefixul și până la ce lungime. Un ruter care validează clasifică fiecare rută drept validă, invalidă sau negăsită."],
  vantage: ["Punct de observație", "Un ruter de la care colectorul primește anunțurile. Un anunț fals ajunge doar la o parte dintre rețele, de aceea detectorul ascultă mai multe puncte deodată."],
  collector: ["Colector", "Un proces căruia ruterele îi raportează rutele, doar ca să fie ascultate. În laborator, fiecare ruter îi trimite prin BMP (BGP Monitoring Protocol, RFC 7854) rutele pe care le-a ales; colectorul nu anunță nimic, iar tot ce primește ajunge la detector. RIPE RIS și RouteViews sunt colectoare publice, care primesc rutele prin sesiuni BGP."],
  mrt: ["MRT", "Formatul fișierelor în care colectoarele publice arhivează mesajele BGP (RFC 6396). Parserul acestui proiect le citește direct."],
  ris: ["RIPE RIS", "Routing Information Service, colectoarele publice ale RIPE NCC. Fișierele lor din 24 februarie 2008 sunt cele redate pe această pagină."],
  next_hop: ["Următorul salt", "Adresa vecinului către care un ruter trimite pachetul pentru un prefix. Pe un segment partajat, un anunț poate păstra următorul salt al rutei originale, așa că pachetul ocolește ruterul care l-a transmis."],
  blackhole: ["Rută blackhole", "O rută care aruncă pachetele în loc să le trimită mai departe. Atacatorul din laborator o folosește pentru prefixele deturnate, cum s-a întâmplat și cu traficul YouTube în 2008."],
};

export function termMarkup(key, text) {
  return `<dfn class="term" data-term="${esc(key)}" tabindex="0" role="button" aria-haspopup="true">${esc(text)}</dfn>`;
}

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
