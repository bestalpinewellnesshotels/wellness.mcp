import { createInterface } from "node:readline";
import { Readable } from "node:stream";

function toNodeReadable(body) {
  if (!body) return null;
  if (typeof body.getReader === "function" && typeof Readable.fromWeb === "function") {
    return Readable.fromWeb(body);
  }
  return body;
}

/**
 * Consume API SSE (event: step|result|done|error) and invoke callbacks.
 */
export async function consumeApiSse(response, { onStep, onResult, onDone, onError }) {
  const stream = toNodeReadable(response.body);
  if (!stream) {
    throw new Error("API stream body missing");
  }

  const rl = createInterface({ input: stream, crlfDelay: Infinity });
  let eventName = "message";
  let dataLines = [];

  const flush = async () => {
    if (!dataLines.length) return;
    const raw = dataLines.join("\n");
    dataLines = [];
    const name = eventName;
    eventName = "message";
    let payload;
    try {
      payload = JSON.parse(raw);
    } catch {
      payload = { raw };
    }
    if (name === "step") await onStep?.(payload);
    else if (name === "result") await onResult?.(payload);
    else if (name === "done") await onDone?.(payload);
    else if (name === "error") await onError?.(payload);
  };

  for await (const line of rl) {
    if (line.startsWith("event:")) {
      eventName = line.slice(6).trim();
      continue;
    }
    if (line.startsWith("data:")) {
      dataLines.push(line.slice(5).trimStart());
      continue;
    }
    if (line === "") {
      await flush();
    }
  }
  await flush();
}
