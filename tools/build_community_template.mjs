#!/usr/bin/env node

import fs from "node:fs";
import path from "node:path";
import vm from "node:vm";

const project = path.resolve(import.meta.dirname, "..");
const dataDirectory = process.argv[2];
const outputPath = path.resolve(
  process.argv[3] ?? path.join(project, "translations", "community_translation_template.json"),
);

if (!dataDirectory) {
  console.error("Usage: node tools/build_community_template.mjs <game-data-directory> [output.json]");
  process.exit(1);
}

const english = JSON.parse(
  fs.readFileSync(path.join(project, "translations", "en.json"), "utf8"),
);
const canonical = Object.keys(english);
const context = { window: { BuildData: {} } };
context.BuildData = context.window.BuildData;
vm.createContext(context);
for (const filename of ["data.weapons.js", "data.perks.js", "data.mutations.js", "data.upgrades.js"]) {
  vm.runInContext(
    fs.readFileSync(path.join(dataDirectory, filename), "utf8"),
    context,
    { filename },
  );
}

const { weapons, perkData, mutations, upgradeTrees } = context.BuildData;
const entries = new Map();
const assigned = new Set();
let sectionNumber = 0;

function plain(value) {
  return value
    .replace(/<[^>]+>/g, "")
    .replace(/'''/g, "")
    .replace(/^\s*>\s*/gm, "")
    .replace(/\s+/g, " ")
    .trim();
}

function communityRows(value) {
  return value
    .split(/\r?\n/)
    .map((line) => line.replace(/<[^>]+>/g, "").replace(/'''/g, "").trim())
    .map((line) => line.replace(/^[>〉]\s*/, "").replace(/\[([^\]]+)\]/g, "$1"))
    .filter(Boolean);
}

function marker(label) {
  sectionNumber += 1;
  const slug = label.toUpperCase().replace(/[^A-Z0-9]+/g, "_").replace(/^_|_$/g, "");
  entries.set(`__SECTION_${String(sectionNumber).padStart(3, "0")}_${slug}__`, "");
}

function add(source) {
  for (const row of communityRows(source ?? "")) {
    if (assigned.has(row)) continue;
    entries.set(row, "");
    assigned.add(row);
  }
}

function addExactConcept(label) {
  const wanted = plain(label).toLocaleLowerCase("en-US");
  const matches = canonical.filter((source) => {
    const candidate = plain(source).replace(/^\d+\.\s*/, "").toLocaleLowerCase("en-US");
    return candidate === wanted;
  });
  const exact = matches.filter((source) => plain(source).toLocaleLowerCase("en-US") === wanted);
  const preferred = exact.find((source) => plain(source) === plain(label).toUpperCase()) ?? exact[0] ?? matches[0];
  if (preferred) add(preferred);
}

function descriptionRows(description) {
  return (description ?? "").split(/\s*\|\s*/).flatMap(communityRows);
}

function addDescription(description) {
  for (const row of descriptionRows(description)) add(row);
}

function addMatching(pattern) {
  for (const source of canonical) {
    if (pattern.test(plain(source))) add(source);
  }
}

function addConcepts(labels) {
  for (const label of labels) addExactConcept(label);
}

marker("MAIN MENU");
entries.set(
  "__COMMENT_TRANSLATE_VALUES_ONLY_LEAVE_NUMBER_ROWS_BLANK__",
  "",
);
entries.set(
  "__COMMENT_NUMBERS_KEY_BINDINGS_AND_FORMATTING_ARE_ADDED_AFTER_TRANSLATION__",
  "",
);
addConcepts([
  "DISFIGURE", "PLAY", "LEADERBOARD", "SETTINGS", "CREDITS", "QUIT", "BACK",
  "SELECT MAP", "MAP 1", "MAP 2", "MAP 3", "COMING SOON", "CONTINUE", "NEW RUN",
]);

marker("WEAPONS — SHARED STAT NAMES");
const sharedStatNames = [
  "Bullet Damage", "Fire Rate", "Bullet Size", "Bullet Speed", "Projectile Speed",
  "Projectiles", "Projectiles Per Shot", "Burst Fire", "Melee Damage", "Melee Width",
  "Melee Range", "Damage", "Damage Rate", "Width", "Range", "Catalyst Damage",
  "Catalyst Duration", "Catalyst Damage Rate", "Explosion Size", "Pierce", "Perks",
];
for (const label of sharedStatNames) addExactConcept(label);
const sharedStatSet = new Set(sharedStatNames.map((name) => name.toLocaleLowerCase("en-US")));
for (const source of canonical) {
  const lines = source.split(/\r?\n/).map(plain).filter(Boolean);
  if (lines.length > 1 && lines.every((line) => sharedStatSet.has(line.toLocaleLowerCase("en-US")))) {
    add(source);
  }
}

