const googleDataEl = document.getElementById("google-wizard-data");
const storeId = parseInt(googleDataEl.getAttribute("data-store-id") || "0", 10);
const xmlContainer = document.getElementById("xmlContainer");
const progressContainer = document.getElementById("xmlProgressContainer");

let existingMappingsJson = googleDataEl.getAttribute("data-existing-mappings") || "[]";
let existingMappings = [];
try { existingMappings = JSON.parse(existingMappingsJson); }
catch (e) { existingMappings = []; }

let mappingForField = {
    "ExternalId": null, "Url": null, "GoogleEan": null, "GoogleImage": null,
    "GoogleExportedName": null, "GoogleExportedProducer": null,
    "GoogleExportedProducerCode": null, "GoogleXMLPrice": null, "GoogleDeliveryXMLPrice": null
};

existingMappings.forEach(m => {
    if (m.fieldName) mappingForField[m.fieldName] = { xpath: m.localName, nodeCount: 0, firstValue: "" };
});

let xmlDoc = null;
let proxyUrl = `/GoogleImportWizardXml/ProxyXml?storeId=${storeId}`;

// Ile węzłów DOM budować na jeden "tick" – wyżej = szybciej ale bardziej zamraża UI
// 1000 to dobry kompromis dla dużych plików: ~5x szybciej niż 200, pasek nadal się aktualizuje
const RENDER_CHUNK = 1000;

// ─────────────────────────────────────────────────────────────
// PASEK POSTĘPU
// ─────────────────────────────────────────────────────────────
function showProgress(message, pct) {
    if (!progressContainer) return;
    progressContainer.style.display = 'block';
    progressContainer.innerHTML = `
        <div id="_xmlProgressMsg" style="margin-bottom:6px;font-style:italic;color:#444;">${message}</div>
        <div style="background:#e0e0e0;border-radius:4px;height:14px;overflow:hidden;">
            <div id="_xmlProgressBar" style="height:100%;width:${Math.min(pct, 100)}%;background:linear-gradient(90deg,#4a90d9,#6ab0f5);transition:width 0.15s ease;border-radius:4px;"></div>
        </div>
        <div id="_xmlProgressPct" style="margin-top:4px;font-size:12px;color:#666;">${Math.min(pct, 100)}%</div>`;
}

function updateProgress(message, pct) {
    const msg = document.getElementById('_xmlProgressMsg');
    const bar = document.getElementById('_xmlProgressBar');
    const pctEl = document.getElementById('_xmlProgressPct');
    if (msg) msg.innerText = message;
    if (bar) bar.style.width = Math.min(pct, 100) + '%';
    if (pctEl) pctEl.innerText = Math.min(pct, 100) + '%';
}

function hideProgress() {
    if (!progressContainer) return;
    progressContainer.style.display = 'none';
    progressContainer.innerHTML = '';
}

// ─────────────────────────────────────────────────────────────
// ŁADOWANIE Z SIECI
// ─────────────────────────────────────────────────────────────
function loadFromNetwork() {
    console.log(`Próba załadowania XML z sieci: ${proxyUrl}`);
    xmlContainer.innerHTML = '';
    showProgress('Pobieranie XML z sieci...', 5);

    requestAnimationFrame(() => requestAnimationFrame(() => {
        fetch(proxyUrl)
            .then(resp => {
                if (!resp.ok) throw new Error(`Błąd sieci: ${resp.status} ${resp.statusText}`);
                const contentLength = resp.headers.get('Content-Length');
                if (contentLength && resp.body) {
                    return readStreamWithProgress(resp.body, parseInt(contentLength, 10));
                }
                return resp.text();
            })
            .then(xmlStr => {
                console.log("--- Surowy tekst z sieci (pierwsze 1000 znaków) ---");
                console.log(xmlStr ? xmlStr.substring(0, 1000) : "[Pusta odpowiedź]");
                console.log("--- Koniec podglądu ---");

                let startIndex = xmlStr ? xmlStr.indexOf('<') : -1;
                if (startIndex === -1) throw new Error("Odpowiedź nie zawiera znaku '<'.");
                let cleanedXmlStr = startIndex > 0 ? xmlStr.substring(startIndex) : xmlStr;
                if (cleanedXmlStr.charCodeAt(0) === 65279) cleanedXmlStr = cleanedXmlStr.substring(1);

                processXmlString(cleanedXmlStr, `Sieć (${proxyUrl})`);
            })
            .catch(err => {
                console.error("Błąd ładowania XML z sieci:", err);
                hideProgress();
                xmlContainer.innerText = `Błąd ładowania z sieci:\n${err.message}\n\nSpróbuj wczytać plik lokalny.`;
            });
    }));
}

