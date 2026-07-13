"""Public tests for the dependency-free stdio MCP adapter (issue #1942).

These tests are entirely private-ROM-free: protocol/framing/schema behavior
is exercised by mocking the ``core.*`` wrappers the same way ``test_verbs.py``
does (monkeypatching the module-level function that each handler imports at
call time). The only real-backend test here is the LZ77 roundtrip (see
``test_verbs.py``), and it is synthetic (no ROM) and skip-gated only on
backend availability.
"""

import json
import io
import os
import queue
import subprocess
import sys
import threading
from pathlib import Path

import pytest

from cli_anything.febuildergba import mcp_server as srv
from cli_anything.febuildergba.core.session import (
    HISTORY_OP_DATA_EXPORT,
    HISTORY_OP_DATA_IMPORT,
    HISTORY_OP_IMPORT_PALETTE,
    MAX_SESSION_FILE_BYTES,
    MAX_SESSION_INTEGER_DIGITS,
    Session,
)


# ── Fixtures ────────────────────────────────────────────────────────────

@pytest.fixture
def state(tmp_path):
    session = Session(str(tmp_path / "session.json"))
    return srv._ServerState(session)


def _init_params(protocol_version="2025-03-26", **overrides):
    """A realistic, fully-conformant 'initialize' params object: object
    capabilities + object clientInfo with non-empty string name/version, as
    required by the server's initialize conformance checks."""
    params = {
        "protocolVersion": protocol_version,
        "capabilities": {"roots": {"listChanged": True}},
        "clientInfo": {"name": "febuildergba-test-client", "version": "1.0.0"},
    }
    params.update(overrides)
    return params


@pytest.fixture(params=srv.SUPPORTED_PROTOCOL_VERSIONS)
def initialized_state(state, request):
    response = srv.handle_line(state, json.dumps({
        "jsonrpc": "2.0", "id": 1, "method": "initialize",
        "params": _init_params(request.param),
    }))
    assert response["result"]["protocolVersion"] == request.param
    return state


def _req(method, params=None, id_=1):
    msg = {"jsonrpc": "2.0", "id": id_, "method": method}
    if params is not None:
        msg["params"] = params
    return msg


def _notif(method, params=None):
    msg = {"jsonrpc": "2.0", "method": method}
    if params is not None:
        msg["params"] = params
    return msg


def _call_tool(state, name, arguments=None, id_=1):
    return srv.handle_line(state, json.dumps(_req(
        "tools/call", {"name": name, "arguments": arguments or {}}, id_,
    )))


def _initialized_state_from_session_payload(tmp_path, payload):
    session_path = tmp_path / "persisted-session.json"
    session_path.write_text(json.dumps(payload))
    state = srv._ServerState(Session(str(session_path)))
    response = srv.handle_line(state, json.dumps(_req(
        "initialize", _init_params(),
    )))
    assert "result" in response
    return state


def _write_valid_test_rom(path):
    rom = bytearray(0x100000)
    rom[0xA0:0xAC] = b"FIRE EMBLEM\x00"
    rom[0xAC:0xB0] = b"BE8E"
    rom[0xB0:0xB2] = b"01"
    rom[0xB2] = 0x96
    rom[0xBC] = 0x01
    rom[0xBD] = 0xF3
    path.write_bytes(rom)


# ── Version negotiation ─────────────────────────────────────────────────

class TestVersionNegotiation:
    def test_latest_requested(self, state):
        resp = srv.handle_line(state, json.dumps(_req("initialize", _init_params("2025-03-26"))))
        assert resp["result"]["protocolVersion"] == "2025-03-26"

    def test_older_supported_requested(self, state):
        resp = srv.handle_line(state, json.dumps(_req("initialize", _init_params("2024-11-05"))))
        assert resp["result"]["protocolVersion"] == "2024-11-05"

    def test_unsupported_falls_back_to_latest(self, state):
        # A well-formed-but-unrecognized protocolVersion still negotiates
        # the latest supported version rather than failing.
        resp = srv.handle_line(state, json.dumps(_req("initialize", _init_params("1999-01-01"))))
        assert resp["result"]["protocolVersion"] == srv.LATEST_PROTOCOL_VERSION

    def test_capabilities_shape(self, state):
        resp = srv.handle_line(state, json.dumps(_req("initialize", _init_params())))
        caps = resp["result"]["capabilities"]
        assert caps["tools"] == {"listChanged": False}
        assert caps["resources"] == {"subscribe": False, "listChanged": False}


# ── Initialize conformance ───────────────────────────────────────────────

class TestInitializeConformance:
    def test_missing_protocol_version_is_invalid_params(self, state):
        params = _init_params()
        del params["protocolVersion"]
        resp = srv.handle_line(state, json.dumps(_req("initialize", params)))
        assert resp["error"]["code"] == srv.INVALID_PARAMS

    def test_non_string_protocol_version_is_invalid_params(self, state):
        resp = srv.handle_line(state, json.dumps(_req("initialize", _init_params(protocol_version=123))))
        assert resp["error"]["code"] == srv.INVALID_PARAMS

    def test_empty_protocol_version_is_invalid_params(self, state):
        resp = srv.handle_line(state, json.dumps(_req("initialize", _init_params(protocol_version=""))))
        assert resp["error"]["code"] == srv.INVALID_PARAMS

    def test_missing_capabilities_is_invalid_params(self, state):
        params = _init_params()
        del params["capabilities"]
        resp = srv.handle_line(state, json.dumps(_req("initialize", params)))
        assert resp["error"]["code"] == srv.INVALID_PARAMS

    def test_malformed_capabilities_is_invalid_params(self, state):
        resp = srv.handle_line(state, json.dumps(_req("initialize", _init_params(capabilities="nope"))))
        assert resp["error"]["code"] == srv.INVALID_PARAMS

    def test_missing_client_info_is_invalid_params(self, state):
        params = _init_params()
        del params["clientInfo"]
        resp = srv.handle_line(state, json.dumps(_req("initialize", params)))
        assert resp["error"]["code"] == srv.INVALID_PARAMS

    def test_client_info_not_object_is_invalid_params(self, state):
        resp = srv.handle_line(state, json.dumps(_req("initialize", _init_params(clientInfo="nope"))))
        assert resp["error"]["code"] == srv.INVALID_PARAMS

    def test_client_info_missing_name_is_invalid_params(self, state):
        resp = srv.handle_line(state, json.dumps(_req(
            "initialize", _init_params(clientInfo={"version": "1.0.0"}))))
        assert resp["error"]["code"] == srv.INVALID_PARAMS

    def test_client_info_empty_name_is_invalid_params(self, state):
        resp = srv.handle_line(state, json.dumps(_req(
            "initialize", _init_params(clientInfo={"name": "", "version": "1.0.0"}))))
        assert resp["error"]["code"] == srv.INVALID_PARAMS

    def test_client_info_missing_version_is_invalid_params(self, state):
        resp = srv.handle_line(state, json.dumps(_req(
            "initialize", _init_params(clientInfo={"name": "client"}))))
        assert resp["error"]["code"] == srv.INVALID_PARAMS

    def test_client_info_non_string_version_is_invalid_params(self, state):
        resp = srv.handle_line(state, json.dumps(_req(
            "initialize", _init_params(clientInfo={"name": "client", "version": 1}))))
        assert resp["error"]["code"] == srv.INVALID_PARAMS

    def test_well_formed_initialize_succeeds(self, state):
        resp = srv.handle_line(state, json.dumps(_req("initialize", _init_params())))
        assert resp["result"]["protocolVersion"] == "2025-03-26"

    def test_duplicate_initialize_is_invalid_request(self, initialized_state):
        resp = srv.handle_line(initialized_state, json.dumps(_req("initialize", _init_params(), id_=2)))
        assert resp["error"]["code"] == srv.INVALID_REQUEST


# ── Lifecycle ────────────────────────────────────────────────────────────

class TestLifecycle:
    def test_operation_before_initialize_is_invalid_request(self, state):
        resp = srv.handle_line(state, json.dumps(_req("tools/list")))
        assert resp["error"]["code"] == srv.INVALID_REQUEST

    def test_ping_allowed_before_initialize(self, state):
        resp = srv.handle_line(state, json.dumps(_req("ping")))
        assert resp["result"] == {}

    def test_tools_list_allowed_after_initialize(self, initialized_state):
        resp = srv.handle_line(initialized_state, json.dumps(_req("tools/list")))
        assert "tools" in resp["result"]

    def test_initialize_cannot_be_batched(self, state):
        batch = [_req("initialize", _init_params(), id_=1)]
        resp = srv.handle_line(state, json.dumps(batch))
        assert isinstance(resp, list) and len(resp) == 1
        assert resp[0]["error"]["code"] == srv.INVALID_REQUEST


# ── Framing: single / batch / notifications ─────────────────────────────

class TestFraming:
    def test_single_message_returns_dict(self, initialized_state):
        resp = srv.handle_line(initialized_state, json.dumps(_req("ping")))
        assert isinstance(resp, dict)

    def test_batch_of_requests_returns_list_in_order(self, initialized_state):
        batch = [_req("ping", id_=1), _req("ping", id_=2)]
        resp = srv.handle_line(initialized_state, json.dumps(batch))
        assert isinstance(resp, list) and len(resp) == 2
        assert [r["id"] for r in resp] == [1, 2]

    def test_mixed_batch_request_notification_invalid(self, initialized_state):
        batch = [
            _req("ping", id_=1),
            _notif("notifications/cancelled"),
            {"jsonrpc": "1.0", "id": 2, "method": "ping"},  # invalid
        ]
        resp = srv.handle_line(initialized_state, json.dumps(batch))
        # notification produces no entry; ping + invalid produce 2 entries
        assert isinstance(resp, list) and len(resp) == 2
        assert resp[0]["result"] == {}
        assert resp[1]["error"]["code"] == srv.INVALID_REQUEST

    def test_empty_batch_is_invalid_request(self, initialized_state):
        resp = srv.handle_line(initialized_state, "[]")
        assert resp["error"]["code"] == srv.INVALID_REQUEST

    def test_notification_only_batch_emits_nothing(self, initialized_state):
        batch = [_notif("notifications/cancelled"), _notif("notifications/initialized")]
        resp = srv.handle_line(initialized_state, json.dumps(batch))
        assert resp is None

    def test_single_notification_emits_nothing(self, initialized_state):
        resp = srv.handle_line(initialized_state, json.dumps(_notif("notifications/initialized")))
        assert resp is None

    @pytest.mark.parametrize("with_id", [True, False])
    def test_explicit_null_params_is_invalid_request(
            self, initialized_state, with_id):
        message = {"jsonrpc": "2.0", "method": "ping", "params": None}
        if with_id:
            message["id"] = 17

        resp = srv.handle_line(initialized_state, json.dumps(message))

        assert resp["id"] == (17 if with_id else None)
        assert resp["error"]["code"] == srv.INVALID_REQUEST
        assert "params must be an object" in resp["error"]["message"]

    def test_initialize_notification_is_ignored_without_consuming_lifecycle(self, state):
        resp = srv.handle_line(state, json.dumps(_notif(
            "initialize", _init_params(),
        )))
        assert resp is None
        assert state.initialized is False

        real = srv.handle_line(state, json.dumps(_req(
            "initialize", _init_params(), id_=7,
        )))
        assert real["id"] == 7
        assert real["result"]["protocolVersion"] == "2025-03-26"
        assert state.initialized is True

    def test_tools_call_notification_does_not_execute_handler(
            self, initialized_state, monkeypatch):
        called = []

        def destructive_handler(session, arguments):
            called.append(True)
            return {"status": "closed"}, False

        monkeypatch.setitem(
            srv.TOOL_HANDLERS, "session_close", destructive_handler,
        )
        resp = srv.handle_line(initialized_state, json.dumps(_notif(
            "tools/call", {"name": "session_close", "arguments": {}},
        )))
        assert resp is None
        assert called == []

    @pytest.mark.parametrize(
        "method",
        ["notifications/initialized", "notifications/cancelled"],
    )
    def test_notification_only_method_rejects_request(
            self, initialized_state, method):
        resp = srv.handle_line(
            initialized_state, json.dumps(_req(method, {}, id_=9)),
        )
        assert resp["id"] == 9
        assert resp["error"]["code"] == srv.INVALID_REQUEST
        assert "notification-only" in resp["error"]["message"]

    def test_notification_to_unknown_method_is_suppressed(self, initialized_state):
        resp = srv.handle_line(initialized_state, json.dumps(_notif("bogus/method")))
        assert resp is None

    def test_malformed_method_without_id_is_not_suppressed(self, initialized_state):
        resp = srv.handle_line(initialized_state, json.dumps({"jsonrpc": "2.0"}))
        assert resp["id"] is None
        assert resp["error"]["code"] == srv.INVALID_REQUEST

    def test_malformed_method_without_id_in_batch_returns_entry(self, initialized_state):
        batch = [{"jsonrpc": "2.0"}, _notif("notifications/initialized")]
        resp = srv.handle_line(initialized_state, json.dumps(batch))
        assert isinstance(resp, list) and len(resp) == 1
        assert resp[0]["id"] is None
        assert resp[0]["error"]["code"] == srv.INVALID_REQUEST

    def test_oversized_batch_is_rejected_as_one_invalid_request(self, initialized_state):
        batch = [_req("ping", id_=i) for i in range(srv.MAX_BATCH_ITEMS + 1)]
        resp = srv.handle_line(initialized_state, json.dumps(batch))
        assert isinstance(resp, dict)
        assert resp["id"] is None
        assert resp["error"]["code"] == srv.INVALID_REQUEST

    def test_arbitrary_string_and_integer_ids_roundtrip(self, initialized_state):
        resp = srv.handle_line(initialized_state, json.dumps(_req("ping", id_="abc-123")))
        assert resp["id"] == "abc-123"
        resp2 = srv.handle_line(initialized_state, json.dumps(_req("ping", id_=42)))
        assert resp2["id"] == 42


