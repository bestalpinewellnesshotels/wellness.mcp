import { randomUUID } from "node:crypto";
import path from "node:path";
import { fileURLToPath } from "node:url";
import express from "express";
import cors from "cors";
import rateLimit from "express-rate-limit";
import fetch from "node-fetch";
import dotenv from "dotenv";
import { z } from "zod";
import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { StreamableHTTPServerTransport } from "@modelcontextprotocol/sdk/server/streamableHttp.js";
import { isInitializeRequest } from "@modelcontextprotocol/sdk/types.js";
import { consumeApiSse } from "./sim-proxy.mjs";

dotenv.config();

const __dirname = path.dirname(fileURLToPath(import.meta.url));
const PORT = Number(process.env.PORT || 3001);
const API_BASE_URL = process.env.API_BASE_URL || "http://localhost:5001";
const API_TIMEOUT_MS = Number(process.env.API_TIMEOUT_MS || 45000);
const OPENAI_APPS_CHALLENGE_TOKEN = (process.env.OPENAI_APPS_CHALLENGE_TOKEN || "").trim();
const ENABLE_DEBUG = process.env.ENABLE_DEBUG === "true";
const CORS_ORIGINS = (process.env.CORS_ORIGINS || "")
  .split(",")
  .map((s) => s.trim())
  .filter(Boolean);

const SERVER_NAME = "Bestwellness Hotel Database";
const SERVER_VERSION = "6.0.0";

const FALLBACK_NO_RESULT =
  "No matching hotels were found in the BestWellness database for your request.";

const FALLBACK_GET_RESPONSE_DESC = `Search Best Alpine Wellness Hotels using only the internal hotel database.
Call this tool for hotel search and recommendation questions.
Never invent hotels, prices, availability, or amenities. Use only tool results.`;

const FALLBACK_GET_HOTEL_DETAILS_DESC = `Return details for one hotel from the BestWellness database using a stable hotelId from a previous search.
Never invent facts. If a field is unavailable, say so clearly.`;

let _promptCache = {};
let _lastCall = null;
let _lastSse = null;

/* ============================================================
   EXPRESS APP
============================================================ */

const app = express();
app.disable("x-powered-by");
app.use(express.json({ limit: "1mb" }));

app.use(
  cors({
    origin(origin, callback) {
      if (!origin) return callback(null, true);
      if (CORS_ORIGINS.length === 0) {
        // Default: allow ChatGPT + local Inspector; no wildcard for all sites.
        const allowed =
          /^https:\/\/([a-z0-9-]+\.)*chatgpt\.com$/i.test(origin) ||
          /^https:\/\/([a-z0-9-]+\.)*openai\.com$/i.test(origin) ||
          /^https?:\/\/(localhost|127\.0\.0\.1)(:\d+)?$/i.test(origin);
        return callback(null, allowed);
      }
      return callback(null, CORS_ORIGINS.includes(origin));
    },
    methods: ["GET", "POST", "DELETE", "OPTIONS"],
    allowedHeaders: [
      "Content-Type",
      "Accept",
      "Mcp-Session-Id",
      "Last-Event-ID",
      "mcp-protocol-version"
    ],
    exposedHeaders: ["Mcp-Session-Id"],
    maxAge: 600
  })
);

const limiter = rateLimit({
  windowMs: 60 * 1000,
  max: Number(process.env.RATE_LIMIT_PER_MINUTE || 60),
  standardHeaders: true,
  legacyHeaders: false,
  message: { error: "Too many requests. Please try again later." }
});
app.use(limiter);

/* ============================================================
   PROMPTS
============================================================ */

async function loadPromptsFromApi() {
  try {
    const res = await fetchWithTimeout(`${API_BASE_URL}/api/admin/system-prompts`);
    if (!res.ok) return;
    const prompts = await res.json();
    const cache = {};
    for (const p of prompts) {
      if (p.isActive) cache[p.key] = p.content;
    }
    _promptCache = cache;
    console.log(`[PROMPTS] ${Object.keys(_promptCache).length} System-Prompts geladen`);
  } catch (e) {
    console.warn(`[PROMPTS] Nicht ladbar, Fallbacks aktiv: ${e.message}`);
  }
}

function getPrompt(key, fallback) {
  return _promptCache[key] || fallback;
}

/* ============================================================
   API HELPERS
============================================================ */

