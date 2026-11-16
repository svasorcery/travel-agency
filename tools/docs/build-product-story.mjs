import { createHash } from 'node:crypto';
import { mkdir, readFile, writeFile } from 'node:fs/promises';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

// Markdown is the editable text source. SVG/HTML need no third-party packages.
// Optional --png uses the repository's installed Playwright and local Chromium.
const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../..');
const dir = path.join(root, 'docs/product');
const assets = path.join(dir, 'assets');
await mkdir(assets, { recursive: true });

const c = {
  paper: '#f6f3eb',
  ink: '#142b35',
  muted: '#4e6367',
  line: '#b5c1bb',
  teal: '#14655c',
  pale: '#e4eee6',
  blue: '#275b8c',
  bluePale: '#e5edf5',
  rust: '#a34a2f',
  rustPale: '#f5e6d9',
  white: '#fffefb',
};
const esc = (s) =>
  String(s).replaceAll('&', '&amp;').replaceAll('<', '&lt;').replaceAll('>', '&gt;').replaceAll('"', '&quot;');
const text = (x, y, s, size = 22, fill = c.ink, weight = 400, extra = '') =>
  `<text x="${x}" y="${y}" font-size="${size}" fill="${fill}" font-weight="${weight}" ${extra}>${esc(s)}</text>`;
const lines = (x, y, list, size = 21, fill = c.muted, gap = 29) =>
  list.map((s, i) => text(x, y + i * gap, s, size, fill)).join('');
const rect = (x, y, w, h, fill, r = 14, stroke = 'none') =>
  `<rect x="${x}" y="${y}" width="${w}" height="${h}" rx="${r}" fill="${fill}" stroke="${stroke}"/>`;
const line = (x1, y1, x2, y2, color = c.line, arrow = false) =>
  `<line x1="${x1}" y1="${y1}" x2="${x2}" y2="${y2}" stroke="${color}" stroke-width="2" ${arrow ? 'marker-end="url(#arrow)"' : ''}/>`;
const label = (x, y, s) => text(x, y, s, 17, c.muted, 600, 'letter-spacing="2"');
const node = (x, y, w, h, title, body, fill = c.white, accent = c.teal) =>
  `<g class="product-node" data-x="${x}" data-y="${y}" data-width="${w}" data-height="${h}">` +
  rect(x, y, w, h, fill) +
  rect(x, y + 19, 4, h - 38, accent, 2) +
  text(x + 24, y + 39, title, 27, c.ink, 600) +
  lines(x + 24, y + 72, body, 20, c.muted, 27) +
  '</g>';
const svg = (
  w,
  h,
  title,
  desc,
  body,
) => `<svg xmlns="http://www.w3.org/2000/svg" width="${w}" height="${h}" viewBox="0 0 ${w} ${h}" role="img" aria-labelledby="title desc">
<title id="title">${esc(title)}</title><desc id="desc">${esc(desc)}</desc>
<defs><marker id="arrow" viewBox="0 0 10 10" refX="9" refY="5" markerWidth="6" markerHeight="6" orient="auto-start-reverse"><path d="M 0 0 L 10 5 L 0 10 z" fill="${c.line}"/></marker></defs>
<g font-family="Segoe UI, Arial, sans-serif">${rect(0, 0, w, h, c.paper, 0)}${body}</g></svg>\n`;

let map =
  label(60, 55, 'TRAVEL / КАРТА ПРОДУКТА') +
  text(60, 117, 'Что работает вокруг одной поездки', 48, c.ink, 600) +
  text(60, 159, 'От выбора дороги и жилья до заказа, личного плана и разговора с поддержкой.', 24, c.muted) +
  node(60, 201, 710, 108, 'Путешественник', ['Выбирает · оформляет · планирует · задаёт вопросы'], c.pale) +
  node(
    790,
    201,
    710,
    108,
    'Оператор поддержки',
    ['Читает обращение · уточняет · отвечает · решает'],
    c.bluePale,
    c.blue,
  ) +
  line(415, 311, 415, 338, c.line, true) +
  line(1145, 311, 1145, 338, c.line, true) +
  rect(60, 344, 1440, 76, c.ink) +
  text(88, 390, 'Общий веб-интерфейс', 28, c.white, 600) +
  text(478, 390, 'Angular · поиск, личные разделы и рабочее место оператора', 24, '#e0e9e7') +
  line(580, 421, 580, 449, c.line, true) +
  label(60, 476, 'TRAVEL.HOST / ШЕСТЬ ЧАСТЕЙ ОДНОГО ПРИЛОЖЕНИЯ') +
  label(1160, 476, 'ПОМОЩЬ С ТЕКСТОМ');
