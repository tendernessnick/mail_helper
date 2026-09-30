# -*- coding: utf-8 -*-
"""S13-A 只读统计：分类差距分析（仅聚合输出，不导出任何邮件内容）。"""
import sqlite3, sys, io, collections, re

sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding="utf-8")
DB = r"C:\Users\92145\AppData\Roaming\MailHelper\mailhelper.db"
con = sqlite3.connect(f"file:{DB}?mode=ro", uri=True)  # 只读
cur = con.cursor()

def q(sql, args=()):
    return cur.execute(sql, args).fetchall()

print("== 表结构 ==")
for (name,) in q("SELECT name FROM sqlite_master WHERE type='table' ORDER BY name"):
    print(" ", name)

total = q("SELECT COUNT(*) FROM messages")[0][0]
print(f"\n== 总量 ==\nmessages={total}")

print("\n== 分类来源分布 ==")
for r in q("SELECT classified_by, COUNT(*) FROM messages GROUP BY 1"):
    print(f"  {r[0]}: {r[1]}")

print("\n== 类别分布 ==")
for r in q("SELECT category, importance, COUNT(*) FROM messages GROUP BY 1,2 ORDER BY 3 DESC"):
    print(f"  cat={r[0]} imp={r[1]}: {r[2]}")

print("\n== 待确认（classified_by='rule' 且 confidence<0.55 或 pending）==")
pending = q("SELECT from_address, subject FROM messages WHERE classified_at_utc IS NULL OR confidence IS NULL OR confidence < 0.55")
print(f"  待确认数≈{len(pending)}")
dom = collections.Counter()
words = collections.Counter()
for addr, subj in pending:
    d = (addr or "").split("@")[-1].lower().strip()
    if d: dom[d] += 1
    for w in re.findall(r"[A-Za-z][A-Za-z&\-']{2,}", subj or ""):
        words[w.lower()] += 1
print("\n== 待确认·发件域 Top25 ==")
for d, c in dom.most_common(25):
    print(f"  {c:4d}  {d}")
print("\n== 待确认·主题高频词 Top45 ==")
for w, c in words.most_common(45):
    if c >= 2: print(f"  {c:4d}  {w}")

print("\n== 已分类·发件域 Top15（对照）==")
dom2 = collections.Counter()
for (addr,) in q("SELECT from_address FROM messages WHERE classified_at_utc IS NOT NULL AND confidence >= 0.55"):
    d = (addr or "").split("@")[-1].lower().strip()
    if d: dom2[d] += 1
for d, c in dom2.most_common(15):
    print(f"  {c:4d}  {d}")
con.close()