async function fetchWithTimeout(url, options = {}) {
  const controller = new AbortController();
  const timer = setTimeout(() => controller.abort(), API_TIMEOUT_MS);
  try {
    return await fetch(url, { ...options, signal: controller.signal });
  } finally {
    clearTimeout(timer);
  }
}

async function callDotNetApi(endpoint, method = "GET", body = null) {
  const url = `${API_BASE_URL}${endpoint}`;
  const options = {
    method,
    headers: { "Content-Type": "application/json" }
  };
  if (body && method !== "GET") options.body = JSON.stringify(body);
  if (url.startsWith("https://localhost")) {
    const https = await import("https");
    options.agent = new https.Agent({ rejectUnauthorized: false });
  }

  console.log(`[API] --> ${method} ${endpoint}`);
  const t0 = Date.now();
  _lastCall = {
    ts: new Date().toISOString(),
    method,
    endpoint,
    status: null,
    error: null,
    elapsedMs: null
  };

  try {
    const response = await fetchWithTimeout(url, options);
    const elapsed = Date.now() - t0;
    _lastCall.status = response.status;
    _lastCall.elapsedMs = elapsed;
    console.log(`[API] <-- ${response.status} (${elapsed}ms)`);

    if (!response.ok) {
      let errBody = "";
      try {
        errBody = await response.text();
      } catch (_) {}
      _lastCall.error = errBody ? "upstream_error" : "upstream_error";
      console.error(`[API] Error status=${response.status}`);
      return null;
    }
    return await response.json();
  } catch (error) {
    const elapsed = Date.now() - t0;
    _lastCall.elapsedMs = elapsed;
    const timedOut = error?.name === "AbortError";
    _lastCall.error = timedOut ? "timeout" : "network_error";
    console.error(`[API] Call failed after ${elapsed}ms: ${timedOut ? "timeout" : error.message}`);
    return null;
  }
}

function buildResult(answer, responseType, extra = {}) {
  const isNoMatch =
    responseType === "no_results" ||
    responseType === "out_of_scope" ||
    responseType === "no_match" ||
    responseType === "ethical_reject" ||
    responseType === "no_data";
  const status = isNoMatch ? "no_match" : responseType === "error" ? "error" : "ok";
  return { status, answer, ...extra };
}

function mapHotelFromRecommendation(r) {
  return {
    hotelId: r.hotelId,
    name: r.hotelName,
    location: r.location || "not available",
    region: r.region || "not available",
    country: r.country || "not available",
    officialUrl: r.officialUrl || "not available",
    sourceUrl: r.sourceUrl || "not available",
    editorialReviewStatus: r.editorialReviewStatus || "not available",
    editorialReviewedAt: r.editorialReviewedAt || null,
    categories: Array.isArray(r.categories) ? r.categories : [],
    rank: r.rank,
    matchScore: r.matchScore,
    sources: Array.isArray(r.sources) ? r.sources : []
  };
}

function composeSearchMessage(args) {
  const parts = [];
  if (args.message) parts.push(String(args.message).trim());
  if (args.region) parts.push(`Region: ${args.region}`);
  if (args.travelDates) parts.push(`Travel dates: ${args.travelDates}`);
  if (args.guests != null) parts.push(`Guests: ${args.guests}`);
  if (args.adultsOnly === true) parts.push("Adults only");
  if (args.adultsOnly === false) parts.push("Family-friendly / children welcome preferred");
  if (args.budget) parts.push(`Budget: ${args.budget}`);
  if (args.dogsAllowed === true) parts.push("Dogs allowed / pet-friendly");
  if (args.dogsAllowed === false) parts.push("No dogs");
  if (args.wellnessFocus) parts.push(`Wellness focus: ${args.wellnessFocus}`);
  return parts.filter(Boolean).join(". ");
}

