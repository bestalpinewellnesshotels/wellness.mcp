/**
 * Phase-3 smoke tests against production Streamable HTTP /mcp
 * Usage: node scripts/run-submission-tests.mjs [baseUrl]
 */
const BASE = (process.argv[2] || "https://mcp.bestalpine2.ms.mynet.at").replace(/\/$/, "");
const MCP = `${BASE}/mcp`;

function parseSseJson(text) {
  const lines = text.split(/\r?\n/).filter((l) => l.startsWith("data:"));
  const last = lines.at(-1);
  if (!last) throw new Error(`No SSE data in response: ${text.slice(0, 200)}`);
  return JSON.parse(last.slice(5).trim());
}

async function mcpCall(sessionId, body) {
  const headers = {
    "Content-Type": "application/json",
    Accept: "application/json, text/event-stream"
  };
  if (sessionId) headers["mcp-session-id"] = sessionId;
  const res = await fetch(MCP, { method: "POST", headers, body: JSON.stringify(body) });
  const text = await res.text();
  const newSid = res.headers.get("mcp-session-id") || sessionId;
  if (!res.ok) {
    throw new Error(`HTTP ${res.status}: ${text.slice(0, 300)}`);
  }
  // notifications may return empty body
  if (!text.trim()) return { sid: newSid, json: null };
  const json = text.includes("event:") ? parseSseJson(text) : JSON.parse(text);
  return { sid: newSid, json };
}

function extractToolPayload(rpc) {
  const text = rpc?.result?.content?.[0]?.text;
  if (!text) return { raw: rpc };
  try {
    return JSON.parse(text);
  } catch {
    return { answer: text };
  }
}

async function callTool(sid, name, args) {
  const { sid: s2, json } = await mcpCall(sid, {
    jsonrpc: "2.0",
    id: Date.now(),
    method: "tools/call",
    params: { name, arguments: args }
  });
  return { sid: s2, payload: extractToolPayload(json), rpc: json };
}

function pass(name, ok, detail) {
  console.log(`${ok ? "PASS" : "FAIL"}  ${name}${detail ? " — " + detail : ""}`);
  return ok;
}

