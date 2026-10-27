// Runs the NPC dialogue model inside the player's browser with WebLLM (WebGPU).
// Called from NPCDialogueAI (WebGL builds only). Replies go back with SendMessage.
// Plain ES5 on purpose: Unity's WebGL pipeline may not accept newer syntax in .jslib files.
mergeInto(LibraryManager.library, {

  WebLLM_Init: function (modelIdPtr, fallbackModelIdPtr, objectNamePtr) {
    var modelId = UTF8ToString(modelIdPtr);
    var fallbackModelId = UTF8ToString(fallbackModelIdPtr);
    var objectName = UTF8ToString(objectNamePtr);
    var libraryUrl = "https://cdn.jsdelivr.net/npm/@mlc-ai/web-llm@0.2.86/+esm";

    function send(method, value) {
      try { SendMessage(objectName, method, value); } catch (e) { console.warn("WebLLM -> Unity:", e); }
    }

    if (window.__npcLLM) {
      // Scene reloaded (Try Again): reuse the engine that is already loaded or loading.
      window.__npcLLM.objectName = objectName;
      if (window.__npcLLM.ready) send("OnWebLLMReady", window.__npcLLM.modelId);
      else if (window.__npcLLM.error) send("OnWebLLMError", window.__npcLLM.error);
      return;
    }

    var state = { ready: false, error: null, engine: null, modelId: modelId, objectName: objectName, queue: Promise.resolve() };
    window.__npcLLM = state;

    function fail(message) {
      state.error = message;
      console.error("WebLLM:", message);
      SendMessage(state.objectName, "OnWebLLMError", message);
    }

    if (!navigator.gpu) {
      fail("This browser has no WebGPU. Try a recent Chrome or Edge.");
      return;
    }

    navigator.gpu.requestAdapter().then(function (adapter) {
      if (!adapter) throw new Error("No WebGPU adapter (GPU) available.");
      // GPUs without 16-bit float support need the f32 build of the model.
      state.modelId = adapter.features.has("shader-f16") ? modelId : fallbackModelId;

      return new Promise(function (resolve, reject) {
        if (window.__webllmLib) { resolve(window.__webllmLib); return; }
        window.addEventListener("webllm-loaded", function () { resolve(window.__webllmLib); }, { once: true });
        window.addEventListener("webllm-failed", function () { reject(new Error("Could not download the WebLLM library.")); }, { once: true });
        var script = document.createElement("script");
        script.type = "module";
        script.textContent =
          "import * as webllm from '" + libraryUrl + "';" +
          "window.__webllmLib = webllm; window.dispatchEvent(new Event('webllm-loaded'));";
        script.onerror = function () { window.dispatchEvent(new Event("webllm-failed")); };
        document.head.appendChild(script);
        setTimeout(function () { if (!window.__webllmLib) reject(new Error("Timed out loading the WebLLM library.")); }, 60000);
      });
    }).then(function (webllm) {
      return webllm.CreateMLCEngine(state.modelId, {
        initProgressCallback: function (report) {
          SendMessage(state.objectName, "OnWebLLMProgress",
            JSON.stringify({ progress: report.progress || 0, text: report.text || "" }));
        }
      });
    }).then(function (engine) {
      state.engine = engine;
      state.ready = true;
      SendMessage(state.objectName, "OnWebLLMReady", state.modelId);
    }).catch(function (e) {
      fail(String((e && e.message) || e));
    });
  },

  WebLLM_GenerateChat: function (requestId, messagesJsonPtr, maxTokens, temperature, penalty) {
    var state = window.__npcLLM;
    var messages = [];
    try { messages = JSON.parse(UTF8ToString(messagesJsonPtr)).messages || []; } catch (e) { console.warn(e); }

    function reply(method, payload) {
      try { SendMessage(state.objectName, method, JSON.stringify(payload)); } catch (e) { console.warn(e); }
    }

    if (!state || !state.ready) {
      if (state) reply("OnWebLLMReplyError", { id: requestId, error: "model not ready" });
      return;
    }

    state.queue = state.queue.then(function () {
      return state.engine.chat.completions.create({
        messages: messages,
        max_tokens: maxTokens,
        temperature: temperature,
        frequency_penalty: penalty,
        presence_penalty: penalty * 0.6
      }).then(function (result) {
        var text = (result && result.choices && result.choices[0] && result.choices[0].message.content) || "";
        reply("OnWebLLMReply", { id: requestId, text: text });
      }).catch(function (e) {
        reply("OnWebLLMReplyError", { id: requestId, error: String((e && e.message) || e) });
      });
    });
  },

  WebLLM_Generate: function (requestId, promptPtr, maxTokens, temperature) {
    var prompt = UTF8ToString(promptPtr);
    var state = window.__npcLLM;

    function reply(method, payload) {
      try { SendMessage(state.objectName, method, JSON.stringify(payload)); } catch (e) { console.warn(e); }
    }

    if (!state || !state.ready) {
      if (state) reply("OnWebLLMReplyError", { id: requestId, error: "model not ready" });
      return;
    }

    // One request at a time on the GPU.
    state.queue = state.queue.then(function () {
      return state.engine.chat.completions.create({
        messages: [{ role: "user", content: prompt }],
        max_tokens: maxTokens,
        temperature: temperature
      }).then(function (result) {
        var text = (result && result.choices && result.choices[0] && result.choices[0].message.content) || "";
        reply("OnWebLLMReply", { id: requestId, text: text });
      }).catch(function (e) {
        reply("OnWebLLMReplyError", { id: requestId, error: String((e && e.message) || e) });
      });
    });
  }
});