# ── Protocol errors ──────────────────────────────────────────────────────

class TestProtocolErrors:
    def test_parse_error(self, initialized_state):
        resp = srv.handle_line(initialized_state, "{not json")
        assert resp["error"]["code"] == srv.PARSE_ERROR
        assert resp["id"] is None

    @pytest.mark.parametrize("constant", ["NaN", "Infinity", "-Infinity"])
    def test_nonstandard_json_constant_is_parse_error(
            self, initialized_state, constant):
        line = (
            '{"jsonrpc":"2.0","id":1,"method":"ping",'
            f'"params":{{"_meta":{constant}}}}}'
        )
        resp = srv.handle_line(initialized_state, line)
        assert resp["error"]["code"] == srv.PARSE_ERROR
        assert resp["id"] is None

    def test_invalid_request_bad_version(self, initialized_state):
        resp = srv.handle_line(initialized_state, json.dumps({"jsonrpc": "1.0", "id": 1, "method": "ping"}))
        assert resp["error"]["code"] == srv.INVALID_REQUEST

    def test_invalid_request_null_id(self, initialized_state):
        resp = srv.handle_line(initialized_state, json.dumps({"jsonrpc": "2.0", "id": None, "method": "ping"}))
        assert resp["error"]["code"] == srv.INVALID_REQUEST

    def test_invalid_request_bool_id(self, initialized_state):
        resp = srv.handle_line(initialized_state, json.dumps({"jsonrpc": "2.0", "id": True, "method": "ping"}))
        assert resp["error"]["code"] == srv.INVALID_REQUEST
        assert resp["id"] is None

    def test_bad_version_does_not_echo_invalid_bool_id(self, initialized_state):
        request = {"jsonrpc": "1.0", "id": True, "method": "ping"}
        resp = srv.handle_line(initialized_state, json.dumps(request))
        assert resp["error"]["code"] == srv.INVALID_REQUEST
        assert resp["id"] is None

    def test_oversized_input_line_is_invalid_request(self, initialized_state):
        resp = srv.handle_line(initialized_state, "x" * (srv.MAX_REQUEST_LINE_CHARS + 1))
        assert resp["id"] is None
        assert resp["error"]["code"] == srv.INVALID_REQUEST

    def test_invalid_request_params_not_object(self, initialized_state):
        resp = srv.handle_line(initialized_state, json.dumps({"jsonrpc": "2.0", "id": 1, "method": "ping", "params": [1, 2]}))
        assert resp["error"]["code"] == srv.INVALID_REQUEST

    def test_invalid_request_not_an_object(self, initialized_state):
        resp = srv.handle_line(initialized_state, json.dumps(42))
        assert resp["error"]["code"] == srv.INVALID_REQUEST

    def test_invalid_request_missing_method(self, initialized_state):
        resp = srv.handle_line(initialized_state, json.dumps({"jsonrpc": "2.0", "id": 1}))
        assert resp["error"]["code"] == srv.INVALID_REQUEST

    def test_invalid_request_empty_method(self, initialized_state):
        resp = srv.handle_line(initialized_state, json.dumps({"jsonrpc": "2.0", "id": 1, "method": ""}))
        assert resp["error"]["code"] == srv.INVALID_REQUEST

    def test_invalid_request_non_string_method(self, initialized_state):
        resp = srv.handle_line(initialized_state, json.dumps({"jsonrpc": "2.0", "id": 1, "method": 42}))
        assert resp["error"]["code"] == srv.INVALID_REQUEST

    def test_method_not_found_unknown_method(self, initialized_state):
        resp = srv.handle_line(initialized_state, json.dumps(_req("bogus/method")))
        assert resp["error"]["code"] == srv.METHOD_NOT_FOUND

    def test_invalid_params_unknown_tool(self, initialized_state):
        resp = _call_tool(initialized_state, "no_such_tool")
        assert resp["error"]["code"] == srv.INVALID_PARAMS

    def test_invalid_params_missing_required_arg(self, initialized_state):
        resp = _call_tool(initialized_state, "data_export", {"table": "units"})  # missing out_path
        assert resp["error"]["code"] == srv.INVALID_PARAMS

    def test_invalid_params_unexpected_property(self, initialized_state):
        resp = _call_tool(initialized_state, "rom_info", {"bogus_extra": 1})
        assert resp["error"]["code"] == srv.INVALID_PARAMS

    def test_invalid_params_wrong_type(self, initialized_state):
        resp = _call_tool(initialized_state, "session_history", {"count": "ten"})
        assert resp["error"]["code"] == srv.INVALID_PARAMS

    def test_invalid_params_bool_not_accepted_as_int(self, initialized_state):
        resp = _call_tool(initialized_state, "session_history", {"count": True})
        assert resp["error"]["code"] == srv.INVALID_PARAMS

    def test_invalid_params_out_of_bounds(self, initialized_state):
        resp = _call_tool(initialized_state, "session_history", {"count": 0})
        assert resp["error"]["code"] == srv.INVALID_PARAMS
        resp2 = _call_tool(initialized_state, "session_history", {"count": 101})
        assert resp2["error"]["code"] == srv.INVALID_PARAMS

    def test_invalid_params_tools_call_missing_name(self, initialized_state):
        resp = srv.handle_line(initialized_state, json.dumps(_req("tools/call", {"arguments": {}})))
        assert resp["error"]["code"] == srv.INVALID_PARAMS

    def test_unknown_resource_is_resource_not_found(self, initialized_state):
        resp = srv.handle_line(initialized_state, json.dumps(_req("resources/read", {"uri": "febuildergba://nope"})))
        assert resp["error"]["code"] == srv.RESOURCE_NOT_FOUND

    def test_invalid_params_resources_read_missing_uri(self, initialized_state):
        resp = srv.handle_line(initialized_state, json.dumps(_req("resources/read", {})))
        assert resp["error"]["code"] == srv.INVALID_PARAMS


# ── Method-specific params tightening (tools/call, resources/read,
# tools/list, resources/list, ping) ───────────────────────────────────────

class TestMethodSpecificParams:
    def test_ping_rejects_unknown_field(self, initialized_state):
        resp = srv.handle_line(initialized_state, json.dumps(_req("ping", {"bogus": 1})))
        assert resp["error"]["code"] == srv.INVALID_PARAMS

    def test_ping_allows_meta(self, initialized_state):
        resp = srv.handle_line(initialized_state, json.dumps(_req("ping", {"_meta": {"x": 1}})))
        assert resp["result"] == {}

    def test_ping_allows_no_params(self, initialized_state):
        resp = srv.handle_line(initialized_state, json.dumps(_req("ping")))
        assert resp["result"] == {}

    def test_tools_list_rejects_unknown_field(self, initialized_state):
        resp = srv.handle_line(initialized_state, json.dumps(_req("tools/list", {"bogus": 1})))
        assert resp["error"]["code"] == srv.INVALID_PARAMS

    def test_tools_list_allows_cursor_and_meta(self, initialized_state):
        resp = srv.handle_line(initialized_state, json.dumps(
            _req("tools/list", {"cursor": "abc", "_meta": {}})))
        assert "tools" in resp["result"]

    def test_tools_list_rejects_non_string_cursor(self, initialized_state):
        resp = srv.handle_line(initialized_state, json.dumps(_req("tools/list", {"cursor": 1})))
        assert resp["error"]["code"] == srv.INVALID_PARAMS

    def test_resources_list_rejects_unknown_field(self, initialized_state):
        resp = srv.handle_line(initialized_state, json.dumps(_req("resources/list", {"bogus": 1})))
        assert resp["error"]["code"] == srv.INVALID_PARAMS

    def test_resources_list_allows_cursor_and_meta(self, initialized_state):
        resp = srv.handle_line(initialized_state, json.dumps(
            _req("resources/list", {"cursor": "abc", "_meta": {}})))
        assert "resources" in resp["result"]

    def test_tools_call_rejects_unknown_field(self, initialized_state):
        resp = srv.handle_line(initialized_state, json.dumps(
            _req("tools/call", {"name": "backend_check", "bogus": 1})))
        assert resp["error"]["code"] == srv.INVALID_PARAMS

    def test_tools_call_allows_meta(self, initialized_state):
        resp = srv.handle_line(initialized_state, json.dumps(
            _req("tools/call", {"name": "backend_check", "arguments": {}, "_meta": {"trace": "x"}})))
        assert "result" in resp

    def test_tools_call_rejects_null_arguments(self, initialized_state):
        resp = srv.handle_line(initialized_state, json.dumps(_req(
            "tools/call",
            {"name": "backend_check", "arguments": None},
        )))
        assert resp["error"]["code"] == srv.INVALID_PARAMS
        assert "'arguments' must be an object" in resp["error"]["message"]

    def test_resources_read_rejects_unknown_field(self, initialized_state):
        resp = srv.handle_line(initialized_state, json.dumps(
            _req("resources/read", {"uri": "febuildergba://session", "bogus": 1})))
        assert resp["error"]["code"] == srv.INVALID_PARAMS

    def test_resources_read_allows_meta(self, initialized_state):
        resp = srv.handle_line(initialized_state, json.dumps(
            _req("resources/read", {"uri": "febuildergba://session", "_meta": {}})))
        assert "result" in resp