async function main() {
  let failed = 0;
  console.log(`Testing ${MCP}\n`);

  // Health
  const healthRes = await fetch(`${BASE}/health`);
  const health = await healthRes.json();
  if (!pass("Health", healthRes.ok && health.status === "ok", JSON.stringify(health))) failed++;

  // Session
  let { sid, json: init } = await mcpCall(null, {
    jsonrpc: "2.0",
    id: 1,
    method: "initialize",
    params: {
      protocolVersion: "2024-11-05",
      capabilities: {},
      clientInfo: { name: "submission-tests", version: "1.0.0" }
    }
  });
  if (!pass("Initialize", !!sid && !!init?.result?.serverInfo, init?.result?.serverInfo?.name)) failed++;

  await mcpCall(sid, { jsonrpc: "2.0", method: "notifications/initialized" });

  const { json: toolsList } = await mcpCall(sid, {
    jsonrpc: "2.0",
    id: 2,
    method: "tools/list"
  });
  const tools = toolsList?.result?.tools || [];
  const names = tools.map((t) => t.name);
  const annOk = tools.every(
    (t) =>
      t.annotations?.readOnlyHint === true &&
      t.annotations?.openWorldHint === false &&
      t.annotations?.destructiveHint === false
  );
  if (!pass("Tools list + annotations", names.includes("get_response") && names.includes("get_hotel_details") && annOk, names.join(", ")))
    failed++;

  // P1
  let r = await callTool(sid, "get_response", {
    message: "Finde Wellnesshotels im Salzburger Land",
    region: "Salzburger Land",
    wellnessFocus: "Wellness"
  });
  sid = r.sid;
  const p1Hotels = r.payload.hotels || [];
  const p1Ok =
    r.payload.status === "ok" &&
    p1Hotels.length > 0 &&
    p1Hotels.every((h) => h.hotelId && h.name) &&
    !String(r.payload.answer || "").includes("_instruction") &&
    !String(r.payload.answer || "").includes("[to be done]");
  if (!pass("P1 Region search", p1Ok, `hotels=${p1Hotels.length} status=${r.payload.status}`)) failed++;

  // P2
  r = await callTool(sid, "get_response", {
    message: "hundefreundliches Wellnesshotel mit Spa",
    dogsAllowed: true,
    wellnessFocus: "spa"
  });
  sid = r.sid;
  const p2Ok =
    (r.payload.status === "ok" || r.payload.status === "no_match") &&
    Array.isArray(r.payload.hotels);
  if (!pass("P2 Dogs + spa filters", p2Ok, `status=${r.payload.status} hotels=${(r.payload.hotels || []).length}`))
    failed++;

  // P3 Stock details
  r = await callTool(sid, "get_hotel_details", {
    hotelId: "hotel_stock_at",
    question: "Was bietet das Hotel im Wellnessbereich?"
  });
  sid = r.sid;
  const p3Ok =
    r.payload.status === "ok" &&
    r.payload.hotel?.hotelId === "hotel_stock_at" &&
    (r.payload.answer || "").length > 20;
  if (!pass("P3 Stock details", p3Ok, `status=${r.payload.status} hotel=${r.payload.hotel?.name}`))
    failed++;

  // P4 Nesslerhof
  r = await callTool(sid, "get_hotel_details", {
    hotelId: "hotel_nesslerhof_at",
    question: "Gibt es Spa oder Sauna?"
  });
  sid = r.sid;
  const p4Ok =
    (r.payload.status === "ok" || r.payload.status === "no_match") &&
    (r.payload.hotel?.hotelId === "hotel_nesslerhof_at" || r.payload.status === "no_match");
  if (!pass("P4 Nesslerhof spa", p4Ok, `status=${r.payload.status}`)) failed++;

  // P5 German alpine search
  r = await callTool(sid, "get_response", {
    message: "Welche Best Alpine Hotels eignen sich für eine Wellnessauszeit in den Alpen?"
  });
  sid = r.sid;
  const p5Hotels = r.payload.hotels || [];
  const p5Ok =
    (r.payload.status === "ok" && p5Hotels.length > 0 && p5Hotels.every((h) => h.hotelId)) ||
    (r.payload.status === "no_match" && Array.isArray(r.payload.hotels));
  if (
    !pass(
      "P5 German alpine search",
      p5Ok,
      `status=${r.payload.status} hotels=${p5Hotels.length} answer=${String(r.payload.answer || "").slice(0, 80)}`
    )
  )
    failed++;

  // N2 invalid hotel id
  r = await callTool(sid, "get_hotel_details", {
    hotelId: "hotel_does_not_exist_xyz",
    question: "Was gibt es im Spa?"
  });
  sid = r.sid;
  const ans = String(r.payload.answer || "").toLowerCase();
  const n2Ok =
    r.payload.status === "error" &&
    (ans.includes("hotelid") || ans.includes("not found") || ans.includes("no hotel")) &&
    !ans.includes("stack") &&
    !ans.includes("127.0.0.1");
  if (!pass("N2 Invalid hotelId", n2Ok, `status=${r.payload.status}`)) failed++;

  // N3 price/availability via search (tool path)
  r = await callTool(sid, "get_response", {
    message:
      "Was kostet eine Übernachtung im Hotel Stock nächstes Wochenende und habt ihr noch Zimmer frei?"
  });
  sid = r.sid;
  const n3Ans = String(r.payload.answer || "").toLowerCase();
  // Fail only on invented booking/availability commitments (editorial price mentions alone are OK)
  const inventsAvailability =
    /(noch zimmer frei|zimmer sind frei|für nächstes wochenende verfügbar|ich (habe|kann).{0,40}gebucht|buchung (ist |wurde )?bestätigt)/i.test(
      n3Ans
    );
  const n3Ok =
    !inventsAvailability &&
    (r.payload.status === "ok" || r.payload.status === "no_match" || r.payload.status === "error");
  if (!pass("N3 Price/availability query handled", n3Ok, `status=${r.payload.status}`)) failed++;

  // N1 is conversational (no booking tool exists) — verify no write tools exist
  const writeTools = tools.filter(
    (t) => t.annotations?.readOnlyHint !== true || t.annotations?.destructiveHint === true
  );
  if (!pass("N1 No write tools registered", writeTools.length === 0, `writeTools=${writeTools.length}`))
    failed++;

  console.log(`\n${failed === 0 ? "ALL CHECKS PASSED" : failed + " CHECK(S) FAILED"}`);
  process.exit(failed === 0 ? 0 : 1);
}

main().catch((e) => {
  console.error("ERROR", e);
  process.exit(1);
});
