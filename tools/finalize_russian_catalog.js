const fs = require('fs');

const catalogPath = process.argv[2] || 'DisfigureTranslationMod/translations/ru.json';
const overridePath = process.argv[3] || 'DisfigureTranslationMod/translations/ru_overrides.json';
const catalog = JSON.parse(fs.readFileSync(catalogPath, 'utf8'));
const overrides = JSON.parse(fs.readFileSync(overridePath, 'utf8'));

Object.assign(catalog, overrides);

const labels = {
  'Inside Vision Damage Multiplier': 'Множитель урона в области обзора',
  'Outside Vision Damage Multiplier': 'Множитель урона вне области обзора',
  'Circle/Cone Vision Size': 'Размер кругового/конического обзора',
  'Bullet Damage/Fire Rate': 'Урон пуль/скорострельность',
  'Catalyst Duration/Damage Rate': 'Длительность/частота урона катализатора',
  'Plasma Damage/Cooldown': 'Урон/перезарядка плазмы',
  'Bleed Damage/Chance': 'Урон/шанс кровотечения',
  'Blaze Damage/Range': 'Урон/радиус вспышки',
  'Bullet Size/Speed': 'Размер/скорость пуль',
  'Melee Range/Width': 'Дальность/ширина ближней атаки',
  'Catalyst Laser Start Delay': 'Задержка лазера катализатора',
  'Circle Vision Size': 'Размер кругового обзора',
  'Cone Vision Size': 'Размер конического обзора',
  'EXP Pickup Range': 'Радиус подбора опыта',
  'Charged Round Chance': 'Шанс заряженной пули',
  'Catalyst Damage Rate': 'Частота урона катализатора',
  'Guided Laser Damage': 'Урон управляемого лазера',
  'Lightning Bolt Damage': 'Урон молнии',
  'Radiation Damage': 'Урон радиации',
  'Singularity Damage': 'Урон сингулярности',
  'Wild Fire Damage': 'Урон дикого огня',
  'Catalyst Damage': 'Урон катализатора',
  'Missile Damage': 'Урон ракет',
  'Shock Damage': 'Урон шока',
  'Outflow Damage': 'Урон оттока',
  'Meteor Damage Multiplier': 'Множитель урона метеора',
  'Bullet Accuracy': 'Точность пуль',
  'Bullet Damage': 'Урон пуль',
  'Bullet Speed': 'Скорость пуль',
  'Bullet Size': 'Размер пуль',
  'Fire Rate': 'Скорострельность',
  'Movement Speed': 'Скорость передвижения',
  'Melee Damage': 'Урон ближнего боя',
  'Melee Range': 'Дальность ближнего боя',
  'Melee Width': 'Ширина ближней атаки',
  'Melee Size': 'Размер ближней атаки',
  'Enemy Speed': 'Скорость врагов',
  'EXP Gain': 'Получение опыта',
  'Max Health': 'Максимум здоровья',
  'Weapon Stats': 'Характеристики оружия',
  'Akimbo Recoil': 'Отдача парного оружия',
  'Fire Damage': 'Урон огня',
  'Bleed Damage': 'Урон кровотечения',
  'Bleed Chance': 'Шанс кровотечения',
  'Catalyst Duration': 'Длительность катализатора',
  'Fire Cooldown': 'Перезарядка огня',
  'Blaze Damage': 'Урон вспышки',
  'Blaze Range': 'Радиус вспышки',
  'Parry Damage Range': 'Радиус урона парирования'
};
const sortedLabels = Object.keys(labels).sort((a, b) => b.length - a.length);

function translateStat(source) {
  for (const label of sortedLabels) {
    if (!source.startsWith(label)) continue;
    const suffix = source.slice(label.length);
    if (/^\s*(?:<color\b|[+-]\d)/i.test(suffix)) return labels[label] + suffix;
  }
  return null;
}

for (const source of Object.keys(catalog)) {
  const sourceLines = source.split('\n');
  const valueLines = String(catalog[source]).split('\n');
  for (let i = 0; i < sourceLines.length; i++) {
    const direct = translateStat(sourceLines[i]);
    if (direct !== null) {
      valueLines[i] = direct;
      continue;
    }
    const parts = sourceLines[i].split(' | ');
    const translated = parts.map(translateStat);
    if (parts.length > 1 && translated.every(value => value !== null)) valueLines[i] = translated.join(' | ');
  }
  catalog[source] = valueLines.join('\n');
}

for (const source of Object.keys(catalog)) {
  let value = catalog[source];
  for (const label of sortedLabels) value = value.split(label).join(labels[label]);
  value = value.split('Charged Round').join('Заряженная пуля');
  value = value.split('Blight').join('Скверна');
  value = value.split('Vision').join('Обзор');
  catalog[source] = value;
}

const endLabels = ['MINIBOSS KILLS', 'BOSS KILLS', 'LEVELS GAINED', 'KILLS'];
for (const source of Object.keys(catalog)) {
  for (const label of endLabels) {
    const suffix = source.slice(label.length);
    if (source.startsWith(label) && /^\s*(?:<color\b|\[)/i.test(suffix)) {
      catalog[source] = overrides[label] + suffix;
    }
  }
}

const corrupted = Object.entries(catalog).filter(([, value]) => /\?{2,}/.test(value));
if (corrupted.length) throw new Error(`${corrupted.length} values still contain encoding-corruption question marks.`);

fs.writeFileSync(catalogPath, JSON.stringify(catalog, null, 2) + '\n', 'utf8');
console.log(`Finalized ${Object.keys(catalog).length} Russian translations with ${Object.keys(overrides).length} overrides.`);