function readStreamWithProgress(body, totalBytes) {
    return new Promise((resolve, reject) => {
        const reader = body.getReader();
        const decoder = new TextDecoder('utf-8');
        let received = 0;
        const chunks = [];
        function pump() {
            reader.read().then(({ done, value }) => {
                if (done) { resolve(chunks.join('')); return; }
                received += value.length;
                chunks.push(decoder.decode(value, { stream: true }));
                const pct = Math.round((received / totalBytes) * 30) + 5;
                updateProgress(
                    `Pobieranie... ${(received / 1024 / 1024).toFixed(1)} / ${(totalBytes / 1024 / 1024).toFixed(1)} MB`,
                    pct
                );
                pump();
            }).catch(reject);
        }
        pump();
    });
}

// ─────────────────────────────────────────────────────────────
// WCZYTYWANIE PLIKU LOKALNEGO
// ─────────────────────────────────────────────────────────────
const processButton = document.getElementById('processXmlButton');
if (processButton) {
    processButton.addEventListener('click', () => {
        const fileInput = document.getElementById('xmlFileInput');
        if (!fileInput || fileInput.files.length === 0) { alert('Najpierw wybierz plik XML/TXT.'); return; }
        const file = fileInput.files[0];
        xmlContainer.innerHTML = '';
        showProgress(`Wczytywanie: ${file.name} (${(file.size / 1024 / 1024).toFixed(1)} MB)...`, 5);

        requestAnimationFrame(() => requestAnimationFrame(() => {
            const reader = new FileReader();
            reader.onprogress = e => {
                if (e.lengthComputable) {
                    updateProgress(
                        `Wczytywanie: ${(e.loaded / 1024 / 1024).toFixed(1)} / ${(e.total / 1024 / 1024).toFixed(1)} MB`,
                        Math.round((e.loaded / e.total) * 30) + 5
                    );
                }
            };
            reader.onload = e => processXmlString(e.target.result, `Plik (${file.name})`);
            reader.onerror = e => { hideProgress(); xmlContainer.innerText = 'Błąd odczytu pliku: ' + e.target.error; };
            reader.readAsText(file);
        }));
    });
}

// ─────────────────────────────────────────────────────────────
// GŁÓWNA FUNKCJA PRZETWARZANIA
// ─────────────────────────────────────────────────────────────
function processXmlString(xmlStr, sourceDescription) {
    console.log(`--- Przetwarzanie XML: ${sourceDescription} ---`);
    updateProgress(`Parsowanie XML...`, 38);

    setTimeout(() => {
        const parser = new DOMParser();
        xmlDoc = parser.parseFromString(xmlStr, "application/xml");

        if (!xmlDoc || xmlDoc.documentElement.nodeName === "parsererror") {
            const errorContent = xmlDoc?.documentElement?.textContent ?? "Nieznany błąd";
            hideProgress();
            xmlContainer.innerText = `Błąd parsowania XML (${sourceDescription}):\n${errorContent}\n\nSprawdź konsolę (F12).`;
            return;
        }

        console.log(`XML sparsowany pomyślnie.`);

        // Policz wszystkie węzły żeby wiedzieć ile pracy przed nami
        const totalNodes = xmlDoc.documentElement.querySelectorAll('*').length + 1;
        console.log(`Łączna liczba węzłów do wyrenderowania: ${totalNodes}`);
        updateProgress(`Budowanie drzewa (0 / ${totalNodes} węzłów)...`, 40);

        setTimeout(() => {
            xmlContainer.innerHTML = '';
            const ulRoot = document.createElement("ul");
            xmlContainer.appendChild(ulRoot);

            // Budujemy drzewo asynchronicznie – węzeł główny + kolejka dzieci
            buildTreeAsync(xmlDoc.documentElement, ulRoot, "", totalNodes, () => {
                updateProgress("Stosowanie mapowań...", 97);
                setTimeout(() => {
                    applyExistingMappings();
                    renderMappingTable();
                    hideProgress();
                }, 30);
            });
        }, 30);
    }, 50);
}

