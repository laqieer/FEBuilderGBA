"""Dependency-free tests for the strict scenario model."""

import copy
import json

import pytest

from febuildergba_playtest import model
from febuildergba_playtest.model import ScenarioError, load_scenario


def base_doc():
    return {
        "schemaVersion": 1,
        "runFrames": 10,
        "name": "demo",
        "keys": [{"frame": 0, "keys": ["A"]}, {"frame": 3, "keys": ["START", "B"]}],
        "writes": [{"frame": 1, "domain": "wram", "address": 0, "width": 32, "value": 0}],
        "assertions": [{"domain": "wram", "address": 0, "width": 8, "op": "equals", "value": 5}],
        "watchdogs": [{"domain": "iwram", "address": 4, "width": 32, "maxStallFrames": 5}],
    }


def load(doc):
    return load_scenario(json.dumps(doc))


# --- Happy path ------------------------------------------------------------


def test_valid_scenario_parses():
    scenario = load(base_doc())
    assert scenario.schemaVersion == 1
    assert scenario.runFrames == 10
    assert scenario.name == "demo"
    assert len(scenario.events) == 2
    assert scenario.events[0].keys == ("A",)
    assert scenario.events[1].keys == ("B", "START")  # sorted
    assert scenario.writes[0].value == 0
    assert scenario.assertions[0].op == "equals"
    assert scenario.watchdogs[0].maxStallFrames == 5


def test_input_mask_bits():
    scenario = load({
        "schemaVersion": 1, "runFrames": 2,
        "keys": [{"frame": 0, "keys": ["A", "L", "DOWN"]}],
        "assertions": [{"domain": "wram", "address": 0, "width": 8, "op": "changed"}],
    })
    mask = scenario.events[0].mask()
    assert mask == (1 << 0) | (1 << 9) | (1 << 7)


def test_hex_string_address_and_value():
    doc = base_doc()
    doc["writes"] = [{"frame": 0, "domain": "wram", "address": "0x100", "width": 16, "value": "0xBEEF"}]
    scenario = load(doc)
    assert scenario.writes[0].address == 0x100
    assert scenario.writes[0].value == 0xBEEF


# --- Schema version --------------------------------------------------------


def test_missing_schema_version():
    doc = base_doc()
    del doc["schemaVersion"]
    with pytest.raises(ScenarioError, match="schemaVersion"):
        load(doc)


def test_wrong_schema_version():
    doc = base_doc()
    doc["schemaVersion"] = 2
    with pytest.raises(ScenarioError, match="unsupported schemaVersion"):
        load(doc)


# --- Duplicate keys / unknown fields --------------------------------------


def test_duplicate_json_key_rejected():
    text = '{"schemaVersion":1,"runFrames":5,"runFrames":6,"assertions":[]}'
    with pytest.raises(ScenarioError, match="duplicate JSON key"):
        load_scenario(text)


def test_unknown_top_level_property():
    doc = base_doc()
    doc["bogus"] = 1
    with pytest.raises(ScenarioError, match="unknown propert"):
        load(doc)


def test_unknown_event_property():
    doc = base_doc()
    doc["keys"] = [{"frame": 0, "keys": ["A"], "extra": 1}]
    with pytest.raises(ScenarioError, match="unknown propert"):
        load(doc)


# --- Numeric strictness ----------------------------------------------------


def test_non_finite_number_rejected():
    text = '{"schemaVersion":1,"runFrames":Infinity,"assertions":[]}'
    with pytest.raises(ScenarioError, match="non-finite"):
        load_scenario(text)


def test_float_runframes_rejected():
    doc = base_doc()
    doc["runFrames"] = 10.5
    with pytest.raises(ScenarioError, match="runFrames must be an integer"):
        load(doc)


def test_bool_not_accepted_as_int():
    doc = base_doc()
    doc["runFrames"] = True
    with pytest.raises(ScenarioError, match="runFrames must be an integer"):
        load(doc)