# ── Tool / resource discovery ────────────────────────────────────────────

EXPECTED_TOOLS = {
    "backend_check", "session_open", "session_close", "session_status",
    "session_history", "rom_info", "rom_validate", "rom_list_tables",
    "rom_checksum", "data_export", "data_import", "data_roundtrip",
    "names_resolve", "text_search", "text_roundtrip", "rom_lint",
    "image_quantize", "image_convert_map", "palette_export",
    "palette_import", "lz77",
}

DESTRUCTIVE_TOOLS = {
    "session_open", "session_close", "data_export", "data_import",
    "image_quantize", "image_convert_map", "palette_export",
    "palette_import", "lz77",
}


class TestDiscovery:
    def test_exactly_21_tools(self):
        assert len(srv.TOOL_DEFS) == 21
        assert {t["name"] for t in srv.TOOL_DEFS} == EXPECTED_TOOLS

    def test_tools_list_matches_defs(self, initialized_state):
        resp = srv.handle_line(initialized_state, json.dumps(_req("tools/list")))
        names = {t["name"] for t in resp["result"]["tools"]}
        assert names == EXPECTED_TOOLS

    def test_no_forbidden_tools(self):
        forbidden = {"run_command", "patch_apply", "rebuild", "repair_header",
                     "compile_event", "import_midi", "export_midi", "songexchange"}
        names = {t["name"] for t in srv.TOOL_DEFS}
        assert names.isdisjoint(forbidden)

    def test_annotation_matrix_exact(self):
        for tool in srv.TOOL_DEFS:
            ann = tool["annotations"]
            assert ann["openWorldHint"] is False
            if tool["name"] in DESTRUCTIVE_TOOLS:
                assert ann["readOnlyHint"] is False, tool["name"]
                assert ann["destructiveHint"] is True, tool["name"]
            else:
                assert ann["readOnlyHint"] is True, tool["name"]
                assert ann["destructiveHint"] is False, tool["name"]

    def test_destructive_count_is_9(self):
        destructive = [t for t in srv.TOOL_DEFS if t["annotations"]["destructiveHint"]]
        assert len(destructive) == 9
        assert {t["name"] for t in destructive} == DESTRUCTIVE_TOOLS

    def test_text_roundtrip_has_no_out_prefix_param(self):
        schema = srv.TOOL_SCHEMAS["text_roundtrip"]
        assert "out_prefix" not in schema["properties"]

    def test_image_quantize_schema_uses_backend_color_count_range(self):
        tool = next(t for t in srv.TOOL_DEFS if t["name"] == "image_quantize")
        prop = tool["inputSchema"]["properties"]["palette_no"]
        assert prop["minimum"] == 1
        assert prop["maximum"] == 256
        assert prop["default"] == 16
        assert "Maximum color count" in prop["description"]

    def test_all_schemas_closed(self):
        for tool in srv.TOOL_DEFS:
            assert tool["inputSchema"]["additionalProperties"] is False, tool["name"]

    def test_three_resources_exact(self):
        uris = {r["uri"] for r in srv.RESOURCE_DEFS}
        assert uris == {
            "febuildergba://session",
            "febuildergba://session/history",
            "febuildergba://rom/metadata",
        }

    def test_resources_list_method(self, initialized_state):
        resp = srv.handle_line(initialized_state, json.dumps(_req("resources/list")))
        uris = {r["uri"] for r in resp["result"]["resources"]}
        assert len(uris) == 3
        for r in resp["result"]["resources"]:
            assert r["mimeType"] == "application/json"


# ── Resources: content + no raw bytes ────────────────────────────────────

class TestResources:
    def _read(self, state, uri):
        resp = srv.handle_line(state, json.dumps(_req("resources/read", {"uri": uri})))
        content = resp["result"]["contents"][0]
        assert content["mimeType"] == "application/json"
        return json.loads(content["text"])

    def test_session_resource_closed(self, initialized_state):
        data = self._read(initialized_state, "febuildergba://session")
        assert data == {"open": False, "truncated": False}

    def test_session_history_resource_closed(self, initialized_state):
        data = self._read(initialized_state, "febuildergba://session/history")
        assert data["open"] is False
        assert data["history"] == []

    def test_session_history_resource_caps_direct_overflow(
            self, initialized_state, tmp_path):
        initialized_state.session.open_rom(str(tmp_path / "r.gba"), "FE8U", 1)
        initialized_state.session.state.history = [
            {"op": f"op_{i}"}
            for i in range(srv.HISTORY_MAX + 20)
        ]

        data = self._read(initialized_state, "febuildergba://session/history")

        assert len(data["history"]) == srv.HISTORY_MAX
        assert data["history"][0]["op"] == "op_20"
        assert data["truncated"] is True

    def test_session_history_resource_exact_cap_is_not_truncated(
            self, initialized_state, tmp_path):
        initialized_state.session.open_rom(str(tmp_path / "r.gba"), "FE8U", 1)
        initialized_state.session.state.history = [
            {"op": f"op_{i}"}
            for i in range(srv.HISTORY_MAX)
        ]

        data = self._read(initialized_state, "febuildergba://session/history")

        assert len(data["history"]) == srv.HISTORY_MAX
        assert data["history"][0]["op"] == "op_0"
        assert data["truncated"] is False

    def test_session_history_resource_bounds_nested_collections(
            self, initialized_state, tmp_path):
        initialized_state.session.open_rom(str(tmp_path / "r.gba"), "FE8U", 1)
        initialized_state.session.state.history = [{
            "nested": list(range(srv.MAX_RESOURCE_COLLECTION_ITEMS + 20)),
            "huge_number": 1 << 4096,
        }]

        data = self._read(initialized_state, "febuildergba://session/history")

        assert len(data["history"][0]["nested"]) == srv.MAX_RESOURCE_COLLECTION_ITEMS
        assert data["history"][0]["huge_number"] is None
        assert data["truncated"] is True

    def test_rom_metadata_resource_closed(self, initialized_state):
        data = self._read(initialized_state, "febuildergba://rom/metadata")
        assert data == {"open": False, "truncated": False}

    def test_rom_metadata_resource_open_no_raw_bytes(self, initialized_state, monkeypatch, tmp_path):
        initialized_state.session.open_rom(str(tmp_path / "r.gba"), "FE8U", 123)

        def fake_rom_header(path):
            return {"rom_path": path, "title": "T", "game_code": "BE8E",
                    "maker_code": "01", "unit_code": 0, "device_type": 0,
                    "software_version": 0, "header_checksum": 0x91}
        monkeypatch.setattr("cli_anything.febuildergba.core.project.rom_header", fake_rom_header)

        data = self._read(initialized_state, "febuildergba://rom/metadata")
        assert data["open"] is True
        assert data["rom_header"]["game_code"] == "BE8E"
        # no raw byte payloads anywhere in the metadata
        blob = json.dumps(data)
        assert "\\u0000" not in blob  # no embedded binary/null bytes leaked in
        for key in data:
            assert key not in ("bytes", "raw", "data", "rom_bytes")

    def test_rom_metadata_resource_rejects_stale_non_rom_session(
            self, initialized_state, tmp_path):
        not_rom = tmp_path / "document.bin"
        content = bytearray(0x100000)
        content[0xA0:0xAC] = b"LOCAL SECRET"
        content[0xAC:0xB0] = b"LEAK"
        content[0xB2] = 0x96
        content[0xBD] = 0x00  # Correct value would be 0xE3.
        not_rom.write_bytes(content)
        initialized_state.session.open_rom(str(not_rom), "FE8U", len(content))

        data = self._read(initialized_state, "febuildergba://rom/metadata")
        assert data["open"] is True
        assert data["rom_header"] is None
        assert "header checksum mismatch" in data["rom_header_error"]
        assert "LOCAL SECRET" not in json.dumps(data)


# ── Tool business logic: session precedence / history / modified flag ───

class TestSessionPrecedence:
    def test_missing_rom_and_no_session_is_tool_error(self, initialized_state):
        resp = _call_tool(initialized_state, "rom_info", {})
        result = resp["result"]
        assert result["isError"] is True
        payload = json.loads(result["content"][0]["text"])
        assert "No ROM specified" in payload["error"]

    @pytest.mark.parametrize("tool_name", ["rom_info", "session_open"])
    def test_non_rom_path_is_rejected_without_content_disclosure(
            self, initialized_state, tmp_path, tool_name):
        not_rom = tmp_path / "document.bin"
        content = bytearray(0x100000)
        content[0xA0:0xAC] = b"LOCAL SECRET"
        content[0xAC:0xB0] = b"LEAK"
        content[0xB2] = 0x96
        content[0xBD] = 0x00  # Correct value would be 0xE3.
        not_rom.write_bytes(content)

        resp = _call_tool(
            initialized_state, tool_name, {"rom_path": str(not_rom)},
        )
        result = resp["result"]
        assert result["isError"] is True
        text = result["content"][0]["text"]
        assert "header checksum mismatch" in text
        assert "LOCAL SECRET" not in text
        assert initialized_state.session.is_open() is False
        assert initialized_state.session.state.history == []
        assert initialized_state.session.path.exists() is False

    def test_session_open_accepts_valid_rom_without_backend(
            self, initialized_state, tmp_path, monkeypatch):
        from cli_anything.febuildergba.core import project
        rom_path = tmp_path / "fe8u.gba"
        _write_valid_test_rom(rom_path)

        def unavailable_backend(args):
            raise RuntimeError("backend unavailable")

        monkeypatch.setattr(project, "run_cli", unavailable_backend)
        resp = _call_tool(
            initialized_state, "session_open", {"rom_path": str(rom_path)},
        )
        result = resp["result"]
        assert result["isError"] is False
        assert initialized_state.session.is_open() is True
        assert initialized_state.session.state.rom_version == "FE8U"
        assert initialized_state.session.state.history[-1]["op"] == "open"

    def test_stale_session_close_does_not_delete_reopened_session(
            self, initialized_state):
        session = initialized_state.session
        session.open_rom("/fake/a.gba", "FE8U", 1)
        current = Session(str(session.path))
        current.open_rom("/fake/b.gba", "FE8U", 1)

        response = _call_tool(initialized_state, "session_close")
        result = response["result"]
        payload = json.loads(result["content"][0]["text"])

        assert result["isError"] is False
        assert payload == {"status": "stale_session"}
        assert session.state.session_id == current.state.session_id
        assert session.state.rom_path.endswith("b.gba")

        persisted = Session(str(session.path))
        assert persisted.state.session_id == current.state.session_id
        assert persisted.state.rom_path.endswith("b.gba")

    def test_explicit_rom_path_overrides_session(self, initialized_state, monkeypatch, tmp_path):
        initialized_state.session.open_rom(str(tmp_path / "session_rom.gba"), "FE8U", 1)
        seen = {}

        def fake_rom_info(rom_path, force_version=""):
            seen["rom_path"] = rom_path
            return {"rom_path": rom_path, "rom_size": 1, "rom_size_hex": "0x1",
                    "rom_size_mb": 0.0, "detected_version": "FE7U",
                    "force_version": force_version, "lint_output": "", "lint_exit_code": 0}
        monkeypatch.setattr("cli_anything.febuildergba.core.project.rom_info", fake_rom_info)

        resp = _call_tool(initialized_state, "rom_info", {"rom_path": "explicit.gba"})
        assert resp["result"]["isError"] is False
        assert seen["rom_path"] == "explicit.gba"

    def test_falls_back_to_session_rom(self, initialized_state, monkeypatch, tmp_path):
        rom_path = str(tmp_path / "session_rom.gba")
        initialized_state.session.open_rom(rom_path, "FE8U", 1)
        seen = {}

        def fake_rom_info(rp, force_version=""):
            seen["rom_path"] = rp
            return {"rom_path": rp, "rom_size": 1, "rom_size_hex": "0x1",
                    "rom_size_mb": 0.0, "detected_version": "FE8U",
                    "force_version": force_version, "lint_output": "", "lint_exit_code": 0}
        monkeypatch.setattr("cli_anything.febuildergba.core.project.rom_info", fake_rom_info)

        resp = _call_tool(initialized_state, "rom_info", {})
        assert resp["result"]["isError"] is False
        assert seen["rom_path"] == rom_path


