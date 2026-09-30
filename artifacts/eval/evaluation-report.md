# 分类器评估报告（评估集，只测不改）

- 样本数: 247
- 类别准确率: 98.79%（达标线 ≥85%）
- P0 召回率: 100.00%（16/16，达标线 ≥95%）
- P0 误报率: 0.00%（达标线 ≤10%）
- 待确认率: 10.53%（上限 ≤30%；健康区间下限 15% 为指示性，D-39）

## 混淆矩阵（行=真实，列=预测）

| 真实\预测 | Course | Career | Admin | Finance | Announce | Subscription | Other |
| --- | --- | --- | --- | --- | --- | --- | --- |
| Course | 65 | 0 | 0 | 0 | 0 | 0 | 0 |
| Career | 0 | 31 | 0 | 0 | 0 | 0 | 0 |
| Admin | 0 | 0 | 32 | 0 | 0 | 0 | 0 |
| Finance | 0 | 0 | 0 | 26 | 0 | 0 | 3 |
| Announce | 0 | 0 | 0 | 0 | 31 | 0 | 0 |
| Subscription | 0 | 0 | 0 | 0 | 0 | 36 | 0 |
| Other | 0 | 0 | 0 | 0 | 0 | 0 | 23 |
## 误分类/低置信明细（S13 规则迭代诊断；评估集为合成样本，无隐私内容）

- Finance→Other conf=0.29 Outstanding fee notice
- Finance→Other conf=0.29 账户余额通知
- Finance→Other conf=0.29 Payment due for Semester 16
- Other→Other conf=0.00 Lunch on Friday?
- Other→Other conf=0.00 周末爬山吗
- Other→Other conf=0.00 Re: quick question about the lab
- Other→Other conf=0.00 Lunch on Friday?
- Other→Other conf=0.00 文件已收到
- Other→Other conf=0.00 Thanks for the notes
- Other→Other conf=0.00 Re: quick question about the lab
- Other→Other conf=0.00 Thanks for the notes
- Other→Other conf=0.00 Thanks for the notes
- Other→Other conf=0.00 周末爬山吗
- Other→Other conf=0.00 文件已收到
- Other→Other conf=0.00 周末爬山吗
- Other→Other conf=0.00 周末爬山吗
- Other→Other conf=0.00 周末爬山吗
- Other→Other conf=0.00 周末爬山吗
- Other→Other conf=0.00 Lunch on Friday?
- Other→Other conf=0.00 文件已收到
- Other→Other conf=0.00 文件已收到
- Other→Other conf=0.00 Lunch on Friday?
- Other→Other conf=0.00 周末爬山吗
- Other→Other conf=0.00 Thanks for the notes
- Other→Other conf=0.00 嗨
- Other→Other conf=0.00 aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa
