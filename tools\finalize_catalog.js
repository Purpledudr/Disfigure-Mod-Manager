const fs = require('fs');

const language = process.argv[2];
if (!language) throw new Error('Usage: node finalize_catalog.js <language>');

const directory = 'DisfigureTranslationMod/translations';
const catalogPath = `${directory}/${language}.json`;
const settings = JSON.parse(fs.readFileSync(`${directory}/catalog_overrides.json`, 'utf8'))[language];
if (!settings) throw new Error(`No catalog settings for ${language}.`);

const catalog = JSON.parse(fs.readFileSync(catalogPath, 'utf8'));
Object.assign(catalog, settings.exact);
const terms = Object.keys(settings.terms).sort((left, right) => right.length - left.length);

function translateStat(source) {
  for (const term of terms) {
    if (!source.startsWith(term)) continue;
    const suffix = source.slice(term.length);
    if (/^\s*(?:<color\b|[+-]\d)/i.test(suffix)) return settings.terms[term] + suffix;
  }
  return null;
}

for (const source of Object.keys(catalog)) {
  const sourceLines = source.split('\n');
  const valueLines = String(catalog[source]).split('\n');
  for (let i = 0; i < sourceLines.length; i++) {
    const direct = translateStat(sourceLines[i]);
    if (direct !== null) valueLines[i] = direct;
  }
  let value = valueLines.join('\n');
  for (const term of terms) value = value.split(term).join(settings.terms[term]);
  catalog[source] = value;
}

for (const label of ['MINIBOSS KILLS', 'BOSS KILLS', 'LEVELS GAINED', 'KILLS']) {
  for (const source of Object.keys(catalog)) {
    const suffix = source.slice(label.length);
    if (source.startsWith(label) && /^\s*(?:<color\b|\[)/i.test(suffix)) {
      catalog[source] = settings.exact[label] + suffix;
    }
  }
}

for (const [source, translated] of Object.entries(settings.exact)) {
  if (catalog[source] !== translated) throw new Error(`Override failed for ${source}.`);
}
if (Object.values(catalog).some(value => /\?{2,}/.test(value))) {
  throw new Error('Catalog contains repeated question-mark encoding corruption.');
}

fs.writeFileSync(catalogPath, JSON.stringify(catalog, null, 2) + '\n', 'utf8');
console.log(`Finalized ${language}: ${Object.keys(catalog).length} entries, ${Object.keys(settings.exact).length} exact overrides.`);