marker("WEAPONS — SHARED STAT NUMBER VALUES — DO NOT TRANSLATE");
const statValues = new Set();
for (const weapon of weapons) {
  for (const [name, value] of Object.entries(weapon.stats ?? {})) {
    if (typeof value !== "number") continue;
    statValues.add(String(value));
    if (weapon.statUnits?.[name]) statValues.add(`${value}${weapon.statUnits[name]}`);
  }
}
statValues.add("100%");
for (const value of [...statValues].sort((a, b) => a.localeCompare(b, "en", { numeric: true }))) add(value);

marker("WEAPON CHALLENGES — GENERAL TEXT");
addMatching(/^(CHALLENGE|CHALLENGES|QUEST|SCORE BONUS)(?:\b|\s*-)/i);
for (const tier of ["GOLDEN", "RUBY", "DIAMOND"]) {
  marker(`WEAPON CHALLENGES — ${tier} TIER`);
  for (const weapon of weapons) {
    const displayName = weapon.name === "Akimbo SMGs" ? "Akimbo Smgs" : weapon.name;
    addExactConcept(`${tier} ${weapon.name}`);
    addExactConcept(`Complete the ${tier === "GOLDEN" ? "Golden" : tier[0] + tier.slice(1).toLowerCase()} tier challenge with the ${displayName}.`);
  }
}

for (const [index, weapon] of weapons.entries()) {
  marker(`WEAPON ${String(index + 1).padStart(2, "0")} — ${weapon.name}`);
  addExactConcept(weapon.name);
  addDescription(weapon.description);
  for (const perkName of weapon.perks ?? []) {
    addExactConcept(perkName);
    addDescription(perkData[perkName]?.text ?? "");
  }
}

marker("PERMANENT STATS SCREEN");
addConcepts([
  "PROFILE STATS", "PLAYER STATS", "RUN STATS", "TOTALS", "TOTAL DAMAGE DEALT",
  "TOTAL SCORE", "CREDITS EARNED", "RESET PROGRESS", "RESET STATS",
  "MUTATIONS UNLOCKED", "SYNERGIES UNLOCKED", "ONLINE SYNC IS REQUIRED TO RESET PROGRESS. CHECK YOUR CONNECTION AND TRY AGAIN.",
  "THIS PERMANENTLY DELETES ALL PROGRESS ON THIS DEVICE AND YOUR SYNCED CLOUD SAVE: CREDITS, UNLOCKS, QUESTS, WEAPONS, MUTATIONS, MAPS, COMPENDIUM, STATS, AND META-PROGRESSION. THIS CANNOT BE UNDONE.",
  "HOLD TO PURCHASE FOR", "HOLD TO UNLOCK FOR",
]);
add("HOLD TO PURCHASE FOR");
add("HOLD TO UNLOCK FOR");
for (const weapon of weapons) addExactConcept(`${weapon.name} UNLOCKED`);
addConcepts(["MAP 2 UNLOCKED", "MAP 3 UNLOCKED"]);

marker("MUTATIONS SCREEN");
addConcepts([
  "MUTATIONS", "CHOOSE A MUTATION", "REROLL", "(this run)",
  "(this level up)", "SKILL POINTS REMAINING", "MUTATION INTRO",
  "KILL A BOSS TO UNLOCK MUTATIONS MENU!",
  "Every boss kill during a run will allow you to choose a Mutation Upgrade. Choose here which mutations you want equipped.",
]);
add("remaining");

marker("MUTATIONS — NAMES AND DESCRIPTIONS");
for (const [index, mutation] of mutations.entries()) {
  addExactConcept(mutation.name);
  addExactConcept(`${index + 1}. ${mutation.name}`);
  addDescription(mutation.effect);
}

marker("UPGRADES SCREEN");
addConcepts([
  "CHOOSE", "CHOOSE AN UPGRADE", "CHOOSE WEAPON PERK", "LEVEL UP", "SKIP",
  "USE UPGRADE", "UPGRADES", "DISCOVER THIS UPGRADE DURING A RUN TO LEARN WHAT IT DOES!",
  "DISCOVER THIS UPGRADE TO LEARN WHAT IT DOES!",
]);
for (const [treeIndex, tree] of upgradeTrees.entries()) {
  marker(`UPGRADE TREE ${String(treeIndex + 1).padStart(2, "0")} — ${tree.name}`);
  const upgrades = [tree.base, ...(tree.branchA ?? []), ...(tree.branchB ?? [])].filter(Boolean);
  for (const upgrade of upgrades) {
    addExactConcept(upgrade.name);
    addDescription(upgrade.effect);
  }
}

