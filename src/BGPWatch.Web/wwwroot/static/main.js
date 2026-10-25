// Wires whichever figures the page holds: the application and the guide share the
// modules and the element ids, so each module starts only where its elements exist.

import { get } from "./api.js";
import { renderGlossary, wirePopovers } from "./glossary.js";
import { mountStory } from "./story.js";
import { startIncident2008 } from "./timeline.js";
import { startPrefix } from "./prefix.js";
import { startLookup } from "./lookup.js";
import { wireDisclosures } from "./disclosures.js";
import { watchStatus } from "./status.js";

const $ = (id) => document.getElementById(id);

if ($("glossary")) renderGlossary($("glossary"));
wirePopovers();
document.querySelectorAll("[data-disclosures]").forEach(wireDisclosures);
document.querySelectorAll("[data-story-view]").forEach(mountStory);
if ($("dot-lab")) watchStatus(get);

// The language toggle keeps the view or the section the reader is on.
document.querySelectorAll("a[data-lang]").forEach((link) => link.addEventListener("click", () => {
  link.href = link.href.split("#")[0] + location.hash;
}));
if ($("lanes")) startIncident2008();
if ($("prefix-choice")) startPrefix();
if ($("lookup-form")) startLookup();