class TestDataExportHistoryAndModified:
    def _fake_export(self, monkeypatch, exit_code=0):
        def fake(rom_path, table, out_path, force_version=""):
            return {"table": table, "output_files": [out_path], "output_path": out_path,
                    "exit_code": exit_code, "stdout": "", "stderr": ""}
        monkeypatch.setattr("cli_anything.febuildergba.core.data.export_table", fake)

    def test_success_on_session_rom_records_history(self, initialized_state, monkeypatch, tmp_path):
        rom = str(tmp_path / "r.gba")
        initialized_state.session.open_rom(rom, "FE8U", 1)
        self._fake_export(monkeypatch, 0)
        resp = _call_tool(initialized_state, "data_export", {"table": "units", "out_path": "units.tsv"})
        assert resp["result"]["isError"] is False
        assert initialized_state.session.state.history[-1]["op"] == HISTORY_OP_DATA_EXPORT

    def test_failure_does_not_record_history(self, initialized_state, monkeypatch, tmp_path):
        rom = str(tmp_path / "r.gba")
        initialized_state.session.open_rom(rom, "FE8U", 1)
        before = len(initialized_state.session.state.history)
        self._fake_export(monkeypatch, 1)
        resp = _call_tool(initialized_state, "data_export", {"table": "units", "out_path": "units.tsv"})
        assert resp["result"]["isError"] is True
        assert len(initialized_state.session.state.history) == before

    def test_other_rom_override_does_not_record_history(self, initialized_state, monkeypatch, tmp_path):
        rom = str(tmp_path / "r.gba")
        initialized_state.session.open_rom(rom, "FE8U", 1)
        before = len(initialized_state.session.state.history)
        self._fake_export(monkeypatch, 0)
        resp = _call_tool(initialized_state, "data_export",
                           {"table": "units", "out_path": "units.tsv",
                            "rom_path": str(tmp_path / "other.gba")})
        assert resp["result"]["isError"] is False
        assert len(initialized_state.session.state.history) == before

    def test_hardlink_alias_records_session_history(
            self, initialized_state, monkeypatch, tmp_path):
        rom = tmp_path / "r.gba"
        alias = tmp_path / "alias.gba"
        rom.write_bytes(b"rom")
        os.link(rom, alias)
        initialized_state.session.open_rom(str(rom), "FE8U", 3)
        self._fake_export(monkeypatch, 0)

        resp = _call_tool(
            initialized_state,
            "data_export",
            {
                "table": "units",
                "out_path": "units.tsv",
                "rom_path": str(alias),
            },
        )

        assert resp["result"]["isError"] is False
        assert initialized_state.session.state.history[-1]["op"] == HISTORY_OP_DATA_EXPORT


class TestDataImportModifiedFlag:
    def _fake_import(self, monkeypatch, exit_code=0):
        def fake(rom_path, table, in_path, force_version=""):
            return {"table": table, "input_path": in_path, "exit_code": exit_code,
                    "stdout": "", "stderr": ""}
        monkeypatch.setattr("cli_anything.febuildergba.core.data.import_table", fake)

    def test_success_on_session_rom_marks_modified(self, initialized_state, monkeypatch, tmp_path):
        rom = str(tmp_path / "r.gba")
        initialized_state.session.open_rom(rom, "FE8U", 1)
        assert initialized_state.session.state.modified is False
        self._fake_import(monkeypatch, 0)
        resp = _call_tool(initialized_state, "data_import", {"table": "units", "in_path": "u.tsv"})
        assert resp["result"]["isError"] is False
        assert initialized_state.session.state.modified is True

    def test_failure_never_marks_modified(self, initialized_state, monkeypatch, tmp_path):
        rom = str(tmp_path / "r.gba")
        initialized_state.session.open_rom(rom, "FE8U", 1)
        self._fake_import(monkeypatch, 1)
        resp = _call_tool(initialized_state, "data_import", {"table": "units", "in_path": "u.tsv"})
        assert resp["result"]["isError"] is True
        assert initialized_state.session.state.modified is False

    def test_other_rom_override_never_marks_modified(self, initialized_state, monkeypatch, tmp_path):
        rom = str(tmp_path / "r.gba")
        initialized_state.session.open_rom(rom, "FE8U", 1)
        self._fake_import(monkeypatch, 0)
        resp = _call_tool(initialized_state, "data_import",
                           {"table": "units", "in_path": "u.tsv",
                            "rom_path": str(tmp_path / "other.gba")})
        assert resp["result"]["isError"] is False
        assert initialized_state.session.state.modified is False

    def test_hardlink_alias_marks_session_modified(
            self, initialized_state, monkeypatch, tmp_path):
        rom = tmp_path / "r.gba"
        alias = tmp_path / "alias.gba"
        rom.write_bytes(b"rom")
        os.link(rom, alias)
        initialized_state.session.open_rom(str(rom), "FE8U", 3)
        self._fake_import(monkeypatch, 0)

        resp = _call_tool(
            initialized_state,
            "data_import",
            {
                "table": "units",
                "in_path": "u.tsv",
                "rom_path": str(alias),
            },
        )

        assert resp["result"]["isError"] is False
        assert initialized_state.session.state.modified is True
        assert initialized_state.session.state.history[-1]["op"] == HISTORY_OP_DATA_IMPORT

    def test_no_phantom_session_when_none_open(self, initialized_state, monkeypatch):
        assert initialized_state.session.is_open() is False
        self._fake_import(monkeypatch, 0)
        resp = _call_tool(initialized_state, "data_import",
                           {"table": "units", "in_path": "u.tsv", "rom_path": "explicit.gba"})
        assert resp["result"]["isError"] is False
        assert initialized_state.session.is_open() is False


class TestPaletteImportSessionEffects:
    """palette_import must follow the same session-owned history/modified
    rules as data_import: only on success AND same-as-session-ROM."""

    def _fake_import_palette(self, monkeypatch, exit_code=0):
        def fake(rom_path, addr, in_path, force_version=""):
            return {"rom_path": rom_path, "addr": addr, "input_path": in_path,
                    "exit_code": exit_code, "stdout": "", "stderr": ""}
        monkeypatch.setattr("cli_anything.febuildergba.core.verbs.import_palette", fake)

    def test_success_on_session_rom_records_history_and_marks_modified(
            self, initialized_state, monkeypatch, tmp_path):
        rom = str(tmp_path / "r.gba")
        initialized_state.session.open_rom(rom, "FE8U", 1)
        assert initialized_state.session.state.modified is False
        self._fake_import_palette(monkeypatch, 0)
        resp = _call_tool(initialized_state, "palette_import", {"addr": "0x5524", "in_path": "p.pal"})
        assert resp["result"]["isError"] is False
        assert initialized_state.session.state.modified is True
        assert initialized_state.session.state.history[-1]["op"] == HISTORY_OP_IMPORT_PALETTE

    def test_failure_does_not_record_history_or_mark_modified(
            self, initialized_state, monkeypatch, tmp_path):
        rom = str(tmp_path / "r.gba")
        initialized_state.session.open_rom(rom, "FE8U", 1)
        before = len(initialized_state.session.state.history)
        self._fake_import_palette(monkeypatch, 1)
        resp = _call_tool(initialized_state, "palette_import", {"addr": "0x5524", "in_path": "p.pal"})
        assert resp["result"]["isError"] is True
        assert initialized_state.session.state.modified is False
        assert len(initialized_state.session.state.history) == before

    def test_other_rom_override_does_not_record_history_or_mark_modified(
            self, initialized_state, monkeypatch, tmp_path):
        rom = str(tmp_path / "r.gba")
        initialized_state.session.open_rom(rom, "FE8U", 1)
        before = len(initialized_state.session.state.history)
        self._fake_import_palette(monkeypatch, 0)
        resp = _call_tool(initialized_state, "palette_import",
                           {"addr": "0x5524", "in_path": "p.pal",
                            "rom_path": str(tmp_path / "other.gba")})
        assert resp["result"]["isError"] is False
        assert initialized_state.session.state.modified is False
        assert len(initialized_state.session.state.history) == before

    def test_hardlink_alias_records_history_and_marks_modified(
            self, initialized_state, monkeypatch, tmp_path):
        rom = tmp_path / "r.gba"
        alias = tmp_path / "alias.gba"
        rom.write_bytes(b"rom")
        os.link(rom, alias)
        initialized_state.session.open_rom(str(rom), "FE8U", 3)
        self._fake_import_palette(monkeypatch, 0)

        resp = _call_tool(
            initialized_state,
            "palette_import",
            {
                "addr": "0x5524",
                "in_path": "p.pal",
                "rom_path": str(alias),
            },
        )

        assert resp["result"]["isError"] is False
        assert initialized_state.session.state.modified is True
        assert initialized_state.session.state.history[-1]["op"] == HISTORY_OP_IMPORT_PALETTE


class TestImageQuantizeContract:
    def _fake_quantize(self, monkeypatch, calls):
        def fake(
                in_path, out_path, palette_no, no_scale,
                no_reserve_1st, ignore_tsa):
            calls.append({
                "palette_no": palette_no,
                "no_reserve_1st": no_reserve_1st,
            })
            return {
                "output_path": out_path,
                "file_size": 0,
                "exit_code": 0,
                "stdout": "",
                "stderr": "",
            }

        monkeypatch.setattr(
            "cli_anything.febuildergba.core.export.decrease_color",
            fake,
        )

    @pytest.mark.parametrize(
        ("arguments", "expected_colors"),
        [
            ({"in_path": "in.png", "out_path": "out.png"}, 16),
            (
                {
                    "in_path": "in.png",
                    "out_path": "out.png",
                    "palette_no": 256,
                },
                256,
            ),
            (
                {
                    "in_path": "in.png",
                    "out_path": "out.png",
                    "palette_no": 1,
                    "no_reserve_1st": True,
                },
                1,
            ),
        ],
    )
    def test_backend_color_ranges_reach_wrapper(
            self, initialized_state, monkeypatch, arguments, expected_colors):
        calls = []
        self._fake_quantize(monkeypatch, calls)

        resp = _call_tool(initialized_state, "image_quantize", arguments)

        assert resp["result"]["isError"] is False
        assert calls[0]["palette_no"] == expected_colors

    def test_one_color_requires_no_reserve(
            self, initialized_state, monkeypatch):
        calls = []
        self._fake_quantize(monkeypatch, calls)

        resp = _call_tool(
            initialized_state,
            "image_quantize",
            {
                "in_path": "in.png",
                "out_path": "out.png",
                "palette_no": 1,
            },
        )

        assert resp["error"]["code"] == srv.INVALID_PARAMS
        assert calls == []

    @pytest.mark.parametrize("palette_no", [0, 257])
    def test_color_count_outside_backend_range_is_invalid_params(
            self, initialized_state, palette_no):
        resp = _call_tool(
            initialized_state,
            "image_quantize",
            {
                "in_path": "in.png",
                "out_path": "out.png",
                "palette_no": palette_no,
            },
        )
        assert resp["error"]["code"] == srv.INVALID_PARAMS