// ─────────────────────────────────────────────────────────────
// BUDOWANIE DRZEWA ASYNC – PEŁNE, ale w chunkach
//
// Zamiast rekurencji synchronicznej używamy kolejki (queue).
// Co RENDER_CHUNK elementów oddajemy sterowanie przeglądarce przez
// setTimeout(0) – dzięki temu pasek postępu się aktualizuje
// i przeglądarka nie pokazuje "Strona nie odpowiada".
// ─────────────────────────────────────────────────────────────
function buildTreeAsync(rootNode, rootUl, rootPath, totalNodes, onDone) {
    let rendered = 0;

    // Kolejka: { node, parentUl, parentPath }
    const queue = [{ node: rootNode, parentUl: rootUl, parentPath: rootPath }];

    function processChunk() {
        const chunkEnd = Math.min(rendered + RENDER_CHUNK, queue.length + rendered);
        let processed = 0;

        while (queue.length > 0 && processed < RENDER_CHUNK) {
            const { node, parentUl, parentPath } = queue.shift();
            processed++;

            const { li, childrenUl } = createLiShell(node, parentPath);
            parentUl.appendChild(li);
            rendered++;

            // Jeśli węzeł ma dzieci, dodaj je do kolejki z ich docelowym <ul>
            // Pomijamy <description> / <g:description> – długie teksty zbędne w podglądzie
            if (node.children.length > 0 && childrenUl) {
                Array.from(node.children).forEach(child => {
                    if (child.localName === 'description') return;
                    queue.push({ node: child, parentUl: childrenUl, parentPath: li.getAttribute("data-xpath") });
                });
            }
        }

        const pct = Math.min(40 + Math.round((rendered / totalNodes) * 55), 95);
        updateProgress(`Budowanie drzewa (${rendered} / ${totalNodes} węzłów)...`, pct);

        if (queue.length > 0) {
            setTimeout(processChunk, 0);
        } else {
            onDone();
        }
    }

    processChunk();
}

// Tworzy <li> dla węzła i zwraca go razem z docelowym <ul> na dzieci
function createLiShell(node, parentPath) {
    const li = document.createElement("li");
    li.classList.add("xml-node");

    const nameAttr = node.getAttribute && node.getAttribute("name");
    const nodeNameWithAttr = nameAttr ? `${node.nodeName}[@name="${nameAttr}"]` : node.nodeName;
    const currentPath = parentPath ? `${parentPath}/${nodeNameWithAttr}` : `/${nodeNameWithAttr}`;
    li.setAttribute("data-xpath", currentPath);

    const b = document.createElement("b");
    b.innerText = node.nodeName + (nameAttr ? ` (name="${nameAttr}")` : "");
    li.appendChild(b);

    // Atrybuty (poza "name")
    if (node.attributes && node.attributes.length > 0) {
        const ulAttrs = document.createElement("ul");
        Array.from(node.attributes).forEach(attr => {
            if (attr.name === "name") return;
            const attrLi = document.createElement("li");
            attrLi.classList.add("xml-node");
            attrLi.setAttribute("data-xpath", `${currentPath}/@${attr.name}`);
            const i = document.createElement("i"); i.innerText = `@${attr.name}: `;
            const sp = document.createElement("span"); sp.innerText = attr.value;
            attrLi.appendChild(i); attrLi.appendChild(sp);
            ulAttrs.appendChild(attrLi);
        });
        if (ulAttrs.children.length) li.appendChild(ulAttrs);
    }

    // Węzeł liść – wartość tekstowa
    if (node.children.length === 0) {
        const textVal = node.textContent.trim();
        if (textVal) {
            const ulVal = document.createElement("ul");
            const liVal = document.createElement("li");
            liVal.classList.add("xml-node");
            liVal.setAttribute("data-xpath", `${currentPath}/#value`);
            const sp = document.createElement("span"); sp.innerText = textVal;
            liVal.appendChild(sp); ulVal.appendChild(liVal);
            li.appendChild(ulVal);
        }
        return { li, childrenUl: null };
    }

    // Węzeł z dziećmi – tworzymy <ul> który trafi do kolejki
    const childrenUl = document.createElement("ul");
    li.appendChild(childrenUl);
    return { li, childrenUl };
}

