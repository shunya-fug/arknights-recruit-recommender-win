// オペレーターデータ(operators.{locale}.json)の新旧を、オペレーター単位で「追加」と「削除・変更」に分ける
// (update-operator-data.ymlから使う)。改行コードなどの見かけの差分では変更ありとみなさないよう、
// テキストの差分ではなくJSONの中身で比較する。既存オペレーターのタグやレアリティが変わった場合は、
// 新しい側は「追加」、古い側は「削除・変更」の両方に現れるため、自動リリースの対象外になる。
//
// 使い方: node compare-operators.mjs <旧JSON> <新JSON>
// GITHUB_OUTPUTが設定されていれば、結果をステップの出力として書き出す。
// 判定ロジック(compareOperators)はcompare-operators.test.mjsでテストしている。
import { appendFileSync, readFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';

const key = (operator) => JSON.stringify(operator);

/** 新旧のオペレーター配列を比べ、ワークフローのステップ出力にする値を返す。 */
export function compareOperators(oldOperators, newOperators) {
  const oldKeys = new Set(oldOperators.map(key));
  const newKeys = new Set(newOperators.map(key));

  const added = newOperators.filter((o) => !oldKeys.has(key(o))).map((o) => o.Name);
  const removedOrChanged = oldOperators.filter((o) => !newKeys.has(key(o))).map((o) => o.Name);

  return {
    changed: added.length + removedOrChanged.length > 0,
    additions_only: removedOrChanged.length === 0,
    added: added.join(', '),
    removed_or_changed: removedOrChanged.join(', '),
  };
}

const load = (path) => JSON.parse(readFileSync(path, 'utf8').replace(/^﻿/, ''));

// テストからimportされた時は実行しない(コマンドとして直接実行された時だけ動かす)。
if (process.argv[1] === fileURLToPath(import.meta.url)) {
  const [oldPath, newPath] = process.argv.slice(2);
  if (!oldPath || !newPath) {
    console.error('usage: node compare-operators.mjs <old.json> <new.json>');
    process.exit(2);
  }

  const outputs = compareOperators(load(oldPath), load(newPath));
  const text = Object.entries(outputs).map(([name, value]) => `${name}=${value}`).join('\n') + '\n';
  process.stdout.write(text);
  if (process.env.GITHUB_OUTPUT) {
    appendFileSync(process.env.GITHUB_OUTPUT, text);
  }
}