def test_malformed_json():
    with pytest.raises(ScenarioError, match="invalid JSON"):
        load_scenario("{not json")


# --- Bounds ----------------------------------------------------------------


def test_runframes_zero_rejected():
    doc = base_doc()
    doc["runFrames"] = 0
    with pytest.raises(ScenarioError, match=r"runFrames must be in"):
        load(doc)


def test_runframes_over_max_rejected():
    doc = base_doc()
    doc["runFrames"] = model.MAX_RUN_FRAMES + 1
    with pytest.raises(ScenarioError, match=r"runFrames must be in"):
        load(doc)


def test_scenario_too_large():
    doc = base_doc()
    doc["name"] = "x" * (model.MAX_SCENARIO_BYTES + 10)
    with pytest.raises(ScenarioError, match="maximum size"):
        load(doc)


def test_too_many_events():
    doc = base_doc()
    doc["keys"] = [{"frame": i, "keys": []} for i in range(model.MAX_EVENTS + 1)]
    doc["runFrames"] = model.MAX_EVENTS + 2
    with pytest.raises(ScenarioError, match="too many input events"):
        load(doc)


# --- Key / domain / width validation --------------------------------------


def test_unsupported_key_rejected():
    doc = base_doc()
    doc["keys"] = [{"frame": 0, "keys": ["X"]}]
    with pytest.raises(ScenarioError, match="unsupported key"):
        load(doc)


def test_opposite_directions_rejected():
    doc = base_doc()
    doc["keys"] = [{"frame": 0, "keys": ["LEFT", "RIGHT"]}]
    with pytest.raises(ScenarioError, match="opposite directions"):
        load(doc)


def test_duplicate_key_in_event_rejected():
    doc = base_doc()
    doc["keys"] = [{"frame": 0, "keys": ["A", "A"]}]
    with pytest.raises(ScenarioError, match="duplicates key"):
        load(doc)


def test_write_to_readonly_domain_rejected():
    doc = base_doc()
    doc["writes"] = [{"frame": 0, "domain": "vram", "address": 0, "width": 8, "value": 1}]
    with pytest.raises(ScenarioError, match="unsupported domain"):
        load(doc)


def test_assertion_rom_domain_rejected():
    doc = base_doc()
    doc["assertions"] = [{"domain": "rom", "address": 0, "width": 8, "op": "changed"}]
    with pytest.raises(ScenarioError, match="unsupported domain"):
        load(doc)


def test_bad_width_rejected():
    doc = base_doc()
    doc["assertions"] = [{"domain": "wram", "address": 0, "width": 24, "op": "changed"}]
    with pytest.raises(ScenarioError, match="width must be one of"):
        load(doc)


def test_unaligned_address_rejected():
    doc = base_doc()
    doc["assertions"] = [{"domain": "wram", "address": 2, "width": 32, "op": "changed"}]
    with pytest.raises(ScenarioError, match="not 32-bit aligned"):
        load(doc)


def test_address_out_of_range_rejected():
    doc = base_doc()
    doc["assertions"] = [{"domain": "iwram", "address": 0x8000, "width": 8, "op": "changed"}]
    with pytest.raises(ScenarioError, match="out of range"):
        load(doc)


def test_value_does_not_fit_width():
    doc = base_doc()
    doc["writes"] = [{"frame": 0, "domain": "wram", "address": 0, "width": 8, "value": 256}]
    with pytest.raises(ScenarioError, match="does not fit"):
        load(doc)


# --- Frame collisions ------------------------------------------------------


def test_duplicate_input_frame_rejected():
    doc = base_doc()
    doc["keys"] = [{"frame": 2, "keys": ["A"]}, {"frame": 2, "keys": ["B"]}]
    with pytest.raises(ScenarioError, match="duplicates an input frame"):
        load(doc)


def test_duplicate_write_rejected():
    doc = base_doc()
    doc["writes"] = [
        {"frame": 1, "domain": "wram", "address": 0, "width": 32, "value": 1},
        {"frame": 1, "domain": "wram", "address": 0, "width": 32, "value": 2},
    ]
    with pytest.raises(ScenarioError, match="duplicates a write"):
        load(doc)