class TestForceVersionPrecedence:
    """Explicit force_version wins over the session's; the session's is used
    only as a fallback when the argument is omitted."""

    def test_explicit_force_version_overrides_session(self, initialized_state, monkeypatch, tmp_path):
        rom = str(tmp_path / "r.gba")
        initialized_state.session.open_rom(rom, "FE8U", 1, "FE8U")
        seen = {}

        def fake_rom_info(rom_path, force_version=""):
            seen["force_version"] = force_version
            return {"rom_path": rom_path, "rom_size": 1, "rom_size_hex": "0x1",
                    "rom_size_mb": 0.0, "detected_version": "FE8U",
                    "force_version": force_version, "lint_output": "", "lint_exit_code": 0}
        monkeypatch.setattr("cli_anything.febuildergba.core.project.rom_info", fake_rom_info)

        resp = _call_tool(initialized_state, "rom_info", {"force_version": "FE7U"})
        assert resp["result"]["isError"] is False
        assert seen["force_version"] == "FE7U"

    def test_falls_back_to_session_force_version(self, initialized_state, monkeypatch, tmp_path):
        rom = str(tmp_path / "r.gba")
        initialized_state.session.open_rom(rom, "FE8U", 1, "FE8J")
        seen = {}

        def fake_rom_info(rom_path, force_version=""):
            seen["force_version"] = force_version
            return {"rom_path": rom_path, "rom_size": 1, "rom_size_hex": "0x1",
                    "rom_size_mb": 0.0, "detected_version": "FE8U",
                    "force_version": force_version, "lint_output": "", "lint_exit_code": 0}
        monkeypatch.setattr("cli_anything.febuildergba.core.project.rom_info", fake_rom_info)

        resp = _call_tool(initialized_state, "rom_info", {})
        assert resp["result"]["isError"] is False
        assert seen["force_version"] == "FE8J"

    def test_no_force_version_anywhere_is_empty(self, initialized_state, monkeypatch, tmp_path):
        rom = str(tmp_path / "r.gba")
        initialized_state.session.open_rom(rom, "FE8U", 1)  # no force_version
        seen = {}

        def fake_rom_info(rom_path, force_version=""):
            seen["force_version"] = force_version
            return {"rom_path": rom_path, "rom_size": 1, "rom_size_hex": "0x1",
                    "rom_size_mb": 0.0, "detected_version": "FE8U",
                    "force_version": force_version, "lint_output": "", "lint_exit_code": 0}
        monkeypatch.setattr("cli_anything.febuildergba.core.project.rom_info", fake_rom_info)

        resp = _call_tool(initialized_state, "rom_info", {})
        assert resp["result"]["isError"] is False
        assert seen["force_version"] == ""


# ── Advisory vs hard tool errors ──────────────────────────────────────────

class TestAdvisoryVsHardErrors:
    def test_checksum_advisory_exit2_is_not_error(self, initialized_state, monkeypatch, tmp_path):
        rom_path = tmp_path / "r.gba"
        _write_valid_test_rom(rom_path)
        rom = str(rom_path)
        initialized_state.session.open_rom(rom, "FE8U", rom_path.stat().st_size)

        def fake_checksum(rom_path, force_version=""):
            return {"exit_code": 2, "stdout": "", "stderr": "", "rom_path": rom_path,
                    "valid": False, "actual": "0xAB", "expected": "0xCD"}
        monkeypatch.setattr("cli_anything.febuildergba.core.verbs.checksum", fake_checksum)

        resp = _call_tool(initialized_state, "rom_checksum", {})
        assert resp["result"]["isError"] is False

    def test_checksum_hard_exit1_is_error(self, initialized_state, monkeypatch, tmp_path):
        rom_path = tmp_path / "r.gba"
        _write_valid_test_rom(rom_path)
        rom = str(rom_path)
        initialized_state.session.open_rom(rom, "FE8U", rom_path.stat().st_size)

        def fake_checksum(rom_path, force_version=""):
            return {"exit_code": 1, "stdout": "", "stderr": "boom", "rom_path": rom_path,
                    "valid": None, "actual": None, "expected": None}
        monkeypatch.setattr("cli_anything.febuildergba.core.verbs.checksum", fake_checksum)

        resp = _call_tool(initialized_state, "rom_checksum", {})
        assert resp["result"]["isError"] is True

    @pytest.mark.parametrize("explicit_path", [False, True])
    def test_checksum_rejects_non_rom_before_backend_without_disclosure(
            self, initialized_state, monkeypatch, tmp_path, explicit_path):
        not_rom = tmp_path / "document.bin"
        content = bytearray(0x100000)
        content[0xA0:0xAC] = b"LOCAL SECRET"
        content[0xAC:0xB0] = b"LEAK"
        not_rom.write_bytes(content)
        backend_calls = []

        def fake_checksum(rom_path, force_version=""):
            backend_calls.append((rom_path, force_version))
            return {
                "exit_code": 2,
                "stdout": "LOCAL SECRET LEAK",
                "stderr": "",
            }

        monkeypatch.setattr(
            "cli_anything.febuildergba.core.verbs.checksum",
            fake_checksum,
        )
        arguments = {"rom_path": str(not_rom)} if explicit_path else {}
        if not explicit_path:
            initialized_state.session.open_rom(
                str(not_rom), "FE8U", not_rom.stat().st_size,
            )

        resp = _call_tool(initialized_state, "rom_checksum", arguments)

        result = resp["result"]
        assert result["isError"] is True
        text = result["content"][0]["text"]
        assert "missing fixed header byte" in text
        assert "LOCAL SECRET" not in text
        assert "LEAK" not in text
        assert backend_calls == []

    def test_checksum_allows_header_checksum_mismatch(
            self, initialized_state, monkeypatch, tmp_path):
        rom_path = tmp_path / "bad-checksum.gba"
        _write_valid_test_rom(rom_path)
        content = bytearray(rom_path.read_bytes())
        content[0xBD] ^= 0xFF
        rom_path.write_bytes(content)
        backend_calls = []

        def fake_checksum(rom_path_arg, force_version=""):
            backend_calls.append((rom_path_arg, force_version))
            return {
                "exit_code": 2,
                "stdout": "",
                "stderr": "",
                "rom_path": rom_path_arg,
                "valid": False,
                "actual": "0x00",
                "expected": "0xFF",
            }

        monkeypatch.setattr(
            "cli_anything.febuildergba.core.verbs.checksum",
            fake_checksum,
        )

        resp = _call_tool(
            initialized_state,
            "rom_checksum",
            {"rom_path": str(rom_path)},
        )

        assert resp["result"]["isError"] is False
        assert backend_calls == [(str(rom_path), "")]

    def test_data_roundtrip_advisory_exit2(self, initialized_state, monkeypatch, tmp_path):
        rom = str(tmp_path / "r.gba")
        initialized_state.session.open_rom(rom, "FE8U", 1)

        def fake(rom_path, table, force_version=""):
            return {"table": table, "lossless": False, "exit_code": 2, "stdout": "", "stderr": ""}
        monkeypatch.setattr("cli_anything.febuildergba.core.data.roundtrip_table", fake)

        resp = _call_tool(initialized_state, "data_roundtrip", {})
        assert resp["result"]["isError"] is False

    def test_text_roundtrip_advisory_exit2_is_not_error(self, initialized_state, monkeypatch, tmp_path):
        rom = str(tmp_path / "r.gba")
        initialized_state.session.open_rom(rom, "FE8U", 1)

        def fake(rom_path, output_prefix="", force_version=""):
            return {"lossless": False, "exit_code": 2, "stdout": "", "stderr": ""}
        monkeypatch.setattr("cli_anything.febuildergba.core.text.roundtrip_text", fake)

        resp = _call_tool(initialized_state, "text_roundtrip", {})
        assert resp["result"]["isError"] is False

    def test_text_roundtrip_hard_exit1_is_error(self, initialized_state, monkeypatch, tmp_path):
        rom = str(tmp_path / "r.gba")
        initialized_state.session.open_rom(rom, "FE8U", 1)

        def fake(rom_path, output_prefix="", force_version=""):
            return {"lossless": None, "exit_code": 1, "stdout": "", "stderr": "boom"}
        monkeypatch.setattr("cli_anything.febuildergba.core.text.roundtrip_text", fake)

        resp = _call_tool(initialized_state, "text_roundtrip", {})
        assert resp["result"]["isError"] is True

    def test_backend_unavailable_is_not_a_tool_error(self, initialized_state, monkeypatch):
        def fake_check_backend():
            return {"available": False, "error": "not found"}
        monkeypatch.setattr("cli_anything.febuildergba.utils.febuildergba_backend.check_backend",
                            fake_check_backend)
        resp = _call_tool(initialized_state, "backend_check", {})
        assert resp["result"]["isError"] is False
        payload = json.loads(resp["result"]["content"][0]["text"])
        assert payload["available"] is False

    def test_backend_os_probe_failure_is_not_a_tool_error(
            self, initialized_state, monkeypatch):
        from cli_anything.febuildergba.utils import febuildergba_backend as backend
        monkeypatch.setattr(backend, "find_febuildergba_cli", lambda: ["blocked-cli"])

        def fail_launch(*args, **kwargs):
            raise PermissionError("execution denied")

        monkeypatch.setattr(backend.subprocess, "run", fail_launch)

        resp = _call_tool(initialized_state, "backend_check", {})

        assert resp["result"]["isError"] is False
        payload = json.loads(resp["result"]["content"][0]["text"])
        assert payload["available"] is False
        assert payload["error"] == (
            "Failed to run blocked-cli --version: execution denied"
        )
        assert payload["error_truncated"] is False


# ── Bounds ────────────────────────────────────────────────────────────────