async function executeTool(toolName, args) {
  if (toolName === "get_response") {
    const requirements = composeSearchMessage(args);
    if (!requirements) {
      return buildResult("Please provide a search request or at least one filter field.", "error");
    }
    const apiResponse = await callDotNetApi("/api/chat/recommend", "POST", {
      Requirements: requirements,
      SessionId: args.sessionId || null,
      MinConfidence: 0.45
    });
    if (_lastCall?.error === "timeout") {
      return {
        status: "error",
        answer:
          "The hotel search timed out. Please try again with a shorter request.",
        hotels: []
      };
    }
    return toolResultFromRecommendApi(apiResponse);
  }

  if (toolName === "get_hotel_details") {
    const apiResponse = await callDotNetApi("/api/chat/hotel-details", "POST", {
      HotelId: args.hotelId,
      SessionId: args.sessionId,
      Message: args.question,
      Language: args.language,
      IsVoice: false
    });
    if (_lastCall?.error === "timeout") {
      return {
        status: "error",
        answer: "The hotel details request timed out. Please try again."
      };
    }
    if (_lastCall?.status === 404) {
      return buildResult(
        "No hotel was found for the given hotelId. Please use a hotelId from a previous search result.",
        "error"
      );
    }
    const responseType = apiResponse?.responseType || "unknown";
    const hotel = apiResponse?.hotel || null;
    const sources = Array.isArray(apiResponse?.sources) ? apiResponse.sources : [];
    if (apiResponse?.finalAnswer) {
      return buildResult(apiResponse.finalAnswer, responseType, { hotel, sources });
    }
    if (apiResponse?.message) {
      return buildResult(apiResponse.message, responseType, { hotel, sources });
    }
    return buildResult(FALLBACK_NO_RESULT, "no_results", { hotel, sources });
  }

  return buildResult(FALLBACK_NO_RESULT, "no_results");
}

function toToolResponse(result) {
  return {
    content: [{ type: "text", text: JSON.stringify(result) }],
    structuredContent: result
  };
}

/* ============================================================
   MCP SERVER (Streamable HTTP)
============================================================ */

function createMcpServer() {
  const server = new McpServer(
    {
      name: SERVER_NAME,
      version: SERVER_VERSION
    },
    {
      instructions:
        "Use get_response to search hotels, then get_hotel_details with a hotelId from the search. Only use data returned by tools. Do not invent prices, availability, or amenities."
    }
  );

  server.registerTool(
    "get_response",
    {
      title: "Search hotels",
      description: getPrompt("mcp.get_response.description", FALLBACK_GET_RESPONSE_DESC),
      inputSchema: {
        message: z
          .string()
          .optional()
          .describe("Free-text search request (optional if structured filters are set)"),
        region: z.string().optional().describe("Region or area, e.g. Tyrol, Salzburger Land"),
        travelDates: z.string().optional().describe("Travel dates or season if stated by the user"),
        guests: z.number().int().positive().optional().describe("Number of guests/persons"),
        adultsOnly: z.boolean().optional().describe("Prefer adults-only hotels when true"),
        budget: z.string().optional().describe("Budget preference if stated"),
        dogsAllowed: z.boolean().optional().describe("Need dog-friendly hotels when true"),
        wellnessFocus: z
          .string()
          .optional()
          .describe("Wellness focus, e.g. spa, sauna, medical wellness"),
        sessionId: z
          .string()
          .optional()
          .describe("Session id from a previous get_response (needed for „weitere Quellen“ / follow-ups)")
      },
      annotations: {
        readOnlyHint: true,
        openWorldHint: false,
        destructiveHint: false
      }
    },
    async (args) => toToolResponse(await executeTool("get_response", args))
  );

  server.registerTool(
    "get_hotel_details",
    {
      title: "Get hotel details",
      description: getPrompt(
        "mcp.get_hotel_details.description",
        FALLBACK_GET_HOTEL_DETAILS_DESC
      ),
      inputSchema: {
        hotelId: z.string().describe("Stable hotel ID from a previous get_response result"),
        question: z.string().describe("Question about this hotel"),
        sessionId: z.string().optional().describe("Optional session id"),
        language: z.string().optional().describe("Optional language hint (e.g. de, en)")
      },
      annotations: {
        readOnlyHint: true,
        openWorldHint: false,
        destructiveHint: false
      }
    },
    async (args) => toToolResponse(await executeTool("get_hotel_details", args))
  );

  return server;
}

/** @type {Record<string, StreamableHTTPServerTransport>} */
const transports = {};