const modules = [
  [60, 500, 'Перелёты', ['Поиск, багаж и места', 'Заказ, отмена и возврат'], c.teal],
  [425, 500, 'Проживание', ['Сравнение предложений', 'Условия и оформление'], c.teal],
  [790, 500, 'Поезда', ['Расписание и фильтры', 'Маршрут с остановками'], c.teal],
  [60, 665, 'Поездки', ['Личный план по дням', 'Подбор прогулок'], c.blue],
  [425, 665, 'Поддержка', ['Переписка и очередь', 'Категория, приоритет'], c.blue],
  [790, 665, 'Личный доступ', ['Вход и права', 'Свои данные и заказы'], c.blue],
];
for (const [x, y, title, body, accent] of modules) map += node(x, y, 345, 145, title, body, c.white, accent);
map +=
  node(
    1160,
    500,
    340,
    180,
    'Travel.AI',
    ['Запрос → поиск рейсов', 'Пожелания → поля формы', 'Факты → объяснение тарифа'],
    c.bluePale,
    c.blue,
  ) +
  node(1160, 700, 340, 110, 'Laya → поддержка', ['Подсказка категории'], c.rustPale, c.rust) +
  text(
    60,
    850,
    'У каждой части свои правила; заказ, предложение поставщика и личный план сохраняют свой смысл.',
    23,
    c.muted,
  ) +
  line(60, 882, 1500, 882) +
  label(60, 923, 'ПОСТАВЩИКИ И ДАННЫЕ / КАКАЯ ЧАСТЬ ИХ ИСПОЛЬЗУЕТ') +
  node(60, 948, 272, 143, 'Duffel', ['Перелёты', 'Проживание · Stays'], c.pale) +
  node(352, 948, 272, 143, 'Travelpayouts', ['Перелёты', 'Переход к партнёру'], c.pale) +
  node(644, 948, 272, 143, 'Hotelbeds', ['Проживание', 'Тарифы и бронь'], c.pale) +
  node(936, 948, 272, 143, 'Яндекс', ['Расписания', 'Поезда и остановки'], c.pale) +
  node(1228, 948, 272, 143, 'Anthropic', ['Travel.AI', 'Языковая модель'], c.bluePale, c.blue) +
  rect(60, 1111, 1440, 62, c.white) +
  text(84, 1150, 'OpenStreetMap + Wikidata → подготовленные пакеты мест и пеших связей → "Поездки"', 24, c.ink) +
  label(60, 1225, 'ЧТО ПОМОГАЕТ СЕРВИСУ РАБОТАТЬ') +
  node(60, 1250, 466, 112, 'PostgreSQL', ['Заказы, планы, переписка, история'], c.white) +
  node(546, 1250, 467, 112, 'Redis', ['Кэш результатов поиска'], c.white) +
  node(1033, 1250, 467, 112, 'Keycloak', ['Вход и права доступа'], c.white) +
  node(
    60,
    1382,
    466,
    142,
    'NATS + Wolverine',
    ['Связь с AI; сохранённые задания', 'и уведомления через PostgreSQL'],
    c.white,
  ) +
  node(546, 1382, 467, 142, 'Почта', ['Уведомления по авиазаказам', 'SMTP · в локальном запуске Mailpit'], c.white) +
  node(
    1033,
    1382,
    467,
    142,
    'Наблюдение за работой',
    ['OpenTelemetry · Aspire Dashboard', 'Опционально Grafana, Loki, Tempo'],
    c.white,
  ) +
  rect(60, 1560, 1440, 77, c.ink) +
  text(84, 1607, 'Travel.AppHost / Aspire', 27, c.white, 600) +
  text(490, 1607, 'Запускает приложения и нужные им сервисы вместе', 24, '#e0e9e7') +
  text(60, 1682, 'Правила сценариев: OpenSpec · решения: ADR · проверки изменений: тесты и CI', 21, c.muted) +
  text(60, 1726, 'Схема состава проекта. Подключения и режимы демонстрации описаны в документе.', 18, c.muted) +
  text(1500, 1726, '01', 22, c.teal, 600, 'text-anchor="end"');