class TestBounds:
    def test_text_search_default_and_max_limit_schema(self):
        schema = srv.TOOL_SCHEMAS["text_search"]["properties"]["limit"]
        assert schema["maximum"] == 500

    def test_text_search_truncation_metadata(self, initialized_state, monkeypatch, tmp_path):
        rom = str(tmp_path / "r.gba")
        initialized_state.session.open_rom(rom, "FE8U", 1)
        matches = [{"id": str(i), "text": f"row{i}"} for i in range(120)]

        def fake_search(rom_path, query, force_version=""):
            return {"query": query, "matches": matches, "match_count": len(matches), "exit_code": 0}
        monkeypatch.setattr("cli_anything.febuildergba.core.text.search_text", fake_search)

        resp = _call_tool(initialized_state, "text_search", {"query": "row", "limit": 50})
        payload = json.loads(resp["result"]["content"][0]["text"])
        assert payload["total"] == 120
        assert payload["returned"] == 50
        assert payload["truncated"] is True

    def test_lint_bounds_metadata(self, initialized_state, monkeypatch, tmp_path):
        rom = str(tmp_path / "r.gba")
        initialized_state.session.open_rom(rom, "FE8U", 1)
        errors = [f"ERROR {i}" for i in range(250)]

        def fake_lint(rom_path, force_version=""):
            return {"rom_path": rom_path, "clean": False, "error_count": len(errors),
                    "warning_count": 0, "errors": errors, "warnings": [], "info": [],
                    "exit_code": 0, "raw_output": ""}
        monkeypatch.setattr("cli_anything.febuildergba.core.lint.lint_rom", fake_lint)

        resp = _call_tool(initialized_state, "rom_lint", {"limit": 200})
        payload = json.loads(resp["result"]["content"][0]["text"])
        assert payload["errors_total"] == 250
        assert len(payload["errors"]) == 200
        assert payload["errors_truncated"] is True

    def test_names_resolve_ids_max_256(self, initialized_state):
        resp = _call_tool(initialized_state, "names_resolve",
                          {"kind": "unit", "ids": list(range(257))})
        assert resp["error"]["code"] == srv.INVALID_PARAMS

    @pytest.mark.parametrize("invalid_id", [-1, 1 << 32])
    def test_names_resolve_id_outside_uint32_is_invalid_params(
            self, initialized_state, invalid_id):
        resp = _call_tool(
            initialized_state,
            "names_resolve",
            {"kind": "unit", "ids": [invalid_id]},
        )
        assert resp["error"]["code"] == srv.INVALID_PARAMS

    def test_names_resolve_uint32_boundaries_pass_schema(self, initialized_state):
        resp = _call_tool(
            initialized_state,
            "names_resolve",
            {"kind": "unit", "ids": [0, 0xFFFFFFFF]},
        )
        assert "error" not in resp

    def test_names_resolve_bounds_each_requested_name(
            self, initialized_state, monkeypatch, tmp_path):
        rom = str(tmp_path / "r.gba")
        initialized_state.session.open_rom(rom, "FE8U", 1)
        overlong = "N" * (srv.MAX_ITEM_STRING_LEN + 17)

        def fake_resolve(rom_path, kind, ids, force_version=""):
            return {
                "names": {"1": overlong, "2": "short", "999": "extra"},
                "count": 3,
                "stdout": "",
                "exit_code": 0,
            }

        monkeypatch.setattr(
            "cli_anything.febuildergba.core.export.resolve_names",
            fake_resolve,
        )

        resp = _call_tool(
            initialized_state,
            "names_resolve",
            {"kind": "unit", "ids": [1, 2]},
        )
        payload = json.loads(resp["result"]["content"][0]["text"])

        assert len(payload["names"]["1"]) == srv.MAX_ITEM_STRING_LEN
        assert payload["names"]["2"] == "short"
        assert "999" not in payload["names"]
        assert payload["count"] == 2
        assert payload["names_truncated"] is True
        assert payload["names_truncated_count"] == 1
        assert payload["names_original_lengths"] == {"1": len(overlong)}
        assert payload["names_omitted_count"] == 1

    def test_stdout_field_bounded(self):
        big = "x" * (srv.MAX_STRING_LEN + 10)
        d = {"stdout": big}
        srv._bound_string_fields(d)
        assert len(d["stdout"]) == srv.MAX_STRING_LEN
        assert d["stdout_truncated"] is True
        assert d["stdout_original_length"] == len(big)

    def test_short_stdout_not_marked_truncated(self):
        d = {"stdout": "short"}
        srv._bound_string_fields(d)
        assert d["stdout_truncated"] is False
        assert "stdout_original_length" not in d

    def test_version_field_bounded(self):
        big = "v" * (srv.MAX_STRING_LEN + 10)
        d = {"version": big}
        srv._bound_string_fields(d)
        assert len(d["version"]) == srv.MAX_STRING_LEN
        assert d["version_truncated"] is True
        assert d["version_original_length"] == len(big)

    def test_backend_check_rejects_overlong_version_at_source(
            self, initialized_state, monkeypatch):
        from cli_anything.febuildergba.utils import febuildergba_backend as backend

        monkeypatch.setattr(
            backend, "find_febuildergba_cli", lambda: ["fake-cli"],
        )
        monkeypatch.setattr(
            backend,
            "run_cli",
            lambda args: subprocess.CompletedProcess(
                ["fake-cli", "--version"],
                0,
                stdout="v" * (backend.MAX_VERSION_TEXT_LEN + 1),
                stderr="",
            ),
        )

        resp = _call_tool(initialized_state, "backend_check", {})
        payload = json.loads(resp["result"]["content"][0]["text"])

        assert payload["available"] is False
        assert payload["error"] == (
            "FEBuilderGBA.CLI version check output exceeded 4096 characters"
        )
        assert "version" not in payload

    @pytest.mark.parametrize("tool_name", ["rom_info", "session_open"])
    def test_rom_metadata_lint_output_is_bounded(
            self, initialized_state, monkeypatch, tool_name):
        big = "x" * (srv.MAX_STRING_LEN + 17)

        def fake_rom_info(rom_path, force_version=""):
            return {
                "rom_path": rom_path,
                "rom_size": 1,
                "rom_size_hex": "0x1",
                "rom_size_mb": 0.0,
                "detected_version": "FE8U",
                "force_version": force_version,
                "lint_output": big,
                "lint_exit_code": 0,
            }

        monkeypatch.setattr(
            "cli_anything.febuildergba.core.project.rom_info",
            fake_rom_info,
        )
        resp = _call_tool(
            initialized_state, tool_name, {"rom_path": "test.gba"},
        )
        payload = json.loads(resp["result"]["content"][0]["text"])
        assert len(payload["lint_output"]) == srv.MAX_STRING_LEN
        assert payload["lint_output_truncated"] is True
        assert payload["lint_output_original_length"] == len(big)

    # ── maxLength bounds on agent-controlled free strings (item 7) ──────

    def test_rom_path_over_max_length_is_invalid_params(self, initialized_state):
        too_long = "a" * (srv.MAX_PATH_LEN + 1)
        resp = _call_tool(initialized_state, "rom_info", {"rom_path": too_long})
        assert resp["error"]["code"] == srv.INVALID_PARAMS

    def test_out_path_over_max_length_is_invalid_params(self, initialized_state):
        too_long = "a" * (srv.MAX_PATH_LEN + 1)
        resp = _call_tool(initialized_state, "data_export",
                          {"table": "units", "out_path": too_long})
        assert resp["error"]["code"] == srv.INVALID_PARAMS

    @pytest.mark.parametrize(
        ("tool_name", "arguments"),
        [
            ("session_open", {"rom_path": ""}),
            ("rom_info", {"rom_path": ""}),
            ("data_export", {"table": "units", "out_path": ""}),
            ("data_import", {"table": "units", "in_path": ""}),
            ("image_quantize", {"in_path": "", "out_path": "out.png"}),
            ("image_quantize", {"in_path": "in.png", "out_path": ""}),
            (
                "image_convert_map",
                {"in_path": "", "out_img": "out.img", "out_tsa": "out.tsa"},
            ),
            (
                "image_convert_map",
                {"in_path": "in.png", "out_img": "", "out_tsa": "out.tsa"},
            ),
            (
                "image_convert_map",
                {"in_path": "in.png", "out_img": "out.img", "out_tsa": ""},
            ),
            ("palette_export", {"addr": "0x100", "out_path": ""}),
            ("palette_import", {"addr": "0x100", "in_path": ""}),
            ("lz77", {"mode": "compress", "in_path": "", "out_path": "out.bin"}),
            ("lz77", {"mode": "compress", "in_path": "in.bin", "out_path": ""}),
        ],
    )
    def test_empty_path_is_invalid_params(
            self, initialized_state, tool_name, arguments):
        resp = _call_tool(initialized_state, tool_name, arguments)
        assert resp["error"]["code"] == srv.INVALID_PARAMS

    def test_query_over_max_length_is_invalid_params(self, initialized_state):
        too_long = "a" * (srv.MAX_QUERY_LEN + 1)
        resp = _call_tool(initialized_state, "text_search", {"query": too_long})
        assert resp["error"]["code"] == srv.INVALID_PARAMS

    def test_table_over_max_length_is_invalid_params(self, initialized_state):
        too_long = "a" * (srv.MAX_TABLE_NAME_LEN + 1)
        resp = _call_tool(initialized_state, "data_export",
                          {"table": too_long, "out_path": "out.tsv"})
        assert resp["error"]["code"] == srv.INVALID_PARAMS

    def test_addr_over_max_length_is_invalid_params(self, initialized_state):
        too_long = "a" * (srv.MAX_ADDR_LEN + 1)
        resp = _call_tool(initialized_state, "palette_export",
                          {"addr": too_long, "out_path": "out.pal"})
        assert resp["error"]["code"] == srv.INVALID_PARAMS

    def test_rom_path_at_max_length_is_accepted_by_schema(self, initialized_state):
        # Exactly at the bound must still validate (schema-wise); the tool
        # will then fail for business reasons (file doesn't exist) — that's
        # an isError result, never a protocol error.
        at_bound = "a" * srv.MAX_PATH_LEN
        resp = _call_tool(initialized_state, "rom_info", {"rom_path": at_bound})
        assert "error" not in resp

    # ── Per-item / recursive truncation metadata (item 8) ───────────────

    def test_text_search_match_text_is_truncated_per_item(self, initialized_state, monkeypatch, tmp_path):
        rom = str(tmp_path / "r.gba")
        initialized_state.session.open_rom(rom, "FE8U", 1)
        overlong = "x" * (srv.MAX_ITEM_STRING_LEN + 500)
        matches = [{"id": "1", "text": overlong}, {"id": "2", "text": "short"}]

        def fake_search(rom_path, query, force_version=""):
            return {"query": query, "matches": matches, "match_count": len(matches), "exit_code": 0}
        monkeypatch.setattr("cli_anything.febuildergba.core.text.search_text", fake_search)

        resp = _call_tool(initialized_state, "text_search", {"query": "x"})
        payload = json.loads(resp["result"]["content"][0]["text"])
        assert len(payload["matches"][0]["text"]) == srv.MAX_ITEM_STRING_LEN
        assert payload["matches"][0]["text_truncated"] is True
        assert payload["matches"][1]["text_truncated"] is False
        assert payload["matches_text_truncated_count"] == 1

    def test_lint_array_strings_truncated_per_item(self, initialized_state, monkeypatch, tmp_path):
        rom = str(tmp_path / "r.gba")
        initialized_state.session.open_rom(rom, "FE8U", 1)
        overlong = "E" * (srv.MAX_ITEM_STRING_LEN + 100)

        def fake_lint(rom_path, force_version=""):
            return {"rom_path": rom_path, "clean": False, "error_count": 1,
                    "warning_count": 0, "errors": [overlong], "warnings": [], "info": [],
                    "exit_code": 0, "raw_output": ""}
        monkeypatch.setattr("cli_anything.febuildergba.core.lint.lint_rom", fake_lint)

        resp = _call_tool(initialized_state, "rom_lint", {})
        payload = json.loads(resp["result"]["content"][0]["text"])
        assert len(payload["errors"][0]) == srv.MAX_ITEM_STRING_LEN
        assert payload["errors_items_truncated_count"] == 1

    def test_tool_error_string_is_bounded(self, initialized_state, monkeypatch):
        overlong = "E" * (srv.MAX_STRING_LEN + 123)

        def fail(_session, _arguments):
            raise RuntimeError(overlong)

        monkeypatch.setitem(srv.TOOL_HANDLERS, "backend_check", fail)
        resp = _call_tool(initialized_state, "backend_check")
        payload = json.loads(resp["result"]["content"][0]["text"])

        assert resp["result"]["isError"] is True
        assert len(payload["error"]) == srv.MAX_STRING_LEN
        assert payload["error_truncated"] is True
        assert payload["error_original_length"] == len(overlong)

    @pytest.mark.parametrize(
        "bad_path",
        [
            ["not", "a", "path"],
            {"not": "a path"},
            "x" * (srv.MAX_PATH_LEN + 1),
        ],
        ids=["list", "object", "overlong"],
    )
    def test_persisted_invalid_rom_path_fails_closed_on_all_surfaces(
            self, tmp_path, bad_path):
        state = _initialized_state_from_session_payload(tmp_path, {
            "rom_path": bad_path,
            "history": [{"op": "should-not-be-exposed-while-closed"}],
        })

        status = _call_tool(state, "session_status")
        status_payload = json.loads(status["result"]["content"][0]["text"])
        assert status_payload["open"] is False
        assert status_payload["rom_path"] == ""

        history = _call_tool(state, "session_history")
        history_payload = json.loads(history["result"]["content"][0]["text"])
        assert history_payload["open"] is False
        assert history_payload["history"] == []

        info = _call_tool(state, "rom_info")
        info_payload = json.loads(info["result"]["content"][0]["text"])
        assert info["result"]["isError"] is True
        assert "No ROM specified" in info_payload["error"]

        resource = srv.handle_line(state, json.dumps(_req(
            "resources/read", {"uri": "febuildergba://rom/metadata"},
        )))
        resource_payload = json.loads(
            resource["result"]["contents"][0]["text"],
        )
        assert resource_payload == {"open": False, "truncated": False}

    def test_persisted_invalid_force_version_fails_before_handler(
            self, tmp_path, monkeypatch):
        state = _initialized_state_from_session_payload(tmp_path, {
            "rom_path": str(tmp_path / "r.gba"),
            "force_version": "INVALID",
        })
        calls = []

        def should_not_run(*args, **kwargs):
            calls.append((args, kwargs))
            raise AssertionError("rom_info handler must not run")

        monkeypatch.setattr(
            "cli_anything.febuildergba.core.project.rom_info",
            should_not_run,
        )
        resp = _call_tool(state, "rom_info")
        payload = json.loads(resp["result"]["content"][0]["text"])

        assert resp["result"]["isError"] is True
        assert "force_version is invalid" in payload["error"]
        assert calls == []

    def test_session_history_resource_bounds_overlong_values(self, initialized_state, tmp_path):
        rom = str(tmp_path / "r.gba")
        initialized_state.session.open_rom(rom, "FE8U", 1)
        overlong = "z" * (srv.MAX_ITEM_STRING_LEN + 250)
        initialized_state.session.record_operation("data_export", {"out": overlong})

        resp = srv.handle_line(initialized_state, json.dumps(
            _req("resources/read", {"uri": "febuildergba://session/history"})))
        payload = json.loads(resp["result"]["contents"][0]["text"])
        last = payload["history"][-1]
        assert len(last["out"]) == srv.MAX_ITEM_STRING_LEN
        assert payload["truncated"] is True

    def test_session_history_tool_bounds_overlong_values(self, initialized_state, tmp_path):
        initialized_state.session.open_rom(str(tmp_path / "r.gba"), "FE8U", 1)
        overlong = "z" * (srv.MAX_ITEM_STRING_LEN + 250)
        initialized_state.session.record_operation("data_export", {"out": overlong})

        resp = _call_tool(initialized_state, "session_history", {"count": 1})
        payload = json.loads(resp["result"]["content"][0]["text"])
        assert len(payload["history"][0]["out"]) == srv.MAX_ITEM_STRING_LEN
        assert payload["truncated"] is True

    def test_session_status_tool_bounds_persisted_values(self, initialized_state, tmp_path):
        initialized_state.session.open_rom(str(tmp_path / "r.gba"), "FE8U", 1)
        initialized_state.session.state.rom_path = (
            "z" * (srv.MAX_ITEM_STRING_LEN + 250)
        )

        resp = _call_tool(initialized_state, "session_status", {})
        payload = json.loads(resp["result"]["content"][0]["text"])
        assert len(payload["rom_path"]) == srv.MAX_ITEM_STRING_LEN
        assert payload["truncated"] is True

    def test_session_resource_not_truncated_for_short_values(self, initialized_state, tmp_path):
        rom = str(tmp_path / "r.gba")
        initialized_state.session.open_rom(rom, "FE8U", 1)
        resp = srv.handle_line(initialized_state, json.dumps(
            _req("resources/read", {"uri": "febuildergba://session"})))
        payload = json.loads(resp["result"]["contents"][0]["text"])
        assert payload["truncated"] is False