marker("GAME HUD AND END SCREEN");
addConcepts([
  "PAUSED", "RESUME", "RESTART", "YOU SURVIVED", "GAME OVER", "NEW HIGH SCORE",
  "HEALTH", "SCORE", "KILLS", "MINIBOSS KILLS", "BOSS KILLS",
  "LEVELS GAINED", "SCORE MULTIPLIER", "TOTAL SCORE", "CREDITS EARNED",
]);
add("LEVEL");
add("Move");
add("Shoot");
add("Switch Vision Mode");
add("Toggle Auto-Fire");
add("Toggle Aim Assist");
add("Toggle Zoom");

marker("SETTINGS AND CONTROLS");
addConcepts([
  "SETTINGS", "OPTIONS", "BACK", "ON", "OFF", "MASTER VOLUME", "EFFECTS VOLUME",
  "MUSIC", "VOLUME", "FULLSCREEN", "VSYNC", "FPS LIMIT", "SHOW FPS", "RESOLUTION",
  "CAMERA MOVEMENT", "SCREEN SHAKE", "SCREEN TEXTURE", "VHS SCANLINES", "COLOR SETTINGS",
  "INPUT SETTINGS", "AUDIO SUPPORT", "LANGUAGE: ENGLISH",
]);

marker("LEADERBOARD");
addConcepts([
  "LEADERBOARD", "LEADERBOARDS", "RANK", "PLAYER", "SCORE", "TIME", "DAILY",
  "WEEKLY", "ALL TIME", "GLOBAL", "FRIENDS", "NEXT", "PREVIOUS",
]);

const lines = ["{"];
const allEntries = [...entries];
for (let index = 0; index < allEntries.length; index += 1) {
  const [key, value] = allEntries[index];
  const comma = index === allEntries.length - 1 ? "" : ",";
  lines.push(`  ${JSON.stringify(key)}: ${JSON.stringify(value)}${comma}`);
}
lines.push("}", "");
fs.writeFileSync(outputPath, lines.join("\n"), "utf8");

const actual = new Set(
  [...entries.keys()].filter(
    (key) => !key.startsWith("__SECTION_") && !key.startsWith("__COMMENT_"),
  ),
);
const required = ["PLAY", "SETTINGS", "MAP 3", ...weapons.map((item) => item.name), ...mutations.map((item) => item.name)];
const actualConcepts = new Set([...actual].map((key) => plain(key).replace(/^\d+\.\s*/, "").toLocaleLowerCase("en-US")));
const missingConcepts = required.filter((key) => !actualConcepts.has(plain(key).toLocaleLowerCase("en-US")));
const requiredDescriptions = [
  ...weapons.flatMap((weapon) => [weapon.description, ...(weapon.perks ?? []).map((name) => perkData[name]?.text)]),
  ...mutations.map((mutation) => mutation.effect),
  ...upgradeTrees.flatMap((tree) => [tree.base, ...(tree.branchA ?? []), ...(tree.branchB ?? [])].filter(Boolean).map((upgrade) => upgrade.effect)),
].flatMap(descriptionRows);
const missingDescriptions = requiredDescriptions.filter((row) => !actual.has(row));
const forbiddenRuntimeText = [
  "Eoniko", "Ezgi Akhan", "Feyy", "Framgel", "FlareBlade", "GSGSGESFSE",
  "Giantfiredog", "Glenn_Blanka", "GoobenPoopen", "Gorthauer", "GrasLeeu",
];
const leakedRuntimeText = forbiddenRuntimeText.filter((key) => actual.has(key));
const nonblank = [...entries].filter(([, value]) => value !== "");
const formatted = [...actual].filter((key) => /<[^>]+>|[〉\[\]{}]|\r|\n/i.test(key));
const obsolete = [...actual].filter((key) => /^LEVEL \d+$|The full game will have|If you enjo/i.test(key));
if (missingConcepts.length || missingDescriptions.length || leakedRuntimeText.length || obsolete.length || nonblank.length || formatted.length || !actual.has("100%")) {
  throw new Error(`Template audit failed: ${missingConcepts.length} concepts missing, ${missingDescriptions.length} descriptions missing, ${leakedRuntimeText.length + obsolete.length} obsolete/runtime strings leaked`);
}

console.log(`${actual.size} source strings in ${sectionNumber} labeled sections -> ${outputPath}`);