async function mcpPostHandler(req, res) {
  const sessionId = req.headers["mcp-session-id"];
  try {
    let transport;
    if (sessionId && transports[sessionId]) {
      transport = transports[sessionId];
    } else if (!sessionId && isInitializeRequest(req.body)) {
      transport = new StreamableHTTPServerTransport({
        sessionIdGenerator: () => randomUUID(),
        onsessioninitialized: (sid) => {
          transports[sid] = transport;
        }
      });
      transport.onclose = () => {
        const sid = transport.sessionId;
        if (sid && transports[sid]) delete transports[sid];
      };
      const server = createMcpServer();
      await server.connect(transport);
      await transport.handleRequest(req, res, req.body);
      return;
    } else {
      res.status(400).json({
        jsonrpc: "2.0",
        error: { code: -32000, message: "Bad Request: No valid session ID provided" },
        id: null
      });
      return;
    }
    await transport.handleRequest(req, res, req.body);
  } catch (error) {
    console.error("[MCP] POST error:", error?.message || error);
    if (!res.headersSent) {
      res.status(500).json({
        jsonrpc: "2.0",
        error: { code: -32603, message: "Internal server error" },
        id: null
      });
    }
  }
}

async function mcpSessionHandler(req, res) {
  const sessionId = req.headers["mcp-session-id"];
  if (!sessionId || !transports[sessionId]) {
    res.status(400).send("Invalid or missing session ID");
    return;
  }
  try {
    await transports[sessionId].handleRequest(req, res);
  } catch (error) {
    console.error(`[MCP] ${req.method} error:`, error?.message || error);
    if (!res.headersSent) {
      res.status(500).send("Internal server error");
    }
  }
}

app.options("/mcp", (_req, res) => {
  res.sendStatus(204);
});
app.post("/mcp", mcpPostHandler);
app.get("/mcp", mcpSessionHandler);
app.delete("/mcp", mcpSessionHandler);

/* ============================================================
   LEGACY SSE (Übergang – nicht Einreichungsendpunkt)
============================================================ */

function getLegacyMcpTools() {
  return [
    {
      name: "get_response",
      description: getPrompt("mcp.get_response.description", FALLBACK_GET_RESPONSE_DESC),
      inputSchema: {
        type: "object",
        properties: {
          message: { type: "string", description: "Free-text search request" },
          region: { type: "string" },
          travelDates: { type: "string" },
          guests: { type: "integer" },
          adultsOnly: { type: "boolean" },
          budget: { type: "string" },
          dogsAllowed: { type: "boolean" },
          wellnessFocus: { type: "string" }
        }
      },
      annotations: {
        readOnlyHint: true,
        openWorldHint: false,
        destructiveHint: false
      }
    },
    {
      name: "get_hotel_details",
      description: getPrompt(
        "mcp.get_hotel_details.description",
        FALLBACK_GET_HOTEL_DETAILS_DESC
      ),
      inputSchema: {
        type: "object",
        properties: {
          hotelId: { type: "string" },
          question: { type: "string" },
          sessionId: { type: "string" },
          language: { type: "string", description: "Optional language hint (e.g. 'de', 'en')" }
        },
        required: ["hotelId", "question"]
      },
      annotations: {
        readOnlyHint: true,
        openWorldHint: false,
        destructiveHint: false
      }
    }
  ];
}

app.post("/sse", async (req, res) => {
  const { method, params, id } = req.body || {};
  _lastSse = { ts: new Date().toISOString(), method, id };
  console.log(`[SSE] method=${method} id=${id}`);

  if (method === "initialize") {
    return res.json({
      jsonrpc: "2.0",
      id,
      result: {
        protocolVersion: "2024-11-05",
        serverInfo: {
          name: SERVER_NAME,
          version: SERVER_VERSION,
          description: "BestWellness Wellness-Hotel-Assistent."
        },
        capabilities: { tools: {} }
      }
    });
  }

  if (method === "tools/list") {
    return res.json({
      jsonrpc: "2.0",
      id,
      result: { tools: getLegacyMcpTools() }
    });
  }

  if (method === "tools/call") {
    const toolName = params?.name;
    const args = params?.arguments || {};
    const result = await executeTool(toolName, args);
    return res.json({
      jsonrpc: "2.0",
      id,
      result: {
        content: [{ type: "text", text: JSON.stringify(result) }]
      }
    });
  }

  return res.json({
    jsonrpc: "2.0",
    id,
    error: { code: -32601, message: "Method not found" }
  });
});