# ── Schema validator unit tests ──────────────────────────────────────────

class TestSchemaValidator:
    def test_object_missing_required(self):
        schema = {"type": "object", "properties": {"a": {"type": "string"}}, "required": ["a"]}
        assert srv.validate_schema(schema, {}) is not None

    def test_object_additional_properties_false(self):
        schema = {"type": "object", "properties": {"a": {"type": "string"}},
                  "required": [], "additionalProperties": False}
        assert srv.validate_schema(schema, {"b": 1}) is not None
        assert srv.validate_schema(schema, {"a": "x"}) is None

    def test_bool_rejected_for_integer(self):
        schema = {"type": "integer"}
        assert srv.validate_schema(schema, True) is not None
        assert srv.validate_schema(schema, 1) is None

    def test_enum(self):
        schema = {"type": "string", "enum": ["a", "b"]}
        assert srv.validate_schema(schema, "c") is not None
        assert srv.validate_schema(schema, "a") is None

    def test_array_bounds(self):
        schema = {"type": "array", "items": {"type": "integer"}, "minItems": 1, "maxItems": 2}
        assert srv.validate_schema(schema, []) is not None
        assert srv.validate_schema(schema, [1, 2, 3]) is not None
        assert srv.validate_schema(schema, [1]) is None


# ── serve() outer-loop regression: unexpected failures must not be silent ──

class TestServeRegression:
    def test_unexpected_failure_emits_internal_error_and_recovers(self, tmp_path, monkeypatch):
        """A bug in handle_line() itself (not a _ProtocolError, not caught
        anywhere else) must still produce a flushed -32603 response with a
        generic message (id null) — and the loop must keep processing
        subsequent lines rather than dying or silently dropping the line."""
        calls = {"n": 0}
        real_handle_line = srv.handle_line

        def flaky_handle_line(state, line):
            calls["n"] += 1
            if calls["n"] == 1:
                raise RuntimeError("boom: a secret internal detail")
            return real_handle_line(state, line)

        monkeypatch.setattr(srv, "handle_line", flaky_handle_line)

        good_req = json.dumps(_req("ping"))
        in_stream = io.StringIO("this line triggers the bug\n" + good_req + "\n")
        out_stream = io.StringIO()

        srv.serve(session_file=str(tmp_path / "session.json"),
                  in_stream=in_stream, out_stream=out_stream)

        lines = [l for l in out_stream.getvalue().splitlines() if l.strip()]
        assert len(lines) == 2

        first = json.loads(lines[0])
        assert first["id"] is None
        assert first["error"]["code"] == srv.INTERNAL_ERROR
        assert first["error"]["message"] == "Internal error"
        assert "boom" not in json.dumps(first)  # never leak exception text to the client

        second = json.loads(lines[1])
        # The important assertion is that the loop kept running after the
        # injected failure and processed the next line normally.
        assert second["jsonrpc"] == "2.0"
        assert second["result"] == {}

    def test_oversized_line_is_drained_and_next_request_is_processed(self, tmp_path):
        oversized = "x" * (srv.MAX_REQUEST_LINE_CHARS + 100)
        good_req = json.dumps(_req("ping"))
        in_stream = io.StringIO(oversized + "\n" + good_req + "\n")
        out_stream = io.StringIO()

        srv.serve(session_file=str(tmp_path / "session.json"),
                  in_stream=in_stream, out_stream=out_stream)

        lines = [json.loads(line) for line in out_stream.getvalue().splitlines()]
        assert len(lines) == 2
        assert lines[0]["error"]["code"] == srv.INVALID_REQUEST
        assert lines[1]["result"] == {}

    def test_serve_refreshes_before_status_and_resource_lines(self, tmp_path):
        session_path = tmp_path / "session.json"
        initial = Session(str(session_path))
        initial.open_rom("/fake/a.gba", "FE8U")
        external = Session(str(session_path))

        class InterleavingInput(io.StringIO):
            def __init__(self, text, after_initialize):
                super().__init__(text)
                self.reads = 0
                self.after_initialize = after_initialize

            def readline(self, *args, **kwargs):
                if self.reads == 1:
                    self.after_initialize()
                self.reads += 1
                return super().readline(*args, **kwargs)

        initialize = json.dumps(_req("initialize", _init_params()))
        status = json.dumps(_req(
            "tools/call", {"name": "session_status", "arguments": {}}, id_=2,
        ))
        resource = json.dumps(_req(
            "resources/read", {"uri": "febuildergba://session"}, id_=3,
        ))
        in_stream = InterleavingInput(
            "\n".join((initialize, status, resource)) + "\n",
            lambda: external.open_rom("/fake/b.gba", "FE8U"),
        )
        out_stream = io.StringIO()

        srv.serve(
            session_file=str(session_path),
            in_stream=in_stream,
            out_stream=out_stream,
        )

        responses = [json.loads(line) for line in out_stream.getvalue().splitlines()]
        status_payload = json.loads(responses[1]["result"]["content"][0]["text"])
        resource_payload = json.loads(responses[2]["result"]["contents"][0]["text"])
        assert status_payload["rom_path"].endswith("b.gba")
        assert resource_payload["rom_path"].endswith("b.gba")

    def test_serve_data_export_after_reopen_does_not_restore_stale_session(
            self, tmp_path, monkeypatch):
        from cli_anything.febuildergba.core import data

        session_path = tmp_path / "session.json"
        initial = Session(str(session_path))
        initial.open_rom("/fake/a.gba", "FE8U")
        external = Session(str(session_path))

        def export_then_reopen(*args):
            external.open_rom("/fake/b.gba", "FE8U")
            return {"exit_code": 0, "stdout": "", "stderr": ""}

        monkeypatch.setattr(data, "export_table", export_then_reopen)
        initialize = json.dumps(_req("initialize", _init_params()))
        export = json.dumps(_req(
            "tools/call",
            {
                "name": "data_export",
                "arguments": {
                    "rom_path": "/fake/a.gba",
                    "table": "units",
                    "out_path": "units.tsv",
                },
            },
            id_=2,
        ))
        in_stream = io.StringIO("\n".join((initialize, export)) + "\n")
        out_stream = io.StringIO()

        srv.serve(
            session_file=str(session_path),
            in_stream=in_stream,
            out_stream=out_stream,
        )

        responses = [json.loads(line) for line in out_stream.getvalue().splitlines()]
        assert responses[1]["result"]["isError"] is False
        persisted = Session(str(session_path))
        assert persisted.state.rom_path.endswith("b.gba")
        assert [entry["op"] for entry in persisted.state.history] == ["open"]

    def test_serve_refresh_oserror_uses_last_known_state_and_throttles_warning(
            self, tmp_path, monkeypatch, capsys):
        real_refresh = Session.refresh
        calls = {"count": 0}

        def refresh_once_then_fail(session):
            calls["count"] += 1
            if calls["count"] == 1:
                return real_refresh(session)
            raise OSError("sensitive path")

        monkeypatch.setattr(Session, "refresh", refresh_once_then_fail)
        in_stream = io.StringIO(
            json.dumps(_req("ping", id_=1))
            + "\n"
            + json.dumps(_req("ping", id_=2))
            + "\n"
        )
        out_stream = io.StringIO()

        srv.serve(
            session_file=str(tmp_path / "session.json"),
            in_stream=in_stream,
            out_stream=out_stream,
        )

        responses = [json.loads(line) for line in out_stream.getvalue().splitlines()]
        captured = capsys.readouterr()
        warning = "[febuildergba-mcp] session refresh failed; using last-known state"
        assert [response["result"] for response in responses] == [{}, {}]
        assert captured.err.count(warning) == 1
        assert "sensitive path" not in captured.err

    def test_mcp_lock_timeout_returns_tool_error_without_disk_write(
            self, tmp_path, monkeypatch):
        from cli_anything.febuildergba.core import data
        from cli_anything.febuildergba.core import session as session_module

        session_path = tmp_path / "session.json"
        session = Session(str(session_path))
        session.open_rom("/fake/a.gba", "FE8U")
        before = session_path.read_bytes()
        state = srv._ServerState(session)
        state.initialized = True
        monkeypatch.setattr(
            data,
            "export_table",
            lambda *args: {"exit_code": 0, "stdout": "", "stderr": ""},
        )
        monkeypatch.setattr(session_module, "SESSION_LOCK_TIMEOUT_SECONDS", 0)
        monkeypatch.setattr(session, "_try_acquire_lock", lambda lock_file: False)

        response = _call_tool(
            state,
            "data_export",
            {"table": "units", "out_path": "units.tsv"},
        )

        assert response["result"]["isError"] is True
        assert session_path.read_bytes() == before


