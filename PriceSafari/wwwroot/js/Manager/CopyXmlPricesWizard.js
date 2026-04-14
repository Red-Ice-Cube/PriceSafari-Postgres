const dataEl = document.getElementById("copyxml-wizard-data");
const storeId = parseInt(dataEl.getAttribute("data-store-id") || "0", 10);
const xmlContainer = document.getElementById("xmlContainer");
const progressContainer = document.getElementById("xmlProgressContainer");

let existingMapping = null;
try {
    const raw = dataEl.getAttribute("data-existing-mapping");
    existingMapping = raw && raw !== "null" ? JSON.parse(raw) : null;
} catch (e) { existingMapping = null; }

let mappingForField = {
    "Key": null,
    "Price": null,
    "PromoPrice": null, // <--- NOWE
    "PriceWithShipping": null,
    "InStock": null
};
let xmlDoc = null;
let scrapableProducts = null; // cache produktów sklepu
const RENDER_CHUNK = 1000;
const proxyUrl = `/CopyXmlPricesWizard/ProxyXml?storeId=${storeId}`;

// Załaduj wartości z istniejącego mapowania
if (existingMapping) {
    if (existingMapping.keyField !== undefined) {
        const kfSel = document.getElementById("keyFieldSelector");
        kfSel.value = (existingMapping.keyField === 1 || existingMapping.keyField === "ExternalId") ? "ExternalId" : "Ean";
    }
    if (existingMapping.keyXPath) mappingForField.Key = { xpath: existingMapping.keyXPath, nodeCount: 0, firstValue: "" };
    if (existingMapping.priceXPath) mappingForField.Price = { xpath: existingMapping.priceXPath, nodeCount: 0, firstValue: "" };
    if (existingMapping.promoPriceXPath) mappingForField.PromoPrice = { xpath: existingMapping.promoPriceXPath, nodeCount: 0, firstValue: "" };
    if (existingMapping.priceWithShippingXPath) mappingForField.PriceWithShipping = { xpath: existingMapping.priceWithShippingXPath, nodeCount: 0, firstValue: "" };
    if (existingMapping.inStockXPath) mappingForField.InStock = { xpath: existingMapping.inStockXPath, nodeCount: 0, firstValue: "" };
    if (existingMapping.inStockMarkerValue) document.getElementById("inStockMarker").value = existingMapping.inStockMarkerValue;
}

// ─── PROGRESS ──────────────────────────────
function showProgress(msg, pct) {
    if (!progressContainer) return;
    progressContainer.style.display = 'block';
    progressContainer.innerHTML = `
        <div id="_p_msg" style="margin-bottom:6px;font-style:italic;color:#444;">${msg}</div>
        <div style="background:#e0e0e0;border-radius:4px;height:14px;overflow:hidden;">
            <div id="_p_bar" style="height:100%;width:${Math.min(pct, 100)}%;background:linear-gradient(90deg,#4a90d9,#6ab0f5);transition:width 0.15s;"></div>
        </div>
        <div id="_p_pct" style="margin-top:4px;font-size:12px;color:#666;">${Math.min(pct, 100)}%</div>`;
}
function updateProgress(msg, pct) {
    const m = document.getElementById('_p_msg'), b = document.getElementById('_p_bar'), p = document.getElementById('_p_pct');
    if (m) m.innerText = msg; if (b) b.style.width = Math.min(pct, 100) + '%'; if (p) p.innerText = Math.min(pct, 100) + '%';
}
function hideProgress() { if (progressContainer) { progressContainer.style.display = 'none'; progressContainer.innerHTML = ''; } }

// ─── ŁADOWANIE XML ──────────────────────────
function loadFromNetwork() {
    xmlContainer.innerHTML = '';
    showProgress('Pobieranie XML z sieci...', 5);
    requestAnimationFrame(() => requestAnimationFrame(() => {
        fetch(proxyUrl)
            .then(r => { if (!r.ok) throw new Error(`${r.status} ${r.statusText}`); return r.text(); })
            .then(xmlStr => {
                const idx = xmlStr ? xmlStr.indexOf('<') : -1;
                if (idx === -1) throw new Error("Brak '<' w odpowiedzi.");
                let clean = idx > 0 ? xmlStr.substring(idx) : xmlStr;
                if (clean.charCodeAt(0) === 65279) clean = clean.substring(1);
                processXmlString(clean);
            })
            .catch(err => { hideProgress(); xmlContainer.innerText = `Błąd: ${err.message}`; });
    }));
}