def test_event_frame_beyond_runframes_rejected():
    doc = base_doc()
    doc["keys"] = [{"frame": 10, "keys": ["A"]}]  # runFrames == 10 -> max frame 9
    with pytest.raises(ScenarioError, match=r"frame must be in"):
        load(doc)


# --- Assertion operators ---------------------------------------------------


def test_equals_requires_value():
    doc = base_doc()
    doc["assertions"] = [{"domain": "wram", "address": 0, "width": 8, "op": "equals"}]
    with pytest.raises(ScenarioError, match="requires 'value'"):
        load(doc)


def test_changed_rejects_value():
    doc = base_doc()
    doc["assertions"] = [{"domain": "wram", "address": 0, "width": 8, "op": "changed", "value": 1}]
    with pytest.raises(ScenarioError, match="must not set value"):
        load(doc)


def test_inclusive_range_requires_min_max():
    doc = base_doc()
    doc["assertions"] = [{"domain": "wram", "address": 0, "width": 8, "op": "inclusiveRange", "min": 1}]
    with pytest.raises(ScenarioError, match="requires 'min' and 'max'"):
        load(doc)


def test_inclusive_range_min_gt_max():
    doc = base_doc()
    doc["assertions"] = [{"domain": "wram", "address": 0, "width": 8, "op": "inclusiveRange", "min": 5, "max": 1}]
    with pytest.raises(ScenarioError, match="min must be <= max"):
        load(doc)


# --- ROM guards ------------------------------------------------------------


def test_expected_rom_sha_validated():
    doc = base_doc()
    doc["expectedRomSha256"] = "abc"
    with pytest.raises(ScenarioError, match="64-character hex"):
        load(doc)


def test_expected_game_code_validated():
    doc = base_doc()
    doc["expectedGameCode"] = "TOOLONG"
    with pytest.raises(ScenarioError, match="1-4 printable"):
        load(doc)


def test_expected_game_code_accepted():
    doc = base_doc()
    doc["expectedGameCode"] = "BE8E"
    doc["expectedRomSha256"] = "a" * 64
    scenario = load(doc)
    assert scenario.expectedGameCode == "BE8E"
    assert scenario.expectedRomSha256 == "a" * 64


# --- Screenshot basename safety -------------------------------------------


def test_screenshot_basename_traversal_rejected():
    doc = base_doc()
    doc["screenshot"] = {"basename": "../evil.png"}
    with pytest.raises(ScenarioError, match="path separators"):
        load(doc)


def test_screenshot_absolute_rejected():
    doc = base_doc()
    doc["screenshot"] = {"basename": "C:evil.png"}
    with pytest.raises(ScenarioError, match="drive or stream"):
        load(doc)


def test_screenshot_backslash_rejected():
    doc = base_doc()
    doc["screenshot"] = {"basename": "sub\\evil.png"}
    with pytest.raises(ScenarioError, match="path separators"):
        load(doc)


def test_screenshot_dotdot_rejected():
    doc = base_doc()
    doc["screenshot"] = {"basename": ".."}
    with pytest.raises(ScenarioError, match=r"must not be"):
        load(doc)


def test_screenshot_valid_basename():
    doc = base_doc()
    doc["screenshot"] = {"basename": "final_frame.png", "expectedSha256": "0" * 64}
    scenario = load(doc)
    assert scenario.screenshot.basename == "final_frame.png"
    assert scenario.screenshot.expectedSha256 == "0" * 64


# --- Empty scenario --------------------------------------------------------


def test_empty_scenario_rejected():
    doc = {"schemaVersion": 1, "runFrames": 5}
    with pytest.raises(ScenarioError, match="at least one assertion"):
        load(doc)


def test_top_level_not_object():
    with pytest.raises(ScenarioError, match="must be a JSON object"):
        load_scenario("[]")