await writeFile(
  path.join(assets, 'travel-map.svg'),
  svg(
    1560,
    1760,
    'Travel: карта продукта',
    'Путешественник и оператор работают в Angular. Travel.Host содержит перелёты, проживание, поезда, поездки, поддержку и личный доступ. Travel.AI – отдельный процесс; Laya даёт подсказки поддержке. Ниже показаны поставщики, подготовленные данные и инфраструктура с назначением каждого компонента.',
    map,
  ),
);

let planning =
  label(48, 48, 'ПОЕЗДКИ / ОТ ПОЖЕЛАНИЙ К СВОЕМУ ПЛАНУ') +
  text(48, 108, 'Хочу три спокойных дня в Петербурге', 43, c.ink, 600) +
  text(48, 150, 'Город, даты, интересы и темп становятся основой для прогулки.', 23, c.muted);
const stages = [
  ['01', 'Параметры', ['Даты и участники', 'Архитектура, история', 'Спокойный темп']],
  ['02', 'Подбор прогулки', ['Места из пакета города', 'Пешие переходы', 'Причины выбора']],
  ['03', 'Мой выбор', ['Рассмотреть маршрут', 'Оценить нагрузку', 'Принять маршрут']],
  ['04', 'Мои дни', ['Сохранить в план', 'Перенести занятие', 'Добавить свои заметки']],
];
for (let i = 0; i < stages.length; i++) {
  const [n, title, body] = stages[i];
  const x = 48 + i * 301;
  planning += text(x, 215, n, 28, c.teal, 600) + node(x, 239, 276, 188, title, body, i === 2 ? c.pale : c.white);
  if (i < 3) planning += line(x + 279, 330, x + 298, 330, c.line, true);
}
planning += text(
  48,
  480,
  'Подбор учитывает места и темп. Сохранение и дальнейшие правки – решения путешественника.',
  22,
  c.muted,
);
await writeFile(
  path.join(assets, 'trip-planning.svg'),
  svg(
    1250,
    524,
    'Как собирается план поездки',
    'Четыре шага: подтверждённые параметры, подбор прогулки по данным и правилам, просмотр и принятие человеком, сохранённый редактируемый план.',
    planning,
  ),
);

const cancellation =
  label(48, 48, 'АВИАЗАКАЗ / ЧТО ПРОИСХОДИТ ПРИ ОТМЕНЕ') +
  text(48, 107, 'У каждого шага – своё подтверждение', 43, c.ink, 600) +
  node(
    48,
    154,
    353,
    171,
    'Условия и согласие',
    ['Весь заказ, сумма, срок', 'Путешественник принимает', 'конкретное предложение'],
    c.white,
  ) +
  node(
    447,
    154,
    353,
    171,
    'Отмена заказа',
    ['Результат поставщика', 'Сохранённая история', 'Обновление страницы'],
    c.pale,
  ) +
  node(
    846,
    154,
    353,
    171,
    'Если результат неясен',
    ['Проверка фактов', 'Ручной разбор', 'Сохранение подтверждений'],
    c.rustPale,
    c.rust,
  ) +
  line(405, 235, 439, 235, c.line, true) +
  label(48, 377, 'ПОСЛЕ ПОДТВЕРЖДЁННОЙ ОТМЕНЫ / ДВЕ ИСТОРИИ РАСЧЁТОВ') +
  node(
    48,
    405,
    553,
    171,
    'Расчёты с поставщиком',
    ['Сколько положено вернуть', 'Куда поступают средства поставщика', 'Какие подтверждения получены'],
    c.white,
  ) +
  node(
    649,
    405,
    553,
    171,
    'Возврат клиенту',
    ['Основание и точная сумма', 'Отправка на исходный способ оплаты', 'Результат попытки возврата'],
    c.bluePale,
    c.blue,
  ) +
  text(48, 628, 'При подходящих условиях сервис финансирует возврат клиенту до расчёта с поставщиком.', 22, c.muted);
await writeFile(
  path.join(assets, 'cancellation.svg'),
  svg(
    1250,
    676,
    'Отмена и возврат: отдельные подтверждения',
    'Согласие на условия и результат отмены. После подтверждённой отмены расчёты поставщика и возврат клиенту учитываются отдельно. Допустимый возврат финансируется агентством без ожидания расчётов поставщика. Неопределённый исход требует проверки фактов и при необходимости ручного разбора.',
    cancellation,
  ),
);