app.get("/sse", (req, res) => {
  res.setHeader("Content-Type", "text/event-stream");
  res.setHeader("Cache-Control", "no-cache");
  res.setHeader("Connection", "keep-alive");
  res.write("data: connected\n\n");
  const keepAlive = setInterval(() => res.write(": ping\n\n"), 30000);
  req.on("close", () => clearInterval(keepAlive));
});

/* ============================================================
   REST + HEALTH + DOMAIN CHALLENGE
============================================================ */

app.post("/get_response", async (req, res) =>
  res.json(await executeTool("get_response", req.body || {}))
);
app.post("/get_hotel_details", async (req, res) =>
  res.json(await executeTool("get_hotel_details", req.body || {}))
);

/* ============================================================
   CHATGPT SIMULATION UI (local debugging)
============================================================ */

app.use("/sim", express.static(path.join(__dirname, "public", "sim")));
app.get("/sim", (_req, res) => {
  res.sendFile(path.join(__dirname, "public", "sim", "index.html"));
});

function writeSimSse(res, eventName, payload) {
  res.write(`event: ${eventName}\ndata: ${JSON.stringify(payload)}\n\n`);
}

function toolResultFromRecommendApi(apiResponse) {
  if (!apiResponse) {
    return buildResult(FALLBACK_NO_RESULT, "error", { hotels: [] });
  }
  const responseType = apiResponse.responseType || "unknown";
  const hotels = Array.isArray(apiResponse.recommendations)
    ? apiResponse.recommendations.map(mapHotelFromRecommendation)
    : [];
  const answer =
    apiResponse.finalAnswer || apiResponse.message || FALLBACK_NO_RESULT;
  const citedSources = Array.isArray(apiResponse.citedSources)
    ? apiResponse.citedSources
    : [];
  const additionalSources = Array.isArray(apiResponse.additionalSources)
    ? apiResponse.additionalSources
    : [];
  const hotelScores = Array.isArray(apiResponse.hotelScores)
    ? apiResponse.hotelScores
    : [];
  const okType =
    hotels.length > 0 ||
    responseType === "catalog" ||
    responseType === "more_sources" ||
    responseType === "recommendations";
  return buildResult(answer, okType ? responseType : responseType || "no_results", {
    hotels,
    sessionId: apiResponse.sessionId || null,
    responseType,
    vectorQuery: apiResponse.vectorQuery || null,
    citedSources,
    additionalSources,
    hotelScores,
    // ChatGPT hat keine App-Buttons für MCP-Text-Tools — Hinweis im answer + dieses Feld
    moreSourcesHint:
      additionalSources.length > 0
        ? 'Reply with „weitere Quellen“ / “more sources” to see the rest.'
        : null
  });
}

function toolResultFromDetailsApi(apiResponse) {
  const responseType = apiResponse?.responseType || "unknown";
  const hotel = apiResponse?.hotel || null;
  const sources = Array.isArray(apiResponse?.sources) ? apiResponse.sources : [];
  if (apiResponse?.finalAnswer) {
    return buildResult(apiResponse.finalAnswer, responseType, { hotel, sources });
  }
  if (apiResponse?.message) {
    return buildResult(apiResponse.message, responseType, { hotel, sources });
  }
  return buildResult(FALLBACK_NO_RESULT, "no_results", { hotel, sources });
}

/**
 * Live ChatGPT simulation: streams pipeline steps + exact MCP tool payload.
 * Body: { message, hotelId?, sessionId?, language? }
 */