// ─────────────────────────────────────────────────────────────
// KLIKNIĘCIE W WĘZEŁ DRZEWA
// ─────────────────────────────────────────────────────────────
document.addEventListener("click", function (e) {
    const el = e.target.closest(".xml-node");
    if (!el) return;
    const xPath = el.getAttribute("data-xpath");
    if (!xPath) return;
    const selectedField = document.getElementById("fieldSelector").value;

    document.querySelectorAll(`.highlight-${selectedField}`)
        .forEach(x => x.classList.remove(`highlight-${selectedField}`));

    const sameNodes = document.querySelectorAll(`.xml-node[data-xpath='${xPath}']`);
    sameNodes.forEach(n => n.classList.add(`highlight-${selectedField}`));

    let firstVal = "";
    if (sameNodes.length > 0) {
        const sp = sameNodes[0].querySelector("span");
        if (sp) firstVal = sp.innerText.replace(/^(\s*:\s*)/, "").trim();
    }

    mappingForField[selectedField] = { xpath: xPath, nodeCount: sameNodes.length, firstValue: firstVal };
    renderMappingTable();
});

function applyExistingMappings() {
    for (const fieldName in mappingForField) {
        const info = mappingForField[fieldName];
        if (!info || !info.xpath) continue;
        const sameNodes = document.querySelectorAll(`.xml-node[data-xpath='${info.xpath}']`);
        let firstVal = "";
        if (sameNodes.length > 0) {
            const sp = sameNodes[0].querySelector("span");
            if (sp) firstVal = sp.innerText.replace(/^(\s*:\s*)/, "").trim();
        }
        info.nodeCount = sameNodes.length;
        info.firstValue = firstVal;
        sameNodes.forEach(n => n.classList.add(`highlight-${fieldName}`));
    }
}

function clearAllHighlights() {
    Object.keys(mappingForField).forEach(field => {
        document.querySelectorAll(`.highlight-${field}`).forEach(el => el.classList.remove(`highlight-${field}`));
    });
}

// ─────────────────────────────────────────────────────────────
// TABELA MAPOWAŃ
// ─────────────────────────────────────────────────────────────
function renderMappingTable() {
    const tbody = document.getElementById("mappingTable").querySelector("tbody");
    tbody.innerHTML = "";
    for (const fieldName in mappingForField) {
        if (!fieldName || fieldName === "undefined") continue;
        const info = mappingForField[fieldName];
        const tr = document.createElement("tr");
        tr.innerHTML = info
            ? `<td>${fieldName}</td><td>${info.xpath || "-"}</td><td>${info.nodeCount || 0}</td><td>${info.firstValue || "-"}</td>`
            : `<td>${fieldName}</td><td>-</td><td>0</td><td>-</td>`;
        tbody.appendChild(tr);
    }
}