document.getElementById("loadFromNetworkBtn").addEventListener("click", loadFromNetwork);

document.getElementById('processXmlButton').addEventListener('click', () => {
    const fi = document.getElementById('xmlFileInput');
    if (!fi || fi.files.length === 0) { alert('Wybierz plik.'); return; }
    const file = fi.files[0];
    xmlContainer.innerHTML = '';
    showProgress(`Wczytywanie ${file.name} (${(file.size / 1024 / 1024).toFixed(1)} MB)...`, 5);
    requestAnimationFrame(() => requestAnimationFrame(() => {
        const r = new FileReader();
        r.onprogress = e => { if (e.lengthComputable) updateProgress(`Wczytywanie: ${(e.loaded / 1024 / 1024).toFixed(1)}/${(e.total / 1024 / 1024).toFixed(1)} MB`, Math.round(e.loaded / e.total * 30) + 5); };
        r.onload = e => processXmlString(e.target.result);
        r.onerror = e => { hideProgress(); xmlContainer.innerText = 'Błąd: ' + e.target.error; };
        r.readAsText(file);
    }));
});

function processXmlString(xmlStr) {
    updateProgress('Parsowanie...', 38);
    setTimeout(() => {
        xmlDoc = new DOMParser().parseFromString(xmlStr, "application/xml");
        if (!xmlDoc || xmlDoc.documentElement.nodeName === "parsererror") {
            hideProgress();
            xmlContainer.innerText = `Błąd parsowania XML:\n${xmlDoc?.documentElement?.textContent ?? ""}`;
            return;
        }
        const total = xmlDoc.documentElement.querySelectorAll('*').length + 1;
        setTimeout(() => {
            xmlContainer.innerHTML = '';
            const ul = document.createElement("ul");
            xmlContainer.appendChild(ul);
            buildTreeAsync(xmlDoc.documentElement, ul, "", total, () => {
                updateProgress("Stosowanie mapowań...", 97);
                setTimeout(() => { applyExistingMappings(); renderMappingTable(); hideProgress(); }, 30);
            });
        }, 30);
    }, 50);
}

// ─── BUDOWA DRZEWA (async chunks) ──────────
function buildTreeAsync(rootNode, rootUl, rootPath, totalNodes, onDone) {
    let rendered = 0;
    const queue = [{ node: rootNode, parentUl: rootUl, parentPath: rootPath }];
    function processChunk() {
        let processed = 0;
        while (queue.length > 0 && processed < RENDER_CHUNK) {
            const { node, parentUl, parentPath } = queue.shift();
            processed++;
            const { li, childrenUl } = createLi(node, parentPath);
            parentUl.appendChild(li);
            rendered++;
            if (node.children.length > 0 && childrenUl) {
                Array.from(node.children).forEach(c => {
                    if (c.localName === 'description') return;
                    queue.push({ node: c, parentUl: childrenUl, parentPath: li.getAttribute("data-xpath") });
                });
            }
        }
        const pct = Math.min(40 + Math.round(rendered / totalNodes * 55), 95);
        updateProgress(`Drzewo (${rendered}/${totalNodes})...`, pct);
        if (queue.length > 0) setTimeout(processChunk, 0);
        else onDone();
    }
    processChunk();
}

