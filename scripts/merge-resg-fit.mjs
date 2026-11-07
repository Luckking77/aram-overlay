import fs from 'node:fs';
import path from 'node:path';
import https from 'node:https';

const ROOT = path.resolve(import.meta.dirname, '..');
const DATA_DIR = path.join(ROOT, 'data');
const CACHE_DIR = path.join(DATA_DIR, 'cache', 'resg-champions');
const RESG_VERSION = process.env.RESG_VERSION || '16.22';
const RESG_API = `https://cdn.resg.top/api/v1/versions/${RESG_VERSION}`;
const PRIOR_GAMES = 300;

fs.mkdirSync(CACHE_DIR, { recursive: true });

function getText(url, timeoutMs = 20000) {
  return new Promise((resolve, reject) => {
    const req = https.get(url, { headers: { 'user-agent': 'aram-overlay/0.1' } }, (res) => {
      if (res.statusCode >= 300 && res.statusCode < 400 && res.headers.location) {
        res.resume();
        getText(new URL(res.headers.location, url).toString(), timeoutMs).then(resolve, reject);
        return;
      }
      if (res.statusCode !== 200) {
        res.resume();
        reject(new Error(`HTTP ${res.statusCode} ${url}`));
        return;
      }
      const chunks = [];
      res.on('data', (d) => chunks.push(d));
      res.on('end', () => resolve(Buffer.concat(chunks).toString('utf8')));
    });
    req.setTimeout(timeoutMs, () => req.destroy(new Error(`timeout ${url}`)));
    req.on('error', reject);
  });
}

async function getModule(url) {
  const text = await getText(url);
  return JSON.parse(text.replace(/^export\s+default\s+/, ''));
}

function clamp(value, min, max) {
  return Math.min(max, Math.max(min, value));
}

function rating(score) {
  if (score >= 90) return '夯';
  if (score >= 75) return '顶级';
  if (score >= 60) return '人上人';
  if (score >= 35) return 'NPC';
  return '拉完了';
}

function parseRa(rows) {
  const map = new Map();
  for (const raw of rows || []) {
    if (!Array.isArray(raw) || raw.length < 4) continue;
    const augmentId = Number(raw[0]);
    const games = Number(raw[2]);
    const wins = Number(raw[3]);
    if (!Number.isFinite(augmentId) || !Number.isFinite(games) || !Number.isFinite(wins) || games <= 0 || wins < 0) continue;
    const winRate = wins / games;
    if (winRate < 0.1 || winRate > 1.0) continue;
    map.set(augmentId, { games, wins, winRate });
  }
  return map;
}

async function main() {
  const fitPath = path.join(DATA_DIR, 'augment-fit.json');
  const fit = JSON.parse(fs.readFileSync(fitPath, 'utf8'));

  console.log('fetching RESG augment overview...');
  const augmentModule = await getModule(`${RESG_API}/augments.js`);
  const augmentOverall = new Map();
  for (const item of augmentModule.items || []) {
    const id = Number(item.id);
    if (Number.isFinite(id) && typeof item.wr === 'number') {
      augmentOverall.set(id, item);
    }
  }
  console.log(`RESG augments: ${augmentOverall.size}, version=${RESG_VERSION}`);

  const championIds = Object.keys(fit.champions || {}).map(Number).filter((id) => id > 0);
  let done = 0;

  async function one(championId) {
    const cachePath = path.join(CACHE_DIR, `${championId}.js`);
    let moduleJson;
    if (fs.existsSync(cachePath)) {
      moduleJson = JSON.parse(fs.readFileSync(cachePath, 'utf8'));
    } else {
      const text = await getText(`${RESG_API}/champions/${championId}.js`);
      moduleJson = JSON.parse(text.replace(/^export\s+default\s+/, ''));
      fs.writeFileSync(cachePath, JSON.stringify(moduleJson));
    }

    const ra = parseRa(moduleJson.ra);
    const rows = fit.champions[championId] || [];
    for (const row of rows) {
      const augmentId = Number(row.augmentId);
      const resg = ra.get(augmentId);
      const overall = augmentOverall.get(augmentId);
      const prior = overall && typeof overall.wr === 'number'
        ? overall.wr
        : (typeof row.cnWinRate === 'number' ? row.cnWinRate : 0.5);
      row.priorWinRate = prior;
      row.resgOverallWinRate = overall ? overall.wr : null;
      row.resgOverallGames = overall ? overall.tm : null;
      row.validationDeltaPp = overall ? Number(((prior - row.cnWinRate) * 100).toFixed(2)) : null;

      if (resg) {
        const shrunk = (resg.wins + prior * PRIOR_GAMES) / (resg.games + PRIOR_GAMES);
        const score = Math.round(clamp(50 + (shrunk - 0.5) * 200, 0, 100));
        row.resgWinRate = Number((resg.winRate * 100).toFixed(2));
        row.resgGames = resg.games;
        row.resgWins = resg.wins;
        row.shrunkWinRate = Number((shrunk * 100).toFixed(2));
        row.score = score;
        row.rating = rating(score);
        row.source = 'resg-champion-augment';
        row.confidence = resg.games >= 1000 ? '高' : (resg.games >= 300 ? '中' : '低');
      } else {
        row.resgWinRate = null;
        row.resgGames = 0;
        row.resgWins = 0;
        row.shrunkWinRate = null;
        row.source = '101+pick-estimate';
        row.confidence = '低';
      }
    }
    rows.sort((a, b) => b.score - a.score || b.pickRate - a.pickRate);
    fit.champions[championId] = rows;
    done++;
    if (done % 20 === 0) console.log(`RESG champions ${done}/${championIds.length}`);
  }

  const queue = championIds.slice();
  await Promise.all(Array.from({ length: 8 }, async () => {
    while (queue.length) {
      const id = queue.shift();
      try {
        await one(id);
      } catch (error) {
        console.warn(`RESG champion ${id} failed: ${error.message}`);
      }
    }
  }));

  fit.schema = 'aram-overlay-augment-fit-v2';
  fit.source = `101 overall augment stats (CN) + ARAMGG champion pick rates (CN) + RESG ${RESG_VERSION} champion×augment samples`;
  fit.note = 'Primary champion×augment win rate is RESG sampled data, shrunk toward RESG overall win rate; 101 is retained as overall prior/validation. This is an adaptation score, not Hexdata hidden score.';
  fit.resgVersion = RESG_VERSION;
  fit.priorGames = PRIOR_GAMES;
  fit.generatedAt = new Date().toISOString();
  fit.augmentCount = Object.keys(fit.champions || {}).reduce((max, id) => Math.max(max, fit.champions[id].length), 0);
  fs.writeFileSync(fitPath, JSON.stringify(fit));
  console.log(`merged RESG into data/augment-fit.json (${championIds.length} champions)`);
}

main().catch((error) => {
  console.error(error);
  process.exit(1);
});
