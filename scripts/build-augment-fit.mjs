import fs from 'node:fs';
import path from 'node:path';
import https from 'node:https';

const ROOT = path.resolve(import.meta.dirname, '..');
const DATA_DIR = path.join(ROOT, 'data');
const CACHE_DIR = path.join(ROOT, 'data', 'cache', 'champions');
const DT_STAT_DATE = process.env.ARAM_DT_STAT_DATE || '20261105';
const DATA_VERSION = '16.21.5';
const DATA_ORIGIN = 'https://data.dtodo.cn';
const API = `${DATA_ORIGIN}/api/client/v1/data/${DATA_VERSION}`;

fs.mkdirSync(CACHE_DIR, { recursive: true });

function getJson(url, timeoutMs = 20000) {
  return new Promise((resolve, reject) => {
    const req = https.get(url, { headers: { 'user-agent': 'aram-overlay/0.1' } }, (res) => {
      if (res.statusCode >= 300 && res.statusCode < 400 && res.headers.location) {
        res.resume();
        getJson(new URL(res.headers.location, url).toString(), timeoutMs).then(resolve, reject);
        return;
      }
      if (res.statusCode !== 200) {
        res.resume();
        reject(new Error(`HTTP ${res.statusCode} ${url}`));
        return;
      }
      const chunks = [];
      res.on('data', (d) => chunks.push(d));
      res.on('end', () => {
        try {
          resolve(JSON.parse(Buffer.concat(chunks).toString('utf8')));
        } catch (error) {
          reject(error);
        }
      });
    });
    req.setTimeout(timeoutMs, () => req.destroy(new Error(`timeout ${url}`)));
    req.on('error', reject);
  });
}

function parse101AugmentList(payload) {
  const field = payload?.data?._fieldValues?.R15381;
  if (!field) throw new Error('101 augment field R15381 missing');
  const parsed = JSON.parse(field);
  const list = String(parsed.augmentlist || '').split('#').filter(Boolean);
  const map = new Map();
  for (const entry of list) {
    const parts = entry.split('_');
    if (parts.length < 6) continue;
    const augmentId = Number(parts[0]);
    const pickRate = Number(parts[2]);
    const winRate = Number(parts[5]);
    if (!Number.isFinite(augmentId) || !Number.isFinite(winRate)) continue;
    map.set(augmentId, { augmentId, pickRate, winRate });
  }
  return { date: parsed.dtstatdate || DT_STAT_DATE, map };
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

function scoreOf(overallWinRate, championPickRate, overallPickRate, championPickTotal, overallPickTotal) {
  const championShare = championPickTotal > 0 ? championPickRate / championPickTotal : 0;
  const overallShare = overallPickTotal > 0 ? overallPickRate / overallPickTotal : 0;
  const lift = (championShare + 0.0001) / (overallShare + 0.0001);
  const raw = 50 + (overallWinRate - 0.5) * 120 + (Math.log2(lift) * 8);
  return Math.round(clamp(raw, 0, 100));
}

async function main() {
  console.log('fetching augment base...');
  const base = await getJson(`${API}/augments.json`);
  const augmentById = new Map((base.data || []).map((a) => [Number(a.id), {
    id: Number(a.id),
    name: a.name,
    key: a.key,
    rarity: a.rarity,
    rarityName: a.rarityName,
    rarityDisplayName: a.rarityDisplayName,
    iconUrl: a.iconUrl,
  }]));

  console.log('fetching 101 overall augment stats...');
  const rankUrl = `https://mlol.qt.qq.com/go/battle_info/odp_proxy/fuwen_aram_rune_rank_v2?dtstatdate=${DT_STAT_DATE}&augmentid_level=255`;
  const rankPayload = await getJson(rankUrl);
  const overall = parse101AugmentList(rankPayload);
  const overallPickTotal = [...overall.map.values()].reduce((sum, row) => sum + row.pickRate, 0);
  console.log(`overall augments: ${overall.map.size}, date=${overall.date}, pickTotal=${overallPickTotal.toFixed(2)}`);

  const championIndex = JSON.parse(fs.readFileSync(path.join(ROOT, 'data', 'latest.json'), 'utf8')).champions || [];
  const champions = {};
  let done = 0;

  async function one(champion) {
    const id = Number(champion.championId);
    const cachePath = path.join(CACHE_DIR, `${id}.json`);
    let detail;
    if (fs.existsSync(cachePath)) {
      detail = JSON.parse(fs.readFileSync(cachePath, 'utf8'));
    } else {
      detail = await getJson(`${API}/champions/${id}.json`);
      fs.writeFileSync(cachePath, JSON.stringify(detail));
    }

    const rows = [];
    const championPickTotal = (detail.augments || []).reduce((sum, item) => sum + Number(item.stats?.pickRate || 0), 0);
    for (const item of detail.augments || []) {
      const augmentId = Number(item.id);
      const meta = augmentById.get(augmentId);
      const overallRow = overall.map.get(augmentId);
      if (!meta || !overallRow) continue;
      const championPickRate = Number(item.stats?.pickRate || 0);
      const championShare = championPickTotal > 0 ? championPickRate / championPickTotal : 0;
      const overallShare = overallPickTotal > 0 ? overallRow.pickRate / overallPickTotal : 0;
      const score = scoreOf(overallRow.winRate, championPickRate, overallRow.pickRate, championPickTotal, overallPickTotal);
      rows.push({
        augmentId,
        name: meta.name,
        rarity: meta.rarityName,
        iconUrl: meta.iconUrl,
        pickRate: championPickRate,
        pickShare: Number(championShare.toFixed(4)),
        cnWinRate: overallRow.winRate,
        cnPickRate: overallRow.pickRate,
        lift: Number(((championShare + 0.0001) / (overallShare + 0.0001)).toFixed(3)),
        score,
        rating: rating(score),
      });
    }
    rows.sort((a, b) => b.score - a.score || b.pickRate - a.pickRate);
    champions[id] = rows;
    done += 1;
    if (done % 20 === 0) console.log(`champions ${done}/${championIndex.length}`);
  }

  const queue = championIndex.slice();
  const workers = Array.from({ length: 8 }, async () => {
    while (queue.length) {
      const champion = queue.shift();
      try {
        await one(champion);
      } catch (error) {
        console.warn(`champion ${champion.championId} failed: ${error.message}`);
        champions[Number(champion.championId)] = [];
      }
    }
  });
  await Promise.all(workers);

  const output = {
    schema: 'aram-overlay-augment-fit-v1',
    source: '101 overall augment stats (CN) + ARAMGG champion augment pick rates (CN)',
    note: 'Adaptation score is self-research, not Hexdata hidden score. Per-champion winRate from ARAMGG is WORLD upload and was intentionally not used.',
    dataVersion: DATA_VERSION,
    date: overall.date,
    generatedAt: new Date().toISOString(),
    augmentCount: augmentById.size,
    championCount: Object.keys(champions).length,
    champions,
  };
  fs.writeFileSync(path.join(DATA_DIR, 'augment-fit.json'), JSON.stringify(output));
  console.log(`wrote data/augment-fit.json (${Object.keys(champions).length} champions)`);
}

main().catch((error) => {
  console.error(error);
  process.exit(1);
});