// ─────────────────────────────────────────────────────────────
// ZAPIS / PRZEŁADOWANIE MAPOWAŃ
// ─────────────────────────────────────────────────────────────
document.getElementById("saveMapping").addEventListener("click", function () {
    const finalMappings = [];
    for (const fieldName in mappingForField) {
        const info = mappingForField[fieldName];
        if (info && info.xpath) finalMappings.push({ fieldName, localName: info.xpath });
    }
    fetch(`/GoogleImportWizardXml/SaveGoogleMappings?storeId=${storeId}`, {
        method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(finalMappings)
    }).then(r => r.json()).then(d => alert(d.message)).catch(err => console.error(err));
});

document.getElementById("reloadMappings").addEventListener("click", function () {
    fetch(`/GoogleImportWizardXml/GetGoogleMappings?storeId=${storeId}`)
        .then(r => r.json())
        .then(data => {
            clearAllHighlights();
            Object.keys(mappingForField).forEach(f => mappingForField[f] = null);
            data.forEach(m => {
                if (m.fieldName) mappingForField[m.fieldName] = { xpath: m.localName, nodeCount: 0, firstValue: "" };
            });
            applyExistingMappings();
            renderMappingTable();
        })
        .catch(err => console.error("Błąd getGoogleMappings:", err));
});

// ─────────────────────────────────────────────────────────────
// EKSTRAKCJA PRODUKTÓW (działa na pełnym xmlDoc w pamięci)
// ─────────────────────────────────────────────────────────────
function parsePrice(value) {
    if (!value) return null;
    const match = value.match(/([\d]+([.,]\d+)?)/);
    if (!match) return null;
    const floatVal = parseFloat(match[1].replace(',', '.'));
    return isNaN(floatVal) ? null : floatVal.toFixed(2);
}

function extractProductsFromXml() {
    if (!xmlDoc) { alert("Brak XML do parsowania"); return; }

    const mappedXPaths = Object.values(mappingForField)
        .filter(m => m && m.xpath)
        .map(m => m.xpath.replace('/#value', '').replace(/\/\@.*/, ''));

    if (mappedXPaths.length === 0) {
        alert("Proszę najpierw zmapować przynajmniej jedno pole.");
        document.getElementById("productMapsPreview").textContent = "[]";
        return;
    }

    const pathParts = mappedXPaths.map(p => p.split('/'));
    let commonPath = [];
    for (let i = 0; i < pathParts[0].length; i++) {
        const segment = pathParts[0][i];
        if (pathParts.every(p => p.length > i && p[i] === segment)) commonPath.push(segment);
        else break;
    }

    if (commonPath.length <= 1) {
        alert("Nie udało się zidentyfikować głównego węzła produktu. Sprawdź mapowania.");
        return;
    }

    const productNodeNameWithPredicate = commonPath[commonPath.length - 1];
    const pureProductNodeName = productNodeNameWithPredicate.split('[')[0];
    console.log("Węzeł produktu:", productNodeNameWithPredicate, "→ tag:", pureProductNodeName);

    const entries = xmlDoc.getElementsByTagName(pureProductNodeName);
    if (entries.length === 0) {
        alert(`Nie znaleziono elementów <${pureProductNodeName}>.`);
        document.getElementById("productMapsPreview").textContent = "[]";
        return;
    }

    showProgress(`Ekstrakcja 0 / ${entries.length} produktów...`, 0);

    const onlyEan = document.getElementById("onlyEanProducts").checked;
    const removeParams = document.getElementById("cleanUrlParameters").checked;
    let productMaps = [];
    let countUrlsWithParams = 0;
    let idx = 0;
    const CHUNK = 100; // małe chunki = płynny pasek, brak zamrożenia UI

    function extractChunk() {
        const end = Math.min(idx + CHUNK, entries.length);
        for (let i = idx; i < end; i++) {
            const pm = {
                StoreId: storeId.toString(),
                ExternalId: getVal(entries[i], "ExternalId", productNodeNameWithPredicate),
                Url: getVal(entries[i], "Url", productNodeNameWithPredicate),
                GoogleEan: getVal(entries[i], "GoogleEan", productNodeNameWithPredicate),
                GoogleImage: getVal(entries[i], "GoogleImage", productNodeNameWithPredicate),
                GoogleExportedName: getVal(entries[i], "GoogleExportedName", productNodeNameWithPredicate),
                GoogleExportedProducer: getVal(entries[i], "GoogleExportedProducer", productNodeNameWithPredicate),
                GoogleExportedProducerCode: getVal(entries[i], "GoogleExportedProducerCode", productNodeNameWithPredicate),
                GoogleXMLPrice: parsePrice(getVal(entries[i], "GoogleXMLPrice", productNodeNameWithPredicate)),
                GoogleDeliveryXMLPrice: parsePrice(getVal(entries[i], "GoogleDeliveryXMLPrice", productNodeNameWithPredicate))
            };
            if (onlyEan && (!pm.GoogleEan || !pm.GoogleEan.trim())) continue;
            if (pm.Url) {
                const qIdx = pm.Url.indexOf('?');
                if (qIdx !== -1) { countUrlsWithParams++; if (removeParams) pm.Url = pm.Url.substring(0, qIdx); }
            }
            productMaps.push(pm);
        }
        idx = end;
        const pct = Math.round((idx / entries.length) * 100);
        updateProgress(`Ekstrakcja ${idx} / ${entries.length} produktów... (znaleziono: ${productMaps.length})`, pct);

        if (idx < entries.length) {
            // requestAnimationFrame zamiast setTimeout – przeglądarka odrysuje pasek przed następnym chunkiem
            requestAnimationFrame(() => setTimeout(extractChunk, 0));
        } else {
            finishExtraction(productMaps, countUrlsWithParams);
        }
    }

    // Dwa rAF na starcie żeby pasek zdążył się pojawić
    requestAnimationFrame(() => requestAnimationFrame(extractChunk));
}

