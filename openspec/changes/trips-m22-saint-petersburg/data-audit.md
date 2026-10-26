# Independent data audit and P1 recheck

Reviewer: separate read-only `spb_data_audit` agent, fresh context, 2026-10-26. Scope is source preparation and factual/geometry consistency; not human suitability, runtime acceptance or rollout.

## Initial finding — candidate b/c rejected

P1: `spb-anichkov-palace` anchor9757260768 and eight selected paths entered OSM relation1770781 (Anichkov Palace garden), also within site way495947840. Sixteen used nodes were inside the forbidden garden; ways182055866/182056943/182056944 were not excluded. This contradicted the approved exterior-only scope. A nominal alternate anchor4693569790 was not a repair: its way entered the garden and it remained inside the institutional site.

The initial raw-source check confirmed749nodes,1075ways,133relations; the problem was scope, not fabricated data or mismatched hashes. b/c remain failed despite reproducible bytes.

## Correction and scoped recheck — candidate d/e

The full Anichkov garden is excluded before shortest-path and nearest-five selection. Its POI is replaced with the independently sourced pedestrian Malaya Sadovaya, not with a shifted coordinate.

Recheck: P1 closed; no new actionable findings.
- Seven d/e files are byte-identical, manifest bindings valid.
- No selected node/segment or any of20anchors intersects relation1770781 or site495947840.
- Replacement Q4277303/revision2500281435/claim Q4277303$B2C52D28-56FC-45BE-9DA7-5D9139EA2204 matches the original response and original retrieval time. Anchor8798305670 is on outdoor footways66683377/950396575, distance3.187500991m.
- All62paths, including9new/changed, passed node/way sequence, direction, eligible tags/barriers, endpoints, length, crossing count, minute model, path-hash and exclusion checks.
- Direct streaming of the retained raw gzip matched755nodes including anchors and244used ways, with no missing/mismatching records.
- Recheck of6774complete local area rings found no additional excluded garden/courtyard/indoor transitions.
- 20distinct POIs; architecture16/culture13/history18/nature3. Profile27794bytes,204groups,851unique way IDs. Six POIs remain without selected in/out edges; this limitation is retained, not hidden as full connectivity.

## Bound identities

- Manifest: `3417fad216eaa25f2e0f85c8abd932b080220347cf85134f9f5a763900542a4d`
- Payload: `dd128fa989936720b2e5cd0893a658b4878c49b4be99ba6aad5c89dfe91a3d12`
- Walking evidence: `a7ba096b9410a808e663ef394fe7a8440119be8c72329cd3138aa01e80a5186b`
- Profile: `ebeab33d161ae0a794374c698de8b0d0e3ab57c441a28596edb2953a66e96a95`
- Raw gzip: `5cb4baf36d15f9e41ddf1356383e18c2598191b35c80deee6ec307d0765069e4`

The reviewer changed no files and ran no Host/DB or product-quality evaluation. Physical current access, safety, walking calibration and human A/B have not been established.

