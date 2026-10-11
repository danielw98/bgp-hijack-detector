// One popover card at a time, anchored to an element: it opens on the side with the
// most room, its arrow points at the anchor's centre, and it closes on Escape, a click
// outside, its close button, a resize or a scroll.

import { esc } from "./format.js";

const WIDTH = 360;
const GAP = 14;
const EDGE = 8;
const ARROW_INSET = 18;

export function createPopover() {
  const card = document.createElement("div");
  card.className = "popover";
  card.setAttribute("role", "dialog");
  card.hidden = true;
  document.body.appendChild(card);
  let anchor = null;
  let returnFocus = null;

  document.addEventListener("keydown", (e) => { if (e.key === "Escape" && !card.hidden) close(); });
  document.addEventListener("pointerdown", (e) => { if (!card.hidden && !card.contains(e.target) && !(anchor && anchor.contains(e.target))) close(); });
  window.addEventListener("resize", () => close());
  window.addEventListener("scroll", () => close());
  card.addEventListener("click", (e) => { if (e.target.closest("[data-close]")) close(); });

  // content: { kind, title, route, position, fields: [[label, html]], footer: html }
  function open(target, content) {
    if (card.hidden) returnFocus = document.activeElement;
    anchor = target;
    card.className = `popover ${content.kind}`;
    card.setAttribute("aria-label", `${content.title} ${content.route || ""}`.trim());
    card.innerHTML = `<div class="head">
        <div class="title"><b class="kind">${esc(content.title)}</b>${content.route ? `<span class="route">${esc(content.route)}</span>` : ""}</div>
        ${content.position ? `<span class="pos">${esc(content.position)}</span>` : ""}
        <button class="close" data-close aria-label="Închide">&times;</button>
      </div>
      ${content.fields.map(([label, html]) => `<div class="field"><div class="label">${esc(label)}</div><div class="value">${html}</div></div>`).join("")}
      ${content.footer ? `<div class="foot">${content.footer}</div>` : ""}
      <span class="arrow"></span>`;
    card.hidden = false;
    place();
    card.querySelector("[data-close]").focus({ preventScroll: true });
  }

  function close() {
    if (card.hidden) return;
    card.hidden = true;
    anchor = null;
    if (returnFocus && returnFocus.isConnected) returnFocus.focus({ preventScroll: true });
  }

  function place() {
    const a = anchor.getBoundingClientRect();
    const w = Math.min(WIDTH, window.innerWidth - 2 * EDGE);
    card.style.width = `${w}px`;
    const h = card.offsetHeight;
    const room = { right: window.innerWidth - a.right, left: a.left, below: window.innerHeight - a.bottom, above: a.top };
    const fitsSide = Math.max(room.right, room.left) >= w + GAP + EDGE;
    const side = fitsSide ? (room.right >= room.left ? "right" : "left") : (room.below >= room.above ? "below" : "above");
    const cx = (a.left + a.right) / 2;
    const cy = (a.top + a.bottom) / 2;
    const clamp = (v, lo, hi) => Math.max(lo, Math.min(hi, v));
    let left;
    let top;
    if (side === "right" || side === "left") {
      left = side === "right" ? a.right + GAP : a.left - GAP - w;
      top = clamp(cy - h / 2, EDGE, window.innerHeight - h - EDGE);
    } else {
      left = clamp(cx - w / 2, EDGE, window.innerWidth - w - EDGE);
      top = side === "below" ? a.bottom + GAP : a.top - GAP - h;
    }
    card.style.left = `${left}px`;
    card.style.top = `${Math.max(EDGE, top)}px`;
    const arrow = card.querySelector(".arrow");
    arrow.className = `arrow ${side}`;
    if (side === "right" || side === "left") arrow.style.cssText = `top:${clamp(cy - top, ARROW_INSET, h - ARROW_INSET)}px`;
    else arrow.style.cssText = `left:${clamp(cx - left, ARROW_INSET, w - ARROW_INSET)}px`;
  }

  return { open, close, isOpen: () => !card.hidden };
}