function createLi(node, parentPath) {
    const li = document.createElement("li");
    li.classList.add("xml-node");
    const nameAttr = node.getAttribute && node.getAttribute("name");
    const nameWithAttr = nameAttr ? `${node.nodeName}[@name="${nameAttr}"]` : node.nodeName;
    const currentPath = parentPath ? `${parentPath}/${nameWithAttr}` : `/${nameWithAttr}`;
    li.setAttribute("data-xpath", currentPath);

    const b = document.createElement("b");
    b.innerText = node.nodeName + (nameAttr ? ` (name="${nameAttr}")` : "");
    li.appendChild(b);

    if (node.attributes && node.attributes.length > 0) {
        const ulA = document.createElement("ul");
        Array.from(node.attributes).forEach(attr => {
            if (attr.name === "name") return;
            const aLi = document.createElement("li");
            aLi.classList.add("xml-node");
            aLi.setAttribute("data-xpath", `${currentPath}/@${attr.name}`);
            const i = document.createElement("i"); i.innerText = `@${attr.name}: `;
            const s = document.createElement("span"); s.innerText = attr.value;
            aLi.appendChild(i); aLi.appendChild(s); ulA.appendChild(aLi);
        });
        if (ulA.children.length) li.appendChild(ulA);
    }

    if (node.children.length === 0) {
        const txt = node.textContent.trim();
        if (txt) {
            const ulV = document.createElement("ul");
            const lv = document.createElement("li");
            lv.classList.add("xml-node");
            lv.setAttribute("data-xpath", `${currentPath}/#value`);
            const s = document.createElement("span"); s.innerText = txt;
            lv.appendChild(s); ulV.appendChild(lv);
            li.appendChild(ulV);
        }
        return { li, childrenUl: null };
    }
    const cu = document.createElement("ul");
    li.appendChild(cu);
    return { li, childrenUl: cu };
}

// ─── KLIKI ────────────────────────────────
document.addEventListener("click", function (e) {
    const el = e.target.closest(".xml-node");
    if (!el) return;
    if (!xmlContainer.contains(el)) return;
    const xPath = el.getAttribute("data-xpath");
    if (!xPath) return;
    const field = document.getElementById("fieldSelector").value;

    document.querySelectorAll(`.highlight-${field}`).forEach(x => x.classList.remove(`highlight-${field}`));
    const same = document.querySelectorAll(`.xml-node[data-xpath='${xPath}']`);
    same.forEach(n => n.classList.add(`highlight-${field}`));

    let firstVal = "";
    if (same.length > 0) {
        const sp = same[0].querySelector("span");
        if (sp) firstVal = sp.innerText.replace(/^(\s*:\s*)/, "").trim();
    }
    mappingForField[field] = { xpath: xPath, nodeCount: same.length, firstValue: firstVal };
    renderMappingTable();
});

function applyExistingMappings() {
    for (const f in mappingForField) {
        const info = mappingForField[f];
        if (!info || !info.xpath) continue;
        const same = document.querySelectorAll(`.xml-node[data-xpath='${info.xpath}']`);
        let firstVal = "";
        if (same.length > 0) {
            const sp = same[0].querySelector("span");
            if (sp) firstVal = sp.innerText.replace(/^(\s*:\s*)/, "").trim();
        }
        info.nodeCount = same.length;
        info.firstValue = firstVal;
        same.forEach(n => n.classList.add(`highlight-${f}`));
    }
}

function renderMappingTable() {
    const tb = document.getElementById("mappingTable").querySelector("tbody");
    tb.innerHTML = "";
    // Dodaj tłumaczenie etykiety dla tabelki:
    const labels = { Key: "Klucz (EAN/ID)", Price: "Cena", PromoPrice: "Cena promocyjna", PriceWithShipping: "Cena z dostawą", InStock: "Dostępność" };
    for (const f in mappingForField) {
        const info = mappingForField[f];
        const tr = document.createElement("tr");
        tr.innerHTML = info
            ? `<td>${labels[f]}</td><td><code>${info.xpath}</code></td><td>${info.nodeCount || 0}</td><td>${info.firstValue || "-"}</td>`
            : `<td>${labels[f]}</td><td>-</td><td>0</td><td>-</td>`;
        tb.appendChild(tr);
    }
}