# ── Launcher subprocess: real framing/flushing over a pipe ───────────────

def _repo_root() -> Path:
    p = Path(__file__).resolve()
    for _ in range(8):
        p = p.parent
        if (p / "agent-harness" / "febuildergba_mcp.py").is_file():
            return p
    pytest.skip("Cannot find repo root containing agent-harness/febuildergba_mcp.py")


def _readline_with_timeout(stream, timeout):
    result = queue.Queue(maxsize=1)

    def read_line():
        try:
            result.put((True, stream.readline()))
        except Exception as exc:
            result.put((False, exc))

    threading.Thread(target=read_line, daemon=True).start()
    try:
        ok, value = result.get(timeout=timeout)
    except queue.Empty:
        pytest.fail(f"timed out after {timeout} seconds waiting for launcher output")
    if not ok:
        raise value
    return value


def _write_malformed_session(path, kind):
    if kind == "invalid_utf8":
        path.write_bytes(b'{"rom_path":"/fake/rom.gba\xff"}')
    elif kind == "excessive_integer_digits":
        path.write_bytes(
            b'{"rom_path":"/fake/rom.gba","rom_size":'
            + b"9" * (MAX_SESSION_INTEGER_DIGITS + 1)
            + b"}"
        )
    elif kind == "nonstandard_constant":
        path.write_bytes(
            b'{"rom_path":"/fake/rom.gba","history":[{"value":NaN}]}'
        )
    elif kind == "float_overflow":
        path.write_bytes(
            b'{"rom_path":"/fake/rom.gba","history":[{"value":1e1000000}]}'
        )
    elif kind == "excessive_nesting":
        depth = sys.getrecursionlimit() + 1
        path.write_bytes(b"[" * depth + b"0" + b"]" * depth)
    elif kind == "oversized":
        content = b'{"rom_path":"/fake/rom.gba"}'
        path.write_bytes(
            content + b" " * (MAX_SESSION_FILE_BYTES + 1 - len(content))
        )
    else:
        raise ValueError(f"Unknown malformed session kind: {kind}")


class TestLauncherArguments:
    @pytest.mark.parametrize(
        ("argv", "expected"),
        [
            ([], None),
            (["--session-file", "session.json"], "session.json"),
            (["--session-file=session.json"], "session.json"),
        ],
    )
    def test_parse_argv_accepts_only_documented_forms(self, argv, expected):
        assert srv._parse_argv(argv) == expected

    @pytest.mark.parametrize(
        "argv",
        [
            ["--session-file"],
            ["--session-file="],
            ["--session-file", " "],
            ["--session-file", "a.json", "--session-file", "b.json"],
            ["--session", "session.json"],
            ["--sesion-file", "session.json"],
            ["unexpected.json"],
        ],
    )
    def test_parse_argv_rejects_malformed_or_unknown_arguments(self, argv):
        with pytest.raises(SystemExit) as exc_info:
            srv._parse_argv(argv)
        assert exc_info.value.code == 2


class TestLauncherSubprocess:
    @pytest.mark.parametrize(
        "args",
        [
            ["--session-file"],
            ["--session-file="],
            ["--session", "session.json"],
            ["--sesion-file", "session.json"],
            ["unexpected.json"],
        ],
    )
    def test_launcher_rejects_bad_arguments_before_starting_server(self, args):
        root = _repo_root()
        launcher = root / "agent-harness" / "febuildergba_mcp.py"
        initialize = json.dumps({
            "jsonrpc": "2.0",
            "id": 1,
            "method": "initialize",
            "params": _init_params(),
        })

        proc = subprocess.run(
            [sys.executable, str(launcher), *args],
            input=initialize + "\n",
            stdout=subprocess.PIPE,
            stderr=subprocess.PIPE,
            text=True,
            timeout=10,
        )

        assert proc.returncode == 2
        assert proc.stdout == ""
        assert proc.stderr.strip()

    def test_launcher_initialize_roundtrip(self):
        root = _repo_root()
        launcher = root / "agent-harness" / "febuildergba_mcp.py"
        proc = subprocess.Popen(
            [sys.executable, str(launcher)],
            stdin=subprocess.PIPE, stdout=subprocess.PIPE, stderr=subprocess.PIPE,
            text=True, bufsize=1,
        )
        try:
            req = json.dumps({"jsonrpc": "2.0", "id": 1, "method": "initialize",
                              "params": _init_params()})
            proc.stdin.write(req + "\n")
            proc.stdin.flush()

            line = _readline_with_timeout(proc.stdout, 10)
            assert line.strip(), "expected a flushed one-line JSON-RPC response"
            resp = json.loads(line)
            assert resp["id"] == 1
            assert resp["result"]["protocolVersion"] == "2025-03-26"
        finally:
            proc.stdin.close()
            try:
                proc.wait(timeout=10)
            except subprocess.TimeoutExpired:
                proc.kill()

    def test_launcher_forces_utf8_over_legacy_pipe_encoding(self):
        root = _repo_root()
        launcher = root / "agent-harness" / "febuildergba_mcp.py"
        method = "m\u00e9thode"
        requests = [
            json.dumps(
                {
                    "jsonrpc": "2.0",
                    "id": 1,
                    "method": "initialize",
                    "params": _init_params(),
                },
                ensure_ascii=False,
            ),
            json.dumps(_req(method, id_=2), ensure_ascii=False),
        ]
        env = os.environ.copy()
        env["PYTHONIOENCODING"] = "cp1252"
        env["PYTHONUTF8"] = "0"

        proc = subprocess.run(
            [sys.executable, str(launcher)],
            input=("\n".join(requests) + "\n").encode("utf-8"),
            stdout=subprocess.PIPE,
            stderr=subprocess.PIPE,
            env=env,
            timeout=10,
            check=False,
        )

        assert proc.returncode == 0, proc.stderr.decode("utf-8", errors="replace")
        responses = [
            json.loads(line)
            for line in proc.stdout.decode("utf-8").splitlines()
            if line.strip()
        ]
        assert len(responses) == 2
        assert responses[1]["error"]["code"] == srv.METHOD_NOT_FOUND
        assert responses[1]["error"]["message"] == f"Method not found: {method}"

    def test_launcher_rejects_invalid_utf8_and_recovers(self):
        root = _repo_root()
        launcher = root / "agent-harness" / "febuildergba_mcp.py"
        good_request = json.dumps(_req("ping", id_=2)).encode("utf-8") + b"\n"
        payload = (
            b'{"jsonrpc":"2.0","id":1,"method":"pi\xffng"}\n'
            + good_request
        )

        proc = subprocess.run(
            [sys.executable, str(launcher)],
            input=payload,
            stdout=subprocess.PIPE,
            stderr=subprocess.PIPE,
            timeout=10,
            check=False,
        )

        assert proc.returncode == 0, proc.stderr.decode("utf-8", errors="replace")
        responses = [
            json.loads(line)
            for line in proc.stdout.decode("utf-8").splitlines()
            if line.strip()
        ]
        assert len(responses) == 2
        assert responses[0]["id"] is None
        assert responses[0]["error"]["code"] == srv.PARSE_ERROR
        assert responses[1]["id"] == 2
        assert responses[1]["result"] == {}

    @pytest.mark.parametrize(
        "kind",
        [
            "invalid_utf8",
            "excessive_integer_digits",
            "nonstandard_constant",
            "float_overflow",
            "excessive_nesting",
            "oversized",
        ],
    )
    def test_launcher_malformed_session_loads_closed(self, tmp_path, kind):
        root = _repo_root()
        launcher = root / "agent-harness" / "febuildergba_mcp.py"
        session_path = tmp_path / f"{kind}.json"
        _write_malformed_session(session_path, kind)
        proc = subprocess.Popen(
            [sys.executable, str(launcher), "--session-file", str(session_path)],
            stdin=subprocess.PIPE, stdout=subprocess.PIPE, stderr=subprocess.PIPE,
            text=True, bufsize=1,
        )
        try:
            initialize = json.dumps({
                "jsonrpc": "2.0",
                "id": 1,
                "method": "initialize",
                "params": _init_params(),
            })
            proc.stdin.write(initialize + "\n")
            proc.stdin.flush()
            initialize_response = json.loads(
                _readline_with_timeout(proc.stdout, 10)
            )
            assert initialize_response["id"] == 1
            assert "result" in initialize_response

            status_request = json.dumps(_req(
                "tools/call",
                {"name": "session_status", "arguments": {}},
                id_=2,
            ))
            proc.stdin.write(status_request + "\n")
            proc.stdin.flush()
            status_response = json.loads(_readline_with_timeout(proc.stdout, 10))
            assert status_response["id"] == 2
            status = json.loads(status_response["result"]["content"][0]["text"])
            assert status["open"] is False
            assert proc.poll() is None
        finally:
            proc.stdin.close()
            try:
                proc.wait(timeout=10)
            except subprocess.TimeoutExpired:
                proc.kill()


# ── .mcp.json registration ────────────────────────────────────────────────

class TestMcpJsonRegistration:
    def test_febuildergba_cli_registered(self):
        root = _repo_root()
        mcp_json = root / ".mcp.json"
        data = json.loads(mcp_json.read_text(encoding="utf-8"))
        servers = data["mcpServers"]
        assert "febuildergba-cli" in servers
        entry = servers["febuildergba-cli"]
        assert entry["type"] == "stdio"
        assert entry["command"] == "python"
        assert any("febuildergba_mcp.py" in a for a in entry["args"])
        # the pre-existing computer-use entry point must be preserved
        assert "febuildergba-computer-use" in servers