function finishExtraction(productMaps, countUrlsWithParams) {
    hideProgress();
    document.getElementById("urlParamsInfo").textContent = "Liczba URL zawierających parametry: " + countUrlsWithParams;

    if (document.getElementById("removeDuplicateUrls").checked) {
        const seen = {};
        productMaps = productMaps.filter(pm => { if (!pm.Url) return true; if (seen[pm.Url]) return false; seen[pm.Url] = true; return true; });
    }
    if (document.getElementById("removeDuplicateEans").checked) {
        const seen = {};
        productMaps = productMaps.filter(pm => { if (!pm.GoogleEan) return true; if (seen[pm.GoogleEan]) return false; seen[pm.GoogleEan] = true; return true; });
    }
    if (document.getElementById("removeDuplicateProducerCodes").checked) {
        const seen = {};
        productMaps = productMaps.filter(pm => { if (!pm.GoogleExportedProducerCode) return true; if (seen[pm.GoogleExportedProducerCode]) return false; seen[pm.GoogleExportedProducerCode] = true; return true; });
    }

    document.getElementById("productMapsPreview").textContent = JSON.stringify(productMaps, null, 2);
    document.getElementById("finalNodesInfo").textContent = "Final nodes po filtrach: " + productMaps.length;
    checkDuplicates(productMaps);
}

function checkDuplicates(productMaps) {
    const urlC = {}, eanC = {}, codeC = {};
    productMaps.forEach(pm => {
        if (pm.Url) urlC[pm.Url] = (urlC[pm.Url] || 0) + 1;
        if (pm.GoogleEan) eanC[pm.GoogleEan] = (eanC[pm.GoogleEan] || 0) + 1;
        if (pm.GoogleExportedProducerCode) codeC[pm.GoogleExportedProducerCode] = (codeC[pm.GoogleExportedProducerCode] || 0) + 1;
    });
    const cnt = o => Object.keys(o).length;
    const dup = o => Object.values(o).filter(v => v > 1).length;
    const row = (label, o) => `${label}: ${cnt(o)} ` + (dup(o) > 0 ? `<span style="color:red;">(Duplikatów: ${dup(o)})</span>` : `(Brak duplikatów)`);
    document.getElementById("duplicatesInfo").innerHTML =
        row("Unikalnych URL", urlC) + "<br>" + row("Unikalnych EAN", eanC) + "<br>" + row("Unikalnych kodów producenta", codeC);
}