// ─── WSPÓLNY PRODUCT NODE PATH ────────────
function computeProductNodeXPath() {
    const paths = Object.values(mappingForField)
        .filter(m => m && m.xpath)
        .map(m => m.xpath.replace('/#value', '').replace(/\/@.*$/, ''));
    if (paths.length === 0) return null;
    const parts = paths.map(p => p.split('/'));
    const common = [];
    for (let i = 0; i < parts[0].length; i++) {
        const seg = parts[0][i];
        if (parts.every(p => p.length > i && p[i] === seg)) common.push(seg);
        else break;
    }
    // Węzeł produktu = przedostatni sensowny + jeden w dół? Nie — common bez ostatniego jeśli nie wspólny
    // Common path kończy się na węźle produktu (np /rss/channel/item)
    return common.join('/');
}

// ─── SAVE / LOAD / TOGGLE ─────────────────
document.getElementById("saveMapping").addEventListener("click", function () {
    if (!mappingForField.Key || !mappingForField.Key.xpath) { alert("Musisz zmapować klucz (EAN lub ID)."); return; }
    if (!mappingForField.Price || !mappingForField.Price.xpath) { alert("Musisz zmapować cenę."); return; }

    const productNode = computeProductNodeXPath();
    const body = {
        storeId: storeId,
        keyField: document.getElementById("keyFieldSelector").value,
        productNodeXPath: productNode,
        keyXPath: mappingForField.Key?.xpath,
        priceXPath: mappingForField.Price?.xpath,
        promoPriceXPath: mappingForField.PromoPrice?.xpath,
        priceWithShippingXPath: mappingForField.PriceWithShipping?.xpath,
        inStockXPath: mappingForField.InStock?.xpath,
        inStockMarkerValue: document.getElementById("inStockMarker").value || null
    };
    fetch("/CopyXmlPricesWizard/SaveMapping", {
        method: "POST", headers: { "Content-Type": "application/json" }, body: JSON.stringify(body)
    }).then(r => r.json()).then(d => alert(d.message)).catch(e => console.error(e));
});

document.getElementById("reloadMapping").addEventListener("click", function () {
    fetch(`/CopyXmlPricesWizard/GetMapping?storeId=${storeId}`)
        .then(r => r.json()).then(d => {
            if (!d) { alert("Brak zapisanego mapowania."); return; }
            Object.keys(mappingForField).forEach(f => mappingForField[f] = null);
            document.querySelectorAll('[class*="highlight-"]').forEach(el => {
                [...el.classList].filter(c => c.startsWith("highlight-")).forEach(c => el.classList.remove(c));
            });
            document.getElementById("keyFieldSelector").value = (d.keyField === 1 || d.keyField === "ExternalId") ? "ExternalId" : "Ean";
            if (d.keyXPath) mappingForField.Key = { xpath: d.keyXPath, nodeCount: 0, firstValue: "" };
            if (d.priceXPath) mappingForField.Price = { xpath: d.priceXPath, nodeCount: 0, firstValue: "" };
            if (d.promoPriceXPath) mappingForField.PromoPrice = { xpath: d.promoPriceXPath, nodeCount: 0, firstValue: "" };
            if (d.priceWithShippingXPath) mappingForField.PriceWithShipping = { xpath: d.priceWithShippingXPath, nodeCount: 0, firstValue: "" };
            if (d.inStockXPath) mappingForField.InStock = { xpath: d.inStockXPath, nodeCount: 0, firstValue: "" };
            document.getElementById("inStockMarker").value = d.inStockMarkerValue || "";
            if (xmlDoc) applyExistingMappings();
            renderMappingTable();
        });
});

document.getElementById("toggleCopyXml").addEventListener("change", function () {
    const enabled = this.checked;
    fetch(`/CopyXmlPricesWizard/ToggleCopyXMLPrices?storeId=${storeId}&enabled=${enabled}`, { method: "POST" })
        .then(r => r.json()).then(d => { if (!d.success) { alert("Błąd: " + d.message); this.checked = !enabled; } });
});