const proofline =
  label(48, 48, 'TRAVEL + PROOFLINE / ДВА САМОСТОЯТЕЛЬНЫХ ПРОЕКТА') +
  text(48, 108, 'Что произошло – и откуда мы это знаем', 43, c.ink, 600) +
  rect(48, 155, 553, 247, c.pale) +
  rect(649, 155, 553, 247, c.bluePale) +
  text(76, 203, 'Travel', 35, c.teal, 600) +
  text(677, 203, 'Proofline', 35, c.blue, 600) +
  text(76, 242, 'Действия по заказу', 26, c.ink, 600) +
  text(677, 242, 'Разбор обращения', 26, c.ink, 600) +
  lines(
    76,
    281,
    ['Выбранное предложение и условия', 'Согласие, оформление, отмена', 'Статус, расчёты и переписка'],
    23,
    c.muted,
    34,
  ) +
  lines(
    677,
    281,
    ['Источники, факты и хронология', 'Цитаты и недостающие подтверждения', 'Правка и решение оператора'],
    23,
    c.muted,
    34,
  ) +
  rect(48, 437, 1154, 130, c.white) +
  text(76, 480, '"Согласен, если номер тихий и окна во двор".', 29, c.ink, 500) +
  text(76, 523, 'Два условия, которые нужно сохранить в разборе и проверить перед следующим решением.', 22, c.muted) +
  text(48, 617, 'Общая тема: сохранять факты, видеть их основания и понимать, кто принял решение.', 22, c.muted);
await writeFile(
  path.join(assets, 'proofline.svg'),
  svg(
    1250,
    661,
    'Travel и Proofline: действия и доказательства',
    'Два самостоятельных приложения. Travel сохраняет действия по заказу и переписку. Proofline помогает проверить факты по источникам, увидеть условия и недостающие подтверждения, исправить разбор и записать решение оператора.',
    proofline,
  ),
);

const markdown = await readFile(path.join(dir, 'travel.ru.md'), 'utf8');
const publicationFiles = [
  'travel.ru.md',
  ...['travel-map', 'trip-planning', 'cancellation', 'proofline'].map((name) => `assets/${name}.svg`),
];
const publicationEntries = await Promise.all(
  publicationFiles.map(async (name) => ({
    path: name,
    sha256: createHash('sha256')
      .update((await readFile(path.join(dir, name), 'utf8')).replace(/\r\n?/g, '\n'))
      .digest('hex'),
  })),
);
const sourceParagraphs = markdown.trim().split(/\r?\n\s*\r?\n/);
await writeFile(
  path.join(dir, 'publication.json'),
  `${JSON.stringify(
    {
      schemaVersion: 1,
      id: 'travel',
      title: sourceParagraphs[0].replace(/^# /, ''),
      description: sourceParagraphs[1],
      document: publicationEntries[0],
      assets: publicationEntries.slice(1),
    },
    null,
    2,
  )}\n`,
);
// Intentionally narrow Markdown renderer for this document: paragraphs, headings,
// one blockquote, emphasis, links and figures. Reject unsupported block syntax.
const slug = (s) =>
  s
    .toLowerCase()
    .replace(/[^\p{L}\p{N}]+/gu, '-')
    .replace(/^-|-$/g, '');
