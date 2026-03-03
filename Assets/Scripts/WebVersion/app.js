// ============================================================
//  Animal Product Scanner — Web Version
// ============================================================

(function () {
    "use strict";

    // ---- Constants ------------------------------------------
    const MODEL_ID = "gemini-2.5-flash";
    const API_URL = (key) =>
        `https://generativelanguage.googleapis.com/v1beta/models/${MODEL_ID}:generateContent?key=${key}`;

    const SYSTEM_PROMPT = `System Role: You are an expert in textile science and animal welfare ethics.
Task: Analyze the attached image to identify clothing, footwear, and textiles.
Analysis Guidelines:
1. Material Identification: Focus on likely animal-derived components (Leather, Wool, Silk, Down, Fur, Suede, Bone/Horn buttons).
2. Animal Count Logic: Use standard industry averages (e.g., 1 sheep yields ~4-5kg wool; 1 cow yields ~5 square meters of leather). Use fractions for single items.
3. Production Ethics: Summarize the standard industrial processes, focusing on environmental impact and animal welfare. Use objective, scientific language to describe common industry practices (e.g., mulesing, live-plucking, chemical tanning).
Constraint: Do not perform a web search for common materials like "Leather" or "Wool" unless the item is from a specific, identifiable brand with unique practices. Use your internal knowledge base to save time.
Output Format: Return a JSON object with this structure: { "summary": { "total_estimated_animals": float, "item_count": int }, "items": [ { "name": "string", "material": "string", "species": "string", "animal_count": float, "confidence": "Low|Medium|High", "production_summary": "Max 3 sentences focusing on ethics/science." } ] }`;

    const LOADING_MESSAGES = [
        "Sending request to Gemini...",
        "Analyzing image pixels...",
        "Identifying objects...",
        "Detecting materials...",
        "Consulting database...",
        "Formulating responses...",
        "Summarizing results...",
        "Preparing results...",
        "Finalizing analysis..."
    ];

    const RESPONSE_SCHEMA = {
        type: "OBJECT",
        properties: {
            summary: {
                type: "OBJECT",
                properties: {
                    total_estimated_animals: { type: "NUMBER" },
                    item_count: { type: "INTEGER" }
                }
            },
            items: {
                type: "ARRAY",
                items: {
                    type: "OBJECT",
                    properties: {
                        name: { type: "STRING" },
                        material: { type: "STRING" },
                        species: { type: "STRING" },
                        animal_count: { type: "NUMBER" },
                        confidence: { type: "STRING" },
                        production_summary: { type: "STRING" }
                    },
                    required: ["name", "confidence"]
                }
            }
        }
    };

    // ---- State -----------------------------------------------
    let apiKey = "";
    let pendingBase64 = "";   // base64 of the chosen image (no prefix)
    let pendingDataUrl = "";  // full data URL for <img> preview
    let geminiResult = null;
    let currentItemIndex = 0;
    let loadingInterval = null;

    // ---- DOM refs --------------------------------------------
    const $ = (sel) => document.querySelector(sel);

    const screens = {
        start:   $("#screen-start"),
        context: $("#screen-context"),
        loading: $("#screen-loading"),
        results: $("#screen-results")
    };

    const els = {
        apiKeyInput:        $("#api-key-input"),
        toggleKeyBtn:       $("#toggle-key-visibility"),
        saveKeyBtn:         $("#save-key-btn"),
        apiKeyStatus:       $("#api-key-status"),
        btnCamera:          $("#btn-camera"),
        btnGallery:         $("#btn-gallery"),
        fileCamera:         $("#file-camera"),
        fileGallery:        $("#file-gallery"),
        // context
        contextPreview:     $("#context-preview"),
        contextInput:       $("#context-input"),
        btnContextCancel:   $("#btn-context-cancel"),
        btnContextConfirm:  $("#btn-context-confirm"),
        // loading
        loadingStatus:      $("#loading-status"),
        // results
        resultsPreview:     $("#results-preview"),
        itemNav:            $("#item-nav"),
        btnPrev:            $("#btn-prev"),
        btnNext:            $("#btn-next"),
        itemCounter:        $("#item-counter"),
        summaryBar:         $("#summary-bar"),
        summaryItems:       $("#summary-items"),
        summaryAnimals:     $("#summary-animals"),
        resultBody:         $("#result-body"),
        btnTryAgain:        $("#btn-try-again")
    };

    // ---- Screen management -----------------------------------
    function showScreen(name) {
        Object.values(screens).forEach((s) => s.classList.remove("active"));
        screens[name].classList.add("active");
        // scroll to top
        screens[name].scrollTop = 0;
    }

    // ---- Local storage helpers --------------------------------
    function loadApiKey() {
        const stored = localStorage.getItem("aps_api_key");
        if (stored) {
            apiKey = stored;
            els.apiKeyInput.value = stored;
            showKeyStatus("API key loaded from storage.", "success");
        }
    }

    function saveApiKey() {
        const val = els.apiKeyInput.value.trim();
        if (!val) {
            showKeyStatus("Please enter a valid API key.", "error");
            return;
        }
        apiKey = val;
        localStorage.setItem("aps_api_key", val);
        showKeyStatus("API key saved ✓", "success");
    }

    function showKeyStatus(msg, type) {
        els.apiKeyStatus.textContent = msg;
        els.apiKeyStatus.className = "status-msg " + type;
        els.apiKeyStatus.classList.remove("hidden");
    }

    // ---- Material cache (localStorage) -----------------------
    function getCache() {
        try {
            return JSON.parse(localStorage.getItem("aps_material_cache") || "{}");
        } catch {
            return {};
        }
    }

    function getCachedSummary(material) {
        if (!material) return null;
        const cache = getCache();
        return cache[material.toLowerCase()] || null;
    }

    function addToCache(material, summary) {
        if (!material || !summary) return;
        const cache = getCache();
        const key = material.toLowerCase();
        if (cache[key]) return; // already exists
        cache[key] = summary;
        localStorage.setItem("aps_material_cache", JSON.stringify(cache));
    }

    // ---- Image handling --------------------------------------
    function handleFile(file) {
        if (!file) return;

        // Ensure API key is set
        apiKey = els.apiKeyInput.value.trim() || apiKey;
        if (!apiKey) {
            showKeyStatus("Please set your Gemini API key first.", "error");
            return;
        }

        const reader = new FileReader();
        reader.onload = (e) => {
            pendingDataUrl = e.target.result;             // data:image/...;base64,...
            pendingBase64 = pendingDataUrl.split(",")[1];  // raw base64

            // Show context screen
            els.contextPreview.src = pendingDataUrl;
            els.contextInput.value = "";
            showScreen("context");
        };
        reader.readAsDataURL(file);
    }

    // ---- Gemini API call -------------------------------------
    async function analyzeImage(base64, userContext) {
        showScreen("loading");
        startLoadingMessages();

        // Build prompt
        let prompt = SYSTEM_PROMPT;
        if (userContext && userContext.trim()) {
            prompt += "\n\nAdditional context from user: " + userContext.trim();
        }

        // Include cached material names to save tokens
        const cache = getCache();
        const cachedMaterials = Object.keys(cache);
        if (cachedMaterials.length > 0) {
            prompt += `\n\nNOTE: I already have detailed production summaries for the following materials: ${cachedMaterials.join(", ")}. If you identify any of these, please leave the 'production_summary' field empty or null to save tokens. I will fill it in from my local database.`;
        }

        const payload = {
            contents: [{
                parts: [
                    { text: prompt },
                    { inline_data: { mime_type: "image/jpeg", data: base64 } }
                ]
            }],
            generationConfig: {
                response_mime_type: "application/json",
                response_schema: RESPONSE_SCHEMA
            }
        };

        try {
            const resp = await fetch(API_URL(apiKey), {
                method: "POST",
                headers: { "Content-Type": "application/json" },
                body: JSON.stringify(payload)
            });

            stopLoadingMessages();

            if (!resp.ok) {
                const errBody = await resp.text();
                let hint = "";
                if (resp.status === 400) hint = "This might be due to an invalid API key or request format.";
                else if (resp.status === 401 || resp.status === 403) hint = "Your API key might be invalid or expired.";
                else if (resp.status === 429) hint = "Rate limit exceeded. Please wait and try again.";
                throw new Error(`API error ${resp.status}: ${errBody}\n${hint}`);
            }

            const data = await resp.json();

            // Parse Gemini wrapper
            if (!data.candidates || !data.candidates.length ||
                !data.candidates[0].content ||
                !data.candidates[0].content.parts ||
                !data.candidates[0].content.parts.length) {
                throw new Error("Unexpected API response structure.");
            }

            const innerJson = data.candidates[0].content.parts[0].text;
            const result = JSON.parse(innerJson);

            // Validate
            if (!result.items) result.items = [];

            // Cache logic: fill from cache or store new entries
            result.items.forEach((item) => {
                if (!item || !item.material) return;
                const ethicsSummary = extractEthicsSummary(item.production_summary);
                if (!ethicsSummary) {
                    const cached = getCachedSummary(item.material);
                    if (cached) item.production_summary = cached;
                } else {
                    addToCache(item.material, ethicsSummary);
                }
            });

            geminiResult = result;
            showResults();

        } catch (err) {
            stopLoadingMessages();
            showError(err.message || "An unknown error occurred.");
        }
    }

    // ---- Ethics tag helpers ----------------------------------
    function extractEthicsSummary(summary) {
        if (!summary) return null;
        if (summary.startsWith("[PRODUCT FOUND:")) {
            const end = summary.indexOf("]");
            return end > 0 ? summary.substring(end + 1).trim() : summary;
        }
        if (summary.startsWith("[NO ONLINE MATCH]")) {
            return summary.replace("[NO ONLINE MATCH]", "").trim();
        }
        if (summary.startsWith("[SEARCH LIMIT REACHED]")) {
            return summary.replace("[SEARCH LIMIT REACHED]", "").trim();
        }
        return summary;
    }

    // ---- Loading messages ------------------------------------
    function startLoadingMessages() {
        let idx = 0;
        els.loadingStatus.textContent = LOADING_MESSAGES[0];
        loadingInterval = setInterval(() => {
            idx++;
            if (idx < LOADING_MESSAGES.length) {
                els.loadingStatus.textContent = LOADING_MESSAGES[idx];
            }
        }, 2500);
    }

    function stopLoadingMessages() {
        if (loadingInterval) {
            clearInterval(loadingInterval);
            loadingInterval = null;
        }
    }

    // ---- Show results ----------------------------------------
    function showResults() {
        showScreen("results");

        // Set image preview
        els.resultsPreview.src = pendingDataUrl;

        if (!geminiResult || !geminiResult.items || geminiResult.items.length === 0) {
            els.itemNav.classList.add("hidden");
            els.summaryBar.classList.add("hidden");
            els.resultBody.innerHTML = `<p class="error-text">No items detected. Please try a different image.</p>`;
            return;
        }

        const totalItems = geminiResult.summary
            ? geminiResult.summary.item_count
            : geminiResult.items.length;
        const totalAnimals = geminiResult.summary
            ? geminiResult.summary.total_estimated_animals
            : 0;

        // Summary bar
        els.summaryItems.textContent = `${totalItems} item${totalItems !== 1 ? "s" : ""} found`;
        els.summaryAnimals.textContent = `~${totalAnimals.toFixed(2)} animals`;
        els.summaryBar.classList.remove("hidden");

        // Navigation
        if (geminiResult.items.length > 1) {
            els.itemNav.classList.remove("hidden");
        } else {
            els.itemNav.classList.add("hidden");
        }

        currentItemIndex = 0;
        renderItem(currentItemIndex);
    }

    function renderItem(index) {
        const items = geminiResult.items;
        const item = items[index];
        if (!item) return;

        // Update counter & arrows
        els.itemCounter.textContent = `${index + 1} / ${items.length}`;
        els.btnPrev.disabled = index === 0;
        els.btnNext.disabled = index === items.length - 1;

        const hasAnimalMaterial = !!item.material;

        // Build HTML
        let html = "";

        // Title
        html += `<div class="item-title">Item ${index + 1}: ${esc(item.name || "Unknown")}</div>`;

        // Material
        if (hasAnimalMaterial) {
            html += row("Material", `<span class="material-value">${esc(item.material)}</span>`);
        } else {
            html += row("Material", `<span class="material-safe">No animal-derived materials detected</span>`);
        }

        // Species & count (only if animal material)
        if (hasAnimalMaterial) {
            if (item.species) {
                html += row("Species", esc(item.species));
            }
            if (item.animal_count > 0) {
                html += row("Animals Used", item.animal_count.toFixed(2));
            }
        }

        // Confidence
        const conf = (item.confidence || "unknown").toLowerCase();
        const confClass = ["high", "medium", "low"].includes(conf) ? conf : "unknown";
        html += row("Confidence", `<span class="confidence-${confClass}">${esc((item.confidence || "Unknown").toUpperCase())}</span>`);

        // Production ethics
        if (hasAnimalMaterial && item.production_summary) {
            html += `<div class="ethics-section">`;
            html += `<div class="ethics-label">Production Ethics</div>`;

            if (item.production_summary.startsWith("[PRODUCT FOUND:")) {
                const endBracket = item.production_summary.indexOf("]");
                if (endBracket > 0) {
                    const tag = item.production_summary.substring(15, endBracket).trim();
                    const body = item.production_summary.substring(endBracket + 1).trim();
                    html += `<span class="ethics-tag found">✓ Product Found: ${esc(tag)}</span>`;
                    if (body) html += `<p class="ethics-text">${esc(body)}</p>`;
                } else {
                    html += `<p class="ethics-text">${esc(item.production_summary)}</p>`;
                }
            } else if (item.production_summary.startsWith("[NO ONLINE MATCH]")) {
                const body = item.production_summary.replace("[NO ONLINE MATCH]", "").trim();
                html += `<span class="ethics-tag no-match">⚠ No online match</span>`;
                if (body) html += `<p class="ethics-text">${esc(body)}</p>`;
            } else if (item.production_summary.startsWith("[SEARCH LIMIT REACHED]")) {
                const body = item.production_summary.replace("[SEARCH LIMIT REACHED]", "").trim();
                html += `<span class="ethics-tag limit">ℹ Search limit reached</span>`;
                if (body) html += `<p class="ethics-text">${esc(body)}</p>`;
            } else {
                html += `<p class="ethics-text">${esc(item.production_summary)}</p>`;
            }

            html += `</div>`;
        }

        els.resultBody.innerHTML = html;
    }

    function row(label, value) {
        return `<div class="result-row"><span class="result-label">${label}</span><span class="result-value">${value}</span></div>`;
    }

    function esc(str) {
        if (!str) return "";
        const d = document.createElement("div");
        d.textContent = str;
        return d.innerHTML;
    }

    // ---- Error display ---------------------------------------
    function showError(message) {
        showScreen("results");
        els.resultsPreview.src = pendingDataUrl || "";
        els.itemNav.classList.add("hidden");
        els.summaryBar.classList.add("hidden");
        els.resultBody.innerHTML = `<p class="error-text">${esc(message)}</p>`;
    }

    // ---- Event wiring ----------------------------------------
    function init() {
        loadApiKey();

        // API key
        els.saveKeyBtn.addEventListener("click", saveApiKey);
        els.toggleKeyBtn.addEventListener("click", () => {
            const inp = els.apiKeyInput;
            inp.type = inp.type === "password" ? "text" : "password";
            els.toggleKeyBtn.textContent = inp.type === "password" ? "👁️" : "🙈";
        });

        // Camera button
        els.btnCamera.addEventListener("click", () => {
            els.fileCamera.value = "";
            els.fileCamera.click();
        });
        els.fileCamera.addEventListener("change", (e) => {
            handleFile(e.target.files[0]);
        });

        // Gallery button
        els.btnGallery.addEventListener("click", () => {
            els.fileGallery.value = "";
            els.fileGallery.click();
        });
        els.fileGallery.addEventListener("change", (e) => {
            handleFile(e.target.files[0]);
        });

        // Context screen
        els.btnContextCancel.addEventListener("click", () => {
            pendingBase64 = "";
            pendingDataUrl = "";
            showScreen("start");
        });

        els.btnContextConfirm.addEventListener("click", () => {
            const ctx = els.contextInput.value;
            analyzeImage(pendingBase64, ctx);
        });

        // Results navigation
        els.btnPrev.addEventListener("click", () => {
            if (currentItemIndex > 0) {
                currentItemIndex--;
                renderItem(currentItemIndex);
            }
        });

        els.btnNext.addEventListener("click", () => {
            if (geminiResult && currentItemIndex < geminiResult.items.length - 1) {
                currentItemIndex++;
                renderItem(currentItemIndex);
            }
        });

        // Try again
        els.btnTryAgain.addEventListener("click", () => {
            geminiResult = null;
            currentItemIndex = 0;
            pendingBase64 = "";
            pendingDataUrl = "";
            showScreen("start");
        });
    }

    // ---- Boot ------------------------------------------------
    if (document.readyState === "loading") {
        document.addEventListener("DOMContentLoaded", init);
    } else {
        init();
    }
})();
