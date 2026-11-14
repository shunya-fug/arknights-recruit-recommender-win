// compare-operators.mjsの判定ロジックのテスト(node --testで実行。ci.ymlからも実行する)。
// 自動リリースしてよいか(additions_only)を決める部分なので、変更・削除を取りこぼさないことを確認する。
import assert from 'node:assert/strict';
import { test } from 'node:test';
import { compareOperators } from './compare-operators.mjs';

const robot = { Name: 'Castle-3', Rarity: 1, Tags: ['前衛タイプ', '近距離', 'ロボット', '支援'] };
const vanguard = { Name: 'フェン', Rarity: 3, Tags: ['先鋒タイプ', '近距離', 'COST回復'] };
const sniper = { Name: 'クルース', Rarity: 3, Tags: ['狙撃タイプ', '遠距離', '火力'] };

test('同じデータなら変更なし', () => {
  // JSONを読み直した別のオブジェクトでも、中身が同じなら同一とみなす。
  const copy = JSON.parse(JSON.stringify([robot, vanguard]));
  assert.deepEqual(compareOperators([robot, vanguard], copy), {
    changed: false,
    additions_only: true,
    added: '',
    removed_or_changed: '',
  });
});

test('追加だけなら自動リリースの対象', () => {
  assert.deepEqual(compareOperators([robot], [robot, vanguard, sniper]), {
    changed: true,
    additions_only: true,
    added: 'フェン, クルース',
    removed_or_changed: '',
  });
});

test('既存オペレーターのタグが変わったら対象外', () => {
  const changed = { ...vanguard, Tags: [...vanguard.Tags, '牽制'] };
  const result = compareOperators([robot, vanguard], [robot, changed]);
  assert.equal(result.changed, true);
  assert.equal(result.additions_only, false);
  assert.equal(result.removed_or_changed, 'フェン');
});

test('既存オペレーターのレアリティが変わったら対象外', () => {
  const changed = { ...sniper, Rarity: 4 };
  assert.equal(compareOperators([sniper], [changed]).additions_only, false);
});

test('削除があれば、追加と同時でも対象外', () => {
  const result = compareOperators([robot, vanguard], [robot, sniper]);
  assert.equal(result.additions_only, false);
  assert.equal(result.added, 'クルース');
  assert.equal(result.removed_or_changed, 'フェン');
});