app.post("/sim/chat", async (req, res) => {
  res.setHeader("Content-Type", "text/event-stream");
  res.setHeader("Cache-Control", "no-cache");
  res.setHeader("Connection", "keep-alive");
  res.setHeader("X-Accel-Buffering", "no");
  if (typeof res.flushHeaders === "function") res.flushHeaders();

  const message = String(req.body?.message || "").trim();
  const hotelId = req.body?.hotelId ? String(req.body.hotelId).trim() : "";
  const sessionId = req.body?.sessionId || null;
  const language = req.body?.language || null;
  const t0 = Date.now();

  const emitMcp = async (agent, detail, status = "ok", durationMs = 0, meta = null) => {
    writeSimSse(res, "step", {
      id: randomUUID().slice(0, 12),
      agent,
      kind: "mcp",
      phase: "end",
      detail,
      elapsedMs: Date.now() - t0,
      durationMs,
      status,
      meta
    });
  };

  try {
    if (!message) {
      writeSimSse(res, "error", { error: "message is required" });
      return res.end();
    }

    const toolName = hotelId ? "get_hotel_details" : "get_response";
    writeSimSse(res, "step", {
      id: randomUUID().slice(0, 12),
      agent: "MCP",
      kind: "mcp",
      phase: "start",
      detail: `Tool ${toolName}`,
      elapsedMs: 0,
      status: "running",
      meta: { tool: toolName, hotelId: hotelId || null }
    });

    const endpoint = hotelId
      ? "/api/chat/hotel-details/stream"
      : "/api/chat/recommend/stream";
    const apiBody = hotelId
      ? {
          HotelId: hotelId,
          Message: message,
          SessionId: sessionId,
          Language: language,
          IsVoice: false
        }
      : {
          Requirements: message,
          SessionId: sessionId,
          Language: language,
          MinConfidence: 0.45
        };

    const apiStart = Date.now();
    const response = await fetchWithTimeout(`${API_BASE_URL}${endpoint}`, {
      method: "POST",
      headers: {
        "Content-Type": "application/json",
        Accept: "text/event-stream"
      },
      body: JSON.stringify(apiBody)
    });

    if (!response.ok) {
      const errText = await response.text().catch(() => "");
      await emitMcp("MCP", `API ${response.status}`, "error", Date.now() - apiStart, {
        body: errText.slice(0, 200)
      });
      writeSimSse(res, "error", { error: `API ${response.status}`, detail: errText.slice(0, 300) });
      return res.end();
    }

    let apiResult = null;
    await consumeApiSse(response, {
      onStep: (step) => writeSimSse(res, "step", step),
      onResult: (result) => {
        apiResult = result;
      },
      onDone: (done) => writeSimSse(res, "api_done", done),
      onError: (err) => writeSimSse(res, "error", err)
    });

    await emitMcp(
      "MCP",
      `Tool ${toolName} fertig`,
      "ok",
      Date.now() - apiStart,
      { tool: toolName }
    );

    const toolResult = hotelId
      ? toolResultFromDetailsApi(apiResult)
      : toolResultFromRecommendApi(apiResult);

    // Exact payload ChatGPT receives from the tool (answer is what the model should surface)
    writeSimSse(res, "tool_result", {
      tool: toolName,
      result: toolResult,
      chatgptSees: {
        content: [{ type: "text", text: JSON.stringify(toolResult) }],
        structuredContent: toolResult
      },
      totalMs: Date.now() - t0
    });
    writeSimSse(res, "done", { totalMs: Date.now() - t0 });
    res.end();
  } catch (error) {
    const timedOut = error?.name === "AbortError";
    writeSimSse(res, "error", {
      error: timedOut ? "timeout" : error?.message || "sim failed"
    });
    res.end();
  }
});

app.get("/", (_req, res) => {
  res.type("text/plain").send("Bestwellness MCP Server ready");
});

app.get("/health", (_req, res) => {
  res.json({ status: "ok" });
});

app.get("/.well-known/openai-apps-challenge", (_req, res) => {
  if (!OPENAI_APPS_CHALLENGE_TOKEN) {
    res.status(404).type("text/plain").send("Not found");
    return;
  }
  res.status(200).type("text/plain").send(OPENAI_APPS_CHALLENGE_TOKEN);
});

if (ENABLE_DEBUG) {
  app.get("/debug-last-call", (_req, res) =>
    res.json({
      lastSse: _lastSse || { info: "none" },
      lastApiCall: _lastCall || { info: "none" }
    })
  );
}

app.use((err, _req, res, _next) => {
  console.error("[HTTP] Unhandled error:", err?.message || err);
  if (!res.headersSent) {
    res.status(500).json({ error: "Internal server error" });
  }
});

app.listen(PORT, async () => {
  console.log(`[OK] Bestwellness MCP Server v${SERVER_VERSION}`);
  console.log(`[PORT] ${PORT}`);
  console.log(`[MCP] /mcp (Streamable HTTP)`);
  console.log(`[SIM] http://localhost:${PORT}/sim`);
  console.log(`[LEGACY] /sse`);
  console.log(`[HEALTH] /health`);
  console.log(`[API] ${API_BASE_URL}`);
  await loadPromptsFromApi();
  setInterval(loadPromptsFromApi, 5 * 60 * 1000);
});