function url(href) {
  if (/^https:\/\//.test(href)) return href;
  if (href.startsWith('assets/')) return href;
  const relative = path.relative(root, path.resolve(dir, href)).split(path.sep).join('/');
  const kind = relative.endsWith('/') || !path.extname(relative) ? 'tree' : 'blob';
  return `https://github.com/svasorcery/travel-agency/${kind}/dev/${relative}`;
}
function inline(s) {
  return esc(s)
    .replace(/\[([^\]]+)\]\(([^)]+)\)/g, (_, label, href) => `<a href="${esc(url(href))}">${label}</a>`)
    .replace(/\*\*([^*]+)\*\*/g, '<strong>$1</strong>')
    .replace(/\*([^*]+)\*/g, '<em>$1</em>');
}
const toc = [];
const blocks = markdown.trim().split(/\r?\n\s*\r?\n/);
let chapter = 0;
const content = [];
for (const block of blocks) {
  if (block.startsWith('# ')) continue;
  if (block.startsWith('## ')) {
    const title = block.slice(3);
    const id = slug(title);
    toc.push({ title, id });
    chapter++;
    content.push(
      `<h2 id="${id}"><span class="chapter">${String(chapter).padStart(2, '0')}</span>${inline(title)}</h2>`,
    );
  } else if (block.startsWith('> ')) {
    content.push(
      `<aside class="demo-note" aria-label="Disclaimer: особенности демонстрации"><p>${inline(block.replace(/^> ?/gm, '').replaceAll('\n', ' '))}</p></aside>`,
    );
  } else if (/^!\[/.test(block)) {
    const match = block.match(/^!\[([^\]]+)\]\(([^)]+)\)$/);
    if (!match) throw new Error('Unsupported image block');
    const data = (await readFile(path.join(dir, match[2]))).toString('base64');
    content.push(
      `<figure class="${match[2].includes('travel-map') ? 'map' : 'illustration'}"><img src="data:image/svg+xml;base64,${data}" alt="${esc(match[1])}" loading="lazy"/><figcaption>${esc(match[1])}</figcaption></figure>`,
    );
  } else if (block === '---') content.push('<hr/>');
  else {
    if (/^(?:[-*] |\d+\. |```|\|)/.test(block)) throw new Error('Unsupported Markdown block');
    content.push(`<p>${inline(block.replaceAll('\n', ' '))}</p>`);
  }
}
const [tagline, ...rest] = content;
const html = `<!doctype html>
<html lang="ru"><head><meta charset="utf-8"/><meta name="viewport" content="width=device-width, initial-scale=1"/>
<title>Travel – из чего складывается поездка</title>
<meta name="description" content="Поиск билетов и жилья, личный план поездки, заказы и поддержка. Иллюстрированная история Travel и связь с Proofline."/>
<style>
:root{color-scheme:light;--paper:#f6f3eb;--ink:#142b35;--muted:#4e6367;--teal:#14655c;--line:#d3d8ce}
*{box-sizing:border-box}html{scroll-behavior:smooth;scroll-padding-top:28px}body{margin:0;background:var(--paper);color:var(--ink);font-family:Georgia,'Times New Roman',serif;font-size:21px;line-height:1.72}a{color:var(--teal);text-decoration-thickness:1px;text-underline-offset:4px}a:hover{text-decoration-thickness:2px}a:focus-visible,summary:focus-visible{outline:3px solid var(--teal);outline-offset:5px}header,main,footer{max-width:1180px;margin:auto;padding-left:44px;padding-right:44px}
header{padding-top:48px}.masthead{display:flex;justify-content:space-between;gap:24px;align-items:center;font:600 13px/1.4 'Segoe UI',Arial,sans-serif;letter-spacing:1.8px;text-transform:uppercase;padding-bottom:22px;border-bottom:1px solid var(--line)}.masthead a{text-decoration:none}.hero{padding:72px 0 52px;max-width:900px}.eyebrow{font:600 14px/1.5 'Segoe UI',Arial,sans-serif;color:var(--teal);letter-spacing:2px;text-transform:uppercase}.hero h1{font-size:clamp(48px,6.1vw,79px);font-weight:400;line-height:1.1;letter-spacing:-2.5px;margin:25px 0 28px}:where(.hero) p{font-size:27px;line-height:1.5;max-width:760px;color:var(--muted);margin:0}.contents{border-top:1px solid var(--line);border-bottom:1px solid var(--line);padding:18px 0;margin-bottom:58px;font:16px/1.7 'Segoe UI',Arial,sans-serif}.contents summary{cursor:pointer;font-weight:600}.contents ol{columns:2;column-gap:40px;list-style:decimal-leading-zero;padding-left:28px;margin:20px 0 6px}.contents li{break-inside:avoid;padding:4px 0}
main>p,main>h2,main>aside,main>hr{max-width:760px;margin-left:auto;margin-right:auto}main>p{margin-top:0;margin-bottom:24px}main>p:first-child{font-size:25px;line-height:1.6}strong{font-weight:700}.demo-note{padding:24px 28px;border-left:3px solid var(--teal);background:#e8ede4;font:16px/1.75 'Segoe UI',Arial,sans-serif;margin-top:40px;margin-bottom:68px}.demo-note p{margin:0}.demo-note strong:first-child{display:block;font-size:18px;margin-bottom:9px}main>h2{font-size:40px;font-weight:400;line-height:1.22;letter-spacing:-.8px;padding-top:38px;margin-top:48px;margin-bottom:28px;break-after:avoid}.chapter{display:block;font:600 13px/1.5 'Segoe UI',Arial,sans-serif;letter-spacing:2px;color:var(--teal);margin-bottom:14px}.illustration,.map{margin:42px 0 34px}figure img{display:block;width:100%;height:auto}.map{max-width:1000px;margin-left:auto;margin-right:auto}figcaption{font:14px/1.6 'Segoe UI',Arial,sans-serif;color:var(--muted);max-width:760px;margin:16px auto 0}main>p:has(>em:only-child){font:15px/1.6 'Segoe UI',Arial,sans-serif;color:var(--muted)}main>hr{border:0;border-top:1px solid var(--line);margin-top:60px;margin-bottom:32px}main>p:last-child{font:16px/1.9 'Segoe UI',Arial,sans-serif}footer{padding-top:38px;padding-bottom:46px;font:13px/1.5 'Segoe UI',Arial,sans-serif;color:var(--muted)}footer div{border-top:1px solid var(--line);padding-top:20px;display:flex;justify-content:space-between;gap:20px}
@media(max-width:700px){header,main,footer{padding-left:22px;padding-right:22px}header{padding-top:26px}.masthead{font-size:10px;letter-spacing:1px}.hero{padding:44px 0 36px}.hero h1{letter-spacing:-1.5px}:where(.hero) p{font-size:23px}.contents{margin-bottom:36px}.contents ol{columns:1}body{font-size:19px;line-height:1.7}main>p:first-child{font-size:22px}.demo-note{padding:20px;font-size:15px;margin-bottom:44px}main>h2{font-size:32px;padding-top:20px;margin-top:36px}.illustration,.map{margin-left:-10px;margin-right:-10px}figcaption{padding:0 10px;font-size:12px}}
@media(prefers-reduced-motion:reduce){html{scroll-behavior:auto}}@media print{body{background:white;font-size:12pt;line-height:1.55}header,main,footer{max-width:none;padding-left:0;padding-right:0}.hero{padding:28px 0}.hero h1{font-size:42pt}:where(.hero) p{font-size:17pt}.masthead{font-size:9pt}.contents{display:none}main>p:first-child{font-size:15pt}main>h2{font-size:25pt;padding-top:12px;margin-top:24px}.chapter{font-size:9pt}.demo-note{font-size:10pt;margin:24px auto;padding:16px}figure{break-inside:avoid}.map{break-before:page;break-after:page;max-width:160mm}.illustration{margin:22px 0}figcaption{font-size:9pt}a{color:inherit}footer{padding-bottom:0}@page{size:A4;margin:18mm}}
</style></head><body>
<header id="top"><div class="masthead"><a href="https://github.com/svasorcery/travel-agency">Travel / авторский проект</a><span>Владимир Синявский · ноябрь 2026</span></div><div class="hero"><div class="eyebrow">История продукта</div><h1>Из чего складывается поездка</h1>${tagline}</div>
<details class="contents"><summary>Открыть оглавление · ${toc.length} глав</summary><ol>${toc.map(({ title, id }) => `<li><a href="#${id}">${esc(title)}</a></li>`).join('')}</ol></details></header>
<main>${rest.join('\n')}</main>
<footer><div><span>Travel · продукт, решения, люди</span><a href="#top">К началу</a></div></footer>
</body></html>\n`;
await writeFile(path.join(dir, 'travel.ru.html'), html);
console.log('Built four SVG illustrations and docs/product/travel.ru.html from travel.ru.md');

if (process.argv.includes('--png')) {
  const { chromium } = await import('playwright');
  const browser = await chromium.launch({ headless: true });
  try {
    const page = await browser.newPage({ viewport: { width: 1560, height: 1760 }, deviceScaleFactor: 2 });
    for (const name of ['travel-map', 'trip-planning', 'cancellation', 'proofline']) {
      await page.setContent(await readFile(path.join(assets, `${name}.svg`), 'utf8'));
      await page.locator('svg').screenshot({ path: path.join(assets, `${name}.png`) });
    }
  } finally {
    await browser.close();
  }
  console.log('Exported four PNG illustrations at 2x resolution');
}
