// The page's texts in the reader's language: the layout writes them into
// <script id="strings" type="application/json">, keyed as in strings.<lang>.json
// without the "js." prefix. t() fills {name} placeholders with the values given.

const node = document.getElementById("strings");
const STRINGS = node ? JSON.parse(node.textContent) : {};

const LANG = document.documentElement.lang || "en";
export const LOCALE = LANG === "ro" ? "ro-RO" : "en-GB";

export function t(key, values = {}) {
  const text = STRINGS[key];
  if (text === undefined) return key;
  return text.replace(/\{(\w+)\}/g, (match, name) => (name in values ? String(values[name]) : match));
}

// A count with its noun from "<key>.one" and "<key>.many"; from 20 on, Romanian puts
// "de" between them (51 de alerte), except after 01-19 in the last two digits (101 alerte).
export function count(n, key) {
  if (n === 1) return t(`${key}.one`);
  const de = LANG === "ro" && n >= 20 && (n % 100 === 0 || n % 100 >= 20) ? "de " : "";
  return `${n} ${de}${t(`${key}.many`)}`;
}

// The word that joins two items of a list, with a space on each side.
export function and() {
  return ` ${t("and")} `;
}
