"""Check relationships and pagination in a completed public capture; no network."""
import json
import sys
from pathlib import Path

capture = Path(sys.argv[1])


def raw(name):
    return json.loads(json.loads((capture / f"{name}.raw.json").read_text())["body"])


checks = []
first = raw("match")
before = raw("match-before")
after = raw("match-after")
threshold = min(e["id"] for e in first["events"])
assert before["events"] and all(e["id"] < threshold for e in before["events"])
assert after["events"] and all(e["id"] > first["first_event_id"] for e in after["events"])
checks.append("Match before/after return nonempty events on the requested side of the boundary")
for name, key, id_field in [("packs", "beatmap_packs", "tag"), ("news", "news_posts", "id"), ("events", "events", "id")]:
    first_page, next_page = raw(name)[key], raw(name + "-next")[key]
    assert first_page and next_page
    assert not ({x[id_field] for x in first_page} & {x[id_field] for x in next_page})
    checks.append(f"{name}: next page is nonempty and captured IDs do not overlap")
first_page, next_page = raw("ranking-performance")["ranking"], raw("ranking-next")["ranking"]
assert first_page and next_page
assert not ({x["user"]["id"] for x in first_page} & {x["user"]["id"] for x in next_page})
checks.append("performance rankings: captured pages contain distinct users")
for mode in ["osu", "taiko", "mania", "fruits"]:
    normal = raw("attributes-" + mode)["attributes"]
    dt = raw("attributes-dt-" + mode)["attributes"]
    assert dt["star_rating"] > normal["star_rating"] and dt["max_combo"] == normal["max_combo"]
    single = raw("user-beatmap-" + mode)["score"]
    scores = raw("user-beatmap-all-" + mode)["scores"]
    assert single["user_id"] == 1646397
    assert scores and all(x["user_id"] == 1646397 and x["beatmap_id"] == single["beatmap_id"] for x in scores)
checks.append("four modes: DT affects difficulty, and single/all beatmap scores retain user/beatmap relationships")
events = raw("filtered-events")["events"]
assert events and all(e["type"] == "offset_edit" for e in events)
checks.append("event type filter returns nonempty offset_edit events")
assert not raw("events-min-date")["events"] and not raw("events-max-date")["events"]
checks.append("date filters exclude the known event when moved to the next/previous day")
discussions = raw("filtered-discussions")["discussions"]
assert discussions and all(d["message_type"] == "problem" for d in discussions)
checks.append("discussion type filter returns nonempty problem discussions")
assert raw("lookup-filename")["id"] == raw("lookup-checksum")["id"] == raw("beatmap-osu")["id"]
assert raw("user-username")["id"] == raw("user-osu")["id"] == 1646397
stable, legacy = raw("stable"), raw("legacy-score")
assert stable["id"] == legacy["id"] and stable["legacy_score_id"] == legacy["legacy_score_id"]
checks.append("filename/checksum/user-name/legacy-ID lookup resolves to the corresponding original object")
(capture / "semantic-checks.json").write_text(json.dumps(checks, indent=2) + "\n")
print(f"Semantic checks passed: {len(checks)} groups")
