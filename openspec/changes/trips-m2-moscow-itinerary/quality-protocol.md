# Trips M2 Q_live protocol v1 (retained frozen inputs)

Дата фиксации:2026-10-21. Это fictional structured requests и правила оценки,не выполненная real-city evaluation и не реальный маршрут.

## Immutable input set

quality-cases.json содержит Q01–Q12 (12real-supported route briefs) и S01–S12 (6edge+6negative source cases). SHA256 указан ниже. Все12Q brief dates fixed2026-11-01…03;если к фактическому run они становятся unsupported/historical,нужна новая версия protocol до просмотра outputs,без тихого сдвига дат.

Generator outputs на этом real-case set ещё не просматривались. Core unit/factory tests используют отдельные явно вымышленные facts. Не переносить их labels/маршруты в Q или production package.

S-case condition recipes are source/test fixture requirements,not real Moscow facts. Each future automated case must instantiate and record its exact mutation/request within that fixed family before assertions;it does not satisfy human route quality.

## Run binding gate

Для будущего Q_live:до первого eval output добавить отдельную immutable запись(datasetId/version/payloadHash,policyVersion,exact source/build SHA,asOfUtc,caseSetHash). Сам case file не редактировать и не заполнять в нём runBinding постфактум. Пока реального принятого payload и reviewer identities нет,Q_live run запрещён;caseSet freeze не закрывает1.3целиком.

Data researcher:Codex(AI). Human curator,payload second reviewer и два quality reviewers не назначены;никакие подписи или scores им не приписываются. User asked us to research sources ourselves;this authorizes research,not invented human signoff.

## Rubric and acceptance

For each of12Qroutes,two humans independently score geographic continuity,pace burden,interest fit,clarity of unknowns1–5. For every(case,dimension):both scores≥3 and mean≥4. Original scores retained;other routes cannot conceal a weak case. Empty/failed/unreviewed Qcase remains in12-case denominator and cannot pass.

Additionally≥11/12Qcases fill declared stop targets;all24case outcome/structural expectations must pass,with0invented facts/known-closed-as-open/unsupported-as-success. Report Q12,S-edge6,S-negative6 separately.

Rubric anchors:1=not usable/contradictory;2=major correction needed;3=usable with substantive reservations;4=useful and coherent with clear limitations;5=strong fit with no material correction. Reviewers do not see generator ranking score. Disagreement>1 is discussed without rewriting original ratings to manufacture a pass.

After any tuning from Qoutputs,the seen set becomes development. Freeze a new12supported confirmatory set and binding before the next run;same per-case thresholds and both reviewers,plus rerun12Sregressions. Never discard failures or lower thresholds retrospectively.

## Current state

Original Q_live status:inputs/rubric frozen;human reviewers and evaluation pending. Current v2task1.3freezes inputs/protocol and is complete;separate actual Q_demo run-binding/results and full source gates remain pending.
Case-set SHA256:F50B7511FC12642B20DF7CA207C780740382EEB4A482258929551CE29AA5756C
## V2demo use of the frozen input corpus

User selected an offlineOSM/modelled-demo revision after research. quality-cases.json and its original SHA256 remain unchanged;the human rubric above is Q_live,not generated/executed by software.

Q_demo reuses the12supported requests and12Sfamilies under a distinct run-binding(dataset/input/path/model/profile/source/case hashes,asOfUtc). It checks real source identity/path connectivity/derived formula/labels/rights,0invented observations/scores,all outcome assertions and≥11/12filled stop targets plus explicit stage/edit/save/reload. There are no human numeric scores and no calibrated travel-quality claim. Q_demo output/tuning/confirmatory history is separate from any later human Q_live.

Pre-run methodology snapshot before the first Q_demo output: D_demo and structural source S were accepted after independent5.1review; helper/real20POI90paths/strictloader/Core/App/HTTP/TS/state/component/35fictionalbrowser cases and actual6production-build/static-delivery bytes are evidenced in verification.md and structural-review.md. Real Q_demo/S12 execution and its immutable run-binding remain pending. Human Q_live/field calibration/CI/target/rollout remain pending. This pre-output methodology is frozen as an evaluation input; later actual results belong in a separate quality-demo-report.md, not a post-hoc edit of this bound protocol.

Task1.3v2clarification:completion freezes inputs+thebindingprotocol,not thefutureas-yet-unbuilt source hash. Separateactualrun-binding is written by3.1/5.2 BEFOREanyQoutputs,usingfinalsource-treehashandacceptedpayload. This avoids thecircularrequirementthat1.3knowspayloadbefore3.1builds/verifiesit. OriginalQ_liveparagraphs remainhistoricalhumanprotocol;appointmentsnotneededtofinishQ_demo protocol. Frozenquality-cases.jsonexactSHA remainsF50B7511FC12642B20DF7CA207C780740382EEB4A482258929551CE29AA5756C;nofileedit orQoutputperformed.

Pre-output fixed asOfUtc chosen from the actual UTC tool clock after structural acceptance, before harness implementation or any Q output: 2026-10-21T14:16:33.0000000Z. This is the deterministic fixture clock, not a claim that execution occurred then. PhaseA freezes exact S recipes and harness/checker/recorder source; independent internal method review precedes the closed phaseB source/binary/run binding. Actual command execution timestamps are recorded separately. Original case bytes/rubric unchanged; no human actors or scores are generated.