document.getElementById("previewMapping").addEventListener("click", async function () {
    if (!xmlDoc) { alert("Najpierw załaduj XML."); return; }
    if (!mappingForField.Key || !mappingForField.Key.xpath) { alert("Brak klucza."); return; }
    if (!mappingForField.Price || !mappingForField.Price.xpath) { alert("Brak ceny."); return; }

    if (!scrapableProducts) {
        const resp = await fetch(`/CopyXmlPricesWizard/GetScrapableProducts?storeId=${storeId}`);
        scrapableProducts = await resp.json();
    }

    const productNodePath = computeProductNodeXPath();  // np. "/rss/channel/item"
    if (!productNodePath) { alert("Nie można ustalić węzła produktu."); return; }

    // Ostatni segment (może zawierać predykat [@name=...])
    const lastSegment = productNodePath.split('/').pop();
    const pureName = lastSegment.split('[')[0]; // np. "item"
    const entries = xmlDoc.getElementsByTagName(pureName);

    const keyField = document.getElementById("keyFieldSelector").value; // "Ean" | "ExternalId"
    const marker = (document.getElementById("inStockMarker").value || "").trim().toLowerCase();

    // Indeksuj XML: key -> {price, priceWithShipping, inStockRaw}
    // UWAGA: getVal oczekuje w 3. argumencie *nazwy taga produktu* (np. "item"), NIE pełnej ścieżki!
    const xmlIndex = {};
    for (let i = 0; i < entries.length; i++) {
        const keyVal = getVal(entries[i], mappingForField.Key.xpath, lastSegment);
        if (!keyVal) continue;
        const k = keyVal.trim();
        if (!k || xmlIndex[k]) continue;

        // --- LOGIKA CENY Z PROMOCJĄ ---
        const parsedStandard = parsePrice(getVal(entries[i], mappingForField.Price.xpath, lastSegment));
        const parsedPromo = mappingForField.PromoPrice ? parsePrice(getVal(entries[i], mappingForField.PromoPrice.xpath, lastSegment)) : null;

        let finalPrice = null;
        if (parsedPromo !== null && !isNaN(parsedPromo)) {
            finalPrice = parsedPromo;
        } else if (parsedStandard !== null && !isNaN(parsedStandard)) {
            finalPrice = parsedStandard;
        }
        // ------------------------------

        xmlIndex[k] = {
            price: finalPrice, // używamy wyliczonej ceny finalnej
            priceWithShipping: mappingForField.PriceWithShipping
                ? parsePrice(getVal(entries[i], mappingForField.PriceWithShipping.xpath, lastSegment))
                : null,
            inStockRaw: mappingForField.InStock
                ? getVal(entries[i], mappingForField.InStock.xpath, lastSegment)
                : null
        };
    }

    // Match produkty
    let matched = 0, unmatched = 0, withoutKey = 0;
    const rows = [];
    scrapableProducts.forEach(p => {
        const eanFromDb = (p.ean || "").trim();
        const idFromDb = p.externalId != null ? String(p.externalId) : "";
        const k = keyField === "Ean" ? eanFromDb : idFromDb;

        if (!k) { withoutKey++; return; }
        const found = xmlIndex[k];
        if (found) {
            matched++;
            const shipping = (found.priceWithShipping != null && found.price != null)
                ? Math.max(0, found.priceWithShipping - found.price).toFixed(2) : "-";
            let inStock = "—";
            if (mappingForField.InStock) {
                if (!marker) inStock = "✓ (brak markera)";
                else inStock = (found.inStockRaw || "").trim().toLowerCase() === marker ? "✓ dostępny" : "✗ niedostępny";
            } else {
                inStock = "✓ (zakładany)";
            }
            rows.push({
                cls: "matched", name: p.productName,
                eanDb: eanFromDb || "-", idDb: idFromDb || "-",
                matchedKey: k,
                price: found.price, shipping, inStock, raw: found.inStockRaw || "-"
            });
        } else {
            unmatched++;
            rows.push({
                cls: "unmatched", name: p.productName,
                eanDb: eanFromDb || "-", idDb: idFromDb || "-",
                matchedKey: "(brak w XML)",
                price: "-", shipping: "-", inStock: "-", raw: "-"
            });
        }
    });

    document.getElementById("previewStats").innerHTML =
        `Produkty scrapable: <b>${scrapableProducts.length}</b> | `
        + `W XML pod kluczem: <b>${Object.keys(xmlIndex).length}</b> | `
        + `<span style="color:green;">Dopasowane: ${matched}</span> | `
        + `<span style="color:red;">Bez dopasowania: ${unmatched}</span> | `
        + `Bez klucza: ${withoutKey}`;

    const sorted = rows.sort((a, b) => (a.cls === "matched" ? -1 : 1));
    const show = sorted.slice(0, 200);

    const keyLabel = keyField === "Ean" ? "EAN" : "ID";
    let html = `<table class="preview-table">
        <thead>
            <tr>
                <th colspan="3" style="background:#e3f2fd;">Z bazy produktów</th>
                <th colspan="1" style="background:#fff3e0;">Dopasowanie</th>
                <th colspan="4" style="background:#e8f5e9;">Dane do doklejenia z XML</th>
            </tr>
            <tr>
                <th>Nazwa</th>
                <th>EAN (baza)</th>
                <th>ID (baza)</th>
                <th>${keyLabel} z XML (po czym match)</th>
                <th>Cena</th>
                <th>Koszt dostawy</th>
                <th>Dostępność</th>
                <th>Raw InStock</th>
            </tr>
        </thead><tbody>`;
    show.forEach(r => {
        html += `<tr class="${r.cls}">
            <td>${r.name || "-"}</td>
            <td>${r.eanDb}</td>
            <td>${r.idDb}</td>
            <td><b>${r.matchedKey}</b></td>
            <td>${r.price ?? "-"}</td>
            <td>${r.shipping}</td>
            <td>${r.inStock}</td>
            <td>${r.raw}</td>
        </tr>`;
    });
    html += `</tbody></table>`;
    if (rows.length > 200) html += `<div style="margin-top:5px;color:#666;">...pokazano pierwsze 200 z ${rows.length}</div>`;
    document.getElementById("previewContainer").innerHTML = html;
});