document.getElementById("extractProducts").addEventListener("click", extractProductsFromXml);
document.getElementById("cleanUrlParameters").addEventListener("change", extractProductsFromXml);
document.getElementById("removeDuplicateUrls").addEventListener("change", extractProductsFromXml);
document.getElementById("removeDuplicateEans").addEventListener("change", extractProductsFromXml);
document.getElementById("removeDuplicateProducerCodes").addEventListener("change", extractProductsFromXml);

// ─────────────────────────────────────────────────────────────
// getVal – oryginalna logika bez zmian
// ─────────────────────────────────────────────────────────────
const nsResolver = prefix => ({ 'g': 'http://base.google.com/ns/1.0' })[prefix] || null;

function getVal(entryNode, fieldName, productNodeName) {
    const info = mappingForField[fieldName];
    if (!info || !info.xpath) return null;
    const originalPath = info.xpath;
    const productNodeIdentifier = '/' + productNodeName;
    const lastIdx = originalPath.lastIndexOf(productNodeIdentifier);
    let relativePath;
    if (lastIdx !== -1) {
        relativePath = '.' + originalPath.substring(lastIdx + productNodeIdentifier.length);
    } else {
        const cleanIdentifier = '/' + productNodeName.split('[')[0];
        const lastIdxClean = originalPath.lastIndexOf(cleanIdentifier);
        if (lastIdxClean !== -1) relativePath = '.' + originalPath.substring(lastIdxClean + cleanIdentifier.length);
        else { console.error(`Nie można ustalić ścieżki dla "${originalPath}"`); return null; }
    }

    if (originalPath.endsWith('/#value')) {
        const elementPath = relativePath.slice(0, -7);
        let elementNode = null;
        try {
            const result = xmlDoc.evaluate(elementPath, entryNode, nsResolver, XPathResult.FIRST_ORDERED_NODE_TYPE, null);
            elementNode = result.singleNodeValue;
        } catch (e) { }
        if (!elementNode) {
            const tagName = elementPath.replace(/.*\//, '').replace(/.*:/, '');
            const candidates = entryNode.getElementsByTagName("*");
            for (let i = 0; i < candidates.length; i++) {
                if (candidates[i].localName === tagName) { elementNode = candidates[i]; break; }
            }
        }
        if (!elementNode) return null;
        let val = elementNode.textContent.trim();
        if (!val && elementNode.localName === 'link' && elementNode.hasAttribute('href')) val = elementNode.getAttribute('href');
        if (!val && elementNode.hasAttribute('src')) val = elementNode.getAttribute('src');
        return val;
    } else {
        try {
            const result = xmlDoc.evaluate(relativePath, entryNode, nsResolver, XPathResult.STRING_TYPE, null);
            return result.stringValue.trim();
        } catch (e) { return null; }
    }
}

// ─────────────────────────────────────────────────────────────
// ZAPIS PRODUCT MAPS
// ─────────────────────────────────────────────────────────────
document.getElementById("saveProductMapsInDb").addEventListener("click", function () {
    const txt = document.getElementById("productMapsPreview").textContent.trim();
    if (!txt) { alert("Brak productMaps do zapisania!"); return; }
    let productMaps;
    try { productMaps = JSON.parse(txt); } catch (e) { alert("Błędny JSON!"); return; }
    fetch("/GoogleImportWizardXml/SaveProductMapsFromFront", {
        method: "POST", headers: { "Content-Type": "application/json" }, body: JSON.stringify(productMaps)
    }).then(r => r.json()).then(d => alert("Zapisano: " + d.message)).catch(err => console.error(err));
});

// ─────────────────────────────────────────────────────────────
// INICJALIZACJA
// ─────────────────────────────────────────────────────────────
renderMappingTable();

const loadNetworkBtn = document.getElementById("loadFromNetworkBtn");
if (loadNetworkBtn) loadNetworkBtn.addEventListener("click", loadFromNetwork);