// ─── HELPERS (getVal skopiowane z GoogleWizard) ──────
const nsResolver = prefix => ({ 'g': 'http://base.google.com/ns/1.0' })[prefix] || null;

function getVal(entryNode, xpathFull, productNodeName) {
    const identifier = '/' + productNodeName;
    const last = xpathFull.lastIndexOf(identifier);
    let relativePath;
    if (last !== -1) relativePath = '.' + xpathFull.substring(last + identifier.length);
    else {
        const pureName = '/' + productNodeName.split('[')[0];
        const lastP = xpathFull.lastIndexOf(pureName);
        if (lastP !== -1) relativePath = '.' + xpathFull.substring(lastP + pureName.length);
        else return null;
    }
    if (xpathFull.endsWith('/#value')) {
        const elPath = relativePath.slice(0, -7);
        let el = null;
        try {
            const r = xmlDoc.evaluate(elPath, entryNode, nsResolver, XPathResult.FIRST_ORDERED_NODE_TYPE, null);
            el = r.singleNodeValue;
        } catch (e) { }
        if (!el) {
            const tag = elPath.replace(/.*\//, '').replace(/.*:/, '');
            const cand = entryNode.getElementsByTagName("*");
            for (let i = 0; i < cand.length; i++) if (cand[i].localName === tag) { el = cand[i]; break; }
        }
        if (!el) return null;
        let v = el.textContent.trim();
        if (!v && el.localName === 'link' && el.hasAttribute('href')) v = el.getAttribute('href');
        if (!v && el.hasAttribute('src')) v = el.getAttribute('src');
        return v;
    }
    try {
        const r = xmlDoc.evaluate(relativePath, entryNode, nsResolver, XPathResult.STRING_TYPE, null);
        return r.stringValue.trim();
    } catch (e) { return null; }
}

function parsePrice(v) {
    if (!v) return null;
    let c = v.replace(/[^\d.,-]/g, '');
    if (c.indexOf(',') > -1 && c.indexOf('.') > -1) {
        if (c.indexOf(',') < c.indexOf('.')) c = c.replace(/,/g, '');
        else c = c.replace(/\./g, '').replace(',', '.');
    } else if (c.indexOf(',') > -1) c = c.replace(',', '.');
    const f = parseFloat(c);
    return isNaN(f) ? null : parseFloat(f.toFixed(2));
}

renderMappingTable();